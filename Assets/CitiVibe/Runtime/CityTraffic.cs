using System;
using System.Collections.Generic;
using UnityEngine;

namespace CitiVibe
{
    /// <summary>
    /// One-way avenue and street traffic: fixed-time signals per intersection with a green wave,
    /// car following with safe gaps, and stop lines. Cars leaving the diorama re-enter at the
    /// opposite edge once their entry is clear.
    /// </summary>
    public class CityTraffic : MonoBehaviour
    {
        [Serializable]
        public class Lane
        {
            public Vector3 origin;
            public Vector3 direction;
            public float length;
            public float maxSpeed = 9f;
            public bool avenue;
            public float[] stops = Array.Empty<float>();
            public int[] stopIntersections = Array.Empty<int>();
        }

        [Serializable]
        public class Car
        {
            public Transform transform;
            public int lane;
            public float length = 4.8f;
            public float speedFactor = 1f;
            [NonSerialized] public float s, v, hiddenFor;
            [NonSerialized] public bool hidden;
        }

        [Serializable]
        public class SignalHead
        {
            public Renderer renderer;
            public int intersection;
            /// <summary>Vehicle heads: faces avenue traffic. Pedestrian heads: governs crossing the street (walking parallel to the avenue).</summary>
            public bool avenue;
            public bool pedestrian;
        }

        public static CityTraffic Instance { get; private set; }

        public List<Lane> lanes = new();
        public List<Car> cars = new();
        public List<SignalHead> heads = new();
        public float[] intersectionOffsets = new float[4];
        public float avenueGreen = 24f, streetGreen = 20f, amber = 3f, allRed = 2.5f;
        public float pedestrianStartWindow = 6f;
        public Material lensRed, lensAmber, lensGreen, lensWalk, lensStop;

        const float Accel = 2.4f, Brake = 7f, PlanDecel = 3.2f, Gap = 2.6f;

        List<int>[] laneCars;
        MaterialPropertyBlock block;
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor"), Emission = Shader.PropertyToID("_EmissionColor");

        public float Cycle => avenueGreen + streetGreen + 2f * (amber + allRed);

        public enum Aspect { Green, Amber, Red }

        /// <summary>Signal for one axis at an intersection, and seconds since its green began.</summary>
        public Aspect State(int intersection, bool avenue, out float sinceGreen)
        {
            float t = Mathf.Repeat(Time.time + intersectionOffsets[intersection], Cycle);
            float streetStart = avenueGreen + amber + allRed;
            if (avenue)
            {
                sinceGreen = t;
                if (t < avenueGreen) return Aspect.Green;
                return t < avenueGreen + amber ? Aspect.Amber : Aspect.Red;
            }
            sinceGreen = t - streetStart;
            if (t >= streetStart && t < streetStart + streetGreen) return Aspect.Green;
            return t >= streetStart + streetGreen && t < streetStart + streetGreen + amber ? Aspect.Amber : Aspect.Red;
        }

        /// <summary>Pedestrians may step off the curb only early in the parallel green, so they clear before conflicting traffic moves.</summary>
        public bool PedestrianMayStart(int intersection, bool parallelToAvenue)
        {
            var light = State(intersection, parallelToAvenue, out float since);
            return light == Aspect.Green && since < pedestrianStartWindow;
        }

        void OnEnable() => Instance = this;
        void OnDisable() { if (Instance == this) Instance = null; }

        void Start()
        {
            block = new MaterialPropertyBlock();
            laneCars = new List<int>[lanes.Count];
            for (int i = 0; i < lanes.Count; i++) laneCars[i] = new List<int>();
            foreach (var car in cars)
            {
                var lane = lanes[car.lane];
                car.s = Vector3.Dot(car.transform.position - lane.origin, lane.direction);
                car.v = lane.maxSpeed * car.speedFactor * 0.6f;
            }
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            for (int i = 0; i < laneCars.Length; i++) laneCars[i].Clear();
            for (int i = 0; i < cars.Count; i++) laneCars[cars[i].lane].Add(i);
            for (int li = 0; li < lanes.Count; li++)
            {
                var lane = lanes[li];
                var list = laneCars[li];
                list.Sort((a, b) => cars[b].s.CompareTo(cars[a].s));
                Car leader = null;
                foreach (int ci in list)
                {
                    var car = cars[ci];
                    if (car.hidden) { TryRespawn(car, lane, list, dt); continue; }
                    Drive(car, lane, leader, dt);
                    leader = car;
                }
            }
            UpdateHeads();
        }

