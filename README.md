# AINPC — 귀환자의 수련록

천마를 쓰러뜨린 뒤 고향과 동료를 잃은 검객이 수십 년 후 다시 강호로 돌아오는 **2D 탑뷰 무협 액션 RPG 프로토타입**입니다. 플레이어와 내면의 존재가 몸의 조작권을 공유하고, 로컬 LLM 대화가 Mood와 조작권에 영향을 줍니다.

한신대학교 PD학기제 프로젝트 · 개발 김정현, 이정택.

> 상태 기준: 2026-09-20. 사천왕의 네 섬과 천마의 마지막 섬은 전체 이야기의 목표이며, 다섯 섬이 완성된 게임은 아닙니다. 우선 목표는 첫 섬에서 전투·성장·대화·선택·보스를 연결하는 짧은 데모입니다.

## 문서

- [비판점·개선안·담당별 실행계획](docs/DEVELOPMENT_REVIEW_AND_PLAN.md)
- [게임 기획서·단계별 제작계획·선택지](docs/GAME_DESIGN_AND_EXECUTION_PLAN.md)
- [1섬 제작 기획서·퀘스트표·보스 목표](docs/FIRST_ISLAND_PRODUCTION_PLAN.md)
- [이번 프로젝트 목표·마일스톤·우선순위](docs/GOALS_AND_MILESTONES.md)
- [게임 시작 프롤로그 시네마틱 기획](docs/PROLOGUE_CINEMATIC_PLAN.md)
- [RPG UI 사용법·검증](docs/RPG_UI.md)
- [Main 씬 수동 플레이 검사 기록지](docs/MANUAL_PLAYTEST.md)
- [사망 복구·통합 저장 사용법](docs/RECOVERY_AND_SAVE.md)
- [자료구조·알고리즘 코드 설명](docs/RPG_CODE_EXPLAINED.md)
- [기존 2학기 계획 — 과거 분석과 일정](docs/2학기_개선계획.md)

## 현재 구현

- 이동, 일반 공격, 충전 대시, 몬스터 전투.
- 체력·공격력·방어력·내력 투자, 레벨업, 성장 저장과 이전 저장 마이그레이션.
- 사망 화면과 재도전(R), 체크포인트 복귀, 플레이어·퀘스트·Mood 통합 저장.
- 검술·호신·내공의 9개 무공 노드와 월영참·금강호신·운기조식.
- 밝은 종이 패널, 목재 버튼, 옥색 선택 표시를 사용하는 수련 UI.
- Mood와 전투 상태에 따른 플레이어/AI 조작권 전환.
- NPC/내면 대화, 로컬 LLM 스트리밍, 기억 검색을 위한 연결 코드.
- 첫 섬 퀘스트 데이터 9개와 진행·저장 시스템. 일부 완료 조건은 임시 트리거입니다.

![목재 무공 화면](docs/RpgScreenshots/skills.png)

## 실행 준비

1. Unity Hub에서 **Unity 6000.3.10f1**로 프로젝트를 엽니다.
2. 패키지 복원과 임포트를 기다립니다.
3. `Assets/Scenes/Main.unity`를 엽니다. 빌드의 첫 활성 씬도 Main입니다.
4. 아래 모델 연결을 확인한 뒤 Play를 실행합니다.

### 모델 파일

GGUF는 Git에서 제외됩니다. 저장소만 내려받아서는 LLM 준비가 완료되지 않습니다.

- Main의 `LLM_Server`와 `InnerVoice_LLM_Server`는 현재 모두 `Models/darkness-Q4_K_M.gguf`를 참조합니다.
- 프로젝트 내 위치: `Assets/StreamingAssets/Models/darkness-Q4_K_M.gguf`.
- `Embedding_Server`는 `all-MiniLM-L12-v2.Q4_K_M.gguf`를 참조합니다. 실제 모델 해석 경로를 LLMUnity 설정에서 확인해야 합니다.
- 이번 로컬 점검에서는 darkness 파일을 확인했지만 StreamingAssets 아래에서 임베딩 GGUF는 확인하지 못했습니다. 다른 캐시 경로의 존재나 기억 검색 성공까지 검증한 것은 아닙니다.

