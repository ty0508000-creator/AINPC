# -*- coding: utf-8 -*-
"""
로컬(Windows + NVIDIA GPU) 대화 모델 파인튜닝: QLoRA 학습 → LoRA 병합 → GGUF(Q4_K_M) 변환
──────────────────────────────────────────────────────────────
- Colab 용 train.py / 노트북과 따로 둔다. 윈도우에서 unsloth 없이 transformers + peft 만 쓴다.
- 대화 형식은 각 모델 토크나이저의 chat template 을 그대로 쓴다. GGUF 에도 같은 template 이 들어가므로
  게임(LlamaLib)이 만드는 프롬프트와 학습 형식이 어긋나지 않는다.
- 손실은 어시스턴트 응답({"dialogue":…,"mood_delta":…})에만 건다. 시스템 프롬프트를 외우게 하지 않는다.

설치(가상환경):
    pip install torch --index-url https://download.pytorch.org/whl/cu128
    pip install transformers peft datasets accelerate bitsandbytes gguf sentencepiece protobuf safetensors
실행 예:
    python train_local.py --model <베이스 HF 폴더> --name AINPC-Qwen3-4B --out <출력 폴더> \
        --llama-cpp <llama.cpp 소스 폴더> --quantize <llama-quantize.exe>
"""

import argparse
import gc
import json
import os
import subprocess
import sys

import torch
import torch.nn.functional as F
from peft import LoraConfig, PeftModel, get_peft_model, prepare_model_for_kbit_training
from transformers import (AutoModelForCausalLM, AutoTokenizer, BitsAndBytesConfig, Trainer,
                          TrainingArguments)

HERE = os.path.dirname(os.path.abspath(__file__))


def build_examples(tokenizer, path, max_len):
    """(system, user, assistant) 한 턴 샘플을 토큰으로 바꾸고, 응답 앞부분의 라벨은 -100 으로 가린다."""
    examples = []
    with open(path, encoding="utf-8") as f:
        for line in f:
            if not line.strip():
                continue
            messages = json.loads(line)["messages"]
            prompt = tokenizer.apply_chat_template(messages[:-1], tokenize=False, add_generation_prompt=True)
            full = tokenizer.apply_chat_template(messages, tokenize=False)
            # 응답만 학습하려면 전체 문자열이 프롬프트로 시작해야 한다(템플릿이 앞부분을 바꾸면 여기서 멈춘다)
            assert full.startswith(prompt), "chat template 이 생성 프롬프트와 전체 대화를 다르게 만든다"
            prompt_ids = tokenizer(prompt, add_special_tokens=False)["input_ids"]
            full_ids = tokenizer(full, add_special_tokens=False)["input_ids"]
            assert full_ids[:len(prompt_ids)] == prompt_ids, "프롬프트 경계에서 토큰이 갈라진다"
            assert len(full_ids) <= max_len, f"샘플이 {len(full_ids)}토큰이라 --max-len {max_len} 을 넘는다"
            labels = [-100] * len(prompt_ids) + full_ids[len(prompt_ids):]
            assert any(t != -100 for t in labels), "학습할 응답 토큰이 없다"
            examples.append({"input_ids": full_ids, "labels": labels})
    return examples


def collate(pad_id):
    # 왼쪽을 채운다. 응답이 모든 샘플에서 끝에 오므로 끝 몇 위치의 로짓만 계산하면 된다(ResponseOnlyTrainer)
    def fn(batch):
        width = max(len(b["input_ids"]) for b in batch)
        ids = torch.full((len(batch), width), pad_id, dtype=torch.long)
        labels = torch.full((len(batch), width), -100, dtype=torch.long)
        mask = torch.zeros((len(batch), width), dtype=torch.long)
        for i, b in enumerate(batch):
            n = len(b["input_ids"])
            ids[i, width - n:] = torch.tensor(b["input_ids"])
            labels[i, width - n:] = torch.tensor(b["labels"])
            mask[i, width - n:] = 1
        return {"input_ids": ids, "labels": labels, "attention_mask": mask}
    return fn


