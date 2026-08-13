# AINPC — 내면의 대화 액션

> 플레이어가 캐릭터를 조종하지만, 캐릭터의 **「내면」이 조작권을 빼앗아 가는** 2D 탑뷰 액션 RPG.
> NPC 대화와 내면의 협상을 **기기에서 직접 도는 로컬 LLM**으로 생성한다. 외부 API 호출은 0회.

한신대학교 2026-1학기 PD학기제 과제 · 개발 2인 (김정현, 이정택) · 16주

---

## 핵심 아이디어

대부분의 게임에서 페널티는 **체력 감소**다. 이 게임의 페널티는 **조작권 상실**이다.

캐릭터에게는 기분(Mood) 게이지가 있고, 전투 중 이 값이 바닥나면 「어둠(Darkness)」이라는 또 다른 인격이 표면으로 올라와 몸을 가져간다. 그동안 플레이어의 입력은 먹히지 않고 AI가 알아서 싸운다. 플레이어는 「내면의 공간」에서 어둠과 대화로 협상해 조작권을 되찾아야 한다.

```
Mood ≥ 70  ─────▶  플레이어가 조종 (조작권 반환)
Mood ≤ 20  ─────▶  AI가 조종   (조작권 강탈)
              └─ 단, 전투 중일 때만 강탈된다

[대화] ──mood_delta──▶ [기분 수치] ──조건 판정──▶ [조작권] ──▶ [대화]
```

LLM의 대화 결과가 게임 수치를 바꾸고, 그 수치가 다시 조작권을 결정하는 **순환 구조**가 이 프로젝트의 핵심이다. AI는 강하지만 몸을 사리지 않아 스스로 피해를 입기 때문에, "AI에게 몸을 맡기면 적은 잘 잡지만 대신 몸이 상한다"는 트레이드오프가 성립한다.

---

## 실행 방법

### 요구 사항

| 항목 | 버전 |
|---|---|
| Unity | 6000.3.10f1 |
| 언어 | C# |
| LLM 런타임 | LLMUnity (임베디드 llama.cpp) |
| 권장 사양 | GGUF 8B 모델 로드가 가능한 메모리 (16GB 이상 권장) |

### 모델 파일 배치

**GGUF 모델은 용량 문제로 저장소에 포함되어 있지 않다** (`.gitignore`에서 `*.gguf` 제외). 아래 경로에 직접 넣어야 게임이 동작한다.

```
Assets/StreamingAssets/Models/darkness-Q4_K_M.gguf   # 약 4.9GB, 「어둠」 페르소나 파인튜닝 모델
```

`darkness-Q4_K_M.gguf`는 [`Training/`](Training/) 의 파이프라인으로 직접 만들 수 있다 (아래 참조). 일반 NPC 대화용으로 베이스 모델(`Meta-Llama-3-8B-Instruct-Q4_K_M.gguf`)을 함께 쓸 수 있다.

### 실행

1. Unity Hub에서 프로젝트를 연다 (6000.3.10f1).
2. 위 경로에 GGUF 파일을 넣는다.
3. 메인 씬을 열고 Play.

| 조작 | 동작 |
|---|---|
| `WASD` | 이동 |
| 좌클릭 | 일반 공격 |
| 우클릭 (길게) | 충전 대시 공격 — 누른 시간에 비례해 사거리·피해 증가, 대시 중 무적 |
| NPC 근접 | 대화창 자동 활성화 |

---

## 아키텍처

