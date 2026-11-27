using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CitiVibe.EditorTools
{
    /// <summary>
    /// Builds Rainlight Square, an original NYC-inspired diorama: a 3 x 3 block grid with one-way
    /// avenues and streets, a central park, procedurally generated buildings and textures, and
    /// Blender-authored vehicles, people, trees and street furniture (Tools/Blender/citivibe_models.py).
    /// Run from the CitiVibe menu; everything under Assets/CitiVibe/Generated is regenerated.
    /// </summary>
    public static partial class MiniCityBuilder
    {
        const string Root = "Assets/CitiVibe";
        const string Gen = Root + "/Generated";
        const string ModelDir = Root + "/Models";
        const string ScenePath = Root + "/Scenes/MiniCity.unity";

        // Layout in metres. X = east, Z = north. Avenues run north-south, streets east-west.
        const float AV = 34f, AH = 9f, SZ = 28f, SH = 5f, SW = 5.5f, EX = 80f, EZ = 70f, Curb = 0.15f;
        /// <summary>Walk lines sit 3 m in from the road edge, inside the crosswalks.</summary>
        const float WalkInset = 3f;
        static readonly float[] Avenues = { -AV, AV };
        static readonly float[] Streets = { -SZ, SZ };

        static Mats M;
        static Mesh container;
        static System.Random rng;
        static int triangleCount;

        [MenuItem("CitiVibe/Build Mini City")]
        static void BuildMenu() => Debug.Log(Build());

        /// <summary>Regenerates every asset under Assets/CitiVibe/Generated and saves the MiniCity scene.</summary>
        public static string Build()
        {
            for (int i = 0; i < EditorSceneManager.sceneCount; i++)
            {
                var open = EditorSceneManager.GetSceneAt(i);
                if (open.isDirty && open.path != ScenePath)
                    return $"Aborted: '{open.name}' has unsaved changes. Save or discard them first.";
            }
            rng = new System.Random(20261127);
            triangleCount = 0;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            AssetDatabase.DeleteAsset(Gen);
            EnsureFolder(Gen + "/Textures");
            EnsureFolder(Gen + "/Materials");
            EnsureFolder(Root + "/Scenes");

            M = new Mats();
            ConfigureModels();
            container = new Mesh { name = "MiniCity Meshes" };
            AssetDatabase.CreateAsset(container, Gen + "/MiniCityMeshes.asset");

            var city = new GameObject("Rainlight Square").transform;
            var systems = new GameObject("City Systems");
            BuildGround(Group(city, "Ground"));
            int buildings = BuildBlocks(Group(city, "Buildings"));
            BuildPark(Group(city, "Central Park"));
            var lamps = BuildStreetFurniture(Group(city, "Street Furniture"), systems);
            int vehicles = BuildVehicles(Group(city, "Vehicles"), systems);
            int people = BuildPedestrians(Group(city, "People"), systems);
            lamps.AddRange(parkLamps);
            BuildEnvironment(systems, lamps);

            AssetDatabase.SaveAssets();
            EditorSceneManager.SaveScene(scene, ScenePath);
            RegisterScene();
            return $"Built {ScenePath}: {buildings} buildings, {vehicles} vehicles, {people} people, {lamps.Count} lamps, {triangleCount:N0} procedural triangles.";
        }

        static void RegisterScene()
        {
            var scenes = new List<EditorBuildSettingsScene> { new(ScenePath, true) };
            foreach (var s in EditorBuildSettings.scenes)
                if (s.path != ScenePath) scenes.Add(new EditorBuildSettingsScene(s.path, false));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static Transform Group(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        static void EnsureFolder(string path)
        {
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static float Rand(float a, float b) => a + (b - a) * (float)rng.NextDouble();
        static T Pick<T>(IReadOnlyList<T> list) => list[rng.Next(list.Count)];

        // ------------------------------------------------------------------ textures

        delegate void Sampler(float u, float v, out Color albedo, out float height);
        const int TexSize = 512;

        static int Mod(int a, int m) => ((a % m) + m) % m;

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        static float ValueNoise(float x, float y, int seed, int period)
        {
            int xi = Mathf.FloorToInt(x), yi = Mathf.FloorToInt(y);
            float fx = x - xi, fy = y - yi;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            int x0 = Mod(xi, period), x1 = Mod(xi + 1, period), y0 = Mod(yi, period), y1 = Mod(yi + 1, period);
            return Mathf.Lerp(Mathf.Lerp(Hash(x0, y0, seed), Hash(x1, y0, seed), fx),
                Mathf.Lerp(Hash(x0, y1, seed), Hash(x1, y1, seed), fx), fy);
        }

        /// <summary>Tileable fractal noise over the unit square.</summary>
        static float Fbm(float u, float v, int seed, int freq, int octaves)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * ValueNoise(u * freq, v * freq, seed + i * 31, freq);
                norm += amp;
                freq *= 2;
                amp *= 0.5f;
            }
            return sum / norm;
        }

        static float Grain(float u, float v, int seed) => Hash((int)(u * TexSize), (int)(v * TexSize), seed);

        static Color Opaque(Color c) { c.a = 1f; return c; }

        static (Texture2D albedo, Texture2D normal) MakeTexture(string name, Sampler sample, float bumpStrength)
        {
            var colors = new Color[TexSize * TexSize];
            var height = new float[TexSize * TexSize];
            for (int y = 0; y < TexSize; y++)
            for (int x = 0; x < TexSize; x++)
            {
                sample((x + 0.5f) / TexSize, (y + 0.5f) / TexSize, out var c, out var h);
                colors[y * TexSize + x] = Opaque(c);
                height[y * TexSize + x] = h;
            }
            var normals = new Color[TexSize * TexSize];
            for (int y = 0; y < TexSize; y++)
            for (int x = 0; x < TexSize; x++)
            {
                float dx = height[y * TexSize + Mod(x + 1, TexSize)] - height[y * TexSize + Mod(x - 1, TexSize)];
                float dy = height[Mod(y + 1, TexSize) * TexSize + x] - height[Mod(y - 1, TexSize) * TexSize + x];
                var n = new Vector3(-dx * bumpStrength, -dy * bumpStrength, 1f).normalized;
                normals[y * TexSize + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
            }
            string albedoPath = $"{Gen}/Textures/{name}_Albedo.png", normalPath = $"{Gen}/Textures/{name}_Normal.png";
            WritePng(albedoPath, colors);
            WritePng(normalPath, normals);
            ConfigureImport(albedoPath, false);
            ConfigureImport(normalPath, true);
            return (AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath), AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
        }

        static void WritePng(string path, Color[] pixels)
        {
            var tex = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false);
            tex.SetPixels(pixels);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }

        static void ConfigureImport(string path, bool normal)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !normal;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.mipmapEnabled = true;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.SaveAndReimport();
        }

        /// <summary>Running-bond modular brick; the tile is 4 stretchers by 8 courses (1.68 x 1.64 m).</summary>
        static Sampler Brick(Color a, Color b, Color mortar, int seed) => (float u, float v, out Color c, out float h) =>
        {
            const int cols = 4, rows = 8;
            const float bw = 0.42f, bh = 0.205f, joint = 0.011f;
            float ry = v * rows;
            int r = Mathf.FloorToInt(ry);
            float fy = ry - r;
            float rx = u * cols + ((r & 1) == 1 ? 0.5f : 0f);
            int ci = Mathf.FloorToInt(rx);
            float fx = rx - ci;
            float edge = Mathf.Min(Mathf.Min(fx, 1f - fx) * bw, Mathf.Min(fy, 1f - fy) * bh);
            float brick = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(joint * 0.4f, joint, edge));
            float tone = Hash(Mod(ci, cols), Mod(r, rows), seed);
            float grime = Fbm(u, v, seed + 7, 6, 4);
            Color bc = Color.Lerp(a, b, tone) * (0.84f + 0.26f * grime) * (0.93f + 0.12f * Grain(u, v, seed));
            Color mc = mortar * (0.88f + 0.18f * Fbm(u, v, seed + 3, 24, 2));
            c = Color.Lerp(mc, bc, brick);
            h = brick * (0.75f + 0.25f * Fbm(u, v, seed + 11, 32, 3));
        };

        /// <summary>Ashlar stone blocks in a half-offset bond.</summary>
        static Sampler Stone(int cols, int rows, float blockW, float blockH, Color baseColor, float variation, int seed) =>
            (float u, float v, out Color c, out float h) =>
            {
                const float joint = 0.008f;
                float ry = v * rows;
                int r = Mathf.FloorToInt(ry);
                float fy = ry - r;
                float rx = u * cols + ((r & 1) == 1 ? 0.5f : 0f);
                int ci = Mathf.FloorToInt(rx);
                float fx = rx - ci;
                float edge = Mathf.Min(Mathf.Min(fx, 1f - fx) * blockW, Mathf.Min(fy, 1f - fy) * blockH);
                float block = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(joint * 0.3f, joint, edge));
                float tone = 1f + variation * (Hash(Mod(ci, cols), Mod(r, rows), seed) - 0.5f);
                float weather = Fbm(u, v, seed + 5, 4, 5);
                c = baseColor * tone * (0.82f + 0.3f * weather) * (0.96f + 0.08f * Grain(u, v, seed));
                c = Color.Lerp(baseColor * 0.55f, c, 0.35f + 0.65f * block);
                h = 0.6f * block + 0.4f * Fbm(u, v, seed + 9, 16, 4);
            };

        /// <summary>Concrete sidewalk: 2 x 2 NYC flags of about 1.52 m.</summary>
        static void Concrete(float u, float v, out Color c, out float h)
        {
            const float flag = 1.52f, joint = 0.012f;
            float gx = u * 2f, gy = v * 2f;
            int ix = Mathf.FloorToInt(gx), iy = Mathf.FloorToInt(gy);
            float fx = gx - ix, fy = gy - iy;
            float edge = Mathf.Min(Mathf.Min(fx, 1f - fx), Mathf.Min(fy, 1f - fy)) * flag;
            float slab = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(joint * 0.3f, joint, edge));
            float tone = 0.93f + 0.1f * Hash(ix, iy, 41);
            float stain = Fbm(u, v, 43, 3, 5);
            float speck = Grain(u, v, 44);
            float g = 0.66f * tone * (0.88f + 0.22f * stain) * (0.95f + 0.1f * speck);
            c = new Color(g, g * 0.99f, g * 0.96f) * Mathf.Lerp(0.6f, 1f, slab);
            h = slab * (0.85f + 0.15f * speck);
        }

        static void Asphalt(float u, float v, out Color c, out float h)
        {
            float patches = Fbm(u, v, 51, 4, 5);
            float speck = Grain(u, v, 52);
            float aggregate = speck > 0.9f ? (speck - 0.9f) * 1.6f : 0f;
            float crackField = Mathf.Abs(Fbm(u, v, 53, 3, 5) - 0.5f);
            float crack = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.003f, 0.011f, crackField));
            float g = (0.16f + 0.07f * patches + aggregate) * (1f - 0.45f * crack);
            c = new Color(g, g * 1.01f, g * 1.04f);
            h = 0.55f + 0.45f * speck * 0.6f + 0.2f * patches - 0.6f * crack;
        }

        static void Grass(float u, float v, out Color c, out float h)
        {
            float broad = Fbm(u, v, 61, 4, 4);
            float fine = Grain(u, v, 62);
            var dark = new Color(0.17f, 0.3f, 0.12f);
            var light = new Color(0.36f, 0.5f, 0.2f);
            c = Color.Lerp(dark, light, broad) * (0.8f + 0.4f * fine);
            h = fine * 0.7f + broad * 0.3f;
        }

        static void Gravel(float u, float v, out Color c, out float h)
        {
            float pebble = ValueNoise(u * 96f, v * 96f, 71, 96);
            float speck = Grain(u, v, 72);
            float broad = Fbm(u, v, 73, 4, 3);
            var tan = new Color(0.68f, 0.6f, 0.47f);
            c = tan * (0.78f + 0.25f * pebble + 0.08f * speck) * (0.92f + 0.12f * broad);
            h = pebble;
        }

        // ------------------------------------------------------------------ mesh building

        /// <summary>Accumulates geometry per material; planar UVs are in metres so textures keep real scale.</summary>
        sealed class MeshBuilder
        {
            readonly List<Vector3> verts = new();
            readonly List<Vector3> norms = new();
            readonly List<Vector2> uvs = new();
            readonly Dictionary<Material, List<int>> tris = new();
            readonly List<Material> order = new();

            List<int> Tris(Material m)
            {
                if (!tris.TryGetValue(m, out var list))
                {
                    list = new List<int>();
                    tris[m] = list;
                    order.Add(m);
                }
                return list;
            }

            static Vector2 Project(Vector3 p, Vector3 n)
            {
                float ax = Mathf.Abs(n.x), ay = Mathf.Abs(n.y), az = Mathf.Abs(n.z);
                if (ay >= ax && ay >= az) return new Vector2(p.x, p.z);
                if (ax >= az) return new Vector2(n.x > 0 ? -p.z : p.z, p.y);
                return new Vector2(n.z > 0 ? p.x : -p.x, p.y);
            }

            int Vert(Vector3 p, Vector3 n)
            {
                verts.Add(p);
                norms.Add(n);
                uvs.Add(Project(p, n));
                return verts.Count - 1;
            }

            /// <summary>Adds a flat quad, wound so its front face points along <paramref name="n"/>.</summary>
            public void Quad(Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0f) (b, d) = (d, b);
                var t = Tris(m);
                int i0 = Vert(a, n), i1 = Vert(b, n), i2 = Vert(c, n), i3 = Vert(d, n);
                t.Add(i0); t.Add(i1); t.Add(i2); t.Add(i0); t.Add(i2); t.Add(i3);
            }

            public void Tri(Material m, Vector3 a, Vector3 b, Vector3 c, Vector3 n)
            {
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) < 0f) (b, c) = (c, b);
                var t = Tris(m);
                t.Add(Vert(a, n)); t.Add(Vert(b, n)); t.Add(Vert(c, n));
            }

            public void Box(Material m, Vector3 center, Vector3 size) => Box(m, center, size, Quaternion.identity);

            public void Box(Material m, Vector3 center, Vector3 size, Quaternion q)
            {
                Vector3 h = size * 0.5f;
                Vector3 X = q * Vector3.right * h.x, Y = q * Vector3.up * h.y, Z = q * Vector3.forward * h.z;
                Side(m, center + X, Y, Z, (q * Vector3.right));
                Side(m, center - X, Y, Z, -(q * Vector3.right));
                Side(m, center + Y, X, Z, (q * Vector3.up));
                Side(m, center - Y, X, Z, -(q * Vector3.up));
                Side(m, center + Z, X, Y, (q * Vector3.forward));
                Side(m, center - Z, X, Y, -(q * Vector3.forward));
            }

            void Side(Material m, Vector3 c, Vector3 u, Vector3 v, Vector3 n) =>
                Quad(m, c - u - v, c - u + v, c + u + v, c + u - v, n);

            /// <summary>Faceted cylinder standing on <paramref name="baseCenter"/> along the rotated up axis.</summary>
            public void Prism(Material m, Vector3 baseCenter, float radius, float height, int segments, Quaternion q, bool caps = true)
            {
                Vector3 up = q * Vector3.up;
                for (int i = 0; i < segments; i++)
                {
                    float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments, am = (a0 + a1) * 0.5f;
                    Vector3 d0 = q * new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius;
                    Vector3 d1 = q * new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
                    Vector3 b0 = baseCenter + d0, b1 = baseCenter + d1, t0 = b0 + up * height, t1 = b1 + up * height;
                    Quad(m, b0, t0, t1, b1, q * new Vector3(Mathf.Cos(am), 0f, Mathf.Sin(am)));
                    if (!caps) continue;
                    Tri(m, baseCenter + up * height, t0, t1, up);
                    Tri(m, baseCenter, b0, b1, -up);
                }
            }

            public void Prism(Material m, Vector3 baseCenter, float radius, float height, int segments = 14) =>
                Prism(m, baseCenter, radius, height, segments, Quaternion.identity);

            public void Ring(Material m, Vector3 center, float inner, float outer, float y0, float y1, int segments)
            {
                for (int i = 0; i < segments; i++)
                {
                    float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments, am = (a0 + a1) * 0.5f;
                    var d0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0));
                    var d1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1));
                    var dm = new Vector3(Mathf.Cos(am), 0f, Mathf.Sin(am));
                    Vector3 lo = Vector3.up * y0, hi = Vector3.up * y1;
                    Quad(m, center + d0 * inner + hi, center + d0 * outer + hi, center + d1 * outer + hi, center + d1 * inner + hi, Vector3.up);
                    Quad(m, center + d0 * outer + lo, center + d0 * outer + hi, center + d1 * outer + hi, center + d1 * outer + lo, dm);
                    Quad(m, center + d0 * inner + lo, center + d0 * inner + hi, center + d1 * inner + hi, center + d1 * inner + lo, -dm);
                }
            }

            public void Cone(Material m, Vector3 baseCenter, float radius, float height, int segments)
            {
                Vector3 apex = baseCenter + Vector3.up * height;
                for (int i = 0; i < segments; i++)
                {
                    float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments, am = (a0 + a1) * 0.5f;
                    Vector3 b0 = baseCenter + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius;
                    Vector3 b1 = baseCenter + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
                    var n = (new Vector3(Mathf.Cos(am), 0f, Mathf.Sin(am)) * height + Vector3.up * radius).normalized;
                    Tri(m, b0, apex, b1, n);
                }
            }

            /// <summary>Smooth ellipsoid with lat/long UVs (used for foliage, globes and caps).</summary>
            public void Sphere(Material m, Vector3 c, Vector3 radii, int lon = 14, int lat = 9)
            {
                var t = Tris(m);
                int start = verts.Count;
                for (int j = 0; j <= lat; j++)
                {
                    float th = Mathf.PI * j / lat;
                    for (int i = 0; i <= lon; i++)
                    {
                        float ph = Mathf.PI * 2f * i / lon;
                        var d = new Vector3(Mathf.Sin(th) * Mathf.Cos(ph), Mathf.Cos(th), Mathf.Sin(th) * Mathf.Sin(ph));
                        verts.Add(c + Vector3.Scale(d, radii));
                        norms.Add(new Vector3(d.x / radii.x, d.y / radii.y, d.z / radii.z).normalized);
                        uvs.Add(new Vector2((float)i / lon, (float)j / lat));
                    }
                }
                for (int j = 0; j < lat; j++)
                for (int i = 0; i < lon; i++)
                {
                    int a = start + j * (lon + 1) + i, b = a + lon + 1;
                    Oriented(t, a, b, a + 1, c);
                    Oriented(t, a + 1, b, b + 1, c);
                }
            }

            void Oriented(List<int> t, int a, int b, int c, Vector3 center)
            {
                Vector3 n = Vector3.Cross(verts[b] - verts[a], verts[c] - verts[a]);
                if (Vector3.Dot(n, (verts[a] + verts[b] + verts[c]) / 3f - center) < 0f) (b, c) = (c, b);
                t.Add(a); t.Add(b); t.Add(c);
            }

            public GameObject Finish(string name, Transform parent, Vector3 position = default, float yaw = 0f)
            {
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.transform.localPosition = position;
                go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                if (verts.Count == 0) return go;
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(verts);
                mesh.SetNormals(norms);
                mesh.SetUVs(0, uvs);
                mesh.subMeshCount = order.Count;
                for (int i = 0; i < order.Count; i++)
                {
                    mesh.SetTriangles(tris[order[i]], i);
                    triangleCount += tris[order[i]].Count / 3;
                }
                mesh.RecalculateBounds();
                mesh.RecalculateTangents();
                AssetDatabase.AddObjectToAsset(mesh, container);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = order.ToArray();
                renderer.shadowCastingMode = ShadowCastingMode.On;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
                return go;
            }
        }

        /// <summary>A building facade in face space: a runs along the wall, y up, o outward from it.</summary>
        sealed class Face
        {
            readonly MeshBuilder mb;
            readonly Vector3 origin, right, normal;
            readonly Quaternion q;
            public readonly float Width;

            public Face(MeshBuilder mb, Vector3 origin, Vector3 normal, float width)
            {
                this.mb = mb;
                this.origin = origin;
                this.normal = normal;
                right = Vector3.Cross(Vector3.up, normal);
                q = Quaternion.LookRotation(normal, Vector3.up);
                Width = width;
            }

            Vector3 P(float a, float y, float o) => origin + right * a + Vector3.up * y + normal * o;

            public void Box(Material m, float a, float y, float o, float sizeA, float sizeY, float sizeO) =>
                mb.Box(m, P(a, y, o), new Vector3(sizeA, sizeY, sizeO), q);

            public void Box(Material m, float a, float y, float o, Vector3 size, Quaternion local) =>
                mb.Box(m, P(a, y, o), size, q * local);
        }

    }
}
