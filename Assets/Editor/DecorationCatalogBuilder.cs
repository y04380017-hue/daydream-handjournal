using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace VintageJournal.Editor
{
    public static class DecorationCatalogBuilder
    {
        [MenuItem("Vintage Journal/Stage 3/Build decoration catalog from existing art")]
        public static void BuildCatalog()
        {
            const string path = "Assets/Resources/DecorationCatalog.asset";
            var catalog = AssetDatabase.LoadAssetAtPath<DecorationCatalog>(path);
            if (!catalog)
            {
                catalog = ScriptableObject.CreateInstance<DecorationCatalog>();
                AssetDatabase.CreateAsset(catalog, path);
            }
            var source = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Resources/Catalog.json");
            if (!source) throw new InvalidOperationException("Existing stationery catalog is missing.");
            var original = JsonUtility.FromJson<ItemCatalog>(source.text);
            var result = new List<DecorationDefinition>();
            foreach (var item in original.items)
            {
                DecorationCategory category;
                string texturePath;
                if (item.category == "tape")
                {
                    category = DecorationCategory.Tape;
                    texturePath = "Assets/Textures/Tapes/" + item.id + "_pattern.png";
                }
                else if (item.category == "sticker")
                {
                    category = DecorationCategory.Sticker;
                    texturePath = "Assets/Sprites/Stickers/" + item.id + ".png";
                }
                else if (item.category == "note")
                {
                    category = DecorationCategory.Note;
                    texturePath = "Assets/Sprites/Notes/" + item.id + ".png";
                }
                else continue;

                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                var thumbnail = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Catalog/" + item.id + ".png");
                var size = new Vector2(.32f, .065f);
                if (category != DecorationCategory.Tape && texture)
                {
                    float height = category == DecorationCategory.Note ? .35f : .23f;
                    float width = height * .52f / .393f * texture.width / texture.height;
                    float fit = Mathf.Min(1f, .8f / Mathf.Max(width, height));
                    size = new Vector2(width * fit, height * fit);
                }
                result.Add(new DecorationDefinition {
                    assetId = item.id, displayName = item.name, category = category,
                    texture = texture, thumbnail = thumbnail, defaultSize = size,
                    minScale = .35f, maxScale = 2f,
                    supportsTint = false, supportsLengthAdjustment = category == DecorationCategory.Tape,
                    alphaMask = ReadAlphaMask(texturePath), maskWidth = 64, maskHeight = 64
                });
            }
            catalog.assets = result.ToArray();
            catalog.paperShader = Shader.Find("VintageJournal/ScrapbookPaper");
            if (!catalog.paperShader) throw new InvalidOperationException("ScrapbookPaper shader was not imported.");
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("docs/stage3-logs");
            int tape = 0, sticker = 0, note = 0, unavailable = 0;
            foreach (var definition in catalog.assets)
            {
                if (definition.category == DecorationCategory.Tape) tape++;
                if (definition.category == DecorationCategory.Sticker) sticker++;
                if (definition.category == DecorationCategory.Note) note++;
                if (!definition.IsAvailable) unavailable++;
            }
            File.WriteAllText("docs/stage3-logs/decoration-catalog.txt",
                "Existing art only. Tape=" + tape + "; Sticker=" + sticker + "; Note=" + note +
                "; Unavailable=" + unavailable + "; Shader=" + catalog.paperShader.name + "\n");
            if (tape < 8 || sticker < 12 || note < 6 || unavailable != 0)
                throw new InvalidOperationException("Decoration catalog does not satisfy the usable asset counts.");
            Debug.Log("[ScrapbookCatalog] Existing original textures registered: " + catalog.assets.Length);
        }

        static byte[] ReadAlphaMask(string imagePath)
        {
            var decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!ImageConversion.LoadImage(decoded, File.ReadAllBytes(imagePath), false))
                    throw new InvalidDataException("Cannot decode original decoration alpha: " + imagePath);
                var pixels = decoded.GetPixels32();
                var mask = new byte[64 * 64];
                for (int y = 0; y < 64; y++)
                    for (int x = 0; x < 64; x++)
                    {
                        int px = Mathf.Clamp(Mathf.RoundToInt((x + .5f) / 64 * decoded.width), 0, decoded.width - 1);
                        int py = Mathf.Clamp(Mathf.RoundToInt((y + .5f) / 64 * decoded.height), 0, decoded.height - 1);
                        mask[y * 64 + x] = pixels[py * decoded.width + px].a;
                    }
                return mask;
            }
            finally { UnityEngine.Object.DestroyImmediate(decoded); }
        }
    }
}
