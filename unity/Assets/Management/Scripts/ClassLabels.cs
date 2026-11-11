using System.Collections.Generic;
using TarmacGauntlet.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// Class labels: the class letter in its class colour, with a border of the same colour. F1 is white on a black
    /// and white chequer.
    /// </summary>
    internal static class ClassLabels
    {
        private static readonly Dictionary<CarClass, Color32> Colours = new Dictionary<CarClass, Color32>
        {
            { CarClass.F, new Color32(0xa0, 0x78, 0x5a, 0xff) },
            { CarClass.E, new Color32(0x5a, 0xb8, 0xd8, 0xff) },
            { CarClass.D, new Color32(0xd0, 0x60, 0xa0, 0xff) },
            { CarClass.C, new Color32(0xe0, 0x94, 0x3a, 0xff) },
            { CarClass.B, new Color32(0xd0, 0x40, 0x40, 0xff) },
            { CarClass.A, new Color32(0x50, 0xc8, 0x78, 0xff) },
        };

        /// <summary>Quickest first, as the results list them.</summary>
        internal static readonly CarClass[] Order = { CarClass.F1, CarClass.A, CarClass.B, CarClass.C, CarClass.D, CarClass.E, CarClass.F };

        private static Texture2D chequer;

        internal static string Name(CarClass c) => c == CarClass.F1 ? "F1" : c.ToString();

        /// <summary>The class colour, or white for F1.</summary>
        internal static Color Colour(CarClass c) => Colours.TryGetValue(c, out Color32 colour) ? colour : Color.white;

        /// <summary>A boxed class label. The size comes from the class given, such as "cls-sm".</summary>
        internal static Label Make(CarClass c, string sizeClass)
        {
            var label = new Label(Name(c)) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("cls");
            label.AddToClassList(sizeClass);
            label.style.unityFontDefinition = FontDefinition.FromFont(MenuKit.Bold);
            Paint(label, c);
            return label;
        }

        /// <summary>Colours a label's text and border for its class.</summary>
        internal static void Paint(Label label, CarClass c)
        {
            label.text = Name(c);
            Color colour = Colour(c);
            label.style.color = colour;
            label.style.borderTopColor = label.style.borderBottomColor = label.style.borderLeftColor = label.style.borderRightColor = colour;
            label.style.backgroundImage = c == CarClass.F1 ? Chequer : null;
        }

        /// <summary>Text only in the class colour, for class letters that stand on their own.</summary>
        internal static void Tint(Label label, CarClass c)
        {
            label.text = Name(c);
            label.style.color = Colour(c);
        }

        /// <summary>A black and white chequer, 4 by 4 squares, faded so the white F1 reads over it.</summary>
        private static Texture2D Chequer
        {
            get
            {
                if (chequer != null) return chequer;
                const int n = 4;
                chequer = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.HideAndDontSave };
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    chequer.SetPixel(x, y, (x + y) % 2 == 0 ? new Color(0, 0, 0, 0.9f) : new Color(1, 1, 1, 0.35f));
                chequer.Apply();
                return chequer;
            }
        }
    }
}
