using System.Collections.Generic;
using UnityEngine;

namespace CitiVibe.EditorTools
{
    public static partial class MiniCityBuilder
    {
        static readonly List<Vector3> hydrants = new(), busStops = new();

        static List<Light> BuildStreetFurniture(Transform parent, GameObject systems)
        {
            hydrants.Clear();
            busStops.Clear();
            var lamps = new List<Light>();
            var traffic = systems.AddComponent<CityTraffic>();
            traffic.lensRed = M.lensRed;
            traffic.lensAmber = M.lensAmber;
            traffic.lensGreen = M.lensGreen;
            traffic.lensWalk = M.lensWalk;
            traffic.lensStop = M.lensStop;
            traffic.intersectionOffsets = new[] { -6f, 0f, 0f, -6f };
            BuildSignals(Group(parent, "Signals"), traffic);

            var items = Group(parent, "Sidewalk Items");
            var pits = new MeshBuilder();
            var fixtures = new MeshBuilder();
            int mailboxes = 0, newsstands = 0;
            foreach (var run in runs)
            {
                bool park = run.ix == 1 && run.iz == 1;
                for (float u = 6f; u < run.length - 3f; u += 24f)
                {
                    var lamp = Spawn("StreetLamp", items, run.At(u, 0.45f), run.toRoad);
                    lamps.Add(AddLamp(lamp.transform, new Vector3(0f, 8.25f, 2.6f), true));
                    run.Take(u, 1.3f);
                }
                if (run.length > 15f) TryPlace(run, run.length * Rand(0.2f, 0.35f), 1.4f, u =>
                {
                    var p = run.At(u, 0.55f);
                    Spawn("Hydrant", items, p, run.toRoad);
                    hydrants.Add(p);
                });
                foreach (float u in new[] { 1.3f, run.length - 1.3f })
                    Spawn("LitterBasket", items, run.At(u, 1.6f), run.along);
                if (run.avenue && run.iz == 1 && run.ix != 1)
                    TryPlace(run, run.length * 0.5f, 2.6f, u =>
                    {
                        var p = run.At(u, 1.2f);
                        Spawn("BusShelter", items, p, run.toRoad);
                        busStops.Add(p);
                    });
                if (run.avenue && run.ix == 1 && newsstands < 2 && run.length > 10f)
                {
                    Spawn("Newsstand", items, run.At(Mathf.Min(8f, run.length * 0.5f), 4.6f), run.toRoad);
                    newsstands++;
                }
                if (run.avenue && ((run.ix == 1 && run.iz == 0 && run.toRoad.x < 0f) || (run.ix == 2 && run.iz == 2)) && run.length > 14f)
                    SubwayEntrance(fixtures, run, 9f);
                if (mailboxes < 4 && !park && rng.NextDouble() < 0.25)
                    TryPlace(run, Rand(5f, run.length - 5f), 0.6f, u => { Spawn("Mailbox", items, run.At(u, 1.6f), run.toRoad); mailboxes++; });
                if (!run.avenue && !park && rng.NextDouble() < 0.55)
                    for (int k = 0; k < 2; k++)
                        TryPlace(run, Rand(6f, run.length - 6f), 1.1f, u => Spawn("TrashBags", items, run.At(u, 1.35f), run.along));
                if (!park && rng.NextDouble() < 0.2)
                    TryPlace(run, Rand(6f, run.length - 6f), 2.2f, u =>
                    {
                        for (int k = -1; k <= 1; k++) Spawn("BikeRack", items, run.At(u + k * 1.1f, 1.6f), run.toRoad);
                    });
                if (park)
                    for (float u = 10f; u < run.length - 8f; u += 13f)
                        Spawn("Bench", items, run.At(u, 4.85f), run.toRoad);

                for (float u = 4.5f; u < run.length - 4f; u += Rand(7.2f, 8.8f))
                {
                    if (!run.Free(u, 1.5f)) continue;
                    run.Take(u, 1.5f);
                    var p = run.At(u, 0.95f);
                    TreePit(pits, p);
                    Spawn(rng.NextDouble() < 0.6 ? "TreePlane" : "TreeLocust", items, p, new Vector3(Rand(-1f, 1f), 0f, Rand(-1f, 1f)), null, true, Rand(0.75f, 1.0f));
                }
            }
            pits.Finish("Tree Pits", parent);
            fixtures.Finish("Subway Entrances", parent);
            return lamps;
        }

        static void TryPlace(Run run, float u, float radius, System.Action<float> place)
        {
            for (int k = 0; k < 8; k++)
            {
                float candidate = u + (k % 2 == 0 ? 1 : -1) * k * 1.7f;
                if (!run.Free(candidate, radius)) continue;
                run.Take(candidate, radius);
                place(candidate);
                return;
            }
        }

