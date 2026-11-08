using UnityEngine;

namespace VintageJournal
{
    /// <summary>A reserved slot must be pulled through this entrance before lifting or rotating.</summary>
    [DisallowMultipleComponent]
    public sealed class PhysicalStorageAccessPath : MonoBehaviour
    {
        public Vector3 localExitOffset = new Vector3(0, 0, -.20f);

        public Pose ExitPose(Pose slotPose)
        {
            return new Pose(slotPose.position + transform.TransformVector(localExitOffset), slotPose.rotation);
        }

        public static PhysicalStorageAccessPath ForSlot(PhysicalStorageSlot slot)
        {
            return slot && slot.container ? slot.container.GetComponentInParent<PhysicalStorageAccessPath>() : null;
        }
    }
}
