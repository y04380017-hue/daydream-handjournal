using UnityEngine;
namespace VintageJournal
{
    /// <summary>References to the second layout; no art names participate in gameplay.</summary>
    public sealed class DeskReferenceBindings : MonoBehaviour
    {
        public PhysicalContainer clothBag, releaseShelf;
        public Transform cabinet, glueShell;
        public Transform bagRoot => clothBag ? clothBag.transform : null;
        public Transform rackRoot => cabinet;
        public PhysicalStorageSlot[] showcaseSlots, releaseShelfSlots;
        public TapePatternPreview[] sampleStripViews;
        public Renderer[] glueShellRenderers, leftDrawerBody, rightDrawerBody;
        public PhysicalInteractable[] addedRolls;
        public DrawerStockIndex stockIndex;
        public Vector3 drawerExterior = new Vector3(.3156f,.078f,.270f);
    }
}
