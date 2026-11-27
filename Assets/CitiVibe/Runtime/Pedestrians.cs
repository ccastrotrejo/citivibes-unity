using System;
using System.Collections.Generic;
using UnityEngine;

namespace CitiVibe
{
    /// <summary>
    /// Sidewalk pedestrians on a walk graph (sidewalks, crosswalks, edge portals) plus park strollers
    /// circling the fountain. Crossings wait for the parallel green; walkers keep to the right and
    /// fall in behind slower people going the same way.
    /// </summary>
    public class Pedestrians : MonoBehaviour
    {
        public enum EdgeKind { Sidewalk, CrossStreet, CrossAvenue }

        [Serializable]
        public class Edge
        {
            public int a, b;
            public EdgeKind kind;
            public int intersection = -1;
        }

        [Serializable]
        public class Walker
        {
            public Transform root, legL, legR, armL, armR;
            public float speed = 1.3f;
            public float lateral = 0.45f;
            public bool park;
            public int from, to;
            [Range(0, 1)] public float t;
            public float angle;
            [NonSerialized] public float phase, current, hiddenFor, height;
            [NonSerialized] public bool hidden, waiting;
            [NonSerialized] public int edge, nextTo;
            [NonSerialized] public Vector3 offset;
        }

        public Vector3[] nodes = Array.Empty<Vector3>();
        public bool[] endNodes = Array.Empty<bool>();
        public List<Edge> edges = new();
        public List<Walker> walkers = new();
        public Vector3 parkCenter;
        public float parkRadius = 5.2f;
        public float groundHeight = 0.15f;
        public float parkHeight = 0.335f;
        public float[] avenueX = Array.Empty<float>();
        public float[] streetZ = Array.Empty<float>();
        public float avenueHalf = 9f, streetHalf = 5f;

        List<int>[] adjacency;
        readonly Dictionary<long, List<Walker>> onEdge = new();
        System.Random rng = new(5);

        void Start()
        {
            adjacency = new List<int>[nodes.Length];
            for (int i = 0; i < nodes.Length; i++) adjacency[i] = new List<int>();
            for (int i = 0; i < edges.Count; i++)
            {
                adjacency[edges[i].a].Add(i);
                adjacency[edges[i].b].Add(i);
            }
            foreach (var w in walkers)
            {
                w.phase = (float)rng.NextDouble() * 10f;
                w.height = w.park ? parkHeight : groundHeight;
                if (w.park) continue;
                w.edge = FindEdge(w.from, w.to);
                w.offset = Offset(w, w.from, w.to);
            }
        }

        int FindEdge(int a, int b)
        {
            foreach (int e in adjacency[a])
                if (Other(e, a) == b) return e;
            return adjacency[a].Count > 0 ? adjacency[a][0] : 0;
        }

        int Other(int edge, int node) => edges[edge].a == node ? edges[edge].b : edges[edge].a;

        Vector3 Offset(Walker w, int a, int b)
        {
            Vector3 dir = (nodes[b] - nodes[a]).normalized;
            return Vector3.Cross(Vector3.up, dir) * w.lateral;
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            onEdge.Clear();
            foreach (var w in walkers)
            {
                if (w.park || w.hidden || w.waiting) continue;
                long key = ((long)w.from << 32) | (uint)w.to;
                if (!onEdge.TryGetValue(key, out var list)) onEdge[key] = list = new List<Walker>();
                list.Add(w);
            }
            foreach (var w in walkers)
            {
                if (w.park) Stroll(w, dt);
                else Walk(w, dt);
            }
        }

        void Stroll(Walker w, float dt)
        {
            w.current = Mathf.MoveTowards(w.current, w.speed, dt);
            float dirSign = w.lateral >= 0 ? 1f : -1f;
            w.angle += dirSign * w.current * dt / parkRadius;
            float r = parkRadius + Mathf.Abs(w.lateral) - 0.45f;
            var pos = parkCenter + new Vector3(Mathf.Cos(w.angle), 0f, Mathf.Sin(w.angle)) * r;
            var tangent = new Vector3(-Mathf.Sin(w.angle), 0f, Mathf.Cos(w.angle)) * dirSign;
            Pose(w, pos, tangent, dt, parkHeight);
        }

        bool OnRoad(Vector3 p)
        {
            foreach (float x in avenueX) if (Mathf.Abs(p.x - x) < avenueHalf) return true;
            foreach (float z in streetZ) if (Mathf.Abs(p.z - z) < streetHalf) return true;
            return false;
        }

