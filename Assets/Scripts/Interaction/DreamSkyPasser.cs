using UnityEngine;

namespace VintageJournal
{
    /// <summary>A scheduled distant decoration. Its whole path stays outside the interaction volume.</summary>
    public sealed class DreamSkyPasser : MonoBehaviour
    {
        public DreamTimeController timeOfDay;
        public Transform visual;
        public Vector3 from = new Vector3(-5, 1.5f, 4), to = new Vector3(5, 2, 4);
        [Min(1)] public float period = 85, duration = 36;
        public float phaseSeconds, bob = .06f;
        public bool nightOnly, dayOnly, resetOnNightfall;
        float nightStart; bool inNight;
        public bool IsVisible => visual && visual.gameObject.activeInHierarchy;
        public int PassesStarted { get; private set; }
        bool wasVisible;

        void Update()
        {
            bool now = timeOfDay && timeOfDay.NightAmount > .65f;
            if(now && !inNight) nightStart=Time.time;
            inNight=now;
            Sample(resetOnNightfall ? Time.time-nightStart : Time.time);
        }

        public void Sample(float elapsed)
        {
            float cycle = Mathf.Repeat(elapsed + phaseSeconds, Mathf.Max(1, period));
            bool show = cycle < Mathf.Min(duration, period) && (!dayOnly || !timeOfDay || timeOfDay.NightAmount < .47f) && (!nightOnly || (timeOfDay && timeOfDay.NightAmount > .65f));
            if (!visual) return;
            visual.gameObject.SetActive(show);
            if (show)
            {
                float progress = cycle / Mathf.Max(1, duration);
                transform.localPosition = Vector3.Lerp(from, to, progress) + Vector3.up * (Mathf.Sin(progress * Mathf.PI * 4) * bob);
                visual.localRotation = Quaternion.Euler(0, 0, Mathf.Sin(progress * Mathf.PI * 2) * 4);
                if (!wasVisible) PassesStarted++;
            }
            wasVisible = show;
        }
    }
}
