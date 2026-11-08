using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VintageJournal
{
    /// <summary>A deterministic segmented paper turn. It has no camera or document ownership.</summary>
    [DisallowMultipleComponent]
    public sealed class JournalFlipController : MonoBehaviour
    {
        [Range(.3f, 1.3f)] public float duration = .65f;
        public float pageWidth = .393f;
        public float pageDepth = .52f;
        public float spineGap = .0125f;
        public float surfaceHeight = .057f;
        public bool IsFlipping { get; private set; }
        public float Progress { get; private set; }

        private int direction;
        private Action midpointCallback, completedCallback;
        private bool midpointCalled;
        private Texture2D frontTexture, backTexture;
        private GameObject paperObject;
        private Mesh paperMesh;
        private Material paperMaterial, frontMaterial, backMaterial;
        private readonly List<Vector3> vertices = new List<Vector3>();
        private readonly List<Vector2> uvs = new List<Vector2>();
        private readonly List<int>[] triangles = { new List<int>(), new List<int>(), new List<int>() };
        private const int Segments = 28;

        /// <param name="turnDirection">+1 turns right to left; -1 turns left to right.</param>
        /// <param name="front">The old exposed page.</param>
        /// <param name="back">The newly revealed reverse side.</param>
        public bool StartFlip(int turnDirection, Texture2D front, Texture2D back, Action midpoint, Action completed)
        {
            if (IsFlipping || turnDirection == 0 || !isActiveAndEnabled) return false;
            direction = turnDirection > 0 ? 1 : -1;
            frontTexture = front;
            backTexture = back;
            midpointCallback = midpoint;
            completedCallback = completed;
            midpointCalled = false;
            Progress = 0;
            IsFlipping = true;
            CreatePaper();
            UpdateMesh();
            Debug.Log("[Journal] Flip:Start:" + direction);
            return true;
        }

        private void CreatePaper()
        {
            paperObject = new GameObject("Turning paper (runtime)");
            paperObject.transform.SetParent(transform, false);
            paperObject.transform.localPosition = new Vector3(0, surfaceHeight, 0);
            paperMesh = new Mesh { name = "Runtime segmented turning paper" };
            paperMesh.MarkDynamic();
            paperObject.AddComponent<MeshFilter>().sharedMesh = paperMesh;
            var renderer = paperObject.AddComponent<MeshRenderer>();
            paperMaterial = NewMaterial("Turning paper fibers", null, new Color(.94f, .89f, .77f, 1));
            frontMaterial = NewMaterial("Turning front artwork", frontTexture, Color.white);
            backMaterial = NewMaterial("Turning reverse artwork", backTexture, Color.white);
            renderer.sharedMaterials = new[] { paperMaterial, frontMaterial, backMaterial };
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        private static Material NewMaterial(string name, Texture2D texture, Color color)
        {
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetTexture("_BaseMap", texture);
            material.SetFloat("_Metallic", 0);
            material.SetFloat("_Smoothness", .07f);
            return material;
        }

        private void Update()
        {
            if (!IsFlipping) return;
            Progress = Mathf.Min(1, Progress + Time.unscaledDeltaTime / Mathf.Max(.05f, duration));
            UpdateMesh();
            if (!midpointCalled && Progress >= .5f) InvokeMidpoint();
            if (IsFlipping && Progress >= 1) Finish(true);
        }

        private void UpdateMesh()
        {
            if (!paperMesh) return;
            vertices.Clear(); uvs.Clear();
            foreach (var indices in triangles) indices.Clear();
            AppendFace(0, Vector2.zero, Vector2.one, false, .0000f);
            AppendFace(0, Vector2.zero, Vector2.one, true, -.0006f);
            FitBounds(frontTexture, out Vector2 frontMin, out Vector2 frontMax);
            FitBounds(backTexture, out Vector2 backMin, out Vector2 backMax);
            if (frontTexture) AppendFace(1, frontMin, frontMax, false, .0008f);
            if (backTexture) AppendFace(2, backMin, backMax, true, -.0011f);
            paperMesh.Clear();
            paperMesh.SetVertices(vertices);
            paperMesh.SetUVs(0, uvs);
            paperMesh.subMeshCount = 3;
            for (int i = 0; i < 3; i++) paperMesh.SetTriangles(triangles[i], i, false);
            paperMesh.RecalculateNormals();
            paperMesh.RecalculateBounds();
        }

        private void FitBounds(Texture2D texture, out Vector2 min, out Vector2 max)
        {
            Vector2 fitted = JournalPageView.FitSize(new Vector2(pageWidth, pageDepth), texture);
            Vector2 fraction = new Vector2(fitted.x / pageWidth, fitted.y / pageDepth);
            min = (Vector2.one - fraction) * .5f;
            max = Vector2.one - min;
        }

        private void AppendFace(int submesh, Vector2 min, Vector2 max, bool back, float offset)
        {
            int start = vertices.Count;
            float eased = Progress * Progress * (3 - 2 * Progress);
            float angle = eased * Mathf.PI;
            Vector3 normal = new Vector3(-direction * Mathf.Sin(angle), Mathf.Cos(angle), 0);
            for (int segment = 0; segment <= Segments; segment++)
            {
                float q = segment / (float)Segments;
                float u = Mathf.Lerp(min.x, max.x, q);
                float radius = spineGap + u * pageWidth;
                // A shallow, smooth arch changes continuously through the turn.
                float curl = Mathf.Sin(u * Mathf.PI) * Mathf.Sin(angle) * .028f;
                float x = direction * radius * Mathf.Cos(angle);
                float y = radius * Mathf.Sin(angle) + curl + Mathf.Sin(angle) * .004f;
                for (int edge = 0; edge < 2; edge++)
                {
                    float v = edge == 0 ? min.y : max.y;
                    vertices.Add(new Vector3(x, y, (v - .5f) * pageDepth) + normal * offset);
                    bool mirror = (direction < 0) ^ back;
                    uvs.Add(new Vector2(mirror ? 1 - q : q, edge));
                }
            }
            bool reverse = (direction < 0) ^ back;
            var indices = triangles[submesh];
            for (int segment = 0; segment < Segments; segment++)
            {
                int a = start + segment * 2;
                if (!reverse)
                {
                    indices.Add(a); indices.Add(a + 1); indices.Add(a + 3);
                    indices.Add(a); indices.Add(a + 3); indices.Add(a + 2);
                }
                else
                {
                    indices.Add(a); indices.Add(a + 3); indices.Add(a + 1);
                    indices.Add(a); indices.Add(a + 2); indices.Add(a + 3);
                }
            }
        }

        private void InvokeMidpoint()
        {
            if (midpointCalled) return;
            midpointCalled = true;
            Action callback = midpointCallback;
            midpointCallback = null;
            callback?.Invoke();
        }

        /// <summary>
        /// complete=true commits the logical turn and calls both callbacks once.
        /// complete=false discards the temporary sheet without calling callbacks;
        /// the owning mode controller then decides its exit/document state.
        /// </summary>
        public void CancelFlip(bool complete)
        {
            if (!IsFlipping) return;
            Finish(complete);
            Debug.Log("[Journal] Flip:Cancel; complete=" + complete);
        }

        private void Finish(bool complete)
        {
            if (!IsFlipping) return;
            if (complete && !midpointCalled) InvokeMidpoint();
            IsFlipping = false;
            Progress = complete ? 1 : 0;
            Action completed = complete ? completedCallback : null;
            midpointCallback = null;
            completedCallback = null;
            Cleanup();
            if (complete) Debug.Log("[Journal] Flip:Complete");
            completed?.Invoke();
        }

        private void Cleanup()
        {
            DestroyOwned(paperObject);
            DestroyOwned(paperMesh);
            DestroyOwned(paperMaterial);
            DestroyOwned(frontMaterial);
            DestroyOwned(backMaterial);
            paperObject = null;
            paperMesh = null;
            paperMaterial = frontMaterial = backMaterial = null;
        }

        private void OnDisable() { CancelFlip(false); }
        private void OnDestroy() { Cleanup(); }

        private static void DestroyOwned(UnityEngine.Object value)
        {
            if (!value) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }
    }
}
