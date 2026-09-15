using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AINPC.MapGen
{
    /// <summary>Tools 메뉴에서 여는 맵 생성 창.</summary>
    public class MapGeneratorWindow : EditorWindow
    {
        private const string PrefsKey = "AINPC.MapGen.Settings";
        private const string SceneFolder = "Assets/Scenes/";

        private MapGenSettings settings = new MapGenSettings();
        private Vector2 scroll;

        [MenuItem("Tools/맵 생성기 (Map Generator)")]
        public static void Open()
        {
            var window = GetWindow<MapGeneratorWindow>("맵 생성기");
            window.minSize = new Vector2(330f, 480f);
        }

        private void OnEnable()
        {
            string json = EditorPrefs.GetString(PrefsKey, string.Empty);
            if (!string.IsNullOrEmpty(json))
                JsonUtility.FromJsonOverwrite(json, settings);
        }

        private void OnDisable()
        {
            EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(settings));
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.LabelField("씬", EditorStyles.boldLabel);
            settings.sceneName = EditorGUILayout.TextField("씬 이름", settings.sceneName);
            EditorGUILayout.LabelField(" ", SceneFolder + SafeSceneName() + ".unity", EditorStyles.miniLabel);
            settings.spawnPlayer = EditorGUILayout.Toggle("플레이어 배치", settings.spawnPlayer);
            settings.setupCamera = EditorGUILayout.Toggle("카메라 세팅", settings.setupCamera);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("크기", EditorStyles.boldLabel);
            settings.width = EditorGUILayout.IntSlider("가로(칸)", settings.width, 40, 500);
            settings.height = EditorGUILayout.IntSlider("세로(칸)", settings.height, 40, 500);

            if (settings.width * settings.height > 90000)
                EditorGUILayout.HelpBox(
                    "맵이 큽니다. 소품이 수만 개까지 늘어 씬 저장과 에디터가 느려질 수 있습니다. "
                    + "나무 / 잡초 밀도를 낮춰 보세요.", MessageType.Warning);

            using (new EditorGUILayout.HorizontalScope())
            {
                settings.seed = EditorGUILayout.IntField("시드", settings.seed);
                if (GUILayout.Button("랜덤", GUILayout.Width(52f)))
                    settings.seed = Random.Range(1, int.MaxValue);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("물", EditorStyles.boldLabel);
            settings.waterBorder = EditorGUILayout.IntSlider("테두리 두께", settings.waterBorder, 0, 12);
            settings.river = EditorGUILayout.Toggle("강", settings.river);
            using (new EditorGUI.DisabledScope(!settings.river))
            {
                settings.riverWidth = EditorGUILayout.IntSlider("강 폭", settings.riverWidth, 2, 14);
                settings.riverX = EditorGUILayout.Slider("강 위치", settings.riverX, 0.05f, 0.95f);
            }
            settings.lakeCount = EditorGUILayout.IntSlider("호수 개수", settings.lakeCount, 0, 6);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("마을", EditorStyles.boldLabel);
            settings.village = EditorGUILayout.Toggle("마을 생성", settings.village);
            using (new EditorGUI.DisabledScope(!settings.village))
            {
                settings.villageWidth = EditorGUILayout.IntSlider("마을 가로", settings.villageWidth, 12, 480);
                settings.villageHeight = EditorGUILayout.IntSlider("마을 세로", settings.villageHeight, 12, 480);
                settings.villageX = EditorGUILayout.Slider("마을 위치", settings.villageX, 0.05f, 0.95f);
                settings.blockSize = EditorGUILayout.IntSlider("거리 간격", settings.blockSize, 10, 40);
                EditorGUILayout.LabelField(" ", "거리 간격이 좁을수록 집이 촘촘해집니다", EditorStyles.miniLabel);
                settings.houseGap = EditorGUILayout.IntSlider("집 간격", settings.houseGap, 1, 12);
                settings.houseChance = EditorGUILayout.Slider("집 채우는 비율", settings.houseChance, 0.1f, 1f);
                EditorGUILayout.LabelField(" ", "간격을 넓히고 비율을 낮추면 집이 성기게 들어섭니다", EditorStyles.miniLabel);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("자연물", EditorStyles.boldLabel);
            settings.treeDensity = EditorGUILayout.Slider("나무 밀도", settings.treeDensity, 0f, 0.4f);
            settings.decorDensity = EditorGUILayout.Slider("잡초/바위 밀도", settings.decorDensity, 0f, 0.3f);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "새 씬을 만들어 타일을 찍습니다. 지금 열린 씬은 저장 여부를 물어본 뒤 닫힙니다.",
                MessageType.Info);

            if (GUILayout.Button("맵 생성", GUILayout.Height(34f)))
                EditorApplication.delayCall += Generate;   // OnGUI 밖에서 씬을 새로 연다

            EditorGUILayout.EndScrollView();
        }

        private string SafeSceneName()
        {
            string name = string.IsNullOrWhiteSpace(settings.sceneName) ? "MapGen" : settings.sceneName.Trim();
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        private void Generate()
        {
            string scenePath = SceneFolder + SafeSceneName() + ".unity";

            if (File.Exists(scenePath) &&
                !EditorUtility.DisplayDialog("덮어쓰기", scenePath + " 이(가) 이미 있습니다. 덮어쓸까요?", "덮어쓰기", "취소"))
                return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(settings));

            try
            {
                EditorUtility.DisplayProgressBar("맵 생성기", "지형 계산 중...", 0.3f);
                MapPlan plan = MapBuilder.Build(settings);

                EditorUtility.DisplayProgressBar("맵 생성기", "타일 찍는 중...", 0.7f);
                bool ok = MapSceneBuilder.CreateScene(settings, plan, scenePath, out string report);

                EditorUtility.ClearProgressBar();

                if (ok)
                {
                    Debug.Log("[MapGen] 생성 완료\n" + report);
                    EditorUtility.DisplayDialog("맵 생성 완료", report, "확인");
                }
                else
                {
                    Debug.LogError("[MapGen] 생성 실패\n" + report);
                    EditorUtility.DisplayDialog("맵 생성 실패", report, "확인");
                }
            }
            catch (System.Exception e)
            {
                EditorUtility.ClearProgressBar();
                Debug.LogException(e);
                EditorUtility.DisplayDialog("맵 생성 실패", e.Message, "확인");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
