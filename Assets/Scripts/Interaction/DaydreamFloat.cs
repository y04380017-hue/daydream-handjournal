using UnityEngine;

namespace VintageJournal
{
    /// <summary>
    /// Gentle bob / drift for purely decorative objects in the floating sky room.
    /// Never attach to the desk, storage, interactables, page anchors or colliders.
    /// </summary>
    public sealed class DaydreamFloat : MonoBehaviour
    {
        public Vector3 bobAxis = Vector3.up;
        public float bobMetres = .02f, bobPeriod = 6f, swayDegrees = 2f, driftMetresPerSecond, driftSpan = 4f;
        public Vector3 driftAxis = Vector3.right;
        public float phase;
        Vector3 home; Quaternion homeRotation;
        /// <summary>Shared pace for all floats (night slows everything down by ~1.4x). 1 = original timing.</summary>
        public static float TimeScale = 1;
        static float clock; static int clockFrame = -1;
        public static float Clock { get { if (clockFrame != Time.frameCount) { clockFrame = Time.frameCount; clock += Time.deltaTime * TimeScale; } return clock; } }

        void Awake()
        {
            home = transform.localPosition; homeRotation = transform.localRotation;
            if (Mathf.Approximately(phase, 0)) phase = Mathf.Abs(home.x * 3.1f + home.z * 1.7f + home.y);
            foreach (var c in GetComponentsInChildren<Collider>()) if (c.enabled) Debug.LogWarning("[DaydreamFloat] Decorative float has a collider: " + name);
        }

        void Update()
        {
            float now = Clock, t = now + phase * bobPeriod;
            float w = 2 * Mathf.PI / Mathf.Max(.5f, bobPeriod);
            Vector3 drift = Vector3.zero;
            if (driftMetresPerSecond > 0 && driftSpan > 0)
                drift = driftAxis.normalized * (Mathf.Repeat(now * driftMetresPerSecond + phase * driftSpan, driftSpan) - driftSpan * .5f);
            transform.localPosition = home + bobAxis.normalized * (bobMetres * Mathf.Sin(t * w)) + drift;
            transform.localRotation = homeRotation * Quaternion.Euler(swayDegrees * Mathf.Sin(t * w * .7f), 0, swayDegrees * .6f * Mathf.Sin(t * w * .5f + 1));
        }
    }
}
