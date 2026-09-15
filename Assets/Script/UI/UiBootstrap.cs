using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

/// <summary>런타임에 UI 를 만들 때 공통으로 필요한 것들.</summary>
public static class UiBootstrap
{
    /// <summary>버튼을 누르려면 EventSystem 이 있어야 한다. 씬에 없으면 만들어 붙인다.</summary>
    public static void EnsureEventSystem()
    {
        if (EventSystem.current != null)
            return;

        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        go.AddComponent<InputSystemUIInputModule>();
    }
}
