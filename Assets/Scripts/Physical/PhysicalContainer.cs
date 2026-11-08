using UnityEngine;

namespace VintageJournal
{
    [DisallowMultipleComponent]
    public sealed class PhysicalContainer : MonoBehaviour
    {
        public string displayName = "收纳盒";
        public bool isOpen = true;
        public bool contentsVisibleWhenClosed;
        public Transform lid;
        public Transform foldingLid;
        public bool canClose = true;
        public Vector3 closedLidEuler;
        public Vector3 openLidEuler = new Vector3(-105, 0, 0);
        public Vector3 closedFoldEuler;
        public Vector3 openFoldEuler = new Vector3(180, 0, 0);
        public bool IsAccessibleHierarchy
        {
            get
            {
                foreach (var enclosing in GetComponentsInParent<PhysicalContainer>(true))
                    if (!enclosing.isOpen) return false;
                return true;
            }
        }
        public bool ContentsVisibleHierarchy
        {
            get
            {
                foreach (var enclosing in GetComponentsInParent<PhysicalContainer>(true))
                    if (!enclosing.isOpen && !enclosing.contentsVisibleWhenClosed) return false;
                return true;
            }
        }
        public bool ToggleOpen() => SetOpen(!isOpen);
        public bool SetOpen(bool value)
        {
            if (!value)
            {
                if (!canClose) return false;
                foreach (var slot in GetComponentsInChildren<PhysicalStorageSlot>(true))
                {
                    var item = slot.ReservedItem;
                    if (item && (item.CurrentState == PhysicalItemState.Grabbed || item.CurrentState == PhysicalItemState.Moving ||
                        item.CurrentState == PhysicalItemState.Returning || item.CurrentState == PhysicalItemState.Processing)) return false;
                }
            }
            isOpen = value; Refresh();
            Debug.Log("[Physical] Container:" + displayName + ":" + (value ? "Open" : "Closed"));
            return true;
        }
        public void Refresh()
        {
            if (lid) lid.localRotation = Quaternion.Euler(isOpen ? openLidEuler : closedLidEuler);
            if (foldingLid) foldingLid.localRotation = Quaternion.Euler(isOpen ? openFoldEuler : closedFoldEuler);
            foreach (var slot in GetComponentsInChildren<PhysicalStorageSlot>(true))
                if (slot.Occupant) slot.Occupant.RefreshVisibility();
        }
        void Start() { Refresh(); }
    }
}
