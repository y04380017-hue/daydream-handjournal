using UnityEngine;

namespace VintageJournal
{
    public enum TravelersNotebookSize { Standard, Passport }

    /// <summary>Replaceable TN presentation and future opening/insert anchors; no binder-page behaviour.</summary>
    [DisallowMultipleComponent]
    public sealed class TravelersNotebookBinding : MonoBehaviour
    {
        public TravelersNotebookSize sizeType;
        public Transform frontCover;
        public Transform backCover;
        public Transform spine;
        public Transform elastic;
        public Transform insertAnchor;
        public Transform openingHinge;
        public Transform unfoldedAnchor;
        bool inspectionOpen;
        public bool IsOpen => inspectionOpen;
        public bool SupportsOpening => true;
        public void SetInspectionOpen(bool value) { inspectionOpen = value; }
    }
}
