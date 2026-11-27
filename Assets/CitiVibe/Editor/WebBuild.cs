using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace CitiVibe.EditorTools
{
    /// <summary>Builds the MiniCity scene as a self-contained web player (Builds/Web) that any static host can serve.</summary>
    public static class WebBuild
    {
        const string Scene = "Assets/CitiVibe/Scenes/MiniCity.unity";

        [MenuItem("CitiVibe/Build Web Player")]
        static void Menu() => Debug.Log(Build());

        public static string Build(string output = "Builds/Web")
        {
            PlayerSettings.productName = "Rainlight Square";
            PlayerSettings.companyName = "CitiVibe";
            PlayerSettings.WebGL.template = "PROJECT:CitiVibe";
            // Gzip plus the JS decompression fallback runs on hosts that don't send Content-Encoding headers.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
            PlayerSettings.WebGL.decompressionFallback = true;
            PlayerSettings.WebGL.dataCaching = true;
            ConfigureWebPipeline();
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = output,
                target = BuildTarget.WebGL,
                targetGroup = BuildTargetGroup.WebGL,
                options = BuildOptions.None,
            });
            var summary = report.summary;
            return $"{summary.result}: {summary.totalSize / 1e6:F1} MB to {output} in {summary.totalTime:mm\\:ss}, {summary.totalErrors} errors, {summary.totalWarnings} warnings";
        }

        /// <summary>
        /// WebGL 2 cannot run URP's Forward+ path (every draw fails with a sampler-type mismatch), so the
        /// quality level the Web platform uses renders with plain Forward and full-diorama shadows.
        /// </summary>
        static void ConfigureWebPipeline()
        {
            int level = QualitySettings.GetQualityLevel();
            var asset = QualitySettings.GetRenderPipelineAssetAt(WebQualityLevel()) as UniversalRenderPipelineAsset;
            if (asset == null) return;
            var so = new SerializedObject(asset);
            so.FindProperty("m_ShadowDistance").floatValue = 330f;
            so.FindProperty("m_MainLightShadowmapResolution").intValue = 2048;
            so.FindProperty("m_AdditionalLightsPerObjectLimit").intValue = 8;
            so.ApplyModifiedProperties();
            var renderers = so.FindProperty("m_RendererDataList");
            for (int i = 0; i < renderers.arraySize; i++)
            {
                var data = renderers.GetArrayElementAtIndex(i).objectReferenceValue;
                if (data == null) continue;
                var rso = new SerializedObject(data);
                var mode = rso.FindProperty("m_RenderingMode");
                if (mode != null && mode.intValue != 0)
                {
                    mode.intValue = 0;
                    rso.ApplyModifiedProperties();
                    EditorUtility.SetDirty(data);
                }
            }
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            QualitySettings.SetQualityLevel(level, false);
        }

        static int WebQualityLevel()
        {
            var so = new SerializedObject(AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/QualitySettings.asset"));
            var defaults = so.FindProperty("m_PerPlatformDefaultQuality");
            for (int i = 0; i < defaults.arraySize; i++)
            {
                var entry = defaults.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("first").stringValue == "WebGL") return entry.FindPropertyRelative("second").intValue;
            }
            return 0;
        }
    }
}
