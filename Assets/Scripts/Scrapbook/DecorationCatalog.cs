using System;
using UnityEngine;

namespace VintageJournal
{
    public enum DecorationCategory { Tape, Sticker, Note }

    [Serializable]
    public sealed class DecorationDefinition
    {
        public string assetId;
        public string displayName;
        public DecorationCategory category;
        public Texture2D thumbnail;
        public Texture2D texture;
        // Physical paper-normalized dimensions, before a player's uniform scale.
        public Vector2 defaultSize = new Vector2(.3f, .23f);
        public float minScale = .35f;
        public float maxScale = 2f;
        public bool supportsTint;
        public bool supportsLengthAdjustment;
        [HideInInspector] public byte[] alphaMask;
        [HideInInspector] public int maskWidth = 64;
        [HideInInspector] public int maskHeight = 64;
        public bool IsAvailable => !string.IsNullOrEmpty(assetId) && texture && thumbnail &&
            defaultSize.x > 0 && defaultSize.y > 0;

        public bool HitAlpha(Vector2 uv)
        {
            if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) return false;
            if (alphaMask == null || alphaMask.Length != maskWidth * maskHeight || maskWidth < 1 || maskHeight < 1) return true;
            int x = Mathf.Clamp(Mathf.FloorToInt(uv.x * maskWidth), 0, maskWidth - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt(uv.y * maskHeight), 0, maskHeight - 1);
            return alphaMask[y * maskWidth + x] > 24;
        }
    }

    [CreateAssetMenu(menuName = "Vintage Journal/Decoration Catalog")]
    public sealed class DecorationCatalog : ScriptableObject
    {
        public DecorationDefinition[] assets = Array.Empty<DecorationDefinition>();
        // An explicit serialized reference prevents Shader.Find-only build stripping.
        public Shader paperShader;

        public DecorationDefinition Find(string assetId)
        {
            if (assets == null || string.IsNullOrEmpty(assetId)) return null;
            foreach (var definition in assets)
                if (definition != null && definition.assetId == assetId) return definition;
            // Finite earned materials resolve for rendering without entering the unlimited legacy browser.
            return NightMaterialCatalog.Find(assetId) ?? PersonalStudioAssets.Find(assetId) ?? SocialMaterialAssets.Find(assetId);
        }

        public static DecorationCatalog Load()
        {
            return Resources.Load<DecorationCatalog>("DecorationCatalog");
        }
    }
}
