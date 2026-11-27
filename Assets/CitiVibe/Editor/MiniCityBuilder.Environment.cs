using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CitiVibe.EditorTools
{
    public static partial class MiniCityBuilder
    {
        static void BuildEnvironment(GameObject systems, List<Light> lamps)
        {
            if (GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp)
            {
                // The whole diorama is in view, so shadows must reach every corner of the plinth.
                var so = new SerializedObject(urp);
                so.FindProperty("m_ShadowDistance").floatValue = 330f;
                so.FindProperty("m_MainLightShadowmapResolution").intValue = 4096;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(urp);
            }

            var env = new GameObject("Environment").transform;
            var sunGo = new GameObject("Sun");
            sunGo.transform.SetParent(env, false);
            sunGo.transform.rotation = Quaternion.Euler(42f, -60f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.9f, 0.78f);
            sun.intensity = 1.7f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.85f;
            sun.shadowNormalBias = 0.35f;
            sun.shadowBias = 0.05f;
            RenderSettings.sun = sun;

            var sky = new Material(Shader.Find("Skybox/Procedural")) { name = "City Sky" };
            sky.SetFloat("_SunSize", 0.035f);
            sky.SetFloat("_AtmosphereThickness", 0.85f);
            sky.SetFloat("_Exposure", 1.15f);
            sky.SetColor("_SkyTint", new Color(0.52f, 0.6f, 0.7f));
            AssetDatabase.CreateAsset(sky, $"{Gen}/Materials/City Sky.mat");
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.68f, 0.76f);
            RenderSettings.ambientEquatorColor = new Color(0.58f, 0.57f, 0.53f);
            RenderSettings.ambientGroundColor = new Color(0.32f, 0.29f, 0.25f);
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            DynamicGI.UpdateEnvironment();

            var camGo = new GameObject("City Camera") { tag = "MainCamera" };
            camGo.transform.SetParent(env, false);
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 74f;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 900f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.88f, 0.87f, 0.83f);
            camGo.AddComponent<AudioListener>();
            var controller = camGo.AddComponent<CityCamera>();
            controller.target = new Vector3(0f, 16f, 0f);
            controller.size = 74f;
            controller.bounds = new Vector2(EX, EZ);
            float yaw = controller.yaw * Mathf.Deg2Rad, pitch = controller.pitch * Mathf.Deg2Rad;
            camGo.transform.position = controller.target + new Vector3(Mathf.Sin(yaw) * Mathf.Cos(pitch), Mathf.Sin(pitch), Mathf.Cos(yaw) * Mathf.Cos(pitch)) * controller.distance;
            camGo.transform.LookAt(controller.target);
            var camData = cam.GetUniversalAdditionalCameraData();
            camData.renderPostProcessing = true;
            camData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            camData.antialiasingQuality = AntialiasingQuality.High;

            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, $"{Gen}/City Volume.asset");
            profile.Add<Tonemapping>(true).mode.Override(TonemappingMode.Neutral);
            var bloom = profile.Add<Bloom>(true);
            bloom.intensity.Override(0.45f);
            bloom.threshold.Override(1.0f);
            bloom.scatter.Override(0.65f);
            var color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.12f);
            color.contrast.Override(9f);
            color.saturation.Override(6f);
            profile.Add<Vignette>(true).intensity.Override(0.15f);
            var smh = profile.Add<ShadowsMidtonesHighlights>(true);
            smh.shadows.Override(new Vector4(0.96f, 0.98f, 1.06f, 0f));
            smh.highlights.Override(new Vector4(1.04f, 1.01f, 0.96f, 0f));
            foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
            var volumeGo = new GameObject("Post Processing");
            volumeGo.transform.SetParent(env, false);
            var volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;

            var probeGo = new GameObject("City Reflections");
            probeGo.transform.SetParent(env, false);
            probeGo.transform.position = new Vector3(0f, 25f, 0f);
            var probe = probeGo.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked;
            probe.size = new Vector3(EX * 2f, 80f, EZ * 2f);
            probe.resolution = 256;
            probe.importance = 1;

            var dayNight = systems.AddComponent<DayNight>();
            dayNight.sun = sun;
            dayNight.viewCamera = cam;
            dayNight.sky = sky;
            dayNight.lamps = lamps;
            dayNight.lampIntensity = 32f;
            dayNight.reflections = probe;
            void Glow(Material m, Color night, Color day = default) =>
                dayNight.glows.Add(new DayNight.Glow { material = m, night = night, day = day });
            Glow(M.litWarm, new Color(1f, 0.7f, 0.4f) * 1.6f);
            Glow(M.litCool, new Color(0.72f, 0.84f, 1f) * 1.15f);
            Glow(M.storeGlass, new Color(1f, 0.82f, 0.58f) * 1.4f);
            Glow(M.officeGlass, new Color(0.5f, 0.62f, 0.8f) * 0.16f);
            Glow(M.lampLens, new Color(1f, 0.85f, 0.62f) * 6f);
            Glow(M.headlight, new Color(1f, 0.97f, 0.9f) * 3.2f, new Color(0.3f, 0.3f, 0.28f));
            Glow(M.taillight, new Color(1f, 0.05f, 0.03f) * 2.2f, new Color(0.3f, 0.01f, 0.01f));
            Glow(M.taxiSign, new Color(1f, 0.95f, 0.8f) * 2f, new Color(0.3f, 0.28f, 0.2f));
            Glow(M.globeGreen, new Color(0.15f, 0.95f, 0.35f) * 3f, new Color(0.15f, 0.9f, 0.35f) * 0.6f);
            Glow(M.mapPanel, new Color(0.9f, 0.9f, 0.85f) * 1.3f);

            Lightmapping.BakeReflectionProbe(probe, $"{Gen}/CityReflections.exr");

            var view = SceneView.lastActiveSceneView;
            if (view != null)
            {
                view.orthographic = false;
                view.LookAt(controller.target, camGo.transform.rotation, 150f);
            }
        }
    }
}
