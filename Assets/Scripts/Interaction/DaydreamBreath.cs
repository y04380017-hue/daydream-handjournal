using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VintageJournal
{
    /// <summary>
    /// One slow shared "breath" for the floating room: sunlight, exposure, bloom and the halo under the desk
    /// swell and settle together. Purely presentational; never touches the camera, colliders or interactables.
    /// </summary>
    public sealed class DaydreamBreath : MonoBehaviour
    {
        [Min(3)] public float periodSeconds = 9f;
        [Range(0, .15f)] public float lightAmount = .05f;
        [Range(0, .3f)] public float exposureAmount = .06f, bloomAmount = .10f;
        [Range(0, .15f)] public float haloScaleAmount = .05f;
        [Range(0, .5f)] public float haloAlphaAmount = .18f;
        public Light sun;
        public Volume grade;
        public Renderer halo;
        float sunBase, exposureBase, bloomBase, haloAlphaBase;
        Vector3 haloScaleBase;
        ColorAdjustments adjustments; Bloom bloom; Material haloMaterial; VolumeProfile runtimeProfile;
        public static float Phase { get; private set; }
        /// <summary>Night stretches the breath (1 = the daytime ~9 s rhythm).</summary>
        public static float TimeScale = 1;
        float clock;
        public float SunMultiplier { get; set; } = 1;
        public float ExposureOffset { get; set; }

        void OnEnable()
        {
            if (sun) sunBase = sun.intensity;
            if (grade && grade.sharedProfile)
            {
                runtimeProfile = Instantiate(grade.sharedProfile); grade.profile = runtimeProfile;
                if (runtimeProfile.TryGet(out adjustments)) exposureBase = adjustments.postExposure.value;
                if (runtimeProfile.TryGet(out bloom)) bloomBase = bloom.intensity.value;
            }
            if (halo) { haloMaterial = halo.material; haloAlphaBase = haloMaterial.GetColor("_BaseColor").a; haloScaleBase = halo.transform.localScale; }
        }

        void Update()
        {
            // Eased breath: a slow inhale and a slightly longer, softer exhale.
            clock += Time.deltaTime * TimeScale;
            float t = clock / Mathf.Max(3, periodSeconds);
            float s = Mathf.Sin(t * 2 * Mathf.PI); float b = s * (s > 0 ? 1f : .8f);
            Phase = b;
            if (sun) sun.intensity = sunBase * SunMultiplier * (1 + lightAmount * b);
            if (adjustments) adjustments.postExposure.value = exposureBase + ExposureOffset + exposureAmount * b;
            if (bloom) bloom.intensity.value = Mathf.Max(0, bloomBase + bloomAmount * b);
            if (haloMaterial)
            {
                halo.transform.localScale = haloScaleBase * (1 + haloScaleAmount * b);
                var c = haloMaterial.GetColor("_BaseColor"); c.a = Mathf.Clamp01(haloAlphaBase * (1 + haloAlphaAmount * b)); haloMaterial.SetColor("_BaseColor", c);
            }
        }

        void OnDisable()
        {
            if (sun) sun.intensity = sunBase;
            if (halo) halo.transform.localScale = haloScaleBase;
            if (grade && runtimeProfile) { grade.profile = null; Destroy(runtimeProfile); }
        }
    }
}
