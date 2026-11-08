using System;
using UnityEngine;

namespace VintageJournal
{
    public enum PhysicalItemType { Tape, Sticker, Note, LoosePage, TravelersNotebook, ReleaseBook, TapeStrip, Tool }
    public enum PhysicalItemState { Stored, Available, Hovered, Grabbed, Moving, Placed, Returning, Processing, Disabled }
    [Flags]
    public enum PhysicalOperations { None = 0, Grab = 1, Move = 2, Release = 4, Inspect = 8, ReturnHome = 16, All = 31 }
    public enum PhysicalIntentKind { Hover, Grab, Move, Release, Cancel, Inspect, ReturnHome }

    /// <summary>Input-independent command. Move/Release positions are the root's resting center, without lift.</summary>
    public readonly struct PhysicalInteractionIntent
    {
        public readonly PhysicalIntentKind kind;
        public readonly PhysicalInteractable target;
        public readonly Pose pose;
        public PhysicalInteractionIntent(PhysicalIntentKind kind, PhysicalInteractable target = null, Pose pose = default)
        { this.kind = kind; this.target = target; this.pose = pose; }
    }
}
