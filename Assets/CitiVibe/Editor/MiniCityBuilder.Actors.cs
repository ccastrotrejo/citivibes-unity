using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CitiVibe.EditorTools
{
    public static partial class MiniCityBuilder
    {
        static readonly Dictionary<string, float> VehicleLength = new()
        {
            ["Sedan"] = 4.9f, ["SUV"] = 4.65f, ["Van"] = 5.95f, ["TaxiSedan"] = 4.9f, ["TaxiSUV"] = 4.65f, ["BoxTruck"] = 8.5f, ["Bus"] = 12.2f,
        };

        static int BuildVehicles(Transform parent, GameObject systems)
        {
            var traffic = systems.GetComponent<CityTraffic>();
            var moving = Group(parent, "Traffic");
            var parked = Group(parent, "Parked");
            int count = 0;

            foreach (float ax in Avenues)
            {
                int flow = AvenueFlow(ax);
                for (int li = 0; li < AvenueLanes.Length; li++)
                {
                    float x = ax + flow * AvenueLanes[li];
                    var lane = new CityTraffic.Lane
                    {
                        origin = new Vector3(x, 0f, -flow * EZ), direction = new Vector3(0f, 0f, flow), length = EZ * 2f, maxSpeed = 10.5f, avenue = true,
                    };
                    var stops = new List<(float s, int i)>();
                    foreach (float sz in Streets) stops.Add(((sz - flow * (SH + 4.6f) - lane.origin.z) * flow, IntersectionIndex(ax, sz)));
                    SetStops(lane, stops);
                    traffic.lanes.Add(lane);
                    bool curbLane = li == AvenueLanes.Length - 1;
                    Populate(traffic, moving, traffic.lanes.Count - 1, 5, curbLane ? new[] { "Bus", "BoxTruck", "Sedan", "TaxiSedan", "Van" } : null, ref count);
                }
            }
            foreach (float sz in Streets)
            {
                int flow = StreetFlow(sz);
                var lane = new CityTraffic.Lane
                {
                    origin = new Vector3(-flow * EX, 0f, sz), direction = new Vector3(flow, 0f, 0f), length = EX * 2f, maxSpeed = 7.5f,
                };
                var stops = new List<(float s, int i)>();
                foreach (float ax in Avenues) stops.Add(((ax - flow * (AH + 4.6f) - lane.origin.x) * flow, IntersectionIndex(ax, sz)));
                SetStops(lane, stops);
                traffic.lanes.Add(lane);
                Populate(traffic, moving, traffic.lanes.Count - 1, 4, null, ref count);
            }

            // Curbside parking, kept clear of intersections, hydrants, bus stops and steam stacks.
            foreach (float ax in Avenues)
            {
                int flow = AvenueFlow(ax);
                foreach (float q in new[] { AvenueParkLeft, AvenueParkRight })
                    for (float z = -EZ + 4f; z < EZ - 4f; z += Rand(6.1f, 6.8f))
                        Park(parked, new Vector3(ax + flow * q, 0f, z), new Vector3(0f, 0f, flow), Near(z, Streets, SH + 8f), ref count);
            }
            foreach (float sz in Streets)
            {
                int flow = StreetFlow(sz);
                foreach (int side in new[] { -1, 1 })
                    for (float x = -EX + 4f; x < EX - 4f; x += Rand(6.1f, 6.8f))
                        Park(parked, new Vector3(x, 0f, sz + side * StreetPark), new Vector3(flow, 0f, 0f), Near(x, Avenues, AH + 8f), ref count);
            }
            return count;
        }

        static void SetStops(CityTraffic.Lane lane, List<(float s, int i)> stops)
        {
            stops.Sort((a, b) => a.s.CompareTo(b.s));
            lane.stops = stops.ConvertAll(t => t.s).ToArray();
            lane.stopIntersections = stops.ConvertAll(t => t.i).ToArray();
        }

        static Dictionary<Material, Material> PaintSwap(string model, bool parked)
        {
            var swap = new Dictionary<Material, Material>();
            if (model.StartsWith("Taxi")) swap[M.carPaint[0]] = M.taxi;
            else if (model != "Bus") swap[M.carPaint[0]] = model == "BoxTruck" && rng.NextDouble() < 0.6 ? M.carPaint[8] : Pick(M.carPaint);
            if (parked)
            {
                swap[M.headlight] = M.headlightOff;
                swap[M.taillight] = M.taillightOff;
            }
            return swap;
        }

        static void Populate(CityTraffic traffic, Transform parent, int laneIndex, int n, string[] curbMix, ref int count)
        {
            var lane = traffic.lanes[laneIndex];
            string[] mix = { "TaxiSedan", "TaxiSUV", "TaxiSedan", "Sedan", "Sedan", "SUV", "SUV", "Van", "Sedan", "TaxiSedan" };
            for (int k = 0; k < n; k++)
            {
                string model = curbMix != null ? curbMix[k % curbMix.Length] : Pick(mix);
                float len = VehicleLength[model];
                float s = (k + 0.5f) * lane.length / n + Rand(-3f, 3f);
                var go = Spawn(model, parent, lane.origin + lane.direction * s, lane.direction, PaintSwap(model, false), false);
                go.name = $"{model} {++count}";
                traffic.cars.Add(new CityTraffic.Car
                {
                    transform = go.transform, lane = laneIndex, length = len,
                    speedFactor = model == "Bus" ? 0.72f : model == "BoxTruck" ? 0.8f : Rand(0.85f, 1.08f),
                });
            }
        }

        static void Park(Transform parent, Vector3 p, Vector3 dir, bool nearIntersection, ref int count)
        {
            if (nearIntersection || rng.NextDouble() < 0.18) return;
            foreach (var h in hydrants) if (Vector3.Distance(Flat(h), p) < 5.5f) return;
            foreach (var b in busStops) if (Vector3.Distance(Flat(b), p) < 10f) return;
            foreach (var r in roadReserved) if (Vector3.Distance(Flat(r), p) < 4f) return;
            double r0 = rng.NextDouble();
            string model = r0 < 0.45 ? "Sedan" : r0 < 0.8 ? "SUV" : r0 < 0.96 ? "Van" : "BoxTruck";
            if (model == "BoxTruck") p += dir * 1.8f;
            var go = Spawn(model, parent, p, dir, PaintSwap(model, true));
            go.name = $"Parked {model} {++count}";
        }

        // ------------------------------------------------------------------ pedestrians

        static readonly string[] PersonModels = { "PersonCasual", "PersonSkirt", "PersonCoat", "PersonDress" };

        static int BuildPedestrians(Transform parent, GameObject systems)
        {
            var peds = systems.AddComponent<Pedestrians>();
            var nodes = new List<Vector3>();
            var ends = new List<bool>();
            int Node(Vector3 p, bool end)
            {
                for (int i = 0; i < nodes.Count; i++)
                    if ((nodes[i] - p).sqrMagnitude < 0.01f) return i;
                nodes.Add(p);
                ends.Add(end);
                return nodes.Count - 1;
            }
            var xLines = new List<float>();
            foreach (float ax in Avenues) { xLines.Add(ax - AH - WalkInset); xLines.Add(ax + AH + WalkInset); }
            var zLines = new List<float>();
            foreach (float sz in Streets) { zLines.Add(sz - SH - WalkInset); zLines.Add(sz + SH + WalkInset); }
            xLines.Sort();
            zLines.Sort();

            foreach (float x in xLines)
            {
                var line = new List<int> { Node(new Vector3(x, 0f, -EZ + 0.6f), true) };
                foreach (float z in zLines) line.Add(Node(new Vector3(x, 0f, z), false));
                line.Add(Node(new Vector3(x, 0f, EZ - 0.6f), true));
                for (int i = 0; i + 1 < line.Count; i++)
                {
                    float za = nodes[line[i]].z, zb = nodes[line[i + 1]].z;
                    var e = new Pedestrians.Edge { a = line[i], b = line[i + 1] };
                    foreach (float sz in Streets)
                        if (za < sz && zb > sz && zb - za < 2f * (SH + WalkInset) + 0.5f)
                        {
                            e.kind = Pedestrians.EdgeKind.CrossStreet;
                            e.intersection = IntersectionIndex(x > 0f ? AV : -AV, sz);
                        }
                    peds.edges.Add(e);
                }
            }
            foreach (float z in zLines)
            {
                var line = new List<int> { Node(new Vector3(-EX + 0.6f, 0f, z), true) };
                foreach (float x in xLines) line.Add(Node(new Vector3(x, 0f, z), false));
                line.Add(Node(new Vector3(EX - 0.6f, 0f, z), true));
                for (int i = 0; i + 1 < line.Count; i++)
                {
                    float xa = nodes[line[i]].x, xb = nodes[line[i + 1]].x;
                    var e = new Pedestrians.Edge { a = line[i], b = line[i + 1] };
                    foreach (float ax in Avenues)
                        if (xa < ax && xb > ax && xb - xa < 2f * (AH + WalkInset) + 0.5f)
                        {
                            e.kind = Pedestrians.EdgeKind.CrossAvenue;
                            e.intersection = IntersectionIndex(ax, z > 0f ? SZ : -SZ);
                        }
                    peds.edges.Add(e);
                }
            }
            peds.nodes = nodes.ToArray();
            peds.endNodes = ends.ToArray();
            peds.parkCenter = Vector3.zero;
            peds.parkRadius = ParkRing;
            peds.groundHeight = Curb;
            peds.parkHeight = ParkHeight;
            peds.avenueX = Avenues;
            peds.streetZ = Streets;
            peds.avenueHalf = AH;
            peds.streetHalf = SH;

            var sidewalkEdges = peds.edges.FindAll(e => e.kind == Pedestrians.EdgeKind.Sidewalk);
            int total = 0;
            for (int i = 0; i < 170; i++)
            {
                bool park = i >= 146;
                var w = new Pedestrians.Walker
                {
                    speed = Rand(1.0f, 1.5f) * (park ? 0.75f : 1f), lateral = Rand(0.2f, 0.55f), park = park,
                };
                Vector3 pos;
                if (park)
                {
                    w.angle = Rand(0f, Mathf.PI * 2f);
                    if (rng.NextDouble() < 0.5) w.lateral = -w.lateral;
                    pos = new Vector3(Mathf.Cos(w.angle), 0f, Mathf.Sin(w.angle)) * ParkRing + Vector3.up * ParkHeight;
                }
                else
                {
                    var e = Pick(sidewalkEdges);
                    bool forward = rng.NextDouble() < 0.5;
                    w.from = forward ? e.a : e.b;
                    w.to = forward ? e.b : e.a;
                    w.t = Rand(0.05f, 0.95f);
                    pos = Vector3.Lerp(nodes[w.from], nodes[w.to], w.t) + Vector3.up * Curb;
                }
                string model = Pick(PersonModels);
                var swap = new Dictionary<Material, Material>
                {
                    [M.skin[0]] = Pick(M.skin), [M.shirts[0]] = Pick(M.shirts), [M.pants[0]] = Pick(M.pants),
                    [M.shoes[0]] = Pick(M.shoes), [M.hair[0]] = Pick(M.hair), [M.accents[0]] = Pick(M.accents),
                };
                var go = Spawn(model, parent, pos, Vector3.forward, swap, false, Rand(0.93f, 1.07f));
                go.name = $"Pedestrian {++total}";
                w.root = go.transform;
                w.legL = go.transform.Find("LegL");
                w.legR = go.transform.Find("LegR");
                w.armL = go.transform.Find("ArmL");
                w.armR = go.transform.Find("ArmR");
                peds.walkers.Add(w);
            }
            EditorUtility.SetDirty(peds);
            return total;
        }
    }
}
