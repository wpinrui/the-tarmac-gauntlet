using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The pixels of the paved surfaces' textures, made in code: tarmac grain with its normals, the broad patches laid
    /// over it, sealed cracks and patch repairs, rubber marks about the racing line, kerbs clean and worn, the racing line
    /// along the lap, where water pools on the road and raindrop ripples. Every texture repeats seamlessly. Pixels are
    /// rows from the bottom, as <see cref="Texture2D.SetPixels(Color[])"/> takes them, x along u and y along v.
    /// </summary>
    public static class TrackTextures
    {
        /// <summary>Side of the tarmac tile in pixels: a 4 m tile at about 4 mm a pixel.</summary>
        public const int TarmacSize = 1024;
        /// <summary>Side of the patch tile in pixels.</summary>
        public const int MacroSize = 256;
        /// <summary>Kerb tile in pixels: across its 1.5 m, and along one red and one white block.</summary>
        public const int KerbAcross = 128, KerbAlong = 256;
        /// <summary>Side of the raindrop ripple tile in pixels, and the metres it covers.</summary>
        public const int RippleSize = 256;
        public const float RippleMetres = 1.6f;
        /// <summary>
        /// Repairs tile in pixels, across and along the road, and the metres it covers: about 5 cm a pixel across and
        /// 3.5 cm along, so a sealed crack is a pixel or two wide whichever way it runs.
        /// </summary>
        public const int RepairsAcross = 256, RepairsAlong = 2048;
        public const float RepairsMetresAcross = 12f, RepairsMetresAlong = 72f;
        /// <summary>
        /// Rubber marks tile in pixels, across and along, and the metres it covers: across from 4 m left of the racing
        /// line to 4 m right of it, along a stretch of lap that repeats.
        /// </summary>
        public const int MarksAcross = 128, MarksAlong = 2048;
        public const float MarksMetresAcross = 8f, MarksMetresAlong = 160f;
        /// <summary>Raindrops on the ripple tile, a jittered grid of this many a side.</summary>
        private const int RippleCells = 22;

        /// <summary>
        /// How light the binder between the stones is beside the stones' 1 to 1 + speckle. Close to the stones, so that
        /// up close, where each texel shows, the road reads as a fine grain rather than dark speckle.
        /// </summary>
        public const float Binder = 0.88f;

        /// <summary>
        /// How steep the tarmac's normals stand its stones. Gentle: steeper, a low sun leaves each stone's far side
        /// black, and the road just ahead of the car turns to dark speckle.
        /// </summary>
        public const float TarmacBump = 0.65f;

        /// <summary>Tarmac's shape, shared by every tarmac: its height, and what lies at each pixel.</summary>
        public sealed class Tarmac
        {
            public readonly float[] Height = new float[TarmacSize * TarmacSize];
            /// <summary>How light the stone at a pixel is, 0 where binder shows between the stones.</summary>
            public readonly float[] Stone = new float[TarmacSize * TarmacSize];
            /// <summary>Fine grain over everything, -1 to 1.</summary>
            public readonly float[] Grain = new float[TarmacSize * TarmacSize];
        }

        /// <summary>Stones of aggregate packed in binder, over a gentle unevenness.</summary>
        public static Tarmac MakeTarmac(int seed)
        {
            const int s = TarmacSize;
            var t = new Tarmac();
            var rng = new System.Random(seed);
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    int i = y * s + x;
                    t.Height[i] = 0.25f * Fbm(x, y, s, 16, 4, seed);
                    t.Grain[i] = (float)(rng.NextDouble() * 2 - 1);
                }
            // Stones from 5 to 18 mm across, each a low dome, the bigger ones fewer: a fine racing surface.
            int stones = s * s / 6;
            for (int n = 0; n < stones; n++)
            {
                float cx = (float)rng.NextDouble() * s, cy = (float)rng.NextDouble() * s;
                double pick = rng.NextDouble();
                float r = 0.6f + (float)(pick * pick) * 1.6f;
                float top = 0.35f + (float)rng.NextDouble() * 0.5f;
                float light = 0.35f + (float)rng.NextDouble() * 0.65f;
                int reach = Mathf.CeilToInt(r);
                for (int dy = -reach; dy <= reach; dy++)
                    for (int dx = -reach; dx <= reach; dx++)
                    {
                        float fx = (int)cx + dx + 0.5f - cx, fy = (int)cy + dy + 0.5f - cy;
                        float q = (fx * fx + fy * fy) / (r * r);
                        if (q >= 1f) continue;
                        int i = Wrap((int)cy + dy, s) * s + Wrap((int)cx + dx, s);
                        float h = t.Height[i] * 0.3f + top * Mathf.Sqrt(1f - q);
                        if (h <= t.Height[i]) continue;
                        t.Height[i] = h;
                        t.Stone[i] = light;
                    }
            }
            return t;
        }

        /// <summary>
        /// Tarmac's colour, averaging the given colour: binder with the stones a shade or two lighter, close enough that
        /// the grain reads as a fine texture rather than a speckle. Gloss in alpha: the stones' worn tops a little shinier
        /// than the binder.
        /// </summary>
        public static Color[] TarmacColour(Tarmac t, Color average, float speckle)
        {
            var shade = new float[t.Height.Length];
            double sum = 0;
            for (int i = 0; i < shade.Length; i++)
            {
                float stone = t.Stone[i];
                shade[i] = (1f + t.Grain[i] * 0.04f) * (stone > 0f ? 1f + stone * speckle : Binder);
                sum += shade[i];
            }
            float mean = (float)(sum / shade.Length);
            var px = new Color[shade.Length];
            for (int i = 0; i < px.Length; i++)
            {
                float k = shade[i] / mean, stone = t.Stone[i];
                float gloss = stone > 0f ? 0.5f + stone * 0.35f : 0.35f;
                px[i] = new Color(Mathf.Clamp01(average.r * k), Mathf.Clamp01(average.g * k), Mathf.Clamp01(average.b * k), gloss);
            }
            return px;
        }

        /// <summary>Tangent-space normals of tarmac, 0 to 1 per channel, from its height.</summary>
        public static Color[] TarmacNormals(Tarmac t, float strength) => Normals(t.Height, TarmacSize, TarmacSize, strength);

        /// <summary>
        /// Broad patches in red, about 0.5 with soft swings either way: lighter and darker laying of the tarmac, old
        /// repairs, wear. Green is streaky noise the rubbered line is drawn through. Blue is where water stands first.
        /// Alpha is polish, about 0.5: how worn smooth the surface is, read at a much larger scale than the rest so the
        /// road's sheen swells and fades over hundreds of metres.
        /// </summary>
        public static Color[] Macro(int seed)
        {
            const int s = MacroSize;
            var px = new Color[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float broad = Fbm(x, y, s, 4, 4, seed);
                    // A few sharp-edged patches, as where the surface was relaid.
                    float relaid = Fbm(x, y, s, 6, 2, seed + 11);
                    float patch = broad * 0.7f + (relaid > 0.35f ? 0.12f : relaid < -0.4f ? -0.1f : 0f);
                    float streak = Fbm(x, y, s, 32, 3, seed + 23);
                    // Where water stands first in a hollow: shallow dips in the surface, a few to the tile.
                    float dips = Fbm(x, y, s, 5, 3, seed + 37);
                    float polish = Fbm(x, y, s, 3, 3, seed + 51);
                    px[y * s + x] = new Color(Mathf.Clamp01(0.5f + patch * 0.5f), Mathf.Clamp01(0.5f + streak * 0.6f), Mathf.Clamp01(0.5f + dips * 0.9f),
                        Mathf.Clamp01(0.5f + polish * 0.8f));
                }
            return px;
        }

        /// <summary>
        /// Repairs to the road, laid along it as the road's uv runs: x across, y along. Red is bitumen sealant where cracks
        /// were sealed, long cracks along the road, short ones across it with a few branches, and the joints round some
        /// patches. Green is 1 inside a rectangle of relaid tarmac, and blue that patch's shade, under 0.5 newer and darker,
        /// above it older and paler. Nothing lies within a metre of the tile's ends, so a tile may be mirrored or shifted
        /// against the next without a seam; across, the tile repeats.
        /// </summary>
        public static Color[] Repairs(int seed)
        {
            const int w = RepairsAcross, h = RepairsAlong;
            const float mx = RepairsMetresAcross / w, my = RepairsMetresAlong / h, end = 1f;
            var seal = new float[w * h];
            var patch = new float[w * h];
            var shade = new float[w * h];
            for (int i = 0; i < shade.Length; i++) shade[i] = 0.5f;
            var rng = new System.Random(seed);
            float Next() => (float)rng.NextDouble();

            // A band of sealant centred at (u, v) metres, its half width in metres, soft for a pixel at its edge.
            void Stamp(float u, float v, float half)
            {
                int x0 = Mathf.FloorToInt((u - half - mx) / mx), x1 = Mathf.CeilToInt((u + half + mx) / mx);
                int y0 = Mathf.Max(0, Mathf.FloorToInt((v - half - my) / my)), y1 = Mathf.Min(h - 1, Mathf.CeilToInt((v + half + my) / my));
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float dx = (x + 0.5f) * mx - u, dy = (y + 0.5f) * my - v;
                        float c = Mathf.Clamp01((half - Mathf.Sqrt(dx * dx + dy * dy)) / 0.03f + 0.5f);
                        int i = y * w + Wrap(x, w);
                        if (c > seal[i]) seal[i] = c;
                    }
            }

            // A crack sealed along its length: a walk from (u, v) heading off at the angle given (0 along the road),
            // wandering as it goes and pulled back towards its heading. It stops short of the tile's ends.
            void Crack(float u, float v, float heading, float length, float wander, float half, int branches)
            {
                float angle = heading;
                const float step = 0.03f;
                int steps = Mathf.CeilToInt(length / step);
                var at = new System.Collections.Generic.List<Vector2>(steps);
                for (int k = 0; k < steps; k++)
                {
                    if (v < end || v > RepairsMetresAlong - end) break;
                    Stamp(u, v, half);
                    at.Add(new Vector2(u, v));
                    angle += (Next() * 2f - 1f) * wander - (angle - heading) * 0.04f;
                    u += Mathf.Sin(angle) * step;
                    v += Mathf.Cos(angle) * step;
                }
                for (int b = 0; b < branches && at.Count > 10; b++)
                {
                    Vector2 from = at[rng.Next(at.Count)];
                    float side = Next() < 0.5f ? -1f : 1f;
                    Crack(from.x, from.y, heading + side * (0.5f + Next() * 0.7f), 0.4f + Next() * 1.6f, wander * 1.5f, half * 0.8f, 0);
                }
            }

            // Rectangles of relaid tarmac, a few to the tile, some with their joints sealed round.
            int patches = 4 + rng.Next(3);
            for (int p = 0; p < patches; p++)
            {
                float pw = 1.2f + Next() * 3.5f, pl = 1.5f + Mathf.Pow(Next(), 1.5f) * 10f;
                float pu = Next() * RepairsMetresAcross, pv = end + 0.3f + Next() * (RepairsMetresAlong - 2f * end - 0.6f - pl);
                float tone = 0.15f + Next() * 0.6f;
                int x0 = Mathf.RoundToInt(pu / mx), x1 = Mathf.RoundToInt((pu + pw) / mx);
                int y0 = Mathf.RoundToInt(pv / my), y1 = Mathf.RoundToInt((pv + pl) / my);
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        int i = y * w + Wrap(x, w);
                        patch[i] = 1f;
                        shade[i] = tone;
                    }
                if (Next() < 0.65f)
                {
                    const float half = 0.035f;
                    for (float a = 0f; a <= pw; a += 0.03f) { Stamp(pu + a, pv, half); Stamp(pu + a, pv + pl, half); }
                    for (float a = 0f; a <= pl; a += 0.03f) { Stamp(pu, pv + a, half); Stamp(pu + pw, pv + a, half); }
                }
            }
            // Long cracks along the road, as where the surface was laid in strips; short ones across it.
            for (int c = 0; c < 3; c++)
                Crack(Next() * RepairsMetresAcross, end + Next() * 30f, 0f, 8f + Next() * 32f, 0.05f, 0.03f + Next() * 0.02f, 2);
            for (int c = 0; c < 9; c++)
            {
                float side = Next() < 0.5f ? -1f : 1f;
                Crack(Next() * RepairsMetresAcross, end + 0.5f + Next() * (RepairsMetresAlong - 2f * end - 1f), side * (Mathf.PI * 0.5f + (Next() - 0.5f) * 0.5f),
                    0.8f + Next() * 4f, 0.08f, 0.025f + Next() * 0.02f, rng.Next(2));
            }

            var px = new Color[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(seal[i], patch[i], shade[i], 1f);
            return px;
        }

        /// <summary>
        /// Rubber marks either side of the racing line, laid along it: x across from 4 m left of the line to 4 m right of
        /// it, y along the lap, the darkness of the rubber in red. Pairs of streaks a car's track apart where tyres locked
        /// under braking or spun up out of a corner, single ones where one slid, each starting sharp and fading out, some
        /// drifting off the line or weaving. Repeats along; across, it fades out before its edges.
        /// </summary>
        public static Color[] Marks(int seed)
        {
            const int w = MarksAcross, h = MarksAlong;
            const float mx = MarksMetresAcross / w, my = MarksMetresAlong / h, half = MarksMetresAcross / 2f;
            var dark = new float[w * h];
            var rng = new System.Random(seed);
            float Next() => (float)rng.NextDouble();
            const int marks = 90;
            for (int n = 0; n < marks; n++)
            {
                // Mostly near the line, a few well off it.
                float centre = Mathf.Clamp(((Next() + Next() + Next()) / 1.5f - 1f) * 2.2f, -2.4f, 2.4f);
                float start = Next() * MarksMetresAlong;
                float length = 4f + Next() * Next() * 40f;
                float drift = (Next() * 2f - 1f) * 1.1f;
                bool weaves = Next() < 0.25f;
                float wobble = weaves ? 0.15f + Next() * 0.25f : 0.03f, wave = 6f + Next() * 14f, phase = Next() * 6.28f;
                float strength = 0.25f + Next() * 0.6f;
                float sigma = 0.07f + Next() * 0.06f;
                bool pair = Next() < 0.75f;
                int ys = Mathf.CeilToInt(length / my);
                int y0 = Mathf.FloorToInt(start / my);
                for (int j = 0; j < ys; j++)
                {
                    float t = j / (float)ys;
                    float envelope = Mathf.Clamp01(t / 0.04f) * Mathf.Pow(1f - t, 0.8f) * (0.75f + 0.25f * Fbm(0, y0 + j, w, h, 1, 64, 2, seed + n));
                    float lateral = centre + drift * t + wobble * Mathf.Sin(t * length / wave * 6.28f + phase);
                    int y = Wrap(y0 + j, h);
                    for (int tyre = 0; tyre < (pair ? 2 : 1); tyre++)
                    {
                        float at = pair ? lateral + (tyre == 0 ? -0.8f : 0.8f) : lateral;
                        int x0 = Mathf.Max(0, Mathf.FloorToInt((at + half - 3f * sigma) / mx)), x1 = Mathf.Min(w - 1, Mathf.CeilToInt((at + half + 3f * sigma) / mx));
                        for (int x = x0; x <= x1; x++)
                        {
                            float across = (x + 0.5f) * mx - half;
                            float d = across - at;
                            float edge = 1f - Mathf.Clamp01((Mathf.Abs(across) - 3f) / 0.8f);
                            float c = strength * envelope * edge * Mathf.Exp(-d * d / (2f * sigma * sigma));
                            int i = y * w + x;
                            dark[i] = 1f - (1f - dark[i]) * (1f - c);
                        }
                    }
                }
            }
            var px = new Color[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = new Color(dark[i], 0f, 0f, 1f);
            return px;
        }

        /// <summary>A kerb's height: fine paint grain, with chips where the paint has come off sunk into it.</summary>
        public static float[] KerbHeight(bool worn, int seed)
        {
            var h = new float[KerbAcross * KerbAlong];
            var rng = new System.Random(seed);
            for (int y = 0; y < KerbAlong; y++)
                for (int x = 0; x < KerbAcross; x++)
                {
                    float g = (float)(rng.NextDouble() * 2 - 1) * 0.08f + Fbm(x, y, KerbAcross, KerbAlong, 16, 32, 3, seed) * 0.2f;
                    if (worn && Chipped(x, y, seed)) g -= 0.6f;
                    h[y * KerbAcross + x] = g;
                }
            return h;
        }

        /// <summary>
        /// A kerb: red and white blocks along it, u 0 at the road's edge. Worn, rubber is smeared over its road side
        /// in streaks along it, the paint has chipped back to concrete, and dust from the verge lies on its far side.
        /// Gloss in alpha.
        /// </summary>
        public static Color[] Kerb(bool worn, int seed)
        {
            var red = new Color(0.78f, 0.1f, 0.1f);
            var white = new Color(0.93f, 0.93f, 0.91f);
            var concrete = new Color(0.52f, 0.5f, 0.47f);
            var rubber = new Color(0.07f, 0.07f, 0.075f);
            var dust = new Color(0.55f, 0.5f, 0.4f);
            var rng = new System.Random(seed);
            var px = new Color[KerbAcross * KerbAlong];
            for (int y = 0; y < KerbAlong; y++)
                for (int x = 0; x < KerbAcross; x++)
                {
                    float u = (x + 0.5f) / KerbAcross;
                    Color c = y < KerbAlong / 2 ? red : white;
                    float gloss = 0.6f;
                    float grain = 1f + (float)(rng.NextDouble() * 2 - 1) * 0.03f;
                    if (worn)
                    {
                        // Paint fades towards the road, where the tyres run.
                        c = Color.Lerp(c, concrete, 0.18f * Mathf.Exp(-u / 0.5f));
                        if (Chipped(x, y, seed)) { c = concrete; gloss = 0.2f; }
                        float streaks = 0.5f + 0.5f * Fbm(x, y, KerbAcross, KerbAlong, 32, 2, 3, seed + 5);
                        float smear = Mathf.Clamp01(Mathf.Exp(-u / 0.22f) * (0.35f + streaks * 0.9f) - 0.08f);
                        c = Color.Lerp(c, rubber, smear * 0.85f);
                        gloss = Mathf.Lerp(gloss, 0.45f, smear);
                        float d = Mathf.Clamp01((u - 0.6f) / 0.4f) * (0.5f + 0.5f * Fbm(x, y, KerbAcross, KerbAlong, 8, 16, 3, seed + 9));
                        c = Color.Lerp(c, dust, d * 0.35f);
                        gloss *= 1f - d * 0.5f;
                    }
                    px[y * KerbAcross + x] = new Color(Mathf.Clamp01(c.r * grain), Mathf.Clamp01(c.g * grain), Mathf.Clamp01(c.b * grain), gloss);
                }
            return px;
        }

        /// <summary>Tangent-space normals of a kerb.</summary>
        public static Color[] KerbNormals(bool worn, int seed) => Normals(KerbHeight(worn, seed), KerbAcross, KerbAlong, 1.2f);

        /// <summary>Where a worn kerb's paint has chipped off: patchy, and mostly on its road side.</summary>
        private static bool Chipped(int x, int y, int seed)
        {
            float u = (x + 0.5f) / KerbAcross;
            float n = Fbm(x, y, KerbAcross, KerbAlong, 16, 32, 4, seed + 3);
            return n > 0.32f + u * 0.35f;
        }

        /// <summary>
        /// The racing line along the lap, the road cut into <paramref name="texels"/> equal lengths, each texel the middle of its length: red is how
        /// far the line runs from the road's left edge, m; green how much rubber lies on it, 0 to 1. Rubber gathers
        /// in the corners and in the braking zones before them, and lies thinner along the straights.
        /// </summary>
        /// <param name="along">Distance along the road at each track sample and once more back at the start, m.</param>
        /// <param name="fromLeft">The line's distance from the left edge at each track sample, m.</param>
        /// <param name="curvature">The line's curvature at each track sample, 1/m.</param>
        public static Color[] Line(float[] along, float[] fromLeft, float[] curvature, int texels)
        {
            int n = fromLeft.Length;
            float total = along[n];
            // Rubber per sample: how hard the corner is, smoothed, reaching back over the braking zone.
            var corner = new float[n];
            for (int k = 0; k < n; k++) corner[k] = Mathf.Clamp01(Mathf.Abs(curvature[k]) * 45f);
            var rubber = new float[n];
            float step = total / n;
            int back = Mathf.Max(1, Mathf.RoundToInt(110f / step)), ahead = Mathf.Max(1, Mathf.RoundToInt(15f / step));
            for (int k = 0; k < n; k++)
            {
                float most = 0f;
                for (int j = -ahead; j <= back; j++)
                {
                    float fall = j <= 0 ? 1f + j / (float)(ahead + 1) : 1f - 0.45f * j / back;
                    most = Mathf.Max(most, corner[((k + j) % n + n) % n] * fall);
                }
                rubber[k] = 0.4f + 0.6f * most;
            }
            var px = new Color[texels];
            int seg = 0;
            for (int j = 0; j < texels; j++)
            {
                float d = total * (j + 0.5f) / texels;
                while (seg < n - 1 && along[seg + 1] <= d) seg++;
                float span = along[seg + 1] - along[seg];
                float t = span > 1e-4f ? Mathf.Clamp01((d - along[seg]) / span) : 0f;
                int b = (seg + 1) % n;
                px[j] = new Color(Mathf.Lerp(fromLeft[seg], fromLeft[b], t), Mathf.Lerp(rubber[seg], rubber[b], t), 0f, 1f);
            }
            return px;
        }

        /// <summary>
        /// Raindrop rings on a tile, read by the track shader to ripple standing water. Each pixel inside a drop's ring
        /// holds how near the drop's centre it is in r (1 at the centre, 0 at the ring's edge), the direction out from the
        /// centre in g and b (0.5 for none), and the drop's own moment in its cycle in a, so the drops fall out of step.
        /// Pixels outside every drop are (0, 0.5, 0.5, 0). Repeats seamlessly.
        /// </summary>
        public static Color[] Ripples(int seed)
        {
            const int s = RippleSize, cells = RippleCells;
            float cell = s / (float)cells;
            var cx = new float[cells * cells];
            var cy = new float[cells * cells];
            var radius = new float[cells * cells];
            var phase = new float[cells * cells];
            var rng = new System.Random(seed);
            for (int i = 0; i < cells * cells; i++)
            {
                cx[i] = ((i % cells) + 0.15f + 0.7f * (float)rng.NextDouble()) * cell;
                cy[i] = ((i / cells) + 0.15f + 0.7f * (float)rng.NextDouble()) * cell;
                radius[i] = cell * (0.55f + 0.5f * (float)rng.NextDouble());
                phase[i] = (float)rng.NextDouble();
            }
            var px = new Color[s * s];
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float px0 = x + 0.5f, py0 = y + 0.5f;
                    int gx = (int)(px0 / cell), gy = (int)(py0 / cell);
                    Color best = new Color(0f, 0.5f, 0.5f, 0f);
                    float nearest = 0f;
                    for (int j = -1; j <= 1; j++)
                        for (int i = -1; i <= 1; i++)
                        {
                            int ix = Wrap(gx + i, cells), iy = Wrap(gy + j, cells), k = iy * cells + ix;
                            // The drop's centre, moved by a whole tile where the neighbour wrapped round.
                            float dx = px0 - (cx[k] + (gx + i - ix) * cell), dy = py0 - (cy[k] + (gy + j - iy) * cell);
                            float d = Mathf.Sqrt(dx * dx + dy * dy);
                            float near = 1f - d / radius[k];
                            if (near <= nearest) continue;
                            nearest = near;
                            float inv = d > 1e-4f ? 1f / d : 0f;
                            best = new Color(near, 0.5f + 0.5f * dx * inv, 0.5f + 0.5f * dy * inv, phase[k]);
                        }
                    px[y * s + x] = best;
                }
            return px;
        }

        /// <summary>
        /// Where water stands on the road along the lap, one texel a metre or so, read by the track shader beside the
        /// racing line map: in r how ready the road is to hold a puddle, high in a dip it can't drain from and low on a
        /// slope; in g which edge the camber drains to, 1 the right, -1 the left, 0 both; in b the road's width, m.
        /// </summary>
        /// <param name="along">Distance along the road at each sample and once more at the end, as <see cref="CircuitBuilder.RoadAlong"/>.</param>
        /// <param name="height">The road's height at its middle at each sample, m.</param>
        /// <param name="crossfall">Its rise to the left per metre across at each sample.</param>
        /// <param name="width">Its width at each sample, m.</param>
        public static Color[] Pools(float[] along, float[] height, float[] crossfall, float[] width, int texels)
        {
            int n = height.Length;
            float total = along[n];
            float step = total / n;
            // A dip: lower than the highest road both ahead and behind within a reach, water can't run out of it.
            int reach = Mathf.Max(1, Mathf.RoundToInt(80f / step)), near = Mathf.Max(1, Mathf.RoundToInt(6f / step));
            var pool = new float[n];
            for (int k = 0; k < n; k++)
            {
                float behind = height[k], ahead = height[k];
                for (int j = 1; j <= reach; j++)
                {
                    behind = Mathf.Max(behind, height[((k - j) % n + n) % n]);
                    ahead = Mathf.Max(ahead, height[(k + j) % n]);
                }
                float dip = Mathf.Clamp01((Mathf.Min(behind, ahead) - height[k]) / 0.3f);
                float grade = Mathf.Abs(height[(k + near) % n] - height[((k - near) % n + n) % n]) / (2f * near * step);
                float level = Mathf.Clamp01(1f - grade / 0.015f);
                pool[k] = Mathf.Clamp01(dip * 0.9f + level * 0.4f);
            }
            var px = new Color[texels];
            int seg = 0;
            for (int j = 0; j < texels; j++)
            {
                float d = total * (j + 0.5f) / texels;
                while (seg < n - 1 && along[seg + 1] <= d) seg++;
                float span = along[seg + 1] - along[seg];
                float t = span > 1e-4f ? Mathf.Clamp01((d - along[seg]) / span) : 0f;
                int b = (seg + 1) % n;
                float cross = Mathf.Lerp(crossfall[seg], crossfall[b], t);
                px[j] = new Color(Mathf.Lerp(pool[seg], pool[b], t), Mathf.Clamp(cross / 0.012f, -1f, 1f), Mathf.Lerp(width[seg], width[b], t), 1f);
            }
            return px;
        }

        // ---------------------------------------------------------------- noise

        private static int Wrap(int v, int size) => ((v % size) + size) % size;

        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xffffff) / (float)0xffffff * 2f - 1f;
            }
        }

        /// <summary>Smooth value noise over a square tile that repeats every <paramref name="size"/> pixels.</summary>
        private static float Fbm(int x, int y, int size, int cells, int octaves, int seed) => Fbm(x, y, size, size, cells, cells, octaves, seed);

        /// <summary>
        /// Smooth value noise, -1 to 1 or so, over a tile of w by h pixels with cx by cy cells at the coarsest
        /// octave, each finer octave twice the cells at half the weight. Repeats with the tile.
        /// </summary>
        private static float Fbm(int x, int y, int w, int h, int cx, int cy, int octaves, int seed)
        {
            float sum = 0f, weight = 0.5f, norm = 0f;
            for (int o = 0; o < octaves; o++)
            {
                float fx = (x + 0.5f) * cx / w, fy = (y + 0.5f) * cy / h;
                int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
                float tx = Smooth(fx - x0), ty = Smooth(fy - y0);
                int s = seed * 31 + o;
                float a = Hash(Wrap(x0, cx), Wrap(y0, cy), s), b = Hash(Wrap(x0 + 1, cx), Wrap(y0, cy), s);
                float c = Hash(Wrap(x0, cx), Wrap(y0 + 1, cy), s), d = Hash(Wrap(x0 + 1, cx), Wrap(y0 + 1, cy), s);
                sum += weight * Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
                norm += weight;
                weight *= 0.5f;
                cx *= 2;
                cy *= 2;
            }
            return sum / norm;
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);

        /// <summary>Tangent-space normals from a height field that wraps, a height of 1 standing a pixel tall times the strength.</summary>
        private static Color[] Normals(float[] height, int w, int h, float strength)
        {
            var px = new Color[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float dx = height[y * w + Wrap(x + 1, w)] - height[y * w + Wrap(x - 1, w)];
                    float dy = height[Wrap(y + 1, h) * w + x] - height[Wrap(y - 1, h) * w + x];
                    var n = new Vector3(-dx * strength * 0.5f, -dy * strength * 0.5f, 1f).normalized;
                    px[y * w + x] = new Color(n.x * 0.5f + 0.5f, n.y * 0.5f + 0.5f, n.z * 0.5f + 0.5f, 1f);
                }
            return px;
        }
    }
}
