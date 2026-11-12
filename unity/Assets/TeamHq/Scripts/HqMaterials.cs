using System.Collections.Generic;
using TarmacGauntlet.Rendering;
using UnityEngine;

namespace TarmacGauntlet.TeamHq
{
    /// <summary>The building's materials, made in code; the textured surfaces read Resources/Textures.</summary>
    public static class HqMaterials
    {
        /// <summary>Parts that let light through, so they cast no shadow.</summary>
        public static readonly HashSet<string> NoShadow = new HashSet<string> { "window", "glass", "lamp", "screen", "blob" };

        public static Material For(string key)
        {
            if (key.StartsWith("photo")) return Photo(key);
            switch (key)
            {
                // Photo textures from Poly Haven (CC0), recoloured by tools/team-hq/props/make_textures.py.
                case "floor":
                case "wall":
                case "dado":
                case "ceiling":
                case "slab":
                case "partition":
                case "walnut":
                case "carpet":
                case "parquet":
                case "tread":
                case "door":
                    return Surface(key);
                case "blob": return Blob();
                case "window": return Emissive(key, Hex(0xdbeaf3), Hex(0xcfe4f2) * 0.9f);
                case "lamp": return Emissive(key, Color.white, Hex(0xfff6e6) * 2.2f);
                case "screen": return Emissive(key, Hex(0x10181e), Hex(0x35627e) * 0.8f, 0.35f);
                case "line": return Make(key, Hex(0xe2c23a), 0.5f);
                case "glass": return Glass(key);
                case "trim": return Make(key, Hex(0x2b2f2e), 0.55f, 0.35f);
                case "steel": return Make(key, Hex(0x6c7472), 0.6f, 0.7f);
                case "chrome": return Make(key, Hex(0xd0d4d6), 0.85f, 1f);
                case "chest": return Make(key, Hex(0x9e2a22), 0.6f, 0.3f);
                case "dark": return Make(key, Hex(0x262a29), 0.4f);
                case "rubber": return Make(key, Hex(0x1b1c1c), 0.1f);
                case "cork": return Make(key, Hex(0xb48a5a), 0.1f);
                case "paper": return Emissive(key, Hex(0xf3f0e7), Hex(0x403c33) * 0.15f, 0.15f);
                case "folder": return Make(key, Hex(0xb53a2e), 0.3f);
                case "frame": return Make(key, Hex(0x1e2120), 0.5f);
                case "leather": return Make(key, Hex(0x1f2224), 0.45f);
                case "cabinet": return Make(key, Hex(0x5b6366), 0.55f, 0.35f);
                case "plinth": return Make(key, Hex(0x1f2122), 0.5f);
                case "grey": return Make(key, Hex(0x8d9194), 0.45f, 0.15f);
                default: return Make(key, Color.magenta, 0.5f);
            }
        }

        /// <summary>A textured surface: colour, normal map, and a mask of metallic (R) and smoothness (A).</summary>
        private static Material Surface(string key)
        {
            var col = Resources.Load<Texture2D>("Textures/" + key + "_c");
            Material m = Make(key, Color.white, 1, 0, col);
            var nrm = Resources.Load<Texture2D>("Textures/" + key + "_n");
            if (nrm != null) Lit.NormalMap(m, nrm);
            var mask = Resources.Load<Texture2D>("Textures/" + key + "_s");
            if (mask != null) Lit.MetallicSmoothnessMap(m, mask);
            return m;
        }

        /// <summary>A race history photo: a shot of the team's own cars in the workshop.</summary>
        private static Material Photo(string key) => Make(key, Color.white, 0.55f, 0, Resources.Load<Texture2D>("Textures/" + key));

        /// <summary>The soft shadow under a car: black, fading out from a rounded rectangle. Same recipe as the viewer.</summary>
        private static Material Blob()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = Mathf.Abs((x + 0.5f) / n * 2 - 1), v = Mathf.Abs((y + 0.5f) / n * 2 - 1);
                    float q = Mathf.Pow(Mathf.Pow(u, 4) + Mathf.Pow(v, 4), 0.25f);
                    float a = Mathf.Clamp01((1 - q) / 0.3f);
                    px[y * n + x] = new Color(0, 0, 0, a * a * (3 - 2 * a) * 0.8f);
                }
            tex.SetPixels(px);
            tex.Apply(true);
            Material m = Make("blob", Color.white, 0, 0, tex);
            Fade(m);
            return m;
        }

        /// <summary>Alpha tested, for leaves and other cut-out textures.</summary>
        public static void Cutout(Material m) => Lit.Cutout(m);

        /// <summary>Alpha blended, no depth writes.</summary>
        public static void Fade(Material m) => Lit.Fade(m);

        private static Color Hex(int rgb) => new Color(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f);

        private static Material Make(string name, Color colour, float smoothness, float metallic = 0, Texture2D texture = null)
        {
            var m = new Material(Lit.Shader) { name = name, color = colour };
            if (texture != null) m.mainTexture = texture;
            m.SetFloat("_Smoothness", smoothness);
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

        /// <summary>See-through, with its reflections at full strength.</summary>
        private static Material Glass(string name)
        {
            Material m = Make(name, new Color(0.81f, 0.89f, 0.93f, 0.14f), 0.95f, 0.1f);
            Lit.Glass(m);
            return m;
        }
    }
}
