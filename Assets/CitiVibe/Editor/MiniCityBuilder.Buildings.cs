using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CitiVibe.EditorTools
{
    public static partial class MiniCityBuilder
    {
        enum Style { Tenement, Brownstone, Office, Loft, Prewar }
        enum Facing { North, South, East, West }

        sealed class Spec
        {
            public string Name;
            public Style Style;
            public float Width, Depth;
            public int Floors;
            public Material Wall, Trim, Awning, SignBand;
            public bool Storefront, FireEscape, WaterTower, Hvac, ExposeLeft, ExposeRight, RoadLeft, RoadRight;
            public float AcChance;

            public List<(float y, float h)> Levels()
            {
                var levels = new List<(float y, float h)>();
                float y = 0f;
                void Add(float h) { levels.Add((y, h)); y += h; }
                switch (Style)
                {
                    case Style.Brownstone:
                        Add(1.4f); Add(3.4f);
                        for (int i = 2; i <= Floors; i++) Add(3.0f);
                        break;
                    case Style.Office:
                        Add(4.5f); Add(4.5f);
                        for (int i = 2; i < Floors; i++) Add(3.6f);
                        break;
                    case Style.Loft:
                        for (int i = 0; i < Floors; i++) Add(i == 0 ? 4.6f : 3.9f);
                        break;
                    case Style.Prewar:
                        Add(5f);
                        for (int i = 1; i < Floors; i++) Add(3.25f);
                        break;
                    default:
                        Add(Storefront ? 4.2f : 3.2f);
                        for (int i = 1; i < Floors; i++) Add(3.05f);
                        break;
                }
                return levels;
            }
        }

        /// <summary>Per-block character: weighted building types for street and avenue frontages.</summary>
        static readonly Dictionary<(int, int), (Style[] street, float[] streetW, Style[] avenue, float[] avenueW)> BlockStyles = new()
        {
            [(0, 0)] = (new[] { Style.Brownstone, Style.Tenement }, new[] { 0.6f, 0.4f }, new[] { Style.Tenement, Style.Loft }, new[] { 0.7f, 0.3f }),
            [(1, 0)] = (new[] { Style.Office, Style.Prewar, Style.Loft }, new[] { 0.35f, 0.35f, 0.3f }, new[] { Style.Prewar, Style.Tenement }, new[] { 0.5f, 0.5f }),
            [(2, 0)] = (new[] { Style.Loft, Style.Tenement }, new[] { 0.7f, 0.3f }, new[] { Style.Loft, Style.Office }, new[] { 0.55f, 0.45f }),
            [(0, 1)] = (new[] { Style.Brownstone, Style.Tenement }, new[] { 0.8f, 0.2f }, new[] { Style.Tenement }, new[] { 1f }),
            [(2, 1)] = (new[] { Style.Tenement, Style.Brownstone }, new[] { 0.6f, 0.4f }, new[] { Style.Tenement, Style.Prewar }, new[] { 0.7f, 0.3f }),
            [(0, 2)] = (new[] { Style.Tenement, Style.Brownstone }, new[] { 0.5f, 0.5f }, new[] { Style.Office, Style.Loft }, new[] { 0.6f, 0.4f }),
            [(1, 2)] = (new[] { Style.Brownstone, Style.Tenement, Style.Prewar }, new[] { 0.45f, 0.4f, 0.15f }, new[] { Style.Tenement, Style.Loft }, new[] { 0.6f, 0.4f }),
            [(2, 2)] = (new[] { Style.Brownstone, Style.Tenement }, new[] { 0.55f, 0.45f }, new[] { Style.Tenement, Style.Office, Style.Prewar }, new[] { 0.45f, 0.3f, 0.25f }),
        };

        static Style Weighted(Style[] styles, float[] weights)
        {
            float total = 0f;
            foreach (float w in weights) total += w;
            float r = Rand(0f, total);
            for (int i = 0; i < styles.Length; i++)
            {
                r -= weights[i];
                if (r <= 0f) return styles[i];
            }
            return styles[^1];
        }

        static Spec MakeSpec(Style style, bool corner, bool avenue, float maxDepth)
        {
            var s = new Spec { Style = style, SignBand = Pick(M.signBands) };
            switch (style)
            {
                case Style.Brownstone:
                    s.Width = Rand(5.9f, 6.4f);
                    s.Depth = Rand(13f, 14f);
                    s.Floors = rng.Next(3, 5);
                    s.Wall = rng.NextDouble() < 0.8 ? M.brownstone : Pick(M.bricks);
                    s.Trim = M.brownstone;
                    s.AcChance = 0.05f;
                    break;
                case Style.Tenement:
                    s.Width = Pick(new[] { 7.6f, 7.6f, 7.6f, 10.1f, 12.6f });
                    s.Depth = Rand(15f, 17.5f);
                    s.Floors = corner ? rng.Next(6, 8) : rng.Next(5, 7);
                    s.Wall = Pick(M.bricks);
                    s.Trim = M.limestone;
                    s.Storefront = avenue || corner || rng.NextDouble() < 0.25;
                    s.FireEscape = rng.NextDouble() < 0.6;
                    s.WaterTower = rng.NextDouble() < 0.35;
                    s.AcChance = 0.14f;
                    break;
                case Style.Loft:
                    s.Width = Rand(12f, 18f);
                    s.Depth = Rand(18f, 22f);
                    s.Floors = rng.Next(6, 10);
                    s.Wall = rng.NextDouble() < 0.5 ? M.loftPaint : Pick(M.bricks);
                    s.Trim = s.Wall == M.loftPaint ? M.loftPaint : M.limestone;
                    s.Storefront = true;
                    s.WaterTower = rng.NextDouble() < 0.55;
                    s.Hvac = rng.NextDouble() < 0.4;
                    s.AcChance = 0.04f;
                    break;
                case Style.Prewar:
                    s.Width = Rand(14f, 20f);
                    s.Depth = Rand(18f, 22f);
                    s.Floors = rng.Next(10, 16);
                    s.Wall = rng.NextDouble() < 0.4 ? M.limestone : Pick(new[] { M.bricks[1], M.bricks[5], M.bricks[6], M.bricks[0] });
                    s.Trim = M.limestone;
                    s.Storefront = true;
                    s.WaterTower = rng.NextDouble() < 0.65;
                    s.Hvac = true;
                    s.AcChance = 0.1f;
                    break;
                default:
                    s.Width = Rand(16f, 24f);
                    s.Depth = Rand(18f, 24f);
                    s.Floors = rng.Next(12, 21);
                    s.Wall = M.spandrel;
                    s.Hvac = true;
                    break;
            }
            if (s.Storefront && rng.NextDouble() < 0.7) s.Awning = Pick(M.awnings);
            s.Depth = Mathf.Min(s.Depth, maxDepth - 0.3f);
            if (s.Depth < 9f && style is Style.Office or Style.Prewar or Style.Loft)
                return MakeSpec(Style.Tenement, corner, avenue, maxDepth);
            return s;
        }

        static void Place(Transform parent, Spec s, Facing facing, float front, float alongMin)
        {
            (Vector3 pos, float yaw) = facing switch
            {
                Facing.South => (new Vector3(alongMin + s.Width, Curb, front), 180f),
                Facing.North => (new Vector3(alongMin, Curb, front), 0f),
                Facing.East => (new Vector3(front, Curb, alongMin + s.Width), 90f),
                _ => (new Vector3(front, Curb, alongMin), -90f),
            };
            var mb = new MeshBuilder();
            var attachments = BuildBuilding(mb, s);
            var go = mb.Finish(s.Name, parent, pos, yaw);
            foreach (var (local, model) in attachments)
            {
                var item = Spawn(model, go.transform, Vector3.zero, Vector3.forward);
                item.transform.localPosition = local;
                item.transform.localRotation = Quaternion.Euler(0f, rng.Next(4) * 90f, 0f);
            }
        }

        /// <summary>
        /// Each block is two back-to-back rows of attached buildings along its street sides (or the
        /// diorama edge). Corner buildings turn the corner with a second storefront facade on the avenue.
        /// </summary>
        static int BuildBlocks(Transform parent)
        {
            int count = 0;
            const float edgeInset = 1.8f;
            for (int ix = 0; ix < 3; ix++)
            for (int iz = 0; iz < 3; iz++)
            {
                if (ix == 1 && iz == 1) continue;
                var lot = Lot(ix, iz);
                var style = BlockStyles[(ix, iz)];
                var block = Group(parent, $"Block {"WCE"[ix]}{"SMN"[iz]}");
                bool roadW = ix > 0, roadE = ix < 2, roadS = iz > 0, roadN = iz < 2;
                float z0 = lot.z0 + (roadS ? 0f : edgeInset), z1 = lot.z1 - (roadN ? 0f : edgeInset);
                float depth = Mathf.Min(24f, (z1 - z0) * 0.5f);
                var edgeStyles = System.Array.ConvertAll(style.street, st => st == Style.Brownstone ? Style.Tenement : st);
                Row(block, roadS ? style.street : edgeStyles, style.streetW, Facing.South, z0, lot.x0, lot.x1, roadW, roadE, depth, !roadS, ref count);
                Row(block, roadN ? style.street : edgeStyles, style.streetW, Facing.North, z1, lot.x0, lot.x1, roadW, roadE, depth, !roadN, ref count);
            }
            return count;
        }

        /// <summary>Fills one frontage with attached buildings from a0 to a1 (world x); returns the deepest building.</summary>
        static float Row(Transform parent, Style[] styles, float[] weights, Facing facing, float front, float a0, float a1,
            bool cornerStart, bool cornerEnd, float maxDepth, bool backOfBlock, ref int count)
        {
            if (a1 - a0 < 4f) return 0f;
            bool tall = System.Array.IndexOf(styles, Style.Office) >= 0 || System.Array.IndexOf(styles, Style.Prewar) >= 0;
            Spec Corner()
            {
                var corner = tall && rng.NextDouble() < 0.5 ? (rng.NextDouble() < 0.5 ? Style.Prewar : Style.Office) : Style.Tenement;
                var spec = MakeSpec(corner, true, true, maxDepth);
                spec.Storefront = spec.Style != Style.Office;
                if (spec.Storefront && spec.Awning == null && rng.NextDouble() < 0.6) spec.Awning = Pick(M.awnings);
                return spec;
            }
            var start = cornerStart ? Corner() : null;
            var end = cornerEnd ? Corner() : null;
            float lo = a0 + (start?.Width ?? 0f), hi = a1 - (end?.Width ?? 0f);
            var middle = new List<Spec>();
            float cursor = lo;
            while (hi - cursor >= 4f)
            {
                var spec = MakeSpec(Weighted(styles, weights), false, false, maxDepth);
                if (backOfBlock) { spec.Storefront = false; spec.Awning = null; }
                if (hi - cursor - spec.Width < 4.5f) spec.Width = hi - cursor;
                if (spec.Style == Style.Brownstone && spec.Width > 8.5f) spec = Widen(MakeSpec(Style.Tenement, false, false, maxDepth), spec.Width);
                middle.Add(spec);
                cursor += spec.Width;
            }
            float slack = hi - cursor;
            if (slack > 0.01f)
            {
                var target = middle.Count > 0 ? middle[^1] : start ?? end;
                if (target == null) return 0f;
                target.Width += slack;
            }
            var all = new List<Spec>();
            if (start != null) all.Add(start);
            all.AddRange(middle);
            if (end != null) all.Add(end);
            all[0].ExposeLeft = all[0].ExposeRight = true;
            all[^1].ExposeLeft = all[^1].ExposeRight = true;
            // Local left (a = 0) is the world-low end for north-facing rows and the world-high end for south-facing ones.
            bool lowIsLeft = facing == Facing.North;
            if (cornerStart) { if (lowIsLeft) all[0].RoadLeft = true; else all[0].RoadRight = true; }
            if (cornerEnd) { if (lowIsLeft) all[^1].RoadRight = true; else all[^1].RoadLeft = true; }
            float along = a0, deepest = 0f;
            foreach (var s in all)
            {
                s.Name = $"{s.Style} {++count}";
                Place(parent, s, facing, front, along);
                along += s.Width;
                deepest = Mathf.Max(deepest, s.Depth);
            }
            return deepest;
        }

        static Spec Widen(Spec s, float width)
        {
            s.Width = width;
            return s;
        }

        /// <summary>Builds one building in local space (front face on z = 0 facing +z, extending to -z); returns rooftop model attachments.</summary>
        static List<(Vector3 local, string model)> BuildBuilding(MeshBuilder mb, Spec s)
        {
            var attachments = new List<(Vector3, string)>();
            var levels = s.Levels();
            float w = s.Width, d = s.Depth;
            float top = levels[^1].y + levels[^1].h;
            float shaftTop = s.Style == Style.Office ? levels[^3].y + levels[^3].h : top;

            mb.Box(s.Wall, new Vector3(w * 0.5f, shaftTop * 0.5f, -d * 0.5f), new Vector3(w, shaftTop, d));

            var front = new Face(mb, Vector3.zero, Vector3.forward, w);
            var faces = new List<(Face face, bool isFront)> { (front, true), (new Face(mb, new Vector3(w, 0f, -d), Vector3.back, w), false) };
            Face left = null, right = null;
            if (s.ExposeLeft) faces.Add((left = new Face(mb, new Vector3(0f, 0f, -d), Vector3.left, d), s.RoadLeft));
            if (s.ExposeRight) faces.Add((right = new Face(mb, new Vector3(w, 0f, 0f), Vector3.right, d), s.RoadRight));

            if (s.Style == Style.Office)
            {
                BuildOffice(mb, s, levels, faces, shaftTop, top);
                attachments.Add((new Vector3(w * 0.3f, top, -d * 0.35f), "HVAC"));
                attachments.Add((new Vector3(w * 0.68f, top, -d * 0.65f), "HVAC"));
                return attachments;
            }

            foreach (var (face, isFront) in faces) Facade(face, s, levels, isFront, top);
            if (s.Storefront)
            {
                Storefront(front, s, levels[0].h);
                if (s.RoadLeft && left != null) Storefront(left, s, levels[0].h);
                if (s.RoadRight && right != null) Storefront(right, s, levels[0].h);
            }
            if (s.Style == Style.Brownstone) Stoop(front, front.Width / 3f * 0.5f);
            if (s.FireEscape) FireEscape(front, levels);

            // Parapet, roof and rooftop structures.
            const float t = 0.25f, ph = 0.6f;
            mb.Box(s.Wall, new Vector3(w * 0.5f, top + ph * 0.5f, -t * 0.5f), new Vector3(w, ph, t));
            mb.Box(s.Wall, new Vector3(w * 0.5f, top + ph * 0.5f, -d + t * 0.5f), new Vector3(w, ph, t));
            mb.Box(s.Wall, new Vector3(t * 0.5f, top + ph * 0.5f, -d * 0.5f), new Vector3(t, ph, d - 2f * t));
            mb.Box(s.Wall, new Vector3(w - t * 0.5f, top + ph * 0.5f, -d * 0.5f), new Vector3(t, ph, d - 2f * t));
            foreach (float z in new[] { -t * 0.5f, -d + t * 0.5f })
                mb.Box(s.Trim, new Vector3(w * 0.5f, top + ph + 0.04f, z), new Vector3(w + 0.04f, 0.08f, t + 0.08f));
            mb.Box(M.roof, new Vector3(w * 0.5f, top + 0.02f, -d * 0.5f), new Vector3(w - 2f * t, 0.04f, d - 2f * t));
            if (w > 5f)
            {
                // Stair bulkhead with a door and a skylight.
                mb.Box(s.Wall, new Vector3(w * 0.72f, top + 1.25f, -d * 0.72f), new Vector3(2.4f, 2.5f, 2.6f));
                mb.Box(M.door, new Vector3(w * 0.72f, top + 1.05f, -d * 0.72f + 1.31f), new Vector3(0.9f, 2.0f, 0.04f));
                mb.Box(M.glass[1], new Vector3(w * 0.35f, top + 0.25f, -d * 0.3f), new Vector3(1.2f, 0.4f, 1.2f));
            }
            if (s.Style == Style.Brownstone)
            {
                mb.Box(s.Wall, new Vector3(0.45f, top + 0.9f, -d * 0.35f), new Vector3(0.6f, 1.8f, 0.9f));
                mb.Box(s.Wall, new Vector3(w - 0.45f, top + 0.9f, -d * 0.6f), new Vector3(0.6f, 1.8f, 0.9f));
            }
            if (s.WaterTower && w >= 7f) attachments.Add((new Vector3(Mathf.Min(w * 0.33f, w - 2.2f), top, -d * 0.55f), "WaterTower"));
            if (s.Hvac && w >= 10f) attachments.Add((new Vector3(w * 0.7f, top, -d * 0.35f), "HVAC"));
            return attachments;
        }

        static Material GlassFor()
        {
            double r = rng.NextDouble();
            if (r < 0.3) return M.litWarm;
            if (r < 0.42) return M.litCool;
            if (r < 0.52) return M.curtained;
            return M.glass[rng.Next(M.glass.Length)];
        }

        static void Window(Face f, float a, float sill, float ww, float wh, Material frame, Material trim, bool surround, float acChance)
        {
            f.Box(GlassFor(), a, sill + wh * 0.5f, 0.01f, ww, wh, 0.02f);
            // Reveal: dark jambs and head make the glass read as set back into the wall.
            f.Box(M.voidBlack, a, sill + wh - 0.02f, 0.03f, ww, 0.04f, 0.06f);
            f.Box(frame, a - ww * 0.5f, sill + wh * 0.5f, 0.035f, 0.07f, wh, 0.07f);
            f.Box(frame, a + ww * 0.5f, sill + wh * 0.5f, 0.035f, 0.07f, wh, 0.07f);
            f.Box(frame, a, sill + wh - 0.035f, 0.035f, ww, 0.07f, 0.07f);
            f.Box(frame, a, sill + wh * 0.52f, 0.035f, ww, 0.05f, 0.06f);
            f.Box(trim, a, sill - 0.05f, 0.08f, ww + 0.24f, 0.1f, 0.16f);
            f.Box(trim, a, sill + wh + 0.12f, 0.05f, ww + 0.3f, 0.22f, 0.1f);
            if (surround)
            {
                f.Box(trim, a - ww * 0.5f - 0.1f, sill + wh * 0.5f, 0.04f, 0.14f, wh, 0.08f);
                f.Box(trim, a + ww * 0.5f + 0.1f, sill + wh * 0.5f, 0.04f, 0.14f, wh, 0.08f);
            }
            if (acChance > 0f && rng.NextDouble() < acChance)
            {
                // Window air conditioner resting on the sill.
                float w = Mathf.Min(0.62f, ww - 0.1f);
                f.Box(M.acWhite, a, sill + 0.21f, 0.2f, w, 0.4f, 0.42f);
                for (int i = 0; i < 5; i++) f.Box(M.iron, a, sill + 0.08f + i * 0.065f, 0.415f, w - 0.1f, 0.02f, 0.01f);
                f.Box(M.iron, a, sill - 0.02f, 0.25f, w + 0.04f, 0.04f, 0.36f);
            }
        }

        static void Facade(Face f, Spec s, List<(float y, float h)> levels, bool isFront, float top)
        {
            bool brownstone = s.Style == Style.Brownstone, loft = s.Style == Style.Loft, prewar = s.Style == Style.Prewar;
            float bay = loft ? 2.6f : prewar ? 2.2f : 2f;
            int bays = brownstone && isFront ? 3 : Mathf.Max(1, Mathf.RoundToInt(f.Width / bay));
            float bw = f.Width / bays;
            Material trim = s.Trim ?? M.limestone;
            Material frame = brownstone || loft || prewar ? M.frameDark : M.frameWhite;

            for (int li = 0; li < levels.Count; li++)
            {
                var (y, h) = levels[li];
                if (isFront && li == 0 && (s.Storefront || brownstone)) continue;
                for (int b = 0; b < bays; b++)
                {
                    float a = (b + 0.5f) * bw;
                    if (brownstone && isFront && li == 1 && b == 0)
                    {
                        f.Box(M.door, a, y + 1.3f, 0.04f, 1.3f, 2.6f, 0.06f);
                        f.Box(trim, a, y + 2.85f, 0.25f, 2f, 0.3f, 0.5f);
                        f.Box(trim, a - 0.8f, y + 1.3f, 0.08f, 0.25f, 2.6f, 0.16f);
                        f.Box(trim, a + 0.8f, y + 1.3f, 0.08f, 0.25f, 2.6f, 0.16f);
                        continue;
                    }
                    if (li == 0 && b == bays / 2)
                    {
                        f.Box(M.door, a, y + 1.12f, 0.04f, 1.15f, 2.25f, 0.06f);
                        if (isFront) f.Box(trim, a, y + 2.45f, 0.3f, 1.8f, 0.12f, 0.6f);
                        continue;
                    }
                    float ww = loft ? 1.7f : Mathf.Min(1.1f, bw * 0.55f);
                    float wh = loft ? 2.6f : brownstone && li == 1 ? 2.4f : brownstone && li == 0 ? 0.8f : 1.75f;
                    float sill = y + (li == 0 && brownstone ? 0.3f : (h - wh) * 0.55f);
                    Window(f, a, sill, ww, wh, frame, trim, (brownstone && li > 0) || (prewar && li == 1), li > 0 ? s.AcChance : 0f);
                }
                if (loft && li > 0) f.Box(M.loftPaint, f.Width * 0.5f, y + 0.12f, 0.1f, f.Width, 0.3f, 0.2f);
                if (prewar && li == 2) f.Box(trim, f.Width * 0.5f, y + 0.1f, 0.08f, f.Width, 0.25f, 0.16f);
            }

            if (loft)
                for (int b = 0; b <= bays; b++)
                    f.Box(M.loftPaint, b * bw, top * 0.5f, 0.1f, 0.4f, top, 0.2f);
            if (prewar)
            {
                // Rusticated limestone base and corner quoins.
                f.Box(M.limestone, f.Width * 0.5f, levels[1].y + levels[1].h * 0.5f, 0.04f, f.Width, levels[1].h, 0.08f);
                for (float y = levels[2].y; y < top - 0.4f; y += 0.6f)
                {
                    f.Box(M.limestone, 0.35f, y + 0.2f, 0.04f, 0.7f, 0.4f, 0.08f);
                    f.Box(M.limestone, f.Width - 0.35f, y + 0.2f, 0.04f, 0.7f, 0.4f, 0.08f);
                }
            }
            if (!brownstone && !loft && s.Storefront && isFront)
                f.Box(M.limestone, f.Width * 0.5f, levels[0].h + 0.06f, 0.05f, f.Width, 0.16f, 0.1f);

            // Cornice: frieze, projecting crown and brackets.
            if (!isFront && !brownstone) return;
            Material crown = loft ? M.loftPaint : prewar ? M.limestone : M.cornice;
            f.Box(crown, f.Width * 0.5f, top + 0.15f, 0.12f, f.Width, 0.6f, 0.24f);
            f.Box(crown, f.Width * 0.5f, top + 0.55f, 0.35f, f.Width, 0.2f, 0.75f);
            for (float a = 0.4f; a < f.Width - 0.2f; a += 1.2f)
                f.Box(crown, a, top + 0.2f, 0.3f, 0.14f, 0.5f, 0.5f);
            for (float a = 0.2f; a < f.Width - 0.1f; a += 0.3f)
                f.Box(crown, a, top + 0.4f, 0.27f, 0.1f, 0.1f, 0.1f);
        }

        static void Storefront(Face f, Spec s, float gh)
        {
            float w = f.Width;
            f.Box(M.frameDark, w * 0.5f, 0.25f, 0.03f, w - 0.6f, 0.5f, 0.06f);
            f.Box(M.storeGlass, w * 0.5f, 0.5f + 1.45f, 0.02f, w - 0.6f, 2.9f, 0.04f);
            f.Box(M.frameDark, w * 0.5f, 3.0f, 0.04f, w - 0.6f, 0.08f, 0.08f);
            for (float a = 0.3f; a <= w - 0.29f; a += (w - 0.6f) / Mathf.Max(1, Mathf.RoundToInt((w - 0.6f) / 1.6f)))
                f.Box(M.frameDark, a, 1.75f, 0.04f, 0.08f, 2.5f, 0.08f);
            f.Box(M.door, 1.1f, 1.25f, 0.05f, 1.0f, 2.4f, 0.06f);
            f.Box(s.SignBand, w * 0.5f, gh - 0.45f, 0.06f, w - 0.4f, 0.6f, 0.12f);
            Material pier = s.Wall == M.loftPaint ? M.loftPaint : M.limestone;
            f.Box(pier, 0.15f, gh * 0.5f, 0.05f, 0.3f, gh, 0.1f);
            f.Box(pier, w - 0.15f, gh * 0.5f, 0.05f, 0.3f, gh, 0.1f);
            if (s.Awning == null) return;
            const float tilt = 20f, depth = 1.5f;
            f.Box(s.Awning, w * 0.5f, 3.3f, depth * 0.5f, new Vector3(w - 0.8f, 0.05f, depth), Quaternion.Euler(tilt, 0f, 0f));
            float outer = depth * Mathf.Cos(tilt * Mathf.Deg2Rad), drop = depth * Mathf.Sin(tilt * Mathf.Deg2Rad);
            f.Box(s.Awning, w * 0.5f, 3.3f - drop * 0.5f - 0.15f, outer * 0.5f + depth * 0.5f - 0.02f, w - 0.8f, 0.3f, 0.03f);
        }

        /// <summary>Compact brownstone stoop: a 1.4 m landing and six risers, kept within 1.7 m of the facade so the walking path stays clear.</summary>
        static void Stoop(Face f, float a)
        {
            f.Box(M.brownstone, a, 0.7f, 0.3f, 1.8f, 1.4f, 0.6f);
            for (int i = 1; i <= 6; i++)
            {
                float topY = 1.4f - 0.2f * i, o0 = 0.6f + (i - 1) * 0.17f;
                f.Box(M.brownstone, a, topY * 0.5f, o0 + 0.085f, 1.7f, topY, 0.17f);
            }
            float run = 1.02f, rise = 1.2f, len = Mathf.Sqrt(run * run + rise * rise), angle = Mathf.Atan2(rise, run) * Mathf.Rad2Deg;
            foreach (int side in new[] { -1, 1 })
            {
                float x = a + side * 0.88f;
                f.Box(M.iron, x, 1.7f, 1.11f, new Vector3(0.05f, 0.05f, len), Quaternion.Euler(angle, 0f, 0f));
                f.Box(M.iron, x, 2.3f, 0.3f, 0.05f, 0.05f, 0.6f);
                foreach (var (o, baseY) in new[] { (0.05f, 1.4f), (0.6f, 1.4f), (1.6f, 0.2f) })
                    f.Box(M.iron, x, baseY + 0.45f, o, 0.05f, 0.9f, 0.05f);
            }
        }

        static void FireEscape(Face f, List<(float y, float h)> levels)
        {
            float a0 = f.Width * 0.25f, a1 = f.Width * 0.75f, span = a1 - a0, mid = (a0 + a1) * 0.5f;
            for (int li = 1; li < levels.Count; li++)
            {
                float y = levels[li].y;
                f.Box(M.iron, mid, y, 0.6f, span, 0.06f, 1.2f);
                f.Box(M.iron, mid, y + 0.95f, 1.18f, span, 0.04f, 0.04f);
                f.Box(M.iron, mid, y + 0.5f, 1.18f, span, 0.03f, 0.03f);
                foreach (float edge in new[] { a0, a1 })
                {
                    f.Box(M.iron, edge, y + 0.95f, 0.6f, 0.04f, 0.04f, 1.2f);
                    f.Box(M.iron, edge, y + 0.48f, 1.18f, 0.04f, 0.96f, 0.04f);
                }
                for (float a = a0 + 0.3f; a < a1; a += 0.3f) f.Box(M.iron, a, y + 0.48f, 1.18f, 0.02f, 0.96f, 0.02f);
                if (li == levels.Count - 1) continue;
                float rise = levels[li].h, run = 1.8f, len = Mathf.Sqrt(rise * rise + run * run);
                float angle = Mathf.Atan2(rise, run) * Mathf.Rad2Deg * (li % 2 == 0 ? 1f : -1f);
                f.Box(M.iron, mid, y + rise * 0.5f, 0.75f, new Vector3(len, 0.05f, 0.6f), Quaternion.Euler(0f, 0f, angle));
            }
            float ladderTop = levels[1].y;
            foreach (float off in new[] { -0.22f, 0.22f })
                f.Box(M.iron, a1 - 0.5f + off, ladderTop - 1.1f, 1.0f, 0.04f, 2.2f, 0.04f);
        }

        static void BuildOffice(MeshBuilder mb, Spec s, List<(float y, float h)> levels, List<(Face face, bool isFront)> faces, float shaftTop, float top)
        {
            float baseTop = levels[1].y + levels[1].h, w = s.Width, d = s.Depth;
            foreach (var (f, isFront) in faces)
            {
                f.Box(M.limestone, f.Width * 0.5f, baseTop * 0.5f, 0.05f, f.Width, baseTop, 0.1f);
                int piers = Mathf.Max(2, Mathf.RoundToInt(f.Width / 3f));
                float pw = f.Width / piers;
                for (int p = 0; p < piers; p++)
                {
                    float a = (p + 0.5f) * pw;
                    f.Box(M.storeGlass, a, 0.6f + 3.6f, 0.12f, pw - 0.6f, 7.2f, 0.04f);
                    f.Box(M.frameDark, a, 4.5f, 0.15f, pw - 0.6f, 0.12f, 0.06f);
                }
                for (int p = 0; p <= piers; p++) f.Box(M.limestone, p * pw, baseTop * 0.5f, 0.2f, 0.5f, baseTop, 0.2f);
                if (isFront) f.Box(s.SignBand, f.Width * 0.5f, baseTop + 0.25f, 0.18f, f.Width, 0.5f, 0.15f);

                for (int li = 2; li < levels.Count - 2; li++)
                    f.Box(M.officeGlass, f.Width * 0.5f, levels[li].y + 1.95f, 0.04f, f.Width - 0.3f, 2.5f, 0.08f);
                for (float a = 0.75f; a < f.Width; a += 1.5f)
                    f.Box(M.aluminium, a, (baseTop + shaftTop) * 0.5f, 0.1f, 0.08f, shaftTop - baseTop, 0.12f);
                f.Box(M.aluminium, 0.1f, (baseTop + shaftTop) * 0.5f, 0.12f, 0.2f, shaftTop - baseTop, 0.2f);
                f.Box(M.aluminium, f.Width - 0.1f, (baseTop + shaftTop) * 0.5f, 0.12f, 0.2f, shaftTop - baseTop, 0.2f);
                f.Box(M.aluminium, f.Width * 0.5f, shaftTop - 0.15f, 0.14f, f.Width, 0.3f, 0.24f);
            }

            // Setback crown with its own glazing.
            const float inset = 2.5f;
            float cw = w - inset * 2f, cd = d - inset * 2f;
            mb.Box(M.spandrel, new Vector3(w * 0.5f, (shaftTop + top) * 0.5f, -d * 0.5f), new Vector3(cw, top - shaftTop, cd));
            mb.Box(M.roof, new Vector3(w * 0.5f, shaftTop + 0.02f, -d * 0.5f), new Vector3(w - 0.4f, 0.04f, d - 0.4f));
            var crownFaces = new[]
            {
                new Face(mb, new Vector3(inset, 0f, -inset), Vector3.forward, cw),
                new Face(mb, new Vector3(w - inset, 0f, -d + inset), Vector3.back, cw),
                new Face(mb, new Vector3(inset, 0f, -d + inset), Vector3.left, cd),
                new Face(mb, new Vector3(w - inset, 0f, -inset), Vector3.right, cd),
            };
            foreach (var f in crownFaces)
            {
                for (int li = levels.Count - 2; li < levels.Count; li++)
                    f.Box(M.officeGlass, f.Width * 0.5f, levels[li].y + 1.95f, 0.04f, f.Width - 0.3f, 2.5f, 0.08f);
                for (float a = 0.75f; a < f.Width; a += 1.5f)
                    f.Box(M.aluminium, a, (shaftTop + top) * 0.5f, 0.1f, 0.08f, top - shaftTop, 0.12f);
            }
            mb.Box(M.roof, new Vector3(w * 0.5f, top + 0.02f, -d * 0.5f), new Vector3(cw - 0.3f, 0.04f, cd - 0.3f));
            mb.Box(M.aluminium, new Vector3(w * 0.5f, shaftTop + 0.5f, -0.1f), new Vector3(w, 1f, 0.2f));
            mb.Box(M.aluminium, new Vector3(w * 0.5f, shaftTop + 0.5f, -d + 0.1f), new Vector3(w, 1f, 0.2f));
            mb.Box(M.aluminium, new Vector3(0.1f, shaftTop + 0.5f, -d * 0.5f), new Vector3(0.2f, 1f, d - 0.4f));
            mb.Box(M.aluminium, new Vector3(w - 0.1f, shaftTop + 0.5f, -d * 0.5f), new Vector3(0.2f, 1f, d - 0.4f));
        }
    }
}
