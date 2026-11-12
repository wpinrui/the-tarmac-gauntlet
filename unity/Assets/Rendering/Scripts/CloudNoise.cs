using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace TarmacGauntlet.Rendering
{
    /// <summary>
    /// The volumetric clouds' 3D noise: a cube that wraps on every face, so it tiles through the sky without a seam. Red is
    /// the clouds' billows, cells of Worley noise softened with value noise; green is finer Worley cells that eat into their
    /// edges. Worked out once, on another thread, from the first time it is asked for (<see cref="Prepare"/>): it takes about
    /// a second.
    /// </summary>
    public static class CloudNoise
    {
        public const int Size = 64;

        private static Texture3D texture;
        private static Task<byte[]> making;

        /// <summary>Starts working the noise out, if it is not already.</summary>
        public static void Prepare()
        {
            if (texture == null && making == null) making = Task.Run(() => Field(Size));
        }

        /// <summary>The noise as a texture; null until it is worked out.</summary>
        public static Texture3D Texture
        {
            get
            {
                if (texture != null) return texture;
                Prepare();
                if (!making.IsCompleted) return null;
                byte[] field = making.Result;
                making = null;
                var px = new Color32[Size * Size * Size];
                for (int i = 0; i < px.Length; i++) px[i] = new Color32(field[i * 2], field[i * 2 + 1], 0, 255);
                texture = new Texture3D(Size, Size, Size, GraphicsFormat.R8G8B8A8_UNorm, TextureCreationFlags.None)
                {
                    name = "Cloud noise 3D",
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.DontSave,
                };
                texture.SetPixels32(px);
                texture.Apply(false, true);
                return texture;
            }
        }

        /// <summary>The noise for a cube <paramref name="size"/> across: two bytes a voxel, the billows then the detail, x fastest.</summary>
        public static byte[] Field(int size)
        {
            var field = new byte[size * size * size * 2];
            int i = 0;
            for (int z = 0; z < size; z++)
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++, i += 2)
                    {
                        float u = (x + 0.5f) / size, v = (y + 0.5f) / size, w = (z + 0.5f) / size;
                        float cells = 0.625f * Worley(u, v, w, 4, 1) + 0.25f * Worley(u, v, w, 8, 2) + 0.125f * Worley(u, v, w, 16, 3);
                        float soft = 0.6f * Value(u, v, w, 4, 4) + 0.3f * Value(u, v, w, 8, 5) + 0.1f * Value(u, v, w, 16, 6);
                        float billows = Mathf.Clamp01((cells - 0.15f) / 0.75f * 0.7f + soft * 0.3f);
                        float detail = 0.625f * Worley(u, v, w, 8, 7) + 0.25f * Worley(u, v, w, 16, 8) + 0.125f * Worley(u, v, w, 32, 9);
                        field[i] = (byte)Mathf.RoundToInt(billows * 255f);
                        field[i + 1] = (byte)Mathf.RoundToInt(Mathf.Clamp01(detail) * 255f);
                    }
            return field;
        }

        /// <summary>1 at a cell's feature point, falling to 0 a cell away: inverted Worley noise on a lattice that wraps.</summary>
        private static float Worley(float u, float v, float w, int cells, int seed)
        {
            float px = u * cells, py = v * cells, pz = w * cells;
            int cx = Mathf.FloorToInt(px), cy = Mathf.FloorToInt(py), cz = Mathf.FloorToInt(pz);
            float nearest = 9f;
            for (int dz = -1; dz <= 1; dz++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int ix = cx + dx, iy = cy + dy, iz = cz + dz;
                        int wx = Wrap(ix, cells), wy = Wrap(iy, cells), wz = Wrap(iz, cells);
                        float fx = ix + Hash(wx, wy, wz, seed * 3) - px;
                        float fy = iy + Hash(wx, wy, wz, seed * 3 + 1) - py;
                        float fz = iz + Hash(wx, wy, wz, seed * 3 + 2) - pz;
                        float d = fx * fx + fy * fy + fz * fz;
                        if (d < nearest) nearest = d;
                    }
            return 1f - Mathf.Clamp01(Mathf.Sqrt(nearest));
        }

        /// <summary>Smooth value noise, 0 to 1, on a lattice that wraps.</summary>
        private static float Value(float u, float v, float w, int cells, int seed)
        {
            float px = u * cells, py = v * cells, pz = w * cells;
            int x0 = Mathf.FloorToInt(px), y0 = Mathf.FloorToInt(py), z0 = Mathf.FloorToInt(pz);
            float tx = Fade(px - x0), ty = Fade(py - y0), tz = Fade(pz - z0);
            float Corner(int dx, int dy, int dz) => Hash(Wrap(x0 + dx, cells), Wrap(y0 + dy, cells), Wrap(z0 + dz, cells), seed);
            float a = Mathf.Lerp(Corner(0, 0, 0), Corner(1, 0, 0), tx), b = Mathf.Lerp(Corner(0, 1, 0), Corner(1, 1, 0), tx);
            float c = Mathf.Lerp(Corner(0, 0, 1), Corner(1, 0, 1), tx), d = Mathf.Lerp(Corner(0, 1, 1), Corner(1, 1, 1), tx);
            return Mathf.Lerp(Mathf.Lerp(a, b, ty), Mathf.Lerp(c, d, ty), tz);
        }

        private static int Wrap(int i, int n) => ((i % n) + n) % n;

        private static float Fade(float t) => t * t * (3f - 2f * t);

        /// <summary>A repeatable random number from 0 to 1 for a lattice point.</summary>
        private static float Hash(int x, int y, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)z * 3266489917u + (uint)seed * 2246822519u;
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0xffffff) / 16777216f;
            }
        }
    }
}
