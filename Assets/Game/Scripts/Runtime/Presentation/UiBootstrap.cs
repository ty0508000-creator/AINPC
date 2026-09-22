using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

/// <summary>런타임에 UI 를 만들 때 공통으로 필요한 것들.</summary>
public static class UiBootstrap
{
    /// <summary>버튼을 누르려면 EventSystem 이 있어야 한다. 씬에 없으면 만들어 붙인다.</summary>
    public static void EnsureEventSystem()
    {
        // current 는 재생 중에만 채워진다. 씬에 구워 넣을 때는 직접 찾아야 중복이 생기지 않는다.
        if (EventSystem.current != null ||
            Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include) != null)
            return;

        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<InputSystemUIInputModule>();
    }

    /// <summary>
    /// 씬에서 이미 쓰고 있는 글꼴을 빌려 온다.
    /// 런타임에 만드는 UI 는 인스펙터로 폰트를 받을 데가 없어서, HUD 같은 데
    /// 이미 지정된 한글 폰트를 따라가게 한다. 못 찾으면 기본 폰트.
    /// </summary>
    public static TMP_FontAsset FindSceneFont()
    {
        TMP_FontAsset fallback = TMP_Settings.defaultFontAsset;

        foreach (TMP_Text text in Object.FindObjectsByType<TMP_Text>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (text.font != null && text.font != fallback)
                return text.font;
        }

        return fallback;
    }
}
