using UnityEngine;

/// <summary>
/// Destroys its GameObject after <see cref="Lifetime"/> seconds. Used for
/// one-shot effects (death particles and sound) that are spawned as standalone
/// objects so they survive the enemy that spawned them being destroyed.
/// </summary>
[DisallowMultipleComponent]
public class AutoDestroyAfter : MonoBehaviour
{
    public float Lifetime = 2f;

    float m_Elapsed;

    void Update()
    {
        m_Elapsed += Time.deltaTime;
        if (m_Elapsed >= Lifetime)
            Destroy(gameObject);
    }
}
