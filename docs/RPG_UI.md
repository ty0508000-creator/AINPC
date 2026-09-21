# 무협 RPG 성장 UI

`PlayerStats`가 있는 플레이어에 `RpgSkillController`와 `RpgUI`가 실행 시 자동으로 추가됩니다.
기존 `PlayerHUD`는 새 UI가 있으면 중복 표시하지 않습니다. 원본 맵이나 프리팹을 다시 배치할 필요는 없습니다.

![무공 트리](RpgScreenshots/skills.png)

![능력치 강화](RpgScreenshots/attributes.png)

## 조작

- `C`: 능력치 창. 체력, 공격력, 방어력, 내력에 포인트를 투자합니다.
- `K`: 무공 트리. 노드를 선택한 뒤 상세 패널에서 습득하거나 강화합니다.
- `1`: 월영참. 전방 범위 피해.
- `2`: 금강호신. 6초 동안 피해 감소.
- `3`: 운기조식. 체력 회복.
- `Escape`: 성장 창 닫기. 단축바 버튼으로도 무공을 사용할 수 있습니다.
- 사망 시 `R` 또는 목재 재도전 버튼: 체크포인트 복귀, HP/MP 회복. 성장과 퀘스트 진행 유지.

성장 창은 게임 시간을 정지시키지 않습니다. 창을 열면 플레이어 이동과 공격 입력만 차단합니다.
대화, 내면 세계, 사망 중에는 성장 창이 닫힙니다. AI가 몸을 제어 중이면 직접 무공을 사용할 수 없습니다.

## 성장 규칙

신규 캐릭터에게 능력치 5점과 무공 3점을 지급합니다. 레벨업마다 각각 3점과 1점을 얻습니다.
체력 1단계는 최대 HP +20, 공격은 +2, 방어는 +2, 내력은 최대 MP +10입니다.
공격력 투자는 일반 공격과 대시, 월영참에 반영됩니다.
방어력은 `원래 피해 × 100 / (100 + 방어력 × 4)`로 적용하며 금강호신은 추가로 피해를 줄입니다.
양수 피해는 최소 1로 처리합니다.

검술 / 호신 / 내공에 각각 3개 노드가 있습니다. 두 번째 단계는 레벨 3, 마지막은 레벨 5를 요구합니다.
직전 노드를 하나 이상 습득해야 다음 노드를 배울 수 있습니다. 각 강화는 1포인트를 사용합니다.
기본 마나 재생은 초당 1이며 내공 패시브가 이를 높입니다.
스킬 정의와 저장용 인덱스는 `RpgSkillCatalog.cs`에 있습니다. 기존 인덱스를 바꾸면 저장 파일과 충돌하므로 새 스킬은 끝에 추가해야 합니다.

## 저장과 연결

투자, 스킬 습득, 경험치 획득, 종료 시 기존 `player_save.json`에 성장 정보를 저장합니다.
임시 파일을 쓴 뒤 원본을 교체하고 이전 파일은 `.bak`으로 보관합니다.
이전 버전 저장 파일은 체력과 레벨을 보존하며 해당 레벨까지 받을 포인트를 한 번 지급합니다.
저장 파일이 손상되면 백업을 먼저 읽습니다.

후속 개선으로 퀘스트·Mood·체크포인트도 같은 파일에 포함합니다. 주 파일이 없어도 백업 복구를 시도하며, 예전 분리 저장은 원본을 남기고 가져옵니다. [통합 저장 상세](RECOVERY_AND_SAVE.md).

화면은 1440×900 기준 CanvasScaler의 Expand 모드를 사용합니다.
한글 폰트는 씬에 로드된 Paperlogy 또는 TMP 기본 폰트를 사용합니다.
문양은 기존 코드 기반 벡터 UI를 유지합니다. 패널·버튼에는 Black Hammer의
Fantasy Wooden GUI : Free 2.1 원본 PNG 6개를 사용합니다.
`Assets/Resources/RpgWooden`에서 런타임 로드하며, 프레임과 버튼은 9-slice로 크기를 조정합니다.
밝은 종이 패널, 먹색 본문, 목재 버튼, 옥색 선택 표시를 적용했습니다.
아이콘 생성이나 교체, 전투 수식·저장 형식 변경은 하지 않았습니다.
출처와 라이선스는 해당 폴더의 README.md에 기록했습니다.

## 검증

작업 씬을 저장하고 Unity 에디터를 닫은 상태에서 실행하거나, 별도 프로젝트 복사본을 사용합니다. 검증은 빈 씬을 만듭니다. `VerificationResults` 아래의 별도 저장 폴더를 사용하며, 결과는 Unity 종료 후에도 보존됩니다. GUI 실행 파일은 셸에서 먼저 반환할 수 있으므로 결과 파일과 로그의 PASS를 확인해야 합니다.

```powershell
& 'C:/Program Files/Unity/Hub/Editor/6000.3.10f1/Editor/Unity.exe' -batchmode -projectPath C:/AINPC -executeMethod RpgVerification.Run -logFile C:/AINPC/Temp/rpg-verification.log -quit
& 'C:/Program Files/Unity/Hub/Editor/6000.3.10f1/Editor/Unity.exe' -batchmode -projectPath C:/AINPC -executeMethod RpgPlayVerification.Run -logFile C:/AINPC/Temp/rpg-play-verification.log
```

첫 검증은 포인트, 레벨업, 저장 복원과 이전 저장 마이그레이션을 확인하고 실제 Unity UI를 PNG로 렌더링합니다.
두 번째는 실제 Play 모드에서 스킬 피해, 다중 콜라이더 중복 피격 방지, 회복, 방어 효과, 마나 소비와 재사용 제한을 확인한 뒤 자동 종료합니다.
2026-09-20 목재 연결 검증에서 기능/저장/실제 버튼 콜백 검사 30개, Play 모드 전투 검사 13개가 통과했습니다. `VerificationResults/RpgVerification/result.txt`와 `VerificationResults/RpgPlayVerification/result.txt`에 결과가 기록됩니다. 캡처에는 1440×900 기본 화면과 1280×720, 1920×1080 무공 화면이 포함됩니다. RenderTexture 캡처는 실제 Game View 창 크기/마우스 입력 검사를 대체하지 않습니다.
이 검증은 본편 맵 전체 진행, 보스 밸런스, LLM 대화까지 검증하지는 않습니다.

검증 로그의 잔여 진단: 최종 Play 실행 시작 때 `UnityEditor.Search.SearchDatabase`의 인덱싱 `ArgumentOutOfRangeException`이 한 차례 기록됐습니다. 이후 13개 검사는 통과했지만 로그 전체가 오류 없는 상태라는 뜻은 아닙니다. 이는 게임 코드가 아닌 에디터 검색 초기화 스택이며 원인/재현성은 추가 확인이 필요합니다. 빈 테스트 씬의 AudioListener 부재 경고와 기존 `InnerVoiceManager.llmTimeout` 미사용 경고도 남아 있습니다. 이번 작업에서는 관련 패키지나 사용자 캐시를 삭제하지 않았습니다.
