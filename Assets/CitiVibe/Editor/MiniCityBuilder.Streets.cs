using System.Collections.Generic;
using UnityEngine;

namespace CitiVibe.EditorTools
{
    public static partial class MiniCityBuilder
    {
        /// <summary>A straight sidewalk between two intersections, measured from the road edge inward.</summary>
        sealed class Run
        {
            public Vector3 start, along, toRoad;
            public float length;
            public bool avenue;
            public int ix, iz;
            public readonly List<(float u, float r)> taken = new();

            /// <summary>Point <paramref name="u"/> metres along the run and <paramref name="d"/> metres in from the road edge, on the sidewalk surface.</summary>
            public Vector3 At(float u, float d) => start + along * u - toRoad * d + Vector3.up * Curb;

            public bool Free(float u, float r)
            {
                if (u - r < 0.5f || u + r > length - 0.5f) return false;
                foreach (var (tu, tr) in taken)
                    if (Mathf.Abs(tu - u) < tr + r) return false;
                return true;
            }

            public void Take(float u, float r) => taken.Add((u, r));
        }

        /// <summary>Road-edge span of block column <paramref name="ix"/> (0 = west of the west avenue).</summary>
        static (float a, float b) CellX(int ix) => ix switch { 0 => (-EX, -AV - AH), 1 => (-AV + AH, AV - AH), _ => (AV + AH, EX) };
        static (float a, float b) CellZ(int iz) => iz switch { 0 => (-EZ, -SZ - SH), 1 => (-SZ + SH, SZ - SH), _ => (SZ + SH, EZ) };

        /// <summary>Building lot of a block: its road-edge span minus the sidewalks on road sides.</summary>
        static (float x0, float x1, float z0, float z1) Lot(int ix, int iz)
        {
            var (xa, xb) = CellX(ix);
            var (za, zb) = CellZ(iz);
            return (xa + (ix > 0 ? SW : 0f), xb - (ix < 2 ? SW : 0f), za + (iz > 0 ? SW : 0f), zb - (iz < 2 ? SW : 0f));
        }

        static int AvenueFlow(float ax) => ax > 0f ? 1 : -1;
        static int StreetFlow(float sz) => sz < 0f ? 1 : -1;
        static int IntersectionIndex(float ax, float sz) => (ax > 0f ? 1 : 0) + (sz > 0f ? 2 : 0);

        /// <summary>Avenue lane centres in the direction of travel (positive = driver's right).</summary>
        static readonly float[] AvenueLanes = { -2.333f, 1f, 4.333f };
        const float AvenueParkLeft = -5.2f, AvenueParkRight = 7.4f, StreetPark = 3.75f;

        static List<Run> runs;
        static readonly List<Vector3> roadReserved = new();

        static List<Run> SidewalkRuns()
        {
            var list = new List<Run>();
            for (int ix = 0; ix < 3; ix++)
            for (int iz = 0; iz < 3; iz++)
            {
                var (xa, xb) = CellX(ix);
                var (za, zb) = CellZ(iz);
                var lot = Lot(ix, iz);
                if (iz > 0) list.Add(new Run { start = new Vector3(lot.x0, 0f, za), along = Vector3.right, toRoad = Vector3.back, length = lot.x1 - lot.x0, ix = ix, iz = iz });
                if (iz < 2) list.Add(new Run { start = new Vector3(lot.x0, 0f, zb), along = Vector3.right, toRoad = Vector3.forward, length = lot.x1 - lot.x0, ix = ix, iz = iz });
                if (ix > 0) list.Add(new Run { start = new Vector3(xa, 0f, lot.z0), along = Vector3.forward, toRoad = Vector3.left, length = lot.z1 - lot.z0, avenue = true, ix = ix, iz = iz });
                if (ix < 2) list.Add(new Run { start = new Vector3(xb, 0f, lot.z0), along = Vector3.forward, toRoad = Vector3.right, length = lot.z1 - lot.z0, avenue = true, ix = ix, iz = iz });
            }
            return list;
        }

        static void Mark(MeshBuilder mb, Material m, float x0, float x1, float z0, float z1) =>
            mb.Box(m, new Vector3((x0 + x1) * 0.5f, 0.003f, (z0 + z1) * 0.5f), new Vector3(Mathf.Abs(x1 - x0), 0.006f, Mathf.Abs(z1 - z0)));

