using System;
using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The floodlights' light over the ground, summed from every lamp's position and aim once at the start of the race, as
    /// a map seen from above: how much light falls on flat ground at each point, the way it mostly comes from, and the
    /// ground's height there. Every lit shader adds it (Rendering/Shaders/NightLit.hlsl), so the track, the lane, the
    /// grass and whatever stands on them take it in their own colour. Each lamp throws a wide beam well past the pool it is
    /// aimed at, so neighbours overlap and the track is lit evenly, with no discs. However many lamps overlap, the light
    /// eases towards <see cref="Most"/>, so nowhere is lit more than the rest.
    /// No Unity objects, so it can be checked outside Unity (tools/night-lights).
    /// </summary>
    public sealed class FloodField
    {
        /// <summary>The map's cell, m.</summary>
        public const float Texel = 4f;
        /// <summary>The map reaches this far past the farthest point a lamp aims at, m.</summary>
        public const float Margin = 70f;
        /// <summary>
        /// A lamp's beam spreads this many times as far as its aimed pool before it falls to about a third: along the
        /// track, so neighbours overlap, and across it, out from the lamp, where it falls off sooner and leaves the
        /// grass beyond the track darker.
        /// </summary>
        public const float Spread = 2.4f;
        public const float SpreadOut = 1f;
        /// <summary>
        /// The most light anywhere, however many lamps overlap. The light the dimmer lamps' targets get is set to it
        /// (<see cref="Dim"/> of them are dimmer still), and anything brighter eases towards it, so the lit track is even.
        /// </summary>
        public const float Most = 1f;
        /// <summary>The share of the lamps' targets that may be lit less than <see cref="Most"/> before the light eases.</summary>
        public const float Dim = 0.1f;
        /// <summary>
        /// Past the edge of the road or the pit lane the light fades out over these distances, m: the verges and the
        /// pit building's front stay lit, and the grass beyond darkens as it would at night.
        /// </summary>
        public const float EdgeFrom = 3f, EdgeTo = 15f;
        /// <summary>Nearer than its target, a lamp lights the ground at most this many times as brightly as at its target.</summary>
        private const float NearMost = 2f;

        public readonly int Width, Height;
        /// <summary>The map's corner with the least x and z, m.</summary>
        public readonly float X0, Z0;
        /// <summary>Light on flat ground at each cell, row by row from <see cref="Z0"/>: at most <see cref="Most"/>.</summary>
        public readonly float[] Light;
        /// <summary>The ground's height at each cell, m.</summary>
        public readonly float[] Ground;
        /// <summary>The way the light mostly comes from at each cell: a unit vector towards the lamps.</summary>
        public readonly Vector3[] Toward;

        public FloodField(Floodlights floods)
        {
            float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            foreach (Floodlight f in floods.Lights)
            {
                x0 = Mathf.Min(x0, f.Target.x); x1 = Mathf.Max(x1, f.Target.x);
                z0 = Mathf.Min(z0, f.Target.z); z1 = Mathf.Max(z1, f.Target.z);
            }
            if (floods.Lights.Count == 0) { x0 = x1 = z0 = z1 = 0f; }
            X0 = x0 - Margin;
            Z0 = z0 - Margin;
            Width = Mathf.CeilToInt((x1 - x0 + 2f * Margin) / Texel) + 1;
            Height = Mathf.CeilToInt((z1 - z0 + 2f * Margin) / Texel) + 1;
            int n = Width * Height;
            Light = new float[n];
            Ground = new float[n];
            Toward = new Vector3[n];

            // The ground's height on a coarser grid, only where a lamp reaches: it sets how far above the ground the
            // light carries, and where the slant falls.
            const int step = 2;
            int cw = (Width + step - 1) / step + 2, ch = (Height + step - 1) / step + 2;
            var needed = new bool[cw * ch];
            foreach (Floodlight f in floods.Lights)
            {
                Box(f, out int i0, out int i1, out int j0, out int j1);
                for (int j = j0 / step; j <= j1 / step + 1; j++)
                    for (int i = i0 / step; i <= i1 / step + 1; i++) needed[j * cw + i] = true;
            }
            var coarse = new float[cw * ch];
            var edge = new float[cw * ch];
            int hint = -1;
            for (int j = 0; j < ch; j++)
            {
                bool chained = false;
                for (int i = 0; i < cw; i++)
                {
                    if (!needed[j * cw + i]) { chained = false; continue; }
                    var p = new Vector3(X0 + i * step * Texel, 0f, Z0 + j * step * Texel);
                    coarse[j * cw + i] = floods.Ground(p, chained ? hint : -1, out hint, out float beyond);
                    float t = Mathf.Clamp01((beyond - EdgeFrom) / (EdgeTo - EdgeFrom));
                    edge[j * cw + i] = 1f - t * t * (3f - 2f * t);
                    chained = true;
                }
            }
            var raw = new float[n];
            var near = new float[n];
            for (int j = 0; j < Height; j++)
                for (int i = 0; i < Width; i++)
                {
                    int ci = i / step, cj = j / step;
                    float fi = (float)(i - ci * step) / step, fj = (float)(j - cj * step) / step;
                    Ground[j * Width + i] = Mathf.Lerp(Mathf.Lerp(coarse[cj * cw + ci], coarse[cj * cw + ci + 1], fi),
                        Mathf.Lerp(coarse[(cj + 1) * cw + ci], coarse[(cj + 1) * cw + ci + 1], fi), fj);
                    near[j * Width + i] = Mathf.Lerp(Mathf.Lerp(edge[cj * cw + ci], edge[cj * cw + ci + 1], fi),
                        Mathf.Lerp(edge[(cj + 1) * cw + ci], edge[(cj + 1) * cw + ci + 1], fi), fj);
                }
            // Each lamp over the cells its beam can reach.
            foreach (Floodlight f in floods.Lights)
            {
                Box(f, out int i0, out int i1, out int j0, out int j1);
                for (int j = j0; j <= j1; j++)
                    for (int i = i0; i <= i1; i++)
                    {
                        int k = j * Width + i;
                        var p = new Vector3(X0 + i * Texel, Ground[k], Z0 + j * Texel);
                        float e = Lamp(f, p);
                        if (e <= 0f) continue;
                        raw[k] += e;
                        Toward[k] += (f.Position - p).normalized * e;
                    }
            }
            for (int k = 0; k < n; k++) Toward[k] = Toward[k].sqrMagnitude > 1e-12f ? Toward[k].normalized : Vector3.up;

            // Most is the light where the dimmer lamps aim.
            var aimed = new List<float>();
            foreach (Floodlight f in floods.Lights) aimed.Add(Sample(raw, f.Target.x, f.Target.z));
            aimed.Sort();
            float scale = aimed.Count > 0 && aimed[(int)(aimed.Count * Dim)] > 1e-6f ? Most / aimed[(int)(aimed.Count * Dim)] : 0f;
            for (int k = 0; k < n; k++) Light[k] = Knee(raw[k] * scale, Most) * near[k];
        }

        /// <summary>The cells a lamp's beam can reach.</summary>
        private void Box(in Floodlight f, out int i0, out int i1, out int j0, out int j1)
        {
            float r = 3f * Spread * Mathf.Max(f.Long, f.Wide);
            i0 = Mathf.Max(0, Mathf.FloorToInt((f.Target.x - r - X0) / Texel));
            i1 = Mathf.Min(Width - 1, Mathf.CeilToInt((f.Target.x + r - X0) / Texel));
            j0 = Mathf.Max(0, Mathf.FloorToInt((f.Target.z - r - Z0) / Texel));
            j1 = Mathf.Min(Height - 1, Mathf.CeilToInt((f.Target.z + r - Z0) / Texel));
        }

        /// <summary>
        /// One lamp's light on flat ground at p, against its light at its target: its beam falls off across a wide
        /// ellipse round the target, and with the square of the distance and the slant, but nearer than the target it
        /// never lights more than <see cref="NearMost"/> times as brightly.
        /// </summary>
        public static float Lamp(in Floodlight f, Vector3 p)
        {
            Vector3 v = p - f.Position;
            float dd = v.sqrMagnitude;
            if (dd < 1e-4f || Vector3.Dot(v, f.Aim) <= 0f) return 0f;
            float d = Mathf.Sqrt(dd);
            float slant = -v.y / d;
            if (slant <= 0f) return 0f;
            Vector3 o = p - f.Target;
            o.y = 0f;
            Vector3 across = new Vector3(-f.PoolAxis.z, 0f, f.PoolAxis.x);
            float u = Vector3.Dot(o, f.PoolAxis) / (f.Long * SpreadOut), w = Vector3.Dot(o, across) / (f.Wide * Spread);
            float r2 = u * u + w * w;
            if (r2 >= 9f) return 0f;
            float t = Mathf.Clamp01((r2 - 4f) / 5f);
            float beam = Mathf.Exp(-r2) * (1f - t * t * (3f - 2f * t));
            float reach = f.Reach, slantAt = Mathf.Max(-f.Aim.y, 0.05f);
            float nearer = Mathf.Min(reach * reach / dd * (slant / slantAt), NearMost);
            return beam * nearer;
        }

        /// <summary>
        /// Light that rises one for one up to half of <paramref name="most"/>, then eases towards it and never reaches it.
        /// The same curve as Knee in NightLit.hlsl.
        /// </summary>
        public static float Knee(float x, float most)
        {
            float k = most * 0.5f;
            if (x <= k) return x;
            return k + (most - k) * (1f - Mathf.Exp(-(x - k) / (most - k)));
        }

        /// <summary>The light on flat ground at a point, between the cells, as the shader samples the map.</summary>
        public float At(float x, float z) => Sample(Light, x, z);

        private float Sample(float[] map, float x, float z)
        {
            float gi = (x - X0) / Texel, gj = (z - Z0) / Texel;
            if (gi < 0f || gj < 0f || gi > Width - 1 || gj > Height - 1) return 0f;
            int i = Mathf.Min((int)gi, Width - 2), j = Mathf.Min((int)gj, Height - 2);
            float fi = gi - i, fj = gj - j;
            return Mathf.Lerp(Mathf.Lerp(map[j * Width + i], map[j * Width + i + 1], fi),
                Mathf.Lerp(map[(j + 1) * Width + i], map[(j + 1) * Width + i + 1], fi), fj);
        }
    }
}
