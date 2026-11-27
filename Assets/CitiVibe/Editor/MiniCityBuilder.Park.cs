using System.Collections.Generic;
using UnityEngine;

namespace CitiVibe.EditorTools
{
    public static partial class MiniCityBuilder
    {
        static readonly List<Light> parkLamps = new();
        const float ParkHeight = 0.335f, PlazaRadius = 7.6f, ParkRing = 5.2f;

        static void BuildPark(Transform parent)
        {
            parkLamps.Clear();
            var lot = Lot(1, 1);
            float x1 = lot.x1 - 0.2f, z1 = lot.z1 - 0.2f;
            const float c = 0.35f, gate = 1.7f, cornerGate = 3.6f;
            var mb = new MeshBuilder();
            mb.Box(M.grass, new Vector3(0f, 0.235f, 0f), new Vector3((x1 - c) * 2f, 0.17f, (z1 - c) * 2f));

            // Granite coping topped by a low iron fence, broken at the eight gates.
            void Edge(Vector3 a, Vector3 b)
            {
                Vector3 dir = b - a;
                mb.Box(M.granite, (a + b) * 0.5f + Vector3.up * 0.325f, new Vector3(c, 0.35f, dir.magnitude), Quaternion.LookRotation(dir));
                Fence(mb, a + Vector3.up * 0.5f, b + Vector3.up * 0.5f);
            }
            foreach (int s in new[] { -1, 1 })
            {
                float z = s * (z1 - c * 0.5f), x = s * (x1 - c * 0.5f);
                Edge(new Vector3(-x1 + cornerGate, 0f, z), new Vector3(-gate, 0f, z));
                Edge(new Vector3(gate, 0f, z), new Vector3(x1 - cornerGate, 0f, z));
                Edge(new Vector3(x, 0f, -z1 + cornerGate), new Vector3(x, 0f, -gate));
                Edge(new Vector3(x, 0f, gate), new Vector3(x, 0f, z1 - cornerGate));
            }

            var paths = new List<(Vector3 a, Vector3 b)>();
            foreach (var end in new[] { new Vector3(0f, 0f, -z1), new Vector3(0f, 0f, z1), new Vector3(-x1, 0f, 0f), new Vector3(x1, 0f, 0f) })
                paths.Add((end, end.normalized * (PlazaRadius - 0.3f)));
            foreach (int sx in new[] { -1, 1 })
            foreach (int sz in new[] { -1, 1 })
            {
                var corner = new Vector3(sx * x1, 0f, sz * z1);
                paths.Add((corner, corner.normalized * (PlazaRadius - 0.3f)));
            }
            foreach (var (a, b) in paths)
            {
                Vector3 dir = b - a;
                mb.Box(M.gravel, (a + b) * 0.5f + Vector3.up * (ParkHeight - 0.02f), new Vector3(2.6f, 0.04f, dir.magnitude + 1.2f), Quaternion.LookRotation(dir));
            }
            mb.Prism(M.gravel, Vector3.up * 0.29f, PlazaRadius, ParkHeight - 0.29f, 48);

            // Two-tier fountain.
            mb.Ring(M.limestone, Vector3.zero, 3.0f, 3.35f, ParkHeight, 0.95f, 48);
            mb.Ring(M.limestone, Vector3.zero, 3.35f, 3.6f, ParkHeight, 0.45f, 48);
            mb.Prism(M.water, Vector3.up * ParkHeight, 3.01f, 0.48f, 48);
            mb.Prism(M.limestone, Vector3.up * 0.8f, 0.5f, 1.1f, 20);
            mb.Prism(M.limestone, Vector3.up * 1.9f, 1.25f, 0.12f, 32);
            mb.Ring(M.limestone, Vector3.zero, 1.25f, 1.42f, 1.9f, 2.25f, 32);
            mb.Prism(M.water, Vector3.up * 2.0f, 1.26f, 0.15f, 32);
            mb.Prism(M.limestone, Vector3.up * 2.2f, 0.18f, 0.55f, 12);
            mb.Sphere(M.limestone, Vector3.up * 2.85f, new Vector3(0.28f, 0.22f, 0.28f));
            mb.Finish("Park Grounds", parent);

            var items = Group(parent, "Park Furniture");
            for (int k = 0; k < 8; k++)
            {
                float deg = 22.5f + k * 45f;
                var dir = new Vector3(Mathf.Sin(deg * Mathf.Deg2Rad), 0f, Mathf.Cos(deg * Mathf.Deg2Rad));
                Spawn("Bench", items, dir * 6.55f + Vector3.up * ParkHeight, -dir);
            }
            foreach (var (a, b) in paths)
            {
                Vector3 dir = (b - a).normalized, side = Vector3.Cross(Vector3.up, dir);
                var lamp = Spawn("ParkLamp", items, Vector3.Lerp(a, b, 0.45f) + side * 1.75f + Vector3.up * 0.32f, dir);
                parkLamps.Add(AddLamp(lamp.transform, new Vector3(0f, 3.7f, 0f), false));
            }

            var placed = new List<Vector3>();
            for (int attempt = 0; attempt < 900 && placed.Count < 22; attempt++)
            {
                var p = new Vector3(Rand(-x1 + 2.6f, x1 - 2.6f), 0.32f, Rand(-z1 + 2.6f, z1 - 2.6f));
                if (!ClearOfPaths(p, paths, 3.1f) || Flat(p).magnitude < PlazaRadius + 3.2f) continue;
                bool crowded = false;
                foreach (var q in placed) crowded |= Vector3.Distance(p, q) < 5.5f;
                if (crowded) continue;
                placed.Add(p);
                var tree = Spawn(rng.NextDouble() < 0.6 ? "TreeOak" : "TreePlane", items, p, new Vector3(Rand(-1f, 1f), 0f, Rand(-1f, 1f)), null, true, Rand(0.85f, 1.15f));
                tree.name = "Park Tree";
            }
            for (int attempt = 0; attempt < 500; attempt++)
            {
                var p = new Vector3(Rand(-x1 + 0.9f, x1 - 0.9f), 0.32f, Rand(-z1 + 0.9f, z1 - 0.9f));
                bool edge = Mathf.Abs(p.x) > x1 - 1.5f || Mathf.Abs(p.z) > z1 - 1.5f;
                if (!edge || !ClearOfPaths(p, paths, 2.3f)) continue;
                Spawn("Shrub", items, p, new Vector3(Rand(-1f, 1f), 0f, Rand(-1f, 1f)), null, true, Rand(0.7f, 1.2f));
            }
        }

