# AINPC

Unity 6000.3.10f1 2D 게임. LLM 으로 움직이는 NPC 가 주제.

## 작업 방식

- **`main` 에서 직접 작업하지 않는다.** 작업을 시작할 때 `feature/<주제>` 브랜치를 먼저 만든다.
- **작업 단위마다 바로 커밋한다.** 기능 하나, 버그 하나가 끝나면 그때 커밋한다.
  세션 끝에 몰아서 하지 않는다.
- **내가 작성하지 않은 변경은 커밋하지 않는다.** Unity 가 제멋대로 건드리는
  `Packages/packages-lock.json`, `ProjectSettings/*`, `AINPC.slnx`, `*.meta` 삭제 등은
  그대로 두고 사용자에게 알린다. 커밋할 파일은 경로로 하나씩 지정한다 (`git add -A` 금지).
- 커밋 메시지는 한국어로, 무엇을 왜 고쳤는지 쓴다.

## 코드 규칙

- 주석과 `<summary>` 문서 주석은 한국어.
- 줄바꿈은 CRLF (`core.autocrlf=true`). 스크립트로 파일을 고칠 때 LF 로 납작해지지 않게 주의.
- `Assets/MapGen/Editor/` 는 에디터 전용 어셈블리다. 런타임 코드(`Assets/Game/Scripts/Runtime/`)에서
  참조할 수 없고, 반대 방향은 가능하다.

## 스프라이트 정렬

월드에 서는 것은 전부 `YSortRenderer` 를 달아야 한다. 안 달면 정렬값 0 으로 남아
맵 생성기가 만든 소품(500~1500)에 통째로 가려진다. 기준값의 원본은 `YSortRenderer` 의
`DefaultBaseOrder` / `DefaultPrecision` 이고 `MapGenSettings` 가 이를 참조한다.
항상 지형 위에 떠야 하는 표시는 `YSortRenderer.WorldOverlayOrder`.

## 맵 생성기

`Tools > 맵 생성기`. 지형 계산(`MapBuilder`)과 씬 생성(`MapSceneBuilder`)이 분리돼 있어,
`MapBuilder.Build(settings)` 만 호출하면 **씬을 건드리지 않고** 배치 결과를 검증할 수 있다.

`Assets/testAsset/Houses_Pack/houses.png` 에는 완성된 집이 없다. 지붕과 벽이 따로인
조립 키트라 `MapGenSettings.HouseRecipes` 에서 쌓아 올린다.

## 검증

- Unity 가 떠 있으면 UnityMCP 의 `read_console` 로 컴파일 결과를, `execute_code` 로
  실제 동작을 확인한다. 다른 Unity 프로젝트가 같이 떠 있으면 먼저
  `set_active_instance` 로 `AINPC@…` 를 골라야 한다.
- 씬을 새로 만들거나 여는 메뉴(맵 생성기, `Tools > AINPC > …` 씬 빌더)를 돌리면
  **열려 있는 씬이 닫히므로** 저장 안 된 변경이 없는지 먼저 확인하거나 물어볼 것.
- 플레이 모드로 시험할 때 보스 처치·자동 저장이 실제 세이브를 덮어쓴다. 플레이 진입 후
  `SaveSystem.VerificationDirectory` 를 임시 폴더로 바꿔 두면 세이브가 그쪽으로 간다.
