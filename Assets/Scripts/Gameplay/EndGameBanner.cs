using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The one piece of text in the game (everything else is deliberately wordless,
/// see CLAUDE.md): a card that fills the player's view when the night is
/// survived or when their life runs out. It is built entirely at runtime - a
/// world-space canvas with a single <see cref="Text"/> using Unity's built-in
/// font - so it needs no authored UI prefab or font asset. It stays centred in
/// front of the player, billboarded, until the scene restarts.
/// </summary>
[DisallowMultipleComponent]
public class EndGameBanner : MonoBehaviour
{
    // Unity renamed the always-available font, so try both spellings.
    static readonly string[] k_FontNames = { "LegacyRuntime.ttf", "Arial.ttf" };

    const string k_WinMessage = "¡Has ganado!";
    const string k_LoseMessage = "¡Has perdido!";
    const float k_ReferencePixels = 1200f;
    static readonly Color k_WinColor = new Color(1f, 0.85f, 0.4f);
    static readonly Color k_LoseColor = new Color(0.92f, 0.16f, 0.13f);

    [Tooltip("How far in front of the player the card floats, in metres.")]
    [SerializeField] float m_Distance = 2.1f;
    [Tooltip("How wide the card is in the world, in metres.")]
    [SerializeField] float m_Width = 2.4f;
    [SerializeField] int m_FontSize = 120;

    Transform m_Head;

    /// <summary>
    /// Builds the banner in front of <paramref name="head"/> (the player's
    /// camera) and leaves it there. Safe to call with a null head - it then
    /// just sits where it was created.
    /// </summary>
    public static EndGameBanner Show(bool won, Transform head)
    {
        var go = new GameObject(won ? "End Game Banner (Won)" : "End Game Banner (Lost)");
        var banner = go.AddComponent<EndGameBanner>();
        banner.Build(won, head);
        return banner;
    }

    void Build(bool won, Transform head)
    {
        m_Head = head;

        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.dynamicPixelsPerUnit = 10f;

        var canvasRect = (RectTransform)transform;
        canvasRect.sizeDelta = new Vector2(k_ReferencePixels, k_ReferencePixels * 0.29f);
        transform.localScale = Vector3.one * (m_Width / k_ReferencePixels);

        var textGo = new GameObject("Message");
        textGo.transform.SetParent(transform, false);

        var text = textGo.AddComponent<Text>();
        text.font = FindBuiltinFont();
        text.text = won ? k_WinMessage : k_LoseMessage;
        text.color = won ? k_WinColor : k_LoseColor;
        text.fontSize = m_FontSize;
        text.fontStyle = FontStyle.Bold;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;

        if (text.font == null)
            Debug.LogWarning("[EndGameBanner] No built-in font available; the end-of-night message will not draw.");

        var textRect = (RectTransform)textGo.transform;
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        Place();
    }

    void LateUpdate()
    {
        Place();
    }

    /// <summary>Keeps the card centred in front of the player and facing them.</summary>
    void Place()
    {
        if (m_Head == null)
            return;

        Vector3 forward = m_Head.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        transform.position = m_Head.position + forward * m_Distance;
        transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
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
