using System;
using UnityEngine;

namespace VintageJournal
{
    /// <summary>Stable entity identity and state. This component has no input or art-specific dependencies.</summary>
    [DisallowMultipleComponent]
    public sealed class PhysicalInteractable : MonoBehaviour
    {
        public string itemInstanceId;
        public string assetId;
        public string displayName = "实体物品";
        public PhysicalItemType itemType;
        public PhysicalDimensionProfile dimensions;
        public Transform grabAnchor, homeAnchor, inspectAnchor, placementAnchor, visualRoot;
        public BoxCollider interactionCollider;
        public PhysicalStorageSlot homeSlot;
        public PhysicalOperations allowedOperations = PhysicalOperations.All;
        [SerializeField] PhysicalItemState currentState = PhysicalItemState.Available;
        [SerializeField] PhysicalStorageSlot currentSlot;
        PhysicalVisualBinding visual;
        bool registered;
        public PhysicalItemState CurrentState => currentState;
        public PhysicalContainer CurrentContainer => currentSlot ? currentSlot.container : null;
        public PhysicalStorageSlot CurrentSlot => currentSlot;
        public Pose CurrentWorldPose => new Pose(transform.position, transform.rotation);
        public Pose GrabWorldPose
        {
            get
            {
                Transform anchor = grabAnchor ? grabAnchor : transform;
                return new Pose(anchor.TransformPoint(dimensions ? dimensions.grabOffset : Vector3.zero), anchor.rotation);
            }
        }
        public Vector3 HalfExtents => (dimensions && dimensions.IsValid ? dimensions.colliderSize : new Vector3(.04f, .01f, .04f)) * .5f;
        public bool IsAccessible => currentState != PhysicalItemState.Disabled && (!currentSlot || currentSlot.IsAccessible);

        void Awake() { Initialize(); }
        /// <summary>Explicit authoring/migration operation; ordinary grabs never change the permanent home.</summary>
        public void ConfigureHome(PhysicalStorageSlot slot)
        {
            if(currentSlot){var previous=currentSlot;previous.Vacate(this);previous.ReleaseReservation(this);}
            if(homeSlot&&homeSlot!=slot){homeSlot.Vacate(this);homeSlot.ReleaseReservation(this);}
            currentSlot=null;homeSlot=slot;homeAnchor=slot?slot.transform:null;registered=false;
            Initialize();
            if(slot)transform.SetPositionAndRotation(slot.WorldPose.position,slot.WorldPose.rotation);
        }
        public void Initialize()
        {
            if (string.IsNullOrEmpty(itemInstanceId)) itemInstanceId = Guid.NewGuid().ToString("N");
            if (!interactionCollider) interactionCollider = GetComponent<BoxCollider>();
            if (dimensions && interactionCollider) dimensions.ApplyCollider(interactionCollider);
            if (!visual) visual = GetComponent<PhysicalVisualBinding>();
            if (!visual) visual = gameObject.AddComponent<PhysicalVisualBinding>();
            visual.Rebind(visualRoot ? visualRoot : transform);
            if (!registered && homeSlot && homeSlot.TryStore(this, true))
            { currentSlot = homeSlot; registered = true; currentState = PhysicalItemState.Stored; }
            if (!homeAnchor && homeSlot) homeAnchor = homeSlot.transform;
            RefreshVisibility();
        }
        public bool Supports(PhysicalOperations operation) => (allowedOperations & operation) == operation;
        public bool CanTransitionTo(PhysicalItemState next)
        {
            if (next == currentState) return true;
            if (next == PhysicalItemState.Disabled) return true;
            switch (currentState)
            {
                case PhysicalItemState.Stored: return next == PhysicalItemState.Hovered || next == PhysicalItemState.Available || next == PhysicalItemState.Grabbed;
                case PhysicalItemState.Available: return next == PhysicalItemState.Hovered || next == PhysicalItemState.Stored || next == PhysicalItemState.Grabbed;
                case PhysicalItemState.Hovered: return next == PhysicalItemState.Stored || next == PhysicalItemState.Available || next == PhysicalItemState.Placed || next == PhysicalItemState.Grabbed;
                case PhysicalItemState.Grabbed: return next == PhysicalItemState.Moving || next == PhysicalItemState.Placed || next == PhysicalItemState.Returning || next == PhysicalItemState.Processing;
                case PhysicalItemState.Moving: return next == PhysicalItemState.Placed || next == PhysicalItemState.Returning || next == PhysicalItemState.Processing;
                case PhysicalItemState.Placed: return next == PhysicalItemState.Hovered || next == PhysicalItemState.Grabbed || next == PhysicalItemState.Returning;
                case PhysicalItemState.Returning: return next == PhysicalItemState.Stored || next == PhysicalItemState.Placed || next == PhysicalItemState.Available;
                case PhysicalItemState.Processing: return next == PhysicalItemState.Placed || next == PhysicalItemState.Stored;
                case PhysicalItemState.Disabled: return next == PhysicalItemState.Available || next == PhysicalItemState.Stored;
                default: return false;
            }
        }
        public bool TryTransition(PhysicalItemState next)
        {
            if (!CanTransitionTo(next)) return false;
            if (currentState == next) return true;
            if (next == PhysicalItemState.Stored && (!currentSlot || currentSlot.Occupant != this)) return false;
            if ((next == PhysicalItemState.Grabbed || next == PhysicalItemState.Moving || next == PhysicalItemState.Placed) && currentSlot)
            { currentSlot.Vacate(this); currentSlot = null; }
            currentState = next; RefreshVisibility();
            Debug.Log("[Physical] State:" + itemInstanceId + ":" + next);
            return true;
        }
        public Pose RootPoseForGrabPose(Pose grabPose)
        {
            Pose grip = GrabWorldPose;
            Quaternion localRotation = Quaternion.Inverse(transform.rotation) * grip.rotation;
            Quaternion rootRotation = grabPose.rotation * Quaternion.Inverse(localRotation);
            Vector3 localOffset = Quaternion.Inverse(transform.rotation) * (grip.position - transform.position);
            return new Pose(grabPose.position - rootRotation * localOffset, rootRotation);
        }
        internal void SetSlot(PhysicalStorageSlot slot)
        { currentSlot = slot; RefreshVisibility(); }
        public void SetHighlight(bool value) { if (visual) visual.SetHighlight(value); }
        public void RebindVisual(Transform replacement)
        { visualRoot = replacement; Initialize(); }
        public void RefreshVisibility()
        {
            bool visible = !CurrentContainer || CurrentContainer.ContentsVisibleHierarchy;
            if (visual && visualRoot && visualRoot != transform) visual.SetVisible(visible);
            // A spindle-mounted roll can be selected and unwound while its axle is closed;
            // withdrawing the entire roll still requires IsAccessible in the grab controller.
            bool reachable = !currentSlot || currentSlot.IsAccessible ||
                (itemType == PhysicalItemType.Tape && currentSlot.accessShaft && (!CurrentContainer || CurrentContainer.IsAccessibleHierarchy));
            if (interactionCollider) interactionCollider.enabled = reachable && currentState != PhysicalItemState.Disabled;
        }
        void OnDisable() { SetHighlight(false); }
    }
}
