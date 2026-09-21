# AINPC 개발 가이드

## 프로젝트 목표

`귀환자의 수련록`은 Unity 6000.3.10f1 기반의 2D 탑뷰 무협 액션 RPG 프로토타입이다. 첫 번째 섬에서 전투, 성장, NPC/내면 대화, 선택, 보스까지 이어지는 짧은 플레이 가능한 데모를 우선 완성한다.

작업 시작 전에는 `README.md`와 요청에 직접 관련된 `docs/` 문서를 읽는다. 기획의 사실과 현재 구현 상태를 혼동하지 않는다. 아직 구현되지 않은 기능은 구현된 것처럼 문서화하거나 주장하지 않는다.

## 폴더와 책임

게임 코드의 기준 위치는 `Assets/Game/Scripts`다.

- `Runtime/Player`: 이동, 공격, 스탯, 체크포인트
- `Runtime/Enemies`: 몬스터, 스폰, 투사체
- `Runtime/Combat`: 전투 공통 인터페이스
- `Runtime/Progression`: 무공 정의, 습득, 쿨다운, 효과
- `Runtime/Control`: Mood, AI/플레이어 조작권
- `Runtime/Quests`: 퀘스트 데이터, 상태, 트리거, 진행
- `Runtime/Persistence`: 플레이어·퀘스트 통합 저장과 DTO
- `Runtime/Dialogue`: NPC 대화, 내면 대사, LLM 응답 파싱
- `Runtime/Memory`: 로컬 LLM 기억과 저장 경로
- `Runtime/Presentation`: HUD, RPG UI, 퀘스트 UI, 카메라, 화면 연출
- `Editor`: Unity 편집기 도구와 자동 검증. 런타임 코드에서 참조하지 않는다.

외부 패키지인 `Assets/Runtime`, `Assets/Editor`, `Assets/StreamingAssets/LlamaLib-v2.0.5`에는 게임 기능을 직접 추가하지 않는다. 기능은 `Assets/Game`에 두고 패키지 API를 사용한다.

## Unity 에셋 안전 규칙

- Unity 에셋을 파일시스템에서 이동할 때는 반드시 해당 `.meta`도 함께 이동한다. GUID를 새로 만들거나 복구하지 않는다.
- 씬, 프리팹, ScriptableObject의 직렬화 클래스명과 `[SerializeField]` 필드명은 호환성 확인 없이 변경하지 않는다.
- `Resources.Load`의 문자열 경로와 `Assets/Resources` 구조를 함부로 바꾸지 않는다.
- 대규모 씬 YAML은 필요한 필드만 변경한다. 요청 없이 씬 전체를 재저장하거나 포맷하지 않는다.
- 새 UI를 런타임 생성할 때는 만든 GameObject/Canvas의 소유자와 파괴 시점을 명확히 한다.
- 한글 UI는 `NeoDunggeunmoPro SDF`를 우선 사용한다. 소스 글꼴은 `Assets/Fonts/NeoDunggeunmoPro-Regular.ttf`, TMP 에셋은 `Assets/Resources/Fonts/NeoDunggeunmoPro SDF.asset`이다. 기존 Paperlogy는 대체 글꼴로 보존한다.

## 게임플레이와 저장 규칙

- 게임 규칙은 코드가 판정한다. LLM 응답은 대화와 연출에만 사용하며 퀘스트 완료, 보상, 저장 데이터를 직접 결정하게 하지 않는다.
- 퀘스트 보상, Mood 변경, 플레이어 성장, 퀘스트 상태는 통합 스냅샷으로 저장되는 흐름을 유지한다.
- 저장 읽기 실패와 새 저장 슬롯을 구분한다. 손상되거나 미래 버전인 저장을 기본값으로 덮어쓰지 않는다.
- 섬별로 일부 퀘스트만 로드된 씬에서 저장하더라도 다른 섬의 퀘스트 기록을 제거하지 않는다.
- 이벤트 구독은 초기화 순서와 GameObject 비활성화/재활성화를 고려한다. 구독 해제와 코루틴 정리를 함께 처리한다.

## LLM·모델·개인정보

- GGUF와 대형 모델 파일은 Git에 넣지 않는다. `.gitignore`의 `*.gguf`, `*.bin`, `Assets/StreamingAssets/Ollama/` 규칙을 유지한다.
- 게임 실행 모델은 `Assets/StreamingAssets/Models/`에 두며, 학습 원본은 프로젝트 밖 보관 경로를 우선 사용한다. 자세한 위치는 `docs/LOCAL_STORAGE.md`를 따른다.
- API 키, 로컬 대화 기록, NPC 기억 저장 파일, 사용자 개인 데이터는 커밋하지 않는다.
- 모델 경로나 LLMUnity 설정을 바꾸면 모델 부재 시 게임이 안전하게 대화 기능만 비활성화하는지 확인한다.

## 검증

코드 변경 범위에 따라 가장 가까운 검증을 실행하고 결과를 보고한다.

- UI·성장: `RpgVerification.Run`
- 저장·사망 복구·퀘스트 저장: `RecoverySaveVerification.Run`
- 플레이 모드 전투·AI 제어권·재도전: `RpgPlayVerification.Run`
- 실제 메인 씬 변경: `docs/MANUAL_PLAYTEST.md` 기준 수동 플레이 검사

검증은 실제 플레이어 저장을 건드리지 않는 별도 `VerificationResults` 경로를 사용한다. 임시 Unity 복사 프로젝트를 만들었다면 결과를 보존한 뒤 정리하고, 원본 프로젝트나 사용자 저장 파일을 삭제하지 않는다.

## Git 작업

- 작업 전후 `git status --short`를 확인하고, 기존 사용자 변경은 요청 범위와 관계없으면 건드리지 않는다.
- 커밋 전에는 의도하지 않은 모델, `Library/`, `Temp/`, `VerificationResults/`, 개인 파일, API 키가 stage되지 않았는지 확인한다.
- 커밋 메시지는 한 가지 의도를 명확히 적는다. 예: `feat: add isle one boss encounter`.
- 원격 푸시, 브랜치 병합, 되돌릴 수 있는 대량 삭제는 사용자의 명시적 요청이 있을 때만 한다.
- `.gitattributes`의 LFS 추적 규칙을 임의로 넓히지 않는다. LFS 오류는 추적 대상과 로컬 설정을 먼저 확인한다.

## 문서 기준

- 시스템·데이터 흐름이 바뀌면 관련 `docs/` 문서와 README를 함께 갱신한다.
- 기획 문서는 목표와 선택지를, 코드 문서는 현재 작동 방식과 검증 방법을 적는다.
- 문서에는 절대 경로, 개인 데이터, 모델 원본, API 키를 넣지 않는다. 로컬 전용 경로는 꼭 필요할 때만 `docs/LOCAL_STORAGE.md`에 제한적으로 적는다.

## 작업 방식

- 먼저 짧게 변경 범위와 위험을 알리고, 실제 변경 후 검증 결과를 보고한다.
- 작은 기능 단위로 구현하고 기존 씬/프리팹 연결을 보존한다.
- 불확실한 게임 기획 선택은 코드로 확정하기 전에 사용자에게 짧게 선택지를 제시한다.
- 현재 요청과 무관한 리팩터링, 패키지 업그레이드, 에셋 교체는 하지 않는다.