def response_loss(logits, labels, num_items=None):
    """logits 는 끝 k 위치의 로짓. 위치 t 의 로짓이 t+1 토큰을 맞히므로 끝 k-1 개 라벨과 짝짓는다.
    num_items 를 주면(누적 단계 전체의 라벨 토큰 수) 합을 그 수로 나눠, 누적해도 평균 손실이 되게 한다."""
    k = logits.size(1)
    target = labels[:, -(k - 1):]
    flat = logits[:, :-1].float().reshape(-1, logits.size(-1)), target.reshape(-1)
    if num_items is None:
        return F.cross_entropy(*flat, ignore_index=-100)
    return F.cross_entropy(*flat, ignore_index=-100, reduction="sum") / num_items


class ResponseOnlyTrainer(Trainer):
    """전체 길이 × 전체 어휘의 로짓을 만들지 않고 응답 부분만 계산한다.
    Qwen3 는 어휘가 15만 개라 프롬프트 1000토큰 전체의 로짓만으로 GPU 메모리가 넘친다. 손실 값은 기본 방식과 같다."""

    def compute_loss(self, model, inputs, return_outputs=False, num_items_in_batch=None):
        labels = inputs.pop("labels")
        keep = int((labels != -100).sum(dim=1).max()) + 1
        outputs = model(**inputs, logits_to_keep=keep)
        # Trainer 는 num_items_in_batch 를 넘길 때 누적 단계 수로 나누지 않는다. 여기서 나눠야 평균이 된다
        loss = response_loss(outputs.logits, labels, num_items_in_batch)
        return (loss, outputs) if return_outputs else loss


def _check_response_loss():
    # 끝 위치만 쓴 손실이 전체 로짓으로 낸 표준 손실과 같은지 작은 값으로 확인한다
    torch.manual_seed(0)
    full = torch.randn(2, 7, 5)
    labels = torch.tensor([[-100, -100, -100, -100, -100, 3, 1],
                           [-100, -100, -100, -100, 2, 4, 0]])
    standard = F.cross_entropy(full[:, :-1].reshape(-1, 5), labels[:, 1:].reshape(-1), ignore_index=-100)
    keep = int((labels != -100).sum(dim=1).max()) + 1
    assert torch.allclose(response_loss(full[:, -keep:], labels), standard)
    # 누적 단계 전체의 라벨 수로 나눈 합도 같은 평균이 되어야 한다
    assert torch.allclose(response_loss(full[:, -keep:], labels, (labels[:, 1:] != -100).sum()), standard)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--model", required=True, help="베이스 모델(HF 폴더 또는 저장소 이름)")
    ap.add_argument("--name", required=True, help="출력 모델 이름(GGUF 파일 이름과 general.name)")
    ap.add_argument("--out", required=True, help="어댑터·병합 모델·GGUF 를 둘 폴더")
    ap.add_argument("--data", default=os.path.join(HERE, "data", "train_data.jsonl"))
    ap.add_argument("--epochs", type=float, default=2)
    ap.add_argument("--rank", type=int, default=16)
    ap.add_argument("--lr", type=float, default=2e-4)
    ap.add_argument("--batch", type=int, default=2)
    ap.add_argument("--accum", type=int, default=4)
    ap.add_argument("--max-len", type=int, default=2048)
    ap.add_argument("--llama-cpp", help="convert_hf_to_gguf.py 가 있는 llama.cpp 소스 폴더. 비우면 GGUF 변환을 건너뛴다")
    ap.add_argument("--quantize", help="llama-quantize.exe 경로")
    args = ap.parse_args()
    _check_response_loss()

    adapter_dir = os.path.join(args.out, "lora")
    merged_dir = os.path.join(args.out, "merged")
    tokenizer = AutoTokenizer.from_pretrained(args.model)
    if tokenizer.pad_token is None:
        tokenizer.pad_token = tokenizer.eos_token

    # 병합·변환에서 실패했을 때 다시 실행하면 학습은 건너뛴다
    if os.path.isfile(os.path.join(adapter_dir, "adapter_model.safetensors")):
        print(f"학습된 어댑터가 있어 학습을 건너뛴다: {adapter_dir}")
    else:
        train(args, tokenizer, adapter_dir)

    # ── 병합 (bf16, CPU) ─────────────────────────────────────
    # device_map 을 주면 accelerate 가 여유 메모리를 낮게 잡아 일부를 meta 로 남기고, 저장할 때 실패한다
    base = AutoModelForCausalLM.from_pretrained(args.model, dtype=torch.bfloat16)
    merged = PeftModel.from_pretrained(base, adapter_dir).merge_and_unload()
    merged.save_pretrained(merged_dir, safe_serialization=True)
    tokenizer.save_pretrained(merged_dir)
    del base, merged
    gc.collect()
    print(f"병합 모델: {merged_dir}")

    # ── GGUF 변환·양자화 ─────────────────────────────────────
    if not args.llama_cpp:
        return
    f16 = os.path.join(args.out, f"{args.name}-F16.gguf")
    q4 = os.path.join(args.out, f"{args.name}-Q4_K_M.gguf")
    subprocess.run([sys.executable, os.path.join(args.llama_cpp, "convert_hf_to_gguf.py"), merged_dir,
                    "--outfile", f16, "--outtype", "f16", "--model-name", args.name], check=True)
    # 윈도우는 슬래시가 섞인 상대 경로 실행 파일을 찾지 못하므로 절대 경로로 바꾼다
    subprocess.run([os.path.abspath(args.quantize), f16, q4, "Q4_K_M"], check=True)
    os.remove(f16)   # 중간 산출물(약 2배 크기)은 지운다. 병합 모델 폴더에서 다시 만들 수 있다
    print(f"GGUF: {q4}")


