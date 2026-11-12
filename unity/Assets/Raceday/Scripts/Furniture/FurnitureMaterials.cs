using TarmacGauntlet.Rendering;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Materials for the trackside furniture, made in code like the track's. The lights and panels glow by
    /// their own emission, so the start lights and flags read at night without any real light.
    /// </summary>
    public sealed class FurnitureMaterials
    {
        public readonly Material GreenSteel, Concrete, WhitePaint, BlackSteel, Roof, Galvanised, BoardBack, Boards;
        public readonly Material RedOn, RedOff, GreenOn, GreenOff;
        /// <summary>Flag panel faces by what they show, indexed by <see cref="RaceFlags.Panel"/>.</summary>
        public readonly Material[] Panels;

        private const int Leds = 24;          // a panel is this many LEDs square
        private const int LedPixels = 10;     // texture pixels per LED

        public FurnitureMaterials()
        {
            GreenSteel = Make("Green steel", Grain(new Color(0.2f, 0.3f, 0.24f), 0.04f, 11), 0.35f, 0.4f);
            Concrete = Make("Post concrete", Grain(new Color(0.66f, 0.65f, 0.62f), 0.05f, 12), 0.1f);
            WhitePaint = Make("Gantry white", Grain(new Color(0.86f, 0.86f, 0.84f), 0.025f, 13), 0.45f);
            BlackSteel = Make("Black steel", null, 0.4f, 0.3f, new Color(0.05f, 0.05f, 0.055f));
            Roof = Make("Corrugated roof", Ribs(new Color(0.55f, 0.57f, 0.58f)), 0.5f, 0.6f);
            Galvanised = Make("Galvanised steel", null, 0.55f, 0.8f, new Color(0.62f, 0.64f, 0.66f));
            BoardBack = Make("Board back", null, 0.2f, 0f, new Color(0.3f, 0.31f, 0.32f));
            Boards = Make("Distance boards", Resources.Load<Texture2D>("Furniture/boards"), 0.25f);

            Texture2D lens = Lens();
            RedOn = Glow("Start light red", lens, new Color(1f, 0.08f, 0.04f), 4f);
            RedOff = Glow("Start light red off", lens, new Color(0.16f, 0.02f, 0.02f), 0f);
            GreenOn = Glow("Start light green", lens, new Color(0.1f, 1f, 0.25f), 4f);
            GreenOff = Glow("Start light green off", lens, new Color(0.02f, 0.12f, 0.04f), 0f);

            Panels = new Material[4];
            Panels[(int)RaceFlags.Panel.Off] = Glow("Panel off", Matrix(new Color(0.09f, 0.09f, 0.09f), Full), Color.white, 0f);
            Panels[(int)RaceFlags.Panel.Green] = Glow("Panel green", Matrix(new Color(0.15f, 1f, 0.3f), Full), Color.white, 2.5f);
            Panels[(int)RaceFlags.Panel.Yellow] = Glow("Panel yellow", Matrix(new Color(1f, 0.8f, 0.05f), Full), Color.white, 2.5f);
            Panels[(int)RaceFlags.Panel.SafetyCar] = Glow("Panel SC", Matrix(new Color(1f, 0.8f, 0.05f), SafetyCar), Color.white, 2.5f);
            foreach (Material p in Panels) p.enableInstancing = true;
        }

        private static Material Make(string name, Texture2D texture, float smoothness, float metallic = 0f, Color? colour = null)
        {
            var m = new Material(Lit.Shader) { name = name, enableInstancing = true };
            m.color = colour ?? Color.white;
            if (texture != null) m.mainTexture = texture;
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            return m;
        }

        /// <summary>A material lit by its own texture: black where the texture is black, glowing where it is lit.</summary>
        private static Material Glow(string name, Texture2D texture, Color tint, float strength)
        {
            Material m = Make(name, texture, 0.7f, 0f, tint);
            if (strength > 0f)
            {
                m.EnableKeyword("_EMISSION");
                m.SetTexture("_EmissionMap", texture);
                m.SetColor("_EmissionColor", tint * strength);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            return m;
        }

        /// <summary>A fine grain over a base colour, for painted and cast surfaces.</summary>
        private static Texture2D Grain(Color baseColour, float amount, int seed)
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
            var rng = new System.Random(seed);
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++)
            {
                float v = (float)(rng.NextDouble() - 0.5) * 2f * amount;
                px[i] = new Color(Mathf.Clamp01(baseColour.r + v), Mathf.Clamp01(baseColour.g + v), Mathf.Clamp01(baseColour.b + v), 1);
            }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>Corrugated sheet: light and dark ribs, about ten per metre of roof.</summary>
        private static Texture2D Ribs(Color baseColour)
        {
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float shade = 0.82f + 0.18f * Mathf.Cos(x * 10f * 2f * Mathf.PI / size);
                    px[y * size + x] = baseColour * shade;
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>A round lens: bright in the middle, a darker rim, black outside the disc.</summary>
        private static Texture2D Lens()
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float r = Mathf.Sqrt((x + 0.5f - size / 2f) * (x + 0.5f - size / 2f) + (y + 0.5f - size / 2f) * (y + 0.5f - size / 2f)) / (size / 2f);
                    float v = r > 0.96f ? 0f : 1f - 0.45f * r * r;
                    px[y * size + x] = new Color(v, v, v, 1);
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        private static bool Full(int x, int y) => x > 0 && y > 0 && x < Leds - 1 && y < Leds - 1;

        /// <summary>The letters SC across the panel, each glyph pixel two LEDs square.</summary>
        private static bool SafetyCar(int x, int y)
        {
            string[] rows =
            {
                ".###...###.",
                "#.....#....",
                ".##...#....",
                "...#..#....",
                "###....###.",
            };
            if (y < 7 || y > 16 || x < 1) return false;
            int row = (16 - y) / 2, col = (x - 1) / 2;
            if (row < 0 || row >= rows.Length || col >= rows[0].Length) return false;
            return rows[row][col] == '#';
        }

        /// <summary>A grid of round LEDs, lit in a colour where <paramref name="lit"/> says, dim grey elsewhere.</summary>
        private static Texture2D Matrix(Color on, System.Func<int, int, bool> lit)
        {
            const int size = Leds * LedPixels;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var px = new Color[size * size];
            Color off = new Color(0.07f, 0.07f, 0.07f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int lx = x / LedPixels, ly = y / LedPixels;
                    float dx = x % LedPixels + 0.5f - LedPixels / 2f, dy = y % LedPixels + 0.5f - LedPixels / 2f;
                    bool dot = dx * dx + dy * dy < LedPixels * LedPixels * 0.16f;
                    px[y * size + x] = !dot ? Color.black : lit(lx, ly) ? on : off;
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }
    }
}
