# -*- coding: utf-8 -*-
"""Colab 학습 노트북(.ipynb) 생성기.  실행: python make_colab_notebook.py"""
import json, os

cells = []
def md(text):  cells.append({"cell_type":"markdown","metadata":{},"source":text})
def code(src): cells.append({"cell_type":"code","metadata":{},"execution_count":None,"outputs":[],"source":src})

md("""# AINPC — '어둠(Darkness)' 인격 파인튜닝 (Colab)

**시작 전 필수:** 상단 메뉴 → `런타임` → `런타임 유형 변경` → 하드웨어 가속기 **T4 GPU** 선택.

아래 셀을 위에서부터 순서대로 실행(Shift+Enter)하면 됩니다.
1. GPU 확인 → 2. 설치 → 3. 데이터 업로드 → 4. 모델 로드 → 5. 학습 → 6. GGUF 변환 → 7. 다운로드

마지막에 받은 `.gguf`를 Unity의 LLM 컴포넌트 **Load model**에 넣으면 끝.""")

md("## 1) GPU 확인 (T4가 보여야 정상)")
code("!nvidia-smi")

md("## 2) 설치 (2~3분 소요)")
code("""%%capture
!pip install unsloth
!pip install --upgrade --no-cache-dir "unsloth[colab-new] @ git+https://github.com/unslothai/unsloth.git"
!pip install --upgrade trl datasets transformers""")

md("""## 3) 학습 데이터 업로드
실행하면 파일 선택창이 뜹니다. PC의 `Training/data/train_data.jsonl` 을 선택하세요.""")
code("""from google.colab import files
import os
print('train_data.jsonl 파일을 선택하세요 ↓')
up = files.upload()
os.makedirs('data', exist_ok=True)
fn = list(up.keys())[0]
with open('data/train_data.jsonl', 'wb') as f:
    f.write(up[fn])
n = sum(1 for _ in open('data/train_data.jsonl', encoding='utf-8'))
print(f'저장됨 -> data/train_data.jsonl  (대화 {n}줄)')""")

md("""## 4) 모델 로드 (QLoRA 4bit)
> 게임이 Llama 3(원조)라 정확히 맞추고 싶으면 `MODEL_NAME`을
> `"unsloth/llama-3-8b-Instruct-bnb-4bit"` 로 바꾸고 chat_template도 `"llama-3"`로 바꾸세요.
> 그대로 둬도(3.1) 결과물이 게임 모델을 대체하므로 문제없습니다.""")
code("""import torch
from unsloth import FastLanguageModel
from unsloth.chat_templates import get_chat_template

MODEL_NAME     = "unsloth/Meta-Llama-3.1-8B-Instruct"
MAX_SEQ_LENGTH = 2048
LORA_RANK      = 16

model, tokenizer = FastLanguageModel.from_pretrained(
    model_name     = MODEL_NAME,
    max_seq_length = MAX_SEQ_LENGTH,
    dtype          = None,
    load_in_4bit   = True,
)
tokenizer = get_chat_template(tokenizer, chat_template="llama-3.1")

model = FastLanguageModel.get_peft_model(
    model, r=LORA_RANK,
    target_modules=["q_proj","k_proj","v_proj","o_proj","gate_proj","up_proj","down_proj"],
    lora_alpha=LORA_RANK, lora_dropout=0, bias="none",
    use_gradient_checkpointing="unsloth", random_state=42,
)
print('모델 준비 완료')""")

md("## 5) 데이터셋 구성")
code("""import json
from datasets import Dataset

def load_jsonl(path):
    rows=[]
    with open(path, encoding='utf-8') as f:
        for line in f:
            line=line.strip()
            if line: rows.append(json.loads(line))
    return rows

def format_sample(s):
    return {"text": tokenizer.apply_chat_template(
        s["messages"], tokenize=False, add_generation_prompt=False)}

dataset = Dataset.from_list(load_jsonl('data/train_data.jsonl')).map(format_sample)
print('학습 샘플:', len(dataset))
print('--- 예시 ---')
print(dataset[0]['text'][:600])""")

md("""## 6) 학습 (T4 기준 8B·1000대화 약 20~40분)
> 최신 `trl`은 `SFTConfig`를 사용합니다. `dataset_text_field`·`max_seq_length`도 그 안에 넣습니다.
> 데이터가 1000개로 커졌으니 `num_train_epochs`는 2~3을 권장.""")
code("""import torch
from trl import SFTTrainer, SFTConfig

trainer = SFTTrainer(
    model         = model,
    tokenizer     = tokenizer,
    train_dataset = dataset,
    args = SFTConfig(
        dataset_text_field          = "text",
        max_seq_length              = 2048,
        dataset_num_proc            = 2,
        packing                     = False,
        padding_free                = False,
        per_device_train_batch_size = 2,
        gradient_accumulation_steps = 4,
        num_train_epochs            = 3,
        learning_rate               = 2e-4,
        fp16 = not torch.cuda.is_bf16_supported(),
        bf16 = torch.cuda.is_bf16_supported(),
        logging_steps   = 10,
        optim           = "adamw_8bit",
        weight_decay    = 0.01,
        lr_scheduler_type = "cosine",
        warmup_steps    = 10,
        output_dir      = "output/darkness-lora",
        save_strategy   = "epoch",
        seed            = 42,
        report_to       = "none",
    ),
)
trainer.train()
print('학습 완료')""")

md("## 7) (선택) 빠른 테스트 — 어둠이 JSON으로 답하는지 확인")
code("""FastLanguageModel.for_inference(model)
msgs = [
    {"role":"system","content":"너는 이 캐릭터의 또 다른 인격 '어둠'이야. 반드시 {\\\"dialogue\\\":\\\"\\\",\\\"mood_delta\\\":0} JSON으로만 답해. 현재 상황: HP 10/100, 기분 20/100, 주변 적 5명, 현재 플레이어가 몸을 제어 중."},
    {"role":"user","content":"대화를 시작해."},
]
inputs = tokenizer.apply_chat_template(msgs, tokenize=True, add_generation_prompt=True, return_tensors="pt").to("cuda")
out = model.generate(input_ids=inputs, max_new_tokens=80, temperature=0.85, do_sample=True)
print(tokenizer.decode(out[0][inputs.shape[1]:], skip_special_tokens=True))""")

md("## 8) GGUF 변환 (Q4_K_M) — Unity 로 가져갈 파일")
code("""import os
os.makedirs("output/darkness-gguf", exist_ok=True)
model.save_pretrained_gguf("output/darkness-gguf", tokenizer, quantization_method="q4_k_m")
import glob
path = glob.glob("output/darkness-gguf/*.gguf")[0]
print('GGUF 생성:', path, round(os.path.getsize(path)/1e9,2), 'GB')""")

md("## 9) GGUF 다운로드 → Unity LLM 컴포넌트 'Load model'에 지정")
code("""from google.colab import files
import glob
files.download(glob.glob("output/darkness-gguf/*.gguf")[0])""")

nb = {
    "cells": cells,
    "metadata": {
        "accelerator": "GPU",
        "colab": {"provenance": [], "gpuType": "T4"},
        "kernelspec": {"name": "python3", "display_name": "Python 3"},
        "language_info": {"name": "python"},
    },
    "nbformat": 4, "nbformat_minor": 0,
}

here = os.path.dirname(os.path.abspath(__file__))
out = os.path.join(here, "AINPC_finetune_colab.ipynb")
with open(out, "w", encoding="utf-8") as f:
    json.dump(nb, f, ensure_ascii=False, indent=1)
print("notebook written:", out)
print("cells:", len(cells))
