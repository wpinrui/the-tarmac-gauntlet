using UnityEngine;

namespace TarmacGauntlet.Rendering
{
    /// <summary>
    /// The menus' translucent colours were picked while the game rendered in Gamma colour space, where UI Toolkit blended
    /// in sRGB. In Linear it blends in linear light, so white text at 62% over the dark glass comes out much brighter and
    /// a black dimmer much lighter. <see cref="Alpha"/> changes a colour's alpha so it shows as it did over the background
    /// it mostly sits on: dark glass under a light colour, the scene under a dark one. tools/linear-preview/ui_alpha.py
    /// does the same to the style sheet.
    /// </summary>
    public static class UiBlend
    {
        private const float OverDark = 0.12f, OverLight = 0.45f;

        /// <summary>This colour, as picked for blending in sRGB, with the alpha that shows it the same blended in linear light.</summary>
        public static Color Alpha(Color c)
        {
            c.a = Alpha(c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f, c.a);
            return c;
        }

        /// <summary>The same for an alpha over a colour of this sRGB brightness.</summary>
        public static float Alpha(float brightness, float a)
        {
            if (a <= 0f || a >= 1f) return a;
            float b = brightness >= 0.3f ? OverDark : OverLight;
            float lc = LinearLight.Linear(brightness), lb = LinearLight.Linear(b);
            if (Mathf.Abs(lc - lb) < 0.02f) return a;
            return Mathf.Clamp01((LinearLight.Linear(a * brightness + (1f - a) * b) - lb) / (lc - lb));
        }
    }
}
