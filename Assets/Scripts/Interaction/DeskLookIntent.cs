using UnityEngine;

namespace VintageJournal
{
    public enum DeskLookIntentKind { Look, ResetForward }

    /// <summary>Relative head rotation intent. No position, walking, orbit target or device state is carried.</summary>
    public readonly struct DeskLookIntent
    {
        public readonly DeskLookIntentKind kind;
        public readonly Vector2 degrees;
        public DeskLookIntent(Vector2 degrees) { kind=DeskLookIntentKind.Look;this.degrees=degrees; }
        public DeskLookIntent(DeskLookIntentKind kind) { this.kind=kind;degrees=Vector2.zero; }
    }
}
