using UnityEngine;

namespace VintageJournal
{
    /// <summary>Single owner of physical item transitions. Accepts intents from any input adapter.</summary>
    [DisallowMultipleComponent]
    public sealed class PhysicalInteractionController : MonoBehaviour
    {
        public DeskPlacementSurface surface;
        public Transform inspectAnchor;
        public Transform interactionOrigin;
        [Min(1)] public float moveSharpness = 16f;
        public PhysicalInteractable HeldItem { get; private set; }
        public PhysicalInteractable HoveredItem { get; private set; }
        public bool IsInspecting { get; private set; }
        public bool IsSettling => settlingItem;
        public event System.Action<PhysicalInteractable> Settled;
        public string LastResult { get; private set; } = "Ready";
        PhysicalItemState hoverPrevious;
        PhysicalItemState safeState;
        PhysicalStorageSlot safeSlot;
        Pose safePose, desiredPose, requestedPose;
        PhysicalInteractable settlingItem;
        PhysicalStorageSlot destinationSlot;
        PhysicalItemState destinationState;
        Pose destinationPose;
        bool liftFirst;
        float transitHeight;
        float settleSeconds;
        bool lastMoveReachable = true;
        bool needsBookExtraction, extractingBook;
        Pose bookExtractionPose;
        bool usesStorageAccessPath, sourceExitPending, sourceExitActive;
        Pose sourceExitPose, destinationEntryPose;
        PhysicalStorageAccessPath destinationAccessPath;
        readonly System.Collections.Generic.List<Pose> accessRoute = new System.Collections.Generic.List<Pose>();
        int accessRouteIndex, sourceExitWaypoint = -1;
        public bool IsFollowingStorageAccessPath => usesStorageAccessPath && (sourceExitActive || accessRouteIndex < accessRoute.Count);

        public bool Submit(PhysicalInteractionIntent intent)
        {
            switch (intent.kind)
            {
                case PhysicalIntentKind.Hover: return Hover(intent.target);
                case PhysicalIntentKind.Grab: return Grab(intent.target);
                case PhysicalIntentKind.Move: return Move(intent.pose);
                case PhysicalIntentKind.Release: return Release();
                case PhysicalIntentKind.Cancel: Cancel(); return true;
                case PhysicalIntentKind.Inspect: return Inspect(intent.target ? intent.target : HeldItem);
                case PhysicalIntentKind.ReturnHome: return ReturnHome(intent.target ? intent.target : HeldItem);
                default: return Result(false, "UnknownIntent");
            }
        }
        bool Result(bool success, string detail)
        { LastResult = detail; return success; }