        static void BuildGround(Transform parent)
        {
            runs = SidewalkRuns();
            roadReserved.Clear();
            var mb = new MeshBuilder();
            mb.Box(M.plinth, new Vector3(0f, -1.1f, 0f), new Vector3(EX * 2f, 2f, EZ * 2f));
            foreach (float ax in Avenues) mb.Box(M.asphalt, new Vector3(ax, -0.05f, 0f), new Vector3(AH * 2f, 0.1f, EZ * 2f));
            foreach (float sz in Streets)
                for (int ix = 0; ix < 3; ix++)
                {
                    var (a, b) = CellX(ix);
                    mb.Box(M.asphalt, new Vector3((a + b) * 0.5f, -0.05f, sz), new Vector3(b - a, 0.1f, SH * 2f));
                }

            for (int ix = 0; ix < 3; ix++)
            for (int iz = 0; iz < 3; iz++)
            {
                var (xa, xb) = CellX(ix);
                var (za, zb) = CellZ(iz);
                float sx0 = xa + (ix > 0 ? 0.2f : 0f), sx1 = xb - (ix < 2 ? 0.2f : 0f);
                float sz0 = za + (iz > 0 ? 0.2f : 0f), sz1 = zb - (iz < 2 ? 0.2f : 0f);
                mb.Box(M.concrete, new Vector3((sx0 + sx1) * 0.5f, 0.025f, (sz0 + sz1) * 0.5f), new Vector3(sx1 - sx0, 0.25f, sz1 - sz0));
                if (ix > 0) mb.Box(M.granite, new Vector3(xa + 0.1f, 0.025f, (za + zb) * 0.5f), new Vector3(0.2f, 0.25f, zb - za));
                if (ix < 2) mb.Box(M.granite, new Vector3(xb - 0.1f, 0.025f, (za + zb) * 0.5f), new Vector3(0.2f, 0.25f, zb - za));
                if (iz > 0) mb.Box(M.granite, new Vector3((sx0 + sx1) * 0.5f, 0.025f, za + 0.1f), new Vector3(sx1 - sx0, 0.25f, 0.2f));
                if (iz < 2) mb.Box(M.granite, new Vector3((sx0 + sx1) * 0.5f, 0.025f, zb - 0.1f), new Vector3(sx1 - sx0, 0.25f, 0.2f));
                if (ix == 1 && iz == 1) continue;
                var lot = Lot(ix, iz);
                mb.Box(M.grass, new Vector3((lot.x0 + lot.x1) * 0.5f, 0.155f, (lot.z0 + lot.z1) * 0.5f), new Vector3(lot.x1 - lot.x0 - 0.1f, 0.012f, lot.z1 - lot.z0 - 0.1f));
            }

            foreach (float ax in Avenues) AvenueMarkings(mb, ax);
            foreach (float sz in Streets) StreetMarkings(mb, sz);

            for (int i = 0; i < 14; i++)
            {
                bool avenue = i % 2 == 0;
                float ax = Pick(Avenues);
                Vector3 p = avenue
                    ? new Vector3(ax + AvenueFlow(ax) * Pick(AvenueLanes), 0f, Rand(-EZ + 4f, EZ - 4f))
                    : new Vector3(Rand(-EX + 4f, EX - 4f), 0f, Pick(Streets) + Rand(-1.2f, 1.2f));
                if (Near(p.x, Avenues, AH + 5f) && Near(p.z, Streets, SH + 5f)) continue;
                mb.Prism(M.iron, p, 0.42f, 0.008f, 16);
            }
            // Con Edison-style steam stacks vent from the street; parked cars keep clear of them.
            foreach (var p in new[] { new Vector3(-4f, 0f, SZ + StreetPark), new Vector3(58f, 0f, -SZ - StreetPark), new Vector3(-AV - AvenueParkRight, 0f, -48f) })
            {
                for (int band = 0; band < 5; band++)
                    mb.Prism(band % 2 == 0 ? M.steamOrange : M.lineWhite, p + Vector3.up * band * 0.5f, 0.34f - band * 0.02f, 0.5f, 18);
                mb.Ring(M.iron, p + Vector3.up * 2.5f, 0.2f, 0.27f, 0f, 0.06f, 18);
                roadReserved.Add(p);
            }
            mb.Finish("Streets and Sidewalks", parent);
        }

