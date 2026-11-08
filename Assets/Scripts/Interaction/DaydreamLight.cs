using UnityEngine;

namespace VintageJournal
{
    [RequireComponent(typeof(Light))]
    public sealed class DaydreamLight : MonoBehaviour
    {
        [Range(0, .08f)] public float variation = .022f;
        [Min(20)] public float periodSeconds = 85f;
        Light source;
        float baseline;
        void OnEnable() { source = GetComponent<Light>(); baseline = source.intensity; }
        void Update() { source.intensity = baseline * (1 + variation * Mathf.Sin(Time.time * 2 * Mathf.PI / Mathf.Max(20, periodSeconds))); }
        void OnDisable() { if (source) source.intensity = baseline; }
    }
}
