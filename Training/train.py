"""
어둠(Darkness) 내면 인격 파인튜닝 스크립트
────────────────────────────────────────────
환경: Python 3.10+, CUDA GPU (VRAM 8GB+)
권장: Google Colab (무료 T4 GPU) 또는 WSL2 + CUDA

설치:
    pip install unsloth trl datasets transformers

실행:
    python train.py
"""

import json
import os
import torch
from datasets import Dataset
from trl import SFTTrainer
from transformers import TrainingArguments
from unsloth import FastLanguageModel
from unsloth.chat_templates import get_chat_template

# ── 설정 ─────────────────────────────────────────────────────────

# VRAM 8GB  → Llama-3.2-3B-Instruct
# VRAM 16GB → Meta-Llama-3.1-8B-Instruct
MODEL_NAME      = "unsloth/Meta-Llama-3.1-8B-Instruct"
DATA_PATH       = "data/train_data.jsonl"
OUTPUT_LORA     = "output/darkness-lora"
OUTPUT_GGUF     = "output/darkness-gguf"

MAX_SEQ_LENGTH  = 2048
LORA_RANK       = 16
EPOCHS          = 4
BATCH_SIZE      = 2
GRAD_ACCUM      = 4
LEARNING_RATE   = 2e-4

# ── 모델 로드 ─────────────────────────────────────────────────────

print("모델 로드 중...")
model, tokenizer = FastLanguageModel.from_pretrained(
    model_name=MODEL_NAME,
    max_seq_length=MAX_SEQ_LENGTH,
    dtype=None,          # auto
    load_in_4bit=True,   # QLoRA: VRAM 절약
)

tokenizer = get_chat_template(tokenizer, chat_template="llama-3.1")

# ── LoRA 어댑터 ───────────────────────────────────────────────────

model = FastLanguageModel.get_peft_model(
    model,
    r=LORA_RANK,
    target_modules=[
        "q_proj", "k_proj", "v_proj", "o_proj",
        "gate_proj", "up_proj", "down_proj",
    ],
    lora_alpha=LORA_RANK,
    lora_dropout=0,
    bias="none",
    use_gradient_checkpointing="unsloth",
    random_state=42,
)

# ── 데이터 준비 ───────────────────────────────────────────────────

def load_jsonl(path):
    data = []
    with open(path, "r", encoding="utf-8") as f:
        for line in f:
            line = line.strip()
            if line:
                data.append(json.loads(line))
    return data

def format_sample(sample):
    text = tokenizer.apply_chat_template(
        sample["messages"],
        tokenize=False,
        add_generation_prompt=False,
    )
    return {"text": text}

print("데이터 로드 중...")
raw = load_jsonl(DATA_PATH)
dataset = Dataset.from_list(raw).map(format_sample)
print(f"학습 데이터: {len(dataset)}개")

# ── 학습 ─────────────────────────────────────────────────────────

trainer = SFTTrainer(
    model=model,
    tokenizer=tokenizer,
    train_dataset=dataset,
    dataset_text_field="text",
    max_seq_length=MAX_SEQ_LENGTH,
    dataset_num_proc=2,
    args=TrainingArguments(
        per_device_train_batch_size=BATCH_SIZE,
        gradient_accumulation_steps=GRAD_ACCUM,
        num_train_epochs=EPOCHS,
        learning_rate=LEARNING_RATE,
        fp16=not torch.cuda.is_bf16_supported(),
        bf16=torch.cuda.is_bf16_supported(),
        logging_steps=5,
        optim="adamw_8bit",
        weight_decay=0.01,
        lr_scheduler_type="cosine",
        warmup_ratio=0.05,
        output_dir=OUTPUT_LORA,
        save_strategy="epoch",
        seed=42,
    ),
)

print("학습 시작...")
trainer.train()
print("학습 완료!")

# ── GGUF 변환 (Ollama 용) ─────────────────────────────────────────

print("GGUF 변환 중... (Q4_K_M 양자화)")
os.makedirs(OUTPUT_GGUF, exist_ok=True)

model.save_pretrained_gguf(
    OUTPUT_GGUF,
    tokenizer,
    quantization_method="q4_k_m",   # 품질/크기 균형. 더 작게: q2_k
)

gguf_path = os.path.join(OUTPUT_GGUF, "unsloth.Q4_K_M.gguf")
print(f"\n✅ 완료!")
print(f"   GGUF 파일: {os.path.abspath(gguf_path)}")
print(f"\n다음 단계:")
print(f"   1. Modelfile의 FROM 경로를 위 GGUF 경로로 수정")
print(f"   2. ollama create inner-voice -f Modelfile")
print(f"   3. LLMUnity에서 모델을 'inner-voice'로 변경")
