using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The racing line assist for the car you drive (<see cref="DriveSettings.RacingLine"/>): solid chevrons floating
    /// over the line tuned for your car, from just ahead of it to <see cref="Ahead"/> metres on. Each chevron is
    /// coloured by your speed against the speed the line asks for there: red where you need to be braking already,
    /// amber where you need to lift, green elsewhere. "Corners" shows only the braking zones and corners. One mesh,
    /// rebuilt each frame from the samples ahead.
    /// </summary>
    public sealed class RacingLineAssist : MonoBehaviour
    {
        /// <summary>The car you drive.</summary>
        [NonSerialized] public CarController Car;
        /// <summary>The line used until the one tuned for the car is ready.</summary>
        [NonSerialized] public RacingLine Line;
        /// <summary>True while the line may show: you are at the wheel, on the track.</summary>
        [NonSerialized] public Func<bool> Showing;

        /// <summary>How far ahead the chevrons reach, and the stretch over which they fade out at the far end, m.</summary>
        private const float Ahead = 170f, FarFade = 50f;
        /// <summary>The chevrons start this far ahead of the car's sample and fade in over the next stretch, m.</summary>
        private const float NearStart = 4f, NearFade = 8f;
        /// <summary>One chevron every this many metres.</summary>
        private const float Spacing = 2f;
        /// <summary>Chevron shape, m: tip ahead of its sample, arms back and out, arm thickness along the line.</summary>
        private const float Tip = 0.55f, ArmBack = 0.45f, ArmOut = 0.75f, ArmThick = 0.5f;
        /// <summary>The underside floats this high over the road, and the block is this tall, m.</summary>
        private const float Lift = 0.1f, Height = 0.12f;
        private const float Opacity = 0.9f, SideShade = 0.55f;
        /// <summary>Faster than the line's speed by this much is red, and within <see cref="AmberBelow"/> under it amber, m/s.</summary>
        private const float RedAbove = 1.5f, AmberBelow = 2.5f;
        /// <summary>"Corners" pads each braking zone and corner by this many metres, fading over <see cref="CornerFade"/>.</summary>
        private const float CornerPad = 28f, CornerFade = 12f;
        /// <summary>Plan curvature past which the line counts as a corner, 1/m.</summary>
        private const float CornerCurvature = 1f / 260f;
        /// <summary>The line's speeds are a top driver's: the share of the car's grip and brakes they use.</summary>
        private const float DriverPace = 100f;

        private static readonly Color Green = new Color(0.15f, 0.85f, 0.25f), Amber = new Color(1f, 0.72f, 0.05f), Red = new Color(0.95f, 0.12f, 0.1f);

        [StructLayout(LayoutKind.Sequential)]
        private struct Vertex
        {
            public Vector3 Position;
            public Color32 Colour;
        }

        /// <summary>Faces a chevron arm draws: four sides and the top, each a quad.</summary>
        private const int QuadsPerArm = 5;

        private RacingLine line;
        private SpeedProfile profile;
        private float[] corner;
        private Task<(RacingLine, SpeedProfile, float[])> tuning;
        private Vertex[] vertices;
        private Mesh mesh;
        private MeshRenderer view;
        private int index = -1;

        private const MeshUpdateFlags Quiet = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices
            | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds;

        private void Start()
        {
            CarSpec spec = Car.Spec;
            float limit = AiPilot.LimitFor(DriverPace);
            Use(Line, new SpeedProfile(Line, spec, limit));
            // The line tuned for this car is worked out off the main thread, shared with the AI's, then its speeds.
            Task<RacingLine> tuned = LineTuner.Line(Line.Circuit, spec);
            tuning = LineTuner.Run(() =>
            {
                var p = new SpeedProfile(tuned.Result, spec, limit);
                return (tuned.Result, p, Corners(tuned.Result, p));
            });

            int chevrons = Mathf.CeilToInt(Ahead / Spacing) + 1;
            int quads = chevrons * 2 * QuadsPerArm;
            vertices = new Vertex[quads * 4];
            mesh = new Mesh { name = "Racing line" };
            mesh.MarkDynamic();
            mesh.SetVertexBufferParams(vertices.Length,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4));
            var indices = new int[quads * 6];
            for (int q = 0; q < quads; q++)
            {
                int v = q * 4, i = q * 6;
                indices[i] = v; indices[i + 1] = v + 1; indices[i + 2] = v + 2;
                indices[i + 3] = v; indices[i + 4] = v + 2; indices[i + 5] = v + 3;
            }
            mesh.SetIndexBufferParams(indices.Length, IndexFormat.UInt32);
            mesh.SetIndexBufferData(indices, 0, 0, indices.Length, Quiet);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, indices.Length), Quiet);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);

            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            view = gameObject.AddComponent<MeshRenderer>();
            view.sharedMaterial = new Material(Resources.Load<Shader>("TrackSurface/RacingLine")) { name = "Racing line" };
            view.shadowCastingMode = ShadowCastingMode.Off;
            view.receiveShadows = false;
            view.lightProbeUsage = LightProbeUsage.Off;
            view.reflectionProbeUsage = ReflectionProbeUsage.Off;
            view.enabled = false;
        }

        private void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
            if (view != null) Destroy(view.sharedMaterial);
        }

        private void Use(RacingLine l, SpeedProfile p, float[] corners = null)
        {
            line = l;
            profile = p;
            corner = corners ?? Corners(l, p);
            index = -1;
        }

        /// <summary>
        /// How much of the line "Corners" shows at each sample, 0 to 1: full through braking zones and corners and
        /// <see cref="CornerPad"/> metres either side, fading out beyond.
        /// </summary>
        private static float[] Corners(RacingLine l, SpeedProfile p)
        {
            int n = l.Count;
            float step = l.Circuit.Step;
            var hard = new bool[n];
            for (int k = 0; k < n; k++)
                hard[k] = p.Speed[l.Circuit.Wrap(k + 1)] < p.Speed[k] - 0.02f || Mathf.Abs(l.Curvature[k]) > CornerCurvature;
            // Distance to the nearest hard sample, each way round the lap.
            var near = new float[n];
            for (int k = 0; k < n; k++) near[k] = hard[k] ? 0f : float.MaxValue;
            for (int pass = 0; pass < 2; pass++)
                for (int i = 1; i < 2 * n; i++)
                {
                    int k = i % n, prev = (i - 1) % n;
                    near[k] = Mathf.Min(near[k], near[prev] + step);
                }
            for (int pass = 0; pass < 2; pass++)
                for (int i = 2 * n - 2; i >= 0; i--)
                {
                    int k = i % n, next = (i + 1) % n;
                    near[k] = Mathf.Min(near[k], near[next] + step);
                }
            var shown = new float[n];
            for (int k = 0; k < n; k++) shown[k] = Mathf.Clamp01((CornerPad + CornerFade - near[k]) / CornerFade);
            return shown;
        }

        private void LateUpdate()
        {
            if (tuning != null && tuning.IsCompleted)
            {
                if (tuning.Status == TaskStatus.RanToCompletion) Use(tuning.Result.Item1, tuning.Result.Item2, tuning.Result.Item3);
                else Debug.LogException(tuning.Exception);
                tuning = null;
            }
            RacingLineShown shown = DriveSettings.RacingLine;
            bool on = shown != RacingLineShown.Off && Car != null && !Car.Crashed && (Showing == null || Showing());
            view.enabled = on;
            if (!on) { index = -1; return; }

            index = line.Nearest(Car.transform.position, index);
            Build(shown == RacingLineShown.Corners, Car.Speed);
        }

        /// <summary>Lays the chevrons from the far end in, so each block is drawn over the ones behind it.</summary>
        private void Build(bool cornersOnly, float speed)
        {
            Circuit c = line.Circuit;
            int every = Mathf.Max(1, Mathf.RoundToInt(Spacing / c.Step));
            int chevrons = vertices.Length / (4 * 2 * QuadsPerArm);
            int used = 0;
            for (int n = chevrons - 1; n >= 0; n--)
            {
                float d = n * every * c.Step;
                if (d > Ahead) continue;
                int k = c.Wrap(index + n * every);
                float alpha = Mathf.Clamp01((d - NearStart) / NearFade) * Mathf.Clamp01((Ahead - d) / FarFade);
                if (cornersOnly) alpha *= corner[k];
                if (alpha <= 0.01f) continue;
                Color top = ColourAt(k, speed);
                byte a = (byte)Mathf.RoundToInt(alpha * Opacity * 255f);
                Color32 lit = new Color32((byte)(top.r * 255f), (byte)(top.g * 255f), (byte)(top.b * 255f), a);
                Color32 side = new Color32((byte)(top.r * SideShade * 255f), (byte)(top.g * SideShade * 255f), (byte)(top.b * SideShade * 255f), a);
                Vector3 t = line.Tangent[k];
                var left = new Vector3(-t.z, 0f, t.x);
                Vector3 centre = line.Point[k] + Vector3.up * Lift;
                Vector3 tip = centre + t * Tip;
                for (int s = -1; s <= 1; s += 2)
                {
                    Vector3 back = centre - t * ArmBack + left * (s * ArmOut);
                    Vector3 b0 = tip, b1 = back, b2 = back - t * ArmThick, b3 = tip - t * ArmThick;
                    Vector3 h = Vector3.up * Height;
                    Quad(ref used, b0, b1, b1 + h, b0 + h, side);
                    Quad(ref used, b1, b2, b2 + h, b1 + h, side);
                    Quad(ref used, b2, b3, b3 + h, b2 + h, side);
                    Quad(ref used, b3, b0, b0 + h, b3 + h, side);
                    Quad(ref used, b0 + h, b1 + h, b2 + h, b3 + h, lit);
                }
            }
            // Unused quads collapse to nothing.
            Array.Clear(vertices, used * 4, vertices.Length - used * 4);
            mesh.SetVertexBufferData(vertices, 0, 0, vertices.Length, 0, Quiet);
        }

        private void Quad(ref int used, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color32 colour)
        {
            int v = used++ * 4;
            vertices[v] = new Vertex { Position = a, Colour = colour };
            vertices[v + 1] = new Vertex { Position = b, Colour = colour };
            vertices[v + 2] = new Vertex { Position = c, Colour = colour };
            vertices[v + 3] = new Vertex { Position = d, Colour = colour };
        }

        /// <summary>
        /// Your speed against the line's there: faster means you should be braking by there (red). Where the line slows
        /// or holds its speed, close to it means lift (amber); where it speeds up or runs flat out, green. The red so
        /// starts where braking from your own speed must start, and stays away for a car already slow enough.
        /// </summary>
        private Color ColourAt(int k, float speed)
        {
            float want = profile.Speed[k];
            if (speed > want + RedAbove) return Red;
            if (profile.Speed[line.Circuit.Wrap(k + 1)] > want + 0.01f || want >= Car.Spec.TopSpeed - 0.5f) return Green;
            return speed > want - AmberBelow ? Amber : Green;
        }
    }
}