        static Light AddLamp(Transform lamp, Vector3 local, bool spot)
        {
            var go = new GameObject(spot ? "Street Light" : "Park Light");
            go.transform.SetParent(lamp, false);
            go.transform.localPosition = local;
            var light = go.AddComponent<Light>();
            light.type = spot ? LightType.Spot : LightType.Point;
            light.color = new Color(1f, 0.82f, 0.6f);
            light.range = spot ? 20f : 11f;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            light.enabled = false;
            if (spot)
            {
                go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                light.spotAngle = 125f;
                light.innerSpotAngle = 70f;
            }
            return light;
        }

        static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

        static bool ClearOfPaths(Vector3 p, List<(Vector3 a, Vector3 b)> paths, float clearance)
        {
            foreach (var (a, b) in paths)
            {
                Vector3 ab = Flat(b - a), ap = Flat(p - a);
                float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / ab.sqrMagnitude);
                if ((ap - ab * t).magnitude < clearance) return false;
            }
            return true;
        }

        static void Fence(MeshBuilder mb, Vector3 a, Vector3 b)
        {
            Vector3 dir = b - a;
            float len = dir.magnitude;
            var q = Quaternion.LookRotation(dir);
            mb.Box(M.iron, (a + b) * 0.5f + Vector3.up * 0.95f, new Vector3(0.04f, 0.04f, len), q);
            mb.Box(M.iron, (a + b) * 0.5f + Vector3.up * 0.12f, new Vector3(0.03f, 0.03f, len), q);
            int bars = Mathf.CeilToInt(len / 0.15f);
            for (int i = 0; i <= bars; i++)
            {
                Vector3 p = Vector3.Lerp(a, b, (float)i / bars);
                bool post = i % 10 == 0;
                mb.Box(M.iron, p + Vector3.up * 0.5f, post ? new Vector3(0.07f, 1.05f, 0.07f) : new Vector3(0.02f, 0.95f, 0.02f), q);
                if (post) mb.Sphere(M.iron, p + Vector3.up * 1.07f, Vector3.one * 0.05f, 6, 4);
            }
        }
    }
}
