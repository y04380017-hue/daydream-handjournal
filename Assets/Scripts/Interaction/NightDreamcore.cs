using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace VintageJournal
{
    /// <summary>
    /// "Same dream, lights changed": the night presentation layer. Driven only by DreamTimeController's raw night amount.
    /// Staging follows the spec: haze and grading 0–40 %, lights and sky 20–80 %, emissives and particles 60–100 %.
    /// Presentation only — never touches the camera, colliders, interactables, anchors or save data.
    /// Rollback: menu "Vintage Journal/Daydream/Remove night dreamcore" (or delete the "Night dreamcore" root).
    /// </summary>
    [DefaultExecutionOrder(-80)]
    public sealed class NightDreamcore : MonoBehaviour
    {
        [Serializable]
        public struct Glow
        {
            public Renderer renderer;
            public Color color;
            public float intensity;
            [Tooltip("Relative pulse amplitude, e.g. .1 = ±10 %")] public float pulse;
            public float period;
            public float phase;
            [Tooltip("0 = follows the light stage, 1 = follows the emissive stage")] public int stage;
        }

        [Header("Palette (spec)")]
        public Color moonKey = Hex(0xC7C3F0), ambientSky = Hex(0x8E8BC4), ambientGround = Hex(0x6E5A8A), fog = Hex(0xB8B2DA);
        public Color tablePool = Hex(0xF6D9B8), underGlow = Hex(0xCDE8F2), rim = Hex(0xE8C7E6), lavenderSoft = Hex(0xD9D3EE), lanternCream = Hex(0xFFF1C9);
        public Color skyTop = Hex(0x3B3F6E), skyMid = Hex(0x6F6AA3), skyHorizon = Hex(0xC9A9C9), skyBelow = Hex(0x5F5B94);
        [Range(0, 1.5f)] public float ambientIntensity = .72f;
        public float tablePoolIntensity = .22f, overheadPoolBoost = 1.7f, underGlowIntensity = .12f;
        [Tooltip("Night sun multiplier handed to DaydreamBreath (the old night used .23).")] public float moonMultiplier = .16f;
        public float nightExposure = -.12f, nightShadowStrength = .22f;
        public float fogDensity = .035f, dayFogDensityAtSwitch = .012f;

        [Header("Bindings (authored by NightDreamcoreSetup)")]
        public Volume grade;
        public Light rimLight, underTableGlow;
        public Glow[] glows = Array.Empty<Glow>();
        public Renderer floor, cloudLayer;
        public Renderer[] lanternShells = Array.Empty<Renderer>(), lanternAureoles = Array.Empty<Renderer>();
        public Renderer[] darkNightSurfaces = Array.Empty<Renderer>();
        public DaydreamMotes motes;
        public AudioSource music;
        [Range(.5f, 3)] public float slowMotion = 1.4f;

        public static float Amount { get; private set; }   // raw 0..1, for UI styling
        public static float GlowAmount { get; private set; }
        public static float UiAmount { get; private set; }  // light stage, for IMGUI night styling
        public float HazeStage { get; private set; }
        public float LightStage { get; private set; }
        public float EmissiveStage { get; private set; }

        LuminousAtelier atmosphere; Light sun, workLamp; AudioLowPassFilter lowPass; AudioReverbFilter reverb; AudioSource hum;
        MaterialPropertyBlock block; FogMode dayFogMode; bool dayFogCaptured; float dayShadow = -1; float lastRaw = -1;
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor"), IntensityId = Shader.PropertyToID("_Intensity"), FadeId = Shader.PropertyToID("_Fade"), EmissionId = Shader.PropertyToID("_EmissionColor"), DreamNightId = Shader.PropertyToID("_DreamNight");

        static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);
        static float Stage(float raw, float from, float to) => Mathf.SmoothStep(0, 1, Mathf.Clamp01((raw - from) / (to - from)));
        public static float Haze(float raw) => Stage(raw, 0, .4f);
        public static float Lights(float raw) => Stage(raw, .2f, .8f);
        public static float Emissive(float raw) => Stage(raw, .6f, 1);

        void Awake()
        {
            block = new MaterialPropertyBlock();
            if (music)
            {
                lowPass = music.GetComponent<AudioLowPassFilter>(); if (!lowPass) lowPass = music.gameObject.AddComponent<AudioLowPassFilter>();
                reverb = music.GetComponent<AudioReverbFilter>(); if (!reverb) reverb = music.gameObject.AddComponent<AudioReverbFilter>();
                reverb.reverbPreset = AudioReverbPreset.User; reverb.decayTime = 4f; reverb.dryLevel = 0; reverb.room = -540; reverb.roomHF = -900;
                reverb.reflectionsLevel = -1200; reverb.reverbLevel = -300; reverb.diffusion = 100; reverb.density = 100;
                lowPass.enabled = reverb.enabled = false;
                hum = gameObject.AddComponent<AudioSource>(); hum.clip = RoomTone(); hum.loop = true; hum.playOnAwake = false; hum.volume = 0; hum.spatialBlend = 0;
            }
        }

        /// <summary>Called by DreamTimeController after it has applied the base lighting for this frame.</summary>
        public void Apply(DreamTimeController time, float raw)
        {
            Amount = raw; HazeStage = Haze(raw); LightStage = Lights(raw); EmissiveStage = Emissive(raw); GlowAmount = EmissiveStage; UiAmount = LightStage;
            if (!atmosphere) atmosphere = time.atmosphere;
            if (!sun) sun = time.sun; if (!workLamp) workLamp = time.workLamp;

            if (grade) grade.weight = HazeStage;
            if (!dayFogCaptured) { dayFogMode = RenderSettings.fogMode; dayFogCaptured = true; }
            if (HazeStage > .001f) { RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Exponential; RenderSettings.fogDensity = Mathf.Lerp(dayFogDensityAtSwitch, fogDensity, HazeStage); }
            else RenderSettings.fogMode = dayFogMode;

            float L = LightStage;
            if (sun)
            {
                if (dayShadow < 0) dayShadow = sun.shadowStrength;
                sun.shadowStrength = Mathf.Lerp(dayShadow, nightShadowStrength, L);
            }
            if (workLamp) workLamp.color = Color.Lerp(new Color(1, .886f, .671f), tablePool, L);
            float oldBlend = Mathf.SmoothStep(0, 1, raw);
            if (atmosphere)
            {
                // Same three lamps as the luminous pass, recoloured: cream pool over the journal, lavender wash, mauve rim.
                Recolor(atmosphere.overheadLantern, tablePool, atmosphere.overheadPower * oldBlend * overheadPoolBoost);
                Recolor(atmosphere.leftSoftbox, lavenderSoft, atmosphere.leftPower * oldBlend * .55f);
                Recolor(atmosphere.edgeLight, rim, atmosphere.edgePower * oldBlend * .7f);
            }
            Power(rimLight, .14f * L); Power(underTableGlow, underGlowIntensity * L);

            foreach (var r in lanternShells) if (r) { r.GetPropertyBlock(block); block.SetColor(EmissionId, Color.Lerp(new Color(1, .745f, .42f) * 2.2f, lanternCream * .9f, L).linear); r.SetPropertyBlock(block); }
            foreach (var r in lanternAureoles) if (r) { r.GetPropertyBlock(block); block.SetColor(BaseColorId, Color.Lerp(new Color(1, .53f, .22f, .48f), new Color(lanternCream.r, lanternCream.g, lanternCream.b, .26f), L)); r.SetPropertyBlock(block); }
            foreach (var r in darkNightSurfaces) if (r) r.enabled = L < .5f || raw < .01f;

            if (time.sky) { time.sky.GetPropertyBlock(block); block.SetFloat(DreamNightId, L); block.SetColor("_NightTop", skyTop); block.SetColor("_NightMid", skyMid); block.SetColor("_NightHorizon", skyHorizon); block.SetColor("_NightBelow", skyBelow); time.sky.SetPropertyBlock(block); }
            Fade(floor, L); Fade(cloudLayer, L);
            DaydreamFloat.TimeScale = 1f / Mathf.Lerp(1, slowMotion, L);
            DaydreamBreath.TimeScale = DaydreamFloat.TimeScale;
            if (motes) motes.NightAmount = EmissiveStage;
            if (lowPass) { lowPass.enabled = reverb.enabled = L > .001f; lowPass.cutoffFrequency = Mathf.Lerp(22000, 3200, L); reverb.room = Mathf.RoundToInt(Mathf.Lerp(-10000, -540, L)); }
            if (hum) { hum.volume = .045f * L; if (L > .001f && !hum.isPlaying) hum.Play(); else if (L <= .001f && hum.isPlaying) hum.Stop(); }
            lastRaw = raw;
        }

        void Update()
        {
            if (lastRaw < 0) return;
            float t = Time.unscaledTime;
            foreach (var g in glows)
            {
                if (!g.renderer) continue;
                float stage = g.stage == 1 ? EmissiveStage : LightStage;
                float pulse = g.pulse > 0 && g.period > 0 ? 1 + g.pulse * Mathf.Sin((t / g.period + g.phase) * 2 * Mathf.PI) : 1;
                float value = g.intensity * stage * pulse;
                g.renderer.enabled = value > .0005f;
                if (!g.renderer.enabled) continue;
                g.renderer.GetPropertyBlock(block); block.SetColor(BaseColorId, g.color); block.SetFloat(IntensityId, value); g.renderer.SetPropertyBlock(block);
            }
        }

        void OnDisable() { DaydreamFloat.TimeScale = 1; DaydreamBreath.TimeScale = 1; Amount = 0; GlowAmount = 0; UiAmount = 0; }

        void Recolor(Light lamp, Color color, float intensity)
        {
            if (!lamp) return;
            lamp.color = color;   // these lamps only ever burn at night
            lamp.intensity = intensity; lamp.enabled = intensity > .0005f;
        }
        static void Power(Light lamp, float intensity) { if (!lamp) return; lamp.intensity = intensity; lamp.enabled = intensity > .0005f; }
        void Fade(Renderer r, float value)
        {
            if (!r) return; r.enabled = value > .001f; if (!r.enabled) return;
            r.GetPropertyBlock(block); block.SetFloat(FadeId, value); r.SetPropertyBlock(block);
        }

        /// <summary>Quiet room tone: soft brown noise with a faint low drone, generated once (no audio asset needed).</summary>
        static AudioClip RoomTone()
        {
            const int rate = 22050, seconds = 8; var data = new float[rate * seconds]; var rand = new System.Random(7); float brown = 0;
            for (int i = 0; i < data.Length; i++)
            {
                brown = Mathf.Clamp(brown + ((float)rand.NextDouble() * 2 - 1) * .02f, -1, 1) * .998f;
                float drone = Mathf.Sin(2 * Mathf.PI * 55 * i / rate) * .25f + Mathf.Sin(2 * Mathf.PI * 82.5f * i / rate) * .12f;
                float edge = Mathf.Clamp01(Mathf.Min(i, data.Length - i) / (rate * .5f));   // seamless loop
                data[i] = (brown * .8f + drone * .35f) * Mathf.Lerp(.6f, 1, edge);
            }
            var clip = AudioClip.Create("Night room tone", data.Length, 1, rate, false); clip.SetData(data, 0); return clip;
        }
    }
}