        static bool Near(float v, float[] centres, float within)
        {
            foreach (float c in centres)
                if (Mathf.Abs(v - c) < within) return true;
            return false;
        }

        /// <summary>Lane paint for a one-way avenue: parking-protected bike lane on the left, three travel lanes.</summary>
        static void AvenueMarkings(MeshBuilder mb, float ax)
        {
            int flow = AvenueFlow(ax);
            float X(float q) => ax + flow * q;
            void Band(Material m, float q0, float q1, float z0, float z1) => Mark(mb, m, Mathf.Min(X(q0), X(q1)), Mathf.Max(X(q0), X(q1)), z0, z1);
            float clear = SH + 4.6f;
            var spans = new List<(float, float)> { (-EZ, -SZ - clear), (-SZ + clear, SZ - clear), (SZ + clear, EZ) };
            foreach (var (z0, z1) in spans)
            {
                Band(M.bikeGreen, -AH + 0.2f, -7f, z0, z1);
                Band(M.lineWhite, -7f, -6.88f, z0, z1);
                Band(M.lineWhite, -6.52f, -6.4f, z0, z1);
                for (float z = z0 + 1f; z < z1 - 1f; z += 2.5f)
                    mb.Box(M.lineWhite, new Vector3(X(-6.64f), 0.003f, z), new Vector3(0.1f, 0.006f, 0.62f), Quaternion.Euler(0f, 45f, 0f));
                foreach (float q in new[] { -0.667f, 2.667f })
                    for (float z = z0 + 2f; z < z1 - 3f; z += 9f)
                        Band(M.lineWhite, q - 0.06f, q + 0.06f, z, z + 3f);
            }
            foreach (float sz in Streets)
            {
                foreach (int s in new[] { -1, 1 })
                {
                    float za = sz + s * (SH + 0.6f), zb = sz + s * (SH + 3.6f);
                    for (float x = ax - AH + 0.6f; x <= ax + AH - 0.5f; x += 1.1f)
                        Mark(mb, M.lineWhite, x - 0.28f, x + 0.28f, Mathf.Min(za, zb), Mathf.Max(za, zb));
                }
                int approach = -flow;
                float s0 = sz + approach * (SH + 4f), s1 = sz + approach * (SH + 4.45f);
                Mark(mb, M.lineWhite, ax - AH + 0.3f, ax + AH - 0.3f, Mathf.Min(s0, s1), Mathf.Max(s0, s1));
                // Green conflict markings carry the bike lane across the intersection.
                for (float z = sz - SH; z < sz + SH; z += 1.6f)
                    Band(M.bikeGreen, -AH + 0.2f, -7f, z, z + 0.8f);
            }
        }

        static void StreetMarkings(MeshBuilder mb, float sz)
        {
            int flow = StreetFlow(sz);
            foreach (float ax in Avenues)
            {
                foreach (int s in new[] { -1, 1 })
                {
                    float xa = ax + s * (AH + 0.6f), xb = ax + s * (AH + 3.6f);
                    for (float z = sz - SH + 0.6f; z <= sz + SH - 0.5f; z += 1.1f)
                        Mark(mb, M.lineWhite, Mathf.Min(xa, xb), Mathf.Max(xa, xb), z - 0.28f, z + 0.28f);
                }
                int approach = -flow;
                float s0 = ax + approach * (AH + 4f), s1 = ax + approach * (AH + 4.45f);
                Mark(mb, M.lineWhite, Mathf.Min(s0, s1), Mathf.Max(s0, s1), sz - SH + 0.3f, sz + SH - 0.3f);
            }
            // Parking lane edge lines.
            for (int ix = 0; ix < 3; ix++)
            {
                var (a, b) = CellX(ix);
                float x0 = a + (ix > 0 ? 4.6f : 0f), x1 = b - (ix < 2 ? 4.6f : 0f);
                foreach (int s in new[] { -1, 1 })
                    Mark(mb, M.lineWhite, x0, x1, sz + s * 2.55f - 0.05f, sz + s * 2.55f + 0.05f);
            }
        }
    }
}