        void Drive(Car car, Lane lane, Car leader, float dt)
        {
            float desired = lane.maxSpeed * car.speedFactor;
            float front = car.s + car.length * 0.5f;
            if (leader != null)
            {
                float gap = leader.s - leader.length * 0.5f - front - Gap;
                desired = Mathf.Min(desired, Mathf.Sqrt(2f * PlanDecel * Mathf.Max(0f, gap)) + Mathf.Max(0f, leader.v - 1f) * 0.5f);
                if (gap < 0.3f) desired = 0f;
            }
            for (int k = 0; k < lane.stops.Length; k++)
            {
                float dist = lane.stops[k] - front;
                if (dist < -0.2f) continue;
                if (dist > 70f) break;
                var light = State(lane.stopIntersections[k], lane.avenue, out _);
                bool mustStop = light == Aspect.Red || (light == Aspect.Amber && dist > car.v * car.v / (2f * 4.5f) + 0.5f);
                if (mustStop) desired = Mathf.Min(desired, Mathf.Sqrt(2f * PlanDecel * Mathf.Max(0f, dist - 0.4f)));
                break;
            }
            car.v = desired > car.v ? Mathf.Min(desired, car.v + Accel * dt) : Mathf.Max(desired, car.v - Brake * dt);
            car.s += car.v * dt;
            if (car.s - car.length * 0.5f > lane.length)
            {
                car.hidden = true;
                car.hiddenFor = 0f;
                car.transform.gameObject.SetActive(false);
                return;
            }
            Place(car, lane);
        }

        void TryRespawn(Car car, Lane lane, List<int> list, float dt)
        {
            car.hiddenFor += dt;
            if (car.hiddenFor < 1.5f) return;
            float entry = -car.length * 0.5f;
            foreach (int other in list)
            {
                var o = cars[other];
                if (o == car || o.hidden) continue;
                if (o.s - o.length * 0.5f < entry + car.length + 8f) return;
            }
            car.hidden = false;
            car.s = entry;
            car.v = lane.maxSpeed * car.speedFactor * 0.8f;
            car.transform.gameObject.SetActive(true);
            Place(car, lane);
        }

        static void Place(Car car, Lane lane)
        {
            car.transform.SetPositionAndRotation(lane.origin + lane.direction * car.s, Quaternion.LookRotation(lane.direction));
            float edge = Mathf.Min(car.s + car.length * 0.5f, lane.length - car.s + car.length * 0.5f);
            car.transform.localScale = Vector3.one * Mathf.SmoothStep(0.05f, 1f, Mathf.Clamp01(edge / 3f));
        }

        void UpdateHeads()
        {
            foreach (var head in heads)
            {
                if (head.renderer == null) continue;
                var shared = head.renderer.sharedMaterials;
                if (head.pedestrian)
                {
                    bool walk = PedestrianMayStart(head.intersection, head.avenue) ||
                                (State(head.intersection, head.avenue, out float since) == Aspect.Green && since < pedestrianStartWindow + 4f &&
                                 Mathf.Repeat(Time.time, 1f) < 0.5f);
                    for (int i = 0; i < shared.Length; i++)
                    {
                        if (shared[i] == lensWalk) Lens(head.renderer, i, new Color(0.95f, 0.95f, 0.92f), walk);
                        else if (shared[i] == lensStop) Lens(head.renderer, i, new Color(1f, 0.45f, 0.1f), !walk);
                    }
                    continue;
                }
                var light = State(head.intersection, head.avenue, out _);
                for (int i = 0; i < shared.Length; i++)
                {
                    if (shared[i] == lensRed) Lens(head.renderer, i, new Color(1f, 0.1f, 0.06f), light == Aspect.Red);
                    else if (shared[i] == lensAmber) Lens(head.renderer, i, new Color(1f, 0.62f, 0.08f), light == Aspect.Amber);
                    else if (shared[i] == lensGreen) Lens(head.renderer, i, new Color(0.15f, 1f, 0.6f), light == Aspect.Green);
                }
            }
        }

        void Lens(Renderer r, int index, Color color, bool on)
        {
            block.Clear();
            block.SetColor(BaseColor, on ? color : color * 0.12f);
            block.SetColor(Emission, on ? color * 4f : Color.black);
            r.SetPropertyBlock(block, index);
        }
    }
}
