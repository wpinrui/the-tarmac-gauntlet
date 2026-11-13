using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The C# copy of how a lamp lights a surface: URP's spot light attenuation and Lamps.hlsl's caps, for laying out the
    /// floodlights (<see cref="FloodlightLayout"/>) and for tools/night-budget. Keep it in step with the shader.
    /// </summary>
    public static class LampLight
    {
        /// <summary>Rises one for one up to half of most, then eases towards most and never reaches it: Lamps.hlsl's LampKnee.</summary>
        public static float Knee(float x, float most)
        {
            float k = most * 0.5f;
            return x <= k ? x : k + (most - k) * (1f - Mathf.Exp(-(x - k) / (most - k)));
        }

        /// <summary>
        /// URP's attenuation for a spot light (RealtimeLights.hlsl): the square of the distance, smoothed to nothing at the
        /// range, and the cone, eased from its core to its edge. Unit vector toLamp from the surface to the lamp.
        /// </summary>
        public static float Attenuation(Vector3 toLampVector, Vector3 aim, float range, float coneDegrees, float coreDegrees)
        {
            float dd = Mathf.Max(toLampVector.sqrMagnitude, 6.1e-5f);
            float f = dd / (range * range);
            float smooth = Mathf.Clamp01(1f - f * f);
            smooth *= smooth;
            Vector3 l = toLampVector / Mathf.Sqrt(dd);
            float cosOuter = Mathf.Cos(coneDegrees * 0.5f * Mathf.Deg2Rad), cosInner = Mathf.Cos(coreDegrees * 0.5f * Mathf.Deg2Rad);
            float inv = 1f / Mathf.Max(cosInner - cosOuter, 0.001f);
            float a = Mathf.Clamp01(Vector3.Dot(aim, -l) * inv - cosOuter * inv);
            return smooth / dd * a * a;
        }

        /// <summary>One lamp's irradiance on a surface with this normal, before the caps.</summary>
        public static float Raw(float intensity, Vector3 lamp, Vector3 aim, float range, float cone, float core, Vector3 at, Vector3 normal)
        {
            Vector3 v = lamp - at;
            float facing = Mathf.Clamp01(Vector3.Dot(normal, v.normalized));
            return intensity * Attenuation(v, aim, range, cone, core) * facing;
        }

        /// <summary>
        /// The low beam's shape over the light's cookie, from its left to its right and its bottom to its top, -1 to 1 as URP
        /// lays a spot light's cookie over its cone (the tangent of the angle off the axis over the tangent of half the
        /// cone): full across the middle, softly narrower out to the sides, cut off a little above level, and fading out
        /// well below it.
        /// </summary>
        public static float Beam(float u, float v)
        {
            float half = NightBudget.HeadlampCone * 0.5f, tan = Mathf.Tan(half * Mathf.Deg2Rad);
            // Degrees across from the axis, and up from level, which lies a dip above the axis.
            float across = Mathf.Atan(u * tan) * Mathf.Rad2Deg, up = Mathf.Atan(v * tan) * Mathf.Rad2Deg - NightBudget.HeadlampDip;
            float side = Mathf.Exp(-Mathf.Pow(across / 30f, 4f));
            float cut = 1f - SkyClock.Smooth(0.5f, 3f, up);
            float low = SkyClock.Smooth(-half - 2f, -14f, up);
            return side * cut * low;
        }
    }
}
