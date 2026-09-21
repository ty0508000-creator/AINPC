# 프로젝트 구조

## 코드 배치

게임 소유 코드는 `Assets/Game/Scripts` 아래에 둡니다.

- `Runtime/Player`: 플레이어 이동·공격·스탯·체크포인트
- `Runtime/Enemies`: 몬스터 행동·스폰·투사체·정의
- `Runtime/Combat`: 피해 인터페이스
- `Runtime/Progression`: 스킬 정의·성장·실행
- `Runtime/Control`: 기분과 플레이어/AI 제어권
- `Runtime/Quests`: 퀘스트 정의·상태·트리거·진행
- `Runtime/Persistence`: 통합 저장, 퀘스트 직렬화, 저장 DTO
- `Runtime/Dialogue`: NPC 대화·내면 대사·응답 파싱
- `Runtime/Memory`: LLM 기억과 저장 경로
- `Runtime/Presentation`: HUD·스킬 UI·퀘스트 UI·카메라·연출
- `Editor`: 씬 구성 도구와 자동 검증. 빌드에 포함되지 않음

`Assets/Runtime`, `Assets/Editor`는 LLMUnity 패키지 코드이므로 게임 기능을 넣지 않습니다. `Resources`, 씬, 프리팹, 폰트와 외부 아트는 기존 경로를 유지합니다.

## 참조 보존 원칙

기존 45개 스크립트와 `.meta`를 함께 이동했습니다. 클래스 이름과 직렬화 필드는 유지합니다. `Player_Attack`, `Player_Controller`, `NPCInteraction`은 파일명을 실제 클래스 이름과 맞췄습니다. 어셈블리는 기존 기본 어셈블리를 유지합니다. 이번 정리는 폴더별 강제 어셈블리 격리는 아닙니다.

향후 이동도 Unity 에디터에서 하거나 `.meta`를 반드시 함께 이동하세요. `Resources.Load` 문자열 경로는 GUID와 별개이므로 임의로 변경하지 마세요. 기존 설명 문서의 `Assets/Script` 경로는 이 문서의 역할별 경로로 대체되었습니다.

## 수정한 구조적 위험

- 저장 읽기 성공 여부와 데이터 유무 분리: `TryLoadPlayer`의 성공 + null은 신규 슬롯, 실패는 손상/미지원 버전입니다. 퀘스트는 실패 시 신규 게임으로 진행하지 않습니다.
- 일부 퀘스트 카탈로그로 저장해도 다른 섬의 기록을 유지하며 진행 배열을 복사합니다.
- Mood → Control → AI가 Start 호출 순서에 의존하지 않도록 구독 시 현재 상태를 동기화합니다.
- 비활성 AI의 코루틴을 중단하고 재활성화 시 현재 제어권을 확인합니다. 필수 컴포넌트가 없으면 AI를 시작하지 않습니다.
- 기억 초기화 플래그는 예외와 조기 반환에도 finally에서 해제합니다.
- 플레이어 연출이 생성한 Canvas는 해당 컴포넌트 파괴 시 정리합니다.

## 남은 경계와 다음 작업

폴더 정리만으로 모든 결합이 제거되지는 않습니다. PlayerStats가 스킬/UI를 생성하고, QuestManager가 보상·저장·대화에 관여하는 부분은 남아 있습니다. 다음 단계에서는 플레이어 구성 전용 컴포넌트와 퀘스트 결과 적용 서비스를 추출하고, 그 뒤 asmdef를 도입하는 순서가 안전합니다.

저장 실패 시 플레이어 화면에 복구 선택지를 보여주는 흐름, LLM 비동기 요청의 씬 종료 취소와 제한 시간, 여러 NPC의 대화 소유권, Mood/연출 전체의 비활성화·재활성화 정책은 추가 검증이 필요합니다. 실제 Main 씬 전체 플레이와 LLM 서버 통합 테스트를 자동 테스트 통과로 대체하지 않습니다.

## 검증

원본 플레이어 저장과 씬을 변경하지 않도록 별도 테스트 프로젝트에서 실행합니다.

- `RpgVerification.Run`: 성장·스킬·UI
- `RecoverySaveVerification.Run`: 사망 복구·원자 저장·이전 저장 마이그레이션·손상·다른 섬 기록 보존
- `RpgPlayVerification.Run`: 플레이 모드 전투·복구

검증 결과는 테스트 프로젝트의 `VerificationResults`와 Unity 로그에 기록됩니다.

2026-09-21 검증: Unity 6000.3.10f1 별도 프로젝트에서 저장·복구 42개, 성장·UI 32개, 플레이 모드 전투·제어권·복구 27개, 총 101개 검증을 통과했습니다. 기존 스크립트 GUID 45개 보존과 씬·프리팹·리소스 파일 미변경도 확인했습니다. 기존 패키지의 중복 DLL 경고와 사용되지 않는 `InnerVoiceManager.llmTimeout` 경고는 별도 과제로 남아 있습니다.

플레이 검증 시작 시 UnityEditor.Search.SearchDatabase의 인덱싱 예외도 기록되었습니다. 게임 코드 스택은 아니며 이후 플레이 검증은 통과했지만, 에디터 환경까지 오류가 전혀 없다는 의미는 아닙니다. 원본 프로젝트의 검색 인덱스를 임의로 삭제하거나 패키지를 변경하지 않았습니다.