        void Walk(Walker w, float dt)
        {
            if (w.hidden)
            {
                w.hiddenFor -= dt;
                if (w.hiddenFor <= 0f) Respawn(w);
                return;
            }
            var e = edges[w.edge];
            if (w.waiting)
            {
                bool clear = e.kind == EdgeKind.Sidewalk || CityTraffic.Instance == null ||
                             CityTraffic.Instance.PedestrianMayStart(e.intersection, e.kind == EdgeKind.CrossStreet);
                w.current = Mathf.MoveTowards(w.current, 0f, dt * 3f);
                Pose(w, nodes[w.from] + w.offset, nodes[w.to] - nodes[w.from], dt, groundHeight);
                if (!clear) return;
                w.waiting = false;
            }
            Vector3 a = nodes[w.from], b = nodes[w.to];
            float len = Mathf.Max(0.01f, Vector3.Distance(a, b));
            float target = w.speed * (e.kind == EdgeKind.Sidewalk ? 1f : 1.18f);
            long key = ((long)w.from << 32) | (uint)w.to;
            if (onEdge.TryGetValue(key, out var others))
            {
                foreach (var o in others)
                {
                    if (o == w) continue;
                    float ahead = (o.t - w.t) * len;
                    if (ahead > 0f && ahead < 1.3f && Mathf.Abs(o.lateral - w.lateral) < 0.35f) target = Mathf.Min(target, o.current * 0.95f);
                }
            }
            w.current = Mathf.MoveTowards(w.current, target, dt * 2f);
            w.t += w.current * dt / len;
            w.offset = Vector3.MoveTowards(w.offset, Offset(w, w.from, w.to), dt * 0.8f);
            if (w.t >= 1f) Arrive(w);
            var pos = Vector3.Lerp(nodes[w.from], nodes[w.to], Mathf.Clamp01(w.t)) + w.offset;
            Pose(w, pos, b - a, dt, OnRoad(pos) ? 0f : groundHeight);
        }

        void Arrive(Walker w)
        {
            int at = w.to;
            if (endNodes[at])
            {
                w.hidden = true;
                w.hiddenFor = 1f + (float)rng.NextDouble() * 6f;
                w.root.gameObject.SetActive(false);
                return;
            }
            var options = adjacency[at];
            int choice = options[rng.Next(options.Count)];
            if (options.Count > 1)
                for (int tries = 0; tries < 6 && Other(choice, at) == w.from; tries++) choice = options[rng.Next(options.Count)];
            w.from = at;
            w.to = Other(choice, at);
            w.edge = choice;
            w.t = 0f;
            w.waiting = edges[choice].kind != EdgeKind.Sidewalk;
        }

        void Respawn(Walker w)
        {
            var ends = new List<int>();
            for (int i = 0; i < nodes.Length; i++) if (endNodes[i]) ends.Add(i);
            int start = ends[rng.Next(ends.Count)];
            int edge = adjacency[start][0];
            w.from = start;
            w.to = Other(edge, start);
            w.edge = edge;
            w.t = 0f;
            w.hidden = false;
            w.offset = Offset(w, w.from, w.to);
            w.root.gameObject.SetActive(true);
        }

        void Pose(Walker w, Vector3 pos, Vector3 heading, float dt, float ground)
        {
            // Ease over curb ramps instead of snapping between sidewalk and road height.
            w.height = Mathf.MoveTowards(w.height, ground, dt * 0.5f);
            const float stride = 1.45f;
            w.phase += w.current * dt * (Mathf.PI * 2f / stride);
            float amount = Mathf.Clamp01(w.current / 1.1f);
            float swing = Mathf.Sin(w.phase) * 27f * amount;
            if (w.legL) w.legL.localRotation = Quaternion.Euler(swing, 0f, 0f);
            if (w.legR) w.legR.localRotation = Quaternion.Euler(-swing, 0f, 0f);
            if (w.armL) w.armL.localRotation = Quaternion.Euler(-swing * 0.75f, 0f, 0f);
            if (w.armR) w.armR.localRotation = Quaternion.Euler(swing * 0.75f, 0f, 0f);
            pos.y = w.height + Mathf.Abs(Mathf.Cos(w.phase)) * 0.025f * amount;
            heading.y = 0f;
            var rot = heading.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(heading) : w.root.rotation;
            w.root.SetPositionAndRotation(pos, Quaternion.Slerp(w.root.rotation, rot, dt * 7f));
        }
    }
}