        bool Hover(PhysicalInteractable item)
        {
            if (HoveredItem == item) return true;
            if (HoveredItem)
            { HoveredItem.SetHighlight(false); if (HoveredItem.CurrentState == PhysicalItemState.Hovered) HoveredItem.TryTransition(hoverPrevious); }
            HoveredItem = null;
            if (!item || HeldItem || settlingItem || !item.IsAccessible) return true;
            if (item.CurrentState != PhysicalItemState.Stored && item.CurrentState != PhysicalItemState.Available && item.CurrentState != PhysicalItemState.Placed)
                return false;
            hoverPrevious = item.CurrentState;
            if (!item.TryTransition(PhysicalItemState.Hovered)) return false;
            HoveredItem = item; item.SetHighlight(true); return true;
        }
        bool Grab(PhysicalInteractable item)
        {
            if (!item || HeldItem || settlingItem || !surface || !item.IsAccessible || !item.Supports(PhysicalOperations.Grab) || !IsReachable(item, item.transform.position))
                return Result(false, "GrabUnavailable");
            item.Initialize();
            if (!item.CanTransitionTo(PhysicalItemState.Grabbed)) return Result(false, "InvalidGrabState");
            safeState = item == HoveredItem ? hoverPrevious : item.CurrentState;
            safePose = item.CurrentWorldPose; safeSlot = item.CurrentSlot;
            if (safeSlot && !safeSlot.TryReserve(item)) return Result(false, "SlotReserved");
            Hover(null);
            if (!item.TryTransition(PhysicalItemState.Grabbed)) return false;
            if (safeSlot) safeSlot.Vacate(item);
            item.SetSlot(null);
            HeldItem = item; IsInspecting = false;
            lastMoveReachable = true;
            requestedPose = safePose;
            // Grabbing begins a transaction only. The input adapter must request an actual move
            // before the object leaves its original pose; selecting never creates a lift animation.
            desiredPose = safePose;
            transitHeight = safePose.position.y;
            liftFirst = false;
            var accessPath = PhysicalStorageAccessPath.ForSlot(safeSlot);
            usesStorageAccessPath = accessPath;
            sourceExitPending = accessPath;
            sourceExitActive = false;
            if (accessPath) sourceExitPose = accessPath.ExitPose(safePose);
            accessRoute.Clear(); accessRouteIndex = 0; destinationAccessPath = null;
            needsBookExtraction = !accessPath && safeSlot && item.itemType == PhysicalItemType.TravelersNotebook;
            extractingBook = false;
            Debug.Log("[Physical] Grab:" + item.itemInstanceId + ":" + item.assetId);
            return Result(true, "Grabbed");
        }
        bool Move(Pose restingPose)
        {
            if (!HeldItem || IsInspecting || !HeldItem.Supports(PhysicalOperations.Move))
                return Result(false, "MoveUnavailable");
            if (!DeskPlacementSurface.Finite(restingPose.position) || !DeskPlacementSurface.Finite(restingPose.rotation))
            { lastMoveReachable = false; return Result(false, "MoveInvalidPose"); }
            if (HeldItem.itemType == PhysicalItemType.LoosePage) restingPose.rotation = Quaternion.identity;
            requestedPose = surface.RestingPose(HeldItem, restingPose.position, restingPose.rotation);
            lastMoveReachable = IsReachable(HeldItem, requestedPose.position);
            if (!lastMoveReachable) return Result(false, "MoveOutsideReach");
            sourceExitActive = sourceExitPending;
            if (needsBookExtraction)
            {
                // Pull a spine-out volume straight clear of its neighbours before laying it flat.
                bookExtractionPose = new Pose(safePose.position - surface.transform.forward * (HeldItem.dimensions.size.x + .035f), safePose.rotation);
                needsBookExtraction = false; extractingBook = true;
            }
            desiredPose = new Pose(surface.ClampPosition(HeldItem, requestedPose.position, requestedPose.rotation) +
                surface.transform.up * .006f, requestedPose.rotation);
            Vector3 baseline = sourceExitPending ? sourceExitPose.position :
                surface.RestingPose(HeldItem, HeldItem.transform.position, requestedPose.rotation).position;
            transitHeight = DragTransitHeight(HeldItem, baseline, desiredPose.position, requestedPose.rotation);
            if (HeldItem.transform.position.y < transitHeight - .003f &&
                new Vector2(HeldItem.transform.position.x - desiredPose.position.x, HeldItem.transform.position.z - desiredPose.position.z).sqrMagnitude > .000004f)
                liftFirst = true;
            HeldItem.TryTransition(PhysicalItemState.Moving);
            return Result(true, "Moving");
        }
        float DragTransitHeight(PhysicalInteractable item, Vector3 from, Vector3 to, Quaternion rotation)
        {
            // Check the actual swept footprint, rather than lifting over every nearby shelf.
            // A cabinet beside the pointer path must not send a small sticker into the air.
            float height = Mathf.Max(to.y, from.y + .006f);
            Vector3 delta = to - from;
            if (delta.sqrMagnitude < .000001f) return height;
            Vector3 half = item.HalfExtents;
            Vector3 centre = from + rotation * (item.dimensions ? item.dimensions.colliderCenter : Vector3.zero);
            foreach (var hit in Physics.BoxCastAll(centre, half, delta.normalized, rotation, delta.magnitude,
                surface.obstacleMask, QueryTriggerInteraction.Ignore))
            {
                var collider = hit.collider;
                if (!collider || collider.transform.IsChildOf(item.transform) || collider.transform == surface.transform ||
                    (safeSlot && safeSlot.container && collider.transform.IsChildOf(safeSlot.container.transform)) ||
                    System.Array.IndexOf(surface.ignoredColliders, collider) >= 0) continue;
                float verticalHalf = Mathf.Abs((rotation * Vector3.right).y) * half.x +
                    Mathf.Abs((rotation * Vector3.up).y) * half.y + Mathf.Abs((rotation * Vector3.forward).y) * half.z;
                height = Mathf.Max(height, collider.bounds.max.y + verticalHalf + .006f);
            }
            return height;
        }
        bool Release()
        {
            if (!HeldItem || IsInspecting || !HeldItem.Supports(PhysicalOperations.Release)) return Result(false, "ReleaseUnavailable");
            PhysicalInteractable item = HeldItem;
            if (!lastMoveReachable)
            { RestoreSafe(item); return Result(false, "InvalidPlacementRestored"); }
            PhysicalStorageSlot slot = NearestSlot(item, requestedPose.position);
            if (slot && slot.TryReserve(item))
            { BeginSettle(item, slot.WorldPose, slot, PhysicalItemState.Stored); return Result(true, "SnappedToStorage"); }
            if (lastMoveReachable && surface.IsValidPlacement(item, requestedPose))
            {
                BeginSettle(item, surface.RestingPose(item, requestedPose.position, requestedPose.rotation), null, PhysicalItemState.Placed);
                return Result(true, "Placed");
            }
            RestoreSafe(item); return Result(false, "InvalidPlacementRestored");
        }
        PhysicalStorageSlot NearestSlot(PhysicalInteractable item, Vector3 requested)
        {
            PhysicalStorageSlot best = null; float distance = float.MaxValue;
            foreach (var slot in FindObjectsByType<PhysicalStorageSlot>(FindObjectsSortMode.None))
            {
                if (!slot.IsAccessible || !slot.CanAccept(item)) continue;
                Vector3 delta = slot.transform.position - requested; delta.y = 0;
                float d = delta.sqrMagnitude;
                if (d <= slot.snapRadius * slot.snapRadius && d < distance) { distance = d; best = slot; }
            }
            return best;
        }
        bool Inspect(PhysicalInteractable item)
        {
            if (!item || !item.Supports(PhysicalOperations.Inspect)) return Result(false, "InspectUnavailable");
            Transform anchor = item.inspectAnchor ? item.inspectAnchor : inspectAnchor;
            if (!anchor) return Result(false, "MissingInspectAnchor");
            if (!IsReachable(item, anchor.position)) return Result(false, "InspectOutsideReach");
            if (!HeldItem && !Grab(item)) return false;
            if (HeldItem != item) return Result(false, "AnotherItemHeld");
            IsInspecting = true; liftFirst = true;
            sourceExitActive = sourceExitPending;
            desiredPose = new Pose(anchor.position, anchor.rotation);
            transitHeight = usesStorageAccessPath ? DragTransitHeight(item,
                sourceExitPending ? sourceExitPose.position : item.transform.position, desiredPose.position, desiredPose.rotation) :
                surface.TransitHeight(item, item.transform.position, desiredPose.position);
            item.TryTransition(PhysicalItemState.Moving);
            Debug.Log("[Physical] Inspect:" + item.itemInstanceId);
            return Result(true, "Inspecting");
        }
        bool ReturnHome(PhysicalInteractable item)
        {
            if (!item || !item.Supports(PhysicalOperations.ReturnHome) || (HeldItem && HeldItem != item) || settlingItem)
                return Result(false, "ReturnUnavailable");
            if (!item.homeAnchor || (item.homeSlot && (!item.homeSlot.IsAccessible || !item.homeSlot.TryReserve(item))))
                return Result(false, "HomeUnavailable");
            if (!HeldItem && !Grab(item)) return false;
            BeginSettle(item, new Pose(item.homeAnchor.position, item.homeAnchor.rotation), item.homeSlot,
                item.homeSlot ? PhysicalItemState.Stored : PhysicalItemState.Available);
            Debug.Log("[Physical] ReturnHome:" + item.itemInstanceId);
            return Result(true, "ReturningHome");
        }
        /// <summary>Commit the current grab to an explicit accessible temporary or home slot.</summary>
        public bool CommitToSlot(PhysicalStorageSlot slot)
        {
            if(!HeldItem||settlingItem||!slot||!slot.IsAccessible||!slot.CanAccept(HeldItem))
                return Result(false,"SlotUnavailable");
            var item=HeldItem;
            if(!slot.TryReserve(item))return Result(false,"SlotReserved");
            BeginSettle(item,slot.WorldPose,slot,PhysicalItemState.Stored);
            return Result(true,"InstallingToSlot");
        }
        public void Cancel()
        {
            Hover(null);
            if (HeldItem) RestoreSafe(HeldItem);
            else if (settlingItem)
            {
                if (usesStorageAccessPath) RestoreSafe(settlingItem);
                else CompleteSettle();
            }
            LastResult = "Cancelled";
        }
        // A workbench swap commits ownership through the craft registry after restoring the safe origin.
        // This is explicit cancellation, never a placement through arbitrary colliders.
        public bool RestoreGrabToSafePose(PhysicalInteractable expected)
        {
            if (!expected || HeldItem != expected || settlingItem) return false;
            RestoreSafe(expected);
            if (!usesStorageAccessPath) CompleteSettle();
            return !HeldItem && !settlingItem;
        }
        void RestoreSafe(PhysicalInteractable item)
        {
            BeginSettle(item, safePose, safeSlot, safeSlot ? PhysicalItemState.Stored :
                (safeState == PhysicalItemState.Placed ? PhysicalItemState.Placed : PhysicalItemState.Available));
            Debug.Log("[Physical] Restore:" + item.itemInstanceId);
        }
        void BeginSettle(PhysicalInteractable item, Pose target, PhysicalStorageSlot slot, PhysicalItemState finalState)
        {
            RememberInsertionExit();
            HeldItem = null; IsInspecting = false; settlingItem = item;
            extractingBook = needsBookExtraction = false;
            destinationPose = target; destinationSlot = slot; destinationState = finalState;
            item.TryTransition(PhysicalItemState.Returning);
            destinationAccessPath = PhysicalStorageAccessPath.ForSlot(slot);
            usesStorageAccessPath |= destinationAccessPath;
            if (usesStorageAccessPath) BuildAccessRoute(item);
            transitHeight = surface.TransitHeight(item, item.transform.position, target.position);
            liftFirst = true;
            settleSeconds = 0;
        }
        // Retargeting during the final insertion must first reverse along the same entrance.
        // CurrentSlot is intentionally still null until every swept segment has completed.
        void RememberInsertionExit()
        {
            if (!settlingItem || !usesStorageAccessPath || !destinationAccessPath ||
                accessRouteIndex != accessRoute.Count - 1 || accessRoute.Count == 0) return;
            sourceExitPose = destinationEntryPose;
            sourceExitPending = true; sourceExitActive = false;
        }
        void BuildAccessRoute(PhysicalInteractable item)
        {
            accessRoute.Clear(); accessRouteIndex = 0; sourceExitWaypoint = -1;
            Pose current = item.CurrentWorldPose;
            if (Vector3.Distance(current.position, destinationPose.position) < .00001f &&
                Quaternion.Angle(current.rotation, destinationPose.rotation) < .01f)
            {
                sourceExitPending = sourceExitActive = false;
                accessRoute.Add(destinationPose);
                return;
            }
            // A partially extracted item can reverse its own straight source segment when
            // cancelled. Requiring the blocked front exit first would prevent a safe retreat.
            if (sourceExitPending && safeSlot && destinationSlot == safeSlot &&
                Vector3.Distance(destinationPose.position, safePose.position) < .00001f &&
                Quaternion.Angle(destinationPose.rotation, safePose.rotation) < .01f &&
                Quaternion.Angle(current.rotation, safePose.rotation) < .01f)
            {
                Vector3 segment = sourceExitPose.position - safePose.position;
                float lengthSquared = segment.sqrMagnitude;
                float along = lengthSquared > .00000001f ? Vector3.Dot(current.position - safePose.position, segment) / lengthSquared : -1;
                if (along >= 0 && along <= 1 &&
                    Vector3.Distance(current.position, safePose.position + segment * along) < .0001f)
                {
                    sourceExitPending = sourceExitActive = false;
                    accessRoute.Add(destinationPose);
                    return;
                }
            }
            if (sourceExitPending)
            {
                sourceExitWaypoint = accessRoute.Count;
                accessRoute.Add(sourceExitPose);
                current = sourceExitPose;
            }
            // Lift only after leaving the shelf. On return, lower outside before inserting.
            destinationEntryPose = destinationAccessPath ? destinationAccessPath.ExitPose(destinationPose) : destinationPose;
            float height = DragTransitHeight(item, current.position, destinationEntryPose.position, destinationEntryPose.rotation);
            Vector3 raisedStart = current.position; raisedStart.y = Mathf.Max(height, current.position.y);
            Vector3 raisedEnd = destinationEntryPose.position; raisedEnd.y = Mathf.Max(height, destinationEntryPose.position.y);
            accessRoute.Add(new Pose(raisedStart, current.rotation));
            accessRoute.Add(new Pose(raisedEnd, destinationEntryPose.rotation));
            accessRoute.Add(destinationEntryPose);
            if (destinationAccessPath) accessRoute.Add(destinationPose);
            sourceExitActive = false;
        }
        bool AdvanceAccessPose(PhysicalInteractable item, Pose target, float t)
        {
            Pose current = item.CurrentWorldPose;
            Vector3 next = Vector3.Lerp(current.position, target.position, t);
            Quaternion rotation = Quaternion.Slerp(current.rotation, target.rotation, t);
            // No cabinet exemption: shelf boards, neighbouring books and external obstructions
            // remain solid during extraction, travel and insertion.
            if (Vector3.Distance(next, target.position) < .00005f) next = target.position;
            if (Quaternion.Angle(rotation, target.rotation) < .01f) rotation = target.rotation;
            if (Quaternion.Angle(current.rotation, rotation) > .001f &&
                surface.HasCollision(item, current.position, rotation, false)) rotation = current.rotation;
            next = surface.Sweep(item, current.position, next, rotation, false);
            item.transform.SetPositionAndRotation(next, rotation);
            return Vector3.Distance(next, target.position) < .00001f && Quaternion.Angle(rotation, target.rotation) < .01f;
        }
        bool IsReachable(PhysicalInteractable item, Vector3 position)
        {
            if (!interactionOrigin || !item || !item.dimensions) return true;
            float distance = Vector3.Distance(interactionOrigin.position, position);
            return distance >= item.dimensions.minDistance && distance <= item.dimensions.maxDistance;
        }
        void Update() { Tick(Time.unscaledDeltaTime); }
        public void Tick(float dt)
        {
            if (dt <= 0 || !DeskPlacementSurface.Finite(dt) || !surface) return;
            float t = 1 - Mathf.Exp(-moveSharpness * Mathf.Min(dt, .1f));
            if (HeldItem)
            {
                var item = HeldItem; Pose current = item.CurrentWorldPose;
                if (sourceExitActive && sourceExitPending)
                {
                    if (AdvanceAccessPose(item, sourceExitPose, t)) sourceExitPending = sourceExitActive = false;
                    return;
                }
                if (extractingBook && !IsInspecting)
                {
                    Vector3 extractNext = Vector3.Lerp(current.position, bookExtractionPose.position, t);
                    extractNext = surface.Sweep(item, current.position, extractNext, bookExtractionPose.rotation, true, null, safeSlot ? safeSlot.container : null);
                    item.transform.SetPositionAndRotation(extractNext, bookExtractionPose.rotation);
                    if (Vector3.Distance(extractNext, bookExtractionPose.position) < .002f) extractingBook = false;
                    return;
                }
                Vector3 destination = desiredPose.position;
                if (liftFirst)
                {
                    destination = new Vector3(current.position.x, Mathf.Max(transitHeight, desiredPose.position.y), current.position.z);
                    if (Mathf.Abs(current.position.y - destination.y) < .002f) liftFirst = false;
                }
                else if (new Vector2(current.position.x - desiredPose.position.x, current.position.z - desiredPose.position.z).sqrMagnitude > .000004f)
                    destination.y = Mathf.Max(transitHeight, desiredPose.position.y);
                Vector3 proposed = Vector3.Lerp(current.position, destination, t);
                Quaternion rotation = liftFirst ? current.rotation : Quaternion.Slerp(current.rotation, desiredPose.rotation, t);
                proposed = usesStorageAccessPath ? surface.Sweep(item, current.position, proposed, rotation, false) :
                    surface.Sweep(item, current.position, proposed, rotation, safeSlot != null,null,safeSlot?safeSlot.container:null);
                item.transform.SetPositionAndRotation(proposed, rotation);
            }
            if (!settlingItem) return;
            settleSeconds += dt;
            if (settleSeconds > 6)
            {
                // Keep the last swept pose if a new obstacle blocks recovery; do not tunnel or hang.
                RememberInsertionExit();
                HeldItem = settlingItem; settlingItem = null; destinationSlot = null;
                HeldItem.TryTransition(PhysicalItemState.Available);
                HeldItem.TryTransition(PhysicalItemState.Grabbed);
                requestedPose = surface.RestingPose(HeldItem, HeldItem.transform.position, HeldItem.transform.rotation);
                desiredPose = HeldItem.CurrentWorldPose; IsInspecting = false; liftFirst = false;
                sourceExitActive = false; accessRoute.Clear(); accessRouteIndex = 0; destinationAccessPath = null;
                LastResult = "ReturnObstructedRetained";
                Debug.LogWarning("[Physical] ReturnObstructedRetained:" + HeldItem.itemInstanceId+" position="+HeldItem.transform.position+" target="+destinationPose.position+
                    " blocker="+(surface.LastBlockingCollider?surface.LastBlockingCollider.transform.parent.name+"/"+surface.LastBlockingCollider.name+" "+surface.LastBlockingCollider.bounds:"none"));
                return;
            }
            if (usesStorageAccessPath)
            {
                if (accessRouteIndex < accessRoute.Count && AdvanceAccessPose(settlingItem, accessRoute[accessRouteIndex], t))
                {
                    if (accessRouteIndex == sourceExitWaypoint) sourceExitPending = false;
                    accessRouteIndex++;
                }
                if (accessRouteIndex >= accessRoute.Count) CompleteSettle();
                return;
            }
            Vector3 p = settlingItem.transform.position;
            Vector3 target = destinationPose.position;
            // Lift, travel above furniture, then lower onto the destination. Stored poses are allowed above the desk.
            if (liftFirst)
            {
                target = new Vector3(p.x, transitHeight, p.z);
                if (Mathf.Abs(p.y - target.y) < .002f) liftFirst = false;
            }
            else if (new Vector2(p.x - target.x, p.z - target.z).sqrMagnitude > .000004f)
                target.y = transitHeight;
            Vector3 next = Vector3.Lerp(p, target, t);
            // Return routes use a raised transit corridor. Destination furniture is exempt only for its reserved slot.
            Quaternion nextRotation=liftFirst?settlingItem.transform.rotation:Quaternion.Slerp(settlingItem.transform.rotation,destinationPose.rotation,t);
            next = surface.Sweep(settlingItem, p, next, nextRotation, destinationSlot == settlingItem.homeSlot,
                destinationSlot ? destinationSlot.container : null,safeSlot?safeSlot.container:null);
            settlingItem.transform.SetPositionAndRotation(next,nextRotation);
            if (!liftFirst && Vector3.Distance(next, destinationPose.position) < .001f &&
                Quaternion.Angle(settlingItem.transform.rotation, destinationPose.rotation) < 1f) CompleteSettle();
        }
        void CompleteSettle()
        {
            if (!settlingItem) return;
            PhysicalInteractable item = settlingItem;
            if (destinationSlot && !destinationSlot.TryStore(item, true))
            { LastResult = "RestoreSlotConflict"; return; }
            item.transform.SetPositionAndRotation(destinationPose.position, destinationPose.rotation);
            item.SetSlot(destinationSlot);
            item.TryTransition(destinationState);
            item.RefreshVisibility();
            settlingItem = null; destinationSlot = null;
            usesStorageAccessPath = sourceExitPending = sourceExitActive = false;
            destinationAccessPath = null; accessRoute.Clear(); accessRouteIndex = 0;
            // Publish the final pose before unrelated metadata can commit and reconcile an old registry pose.
            Settled?.Invoke(item);
            Debug.Log("[Physical] Settled:" + item.itemInstanceId + ":" + item.CurrentState);
        }
        void OnDisable()
        {
            Hover(null);
            if (HeldItem) RestoreSafe(HeldItem);
            // Configured routes must not teleport through a shelf when disabled. Their safe
            // transaction remains uncommitted; re-enabling continues the swept restoration.
            if (!usesStorageAccessPath) CompleteSettle();
        }
    }
}
