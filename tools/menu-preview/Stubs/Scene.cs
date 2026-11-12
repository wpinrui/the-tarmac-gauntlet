// Stand-ins for the 3D scene the menus sit over: the camera, the bays and the glass blur. The menus only need to
// call them. The glass is drawn by the viewer's CSS from the .glass class.
using System;
using System.Collections.Generic;
using System.Linq;
using TarmacGauntlet.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace TarmacGauntlet.TeamHq
{
    public sealed class HqCamera : MonoBehaviour
    {
        /// <summary>The last shot asked for, so the viewer can pick a backdrop.</summary>
        public static string Shot = "hub";
        public bool Gliding => false;
        public void Cut(string slug) => Shot = slug;
        public void Show(string slug) => Shot = slug;
    }

    public sealed class HqGeometry
    {
        public const int RaceBay = 1;
        public const float CarZ = 0;
        public static readonly float[] BayX = { -6, -2, 2, 6 };
    }

    public enum HqBodyShape { Hatch, Coupe, Sedan, Wedge, Proto }

    public sealed class HqCarLook
    {
        public string Key, AssetPath;
        public HqBodyShape Shape;
        public float Length, Width, Height, WheelRadius;
    }
}

namespace TarmacGauntlet.Rendering
{
    [RequireComponent(typeof(Camera))]
    public sealed class GlassBackdrop : MonoBehaviour { }

    /// <summary>The quality levels the settings menu steps through; the level is only remembered here.</summary>
    public static class QualityLevels
    {
        public static readonly string[] Names = { "Low", "Medium", "High" };
        public static int Level { get; set; } = 1;
    }

    /// <summary>Scene changes do nothing here: the menus only need to call them.</summary>
    public static class SceneLoader
    {
        public static bool Loading => false;
        public static void Load(string scene) { }
    }
}

namespace TarmacGauntlet.Management
{
    public sealed class MenuGlass
    {
        public MenuGlass(VisualElement root) { }

        public VisualElement Make(params string[] classes)
        {
            var glass = new VisualElement();
            glass.AddToClassList("glass");
            foreach (string c in classes) glass.AddToClassList(c);
            var blur = new VisualElement { pickingMode = PickingMode.Ignore };
            blur.AddToClassList("glass-blur");
            var sheen = new VisualElement { pickingMode = PickingMode.Ignore };
            sheen.AddToClassList("glass-sheen");
            glass.Add(blur);
            glass.Add(sheen);
            return glass;
        }

        public void Refresh() { }
    }

    public sealed class GarageBays : MonoBehaviour
    {
        internal void Browse(string key, string modelId) { }
        internal void ShowInWorkshop(string carId) { }
    }

    internal sealed class NewspaperView
    {
        public NewspaperView(VisualElement layer, TeamHq.HqCamera hqCamera, MenuGlass glass) { }
        public bool IsOpen => false;
        public void Open(Newspaper paper, Action onClosed) { }
        public void Close() { }
        public void Hide() { }
        public void Tick() { }
    }

    /// <summary>The position chart as a polyline of the player's position per lap; Html.cs draws it as SVG.</summary>
    internal sealed class PositionChart : VisualElement
    {
        internal override string Tag => "chart";
        public event Action<int, int> Hovered { add { } remove { } }
        internal readonly IReadOnlyList<int> Positions;
        internal readonly List<(float From, float To, Color Colour)> Stints = new List<(float, float, Color)>();

        public PositionChart(RaceSummary summary, float lapScale, Func<string, Color> driverColour, Font font)
        {
            Positions = summary.PositionByLap;
            foreach (StintRecord s in summary.Stints) Stints.Add(((s.StartLap - 1) * lapScale, s.EndLap * lapScale, driverColour(s.DriverId)));
            style.flexGrow = 1;
        }
    }
}
