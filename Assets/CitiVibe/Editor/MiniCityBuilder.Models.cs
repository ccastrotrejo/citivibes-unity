using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace CitiVibe.EditorTools
{
    public static partial class MiniCityBuilder
    {
        /// <summary>Points every Blender FBX at the generated URP materials by material name.</summary>
        static void ConfigureModels()
        {
            var remap = M.ModelRemap();
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { ModelDir }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not ModelImporter importer) continue;
                importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
                importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
                importer.animationType = ModelImporterAnimationType.None;
                importer.importAnimation = false;
                importer.importCameras = false;
                importer.importLights = false;
                importer.addCollider = false;
                importer.globalScale = 1f;
                importer.bakeAxisConversion = false;
                foreach (var kv in remap)
                    importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), kv.Key), kv.Value);
                importer.SaveAndReimport();
            }
        }

        static readonly Dictionary<string, GameObject> modelCache = new();

        static GameObject Model(string name)
        {
            if (modelCache.TryGetValue(name, out var go) && go != null) return go;
            go = AssetDatabase.LoadAssetAtPath<GameObject>($"{ModelDir}/{name}.fbx");
            if (go == null) throw new System.IO.FileNotFoundException($"Missing model {ModelDir}/{name}.fbx. Run Tools/Blender/citivibe_models.py first.");
            modelCache[name] = go;
            return go;
        }

        /// <summary>Instantiates a Blender model facing <paramref name="forward"/>, optionally swapping materials.</summary>
        static GameObject Spawn(string model, Transform parent, Vector3 position, Vector3 forward, Dictionary<Material, Material> swap = null,
            bool isStatic = true, float scale = 1f)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(Model(model), parent);
            forward.y = 0f;
            go.transform.SetPositionAndRotation(position, forward.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(forward) : Quaternion.identity);
            go.transform.localScale = Vector3.one * scale;
            if (swap != null) Recolor(go, swap);
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>())
            {
                r.shadowCastingMode = ShadowCastingMode.On;
                foreach (var m in r.sharedMaterials)
                    if (m == M.glassPanel) r.shadowCastingMode = ShadowCastingMode.Off;
            }
            if (isStatic)
                foreach (var t in go.GetComponentsInChildren<Transform>())
                    GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
            return go;
        }

        static void Recolor(GameObject go, Dictionary<Material, Material> swap)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && swap.TryGetValue(mats[i], out var to)) mats[i] = to;
                r.sharedMaterials = mats;
            }
        }
    }
}
