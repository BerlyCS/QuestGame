using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// The storytelling beat that follows the first shot: the gold and the chest
/// drop in beside the campfire, as if the night had been waiting to be paid.
///
/// Deliberately decorative and deterministic. Each piece is authored in its
/// resting pose in the scene; this remembers that pose and replays it as a
/// short, staggered fall with a small bounce, so the beat lands the same way
/// every run and nothing can tumble into the fire or drift into the dark. No
/// rigidbodies, and the pieces are not grabbable.
///
/// The pieces start inactive and this turns them on, so before the reveal the
/// camp reads exactly as it always did.
/// </summary>
[DisallowMultipleComponent]
public class TreasureReveal : MonoBehaviour
{
    [Serializable]
    public class Piece
    {
        [Tooltip("The piece. Its authored pose in the scene is the resting pose.")]
        public Transform m_Transform;
        [Tooltip("How high above its resting pose the piece starts, in metres.")]
        public float m_DropHeight = 1.6f;
        [Tooltip("Seconds after the reveal before this piece starts to fall.")]
        public float m_Delay;
        [Tooltip("How far the piece sinks past its resting pose before settling, in metres.")]
        public float m_Dip = 0.06f;
    }

    [Header("Pieces")]
    [SerializeField] Piece[] m_Pieces = Array.Empty<Piece>();

    [Header("Fall")]
    [Tooltip("Seconds a piece takes to reach the ground.")]
    [SerializeField] float m_FallDuration = 0.5f;
    [Tooltip("Seconds spent settling back up out of the dip.")]
    [SerializeField] float m_SettleDuration = 0.22f;

    [Header("Sound")]
    [Tooltip("Optional landing sound. Falls back to the procedural coin spill.")]
    [SerializeField] AudioClip m_LandSfx;
    [Range(0f, 1f)]
    [SerializeField] float m_LandSfxVolume = 0.8f;

    bool m_Revealed;

    /// <summary>True once the treasure has been dropped.</summary>
    public bool HasRevealed => m_Revealed;

    /// <summary>Turns the pieces on and drops them into place. Idempotent.</summary>
    public void Reveal()
    {
        if (m_Revealed)
            return;
        m_Revealed = true;

        var restPoses = new Pose[m_Pieces.Length];
        for (int i = 0; i < m_Pieces.Length; i++)
        {
            if (m_Pieces[i].m_Transform == null)
                continue;

            restPoses[i] = new Pose(m_Pieces[i].m_Transform.localPosition, m_Pieces[i].m_Transform.localRotation);
            m_Pieces[i].m_Transform.gameObject.SetActive(true);
            m_Pieces[i].m_Transform.localPosition = restPoses[i].position + Vector3.up * m_Pieces[i].m_DropHeight;
        }

        StartCoroutine(DropAll(restPoses));
    }

    IEnumerator DropAll(Pose[] restPoses)
    {
        float longest = 0f;
        for (int i = 0; i < m_Pieces.Length; i++)
        {
            if (m_Pieces[i].m_Transform == null)
                continue;

            longest = Mathf.Max(longest, m_Pieces[i].m_Delay + m_FallDuration + m_SettleDuration);
            StartCoroutine(DropPiece(m_Pieces[i], restPoses[i]));
        }

        yield return new WaitForSeconds(longest);

        // Land the sound once, at the first piece, rather than a clink per coin.
        var anchor = FirstPieceTransform();
        if (anchor != null)
        {
            var clip = m_LandSfx != null ? m_LandSfx : ProceduralSfx.TreasureLand;
            ProceduralSfx.PlayAt(clip, anchor.position, m_LandSfxVolume);
        }
    }

    IEnumerator DropPiece(Piece piece, Pose rest)
    {
        Transform pieceTransform = piece.m_Transform;
        if (pieceTransform == null)
            yield break;

        if (piece.m_Delay > 0f)
            yield return new WaitForSeconds(piece.m_Delay);

        Vector3 start = rest.position + Vector3.up * piece.m_DropHeight;

        float elapsed = 0f;
        while (elapsed < m_FallDuration)
        {
            elapsed += Time.deltaTime;
            float n = Mathf.Clamp01(elapsed / m_FallDuration);
            // Accelerating fall: starts slow, lands hard.
            pieceTransform.localPosition = Vector3.LerpUnclamped(start, rest.position, n * n);
            yield return null;
        }

        elapsed = 0f;
        Vector3 dip = rest.position - Vector3.up * piece.m_Dip;
        while (elapsed < m_SettleDuration)
        {
            elapsed += Time.deltaTime;
            float n = Mathf.Clamp01(elapsed / m_SettleDuration);
            pieceTransform.localPosition = Vector3.LerpUnclamped(dip, rest.position, n);
            yield return null;
        }

        pieceTransform.localPosition = rest.position;
        pieceTransform.localRotation = rest.rotation;
    }

    Transform FirstPieceTransform()
    {
        for (int i = 0; i < m_Pieces.Length; i++)
        {
            if (m_Pieces[i].m_Transform != null)
                return m_Pieces[i].m_Transform;
        }

        return null;
    }

    /// <summary>Injection helpers for the scene builder / editor tooling.</summary>
    public void InjectPieces(Piece[] pieces) => m_Pieces = pieces;
    public void InjectLandSfx(AudioClip clip) => m_LandSfx = clip;
}
