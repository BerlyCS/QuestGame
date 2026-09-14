using UnityEngine;

/// <summary>
/// Placeholder for the shield mechanic. Holds a block radius so future
/// blocking/deflection logic has something to work with. No gameplay
/// behaviour is implemented yet.
/// </summary>
[DisallowMultipleComponent]
public class Shield : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Radius used by future block detection.")]
    float m_BlockRadius = 0.45f;

    public float BlockRadius => m_BlockRadius;

    public bool IsBlocking { get; set; }
}
