using UnityEditor;
using UnityEngine;

namespace VintageJournal.Editor
{
    public sealed class DeskAssetImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            var t = (TextureImporter)assetImporter;
            bool sprite = assetPath.StartsWith("Assets/Sprites/") || assetPath.StartsWith("Assets/Resources/Catalog/") || assetPath.StartsWith("Assets/UI/");
            t.textureType = sprite ? TextureImporterType.Sprite : assetPath.Contains("/Normals/") ? TextureImporterType.NormalMap : TextureImporterType.Default;
            t.spriteImportMode = SpriteImportMode.Single;
            t.alphaIsTransparency = sprite || assetPath.Contains("_strip");
            t.mipmapEnabled = !sprite;
            t.maxTextureSize = 1024;
            t.textureCompression = TextureImporterCompression.Uncompressed;
            t.wrapMode = sprite || assetPath.Contains("_strip") ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            t.filterMode = FilterMode.Bilinear;
            t.anisoLevel = sprite ? 1 : 4;
            if (assetPath.Contains("/DaydreamPapers/") || assetPath.Contains("/FloatingV2/Textures/"))
            {
                t.maxTextureSize = 2048;
                t.wrapMode = TextureWrapMode.Clamp;
                t.npotScale = TextureImporterNPOTScale.None;
            }
        }

        [MenuItem("Vintage Journal/Reimport owned texture settings")]
        public static void Reimport()
        {
            foreach (var id in AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Textures", "Assets/Sprites", "Assets/UI", "Assets/Resources/Catalog" }))
                AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(id), ImportAssetOptions.ForceUpdate);
        }
    }
}
