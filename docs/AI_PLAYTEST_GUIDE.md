# AI 에이전트가 에디터에서 직접 플레이하는 방법

2026-10-29 Claude Code 가 AINPC 를 타이틀부터 약 6분(게임 시간) 플레이한 방식을 정리한 문서다.
다른 에이전트(Codex 등)가 같은 방식으로 플레이 확인을 할 때 그대로 따라 할 수 있게 썼다.

## 0. 이 방식으로 확인된 것 (참고)

타이틀 → 새 게임 → 동굴(검 위로 이동, `[Ctrl] 줍기` 안내 확인, Ctrl 로 줍기, 자동 장착 공격력 10) → 동굴 출구 →
Stage1 마을 → 촌장 현묵과 대화·의뢰 수락 → 마을 북서쪽 문 → 잿빛 숲(같은 씬 순간이동, 도착 목표·체크포인트 갱신) →
버섯 3마리 처치(퀘스트 3/3, 피해 0). 실제 세이브는 체크섬으로 바뀌지 않았음을 확인했다.

## 1. 연결 도구

- Unity 에디터에 **MCP for Unity**(`com.coplaydev.unity-mcp`) 패키지가 있고, 에이전트는 UnityMCP 도구를 쓴다.
  - `execute_code`: 에디터 안에서 C# 메서드 본문을 실행하고 `return` 값을 돌려받는다. **플레이의 거의 전부를 이걸로 한다.**
  - `manage_camera` (`action=screenshot`, `include_image=true`): 게임 화면 캡처. 화면을 보고 다음 행동을 정한다.
  - `read_console`: 로그·에러 확인.
