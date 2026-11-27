using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace CitiVibe
{
    /// <summary>
    /// Sun and moon, sky, ambient light and every after-dark effect: lit windows, storefronts,
    /// street and park lamps, headlights. Keys: 1 morning, 2 noon, 3 dusk, 4 night, T pause time, [ ] scrub.
    /// </summary>
    public class DayNight : MonoBehaviour
    {
        [Serializable]
        public class Glow
        {
            public Material material;
            public Color night = Color.white;
            public Color day = Color.black;
            [NonSerialized] public Material runtime;
        }

        [Range(0f, 24f)] public float hour = 16.8f;
        public float minutesPerDay = 6f;
        public bool advance = true;
        public Light sun;
        public Camera viewCamera;
        public Material sky;
        public List<Glow> glows = new();
        public List<Light> lamps = new();
        public float lampIntensity = 6f;
        public ReflectionProbe reflections;

        Material skyRuntime;
        float ambientTimer;
        static readonly int Emission = Shader.PropertyToID("_EmissionColor");

        static readonly Color DaySky = new(0.62f, 0.68f, 0.76f), DayEquator = new(0.58f, 0.57f, 0.53f), DayGround = new(0.32f, 0.29f, 0.25f);
        static readonly Color NightSky = new(0.07f, 0.09f, 0.16f), NightEquator = new(0.05f, 0.06f, 0.09f), NightGround = new(0.03f, 0.03f, 0.04f);
        static readonly Color DayBackground = new(0.88f, 0.87f, 0.83f), DuskBackground = new(0.75f, 0.58f, 0.48f), NightBackground = new(0.05f, 0.06f, 0.1f);

        void Start()
        {
            foreach (var g in glows)
            {
                g.runtime = RuntimeMaterials.Instance(g.material);
                if (g.runtime) g.runtime.EnableKeyword("_EMISSION");
            }
            if (sky)
            {
                skyRuntime = new Material(sky);
                RenderSettings.skybox = skyRuntime;
            }
            Apply();
        }

        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.digit1Key.wasPressedThisFrame) hour = 8f;
                if (kb.digit2Key.wasPressedThisFrame) hour = 13f;
                if (kb.digit3Key.wasPressedThisFrame) hour = 18.6f;
                if (kb.digit4Key.wasPressedThisFrame) hour = 22.5f;
                if (kb.tKey.wasPressedThisFrame) advance = !advance;
                if (kb.leftBracketKey.isPressed) hour -= Time.deltaTime * 2f;
                if (kb.rightBracketKey.isPressed) hour += Time.deltaTime * 2f;
            }
#endif
            if (advance && minutesPerDay > 0f) hour += Time.deltaTime * 24f / (minutesPerDay * 60f);
            hour = Mathf.Repeat(hour, 24f);
            Apply();
        }

        /// <summary>Sun elevation in degrees: peaks near 1 pm, below the horizon from ~7 pm to ~5 am.</summary>
        public static float Elevation(float h) => 64f * Mathf.Sin((h - 6.2f) / 13.4f * Mathf.PI) - (h < 6.2f || h > 19.6f ? 8f : 0f);

        void Apply()
        {
            float elev = Elevation(hour);
            float day = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-6f, 14f, elev));
            float night = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-5f, 7f, elev));
            float golden = Mathf.Clamp01(1f - Mathf.Abs(elev - 4f) / 14f) * (elev > -6f ? 1f : 0f);

            if (sun)
            {
                float azimuth = -95f + (hour - 6f) * 14f;
                if (elev > 0f)
                {
                    sun.transform.rotation = Quaternion.Euler(Mathf.Max(elev, 3f), azimuth, 0f);
                    sun.color = Color.Lerp(new Color(1f, 0.86f, 0.7f), new Color(1f, 0.6f, 0.36f), golden);
                    sun.intensity = 1.7f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, 10f, elev));
                }
                else
                {
                    sun.transform.rotation = Quaternion.Euler(38f, azimuth + 160f, 0f);
                    sun.color = new Color(0.55f, 0.66f, 1f);
                    sun.intensity = 0.22f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, -8f, elev));
                }
                sun.shadowStrength = elev > 0f ? 0.85f : 0.5f;
            }

            RenderSettings.ambientSkyColor = Color.Lerp(NightSky, DaySky, day) + new Color(0.12f, 0.05f, 0f) * golden;
            RenderSettings.ambientEquatorColor = Color.Lerp(NightEquator, DayEquator, day) + new Color(0.14f, 0.06f, 0.02f) * golden;
            RenderSettings.ambientGroundColor = Color.Lerp(NightGround, DayGround, day);
            if (skyRuntime)
            {
                skyRuntime.SetFloat("_Exposure", Mathf.Lerp(0.06f, 1.15f, day));
                skyRuntime.SetFloat("_AtmosphereThickness", Mathf.Lerp(0.85f, 1.6f, golden));
            }
            if (viewCamera)
                viewCamera.backgroundColor = Color.Lerp(Color.Lerp(NightBackground, DayBackground, day), DuskBackground, golden * 0.6f);

            if (reflections) reflections.intensity = Mathf.Lerp(0.12f, 1f, day);
            foreach (var g in glows)
                if (g.runtime) g.runtime.SetColor(Emission, Color.Lerp(g.day, g.night, night));
            foreach (var lamp in lamps)
            {
                if (!lamp) continue;
                lamp.enabled = night > 0.02f;
                // Park lanterns hang low, so they need far less output than the 8 m street luminaires.
                lamp.intensity = lampIntensity * night * (lamp.type == LightType.Spot ? 1f : 0.3f);
            }
            ambientTimer -= Time.deltaTime;
            if (ambientTimer <= 0f)
            {
                ambientTimer = 0.5f;
                DynamicGI.UpdateEnvironment();
            }
        }
    }
}
