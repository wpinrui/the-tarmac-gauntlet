using TarmacGauntlet.Rendering;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>Materials for the circuit, made in code so the scene needs no asset files.</summary>
    public sealed class CircuitMaterials
    {
        public readonly Material Asphalt, PitAsphalt, White, Yellow, Kerb, Verge, Grass, Gravel,
            Armco, Tyres, Concrete, Hatch, Terrain, GarageInside, Bollard;

        public CircuitMaterials()
        {
            Asphalt = Make("Asphalt", Noise(new Color(0.17f, 0.17f, 0.18f), 0.05f, 1), 0.25f);
            PitAsphalt = Make("Pit asphalt", Noise(new Color(0.21f, 0.21f, 0.22f), 0.04f, 2), 0.2f);
            White = Make("White paint", null, 0.35f, new Color(0.92f, 0.92f, 0.9f));
            Yellow = Make("Yellow paint", null, 0.35f, new Color(0.95f, 0.78f, 0.1f));
            Kerb = Make("Kerb", Stripes(new Color(0.78f, 0.1f, 0.1f), new Color(0.93f, 0.93f, 0.91f), false), 0.4f);
            Verge = Make("Verge", Noise(new Color(0.24f, 0.36f, 0.16f), 0.06f, 3), 0.05f);
            Grass = Make("Grass", Noise(new Color(0.23f, 0.41f, 0.15f), 0.08f, 4), 0.05f);
            Gravel = Make("Gravel", Noise(new Color(0.63f, 0.58f, 0.48f), 0.12f, 5), 0.1f);
            Armco = Make("Armco", null, 0.6f, new Color(0.72f, 0.74f, 0.76f), 0.7f);
            Tyres = Make("Tyre wall", null, 0.3f, new Color(0.06f, 0.06f, 0.07f));
            Concrete = Make("Concrete", Noise(new Color(0.74f, 0.73f, 0.7f), 0.03f, 6), 0.15f);
            Hatch = Make("Red and white hatching", Stripes(new Color(0.8f, 0.1f, 0.1f), new Color(0.93f, 0.93f, 0.91f), true), 0.35f);
            Terrain = Make("Terrain", Noise(new Color(0.2f, 0.35f, 0.13f), 0.1f, 7), 0.05f);
            GarageInside = Make("Garage", null, 0.2f, new Color(0.32f, 0.33f, 0.35f));
            Bollard = Make("Bollard", Stripes(new Color(0.95f, 0.78f, 0.1f), new Color(0.08f, 0.08f, 0.08f), false), 0.3f);
        }

        private static Material Make(string name, Texture2D texture, float smoothness, Color? colour = null, float metallic = 0f)
        {
            var m = new Material(Lit.Shader) { name = name };
            m.color = colour ?? Color.white;
            if (texture != null) m.mainTexture = texture;
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            return m;
        }

        /// <summary>A tileable grainy texture, one tile per metre-ish surface patch.</summary>
        private static Texture2D Noise(Color baseColour, float amount, int seed)
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
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

        /// <summary>Two-colour stripes, one pair per texture tile; diagonal for hatching.</summary>
        private static Texture2D Stripes(Color a, Color b, bool diagonal)
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 8 };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int t = diagonal ? (x + y) % size : y;
                    px[y * size + x] = t < size / 2 ? a : b;
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }
    }
}
