using System.Collections.Generic;
using TarmacGauntlet.Rendering;
using TarmacGauntlet.Simulation;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The proximity arrows while you drive: for each car close behind you, three triangles on a wide arc below the
    /// middle of the screen, set round it at the car's bearing from you (straight behind at the bottom, alongside at
    /// either end) and pointing out toward it. One to three of them are lit as the car comes closer
    /// (<see cref="Proximity"/>), the inner one first. They take the car's class colour: lit ones solid with a dark edge,
    /// unlit ones dark glass edged in the colour. They show while the driving HUD does (<see cref="DriveHud.Showing"/>),
    /// unless turned off in the settings (<see cref="DriveSettings.ProximityArrows"/>).
    /// Laid out on a 1920 by 1080 screen and scaled to its height.
    /// </summary>
    public sealed class ProximityArrows : MonoBehaviour
    {
        public DriveHud Hud;
        /// <summary>Every car, in the order of <see cref="Classes"/>.</summary>
        public CarController[] Cars;
        public RaceCar[] Racing;
        public CarClass[] Classes;

        /// <summary>The arc round the middle of the screen, px on the layout: half its width, and its depth below the middle.</summary>
        private const float ArcWidth = 780f, ArcDepth = 320f, ArcEdge = 40f;
        /// <summary>One triangle, px on the layout: across, deep, its edge, and the gap between triangles as a share of its depth.</summary>
        private const float TriWidth = 44f, TriDepth = 28f, Edge = 3.5f, Gap = 0.12f;
        /// <summary>Seconds over which the speed a car closes on you settles.</summary>
        private const float Smoothing = 0.3f;
        /// <summary>Texture pixels to a layout px.</summary>
        private const int Detail = 3;

        private static readonly Color Dark = new Color(0.043f, 0.043f, 0.051f);

        private struct Track
        {
            public float Ahead, Closing;
            public bool Seen;
        }

        private struct Arrow
        {
            public float Bearing, Side;
            public int Level;
            public Color Colour;
        }

        private readonly Dictionary<CarController, Track> tracks = new Dictionary<CarController, Track>();
        private readonly List<Arrow> arrows = new List<Arrow>();
        private Texture2D full, ring, inner;

        private void LateUpdate()
        {
            arrows.Clear();
            CarController own = Hud != null ? Hud.Car : null;
            if (own == null || Cars == null) return;
            float dt = Time.deltaTime;
            Transform me = own.transform;
            float half = HalfLength(own);
            for (int i = 0; i < Cars.Length; i++)
            {
                CarController car = Cars[i];
                if (car == null || car == own || !car.isActiveAndEnabled) { if (car != null) tracks.Remove(car); continue; }
                if (Racing != null && i < Racing.Length && Racing[i] != null && Racing[i].InPitLane) { tracks.Remove(car); continue; }
                Vector3 local = me.InverseTransformPoint(car.transform.position);
                // How fast it gains on you: the change in its distance ahead, smoothed, as gliding cars move in steps.
                tracks.TryGetValue(car, out Track t);
                if (dt > 0f)
                {
                    float rate = t.Seen ? (local.z - t.Ahead) / dt : 0f;
                    t.Closing = t.Seen ? Mathf.Lerp(t.Closing, rate, 1f - Mathf.Exp(-dt / Smoothing)) : 0f;
                    t.Ahead = local.z;
                    t.Seen = true;
                    tracks[car] = t;
                }
                float facing = Vector3.Dot(me.forward, car.transform.forward);
                int level = Proximity.Level(local.x, local.z, local.y, facing, half, HalfLength(car), t.Closing);
                if (level == 0) continue;
                arrows.Add(new Arrow
                {
                    Bearing = Proximity.Bearing(local.x, local.z),
                    Side = local.x < 0f ? -1f : 1f,
                    Level = level,
                    Colour = Classes != null && i < Classes.Length ? RaceTower.ClassColour(Classes[i]) : Color.white,
                });
            }
            // The closest drawn last, on top.
            arrows.Sort((a, b) => a.Level.CompareTo(b.Level));
        }

        private static float HalfLength(CarController car) => car.TryGetComponent(out BoxCollider box) ? box.size.z / 2f : 2.3f;

        private void OnGUI()
        {
            using (GammaGui.Begin()) DrawGui();
        }

        private void DrawGui()
        {
            if (Event.current.type != EventType.Repaint || arrows.Count == 0 || Hud == null || !Hud.Showing || !DriveSettings.ProximityArrows) return;
            if (full == null) Build();
            float k = Screen.height / 1080f;
            var centre = new Vector2(Screen.width / 2f, Screen.height / 2f);
            float rx = Mathf.Min(ArcWidth * k, Screen.width / 2f - ArcEdge * k), ry = ArcDepth * k;
            float w = TriWidth * k, h = TriDepth * k, gap = Gap * h, column = 3f * h + 2f * gap;
            // The textures carry a margin of one edge's width round the triangle.
            float pad = Edge * k;
            Matrix4x4 was = GUI.matrix;
            foreach (Arrow a in arrows)
            {
                float t = a.Bearing * Mathf.Deg2Rad;
                var at = new Vector2(centre.x + a.Side * rx * Mathf.Sin(t), centre.y + ry * Mathf.Cos(t));
                GUIUtility.RotateAroundPivot(-a.Side * a.Bearing, at);
                for (int i = 0; i < 3; i++)
                {
                    // i = 0 is the inner triangle, nearest the middle of the screen, and lights first.
                    float top = at.y - column / 2f + i * (h + gap);
                    var r = new Rect(at.x - w / 2f - pad, top - pad, w + 2f * pad, h + 2f * pad);
                    if (i < a.Level)
                    {
                        Draw(r, ring, new Color(Dark.r, Dark.g, Dark.b, 0.9f));
                        Draw(r, full, a.Colour);
                    }
                    else
                    {
                        Draw(r, inner, new Color(Dark.r, Dark.g, Dark.b, 0.45f));
                        Draw(r, ring, new Color(a.Colour.r, a.Colour.g, a.Colour.b, 0.75f));
                    }
                }
                GUI.matrix = was;
            }
        }

        private static void Draw(Rect r, Texture2D tex, Color colour) =>
            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill, true, 0f, colour, 0f, 0f);

        /// <summary>
        /// White masks of one triangle pointing down, with a margin of one edge's width: the whole triangle, a band one
        /// edge wide centred on its outline, and the part inside that band. Smoothed over a texture pixel.
        /// </summary>
        private void Build()
        {
            int width = Mathf.CeilToInt((TriWidth + 2f * Edge) * Detail), height = Mathf.CeilToInt((TriDepth + 2f * Edge) * Detail);
            full = Mask(width, height, d => d);
            ring = Mask(width, height, d => Mathf.Abs(d) - Edge / 2f);
            inner = Mask(width, height, d => d + Edge / 2f);
        }

        /// <summary>A mask from a signed distance to the triangle's outline, layout px, negative inside: filled where shape(d) is below 0.</summary>
        private static Texture2D Mask(int width, int height, System.Func<float, float> shape)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, hideFlags = HideFlags.DontSave };
            var px = new Color[width * height];
            var a = new Vector2(0f, 0f);
            var b = new Vector2(TriWidth, 0f);
            var c = new Vector2(TriWidth / 2f, TriDepth);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    // Texture rows run bottom up; the triangle's flat side is at the top.
                    var p = new Vector2((x + 0.5f) / Detail - Edge, (height - y - 0.5f) / Detail - Edge);
                    float d = SignedDistance(p, a, b, c);
                    float alpha = Mathf.Clamp01(0.5f - shape(d) * Detail);
                    px[y * width + x] = new Color(1f, 1f, 1f, alpha);
                }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        /// <summary>Distance from p to the triangle abc's outline, negative inside.</summary>
        private static float SignedDistance(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d = Mathf.Min(Segment(p, a, b), Mathf.Min(Segment(p, b, c), Segment(p, c, a)));
            bool inside = Cross(b - a, p - a) >= 0f == Cross(c - b, p - b) >= 0f && Cross(c - b, p - b) >= 0f == Cross(a - c, p - c) >= 0f;
            return inside ? -d : d;
        }

        private static float Cross(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;

        private static float Segment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return (p - (a + t * ab)).magnitude;
        }

        private void OnDestroy()
        {
            if (full != null) Destroy(full);
            if (ring != null) Destroy(ring);
            if (inner != null) Destroy(inner);
        }
    }
}
