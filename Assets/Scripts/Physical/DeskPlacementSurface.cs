using UnityEngine;

namespace VintageJournal
{
    /// <summary>Metric tabletop frame. X/Z are desktop coordinates; Y=0 is its upper surface.</summary>
    [DisallowMultipleComponent]
    public sealed class DeskPlacementSurface : MonoBehaviour
    {
        public Vector2 size = new Vector2(1.3f, .65f);
        public float liftHeight = .06f;
        public LayerMask obstacleMask = ~0;
        public Collider[] ignoredColliders = new Collider[0];
        public float clearance = .0005f;
        public Collider LastBlockingCollider {get;private set;}
        public Vector3 ToLocal(Vector3 world) => transform.InverseTransformPoint(world);
        public Vector3 ToWorld(Vector3 local) => transform.TransformPoint(local);

        public bool TryRaycast(Ray ray, out Vector3 world)
        {
            world = default;
            var plane = new Plane(transform.up, transform.position);
            if (!plane.Raycast(ray, out float distance) || distance < 0 || !Finite(distance)) return false;
            world = ray.GetPoint(distance); return true;
        }
        public Vector3 ClampPosition(PhysicalInteractable item, Vector3 position)
        {
            Quaternion rotation = item && item.itemType == PhysicalItemType.LoosePage ? Quaternion.identity : item.transform.rotation;
            return ClampPosition(item, position, rotation);
        }
        public Vector3 ClampPosition(PhysicalInteractable item, Vector3 position, Quaternion rotation)
        {
            if (!item || !Finite(position)) return item ? item.transform.position : transform.position;
            LocalBounds(item, rotation, out Vector3 extents, out Vector3 offset);
            Vector3 p = ToLocal(position);
            float limitX = Mathf.Max(0, size.x * .5f - extents.x);
            float limitZ = Mathf.Max(0, size.y * .5f - extents.z);
            p.x = Mathf.Clamp(p.x + offset.x, -limitX, limitX) - offset.x;
            p.z = Mathf.Clamp(p.z + offset.z, -limitZ, limitZ) - offset.z;
            p.y = extents.y - offset.y + clearance;
            return ToWorld(p);
        }
        public Pose RestingPose(PhysicalInteractable item, Vector3 position, Quaternion rotation)
        {
            LocalBounds(item, rotation, out Vector3 extents, out Vector3 offset);
            Vector3 p = ToLocal(position); p.y = extents.y - offset.y + clearance;
            return new Pose(ToWorld(p), rotation);
        }
        public bool IsWithinBounds(PhysicalInteractable item, Pose pose)
        {
            if (!item || !Finite(pose.position) || !Finite(pose.rotation)) return false;
            LocalBounds(item, pose.rotation, out Vector3 extents, out Vector3 offset);
            Vector3 p = ToLocal(pose.position) + offset;
            return Mathf.Abs(p.x) + extents.x <= size.x * .5f + .00001f &&
                Mathf.Abs(p.z) + extents.z <= size.y * .5f + .00001f;
        }
        public bool IsValidPlacement(PhysicalInteractable item, Pose pose)
        {
            if (!IsWithinBounds(item, pose)) return false;
            Pose resting = RestingPose(item, pose.position, pose.rotation);
            if (Vector3.Distance(resting.position, pose.position) > .03f) return false;
            if (HasCollision(item, resting.position, resting.rotation, false)) return false;
            // A thin paper can fit below a raised tray collider without overlapping it, yet the
            // hand cannot lower it through that tray. Validate the actual vertical approach too.
            Vector3 approach = resting.position;
            approach.y = TransitHeight(item, resting.position, resting.position);
            if (HasCollision(item, approach, resting.rotation, false)) return false;
            Vector3 reached = Sweep(item, approach, resting.position, resting.rotation, false);
            return Vector3.Distance(reached, resting.position) <= .0001f;
        }
        public bool HasCollision(PhysicalInteractable item, Vector3 position, Quaternion rotation, bool leavingHome)
        {
            if (!item) return true;
            Physics.SyncTransforms();
            Vector3 half = Vector3.Max(item.HalfExtents - Vector3.one * clearance, Vector3.one * .0001f);
            Vector3 center = position + rotation * ColliderOffset(item);
            foreach (Collider other in Physics.OverlapBox(center, half, rotation, obstacleMask, QueryTriggerInteraction.Ignore))
                if (IsBlocking(other, item, leavingHome)) return true;
            return false;
        }
        public Vector3 Sweep(PhysicalInteractable item, Vector3 from, Vector3 to, Quaternion rotation, bool leavingHome, PhysicalContainer allowedContainer=null,PhysicalContainer sourceContainer=null)
        {
            Physics.SyncTransforms();
            Vector3 delta = to - from; float distance = delta.magnitude;
            if (distance < .000001f) return to;
            Vector3 half = Vector3.Max(item.HalfExtents - Vector3.one * clearance, Vector3.one * .0001f);
            Vector3 center = from + rotation * ColliderOffset(item);
            float permitted = distance;
            LastBlockingCollider=null;
            foreach (var hit in Physics.BoxCastAll(center, half, delta / distance, rotation, distance, obstacleMask, QueryTriggerInteraction.Ignore))
                if (IsBlocking(hit.collider, item, leavingHome) && !BelongsToAccessibleStockContainer(hit.collider,item,allowedContainer) &&
                    !BelongsToAccessibleStockContainer(hit.collider,item,sourceContainer))
                {float allowed=Mathf.Max(0,hit.distance-clearance);if(allowed<permitted){permitted=allowed;LastBlockingCollider=hit.collider;}}
            return from + delta / distance * permitted;
        }
        static bool BelongsToAccessibleStockContainer(Collider collider,PhysicalInteractable moving,PhysicalContainer container)
        {
            if(!collider||!moving||!container||!container.IsAccessibleHierarchy)return false;
            var stored=collider.GetComponentInParent<PhysicalInteractable>();
            if(!stored)return collider.transform.IsChildOf(container.transform); // The explicitly allowed source/destination fixture.
            // Stock views are independent scene roots. Art parenting cannot identify the sheets in a stack.
            // A catalog extraction/return may pass its own box's stored layers, but never a held or desktop object.
            var slot=stored.CurrentSlot;
            if(stored==moving||stored.itemInstanceId==moving.itemInstanceId||!slot||
                !slot.IsAccessible||slot.Occupant!=stored||slot.ReservedItem!=stored||string.IsNullOrEmpty(slot.slotId)||
                (stored.CurrentState!=PhysicalItemState.Stored&&stored.CurrentState!=PhysicalItemState.Hovered))return false;
            PhysicalContainer scope=container;
            bool stackedSibling=slot.container!=container;
            if(stackedSibling)
            {
                // Compact drawers contain stacked theme trays. Only overlapping paper layers in that
                // same open drawer form one extraction stack; adjacent stock and every external object remain solid.
                bool thin=moving.itemType==PhysicalItemType.LoosePage||moving.itemType==PhysicalItemType.Sticker||moving.itemType==PhysicalItemType.Note;
                var drawer=container.GetComponentInParent<WorkspaceDrawer>();
                if(!thin||stored.itemType!=moving.itemType||!drawer||!drawer.container||!drawer.container.IsAccessibleHierarchy||
                    slot.GetComponentInParent<WorkspaceDrawer>()!=drawer)return false;
                scope=drawer.container;
            }
            int movingClaims=0,storedClaims=0;
            PhysicalStorageSlot movingSlot=null;
            foreach(var candidate in scope.GetComponentsInChildren<PhysicalStorageSlot>(true))
            {
                if((!stackedSibling&&candidate.container!=container)||!candidate.IsAccessible)continue;
                if(candidate.ReservedItem==moving&&!string.IsNullOrEmpty(candidate.slotId)){movingClaims++;movingSlot=candidate;}
                if(candidate.Occupant==stored)storedClaims++;
            }
            // Both the departing/returning item and its peer must have exclusive addresses in this very container.
            if(movingClaims!=1||storedClaims!=1||!movingSlot||movingSlot.container!=container)return false;
            if(!stackedSibling)return true;
            Quaternion rotation=movingSlot.WorldPose.rotation;
            Vector3 half=moving.HalfExtents;
            Vector3 extents=Abs(rotation*Vector3.right)*half.x+Abs(rotation*Vector3.up)*half.y+Abs(rotation*Vector3.forward)*half.z;
            Vector3 centre=movingSlot.WorldPose.position+rotation*ColliderOffset(moving);
            Bounds peer=collider.bounds;
            return peer.min.x<centre.x+extents.x&&peer.max.x>centre.x-extents.x&&
                peer.min.z<centre.z+extents.z&&peer.max.z>centre.z-extents.z;
        }
        bool IsBlocking(Collider other, PhysicalInteractable item, bool leavingHome)
        {
            if (!other || !other.enabled || other.isTrigger || other.transform.IsChildOf(item.transform)) return false;
            if (other.transform == transform) return false;
            foreach (Collider ignored in ignoredColliders) if (other == ignored) return false;
            if (leavingHome && item.homeSlot && item.homeSlot.container && other.transform.IsChildOf(item.homeSlot.container.transform)) return false;
            return true;
        }
        public float TransitHeight(PhysicalInteractable item, Vector3 from, Vector3 to)
        {
            // The raised corridor clears furniture without depending on mesh names.
            float height = Mathf.Max(from.y, to.y);
            Vector3 a = ToLocal(from), b = ToLocal(to);
            foreach (Collider other in FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (!IsBlocking(other, item, true)) continue;
                Bounds bounds = other.bounds;
                Vector3 center = ToLocal(bounds.center);
                if (center.x < Mathf.Min(a.x, b.x) - .25f || center.x > Mathf.Max(a.x, b.x) + .25f ||
                    center.z < Mathf.Min(a.z, b.z) - .25f || center.z > Mathf.Max(a.z, b.z) + .25f) continue;
                if (bounds.max.y > transform.position.y + .65f) continue;
                height = Mathf.Max(height, bounds.max.y + item.HalfExtents.magnitude + .015f);
            }
            return height + liftHeight;
        }
        void LocalBounds(PhysicalInteractable item, Quaternion rotation, out Vector3 extents, out Vector3 offset)
        {
            Quaternion q = Quaternion.Inverse(transform.rotation) * rotation;
            Vector3 half = item ? item.HalfExtents : Vector3.one * .005f;
            Vector3 right = q * Vector3.right, up = q * Vector3.up, forward = q * Vector3.forward;
            extents = Abs(right) * half.x + Abs(up) * half.y + Abs(forward) * half.z;
            offset = q * ColliderOffset(item);
        }
        static Vector3 ColliderOffset(PhysicalInteractable item) => item && item.dimensions ? item.dimensions.colliderCenter : Vector3.zero;
        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        public static bool Finite(Quaternion value) => Finite(value.x) && Finite(value.y) && Finite(value.z) && Finite(value.w) &&
            value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w > .5f;
    }
}
