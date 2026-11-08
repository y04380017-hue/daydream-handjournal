using UnityEngine;

namespace VintageJournal
{
    /// <summary>Replace/rebind a visual hierarchy without changing item identity, dimensions, anchors or state.</summary>
    [DisallowMultipleComponent]
    public sealed class PhysicalVisualBinding : MonoBehaviour
    {
        public Transform visualRoot;
        Renderer[] renderers;
        MaterialPropertyBlock[][] original;
        bool highlighted;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        static readonly int ColorProperty = Shader.PropertyToID("_Color");
        public bool Highlighted => highlighted;

        public void Rebind(Transform root)
        {
            SetHighlight(false);
            visualRoot = root;
            renderers = root ? root.GetComponentsInChildren<Renderer>(true) : new Renderer[0];
            original = new MaterialPropertyBlock[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++)
            {
                original[i] = new MaterialPropertyBlock[renderers[i].sharedMaterials.Length];
                for (int j = 0; j < original[i].Length; j++)
                { original[i][j] = new MaterialPropertyBlock(); renderers[i].GetPropertyBlock(original[i][j], j); }
            }
        }
        public void SetHighlight(bool value)
        {
            if (highlighted == value) return;
            highlighted = value;
            if (renderers == null) return;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (!renderers[i]) continue;
                var materials = renderers[i].sharedMaterials;
                for (int j = 0; j < materials.Length && j < original[i].Length; j++)
                {
                    if (!value) { renderers[i].SetPropertyBlock(original[i][j], j); continue; }
                    Material mat = materials[j];
                    if (!mat) continue;
                    int property = mat.HasProperty(BaseColor) ? BaseColor : ColorProperty;
                    var block = new MaterialPropertyBlock(); renderers[i].GetPropertyBlock(block, j);
                    if (mat.HasProperty(property))
                        block.SetColor(property, Color.Lerp(mat.GetColor(property), new Color(1, .83f, .45f), .35f));
                    renderers[i].SetPropertyBlock(block, j);
                }
            }
        }
        public void SetVisible(bool visible)
        { if (visualRoot) visualRoot.gameObject.SetActive(visible); }
        void OnDisable() { SetHighlight(false); }
    }
}