        static void TreePit(MeshBuilder mb, Vector3 p)
        {
            mb.Box(M.soil, p + Vector3.up * 0.005f, new Vector3(1.3f, 0.012f, 1.3f));
            foreach (var (dx, dz, sx, sz) in new[] { (0.62f, 0f, 0.03f, 1.24f), (-0.62f, 0f, 0.03f, 1.24f), (0f, 0.62f, 1.24f, 0.03f), (0f, -0.62f, 1.24f, 0.03f) })
            {
                mb.Box(M.iron, p + new Vector3(dx, 0.42f, dz), new Vector3(sx, 0.03f, sz));
                mb.Box(M.iron, p + new Vector3(dx, 0.21f, dz), new Vector3(sx, 0.02f, sz));
            }
            foreach (var (dx, dz) in new[] { (0.62f, 0.62f), (-0.62f, 0.62f), (0.62f, -0.62f), (-0.62f, -0.62f) })
                mb.Box(M.iron, p + new Vector3(dx, 0.21f, dz), new Vector3(0.04f, 0.42f, 0.04f));
        }

        /// <summary>Stair entrance in the building zone: dark opening, railings on three sides, green globes at the mouth.</summary>
        static void SubwayEntrance(MeshBuilder mb, Run run, float u)
        {
            if (!run.Free(u, 3f)) u = run.length * 0.5f;
            const float d = 4.6f, len = 5f, half = 0.85f;
            Vector3 a = run.At(u - len * 0.5f, d), b = run.At(u + len * 0.5f, d);
            Vector3 side = -run.toRoad;
            bool alongX = Mathf.Abs(run.along.x) > 0.5f;
            mb.Box(M.voidBlack, (a + b) * 0.5f + Vector3.up * 0.004f, alongX ? new Vector3(len, 0.008f, half * 2f) : new Vector3(half * 2f, 0.008f, len));
            for (int i = 0; i < 7; i++)
            {
                var step = Vector3.Lerp(a, b, (i + 0.5f) / 7f) + Vector3.up * 0.009f;
                mb.Box(M.granite, step, alongX ? new Vector3(0.06f, 0.006f, half * 2f) : new Vector3(half * 2f, 0.006f, 0.06f));
            }
            Fence(mb, a + side * half, b + side * half);
            Fence(mb, a - side * half, b - side * half);
            Fence(mb, b + side * half, b - side * half);
            foreach (int s in new[] { -1, 1 })
            {
                var post = a + side * (half * s);
                mb.Prism(M.iron, post, 0.05f, 2.3f, 10);
                mb.Sphere(M.globeGreen, post + Vector3.up * 2.5f, Vector3.one * 0.21f);
            }
        }

        static void BuildSignals(Transform parent, CityTraffic traffic)
        {
            foreach (float ax in Avenues)
            foreach (float sz in Streets)
            {
                int idx = IntersectionIndex(ax, sz);
                int flowA = AvenueFlow(ax), flowS = StreetFlow(sz);
                foreach (int sx in new[] { -1, 1 })
                foreach (int s in new[] { -1, 1 })
                {
                    var corner = new Vector3(ax + sx * (AH + 0.55f), Curb, sz + s * (SH + 0.55f));
                    Spawn("SignalPole", parent, corner, Vector3.forward);
                    Head("SignalHead", corner + Vector3.up * 4.3f + Vector3.right * (-sx * 0.35f), new Vector3(0f, 0f, -flowA), idx, true, false);
                    Head("SignalHead", corner + Vector3.up * 4.3f + Vector3.forward * (-s * 0.35f), new Vector3(-flowS, 0f, 0f), idx, false, false);
                    Head("PedSignalHead", corner + Vector3.up * 2.75f + Vector3.right * (-sx * 0.22f), new Vector3(-sx, 0f, 0f), idx, false, true);
                    Head("PedSignalHead", corner + Vector3.up * 2.75f + Vector3.forward * (-s * 0.22f), new Vector3(0f, 0f, -s), idx, true, true);
                }
            }

            void Head(string model, Vector3 pos, Vector3 facing, int idx, bool avenue, bool pedestrian)
            {
                var go = Spawn(model, parent, pos, facing, null, false);
                traffic.heads.Add(new CityTraffic.SignalHead
                {
                    renderer = go.GetComponentInChildren<MeshRenderer>(), intersection = idx, avenue = avenue, pedestrian = pedestrian,
                });
            }
        }
    }
}
