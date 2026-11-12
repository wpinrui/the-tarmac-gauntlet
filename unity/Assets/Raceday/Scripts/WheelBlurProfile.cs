using System;
using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// What a wheel's rim looks like spinning too fast to follow, worked out from its own triangles: looking along the axle
    /// from outside, each ring of the wheel face shows the average of everything that turns through it. A ring through the
    /// spokes is part spoke and part gap; the lip is solid. The result is one colour and cover per ring, from the centre
    /// out, which <see cref="WheelBlur"/> draws as a disc over the wheel. Plain maths, so it runs outside Unity too.
    /// </summary>
    public static class WheelBlurProfile
    {
        /// <summary>Rings from the centre to the rim's edge.</summary>
        public const int Rings = 64;
        /// <summary>Cells across the wheel face the triangles are drawn into.</summary>
        public const int Cells = 192;
        /// <summary>The deepest part seen through the face, in the barrel or at the back of the hub, is lit this much of the face.</summary>
        private const float DeepShade = 0.25f;

        /// <summary>The disc fades in as the wheel turns from the first to the second rate, rad/s (about 10 to 28 km/h)...</summary>
        public const float FadeFrom = 9f, FadeTo = 24f;
        /// <summary>...or as it turns from the first to the second angle in one frame, radians, whichever comes first.</summary>
        public const float StepFrom = 0.14f, StepTo = 0.38f;
        /// <summary>The streaks never turn more than this in a frame, radians, so they never strobe.</summary>
        public const float MostStreakStep = 0.3f;

        /// <summary>How far in the disc is for a wheel turning at a rate, rad/s, with a frame lasting dt, s: 0 none, 1 full.</summary>
        public static float Fade(float rate, float dt)
        {
            float w = Mathf.Abs(rate);
            return Mathf.Max(Smooth(FadeFrom, FadeTo, w), Smooth(StepFrom, StepTo, w * dt));
        }

        /// <summary>How far the streaks turn this frame for a wheel turning at a rate, rad/s: with it, but never too far to follow.</summary>
        public static float StreakStep(float rate, float dt)
        {
            float step = rate * dt;
            return Mathf.Clamp(step, -MostStreakStep, MostStreakStep);
        }

        private static float Smooth(float from, float to, float x)
        {
            float t = Mathf.Clamp01((x - from) / (to - from));
            return t * t * (3f - 2f * t);
        }

        public struct Result
        {
            /// <summary>Per ring, from the centre out: red, green, blue, then the share of the ring covered.</summary>
            public float[] Rgba;
            /// <summary>The rim's radius, and its outer face along the axle (outboard positive), m.</summary>
            public float Radius, Face;
        }

        /// <summary>
        /// The profile of the parts whose triangles are given, as positions in the wheel's space with the axle along x,
        /// outboard positive, and the centre at the origin: three corners a triangle, with its colour. With a picture of
        /// <see cref="Cells"/> squared cells, each gets the colour seen there from outside, or x below 0 where nothing is.
        /// </summary>
        public static Result Build(IReadOnlyList<Vector3> corners, IReadOnlyList<Vector3> colours, Vector3[] picture = null)
        {
            int triangles = Math.Min(corners.Count / 3, colours.Count);
            float radius = 0f, face = float.MinValue, back = float.MaxValue;
            for (int i = 0; i < triangles * 3; i++)
            {
                Vector3 p = corners[i];
                radius = Math.Max(radius, (float)Math.Sqrt(p.y * p.y + p.z * p.z));
                face = Math.Max(face, p.x);
                back = Math.Min(back, p.x);
            }
            var result = new Result { Rgba = new float[Rings * 4], Radius = radius, Face = face };
            if (triangles == 0 || radius <= 1e-4f) return result;

            // The outermost surface over each cell, and its colour.
            var depth = new float[Cells * Cells];
            var colour = new Vector3[Cells * Cells];
            for (int i = 0; i < depth.Length; i++) depth[i] = float.MinValue;
            float cell = 2f * radius / Cells;
            for (int t = 0; t < triangles; t++)
                Draw(corners[3 * t], corners[3 * t + 1], corners[3 * t + 2], colours[t], radius, cell, depth, colour);

            // Each ring's cover and average colour, the deep parts darker as they sit in the barrel's shadow.
            var count = new float[Rings];
            var covered = new float[Rings];
            var sum = new Vector3[Rings];
            float thick = Math.Max(face - back, 1e-4f);
            for (int y = 0; y < Cells; y++)
            for (int z = 0; z < Cells; z++)
            {
                float py = -radius + (y + 0.5f) * cell, pz = -radius + (z + 0.5f) * cell;
                float r = (float)Math.Sqrt(py * py + pz * pz) / radius;
                int k = y * Cells + z;
                if (picture != null) picture[k] = new Vector3(-1f, 0f, 0f);
                if (r >= 1f) continue;
                int ring = Math.Min((int)(r * Rings), Rings - 1);
                count[ring]++;
                if (depth[k] == float.MinValue) continue;
                covered[ring]++;
                float shade = 1f - (1f - DeepShade) * Mathf.Clamp01((face - depth[k]) / thick);
                sum[ring] += colour[k] * shade;
                if (picture != null) picture[k] = colour[k] * shade;
            }

            // Rings too small to hold a cell borrow from the next one out.
            for (int ring = Rings - 1; ring >= 0; ring--)
            {
                if (count[ring] > 0f) continue;
                int from = Math.Min(ring + 1, Rings - 1);
                count[ring] = count[from];
                covered[ring] = covered[from];
                sum[ring] = sum[from];
            }
            for (int ring = 0; ring < Rings; ring++)
            {
                // Softened across neighbouring rings, so the disc has no hard steps the eye could catch turning.
                float n = 0f, c = 0f;
                Vector3 s = Vector3.zero;
                for (int d = -1; d <= 1; d++)
                {
                    int j = Math.Max(0, Math.Min(Rings - 1, ring + d));
                    float weight = d == 0 ? 2f : 1f;
                    n += count[j] * weight;
                    c += covered[j] * weight;
                    s += sum[j] * weight;
                }
                Vector3 average = c > 0f ? s / c : Vector3.zero;
                result.Rgba[ring * 4] = average.x;
                result.Rgba[ring * 4 + 1] = average.y;
                result.Rgba[ring * 4 + 2] = average.z;
                result.Rgba[ring * 4 + 3] = n > 0f ? c / n : 0f;
            }
            return result;
        }

        /// <summary>One triangle into the cells it covers, seen along the axle, keeping the outermost surface over each.</summary>
        private static void Draw(Vector3 a, Vector3 b, Vector3 c, Vector3 rgb, float radius, float cell, float[] depth, Vector3[] colour)
        {
            float area = (b.y - a.y) * (c.z - a.z) - (c.y - a.y) * (b.z - a.z);
            if (Math.Abs(area) < 1e-9f) return;   // edge on to the axle: seen from outside it covers nothing
            int y0 = Clamp((int)((Math.Min(a.y, Math.Min(b.y, c.y)) + radius) / cell)), y1 = Clamp((int)((Math.Max(a.y, Math.Max(b.y, c.y)) + radius) / cell));
            int z0 = Clamp((int)((Math.Min(a.z, Math.Min(b.z, c.z)) + radius) / cell)), z1 = Clamp((int)((Math.Max(a.z, Math.Max(b.z, c.z)) + radius) / cell));
            for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
            {
                float py = -radius + (y + 0.5f) * cell, pz = -radius + (z + 0.5f) * cell;
                float wa = ((b.y - py) * (c.z - pz) - (c.y - py) * (b.z - pz)) / area;
                float wb = ((c.y - py) * (a.z - pz) - (a.y - py) * (c.z - pz)) / area;
                float wc = 1f - wa - wb;
                if (wa < 0f || wb < 0f || wc < 0f) continue;
                float x = wa * a.x + wb * b.x + wc * c.x;
                int k = y * Cells + z;
                if (x <= depth[k]) continue;
                depth[k] = x;
                colour[k] = rgb;
            }
        }

        private static int Clamp(int i) => i < 0 ? 0 : i >= Cells ? Cells - 1 : i;
    }
}
