using System;
using System.Collections.Generic;
using UnityEngine;

namespace VintageJournal
{
    public static class SocialMaterialAssets
    {
        static Func<JournalDocument> read;
        static readonly Dictionary<string, DecorationDefinition> cache = new Dictionary<string, DecorationDefinition>();
        public static void Bind(WorkspaceGameplayController workspace) { read = () => workspace ? (workspace.Ready ? workspace.Service.Document : workspace.journal ? workspace.journal.Document : null) : null; }
        public static bool ValidPng(string base64, out Texture2D texture)
        {
            texture = null;
            try
            {
                var bytes = Convert.FromBase64String(base64);
                if (bytes.Length < 33 || bytes.Length > 150000 || bytes[0] != 137 || bytes[1] != 80 || bytes[2] != 78 || bytes[3] != 71) return false;
                uint width = (uint)(bytes[16] << 24 | bytes[17] << 16 | bytes[18] << 8 | bytes[19]);
                uint height = (uint)(bytes[20] << 24 | bytes[21] << 16 | bytes[22] << 8 | bytes[23]);
                if (width < 1 || height < 1 || width > 256 || height > 256) return false;
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave };
                if (!texture.LoadImage(bytes)) { Release(texture); texture = null; return false; }
                return true;
            }
            catch (Exception) { if (texture) Release(texture); texture = null; return false; }
        }
        public static DecorationDefinition Find(string assetId)
        {
            if (string.IsNullOrEmpty(assetId) || !assetId.StartsWith("social-gift-", StringComparison.Ordinal)) return null;
            if(read==null){var workspace=UnityEngine.Object.FindFirstObjectByType<WorkspaceGameplayController>();if(workspace)Bind(workspace);}
            if (cache.TryGetValue(assetId, out var value) && value.texture) return value;
            var gift = read?.Invoke()?.atelierSocial?.gifts?.Find(g => g.assetId == assetId);
            if (gift == null || !ValidPng(gift.pngBase64, out var texture)) return null;
            var mask = new byte[64 * 64];
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) mask[y * 64 + x] = (byte)Mathf.RoundToInt(texture.GetPixelBilinear((x + .5f)/64, (y+.5f)/64).a * 255);
            value = new DecorationDefinition { assetId = assetId, displayName = gift.name, category = DecorationCategory.Sticker,
                texture = texture, thumbnail = texture, defaultSize = new Vector2(.27f,.19f), alphaMask = mask };
            cache[assetId] = value; return value;
        }
        public static void Clear()
        { foreach (var definition in cache.Values) if (definition.texture) Release(definition.texture); cache.Clear(); read = null; }
        static void Release(Texture2D texture) { if(Application.isPlaying)UnityEngine.Object.Destroy(texture);else UnityEngine.Object.DestroyImmediate(texture); }
    }
}
