using System;
using UnityEngine;

namespace VintageJournal
{
    /// <summary>Explicit scene anchors for the one-time, atomic desk-layout migration. No names or art are used at runtime.</summary>
    public sealed class DeskLayoutBindings : MonoBehaviour
    {
        public int version = 1;
        public DeskLayoutPlacement[] placements = Array.Empty<DeskLayoutPlacement>();
        public WorkspaceDrawer leftDrawer, rightDrawer;
        public PhysicalContainer desktopNotes, desktopBox, notebookShelf;
        public PhysicalInteractable glue;
    }

    [Serializable]
    public sealed class DeskLayoutPlacement
    {
        public PhysicalInteractable item;
        public PhysicalStorageSlot homeSlot;
        public PhysicalStorageSlot desiredDisplaySlot;
        public bool migrateLegacyHome = true;
        public bool relocateStockOnMigration, relocateDesktopOnMigration;
        public string[] legacySlotIds = Array.Empty<string>();
        public string releaseBookId;
    }
}
