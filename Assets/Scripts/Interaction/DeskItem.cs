using UnityEngine;

namespace VintageJournal
{
    public enum DeskItemKind { Tape, Sticker, Pages, Journal, Decoration, Note }

    /// <summary>An independently reusable desk object. It owns no UI or input code.</summary>
    [DisallowMultipleComponent]
    public sealed class DeskItem : MonoBehaviour
    {
        public DeskItemKind kind;
        public string displayName = "桌面物品";
        public bool Highlighted { get; private set; }

        private Renderer[] surfaces;
        private MaterialPropertyBlock[][] originalBlocks;
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorProperty = Shader.PropertyToID("_Color");
        private static readonly int Emission = Shader.PropertyToID("_EmissionColor");

        private void Awake() { ConfigureHighlight(); }

        public void ConfigureHighlight()
        {
            surfaces = GetComponentsInChildren<Renderer>(true);
            originalBlocks = new MaterialPropertyBlock[surfaces.Length][];
            for (int i = 0; i < surfaces.Length; i++)
            {
                var materials = surfaces[i].sharedMaterials;
                originalBlocks[i] = new MaterialPropertyBlock[materials.Length];
                for (int j = 0; j < materials.Length; j++)
                {
                    var block = new MaterialPropertyBlock();
                    surfaces[i].GetPropertyBlock(block, j);
                    originalBlocks[i][j] = block;
                }
            }
        }

        public void SetHighlight(bool enabled)
        {
            if (Highlighted == enabled) return;
            if (surfaces == null) ConfigureHighlight();
            Highlighted = enabled;
            for (int i = 0; i < surfaces.Length; i++)
            {
                if (!surfaces[i]) continue;
                var materials = surfaces[i].sharedMaterials;
                for (int j = 0; j < materials.Length; j++)
                {
                    if (!enabled)
                    {
                        surfaces[i].SetPropertyBlock(originalBlocks[i][j], j);
                        continue;
                    }
                    var material = materials[j];
                    if (!material) continue;
                    var block = new MaterialPropertyBlock();
                    surfaces[i].GetPropertyBlock(block, j);
                    var colorId = material.HasProperty(BaseColor) ? BaseColor : ColorProperty;
                    if (material.HasProperty(colorId))
                    {
                        Color original = material.GetColor(colorId);
                        Color lifted = Color.Lerp(original, new Color(1f, .89f, .64f, original.a), .16f);
                        lifted.a = original.a;
                        block.SetColor(colorId, lifted);
                    }
                    block.SetColor(Emission, new Color(.11f, .075f, .025f, 1));
                    surfaces[i].SetPropertyBlock(block, j);
                }
            }
        }

        private void OnDisable() { if (Highlighted) SetHighlight(false); }
    }
}
