using System;
using System.Runtime.InteropServices;
using TarmacGauntlet.Visuals;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Rubber the tyres leave on the track where they slide or spin (<see cref="CarController.Skid"/>). Every mark is
    /// one mesh of strips, a ring of <see cref="Most"/> pieces where the newest overwrites the oldest, so the cost never
    /// grows however long the race. Only the changed pieces go to the GPU each frame. Cars off physics leave none.
    /// Follows <see cref="GraphicsOptions.SkidMarks"/>: switched off, the marks are cleared and nothing is drawn.
    /// </summary>
    public sealed class SkidMarks : MonoBehaviour
    {
        [NonSerialized] public CarController[] Cars;

        /// <summary>Pieces of mark kept at once.</summary>
        private const int Most = 8000;
        /// <summary>A mark lies this far over the surface, m.</summary>
        private const float Lift = 0.01f;
        /// <summary>A piece is laid each time the tyre has gone this long a step, m, longer at speed.</summary>
        private const float ShortestPiece = 0.3f, LongestPiece = 1.5f;
        /// <summary>A tyre that jumps further than this between frames, or twice its frame's travel, starts a new mark, m.</summary>
        private const float Break = 4f;
        /// <summary>A slide fainter than this leaves nothing.</summary>
        private const float Faintest = 0.05f;

        [StructLayout(LayoutKind.Sequential)]
        private struct Vertex
        {
            public Vector3 Position;
            public Color32 Colour;
        }

        private struct Trail
        {
            public bool Live;
            public Vector3 Point, Left, Right;
            public byte Alpha;
        }

        private readonly Vertex[] vertices = new Vertex[Most * 4];
        private Trail[] trails;
        private Mesh mesh;
        private MeshRenderer view;
        private int next;
        private int dirtyFrom = int.MaxValue, dirtyTo = -1;

        private const MeshUpdateFlags Quiet = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices
            | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds;

        private void Start()
        {
            mesh = new Mesh { name = "Skid marks" };
            mesh.MarkDynamic();
            mesh.SetVertexBufferParams(vertices.Length,
                new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
                new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4));
            var indices = new int[Most * 6];
            for (int q = 0; q < Most; q++)
            {
                int v = q * 4, i = q * 6;
                // Corners: 0 back left, 1 back right, 2 front left, 3 front right.
                indices[i] = v; indices[i + 1] = v + 2; indices[i + 2] = v + 1;
                indices[i + 3] = v + 1; indices[i + 4] = v + 2; indices[i + 5] = v + 3;
            }
            mesh.SetIndexBufferParams(indices.Length, IndexFormat.UInt32);
            mesh.SetIndexBufferData(indices, 0, 0, indices.Length, Quiet);
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, indices.Length), Quiet);
            mesh.SetVertexBufferData(vertices, 0, 0, vertices.Length, 0, Quiet);
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);

            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            view = gameObject.AddComponent<MeshRenderer>();
            view.sharedMaterial = new Material(Resources.Load<Shader>("TrackSurface/SkidMarks")) { name = "Skid marks" };
            view.shadowCastingMode = ShadowCastingMode.Off;
            view.receiveShadows = false;
            view.lightProbeUsage = LightProbeUsage.Off;
            view.reflectionProbeUsage = ReflectionProbeUsage.Off;

            GraphicsOptions.SkidMarks.Changed += OnOption;
            OnOption(GraphicsOptions.SkidMarks);
        }

        private void OnDestroy()
        {
            GraphicsOptions.SkidMarks.Changed -= OnOption;
            if (mesh != null) Destroy(mesh);
            if (view != null) Destroy(view.sharedMaterial);
        }

        private void OnOption(GraphicsOption option)
        {
            if (option.On) { view.enabled = true; return; }
            view.enabled = false;
            Array.Clear(vertices, 0, vertices.Length);
            mesh.SetVertexBufferData(vertices, 0, 0, vertices.Length, 0, Quiet);
            if (trails != null) Array.Clear(trails, 0, trails.Length);
            next = 0;
            dirtyFrom = int.MaxValue;
            dirtyTo = -1;
        }

        private void LateUpdate()
        {
            if (Cars == null || !view.enabled) return;
            if (trails == null || trails.Length != Cars.Length * 4) trails = new Trail[Cars.Length * 4];
            for (int c = 0; c < Cars.Length; c++)
            {
                CarController car = Cars[c];
                if (car == null) continue;
                float halfWidth = car.Spec.WheelRadius * 0.38f;
                float piece = Mathf.Clamp(car.Speed * 0.025f, ShortestPiece, LongestPiece);
                float breakAt = Mathf.Max(Break, car.Speed * Time.deltaTime * 2f);
                for (int w = 0; w < 4; w++)
                {
                    ref Trail t = ref trails[c * 4 + w];
                    float skid = car.Skid(w, out Vector3 contact, out Vector3 normal);
                    if (skid < Faintest) { t.Live = false; continue; }
                    Vector3 p = contact + normal * Lift;
                    byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(skid) * 255f);
                    if (t.Live && (p - t.Point).sqrMagnitude > breakAt * breakAt) t.Live = false;
                    if (!t.Live)
                    {
                        // A mark starts faded in, across the car's heading.
                        Vector3 across = Vector3.Cross(normal, car.transform.forward).normalized * halfWidth;
                        t = new Trail { Live = true, Point = p, Left = p - across, Right = p + across, Alpha = 0 };
                        continue;
                    }
                    Vector3 step = p - t.Point;
                    if (step.sqrMagnitude < piece * piece) continue;
                    Vector3 side = Vector3.Cross(normal, step).normalized * halfWidth;
                    Lay(t.Left, t.Right, p - side, p + side, t.Alpha, alpha);
                    t.Point = p;
                    t.Left = p - side;
                    t.Right = p + side;
                    t.Alpha = alpha;
                }
            }
            if (dirtyTo < 0) return;
            mesh.SetVertexBufferData(vertices, dirtyFrom * 4, dirtyFrom * 4, (dirtyTo - dirtyFrom + 1) * 4, 0, Quiet);
            dirtyFrom = int.MaxValue;
            dirtyTo = -1;
        }

        /// <summary>One piece of mark from a back edge to a front edge, overwriting the oldest piece.</summary>
        private void Lay(Vector3 backLeft, Vector3 backRight, Vector3 frontLeft, Vector3 frontRight, byte back, byte front)
        {
            int v = next * 4;
            vertices[v] = new Vertex { Position = backLeft, Colour = new Color32(0, 0, 0, back) };
            vertices[v + 1] = new Vertex { Position = backRight, Colour = new Color32(0, 0, 0, back) };
            vertices[v + 2] = new Vertex { Position = frontLeft, Colour = new Color32(0, 0, 0, front) };
            vertices[v + 3] = new Vertex { Position = frontRight, Colour = new Color32(0, 0, 0, front) };
            // Wrapping round the ring sends the whole of it this frame, which happens once every Most pieces.
            dirtyFrom = Mathf.Min(dirtyFrom, next);
            dirtyTo = Mathf.Max(dirtyTo, next);
            next = (next + 1) % Most;
        }
    }
}
