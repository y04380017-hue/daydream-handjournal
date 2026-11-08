using UnityEngine;

namespace VintageJournal
{
    public enum WorkspaceIntentKind
    {
        Select, Grab, Move, Place, Return, Open, Close, Peel, Attach, Transfer, Cancel,
        TapeLengthStart, TapeLengthConfirm
    }

    /// <summary>Shared action boundary for native mouse and future hand adapters.</summary>
    public readonly struct WorkspaceIntent
    {
        public readonly WorkspaceIntentKind kind;
        public readonly PhysicalInteractable target;
        public readonly string instanceId;
        public readonly Pose pose;
        public readonly CraftPlacement placement;
        public WorkspaceIntent(WorkspaceIntentKind kind, PhysicalInteractable target = null, string instanceId = null,
            Pose pose = default, CraftPlacement placement = null)
        { this.kind=kind;this.target=target;this.instanceId=instanceId;this.pose=pose;this.placement=placement; }
    }
}
