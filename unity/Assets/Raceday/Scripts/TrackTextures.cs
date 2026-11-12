using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The pixels of the paved surfaces' textures, made in code: tarmac grain with its normals, the broad patches laid
    /// over it, kerbs clean and worn, and the racing line along the lap. Every texture repeats seamlessly. Pixels are
    /// rows from the bottom, as <see cref="Texture2D.SetPixels(Color[])"/> takes them, x along u and y along v.
    /// </summary>
    public static class TrackTextures
    {
        /// <summary>Side of the tarmac tile in pixels: a 4 m tile at about 8 mm a pixel.</summary>
        public const int TarmacSize = 512;
        /// <summary>Side of the patch tile in pixels.</summary>
        public const int MacroSize = 256;
        /// <summary>Kerb tile in pixels: across its 1.5 m, and along one red and one white block.</summary>
        public const int KerbAcross = 128, KerbAlong = 256;

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
            // Stones from 8 to 30 mm across, each a low dome, the bigger ones fewer.
            int stones = s * s / 6;
            for (int n = 0; n < stones; n++)
            {
                float cx = (float)rng.NextDouble() * s, cy = (float)rng.NextDouble() * s;
                double pick = rng.NextDouble();
                float r = 0.5f + (float)(pick * pick) * 1.4f;
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
        /// Tarmac's colour, averaging the given colour: dark binder with the stones a few shades lighter. Gloss in
        /// alpha: the stones' worn tops a little shinier than the binder.
        /// </summary>
        public static Color[] TarmacColour(Tarmac t, Color average, float speckle)
        {
            var shade = new float[t.Height.Length];
            double sum = 0;
            for (int i = 0; i < shade.Length; i++)
            {
                float stone = t.Stone[i];
                shade[i] = (1f + t.Grain[i] * 0.05f) * (stone > 0f ? 1f + stone * speckle : 0.55f);
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
        /// repairs, wear. Green is streaky noise the rubbered line is drawn through.
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
                    px[y * s + x] = new Color(Mathf.Clamp01(0.5f + patch * 0.5f), Mathf.Clamp01(0.5f + streak * 0.6f), 0.5f, 1f);
                }
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
