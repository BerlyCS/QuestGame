using UnityEngine;

/// <summary>
/// Hides the parts of the camp that aren't meant to be seen at the start: only the
/// fire (a faint, permanent ember) and the log pile are visible. The moment the
/// player grabs and throws the first log into the fire, it flares up and this
/// reveals everything else at once. Listens for the fire's first fuel change
/// rather than "ignited", since the ember never actually goes out for it to
/// ignite from.
/// </summary>
[DisallowMultipleComponent]
public class CampRevealController : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Fire whose first feeding triggers the reveal.")]
    CampfireFuel m_Campfire;

    [SerializeField]
    [Tooltip("Objects hidden at scene start and revealed the first time the fire is fed.")]
    GameObject[] m_ObjectsToReveal;

    void Awake()
    {
        SetVisible(false);

        if (m_Campfire != null)
            m_Campfire.OnFuelChanged.AddListener(HandleFirstFed);
    }

    void OnDestroy()
    {
        if (m_Campfire != null)
            m_Campfire.OnFuelChanged.RemoveListener(HandleFirstFed);
    }

    void HandleFirstFed()
    {
        SetVisible(true);
        m_Campfire.OnFuelChanged.RemoveListener(HandleFirstFed);
    }

    void SetVisible(bool visible)
    {
        foreach (var go in m_ObjectsToReveal)
            if (go != null)
                go.SetActive(visible);
    }
}
