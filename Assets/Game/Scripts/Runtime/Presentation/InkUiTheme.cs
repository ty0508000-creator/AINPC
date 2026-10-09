using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>편집 시 적용하는 먹색 UI 팔레트. 실행 중 배치를 다시 만들지 않는다.</summary>
public static class InkUiTheme
{
    public static readonly Color Surface = new Color(0.065f, 0.085f, 0.09f, 0.97f);
    public static readonly Color Card = new Color(0.105f, 0.135f, 0.14f, 0.96f);
    public static readonly Color Selected = new Color(0.16f, 0.31f, 0.28f);
    public static readonly Color Ivory = new Color(0.92f, 0.91f, 0.85f);
    public static readonly Color Muted = new Color(0.64f, 0.70f, 0.69f);
    public static readonly Color Jade = new Color(0.48f, 0.77f, 0.65f);

    public static void Apply(GameObject root)
    {
        foreach (var image in root.GetComponentsInChildren<Image>(true))
        {
            string name = image.name;
            if (name == "Skill icon" || name == "Elder portrait" || name == "Skill demonstration") continue;
            // Preserve gauges, transparent layout containers and takeover animation layers.
            if (name == "Fill" || image.color.a == 0f || image.GetComponentInParent<TakeoverEffect>() != null ||
                name == "Black" || name.Contains("Glitch") || name.Contains("Tint")) continue;
            var button = image.GetComponent<Button>();
            bool overlay = name.Contains("overlay") || name.Contains("Overlay");
            image.sprite = null;
            image.type = Image.Type.Simple;
            image.color = overlay ? new Color(0.02f, 0.035f, 0.04f, 0.82f) :
                name == "Gauge" ? new Color(0.20f, 0.25f, 0.25f) :
                name == "Divider" || name == "Rule" || name == "Prerequisite" ? new Color(0.33f, 0.43f, 0.40f, 0.65f) :
                button != null ? Card : Surface;
            if (name == "Divider" || name == "Rule")
                image.rectTransform.sizeDelta = new Vector2(image.rectTransform.sizeDelta.x, 1f);
            if (button != null)
            {
                var colors = button.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1.35f, 1.5f, 1.4f);
                colors.pressedColor = new Color(0.7f, 1.1f, 0.95f);
                colors.selectedColor = colors.highlightedColor;
                colors.disabledColor = new Color(0.55f, 0.6f, 0.6f, 0.65f);
                colors.fadeDuration = 0.12f;
                button.colors = colors;
                // A single understated edge replaces the thick wooden artwork.
                var edge = image.GetComponent<Outline>();
                if (edge == null) edge = image.gameObject.AddComponent<Outline>();
                edge.effectDistance = new Vector2(1f, -1f);
                edge.effectColor = new Color(0.36f, 0.52f, 0.46f, 0.5f);
                edge.useGraphicAlpha = true;
            }
        }
        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            if (text.color.a == 0f) continue;
            text.color = text.fontSize >= 20f || text.GetComponentInParent<Button>() != null ? Ivory : Muted;
            text.characterSpacing = text.fontSize >= 24f ? 3f : 0f;
            text.lineSpacing = 8f;
        }
    }
}
