using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 학습 데이터 생성기(Training/gen_data.py)가 게임과 글자까지 같은 시스템 프롬프트로 학습하도록, 게임 코드가 만든 프롬프트 틀을 내보낸다.
/// {{...}} 자리는 생성기가 채운다. NPC 성격은 1~4지역 씬에 실제로 놓인 값을 읽는다(씬은 추가로 열었다가 저장하지 않고 닫는다).
/// 프롬프트 코드(InnerVoiceManager·DialogueManager·VillageSocialDialogue)를 고치면 다시 내보내고 데이터를 다시 만든다.
/// </summary>
public static class TrainingPromptExport
{
    public const string OutputPath = "Training/data/prompt_templates.json";

    [MenuItem("Tools/AINPC/학습용 프롬프트 틀 내보내기")]
    public static void ExportMenu() => Debug.Log("[TrainingPromptExport] " + Export());

    public static string Export()
    {
        var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
        var inner = player.GetComponent<InnerVoiceManager>();
        int maxTurns = new SerializedObject(inner).FindProperty("maxTurns").intValue;

        // 숫자 인자는 표식 숫자로 만든 뒤 자리 표시로 바꾼다
        string Situation(bool playerControlled) => InnerVoiceManager.SituationLine(1111f, 2222f, 3333f, 4444, playerControlled)
            .Replace("1111", "{{HP}}").Replace("2222", "{{MAX_HP}}").Replace("3333", "{{MOOD}}").Replace("4444", "{{ENEMIES}}");

        var root = new JObject
        {
            ["_comment"] = "Tools/AINPC/학습용 프롬프트 틀 내보내기 로 만든 파일. 직접 고치지 말 것. {{...}} 는 gen_data.py 가 채운다.",
            // MemoryManager / MrSmithLongTermMemory.Recall 은 "- 기억" 줄을 StringBuilder.AppendLine 으로 잇는다
            ["recall_line_separator"] = Environment.NewLine,
            ["inner"] = new JObject
            {
                ["system"] = InnerVoiceManager.BuildPrompt(inner.Backstory, "{{MEMORY_BLOCK}}", "{{STORY_BLOCK}}", "{{SITUATION}}"),
                ["memory_block"] = InnerVoiceManager.MemoryBlock("{{RECALLED}}"),
                ["story_block"] = InnerVoiceManager.StoryBlock("{{STORY}}"),
                ["situation_player"] = Situation(true),
                ["situation_ai"] = Situation(false),
                ["memory_entry"] = InnerVoiceManager.MemoryEntry("{{PLAYER}}", "{{REPLY}}", 7777).Replace("+7777", "{{SIGNED_DELTA}}"),
                ["turn_line"] = InnerVoiceManager.TurnLine(5555, "{{PLAYER}}", "{{REPLY}}").Replace("5555", "{{TURN}}"),
                ["opening_query"] = InnerVoiceManager.OpeningQuery,
                ["max_turns"] = maxTurns,
            },
            ["npc"] = new JObject
            {
                ["affinity_line"] = DialogueManager.AffinityLine(1111f).Replace("1111", "{{AFFINITY}}"),
                ["turn_line"] = NPCInteraction.TurnLine("{{NAME}}", "{{PLAYER}}", "{{REPLY}}"),
                ["personas"] = ScenePersonas(),
            },
        };

        string json = JsonConvert.SerializeObject(root, Formatting.Indented).Replace("\r\n", "\n").Replace("\n", "\r\n") + "\r\n";
        string path = Path.GetFullPath(OutputPath);
        File.WriteAllText(path, json, new UTF8Encoding(false));
        return $"{path} (NPC {((JArray)root["npc"]["personas"]).Count}명)";
    }

    static JArray ScenePersonas()
    {
        var personas = new JArray();
        for (int n = 1; n <= 4; n++)
        {
            string scenePath = StageBuilder.StagePath(n);
            var scene = SceneManager.GetSceneByPath(scenePath);
            bool openedHere = !scene.isLoaded;
            if (openedHere) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                foreach (var npc in root.GetComponentsInChildren<NPCInteraction>(true))
                    personas.Add(Persona(npc, scene.name));
            }
            finally
            {
                if (openedHere) EditorSceneManager.CloseScene(scene, true);
            }
        }
        return personas;
    }

    static JObject Persona(NPCInteraction npc, string sceneName)
    {
        var actor = npc.GetComponent<HubStoryActor>();
        string actorId = actor != null ? actor.actorId : null;
        bool smith = DialogueManager.IsMrSmith(npc, npc.npcName);
        var variants = new JArray();
        // DialogueManager.OpenDialogue: personality += VillageSocialDialogue.AiContext(npc) — 1지역에서만 붙고, 소윤 선택 상태에 따라 갈린다
        var states = sceneName == "Stage1"
            ? new[] { ("none", false, false), ("rescued", true, false), ("left", false, true) }
            : new[] { ("none", false, false) };
        foreach (var (choice, helped, left) in states)
        {
            string personality = npc.personality + (sceneName == "Stage1" ? VillageSocialDialogue.AiContext(actorId, helped, left) : "");
            var v = new JObject { ["choice"] = choice };
            if (smith)
            {
                // 비어 있는 최근 대화·기억은 BuildMrSmithPrompt 가 "없음" 으로 바꾼다. 생성기도 그렇게 채운다.
                v["system"] = DialogueManager.BuildMrSmithPrompt(personality, "{{RECENT}}", "{{RELATED}}", "{{INPUT}}", "{{AFFINITY_LINE}}");
            }
            else
            {
                v["system_with_recent"] = DialogueManager.BuildNpcPrompt(npc.npcName, personality, "{{AFFINITY_LINE}}", new[] { "{{RECENT}}" });
                v["system_no_recent"] = DialogueManager.BuildNpcPrompt(npc.npcName, personality, "{{AFFINITY_LINE}}", null);
            }
            variants.Add(v);
        }
        return new JObject
        {
            ["scene"] = sceneName,
            ["name"] = npc.npcName,
            ["actor"] = actorId,
            ["smith"] = smith,
            ["variants"] = variants,
        };
    }
}