def train(args, tokenizer, adapter_dir):
    examples = build_examples(tokenizer, args.data, args.max_len)
    print(f"학습 샘플 {len(examples)}개, 가장 긴 샘플 {max(len(e['input_ids']) for e in examples)}토큰")

    # ── 학습 (QLoRA 4bit) ───────────────────────────────────
    model = AutoModelForCausalLM.from_pretrained(
        args.model, device_map={"": 0}, dtype=torch.bfloat16,
        quantization_config=BitsAndBytesConfig(load_in_4bit=True, bnb_4bit_quant_type="nf4",
                                               bnb_4bit_use_double_quant=True, bnb_4bit_compute_dtype=torch.bfloat16))
    model = prepare_model_for_kbit_training(model, use_gradient_checkpointing=True,
                                            gradient_checkpointing_kwargs={"use_reentrant": False})
    model = get_peft_model(model, LoraConfig(
        r=args.rank, lora_alpha=args.rank, lora_dropout=0.0, bias="none", task_type="CAUSAL_LM",
        target_modules=["q_proj", "k_proj", "v_proj", "o_proj", "gate_proj", "up_proj", "down_proj"]))
    model.print_trainable_parameters()

    checkpoints = os.path.join(args.out, "checkpoints")
    trainer = ResponseOnlyTrainer(
        model=model,
        train_dataset=examples,
        data_collator=collate(tokenizer.pad_token_id),
        args=TrainingArguments(
            output_dir=checkpoints, per_device_train_batch_size=args.batch,
            gradient_accumulation_steps=args.accum, num_train_epochs=args.epochs, learning_rate=args.lr,
            lr_scheduler_type="cosine", warmup_ratio=0.05, weight_decay=0.01, bf16=True,
            optim="paged_adamw_8bit", logging_steps=10, save_strategy="steps", save_steps=100, save_total_limit=1,
            report_to="none", group_by_length=True, remove_unused_columns=False, seed=42, dataloader_num_workers=0))
    # 중간에 끊겨도 같은 명령을 다시 실행하면 마지막 저장 지점부터 이어서 학습한다
    resume = os.path.isdir(checkpoints) and any(d.startswith("checkpoint-") for d in os.listdir(checkpoints))
    trainer.train(resume_from_checkpoint=resume or None)
    model.save_pretrained(adapter_dir)
    del trainer, model
    gc.collect()
    torch.cuda.empty_cache()


if __name__ == "__main__":
    main()
