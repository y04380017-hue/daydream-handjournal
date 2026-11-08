using UnityEngine;

namespace VintageJournal
{
    /// <summary>Independent local storage anchor with exclusive occupancy and a stable reservation.</summary>
    [DisallowMultipleComponent]
    public sealed class PhysicalStorageSlot : MonoBehaviour
    {
        public PhysicalContainer container;
        public string slotId;
        public PhysicalItemType acceptedType;
        public float snapRadius = .04f;
        public bool permanentReservation = true;
        public WorkspaceShaft accessShaft;
        [SerializeField] PhysicalInteractable occupant;
        [SerializeField] PhysicalInteractable reservedItem;
        public PhysicalInteractable Occupant => occupant;
        public PhysicalInteractable ReservedItem => reservedItem;
        public Pose WorldPose => new Pose(transform.position, transform.rotation);
        public bool IsAccessible => (!container || container.IsAccessibleHierarchy) && (!accessShaft || accessShaft.IsExtended);

        public bool CanAccept(PhysicalInteractable item)
        {
            return item && item.itemType == acceptedType && (!occupant || occupant == item) &&
                (!reservedItem || reservedItem == item);
        }
        public bool TryReserve(PhysicalInteractable item)
        { if (!CanAccept(item)) return false; reservedItem = item; return true; }
        public bool TryStore(PhysicalInteractable item, bool restoring = false)
        {
            if ((!IsAccessible && !restoring) || !TryReserve(item)) return false;
            occupant = item; return true;
        }
        public void Vacate(PhysicalInteractable item)
        { if (occupant == item) occupant = null; if(!permanentReservation&&reservedItem==item)reservedItem=null; }
        public bool ReleaseReservation(PhysicalInteractable item)
        {
            if (reservedItem != item || occupant == item) return false;
            reservedItem = null; return true;
        }
    }
}
