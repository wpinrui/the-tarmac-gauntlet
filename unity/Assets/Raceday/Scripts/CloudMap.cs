using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Where the race's clouds are, laid flat over the world at <see cref="Base"/>: the sky shader's two layers of cloud
    /// noise, as the cloud shadows and the volumetric clouds draw them (CloudCover.hlsl, the same sum on the GPU). Overhead
    /// the sky shader's painted clouds sit where the map puts them. The map's threshold (<see cref="CoverFor"/>) puts as much
    /// of the ground under cloud as the weather has of the sky, a little more than the painted sky shows overhead, where its
    /// clouds crowd towards the horizon. Read on the CPU to tell whether the sun shines on a spot, for the colour grade.
    /// </summary>
    public struct CloudMap
    {
        /// <summary>Height the map lies at, m: Rendering's Atmosphere.CloudBase.</summary>
        public const float Base = 1500f;

        /// <summary>The noise, <see cref="Size"/> square: big masses and finer detail, as <see cref="CloudField.Fbm"/> made them.</summary>
        public float[] Big, Small;
        public int Size;
        /// <summary>The map's tiles a metre (<see cref="TilesFor"/>), its cover threshold and how overcast it is.</summary>
        public float Tiles, Cover, Overcast;
        /// <summary>The two layers' drift, tiles: xy the big layer, zw the small one.</summary>
        public Vector4 Offset;

        /// <summary>The map's scale for the sky shader's cloud scale under this much overcast: the forms grow under cloud as they do in the sky.</summary>
        public static float TilesFor(float cloudScale, float overcast) => cloudScale * Mathf.Lerp(1f, 0.5f, overcast) / Base;

        /// <summary>The map's cover threshold for this share of the sky under cloud: lower is more cloud.</summary>
        public static float CoverFor(float cloud) => Mathf.Lerp(0.57f, 0.27f, cloud);

        /// <summary>Share of the sun's light a cloud's shadow takes: deep under fair weather cumulus, faint in an overcast deck that has taken the sun already.</summary>
        public static float Strength(float overcast) => 0.85f * (1f - 0.9f * overcast);

        /// <summary>How much cloud is over this spot, 0 to 1, x and z in world metres.</summary>
        public float At(float x, float z)
        {
            float o = Overcast;
            float u = x * Tiles, v = z * Tiles;
            float small = Mathf.Lerp(Sample(Small, u * 2.3f + Offset.z, v * 2.3f + Offset.w), 0.5f, 0.7f * o);
            float n = Sample(Big, u + Offset.x, v + Offset.y) * 0.7f + small * 0.3f;
            return SkyClock.Smooth(Cover - 0.2f * o, Cover + 0.18f + 0.1f * o, n);
        }

        /// <summary>
        /// How much cloud the sun's light crosses on its way to this point: the cloud where the ray towards the sun meets the
        /// map, 0 in full sun.
        /// </summary>
        public float Shading(Vector3 p, Vector3 sun)
        {
            float up = Mathf.Max(sun.y, 0.08f);
            float t = (Base - p.y) / up;
            return At(p.x + sun.x * t, p.z + sun.z * t);
        }

        /// <summary>The noise at a spot on the map, filtered between texels and wrapping as the GPU's texture does.</summary>
        private float Sample(float[] field, float u, float v)
        {
            float fx = u * Size - 0.5f, fy = v * Size - 0.5f;
            int x0 = Mathf.FloorToInt(fx), y0 = Mathf.FloorToInt(fy);
            float tx = fx - x0, ty = fy - y0;
            int x1 = Wrap(x0 + 1), y1 = Wrap(y0 + 1);
            x0 = Wrap(x0);
            y0 = Wrap(y0);
            float a = field[y0 * Size + x0], b = field[y0 * Size + x1], c = field[y1 * Size + x0], d = field[y1 * Size + x1];
            return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), ty);
        }

        private int Wrap(int i) => ((i % Size) + Size) % Size;
    }
}
