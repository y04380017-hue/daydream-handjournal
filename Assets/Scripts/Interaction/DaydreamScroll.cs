using UnityEngine;

namespace VintageJournal
{
    /// <summary>
    /// Slow texture drift for decorative surfaces (shallow water caustics, the far cloud sea).
    /// Brightness follows the shared DaydreamBreath phase. No colliders, no gameplay coupling.
    /// </summary>
    [RequireComponent(typeof(Renderer))]
    public sealed class DaydreamScroll : MonoBehaviour
    {
        public Vector2 speed = new Vector2(.004f, .002f);
        public Vector2 secondSpeed;
        [Range(0, .5f)] public float breathAlpha = .12f;
        Material material; Color baseColor; Vector2 baseOffset;

        void Start()
        {
            material = GetComponent<Renderer>().material;
            baseColor = material.GetColor("_BaseColor"); baseOffset = material.GetTextureOffset("_BaseMap");
        }

        void Update()
        {
            if (!material) return;
            float t = Time.time;
            var drift = speed * t + secondSpeed * Mathf.Sin(t * .21f);
            material.SetTextureOffset("_BaseMap", baseOffset + drift);
            var c = baseColor; c.a = Mathf.Clamp01(baseColor.a * (1 + breathAlpha * DaydreamBreath.Phase)); material.SetColor("_BaseColor", c);
        }

        void OnDestroy() { if (material) Destroy(material); }
    }
}
