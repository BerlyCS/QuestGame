using UnityEngine;

/// <summary>
/// Thin wrapper over OVRInput's simple haptics API (see "Simple haptics" in the Meta
/// Quest docs) that lets short vibration pulses overlap on each controller without one
/// pulse's auto-stop cutting a newer, longer pulse short.
/// </summary>
public static class HapticsUtility
{
    class Driver : MonoBehaviour
    {
        public float LeftStopTime;
        public float RightStopTime;

        void Update()
        {
            if (LeftStopTime > 0f && Time.unscaledTime >= LeftStopTime)
            {
                OVRInput.SetControllerVibration(1f, 0f, OVRInput.Controller.LTouch);
                LeftStopTime = 0f;
            }

            if (RightStopTime > 0f && Time.unscaledTime >= RightStopTime)
            {
                OVRInput.SetControllerVibration(1f, 0f, OVRInput.Controller.RTouch);
                RightStopTime = 0f;
            }
        }
    }

    static Driver s_Driver;

    /// <summary>
    /// Vibrates the given controller(s) at the given amplitude (0-1) for the given
    /// duration in seconds, then stops. Safe to call every frame; overlapping calls on
    /// the same controller extend the vibration rather than cutting it short.
    /// </summary>
    public static void Pulse(OVRInput.Controller controller, float amplitude, float duration)
    {
        amplitude = Mathf.Clamp01(amplitude);
        if (amplitude <= 0f || duration <= 0f || controller == OVRInput.Controller.None)
            return;

        if (s_Driver == null)
        {
            var go = new GameObject("[Haptics]");
            Object.DontDestroyOnLoad(go);
            s_Driver = go.AddComponent<Driver>();
        }

        float stopTime = Time.unscaledTime + duration;

        if ((controller & OVRInput.Controller.LTouch) != 0)
        {
            s_Driver.LeftStopTime = Mathf.Max(s_Driver.LeftStopTime, stopTime);
            OVRInput.SetControllerVibration(1f, amplitude, OVRInput.Controller.LTouch);
        }

        if ((controller & OVRInput.Controller.RTouch) != 0)
        {
            s_Driver.RightStopTime = Mathf.Max(s_Driver.RightStopTime, stopTime);
            OVRInput.SetControllerVibration(1f, amplitude, OVRInput.Controller.RTouch);
        }
    }
}
