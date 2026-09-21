using UnityEngine;

/// <summary>
/// Shared walking logic for the skeleton enemies. The straight line to the
/// target runs through the camp clutter (table, weapon rack, log pile), and a
/// naive "nudge sideways when something is ahead" steer can either circle the
/// same prop forever or cancel its own forward motion out and shuffle in place,
/// which reads as being stuck on whatever it is standing next to.
///
/// Two safeguards take care of that. The steer commits to the side with more
/// room by probing two whiskers either side of the enemy instead of dodging
/// away from the surface it hit (the far side of one prop is often another
/// prop), and a stall watchdog slides the enemy sideways whenever it is walking
/// but not actually covering ground - the net that catches corners and steering
/// loops alike.
/// </summary>
public static class EnemySteering
{
    /// <summary>Per-enemy steering memory; each walker owns one of these.</summary>
    public struct State
    {
        public Vector3 Detour;
        public float DetourUntil;
        public Vector3 LastPosition;
        public float ProgressTimer;
        public bool Started;
    }

    const float k_ProbeDistance = 1.5f;
    const float k_ProbeRadius = 0.35f;
    const float k_ProbeHeight = 1f;
    const float k_WhiskerAngle = 40f;
    const float k_ProgressWindow = 0.5f;
    const float k_ProgressRatio = 0.4f;
    const float k_DetourDuration = 0.7f;

    static readonly RaycastHit[] s_Hits = new RaycastHit[8];

    /// <summary>
    /// Turns the direction the enemy wants to walk in into the direction it
    /// should actually move this frame. <paramref name="steer"/> is how far it
    /// commits sideways when something is in the way (0 turns avoidance off).
    /// </summary>
    public static Vector3 Resolve(Transform self, Vector3 desired, float speed, float steer, ref State state)
    {
        desired.y = 0f;
        if (desired.sqrMagnitude <= 0.0001f)
            return desired;

        desired.Normalize();
        WatchForStall(self, desired, speed, ref state);

        // A sideways escape is already underway: see it through rather than
        // letting the forward steer drag the enemy back into what it left.
        if (Time.time < state.DetourUntil)
            return state.Detour;

        Vector3 origin = self.position + Vector3.up * k_ProbeHeight;
        if (!Probe(origin, desired, self))
            return desired;

        float left = Room(origin, Quaternion.AngleAxis(-k_WhiskerAngle, Vector3.up) * desired, self);
        float right = Room(origin, Quaternion.AngleAxis(k_WhiskerAngle, Vector3.up) * desired, self);
        Vector3 side = Quaternion.AngleAxis(left >= right ? -90f : 90f, Vector3.up) * desired;
        return (desired + side * steer).normalized;
    }

    /// <summary>
    /// Walking but not getting anywhere means the enemy is wedged on something,
    /// so commit to a sideways slide for a moment. This is the safety net
    /// behind the whiskers: it also covers two steers cancelling each other
    /// out, where no single frame looks blocked but the enemy never advances.
    /// </summary>
    static void WatchForStall(Transform self, Vector3 desired, float speed, ref State state)
    {
        if (!state.Started)
        {
            state.Started = true;
            state.LastPosition = self.position;
            state.ProgressTimer = 0f;
            return;
        }

        state.ProgressTimer += Time.deltaTime;
        if (state.ProgressTimer < k_ProgressWindow)
            return;

        float travelled = Vector3.Distance(self.position, state.LastPosition);
        if (travelled < speed * k_ProgressWindow * k_ProgressRatio)
            StartDetour(self, desired, ref state);

        state.LastPosition = self.position;
        state.ProgressTimer = 0f;
    }

    static void StartDetour(Transform self, Vector3 desired, ref State state)
    {
        Vector3 origin = self.position + Vector3.up * k_ProbeHeight;
        float left = Room(origin, Quaternion.AngleAxis(-90f, Vector3.up) * desired, self);
        float right = Room(origin, Quaternion.AngleAxis(90f, Vector3.up) * desired, self);

        state.Detour = Quaternion.AngleAxis(left >= right ? -90f : 90f, Vector3.up) * desired;
        state.DetourUntil = Time.time + k_DetourDuration;
    }

    static float Room(Vector3 origin, Vector3 direction, Transform self)
        => Probe(origin, direction, self, out RaycastHit hit) ? hit.distance : k_ProbeDistance;

    static bool Probe(Vector3 origin, Vector3 direction, Transform self)
        => Probe(origin, direction, self, out _);

    static bool Probe(Vector3 origin, Vector3 direction, Transform self, out RaycastHit hit)
    {
        hit = default;

        int count = Physics.SphereCastNonAlloc(origin, k_ProbeRadius, direction, s_Hits,
            k_ProbeDistance, ~0, QueryTriggerInteraction.Ignore);

        bool found = false;
        float nearest = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            var candidate = s_Hits[i];
            if (candidate.collider == null || candidate.distance >= nearest)
                continue;
            if (candidate.collider.transform.IsChildOf(self) || ShouldIgnore(candidate.collider))
                continue;

            nearest = candidate.distance;
            hit = candidate;
            found = true;
        }

        return found;
    }

    /// <summary>
    /// The campfire, the player and the other enemies are what the walker is
    /// heading for, not obstacles: treating them as scenery to be dodged is
    /// what left it circling in place.
    /// </summary>
    static bool ShouldIgnore(Collider other)
        => other.GetComponentInParent<Skeleton>() != null
            || other.GetComponentInParent<BoneThrower>() != null
            || other.GetComponentInParent<CampfireFuel>() != null
            || other.GetComponentInParent<PlayerHealth>() != null;
}
