using TarmacGauntlet.Rendering;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>Materials for the grandstands, their crowd and the catch fencing, made in code.</summary>
    public sealed class StandMaterials
    {
        public readonly Material Concrete, Steel, Cladding, Seats, Roof, Bank, Crowd, FenceMesh, FencePosts;

        public StandMaterials(StandColours colours)
        {
            (Color steel, Color seats, Color cladding) = Paint(colours);
            Concrete = Make("Stand concrete", Noise(new Color(0.68f, 0.67f, 0.64f), 0.05f, 11), 0.1f);
            Steel = Make("Stand steel", null, 0.35f, steel);
            Cladding = Make("Stand cladding", Noise(cladding, 0.025f, 12), 0.2f);
            Seats = Make("Stand seats", Noise(seats, 0.04f, 13), 0.3f);
            Roof = Make("Stand roof", Noise(new Color(0.36f, 0.37f, 0.38f), 0.03f, 14), 0.25f);
            Bank = Make("Stand bank", Noise(new Color(0.23f, 0.41f, 0.15f), 0.08f, 4), 0.05f);
            Crowd = CrowdMaterial();
            FencePosts = Make("Catch fence posts", null, 0.5f, new Color(0.6f, 0.62f, 0.63f), 0.6f);
            FenceMesh = Make("Catch fence", Mesh(), 0.4f, new Color(0.62f, 0.64f, 0.64f), 0.5f);
            FenceMesh.mainTextureScale = new Vector2(4f, 4f);
            Lit.Cutout(FenceMesh);
        }

        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor"), GlowId = Shader.PropertyToID("_Glow");

        /// <summary>
        /// Lights the seats and the crowd under the roofs at night, as the roofs' floodlights would: each glows in its own
        /// colours, tinted by the floodlights'. 0 by day, 1 at night.
        /// </summary>
        public void Night(float night, Color tint)
        {
            Crowd.SetColor(GlowId, tint * (NightLook.StandGlow * night));
            if (!Seats.IsKeywordEnabled("_EMISSION"))
            {
                Seats.EnableKeyword("_EMISSION");
                Seats.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                Seats.SetTexture("_EmissionMap", Seats.mainTexture);
            }
            Seats.SetColor(EmissionId, tint * (NightLook.StandGlow * night));
        }

        /// <summary>Steel, seat and cladding colours for each look.</summary>
        public static (Color steel, Color seats, Color cladding) Paint(StandColours colours) => colours switch
        {
            StandColours.Blue => (new Color(0.3f, 0.38f, 0.46f), new Color(0.2f, 0.31f, 0.5f), new Color(0.7f, 0.71f, 0.7f)),
            StandColours.Cream => (new Color(0.8f, 0.77f, 0.68f), new Color(0.56f, 0.2f, 0.17f), new Color(0.8f, 0.77f, 0.68f)),
            _ => (new Color(0.2f, 0.32f, 0.24f), new Color(0.17f, 0.36f, 0.25f), new Color(0.62f, 0.64f, 0.6f)),
        };

        private static Material Make(string name, Texture2D texture, float smoothness, Color? colour = null, float metallic = 0f)
        {
            var m = new Material(Lit.Shader) { name = name, enableInstancing = true };
            m.color = colour ?? Color.white;
            if (texture != null) m.mainTexture = texture;
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            return m;
        }

        private static Texture2D Noise(Color baseColour, float amount, int seed)
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
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

        /// <summary>The crowd's cards (Resources/Crowd), coloured from the palette; plain Lit if the shader is missing.</summary>
        private static Material CrowdMaterial()
        {
            Shader shader = Resources.Load<Shader>("Crowd/Crowd");
            if (shader == null)
            {
                Debug.LogWarning("Grandstands: crowd shader missing.");
                return Make("Crowd", Palette(), 0.1f);
            }
            var m = new Material(shader) { name = "Crowd" };
            m.SetTexture("_Palette", Palette());
            return m;
        }

        /// <summary>The crowd's palette, one texel per colour, never blended with its neighbours.</summary>
        private static Texture2D Palette()
        {
            var tex = new Texture2D(CrowdPalette.Width, CrowdPalette.Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[CrowdPalette.Width * CrowdPalette.Height];
            for (int i = 0; i < px.Length; i++) px[i] = Color.grey;
            for (int row = 0; row < CrowdPalette.Height; row++)
            {
                Color[] colours = CrowdPalette.Row(row);
                for (int x = 0; x < colours.Length; x++) px[row * CrowdPalette.Width + x] = colours[x];
            }
            tex.SetPixels(px);
            tex.Apply(false);
            return tex;
        }

        /// <summary>Chain-link mesh: four diamonds a tile, wire opaque and the gaps cut out.</summary>
        private static Texture2D Mesh()
        {
            const int size = 64, cells = 4;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            var px = new Color[size * size];
            const float cell = size / (float)cells;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float a = Mathf.Repeat(x + y, cell), b = Mathf.Repeat(x - y, cell);
                    bool wire = Mathf.Min(a, cell - a) < 1.3f || Mathf.Min(b, cell - b) < 1.3f;
                    px[y * size + x] = new Color(1f, 1f, 1f, wire ? 1f : 0f);
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }
    }
}
