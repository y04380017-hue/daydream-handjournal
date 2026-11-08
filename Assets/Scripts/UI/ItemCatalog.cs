using System;
using UnityEngine;

namespace VintageJournal
{
    [Serializable]
    public sealed class CatalogItem
    {
        public string id;
        public string name;
        public string category;
        public string texture;
        public string description;
        public string prefab;

        [NonSerialized] private Texture2D thumbnail;
        public Texture2D Thumbnail
        {
            get
            {
                if (!thumbnail && !string.IsNullOrEmpty(texture)) thumbnail = Resources.Load<Texture2D>(texture);
                return thumbnail;
            }
        }
    }

    [Serializable]
    public sealed class ItemCatalog
    {
        public CatalogItem[] items = Array.Empty<CatalogItem>();

        public static ItemCatalog Load()
        {
            var source = Resources.Load<TextAsset>("Catalog");
            if (!source)
            {
                Debug.LogError("[Desk] Missing Resources/Catalog.json");
                return new ItemCatalog();
            }
            try
            {
                var catalog = JsonUtility.FromJson<ItemCatalog>(source.text);
                return catalog ?? new ItemCatalog();
            }
            catch (Exception error)
            {
                Debug.LogError("[Desk] Cannot load catalog: " + error.Message);
                return new ItemCatalog();
            }
        }
    }
}
