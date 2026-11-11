using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The circuit's outline drawn once into a white texture, centred and padded, for live timing and the
    /// broadcast's map, and where a car on the circuit sits on it.
    /// </summary>
    public sealed class TrackMap
    {
        public readonly Texture2D Texture;
        public readonly int Width, Height;

        private readonly Vector2 min;
        private readonly float scale;

        public TrackMap(Circuit circuit, int width, int height, float pad = 28f, float half = 6f)
        {
            Width = width;
            Height = height;
            Vector3[] c = circuit.Centre;
            min = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 max = new Vector2(float.MinValue, float.MinValue);
            foreach (Vector3 p in c)
            {
                min = Vector2.Min(min, new Vector2(p.x, p.z));
                max = Vector2.Max(max, new Vector2(p.x, p.z));
            }
            Vector2 size = max - min;
            scale = Mathf.Min((width - 2f * pad) / Mathf.Max(size.x, 1f), (height - 2f * pad) / Mathf.Max(size.y, 1f));
            // Centre the circuit in the texture.
            min -= new Vector2((width / scale - size.x) / 2f, (height / scale - size.y) / 2f);

            var alpha = new byte[width * height];
            for (int i = 0; i < c.Length; i++)
            {
                Vector3 a = c[i], b = c[(i + 1) % c.Length];
                int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) * scale));
                for (int s = 0; s <= steps; s++)
                {
                    Vector3 p = Vector3.Lerp(a, b, s / (float)steps);
                    float px = (p.x - min.x) * scale, py = (p.z - min.y) * scale;
                    for (int y = Mathf.FloorToInt(py - half); y <= Mathf.CeilToInt(py + half); y++)
                    for (int x = Mathf.FloorToInt(px - half); x <= Mathf.CeilToInt(px + half); x++)
                    {
                        if (x < 0 || y < 0 || x >= width || y >= height) continue;
                        float d = Mathf.Sqrt((x + 0.5f - px) * (x + 0.5f - px) + (y + 0.5f - py) * (y + 0.5f - py));
                        byte v = (byte)(255f * Mathf.Clamp01(half - d));
                        if (v > alpha[y * width + x]) alpha[y * width + x] = v;
                    }
                }
            }
            var pixels = new Color32[alpha.Length];
            for (int i = 0; i < alpha.Length; i++) pixels[i] = new Color32(255, 255, 255, alpha[i]);
            Texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            Texture.SetPixels32(pixels);
            Texture.Apply();
        }

        /// <summary>The texture's place in a panel, keeping its shape.</summary>
        public Rect Fit(Rect r)
        {
            float s = Mathf.Min(r.width / Width, r.height / Height);
            float w = Width * s, h = Height * s;
            return new Rect(r.x + (r.width - w) / 2f, r.y + (r.height - h) / 2f, w, h);
        }

        /// <summary>Where a point of the circuit sits on the texture drawn in <paramref name="inner"/>.</summary>
        public Vector2 At(Rect inner, Vector3 p)
        {
            float u = (p.x - min.x) * scale / Width, v = 1f - (p.z - min.y) * scale / Height;
            return new Vector2(inner.x + u * inner.width, inner.y + v * inner.height);
        }

        /// <summary>Where a car is, from its distance and place across the track, so gliding cars show too.</summary>
        public static Vector3 OnTrack(Circuit circuit, RaceCar car)
        {
            TrafficCar t = car.Traffic;
            int i = circuit.IndexOf(Mathf.Repeat(t.Distance, circuit.Lap));
            return circuit.Centre[i] + circuit.Left[i] * t.Lateral;
        }
    }
}
