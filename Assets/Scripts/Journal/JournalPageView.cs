using UnityEngine;
using UnityEngine.Rendering;

namespace VintageJournal
{
    /// <summary>
    /// Owns only the two physical page surfaces. Player paper choices never change a
    /// shared material or the source PNG. Art is fitted inside the original page.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class JournalPageView : MonoBehaviour
    {
        public Renderer leftRenderer;
        public Renderer rightRenderer;
        public Texture2D continuousPaperStock;
        public Color paperAlbedo = Color.white;

        private readonly Renderer[] source = new Renderer[2];
        private readonly Material[] originalMaterials = new Material[2];
        private readonly Material[] backingMaterials = new Material[2];
        private readonly Material[] artMaterials = new Material[2];
        private readonly Mesh[] artMeshes = new Mesh[2];
        private readonly Transform[] surfaces = new Transform[2];
        private readonly Vector2[] sizes = new Vector2[2];
        private readonly GameObject[] artObjects = new GameObject[2];
        private Renderer[] accessories;
        private bool[] accessoryVisibility;
        private bool initialized;
        private bool editing;

        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public bool IsInitialized => initialized;
        public Texture2D LeftTexture { get; private set; }
        public Texture2D RightTexture { get; private set; }

        public bool Initialize()
        {
            if (initialized) return true;
            if (!leftRenderer)
            {
                var child = transform.Find("Left decorated page");
                if (child) leftRenderer = child.GetComponent<Renderer>();
            }
            if (!rightRenderer)
            {
                var child = transform.Find("Right decorated page");
                if (child) rightRenderer = child.GetComponent<Renderer>();
            }
            if (!leftRenderer || !rightRenderer)
            {
                Debug.LogError("[Journal] 无法找到左右纸页表面，请检查手账本绑定。");
                return false;
            }
            source[0] = leftRenderer;
            source[1] = rightRenderer;
            for (int side = 0; side < 2; side++) CreateSurface(side);

            var all = GetComponentsInChildren<Renderer>(true);
            var selected = new System.Collections.Generic.List<Renderer>();
            foreach (var renderer in all)
            {
                string n = renderer.name;
                if (n == "Silk bookmark" || n.StartsWith("Clip ") || n == "Brass clip plate")
                    selected.Add(renderer);
            }
            accessories = selected.ToArray();
            accessoryVisibility = new bool[accessories.Length];
            for (int i = 0; i < accessories.Length; i++) accessoryVisibility[i] = accessories[i].enabled;
            initialized = true;
            SetPaper(JournalSide.Left, originalMaterials[0].GetTexture(BaseMap) as Texture2D);
            SetPaper(JournalSide.Right, originalMaterials[1].GetTexture(BaseMap) as Texture2D);
            return true;
        }

        private void CreateSurface(int side)
        {
            var renderer = source[side];
            var filter = renderer.GetComponent<MeshFilter>();
            Bounds localBounds = filter && filter.sharedMesh
                ? filter.sharedMesh.bounds : new Bounds(Vector3.zero, new Vector3(.393f, 0, .52f));
            sizes[side] = new Vector2(localBounds.size.x, localBounds.size.z);
            if (sizes[side].x < .01f || sizes[side].y < .01f) sizes[side] = new Vector2(.393f, .52f);
            originalMaterials[side] = renderer.sharedMaterial;
            var backing = new Material(renderer.sharedMaterial) { name = "Runtime page backing " + side };
            backing.SetTexture(BaseMap, null);
            backing.SetColor(BaseColor, new Color(.94f, .89f, .77f, 1));
            backing.SetFloat("_Metallic", 0);
            backing.SetFloat("_Smoothness", .08f);
            renderer.sharedMaterial = backing;
            backingMaterials[side] = backing;

            var anchor = new GameObject(side == 0 ? "LeftPageCoordinates" : "RightPageCoordinates");
            anchor.transform.SetParent(renderer.transform, false);
            anchor.transform.localPosition = new Vector3(localBounds.center.x - sizes[side].x * .5f,
                localBounds.center.y + .0008f, localBounds.center.z - sizes[side].y * .5f);
            // Anchor local X is page-right, local Y is page-top (+table Z).
            // Its local Z points into the paper; decoration fronts can face -Z.
            anchor.transform.localRotation = Quaternion.Euler(90, 0, 0);
            surfaces[side] = anchor.transform;

            var art = new GameObject(side == 0 ? "LeftPaperArtwork" : "RightPaperArtwork");
            art.transform.SetParent(renderer.transform, false);
            art.transform.localPosition = new Vector3(localBounds.center.x, localBounds.center.y + .0011f, localBounds.center.z);
            var mesh = new Mesh { name = "Runtime aspect fitted page " + side };
            art.AddComponent<MeshFilter>().sharedMesh = mesh;
            var surfaceRenderer = art.AddComponent<MeshRenderer>();
            var material = new Material(originalMaterials[side]) { name = "Runtime paper artwork " + side };
            material.SetColor(BaseColor, paperAlbedo);
            material.SetFloat("_Metallic", 0);
            material.SetFloat("_Smoothness", .06f);
            surfaceRenderer.sharedMaterial = material;
            surfaceRenderer.shadowCastingMode = ShadowCastingMode.Off;
            surfaceRenderer.receiveShadows = true;
            artObjects[side] = art;
            artMeshes[side] = mesh;
            artMaterials[side] = material;
        }

        public void Apply(Texture2D left, Texture2D right)
        {
            if (!Initialize()) return;
            SetPaper(JournalSide.Left, left);
            SetPaper(JournalSide.Right, right);
        }

        public void SetPaper(JournalSide side, Texture2D texture)
        {
            if (!initialized && !Initialize()) return;
            int i = side == JournalSide.Left ? 0 : 1;
            if (i == 0) LeftTexture = texture; else RightTexture = texture;
            artObjects[i].SetActive(texture);
            if (!texture) return;
            artMaterials[i].SetTexture(BaseMap, texture);
            artMaterials[i].SetTextureScale("_BaseMap", Vector2.one);
            artMaterials[i].SetTextureOffset("_BaseMap", Vector2.zero);

            Vector2 fit = texture == continuousPaperStock ? sizes[i] : FitSize(sizes[i], texture);
            float x = fit.x * .5f, z = fit.y * .5f;
            var mesh = artMeshes[i];
            mesh.Clear();
            mesh.vertices = new[] { new Vector3(-x, 0, -z), new Vector3(-x, 0, z), new Vector3(x, 0, z), new Vector3(x, 0, -z) };
            mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        public static Vector2 FitSize(Vector2 bounds, Texture2D texture)
        {
            if (!texture || texture.height == 0) return bounds;
            float aspect = (float)texture.width / texture.height;
            float height = Mathf.Min(bounds.y, bounds.x / aspect);
            return new Vector2(height * aspect, height);
        }

        /// <summary>
        /// Stable physical-page coordinates, independent of artwork letterboxing:
        /// (0,0) near-left, (1,1) far-right, X right and Y toward the top of the page.
        /// </summary>
        public Transform GetSurface(JournalSide side)
        {
            if (!Initialize()) return null;
            return surfaces[side == JournalSide.Left ? 0 : 1];
        }

        public Vector2 SurfaceSize(JournalSide side)
        {
            Initialize();
            return sizes[side == JournalSide.Left ? 0 : 1];
        }

        public Vector3 WorldFromNormalized(JournalSide side, Vector2 position)
        {
            Transform anchor = GetSurface(side);
            if (!anchor) return transform.position;
            Vector2 size = SurfaceSize(side);
            return anchor.TransformPoint(new Vector3(position.x * size.x, position.y * size.y, 0));
        }

        public bool TryWorldToNormalized(JournalSide side, Vector3 world, out Vector2 position)
        {
            Transform anchor = GetSurface(side);
            position = Vector2.zero;
            if (!anchor) return false;
            Vector2 size = SurfaceSize(side);
            Vector3 local = anchor.InverseTransformPoint(world);
            position = new Vector2(local.x / size.x, local.y / size.y);
            return Mathf.Abs(local.z) <= .02f && position.x >= 0 && position.x <= 1 && position.y >= 0 && position.y <= 1;
        }

        public void SetEditing(bool enabled)
        {
            if (!Initialize() || editing == enabled) return;
            editing = enabled;
            for (int i = 0; i < accessories.Length; i++)
                if (accessories[i]) accessories[i].enabled = !enabled && accessoryVisibility[i];
        }

        private void OnDestroy()
        {
            if (!initialized) return;
            for (int i = 0; i < 2; i++)
            {
                if (source[i]) source[i].sharedMaterial = originalMaterials[i];
                DestroyOwned(backingMaterials[i]);
                DestroyOwned(artMaterials[i]);
                DestroyOwned(artMeshes[i]);
                if (artObjects[i]) DestroyOwned(artObjects[i]);
                if (surfaces[i]) DestroyOwned(surfaces[i].gameObject);
            }
            for (int i = 0; i < accessories.Length; i++)
                if (accessories[i]) accessories[i].enabled = accessoryVisibility[i];
        }

        private static void DestroyOwned(Object value)
        {
            if (!value) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
