using TarmacGauntlet.Rendering;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Materials for the pit complex, made in code like <see cref="CircuitMaterials"/>. The palette holds one texel
    /// a garage, painted in its team's colour once the field is known. The glass's lit windows and the garage lights
    /// glow on their own: no light is cast, so they cost nothing at night.
    /// </summary>
    public sealed class PitMaterials
    {
        public readonly Material Concrete, Glass, Steel, Palette, Fit, Metal, Rubber, Lamps;

        /// <summary>Colour of a garage with no car in it.</summary>
        public static readonly Color Empty = new Color(0.55f, 0.56f, 0.57f);
        /// <summary>Warm office light through the windows at night.</summary>
        private static readonly Color WindowGlow = new Color(1f, 0.82f, 0.58f);
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private readonly Texture2D palette;

        public PitMaterials()
        {
            Concrete = Make("Pit concrete", Noise(new Color(0.66f, 0.65f, 0.62f), 0.035f, 11), 0.12f);
            Glass = Make("Pit glass", null, 0.92f, new Color(0.07f, 0.09f, 0.11f), 0.2f);
            Glass.SetTexture("_EmissionMap", Windows());
            Glass.mainTextureScale = new Vector2(1f / PitComplexBuilder.WindowCells, 1f / PitComplexBuilder.StoreyCells);
            Glow(Glass, Color.black);
            Steel = Make("Green steel", null, 0.45f, new Color(0.16f, 0.30f, 0.22f), 0.3f);
            palette = new Texture2D(PitComplexBuilder.PaletteSize, 1, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapModeU = TextureWrapMode.Clamp,
                wrapModeV = TextureWrapMode.Clamp,
                name = "Team palette",
            };
            Palette = Make("Team colours", palette, 0.35f);
            Paint(new Color[0]);
            Fit = Make("Garage cabinets", null, 0.4f, new Color(0.19f, 0.2f, 0.22f), 0.2f);
            Metal = Make("Garage metal", null, 0.55f, new Color(0.62f, 0.63f, 0.65f), 0.8f);
            Rubber = Make("Tyre rubber", null, 0.2f, new Color(0.05f, 0.05f, 0.055f));
            Lamps = Make("Garage lights", null, 0.3f, Color.white);
            Glow(Lamps, new Color(1.1f, 1.1f, 1.05f));
        }

        /// <summary>Paints each garage in its team's colour, or plain grey where the colour is missing.</summary>
        public void Paint(Color[] garages)
        {
            var px = new Color[PitComplexBuilder.PaletteSize];
            for (int x = 0; x < px.Length; x++) px[x] = x < garages.Length ? garages[x] : Empty;
            palette.SetPixels(px);
            palette.Apply(false);
        }

        /// <summary>Lights the office windows: 0 by day, 1 at night.</summary>
        public void Night(float night) => Glow(Glass, WindowGlow * (1.4f * night));

        private static void Glow(Material m, Color c)
        {
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.SetColor(EmissionId, c);
        }

        private static Material Make(string name, Texture2D texture, float smoothness, Color? colour = null, float metallic = 0f)
        {
            var m = new Material(Lit.Shader) { name = name, enableInstancing = true };
            m.color = colour ?? Color.white;
            if (texture != null) m.mainTexture = texture;
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            return m;
        }

        /// <summary>
        /// Lit and dark panes, a cell per window and a row per storey, each pane framed dark so the mullions read
        /// between them. Some rooms are dimmer than others.
        /// </summary>
        private static Texture2D Windows()
        {
            const int cell = 16;
            int w = PitComplexBuilder.WindowCells * cell, h = PitComplexBuilder.StoreyCells * cell;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "Lit windows" };
            var rng = new System.Random(17);
            var px = new Color[w * h];
            for (int cx = 0; cx < PitComplexBuilder.WindowCells; cx++)
                for (int cy = 0; cy < PitComplexBuilder.StoreyCells; cy++)
                {
                    double roll = rng.NextDouble();
                    float lit = roll < 0.3 ? 0f : roll < 0.45 ? 0.35f : 0.75f + 0.25f * (float)rng.NextDouble();
                    for (int x = 0; x < cell; x++)
                        for (int y = 0; y < cell; y++)
                        {
                            bool frame = x < 1 || x >= cell - 1 || y < 3 || y >= cell - 2;
                            float v = frame ? 0f : lit;
                            px[(cy * cell + y) * w + cx * cell + x] = new Color(v, v, v, 1f);
                        }
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

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
    }
}
