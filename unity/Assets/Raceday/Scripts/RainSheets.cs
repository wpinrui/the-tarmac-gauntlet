using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The rain beyond the particles round the camera: a few open cylinders round it, one inside the other, each the rain
    /// in a slab of air as thin streaks of a raindrop's real width scrolling down it. They are drawn behind whatever stands
    /// nearer, so a car or a wall in front hides the rain beyond it. A streak narrower than a pixel is drawn one pixel wide
    /// and as much fainter, so a wide shot sees the far rain as a haze and a long lens picks out its streaks. They hold
    /// nothing from frame to frame, so the rain is all there on the first frame after a cut. The streaks are fixed in the
    /// world and fall through it, so they slide past a moving camera as rain does.
    /// </summary>
    internal sealed class RainSheets
    {
        /// <summary>Each cylinder's radius at each quality level, m: more of them to draw the rain's depth at the higher levels.</summary>
        private static readonly float[][] RadiiAt =
        {
            new[] { 24f, 80f },
            new[] { 20f, 48f, 115f },
            new[] { 18f, 34f, 64f, 120f },
            new[] { 17f, 26f, 40f, 62f, 96f, 150f },
        };
        /// <summary>Where the slabs start, m: inside the particles' reach, so the two overlap.</summary>
        private const float Inner = 14f;
        /// <summary>A streak's width and length, m: a drop's width, and how far it falls while the shutter is open.</summary>
        public const float Width = 0.0035f, Length = 0.45f;
        /// <summary>The height of a streak's cell, m: a few streaks' lengths, so few overlap.</summary>
        private const float CellHeight = 3.2f * Length;
        /// <summary>Wraps the scroll to keep it precise, in whole cells, as the shader wraps its rows.</summary>
        private const float Wrap = 4096f * CellHeight;
        private const int Sides = 64;

        private readonly Mesh mesh;
        private readonly Material[][] materials;
        private float fall;
        private static readonly int TintId = Shader.PropertyToID("_Tint"), ShareId = Shader.PropertyToID("_Share"),
            CellId = Shader.PropertyToID("_Cell"), FallId = Shader.PropertyToID("_Fall"), SlantId = Shader.PropertyToID("_Slant"),
            StreakId = Shader.PropertyToID("_Streak");

        public RainSheets()
        {
            mesh = Cylinder();
            Shader shader = Resources.Load<Shader>("Weather/RainSheet");
            materials = new Material[RadiiAt.Length][];
            for (int level = 0; level < RadiiAt.Length; level++)
            {
                float[] radii = RadiiAt[level];
                materials[level] = new Material[radii.Length];
                for (int i = 0; i < radii.Length; i++)
                {
                    // Far sheets first, so the nearer ones blend over them.
                    var m = new Material(shader) { name = $"Rain sheet {radii[i]:0} m", renderQueue = 2990 - i };
                    m.SetVector(StreakId, new Vector4(Width, Length, 0f, 0f));
                    materials[level][i] = m;
                }
            }
        }

        /// <summary>
        /// Draws the rain round the camera this frame.
        /// </summary>
        /// <param name="share">How much of the most rain is falling, 0 to 1.</param>
        /// <param name="mostPerCubicMetre">Drops in a cubic metre of air at the most rain, as the particles have it.</param>
        /// <param name="wind">How far the rain drifts sideways for each metre it falls, x and z.</param>
        public void Draw(Camera cam, int level, float share, float mostPerCubicMetre, float dropSpeed, Vector2 wind, Color tint)
        {
            fall = Mathf.Repeat(fall + dropSpeed * Time.deltaTime, Wrap);
            if (share <= 0f) return;
            float[] radii = RadiiAt[Mathf.Clamp(level, 0, RadiiAt.Length - 1)];
            Material[] mats = materials[Mathf.Clamp(level, 0, materials.Length - 1)];
            Vector3 eye = cam.transform.position;
            for (int i = 0; i < radii.Length; i++)
            {
                float r = radii[i];
                float from = i == 0 ? Inner : Mathf.Sqrt(radii[i - 1] * r);
                float to = i == radii.Length - 1 ? r * 1.6f : Mathf.Sqrt(r * radii[i + 1]);
                // The drops in the slab, laid on the sheet: one cell for each at the most rain, and each cell holds its drop
                // as often as the rain is heavy, so the streaks come and go in place as it changes.
                float perSquareMetre = mostPerCubicMetre * (to - from);
                float cellWidth = 1f / (perSquareMetre * CellHeight);
                Material m = mats[i];
                m.SetColor(TintId, tint);
                m.SetFloat(ShareId, share);
                m.SetVector(CellId, new Vector4(cellWidth, CellHeight, 0f, 0f));
                m.SetFloat(FallId, fall);
                m.SetVector(SlantId, wind);
                // Tall enough to fill the view looking up or down from a camera on a tower.
                float half = Mathf.Max(r * 1.2f, 30f);
                var rp = new RenderParams(m)
                {
                    camera = cam,
                    worldBounds = new Bounds(eye, new Vector3(r * 2f, half * 2f, r * 2f)),
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false,
                    lightProbeUsage = LightProbeUsage.Off,
                    reflectionProbeUsage = ReflectionProbeUsage.Off,
                };
                Graphics.RenderMesh(rp, mesh, 0, Matrix4x4.TRS(eye, Quaternion.identity, new Vector3(r, half, r)));
            }
        }

        /// <summary>An open cylinder of radius 1 from -1 to 1 high.</summary>
        private static Mesh Cylinder()
        {
            var vertices = new Vector3[(Sides + 1) * 2];
            var triangles = new int[Sides * 6];
            for (int s = 0; s <= Sides; s++)
            {
                float a = s * Mathf.PI * 2f / Sides;
                var round = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                vertices[s * 2] = round + Vector3.down;
                vertices[s * 2 + 1] = round + Vector3.up;
                if (s == Sides) break;
                int t = s * 6, v = s * 2;
                triangles[t] = v;
                triangles[t + 1] = v + 1;
                triangles[t + 2] = v + 2;
                triangles[t + 3] = v + 2;
                triangles[t + 4] = v + 1;
                triangles[t + 5] = v + 3;
            }
            var mesh = new Mesh { name = "Rain sheet", vertices = vertices, triangles = triangles };
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
