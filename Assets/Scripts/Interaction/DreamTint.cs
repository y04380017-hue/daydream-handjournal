using UnityEngine;

namespace VintageJournal
{
    /// <summary>
    /// Decorative colour per time preset (day / dusk / night), with optional breath-paced drift for light patches.
    /// Reads DreamTimeController and DaydreamBreath only; never touches gameplay objects or the camera.
    /// </summary>
    public sealed class DreamTint : MonoBehaviour
    {
        public Renderer[] targets = System.Array.Empty<Renderer>();
        public Color day = Color.white, dusk = Color.white, night = new Color(1, 1, 1, 0);
        [Tooltip("World-space drift in metres at full breath (light patches).")]
        public Vector3 breathDrift;
        [Range(0, .5f)] public float breathAlpha;
        DreamTimeController time; MaterialPropertyBlock block; Vector3 home;

        void Start() { time = FindFirstObjectByType<DreamTimeController>(); block = new MaterialPropertyBlock(); home = transform.position; Apply(); }
        void Update() { Apply(); }

        void Apply()
        {
            float n = time ? Mathf.SmoothStep(0, 1, time.NightAmount) : 0, d = time ? Mathf.SmoothStep(0, 1, time.DuskAmount) : 0;
            float b = DaydreamBreath.Phase;
            var c = Color.Lerp(Color.Lerp(day, dusk, d), night, n); c.a = Mathf.Clamp01(c.a * (1 + breathAlpha * b));
            foreach (var r in targets)
            {
                if (!r) continue; r.GetPropertyBlock(block); block.SetColor("_BaseColor", c); r.SetPropertyBlock(block);
            }
            if (breathDrift != Vector3.zero) transform.position = home + breathDrift * b;
        }
    }
}
