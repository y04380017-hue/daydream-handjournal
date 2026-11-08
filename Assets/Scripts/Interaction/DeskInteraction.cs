using System;
using UnityEngine;

namespace VintageJournal
{
    /// <summary>Converts pointer rays into item activation; the UI owns all panel state.</summary>
    [DisallowMultipleComponent]
    public sealed class DeskInteraction : MonoBehaviour
    {
        public Camera deskCamera;
        public DeskUI ui;
        public DeskCamera cameraRig;
        public float maxDistance = 12f;
        public DeskItem HoveredItem { get; private set; }
        public string CurrentHover => HoveredItem ? HoveredItem.displayName : string.Empty;
        JournalController journal;
        PhysicalMouseInputAdapter physicalInput;

        private void Awake()
        {
            if (!deskCamera) deskCamera = Camera.main;
            if (!ui) ui = FindFirstObjectByType<DeskUI>();
            if (!cameraRig && deskCamera) cameraRig = deskCamera.GetComponent<DeskCamera>();
            if (ui) ui.interaction = this;
            journal=FindFirstObjectByType<JournalController>();
            physicalInput=FindFirstObjectByType<PhysicalMouseInputAdapter>();
        }

        private void Update()
        {
            if(RuntimeInputGate.BlocksLegacyInput||RuntimeInputGate.ConsumedThisFrame){SetHovered(null);return;}
            if ((physicalInput && physicalInput.enabled && physicalInput.OwnsPointer) || (journal && journal.IsEditing) || !Application.isFocused || !deskCamera || !ui || !ui.SceneInputAllowed ||
                (cameraRig && cameraRig.IsTransitioning) || ui.IsPointerOverInterface(Input.mousePosition))
            {
                SetHovered(null);
                return;
            }
            HoverAt(Input.mousePosition);
            if (Input.GetMouseButtonDown(0) && HoveredItem) Activate(HoveredItem);
        }

        public DeskItem HoverAt(Vector2 screenPosition)
        {
            DeskItem item = null;
            if (deskCamera && Physics.Raycast(deskCamera.ScreenPointToRay(screenPosition), out RaycastHit hit,
                maxDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
                item = hit.collider.GetComponentInParent<DeskItem>();
            SetHovered(item);
            return item;
        }

        private void SetHovered(DeskItem item)
        {
            if (HoveredItem == item) return;
            if (HoveredItem) HoveredItem.SetHighlight(false);
            HoveredItem = item;
            if (HoveredItem)
            {
                HoveredItem.SetHighlight(true);
                Debug.Log("[Desk] Hover:" + HoveredItem.kind + ":" + HoveredItem.displayName);
            }
            if (ui) ui.CurrentHover = CurrentHover;
        }

        public bool TryActivateAt(Vector2 screenPosition)
        {
            if(RuntimeInputGate.BlocksLegacyInput||RuntimeInputGate.ConsumedThisFrame)return false;
            if(physicalInput && physicalInput.enabled && physicalInput.OwnsPointer)return false;
            if ((journal && journal.IsEditing) || !ui || ui.IsBlocking || ui.IsPointerOverInterface(screenPosition)) return false;
            var item = HoverAt(screenPosition);
            if (!item) return false;
            Activate(item);
            return true;
        }

        public void Activate(DeskItem item)
        {
            if(RuntimeInputGate.BlocksLegacyInput||RuntimeInputGate.ConsumedThisFrame)return;
            if ((journal && journal.IsEditing) || !item || !ui || ui.IsBlocking) return;
            Debug.Log("[Desk] Activate:" + item.kind);
            SetHovered(null);
            if (item.kind == DeskItemKind.Journal)
            {
                if (journal) journal.EnterEditing();
                else if (cameraRig) cameraRig.Inspect();
            }
            else ui.ShowCatalog(item.kind);
        }

        [Serializable]
        private sealed class TargetDiagnostic
        {
            public string kind;
            public string name;
            public float x;
            public float y;
            public int width;
            public int height;
        }

        /// <summary>Records visible ray-tested hit coordinates for external OS-input verification.
        /// This only observes scene geometry and never activates objects.</summary>
        public void LogVisibleTargets()
        {
            if (!deskCamera) return;
            foreach (var item in FindObjectsByType<DeskItem>(FindObjectsSortMode.None))
            {
                bool found = false;
                Vector3 candidate = Vector3.zero;
                foreach (var collider in item.GetComponentsInChildren<Collider>())
                {
                    if (!collider.enabled) continue;
                    Bounds bounds = collider.bounds;
                    var center = deskCamera.WorldToScreenPoint(bounds.center);
                    float radius = Mathf.Max(16, bounds.extents.magnitude * Screen.height /
                        Mathf.Max(.1f, center.z) / Mathf.Tan(deskCamera.fieldOfView * Mathf.Deg2Rad * .5f));
                    // Search the projected center first, then nearby samples; every retained
                    // point must hit this exact item through the normal Physics raycast.
                    for (int ring = 0; ring < 5 && !found; ring++)
                        for (int sample = 0; sample < (ring == 0 ? 1 : 16) && !found; sample++)
                        {
                            float angle = sample * Mathf.PI / 8;
                            candidate = center + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0) * radius * ring / 8;
                            if (center.z <= 0 || candidate.x < 8 || candidate.x > Screen.width - 8 ||
                                candidate.y < 8 || candidate.y > Screen.height - 8 ||
                                (ui && ui.IsPointerOverInterface(candidate))) continue;
                            if (Physics.Raycast(deskCamera.ScreenPointToRay(candidate), out var hit, maxDistance,
                                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide) &&
                                hit.collider.GetComponentInParent<DeskItem>() == item) found = true;
                        }
                    if (found) break;
                }
                if (found)
                    Debug.Log("[Desk] Target " + JsonUtility.ToJson(new TargetDiagnostic {
                        kind = item.kind.ToString(), name = item.displayName,
                        x = candidate.x / Screen.width, y = 1f - candidate.y / Screen.height,
                        width = Screen.width, height = Screen.height }));
                else Debug.LogWarning("[Desk] TargetNotVisible:" + item.kind + ":" + item.displayName);
            }
        }

        private void OnDisable() { SetHovered(null); }
    }
}