`Training/`에 학습 코드와 노트북이 있습니다. 학습은 게임 실행과 별도 작업이며 GPU·패키지·모델 이용 조건 확인이 필요합니다.

이 PC의 학습용 원본 GGUF는 프로젝트 밖 `C:/AINPC-ModelArchive/Meta-Llama-3-8B-Instruct-Q4_K_M.gguf`에 보관합니다. 게임 실행용 darkness 모델은 위 StreamingAssets 경로를 유지합니다. 모델 보관 위치와 정리 상태는 [로컬 용량 관리](docs/LOCAL_STORAGE.md)를 참고하세요.

목표 PC에서 RAM/VRAM, 첫 응답 및 완료 시간은 아직 측정하지 않았습니다. 기존 문서의 RAM 수치와 경량화 속도 배수는 보장 사양이 아닙니다.

## 조작과 성장

UI 기본 글꼴은 `NeoDunggeunmoPro-Regular.ttf`입니다. TextMeshPro용 동적 폰트는 `Assets/Resources/Fonts/NeoDunggeunmoPro SDF.asset`에 있으며, 추가 한글은 실행 중 생성됩니다. 기존 Paperlogy 폰트는 특수문자 대체용으로 보존했습니다. `Tools > AINPC > Apply NeoDunggeunmo Font`로 씬의 기존 폰트 연결을 다시 적용할 수 있습니다.

- `WASD`: 이동.
- 좌클릭: 일반 공격. 우클릭 길게 누르기: 충전 대시.
- `C`: 능력치 투자. `K`: 무공 트리.
- `1 / 2 / 3`: 습득한 월영참 / 금강호신 / 운기조식.
- `Escape`: 수련 화면 닫기.
- NPC 근처 이동: 대화 진입. 일부 퀘스트도 근접 트리거 방식입니다.

**수련 화면을 열어도 전투는 계속됩니다.** 이동·공격 입력은 차단되므로 안전한 곳에서 여세요. 내면 대화는 별도로 게임 시간을 정지시킵니다.

새 캐릭터는 능력치 5점·무공 3점, 레벨업마다 능력치 3점·무공 1점을 받습니다. 아이콘은 기존 코드 기반 문양을 유지하며 이미지 제작은 후속 작업입니다.

## 코드의 역할

게임 코드는 `Assets/Game/Scripts/Runtime`에 역할별로 분리되어 있습니다. `Player/PlayerStats.cs`는 성장 데이터, `Presentation/RpgUI.cs`는 표시와 투자 API 호출, `Progression/RpgSkillCatalog.cs`는 정의, `Progression/RpgSkillController.cs`는 비용·쿨타임·효과를 담당합니다. 편집기 도구와 검증 코드는 `Assets/Game/Scripts/Editor`에 있습니다. 자세한 경계와 주의점은 [프로젝트 구조](docs/PROJECT_STRUCTURE.md)를 참고하세요.

`MoodSystem`이 전환 조건을 판단하고 `ControlManager`가 중계합니다. `AIController`는 가까운 적을 추적·공격합니다. **기억에 따라 전투 성향을 바꾸는 기능은 아직 연결되어 있지 않습니다.**

`DialogueManager`와 `InnerVoiceManager`가 대화를 처리하고 `LLMResponseParser`가 대사와 Mood 변화량을 읽습니다. 프롬프트만으로 JSON 준수를 완전히 보장하지는 않습니다.

`QuestManager`는 게임 이벤트로 목표 진행도를 갱신합니다. `Resources/Quests`에 첫 섬 정의가 있습니다. LLM의 말이 아니라 게임 로직이 퀘스트 완료·보상을 판정하도록 유지해야 합니다.

## 저장

실제 저장은 `Application.persistentDataPath`를 사용합니다. 현재 Windows 기본 경로는 `%USERPROFILE%/AppData/LocalLow/DefaultCompany/AINPC`입니다.

