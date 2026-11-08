using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VintageJournal
{
    /// <summary>Owns sky/lighting presets. The camera and physical workspace remain independently controlled.</summary>
    [DefaultExecutionOrder(-90)]
    public sealed class DreamTimeController : MonoBehaviour
    {
        const string Preference = "VintageJournal.DreamNight";
        const string DuskPreference = "VintageJournal.DreamDusk";
        public Light sun, fill, bounce, workLamp;
        public LuminousAtelier atmosphere;
        public DaydreamBreath breath;
        public Renderer sky;
        public GameObject moon;
        [Tooltip("Optional dreamcore night layer. When bound, night uses its lavender palette, staged blend and slower transition.")]
        public NightDreamcore dreamcore;
        [Min(.1f)] public float nightTransitionSeconds = 8f;
        [Min(.1f)] public float transitionSeconds = 2.5f;
        /// <summary>Seconds a full day/night change takes (longer when the dreamcore layer is bound). Tests wait on this.</summary>
        public float NightTransitionDuration => dreamcore ? nightTransitionSeconds : transitionSeconds;
        [Header("Day palette (night stays fixed)")]
        public Color daySun = new Color(1, .97f, .91f), dayFill = new Color(.86f, .92f, 1);
        public Color dayAmbientSky = new Color(.63f, .73f, .84f), dayAmbientEquator = new Color(.70f, .67f, .69f), dayAmbientGround = new Color(.65f, .59f, .57f);
        public Color dayFog = new Color(.84f, .91f, .95f);
        [Header("Dusk palette (pink-orange, optional third preset)")]
        public Color duskSun = new Color(1f, .79f, .66f), duskFill = new Color(.89f, .79f, .94f);
        public Color duskAmbientSky = new Color(.78f, .70f, .85f), duskAmbientEquator = new Color(.88f, .73f, .73f), duskAmbientGround = new Color(.73f, .61f, .65f);
        public Color duskFog = new Color(.95f, .79f, .78f);
        [Range(.2f, 1)] public float duskSunMultiplier = .7f;
        public bool IsDusk { get; private set; }
        public float DuskAmount { get; private set; }
        public bool IsNight { get; private set; }
        public float NightAmount { get; private set; }
        public bool IsTransitioning => Mathf.Abs(NightAmount - (IsNight ? 1 : 0)) > .001f || Mathf.Abs(DuskAmount - (IsDusk ? 1 : 0)) > .001f;
        MaterialPropertyBlock skyProperties;

        void Start()
        {
            var args = Environment.GetCommandLineArgs();
            bool isolated = Array.Exists(args, a => a.Contains("validation") || a.Contains("probe"));
            bool night = !isolated && PlayerPrefs.GetInt(Preference, 0) == 1;
            if (Array.Exists(args, a => a == "--dream-night")) night = true;
            bool dusk = !night && !isolated && PlayerPrefs.GetInt(DuskPreference, 0) == 1;
            if (Array.Exists(args, a => a == "--dream-dusk")) { dusk = true; night = false; }
            SetNight(night, false, true);
            if (dusk) SetDusk(true, false, true);
        }

        /// <summary>Dusk is a variant of the daytime preset; choosing it leaves night.</summary>
        public void SetDusk(bool value, bool remember = true, bool immediate = false)
        {
            IsDusk = value; if (value) IsNight = false;
            if (remember) { PlayerPrefs.SetInt(DuskPreference, value ? 1 : 0); PlayerPrefs.SetInt(Preference, 0); PlayerPrefs.Save(); }
            if (immediate) { DuskAmount = value ? 1 : 0; if (value) NightAmount = 0; }
            ApplyLighting();
        }

        public void SetNight(bool value, bool remember = true, bool immediate = false)
        {
            IsNight = value; IsDusk = false;
            if (remember) { PlayerPrefs.SetInt(DuskPreference, 0); }
            if (immediate) DuskAmount = 0;
            if (remember) { PlayerPrefs.SetInt(Preference, value ? 1 : 0); PlayerPrefs.Save(); }
            if (immediate) NightAmount = value ? 1 : 0;
            ApplyLighting();
        }

        void Update()
        {
            // Night fades over 6–10 s with the dreamcore layer (objects "wake up" as the light fades); dusk/day keep 2.5 s.
            float nightSeconds = dreamcore ? nightTransitionSeconds : transitionSeconds;
            NightAmount = Mathf.MoveTowards(NightAmount, IsNight ? 1 : 0,
                Time.unscaledDeltaTime / Mathf.Max(.1f, nightSeconds));
            DuskAmount = Mathf.MoveTowards(DuskAmount, IsDusk ? 1 : 0, Time.unscaledDeltaTime / Mathf.Max(.1f, transitionSeconds));
            ApplyLighting();
        }

        void ApplyLighting()
        {
            float blend = Mathf.SmoothStep(0, 1, NightAmount), dusk = Mathf.SmoothStep(0, 1, DuskAmount);
            Color DaySide(Color day, Color evening) => Color.Lerp(day, evening, dusk);
            // With the dreamcore layer, lights/sky follow its 20–80 % stage and haze its 0–40 % stage; night colours come from its palette.
            bool dream = dreamcore;
            float lit = dream ? NightDreamcore.Lights(NightAmount) : blend, haze = dream ? NightDreamcore.Haze(NightAmount) : blend;
            Color NightOf(Color luminous, Func<Color> dreamColor) => dream ? dreamColor() : luminous;
            if (sky)
            {
                skyProperties ??= new MaterialPropertyBlock();
                sky.GetPropertyBlock(skyProperties);
                skyProperties.SetFloat("_Night", lit); skyProperties.SetFloat("_Dusk", dusk);
                sky.SetPropertyBlock(skyProperties);
            }
            if (sun) sun.color = Color.Lerp(DaySide(daySun, duskSun), NightOf(atmosphere ? atmosphere.nightKey : new Color(.59f, .67f, 1), () => dreamcore.moonKey), lit);
            if (breath) { breath.SunMultiplier = Mathf.Lerp(Mathf.Lerp(.82f, .82f * duskSunMultiplier, dusk), dream ? dreamcore.moonMultiplier : .23f, lit); breath.ExposureOffset = Mathf.Lerp(-.08f, dream ? dreamcore.nightExposure : -.18f, lit); }
            if (fill) { fill.color = Color.Lerp(DaySide(dayFill, duskFill), NightOf(atmosphere ? atmosphere.nightFill : new Color(.63f, .66f, 1), () => dreamcore.moonKey), lit); fill.intensity = Mathf.Lerp(.28f, dream ? .14f : atmosphere ? .23f : .15f, lit); }
            if (bounce) bounce.intensity = Mathf.Lerp(.22f, .08f, lit);
            if (workLamp) workLamp.intensity = Mathf.Lerp(.018f, dream ? dreamcore.tablePoolIntensity : atmosphere ? .16f : .09f, lit);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Color.Lerp(DaySide(dayAmbientSky, duskAmbientSky), NightOf(atmosphere ? atmosphere.skyAmbient : new Color(.28f, .33f, .48f), () => dreamcore.ambientSky * dreamcore.ambientIntensity), lit);
            RenderSettings.ambientEquatorColor = Color.Lerp(DaySide(dayAmbientEquator, duskAmbientEquator), NightOf(atmosphere ? atmosphere.sideAmbient : new Color(.35f, .33f, .43f), () => Color.Lerp(dreamcore.ambientSky, dreamcore.ambientGround, .5f) * dreamcore.ambientIntensity), lit);
            RenderSettings.ambientGroundColor = Color.Lerp(DaySide(dayAmbientGround, duskAmbientGround), NightOf(atmosphere ? atmosphere.floorAmbient : new Color(.25f, .22f, .31f), () => dreamcore.ambientGround * dreamcore.ambientIntensity), lit);
            RenderSettings.fogColor = Color.Lerp(DaySide(dayFog, duskFog), NightOf(atmosphere ? atmosphere.nightFog : new Color(.23f, .27f, .46f), () => dreamcore.fog), haze);
            if (moon) moon.SetActive(lit > .45f);
            if (atmosphere) atmosphere.Apply(blend);
            if (dream) dreamcore.Apply(this, NightAmount);
        }
    }
}
