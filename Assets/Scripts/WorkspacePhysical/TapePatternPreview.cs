using UnityEngine;
namespace VintageJournal
{
    /// <summary>A non-stock pattern swatch. The input adapter resolves its explicit source roll.</summary>
    public sealed class TapePatternPreview : MonoBehaviour
    {
        public PhysicalInteractable source;
        public PhysicalStorageSlot displaySlot;
        public Renderer preview;
        public Collider hitCollider;
        public PhysicalInteractable Source => source && displaySlot && source.CurrentSlot == displaySlot && source.gameObject.activeInHierarchy ? source : null;
        void LateUpdate(){bool visible=Source;if(preview)preview.enabled=visible;if(hitCollider)hitCollider.enabled=visible;}
    }
}
