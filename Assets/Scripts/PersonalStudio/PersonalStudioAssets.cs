using System;
using System.Collections.Generic;
using UnityEngine;

namespace VintageJournal
{
    public static class PersonalStudioAssets
    {
        static Func<JournalDocument> read;
        static readonly Dictionary<string, DecorationDefinition> cache = new Dictionary<string, DecorationDefinition>(StringComparer.Ordinal);
        public static void Bind(WorkspaceGameplayController workspace) { read = () => workspace && workspace.Ready ? workspace.Service.Document : null; }
        public static void BindDocument(JournalDocument document) { read = () => document; }
        public static string[] TagsFor(string id) => read?.Invoke()?.personalStudio?.artworks.Find(a => a.assetId == id)?.moodTags ?? Array.Empty<string>();
        public static DecorationDefinition Find(string id)
        {
            if (id == null || !id.StartsWith("studio-art-", StringComparison.Ordinal)) return null;
            var art = read?.Invoke()?.personalStudio?.artworks.Find(a => a.assetId == id);
            if (art == null) return null;
            if (cache.TryGetValue(id, out var definition) && definition.texture) return definition;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = art.name, hideFlags = HideFlags.DontSave };
            try { if (!texture.LoadImage(Convert.FromBase64String(art.pngBase64))) { UnityEngine.Object.Destroy(texture); return null; } }
            catch (FormatException) { UnityEngine.Object.Destroy(texture); return null; }
            texture.wrapMode = TextureWrapMode.Clamp;
            var mask = new byte[64 * 64]; var pixels = texture.GetPixels32();
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) mask[y * 64 + x] = pixels[Mathf.Min(texture.height - 1, y * texture.height / 64) * texture.width + Mathf.Min(texture.width - 1, x * texture.width / 64)].a;
            definition = new DecorationDefinition { assetId = id, displayName = art.name, category = art.kind == "photo" ? DecorationCategory.Note : DecorationCategory.Sticker,
                texture = texture, thumbnail = texture, alphaMask = mask, defaultSize = new Vector2(.28f, .20f), minScale = .1f, maxScale = 3 };
            cache[id] = definition; return definition;
        }
        public static void Release()
        { foreach (var definition in cache.Values) if (definition.texture) UnityEngine.Object.Destroy(definition.texture); cache.Clear(); read = null; }
    }
}
