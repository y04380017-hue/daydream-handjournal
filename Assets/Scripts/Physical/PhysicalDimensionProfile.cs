using UnityEngine;

namespace VintageJournal
{
    public enum TravelersNotebookFormat { Standard, Passport, Custom }

    /// <summary>All dimensions are meters. Root transforms remain at unit scale; visual fitting is independent.</summary>
    [CreateAssetMenu(menuName = "Vintage Journal/Physical Dimension Profile")]
    public sealed class PhysicalDimensionProfile : ScriptableObject
    {
        public Vector3 size = new Vector3(.05f, .015f, .05f);
        public Vector3 colliderSize = new Vector3(.05f, .015f, .05f);
        public Vector3 grabOffset;
        public Vector3 colliderCenter;
        [Min(0)] public float minDistance = .05f;
        [Min(.05f)] public float maxDistance = 3f;
        public TravelersNotebookFormat notebookFormat;
        public Vector2 pageSize = new Vector2(.148f, .210f);
        public Vector2 allowedPlacementSize = new Vector2(1.3f, .65f);

        public bool IsValid => FinitePositive(size.x) && FinitePositive(size.y) && FinitePositive(size.z) &&
            FinitePositive(colliderSize.x) && FinitePositive(colliderSize.y) && FinitePositive(colliderSize.z) &&
            minDistance >= 0 && maxDistance > minDistance;
        static bool FinitePositive(float value) => value > 0 && !float.IsInfinity(value) && !float.IsNaN(value);
        public void ApplyCollider(BoxCollider target)
        { if (target && IsValid) { target.size = colliderSize; target.center = colliderCenter; } }
    }

    /// <summary>Explicit metric page bridge; existing normalized page decoration data is never rewritten.</summary>
    public static class PhysicalPageCoordinates
    {
        public static bool TryWorldToNormalized(Transform page, Vector2 metricSize, Vector3 world, out Vector2 uv)
        {
            uv = default;
            if (!page || metricSize.x <= 0 || metricSize.y <= 0) return false;
            Vector3 p = page.InverseTransformPoint(world);
            uv = new Vector2(p.x / metricSize.x + .5f, p.z / metricSize.y + .5f);
            return !float.IsNaN(uv.x) && !float.IsNaN(uv.y) && uv.x >= 0 && uv.x <= 1 && uv.y >= 0 && uv.y <= 1;
        }
        public static Vector3 NormalizedToWorld(Transform page, Vector2 metricSize, Vector2 uv)
        { return page.TransformPoint(new Vector3((uv.x - .5f) * metricSize.x, 0, (uv.y - .5f) * metricSize.y)); }
    }
}
