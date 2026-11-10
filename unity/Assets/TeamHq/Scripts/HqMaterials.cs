using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.TeamHq
{
    /// <summary>The building's materials, made in code so the scene needs no asset files.</summary>
    public static class HqMaterials
    {
        /// <summary>Parts that let light through, so they cast no shadow.</summary>
        public static readonly HashSet<string> NoShadow = new HashSet<string> { "window", "glass", "lamp", "screen" };

        public static Material For(string key)
        {
            switch (key)
            {
                case "floor": return Make(key, Hex(0x8a908e), 0.72f, 0.05f, Noise(Hex(0x777d7b), 0.03f, 1, 256));
                case "wall": return Make(key, Color.white, 0.12f, 0, Block());
                case "dado": return Make(key, Hex(0x3a3e3d), 0.35f);
                case "ceiling": return Make(key, Hex(0x9da2a0), 0.3f, 0.2f, Ribs());
                case "slab": return Make(key, Hex(0xb8bcb6), 0.15f);
                case "door": return Make(key, Hex(0x77807e), 0.5f, 0.4f);
                case "window": return Emissive(key, Hex(0xdbeaf3), Hex(0xcfe4f2) * 0.9f);
                case "lamp": return Emissive(key, Color.white, Hex(0xfff6e6) * 2.2f);
                case "screen": return Emissive(key, Hex(0x10181e), Hex(0x35627e) * 0.8f, 0.8f);
                case "line": return Make(key, Hex(0xe2c23a), 0.5f);
                case "glass": return Glass(key);
                case "trim": return Make(key, Hex(0x2b2f2e), 0.55f, 0.35f);
                case "steel": return Make(key, Hex(0x6c7472), 0.6f, 0.7f);
                case "chrome": return Make(key, Hex(0xd0d4d6), 0.85f, 1f);
                case "walnut": return Make(key, Hex(0x5a3d2b), 0.4f);
                case "dark": return Make(key, Hex(0x262a29), 0.4f);
                case "rubber": return Make(key, Hex(0x1b1c1c), 0.1f);
                case "cork": return Make(key, Hex(0xb48a5a), 0.1f);
                case "paper": return Emissive(key, Hex(0xf3f0e7), Hex(0x403c33) * 0.15f, 0.15f);
                case "folder": return Make(key, Hex(0xb53a2e), 0.3f);
                case "frame": return Make(key, Hex(0x1e2120), 0.5f);
                case "gold": return Make(key, Hex(0xd8a93a), 0.75f, 0.9f);
                case "silver": return Make(key, Hex(0xc8ccd0), 0.75f, 0.9f);
                case "bronze": return Make(key, Hex(0xb07a4a), 0.7f, 0.9f);
                case "fabric": return Make(key, Hex(0x4a525b), 0.05f);
                case "leather": return Make(key, Hex(0x1f2224), 0.45f);
                case "cabinet": return Make(key, Hex(0x5b6366), 0.55f, 0.35f);
                case "plinth": return Make(key, Hex(0x1f2122), 0.5f);
                case "velvet": return Make(key, Hex(0x2c1f22), 0f);
                case "carpet": return Make(key, Hex(0x5c5a55), 0.1f);
                case "partition": return Make(key, Hex(0xe9e8e3), 0.15f);
                case "pot": return Make(key, Hex(0xd9d6cf), 0.3f);
                case "leaves": return Make(key, Hex(0x3f6b3a), 0.2f);
                case "overalls": return Make(key, Hex(0xe08a3c), 0.2f);
                case "legs": return Make(key, Hex(0x2a2e33), 0.2f);
                case "skin": return Make(key, Hex(0xc99a78), 0.3f);
                case "race": return Make(key, Hex(0xc2362b), 0.7f, 0.3f);
                case "owned": return Make(key, Hex(0x4f6478), 0.7f, 0.3f);
                case "owned2": return Make(key, Hex(0x6f7d68), 0.7f, 0.3f);
                case "spare": return Make(key, Hex(0xd6d9db), 0.75f, 0.6f);
                default: return Make(key, Color.magenta, 0.5f);
            }
        }

        private static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

        private static Material Make(string name, Color colour, float smoothness, float metallic = 0, Texture2D texture = null)
        {
            var m = new Material(Shader.Find("Standard")) { name = name, color = colour };
            if (texture != null) m.mainTexture = texture;
            m.SetFloat("_Glossiness", smoothness);
            m.SetFloat("_Metallic", metallic);
            return m;
        }

        private static Material Emissive(string name, Color colour, Color glow, float smoothness = 0.3f)
        {
            Material m = Make(name, colour, smoothness);
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", glow);
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            return m;
        }

        /// <summary>The Standard shader in its transparent mode.</summary>
        private static Material Glass(string name)
        {
            Material m = Make(name, new Color(0.81f, 0.89f, 0.93f, 0.14f), 0.95f, 0.1f);
            m.SetFloat("_Mode", 3);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.DisableKeyword("_ALPHATEST_ON");
            m.DisableKeyword("_ALPHABLEND_ON");
            m.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return m;
        }

        /// <summary>A grainy tile, for the epoxy floor.</summary>
        private static Texture2D Noise(Color baseColour, float amount, int seed, int size)
        {
            Texture2D tex = New(size);
            var rng = new System.Random(seed);
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++)
            {
                float v = (float)(rng.NextDouble() - 0.5) * 2f * amount;
                px[i] = new Color(Mathf.Clamp01(baseColour.r + v + 0.12f), Mathf.Clamp01(baseColour.g + v + 0.12f), Mathf.Clamp01(baseColour.b + v + 0.12f), 1);
            }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>Painted concrete block, 0.4 by 0.2 m, stretcher bond. One tile is 2 m square.</summary>
        private static Texture2D Block()
        {
            const int size = 512;
            Texture2D tex = New(size);
            var rng = new System.Random(2);
            var px = new Color[size * size];
            Color paint = new Color(0.89f, 0.89f, 0.87f), joint = new Color(0.62f, 0.62f, 0.6f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int row = y * 10 / size;
                    int shift = row % 2 == 1 ? size / 10 : 0;
                    bool bed = y % (size / 10) < 2;
                    bool perp = (x + shift) % (size / 5) < 2;
                    float n = (float)(rng.NextDouble() - 0.5) * 0.03f;
                    Color c = bed || perp ? joint : paint;
                    px[y * size + x] = new Color(c.r + n, c.g + n, c.b + n, 1);
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>Ribbed metal decking for the roof, four ribs to the metre.</summary>
        private static Texture2D Ribs()
        {
            const int size = 256;
            Texture2D tex = New(size);
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float t = (x % (size / 4)) / (float)(size / 4);
                    float v = t < 0.12f ? 0.9f : t < 0.24f ? 1.05f : 0.95f;
                    px[y * size + x] = new Color(v * 0.8f, v * 0.81f, v * 0.8f, 1);
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        private static Texture2D New(int size) =>
            new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
    }
}
