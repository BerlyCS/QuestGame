using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the game's world-space text at runtime: a world-space canvas with a
/// single <see cref="Text"/> using Unity's built-in font, so a label needs no
/// authored UI prefab and no font asset. This is the same technique
/// <see cref="EndGameBanner"/> uses for the end-of-night card, factored out so
/// other world labels (see <see cref="GameStartTarget"/>) can reuse it.
///
/// The returned object is built at the origin, unrotated: the caller places and
/// orients it. A world-space canvas is readable from the side its forward
/// points away from, so a billboard should face <c>(label - viewer)</c>.
/// </summary>
public static class WorldText
{
    // Unity renamed the always-available font, so try both spellings.
    static readonly string[] k_FontNames = { "LegacyRuntime.ttf", "Arial.ttf" };

    const float k_ReferencePixels = 1200f;

    /// <summary>
    /// Creates a label showing <paramref name="message"/>. <paramref name="width"/>
    /// is how wide the text is in the world, in metres.
    /// </summary>
    public static GameObject Create(string message, Color color, float width, int fontSize = 120)
    {
        var go = new GameObject("Label: " + message);

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;

        var canvasRect = (RectTransform)go.transform;
        canvasRect.sizeDelta = new Vector2(k_ReferencePixels, k_ReferencePixels * 0.29f);
        go.transform.localScale = Vector3.one * (width / k_ReferencePixels);

        var textGo = new GameObject("Message");
        textGo.transform.SetParent(go.transform, false);

        var text = textGo.AddComponent<Text>();
        text.font = FindBuiltinFont();
        text.text = message;
        text.color = color;
        text.fontSize = fontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;

        if (text.font == null)
            Debug.LogWarning("[WorldText] No built-in font available; the label will not draw.");

        var textRect = (RectTransform)textGo.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        return go;
    }

    static Font FindBuiltinFont()
    {
        foreach (var name in k_FontNames)
        {
            var font = Resources.GetBuiltinResource<Font>(name);
            if (font != null)
                return font;
        }

        return null;
    }
}