직접 작성한 코드는 [`Assets/Script/`](Assets/Script/) 아래 26개 파일이다.
`Assets/Runtime/`, `Assets/Editor/LLM*` 는 [LLMUnity(undreamai)](https://github.com/undreamai/LLMUnity) 오픈소스 패키지다.

### 조작권 전환 — 판단 · 중계 · 실행의 분리

조작권 시스템을 하나의 스크립트로 만들지 않고 역할별로 쪼갰다. 판단이 바뀌어도 실행부를 건드릴 필요가 없다.

| 파일 | 역할 |
|---|---|
| [`System/MoodSystem.cs`](Assets/Script/System/MoodSystem.cs) | 기분 수치(0~100, 시작 50) 관리 + 강탈/반환 **조건 판정**. `CombatWatch` 코루틴이 0.5초 주기로 반경 12 내 적을 검사 |
| [`System/ControlManager.cs`](Assets/Script/System/ControlManager.cs) | 이벤트를 받아 `IsPlayerControlled` 플래그를 갱신하고 **재발행**. 입력·AI·연출이 서로를 모르게 하는 느슨한 결합 지점 |
| [`System/AIController.cs`](Assets/Script/System/AIController.cs) | 강탈 시 가장 가까운 적을 추격·공격. 거리에 따라 근접 / 대시 / 걷기로 분기 |
| [`System/TakeoverEffect.cs`](Assets/Script/System/TakeoverEffect.cs) | 적색 플래시 · 카메라 흔들림 · 프리셋 대사 · 효과음 |

**히스테리시스** — 강탈(20)과 반환(70)의 임계값을 크게 벌려놨다. 같은 값이면 게이지가 경계에서 흔들릴 때 조작권이 초당 몇 번씩 오가며 게임이 망가진다.

**전투 게이트** — 한적한 곳에서 게이지가 낮다고 조작권을 뺏으면 그냥 불편하기만 하다. 적이 주변에 있을 때만 강탈되고, 전투가 끝나면 기분이 낮아도 즉시 반환된다. 플레이어에게 항상 탈출 경로가 있어야 한다.

### LLM 대화

| 파일 | 역할 |
|---|---|
| [`System/InnerVoiceManager.cs`](Assets/Script/System/InnerVoiceManager.cs) | **내면의 공간** — `Time.timeScale = 0`으로 게임을 정지시키고 어둠과 협상. 결과가 `mood_delta`만큼 기분을 움직인다 |
| [`DialogueManager.cs`](Assets/Script/DialogueManager.cs) | **마을 NPC 대화** — NPC별 페르소나 부여. 어둠이 몸을 제어 중이면 NPC가 대화를 거부하고 공포 반응을 보이며, 그 사건을 기억에 남긴다 |
| [`System/MemoryManager.cs`](Assets/Script/System/MemoryManager.cs) · [`Npc/MrSmithLongTermMemory.cs`](Assets/Script/Npc/MrSmithLongTermMemory.cs) | **RAG 장기 기억** — 임베딩 기반 벡터 검색(usearch), NPC별 독립 기억 파일 |

**구조화된 출력** — 시스템 프롬프트로 항상 JSON 응답을 강제해, 한 번의 추론으로 대사와 감정 변화량을 동시에 받는다.

```json
{"dialogue": "...무슨 말을 하든, 네 손은 이미 떨리고 있잖아.", "mood_delta": -8}
```

파싱은 2단계 방어다 — `JsonUtility` 우선, 실패하면 정규식 폴백. 로컬 소형 모델은 형식을 종종 깨뜨리기 때문에 파싱 실패로 게임이 멈추면 안 된다.

**소프트락 방지** — LLM을 게임에 붙일 때 진짜 문제는 품질이 아니라 지연이다. 양쪽 대화 모두 `Task.WhenAny`로 20초 타임아웃을 걸어, 응답이 오지 않으면 "침묵 속에 종료"시킨다. 응답은 토큰 단위 스트리밍으로 받아 **미완성 JSON에서 `dialogue` 값만 뽑아** 타자기 효과로 출력하므로, 완성을 기다리지 않는다.

**상황 인지** — 현재 HP · 기분 수치 · 주변 적 수 · 조작 주체를 시스템 프롬프트에 주입한다.

### 데이터 주도형 설계

몬스터 능력치는 코드가 아니라 [`Monster/MonsterData.cs`](Assets/Script/Monster/MonsterData.cs) (`ScriptableObject`)에 있다. 공통 행동은 추상 클래스 [`MonsterBase.cs`](Assets/Script/Monster/MonsterBase.cs)에 두고 [고블린](Assets/Script/Monster/GoblinMonster.cs) · [버섯](Assets/Script/Monster/MushroomMonster.cs) · [해골](Assets/Script/Monster/SkeletonMonster.cs) · [박쥐](Assets/Script/Monster/BatMonster.cs) 4종이 상속한다. 새 몬스터는 코드 수정 없이 데이터 에셋 교체만으로 추가된다.

### 게임 수학

조작감·판정·성장 곡선은 감이 아니라 명시적 수식에 근거해 설계했다.

- **벡터 정규화** — 대각선 입력 `(1,1)`은 크기가 `√2 ≈ 1.414`라, 보정 없이는 대각 이동이 1.4배 빠르다. `v̂ = v / |v|`로 정규화 후 속도를 곱해 전 방향 속도를 고정
- **선형보간 / 역보간** — 대시 충전 비율 `r`로 사거리 `lerp(1, 7, r)`, 피해 `lerp(5, 25, r)`. AI가 대시할 때는 반대로 적까지의 거리에서 `InverseLerp`로 충전량을 역산
- **삼각함수** — `θ = SignedAngle(↑, dir)`만큼 공격 판정 박스를 회전
- **기하 충돌 질의** — 전투 감지는 `OverlapCircle`, 근접 공격은 회전된 `OverlapBox`, 대시는 경로를 쓸어 검사하는 `BoxCast`
- **등비수열** — 다음 레벨 요구 경험치 `Eₙ = 100 · 1.5ⁿ⁻¹`, 최대 HP/마나는 `+20`/`+10` 등차
- **운동학 적분 · 이징** — `FixedUpdate`에서 `p ← p + v̂ · s · Δt`로 적분해 프레임률 독립성 확보, 화면 전환에는 SmoothStep `s(t) = 3t² − 2t³`

---

## 「어둠」 인격 파인튜닝

프롬프트만으로는 톤과 출력 형식이 흔들려, 전용 모델을 직접 학습시켰다. 전체 파이프라인은 [`Training/`](Training/) 에 있다.

```
gen_data.py  ─▶  train.py (QLoRA)  ─▶  어댑터 병합  ─▶  GGUF(Q4_K_M)  ─▶  LLMUnity 인프로세스 로드
  약 1,000개        Colab T4             llama.cpp        약 4.9GB          외부 API 0회
```

| 파일 | 역할 |
|---|---|
| [`Training/gen_data.py`](Training/gen_data.py) | 규칙 기반 학습 데이터 생성기 |
| [`Training/train.py`](Training/train.py) | QLoRA 학습 (Unsloth + TRL `SFTTrainer`) |
| [`Training/AINPC_finetune_colab.ipynb`](Training/AINPC_finetune_colab.ipynb) | Colab 실행용 노트북 |
| [`Training/Modelfile`](Training/Modelfile) | 시스템 프롬프트 및 추론 파라미터 |
| [`Training/data/train_data.jsonl`](Training/data/train_data.jsonl) | 생성된 학습 데이터 |

**데이터 생성** — HP · 기분 · 적 수 · 조작 주체를 무작위 조합해 멀티턴 샘플 약 1,000개를 자동 생성한다. 플레이어 발화를 항복 · 반항 · 달램 · 거래 · 모욕 · 협력 등 12개 카테고리로 나누고 `(발화 ↔ 반응 ↔ mood_delta 범위)`를 짝지어 감정의 방향성을 일관화했다. 시드를 42로 고정해 재현성을 확보하고, **학습 데이터의 시스템 프롬프트를 런타임과 똑같은 형식**으로 맞춰 학습-추론 분포 불일치를 줄였다.

**학습 설정**

| 항목 | 값 |
|---|---|
| 베이스 모델 | `unsloth/Meta-Llama-3.1-8B-Instruct` (4bit 로드) |
| 기법 | QLoRA — 저랭크 어댑터만 학습 |
| LoRA rank / alpha | 16 / 16 |
| Epochs | 4 |
| Learning rate | 2e-4 (cosine) |
| 유효 배치 | 8 |
| Max sequence length | 2048 |
| 환경 | Google Colab T4 (무료 GPU) |

**배포** — 어댑터를 병합해 `Q4_K_M`으로 양자화하고 LLMUnity의 LLM 컴포넌트에 직접 로드한다. temperature 등 추론 파라미터를 고정해 캐릭터 톤이 흔들리지 않게 했다.

```bash
cd Training
pip install -r requirements.txt
python gen_data.py     # 학습 데이터 생성
python train.py        # QLoRA 학습
```

---

## 왜 로컬 LLM인가

처음에는 구현이 쉬운 외부 API 연동을 전제로 기획했지만, 세 가지가 걸려 방향을 틀었다.

1. **비용** — 플레이어 1인당 추론 비용이 계속 발생한다. 학생 개발 환경에서는 치명적이다.
2. **지연** — 네트워크 왕복이 그대로 몰입 저하로 이어진다.
3. **배포·보안** — 오프라인 실행이 불가능하고, 플레이어의 입력이 외부 서버로 나간다.

로컬 LLM은 초기 구성 난도가 훨씬 높지만, 판매 이후 운영비가 0에 수렴하고 대화 데이터가 기기 밖으로 나가지 않는다.

---

## 프로젝트 구조

```
Assets/Script/
├── System/          MoodSystem · ControlManager · AIController · TakeoverEffect
│                    InnerVoiceManager · MemoryManager
├── Player/          PlayerAttack · PlayerController · PlayerStats
├── Monster/         MonsterBase · MonsterData · MonsterSpawnArea/Point
│                    MonsterProjectile · Bat/Goblin/Mushroom/Skeleton
├── Npc/             MrSmithLongTermMemory · NpcInteraction
├── Save/            SaveSystem · PlayerSaveData
├── UI/              PlayerHUD
└── (루트)            DialogueManager · IDamageable · CameraFollow

Training/            파인튜닝 파이프라인
docs/                설계 문서
```

---

## 알려진 한계

- **로컬 추론 속도** — 8B 모델의 로컬 추론에는 지연이 있다. 타임아웃으로 소프트락은 막았지만 근본 속도는 모델 경량화와 하드웨어에 달려 있다.
- **`timeScale = 0`과 코루틴** — 내면대화 중 강탈이 발동하면 코루틴이 정지해, AI 행동이 대화 종료 후로 밀린다.
- **미구현** — 인벤토리 시스템, 오브젝트 풀링 최적화.
- **배포 용량** — 양자화 모델이 약 4.9GB라 패키징 전략(빌드 포함 vs 최초 실행 시 다운로드)이 남아 있다.

---

## 라이선스 및 크레딧

- 베이스 모델: **Meta Llama 3.1** — Llama 3.1 Community License 적용. 상업적 이용 시 **"Built with Llama"** 표기와 라이선스 동봉 등 조건을 준수해야 한다.
- LLM 런타임: [LLMUnity](https://github.com/undreamai/LLMUnity) (undreamai)