- `player_save.json`: 플레이어 성장·체크포인트·Mood·퀘스트 상태를 하나의 스냅샷으로 저장합니다. 임시 파일 검증/교체와 이전 백업을 사용합니다.
- 기존 `quest_save.json`: 첫 통합 저장 시 가져오며 원본을 삭제하지 않습니다. 통합 이후에는 별도로 쓰지 않습니다.
- 검증: 실제 저장과 분리한 `VerificationResults` 아래 폴더.

주 파일이 손상되거나 없어도 백업을 확인합니다. 복구 불가능한 저장이나 더 최신 형식은 기본값으로 덮어쓰지 않습니다. 저장 실패 시 현재 진행은 메모리에 남지만 종료하면 마지막 정상 저장으로 돌아갑니다. 테스트 목적으로 실제 저장을 삭제하지 마세요.

## 검증

실행 명령은 [RPG UI 문서](docs/RPG_UI.md)에 있습니다. 검증은 빈 씬을 만들므로 **작업 씬을 저장하고 에디터를 종료하거나 별도 프로젝트 복사본에서 실행**하세요.

- `RpgVerification.Run`: 성장·저장·목재 연결·실제 버튼 콜백과 Unity 캡처.
- `RpgPlayVerification.Run`: Play 모드 피해·회복·방어·마나·쿨타임·중복 피격·메뉴 입력 차단.
- `RecoverySaveVerification.Run`: 사망/복귀, 저장 중단 주입, 보상 중복 방지, 백업, 분리 저장 마이그레이션.
- 산출물은 `VerificationResults/`에 보존하고 Git에서는 제외합니다. 선별한 화면은 `docs/RpgScreenshots/`에 있습니다.

이 검사는 실제 LLM 품질, 첫 섬 완주, 실사용 마우스 조작, 배포 빌드, 보스 밸런스를 보장하지 않습니다.

## 먼저 해결할 제한

1. 사망 복구 구현 완료: R/재도전 버튼으로 복귀합니다. 씬별 안전 체크포인트 배치와 Main 플레이 검증은 남아 있습니다.
2. 내면 대화에 실제 타임아웃이 연결되어 있지 않습니다. NPC 타임아웃도 오래된 요청 취소와는 다릅니다.
3. 일부 선택 퀘스트는 구역에 들어가기만 하면 완료됩니다. 선택·보스 처치·조작권 조건으로 교체해야 합니다.
4. 첫 섬 보스가 해금하는 `isle2_arrival` 데이터가 없어 다음 섬으로 이어지지 않습니다.
5. 플레이어·퀘스트·Mood 통합 저장 구현 완료. RAG 기억 기록과 연출은 파일 트랜잭션 밖이며, 과거에 이미 어긋난 분리 저장을 자동 판별하지는 못합니다.
6. 목재 스타일은 성장 UI에 적용했습니다. 대화·퀘스트·내면 UI 통합은 남아 있습니다.

사망 복구와 저장 안정성은 후속 작업으로 개선했습니다. 나머지 항목은 미해결이며 [개선 계획](docs/DEVELOPMENT_REVIEW_AND_PLAN.md)에 범위를 구분했습니다.

## 외부 에셋과 크레딧

- 추론 런타임: [LLMUnity / undreamai](https://github.com/undreamai/LLMUnity). 포함 라이선스를 확인하세요.
- 목재/종이 UI: Black Hammer, **Fantasy Wooden GUI : Free 2.1**. 공식 패키지 PNG 6개. [출처 기록](Assets/Resources/RpgWooden/README.md).
- 글꼴: 프로젝트의 Paperlogy TMP 에셋. 배포 전 원본 이용 조건과 고지 파일 점검이 필요합니다.
- 학습 코드의 베이스 모델: Meta Llama 3.1 계열. 실제 배포 모델과 파생 모델의 이용 조건·고지를 확인해야 합니다.

외부 아트는 프로젝트 자체 저작물이 아닙니다. 독립 재배포와 공개 저장소 포함 여부는 배포 전에 확인해야 합니다.
