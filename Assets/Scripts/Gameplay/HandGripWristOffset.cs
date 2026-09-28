using UnityEngine;
using Oculus.Interaction.HandGrab;
using Oculus.Interaction.Input;

/// <summary>
/// Seats this object's hand-grab poses in wrist space.
///
/// The Interaction SDK only tells the two hands apart through a pose's
/// <see cref="HandPose.Handedness"/>, which is what makes the axe usable in either
/// hand. But supplying a HandPose also switches the SDK's alignment from the grab
/// point to the WRIST (<c>IHandGrabState.GetTargetGrabPose</c> offsets by
/// <c>WristToGrabPoseOffset</c>), so the pose has to sit the wrist-to-palm distance
/// behind the grip - otherwise the axe floats beside the hand.
///
/// That distance is live hand data (it is zero until the tracked hands are up), so it
/// cannot be baked into the prefab. This component measures it once at start-up and
/// moves each pose from grip space into wrist space accordingly.
/// </summary>
[DisallowMultipleComponent]
public class HandGripWristOffset : MonoBehaviour
{
    [SerializeField] Transform m_RightGrip;
    [SerializeField] HandGrabPose m_RightPose;
    [SerializeField] Transform m_LeftGrip;
    [SerializeField] HandGrabPose m_LeftPose;

    bool m_Applied;

    void Update()
    {
        if (m_Applied)
            return;

        bool right = Apply(Handedness.Right, m_RightGrip, m_RightPose);
        bool left = Apply(Handedness.Left, m_LeftGrip, m_LeftPose);
        if (right && left)
            m_Applied = true;
    }

    bool Apply(Handedness handedness, Transform grip, HandGrabPose pose)
    {
        if (grip == null || pose == null || pose.RelativeTo == null)
            return false;

        HandGrabInteractor interactor = null;
        foreach (var candidate in Object.FindObjectsByType<HandGrabInteractor>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            // Skip the controller flavour; only the bare hand interactor counts.
            if (candidate.GetType().Name != "HandGrabInteractor")
                continue;
            if (candidate.Hand != null && candidate.Hand.Handedness == handedness)
            {
                interactor = candidate;
                break;
            }
        }

        if (interactor == null || interactor.Hand == null || interactor.PalmPoint == null)
            return false;
        if (!interactor.Hand.GetRootPose(out Pose wrist))
            return false;

        // wrist -> palm offset, in the wrist's frame.
        Pose palm = new Pose(interactor.PalmPoint.position, interactor.PalmPoint.rotation);
        Quaternion invWrist = Quaternion.Inverse(wrist.rotation);
        Pose wristToPalm = new Pose(invWrist * (palm.position - wrist.position), invWrist * palm.rotation);

        // inverse of that offset.
        Quaternion invOffset = Quaternion.Inverse(wristToPalm.rotation);
        Pose offsetInverse = new Pose(invOffset * -wristToPalm.position, invOffset);

        Transform reference = pose.RelativeTo;
        Quaternion invReference = Quaternion.Inverse(reference.rotation);

        // grip, expressed relative to the object (the pose's reference transform).
        Pose gripRelative = new Pose(
            invReference * (grip.position - reference.position),
            invReference * grip.rotation);

        // The pose must become grip * inverse(offset) so the SDK lands the grip in the palm.
        Pose target = new Pose(
            gripRelative.position + gripRelative.rotation * offsetInverse.position,
            gripRelative.rotation * offsetInverse.rotation);

        pose.transform.SetPositionAndRotation(
            reference.TransformPoint(target.position),
            reference.rotation * target.rotation);
        return true;
    }
}
