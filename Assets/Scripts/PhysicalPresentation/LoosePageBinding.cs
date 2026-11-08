using UnityEngine;

namespace VintageJournal
{
    /// <summary>Read-only paper identity and future binding geometry, independent of its replaceable artwork.</summary>
    public sealed class LoosePageBinding : MonoBehaviour
    {
        public string paperId;
        public string style;
        public string bindingType = "SixRing_A5";
        public Transform[] holeAnchors;
        public Vector2 pageSize = new Vector2(.148f, .210f);
    }
}
