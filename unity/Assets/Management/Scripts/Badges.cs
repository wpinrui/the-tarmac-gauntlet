using UnityEngine;
using UnityEngine.UIElements;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// Team badges: a shape tinted with the team colour. The shapes are white images in Resources/Badges.
    /// A team saved before badges existed shows the old red shield.
    /// </summary>
    public static class Badges
    {
        public static readonly string[] Shapes = { "shield", "star", "bolt", "wings", "flame", "diamond", "chevrons", "gear", "target" };

        public static readonly string[] Colours = { "#e10600", "#ff7a00", "#ffd400", "#3ddc84", "#00b3a6", "#3aa0ff", "#2355e0", "#8a4dff", "#ff4fa0", "#ffffff" };

        public static Color Colour(string hex) => ColorUtility.TryParseHtmlString(hex, out Color c) ? c : Color.white;

        public static Texture2D Shape(string shape) => Resources.Load<Texture2D>("Badges/" + shape);

        /// <summary>Draws a badge as <paramref name="e"/>'s background.</summary>
        public static void Paint(VisualElement e, string shape, string colour)
        {
            Texture2D image = string.IsNullOrEmpty(shape) ? null : Shape(shape);
            if (image == null)
            {
                e.style.backgroundImage = Resources.Load<Texture2D>("MenuIcons/badge");
                e.style.unityBackgroundImageTintColor = Color.white;
                return;
            }
            e.style.backgroundImage = image;
            e.style.unityBackgroundImageTintColor = Colour(colour);
        }
    }
}