- 다른 Unity 프로젝트가 같이 떠 있으면 먼저 `set_active_instance` 로 `AINPC@…` 를 고른다.
- `execute_code` 의 기본 컴파일러가 `codedom`(C# 6)이면 **튜플·`is` 패턴·로컬 함수 등이 안 된다.** 배열과 람다로 쓴다.
  `Object` 는 `object` 와 헷갈리므로 항상 `UnityEngine.Object` 로 쓴다.

## 2. 시작 전 안전 장치 (반드시)

1. **열린 씬에 저장 안 된 변경이 없는지** 확인한다. 씬을 열면 지금 씬이 닫힌다.
   ```csharp
   var s = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
   return s.path + " dirty=" + s.isDirty;
   ```
2. **테스트 세이브 스위치를 켠다.** `새 게임` 은 `SaveSystem.DeleteSave()` 로 세이브를 지우고, 플레이 중 자동 저장도 일어난다.
   스위치(`Tools > AINPC > 테스트 세이브로 플레이`, 코드로는 `TestSaveMode.Enabled = true`)를 켜면
   플레이 진입 직전에 실제 세이브를 `persistentDataPath/EditorTestSave` 로 복사하고 그 사본만 읽고 쓴다.
   - 플레이 진입 **후**에 `SaveSystem.VerificationDirectory` 를 바꾸는 방법은 시작 직후 자동 저장을 못 막는다. 쓰지 않는다.
   - 스위치는 플레이 중에 바꾸지 않는다(세이브가 두 폴더로 갈린다).
3. 실제 세이브 폴더(`C:/Users/<user>/AppData/LocalLow/DefaultCompany/AINPC`)를 백업하고 체크섬을 남긴다. 끝나고 비교한다.
4. 스크린샷 저장 위치는 **`output_folder: "Temp/PlayCheck"`** 로 준다. 기본값은 `Assets/Screenshots` 라 Git 변경으로 잡힌다.

## 3. 플레이 시작

```csharp
TestSaveMode.Enabled = true;
UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Main/Title.unity");
EditorApplication.isPlaying = true;
return "ok";
```

플레이 진입을 확인한 뒤(다음 호출에서 `EditorApplication.isPlaying`), **백그라운드에서도 게임과 입력이 돌게** 한다.
에디터 창에 포커스가 없으면 플레이가 1프레임에서 멈추거나 입력이 무시된다.

```csharp
Application.runInBackground = true;
var s = UnityEngine.InputSystem.InputSystem.settings;
s.backgroundBehavior = UnityEngine.InputSystem.InputSettings.BackgroundBehavior.IgnoreFocus;
s.editorInputBehaviorInPlayMode = UnityEngine.InputSystem.InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
return "scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + " saveDir=" + SaveSystem.DirectoryPath;
```

`saveDir` 가 `...\EditorTestSave` 인지 확인한다. 이 프로젝트에서 `InputSystem.settings` 는 에셋이 아니라 메모리 값이라
도메인 리로드 때 원래대로 돌아간다(`AssetDatabase.GetAssetPath(InputSystem.settings)` 가 빈 문자열).

## 4. 입력 넣기: 가상 키보드·마우스

게임 코드를 직접 부르지 않고 **Input System 에 가상 장치를 붙여 키 상태를 넣는다.**
플레이어 이동은 `InputSystem.actions.FindAction("Move").ReadValue<Vector2>()`(WASD), 공격은 `Mouse.current`(좌클릭·커서 위치),
줍기는 `Keyboard.current.leftCtrlKey`, 대화는 E 키를 읽는다.

```csharp
UnityEngine.InputSystem.Keyboard kb = null;
foreach (var d in UnityEngine.InputSystem.InputSystem.devices) if (d.name == "PlayKeyboard") kb = (UnityEngine.InputSystem.Keyboard)d;
if (kb == null) kb = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Keyboard>("PlayKeyboard");
kb.MakeCurrent();
// 누르기: 지금 눌려 있어야 할 키 전부를 한 번에 넣는다(넣지 않은 키는 떼진다)
UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.W, UnityEngine.InputSystem.Key.A));
// 떼기
UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState());
```

장치는 이름(`PlayKeyboard`, `PlayMouse`)으로 다음 호출에서 다시 찾는다.

### 핵심: "몇 초 동안만 누르기"는 Unity 안에서 시간을 잰다

에이전트의 호출과 호출 사이는 수 초~수십 초가 걸린다. "누르기 → (다음 호출에서) 떼기" 로 하면 그동안 계속 걸어서
목표를 지나친다(실제로 동굴 끝까지 30칸을 지나쳤다). **누르는 호출 안에서 `EditorApplication.update` 콜백을 걸고
`Time.time` 으로 떼게** 한다.

```csharp
UnityEngine.InputSystem.Keyboard kb = null;
foreach (var d in UnityEngine.InputSystem.InputSystem.devices) if (d.name == "PlayKeyboard") kb = (UnityEngine.InputSystem.Keyboard)d;
UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(UnityEngine.InputSystem.Key.A));
float until = Time.time + 1.0f;   // 이동속도 5 → 1초에 약 5칸
EditorApplication.CallbackFunction release = null;
release = () => {
  if (!EditorApplication.isPlaying || Time.time >= until) {
    if (EditorApplication.isPlaying) UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState());
    EditorApplication.update -= release;
  }
};
EditorApplication.update += release;
return "walk left 1s";
```

여러 동작(걷기 → 멈춤 → E)은 같은 방식으로 `stage` 변수를 두고 시간 순서대로 넣는다.

```csharp
float t0 = Time.time; int stage = 0;
EditorApplication.CallbackFunction step = null;
step = () => {
  if (!EditorApplication.isPlaying) { EditorApplication.update -= step; return; }
  float e = Time.time - t0;
  if (stage == 0 && e >= 0.6f) { /* 이동 키 떼기 */ stage = 1; }
  else if (stage == 1 && e >= 0.9f) { /* E 누르기 */ stage = 2; }
  else if (stage == 2 && e >= 1.1f) { /* E 떼기 */ EditorApplication.update -= step; }
};
EditorApplication.update += step;
```

**주의**: 이 호출이 돌려준 직후 바로 위치를 물으면 아직 움직이는 중이다. 동작 시간이 지난 뒤에 확인한다.
확인 결과가 이상하면 먼저 "아직 진행 중인가"를 의심한다.

## 5. 상황 읽기

화면(스크린샷)과 게임 상태(코드 조회)를 함께 본다. 위치·목표 좌표를 알면 몇 초 걸을지 계산할 수 있다.

```csharp
var p = UnityEngine.Object.FindFirstObjectByType<PlayerStats>();
var sb = new System.Text.StringBuilder("scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name
  + " pos=" + p.transform.position + " hp=" + p.HP + "/" + p.MaxHP + " lv=" + p.Level + " checkpoint=" + p.CheckpointPosition + "\n");
foreach (var a in UnityEngine.Object.FindObjectsByType<HubStoryActor>(FindObjectsSortMode.None)) sb.Append(a.actorId + " " + a.transform.position + "\n");
foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None))
  if (!m.IsDead && Vector2.Distance(m.transform.position, p.transform.position) < 40) sb.Append(m.Data.MonsterName + " @" + m.transform.position + " hp=" + m.CurrentHP + "\n");
return sb.ToString();
```

자주 본 것: 문 위치(`ZonePortal`·`ScenePortal` 의 위치와 `targetSpawn`/`targetScene`), 퀘스트 상태(`QuestManager.Instance.GetState(...)`, `.Get(id).Progress`),
조작을 막는 창(`DialogueManager.IsDialogueOpen`, `StoryConversationUI.IsOpen`, `PauseMenuUI.IsOpen`, `RpgUI.IsOpen`, `InventoryWindow.IsOpen`).

## 6. UI 버튼

타이틀 버튼과 이야기 대화창 버튼은 글자로 찾아 `onClick.Invoke()` 했다(마우스 좌표 클릭보다 확실하다).

```csharp
foreach (var b in UnityEngine.Object.FindObjectsByType<UnityEngine.UI.Button>(FindObjectsSortMode.None)) {
  var t = b.GetComponentInChildren<TMPro.TMP_Text>();
  if (t != null && b.gameObject.activeInHierarchy && t.text.Contains("새 게임")) { b.onClick.Invoke(); return "clicked"; }
}
return "not found";
```

쓴 버튼 글자: `새 게임`, `대화 확인`(대화 확인 / 보고), `의뢰 수락`. LLM 대화창(스미스 등)은 `DialogueManager.CloseDialogue()` 로 닫았다.

## 7. 전투: 입력만 넣는 자동 조종

한 번의 `execute_code` 로 `EditorApplication.update` 콜백을 60초 걸고, 매 틱마다:

1. 가장 가까운 살아 있는 몬스터를 고른다.
2. 몬스터가 공격 예고 중(`RegionalMonster.CurrentPhase == Phase.Windup`)이면 반대 방향 키, 아니면 1.3칸보다 멀 때 다가가는 키를 넣는다.
3. 가상 마우스 커서를 몬스터의 화면 좌표(`Camera.main.WorldToScreenPoint`)에 두고, 1.8칸 안이면 좌클릭을 눌렀다 뗐다 한다
   (같은 상태를 계속 넣으면 `wasPressedThisFrame` 이 다시 안 생기므로 누름·뗌을 번갈아 넣는다).
4. 끝나면 키·버튼을 모두 떼고 `Debug.Log("[PlayCheck] ...")` 로 결과를 남긴다.

```csharp
UnityEngine.InputSystem.Keyboard kb = null; UnityEngine.InputSystem.Mouse mouse = null;
foreach (var d in UnityEngine.InputSystem.InputSystem.devices) { if (d.name == "PlayKeyboard") kb = (UnityEngine.InputSystem.Keyboard)d; if (d.name == "PlayMouse") mouse = (UnityEngine.InputSystem.Mouse)d; }
if (mouse == null) mouse = UnityEngine.InputSystem.InputSystem.AddDevice<UnityEngine.InputSystem.Mouse>("PlayMouse");
mouse.MakeCurrent(); kb.MakeCurrent();
var player = UnityEngine.Object.FindFirstObjectByType<PlayerStats>();
float t0 = Time.time, nextClick = 0; bool pressed = false;
EditorApplication.CallbackFunction pilot = null;
pilot = () => {
  if (!EditorApplication.isPlaying || player == null) { EditorApplication.update -= pilot; return; }
  if (Time.time - t0 > 60f || !player.IsAlive) {
    UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState());
    UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, new UnityEngine.InputSystem.LowLevel.MouseState());
    Debug.Log("[PlayCheck] done hp=" + player.HP + " exp=" + player.EXP);
    EditorApplication.update -= pilot; return;
  }
  MonsterBase target = null; float best = 999;
  foreach (var m in UnityEngine.Object.FindObjectsByType<MonsterBase>(FindObjectsSortMode.None)) {
    if (m.IsDead) continue;
    float dd = Vector2.Distance(m.transform.position, player.transform.position);
    if (dd < best && dd < 30) { best = dd; target = m; }
  }
  var keys = new System.Collections.Generic.List<UnityEngine.InputSystem.Key>();
  var ms = new UnityEngine.InputSystem.LowLevel.MouseState();
  if (target != null) {
    Vector2 delta = target.transform.position - player.transform.position;
    var rm = target as RegionalMonster;
    bool danger = rm != null && rm.CurrentPhase == RegionalMonster.Phase.Windup;
    Vector2 dir = danger ? -delta : (delta.magnitude > 1.3f ? delta : Vector2.zero);
    if (dir.x > 0.3f) keys.Add(UnityEngine.InputSystem.Key.D); else if (dir.x < -0.3f) keys.Add(UnityEngine.InputSystem.Key.A);
    if (dir.y > 0.3f) keys.Add(UnityEngine.InputSystem.Key.W); else if (dir.y < -0.3f) keys.Add(UnityEngine.InputSystem.Key.S);
    Vector3 screen = Camera.main.WorldToScreenPoint(target.transform.position);
    ms.position = new Vector2(screen.x, screen.y);
    if (!danger && delta.magnitude < 1.8f && Time.time >= nextClick) { pressed = !pressed; if (!pressed) nextClick = Time.time + 0.2f; }
    else pressed = false;
    if (pressed) ms.buttons = 1;
  }
  UnityEngine.InputSystem.InputSystem.QueueStateEvent(kb, new UnityEngine.InputSystem.LowLevel.KeyboardState(keys.ToArray()));
  UnityEngine.InputSystem.InputSystem.QueueStateEvent(mouse, ms);
};
EditorApplication.update += pilot;
return "pilot started";
```

이 프로젝트의 몬스터는 사냥터에 카메라가 가까이 가야 생긴다(`MonsterSpawnArea` 의 카메라 범위 검사). 사냥터 입구에서
몬스터가 없으면 안쪽으로 조금 걸어 들어간다.

## 8. 겪은 함정과 해결

| 증상 | 원인 | 해결 |
| --- | --- | --- |
| 플레이가 1프레임에서 안 움직임 | 에디터가 백그라운드 | `Application.runInBackground = true` |
| 목표를 크게 지나침 | 호출 사이 지연 동안 키가 눌린 채 | 누르는 호출 안에서 `EditorApplication.update` 로 시간 재서 떼기 |
| 걸었는데 거의 안 움직임 | NPC·주민·집에 막힘, 또는 대화창이 닫히는 중 | 스크린샷으로 막힌 것 확인 → 돌아가는 경로. 대화 직후에는 창이 닫혔는지 먼저 확인 |
| E 를 눌렀는데 다른 NPC 대화가 열림 | 가장 가까운 상호작용 대상이 반응 | 목표 NPC 옆까지 간 뒤 E. 엉뚱한 대화는 닫기 |
| 위치 확인 값이 틀려 보임 | 동작이 아직 진행 중이거나 씬 이동 중 | 시간이 지난 뒤 다시 조회, `GameFlow.Instance.IsLoading` 확인 |
| 스크린샷이 Git 변경으로 잡힘 | 기본 저장 위치 `Assets/Screenshots` | `output_folder: "Temp/PlayCheck"` |
| `execute_code` 컴파일 실패 | C# 6(codedom) 제한, `Object` 모호함 | 튜플 대신 배열, `UnityEngine.Object` |

## 9. 끝낸 뒤 정리 (반드시)

```csharp
foreach (var d in new System.Collections.Generic.List<UnityEngine.InputSystem.InputDevice>(UnityEngine.InputSystem.InputSystem.devices))
  if (d.name == "PlayKeyboard" || d.name == "PlayMouse") UnityEngine.InputSystem.InputSystem.RemoveDevice(d);
Application.runInBackground = false;
EditorApplication.isPlaying = false;
return "stopping";
```

플레이가 완전히 끝난 다음 호출에서 `TestSaveMode.Enabled = false;` 로 스위치를 원래대로 돌린다(플레이 중에는 바꾸지 않는다).
그다음 실제 세이브 체크섬을 백업과 비교하고, `git status` 로 의도하지 않은 변경(스크린샷, 씬 등)이 없는지 본다.
에이전트가 연 씬이 사용자가 원래 열어 둔 씬과 다르면 원래 씬을 다시 열어 둔다.

## 10. 한계

- 키 입력은 진짜 입력 경로(Input System)를 타지만, 화면을 보는 간격이 호출 단위라 사람처럼 즉각 반응하지는 못한다.
  세밀한 회피는 7번처럼 Unity 안에서 도는 콜백으로 맡긴다.
- 대화창·타이틀 버튼은 마우스 좌표 클릭 대신 `onClick.Invoke()` 로 눌렀다. 촌장에게 처음 말을 건 것은 E 키였지만,
  의뢰를 받으려고 두 번째로 말을 걸 때는 E 대신 `HubStoryActor.Interact()` 를 직접 불렀다(입력 경로가 아니라 게임 함수 호출).
- LLM NPC 대화(로컬 모델)는 확인하지 않았다.
