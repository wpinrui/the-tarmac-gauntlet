using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Blades of grass round the race camera (Resources/Landscape/Grass.shader), on the verges, the runoff and the fields,
    /// in rings of square tiles, each ring's blades twice as far apart and twice as wide as the last's. The tiles in view
    /// are picked afresh each frame, nearest first. Where the blades stand comes from the circuit drawn from straight above
    /// into a map round the camera (Resources/Landscape/GrassCapture.shader), drawn again as the camera moves on.
    /// </summary>
    public sealed class Grass : IDisposable
    {
        /// <summary>The rings at a setting (<see cref="Visuals.GraphicsOptions.Grass"/>): blade spacing in the nearest and each ring's outer edge, m.</summary>
        private struct Rings
        {
            public float Spacing;
            public float[] Edges;
        }

        private static readonly Rings[] Settings =
        {
            default,
            new Rings { Spacing = 0.08f, Edges = new[] { 8f, 25f } },
            new Rings { Spacing = 0.05f, Edges = new[] { 8f, 20f, 50f } },
        };

        /// <summary>Blades a tile is across.</summary>
        private const int Across = 16;
        /// <summary>Each ring thins to the next's density over this last share of it.</summary>
        private const float Thinning = 0.3f;
        /// <summary>Blades start to shrink into the ground past this share of the last ring.</summary>
        private const float ShrinkFrom = 0.55f;
        private const int MostTiles = 8192;
        /// <summary>The map from above: texels across, and how far past the last ring it reaches, m.</summary>
        private const int MapSize = 1024;
        private const float MapMargin = 14f;
        /// <summary>The map is drawn again when the camera has moved this far from its middle, m.</summary>
        private const float Redraw = 8f;
        /// <summary>The map reaches this far above and below the camera, m.</summary>
        private const float MapAbove = 300f, MapDepth = 600f;

        private static readonly int TilesId = Shader.PropertyToID("_Tiles"), StartId = Shader.PropertyToID("_Start"),
            CaptureId = Shader.PropertyToID("_GrassCapture"), CaptureAtId = Shader.PropertyToID("_GrassCaptureAt"),
            ReachId = Shader.PropertyToID("_GrassReach"), RingsId = Shader.PropertyToID("_GrassRings"),
            CaptureViewId = Shader.PropertyToID("_CaptureAt"), CaptureDepthId = Shader.PropertyToID("_CaptureDepth"),
            IsGrassId = Shader.PropertyToID("_Grass");

        private readonly Material material, captureGrass, captureOther;
        private readonly Material[] grassGround;
        private readonly Mesh tile;
        private readonly GraphicsBuffer buffer;
        private readonly Vector4[] data = new Vector4[MostTiles];
        private readonly List<(float Distance, Vector4 Tile)>[] rings = { new List<(float, Vector4)>(), new List<(float, Vector4)>(), new List<(float, Vector4)>() };
        private readonly MaterialPropertyBlock[] blocks = { new MaterialPropertyBlock(), new MaterialPropertyBlock(), new MaterialPropertyBlock() };
        private readonly Plane[] planes = new Plane[6];
        private readonly CommandBuffer commands = new CommandBuffer { name = "Grass map" };
        private RenderTexture map;
        private Vector2 mapAt;
        private float mapHalf;
        private bool mapDrawn;
        private (Renderer Renderer, bool Grass)[] surfaces;
        private float lowest = float.MaxValue, highest = float.MinValue;

        /// <param name="ground">The ground's materials that are grass (the terrain, runoff and verges): the blades take their maps.</param>
        /// <param name="growth">How tall the grass grows over the fields' map, 1 = 1 m.</param>
        public static Grass Load(Material[] ground, Texture2D growth)
        {
            Shader shader = Resources.Load<Shader>("Landscape/Grass"), capture = Resources.Load<Shader>("Landscape/GrassCapture");
            if (shader == null || capture == null || !SystemInfo.supportsComputeShaders)
            {
                Debug.LogWarning("Landscape: grass shaders missing or unsupported, no grass.");
                return null;
            }
            return new Grass(shader, capture, ground, growth);
        }

        private Grass(Shader shader, Shader capture, Material[] ground, Texture2D growth)
        {
            grassGround = ground;
            Material like = ground[0];
            material = new Material(shader) { name = "Grass", enableInstancing = false };
            foreach (string name in new[] { "_MainTex", "_Grain", "_Mowing" }) material.SetTexture(name, like.GetTexture(name));
            material.SetTexture("_Growth", growth);
            material.SetVector("_Map", like.GetVector("_Map"));
            material.SetColor("_BaseColor", like.GetColor("_BaseColor"));
            foreach (string name in new[] { "_StripeWidth", "_StripeContrast", "_Smoothness" }) material.SetFloat(name, like.GetFloat(name));
            captureGrass = new Material(capture) { name = "Grass map, grass" };
            captureGrass.SetFloat(IsGrassId, 1f);
            captureOther = new Material(capture) { name = "Grass map, other" };
            captureOther.SetFloat(IsGrassId, 0f);
            tile = TileMesh();
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MostTiles, 16);
            foreach (MaterialPropertyBlock b in blocks) b.SetBuffer(TilesId, buffer);
        }

        /// <summary>One tile: <see cref="Across"/> x <see cref="Across"/> blades, each two quads and a tip, roots jittered in their cells.</summary>
        private static Mesh TileMesh()
        {
            int n = Across * Across;
            var verts = new Vector3[n * 7];
            var roots = new Vector2[n * 7];
            var blades = new Vector4[n * 7];
            var tris = new int[n * 15];
            var random = new System.Random(5);
            for (int b = 0; b < n; b++)
            {
                var root = new Vector2((b % Across + 0.1f + 0.8f * (float)random.NextDouble()) / Across, (b / Across + 0.1f + 0.8f * (float)random.NextDouble()) / Across);
                float r0 = (float)random.NextDouble(), r1 = (float)random.NextDouble();
                for (int v = 0; v < 7; v++)
                {
                    // Pairs up the blade, left then right, then the tip.
                    float side = v == 6 ? 0f : v % 2 == 0 ? -1f : 1f;
                    float up = v == 6 ? 1f : (v / 2) / 3f;
                    verts[b * 7 + v] = Vector3.zero;
                    roots[b * 7 + v] = root;
                    blades[b * 7 + v] = new Vector4(side, up, r0, r1);
                }
                int s = b * 7, t = b * 15;
                for (int q = 0; q < 2; q++)
                {
                    int a = s + q * 2;
                    tris[t++] = a; tris[t++] = a + 2; tris[t++] = a + 1;
                    tris[t++] = a + 1; tris[t++] = a + 2; tris[t++] = a + 3;
                }
                tris[t++] = s + 4; tris[t++] = s + 6; tris[t] = s + 5;
            }
            var mesh = new Mesh { name = "Grass tile", indexFormat = IndexFormat.UInt16 };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, roots);
            mesh.SetUVs(1, blades);
            mesh.SetTriangles(tris, 0, false);
            // The blades are placed in the shader; the draw's own bounds stand in for these.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1e4f);
            mesh.UploadMeshData(true);
            return mesh;
        }

        /// <summary>Draws the grass round a camera at a setting, 0 for none. Call once a frame.</summary>
        public void Draw(Camera cam, int setting, bool stripes)
        {
            Rings r = Settings[Mathf.Clamp(setting, 0, Settings.Length - 1)];
            if (r.Edges == null) return;
            Vector3 eye = cam.transform.position;
            float last = r.Edges[r.Edges.Length - 1];
            if (surfaces == null) FindSurfaces();
            if (!mapDrawn || Mathf.Abs(eye.x - mapAt.x) > Redraw || Mathf.Abs(eye.z - mapAt.y) > Redraw || mapHalf < last + MapMargin - Redraw)
                DrawMap(eye, last + MapMargin);
            // Too high above the ground for any blade to show, as from a helicopter.
            if (eye.y - highest > last) return;

            if (stripes) material.EnableKeyword("_GROUND_STRIPES");
            else material.DisableKeyword("_GROUND_STRIPES");
            material.SetTexture(CaptureId, map);
            material.SetVector(CaptureAtId, new Vector4(mapAt.x, mapAt.y, 0.5f / mapHalf, 1f));
            material.SetVector(ReachId, new Vector4(last * ShrinkFrom, last, r.Spacing * 0.4f, Thinning));
            material.SetVector(RingsId, new Vector4(r.Edges[0], r.Edges.Length > 1 ? r.Edges[1] : last, last, r.Edges.Length));

            GeometryUtility.CalculateFrustumPlanes(cam, planes);
            int count = 0;
            for (int ring = 0; ring < r.Edges.Length; ring++)
            {
                List<(float Distance, Vector4 Tile)> list = rings[ring];
                list.Clear();
                float size = r.Spacing * Across * (1 << ring);
                float inner = ring == 0 ? 0f : r.Edges[ring - 1], outer = r.Edges[ring];
                int x0 = Mathf.FloorToInt((eye.x - outer) / size), x1 = Mathf.FloorToInt((eye.x + outer) / size);
                int z0 = Mathf.FloorToInt((eye.z - outer) / size), z1 = Mathf.FloorToInt((eye.z + outer) / size);
                // Blades lean and sway out of their tile a little.
                float pad = 0.6f;
                for (int z = z0; z <= z1; z++)
                    for (int x = x0; x <= x1; x++)
                    {
                        float cx = (x + 0.5f) * size, cz = (z + 0.5f) * size;
                        float dx = Mathf.Abs(eye.x - cx), dz = Mathf.Abs(eye.z - cz), half = size * 0.5f + pad;
                        // Nearest and furthest the tile comes to the camera, across the ground.
                        float near = new Vector2(Mathf.Max(dx - half, 0f), Mathf.Max(dz - half, 0f)).magnitude;
                        float far = new Vector2(dx + half, dz + half).magnitude;
                        if (near >= outer || far < inner) continue;
                        var bounds = new Bounds(new Vector3(cx, (lowest + highest) * 0.5f, cz), new Vector3(size + pad * 2f, highest - lowest + 2f, size + pad * 2f));
                        if (!GeometryUtility.TestPlanesAABB(planes, bounds)) continue;
                        float seed = Hash(x, z, ring);
                        list.Add((near, new Vector4(x * size, z * size, size, ring + seed * 0.999f)));
                    }
                list.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            }

            var starts = new int[r.Edges.Length];
            for (int ring = 0; ring < r.Edges.Length; ring++)
            {
                starts[ring] = count;
                foreach ((float _, Vector4 t) in rings[ring])
                {
                    if (count >= MostTiles) break;
                    data[count++] = t;
                }
            }
            if (count == 0) return;
            buffer.SetData(data, 0, 0, count);
            var drawBounds = new Bounds(new Vector3(eye.x, (lowest + highest) * 0.5f, eye.z), new Vector3(last * 2f + 4f, highest - lowest + 4f, last * 2f + 4f));
            for (int ring = 0; ring < r.Edges.Length; ring++)
            {
                int end = ring + 1 < r.Edges.Length ? starts[ring + 1] : count;
                int n = end - starts[ring];
                if (n <= 0) continue;
                blocks[ring].SetInteger(StartId, starts[ring]);
                var rp = new RenderParams(material)
                {
                    camera = cam,
                    matProps = blocks[ring],
                    worldBounds = drawBounds,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = true,
                    lightProbeUsage = LightProbeUsage.Off,
                    reflectionProbeUsage = ReflectionProbeUsage.Off,
                };
                Graphics.RenderMeshPrimitives(rp, tile, 0, n);
            }
        }

        private static float Hash(int x, int z, int ring)
        {
            unchecked
            {
                uint h = (uint)x * 73856093u ^ (uint)z * 19349663u ^ (uint)ring * 83492791u;
                h ^= h >> 13;
                h *= 0x5bd1e995;
                h ^= h >> 15;
                return (h & 0xffffff) / 16777216f;
            }
        }

        /// <summary>
        /// Every surface the map is drawn from: whatever stands still in the scene but the trees and the far hills, grass
        /// where it takes one of the grass ground's materials. Found once, when the whole circuit and pit buildings stand.
        /// </summary>
        private void FindSurfaces()
        {
            var found = new List<(Renderer, bool)>();
            var grass = new HashSet<Material>(grassGround);
            foreach (MeshRenderer r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!r.gameObject.isStatic || r.sharedMaterial == null) continue;
                string shader = r.sharedMaterial.shader != null ? r.sharedMaterial.shader.name : "";
                if (shader.EndsWith("/Impostor") || r.name == "Hills") continue;
                bool isGrass = grass.Contains(r.sharedMaterial);
                found.Add((r, isGrass));
                if (isGrass)
                {
                    lowest = Mathf.Min(lowest, r.bounds.min.y);
                    highest = Mathf.Max(highest, r.bounds.max.y);
                }
            }
            if (lowest > highest) { lowest = -10f; highest = 10f; }
            surfaces = found.ToArray();
        }

        /// <summary>Draws the circuit from straight above round a point, out to a half width, m.</summary>
        private void DrawMap(Vector3 at, float half)
        {
            if (map == null)
            {
                map = new RenderTexture(MapSize, MapSize, 32, RenderTextureFormat.RGFloat, RenderTextureReadWrite.Linear)
                    { name = "Grass map", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, useMipMap = false };
                map.Create();
            }
            // Snapped to whole texels, so the blades don't creep as the map moves.
            float texel = half * 2f / MapSize;
            mapAt = new Vector2(Mathf.Round(at.x / texel) * texel, Mathf.Round(at.z / texel) * texel);
            mapHalf = half;
            float top = highest + MapAbove * 0.1f;
            commands.Clear();
            commands.SetRenderTarget(map);
            commands.ClearRenderTarget(true, true, Color.clear);
            commands.SetGlobalVector(CaptureViewId, new Vector4(mapAt.x, mapAt.y, 1f / (half * 2f), top));
            commands.SetGlobalFloat(CaptureDepthId, Mathf.Max(top - lowest + 10f, MapDepth * 0.1f));
            var area = new Bounds(new Vector3(mapAt.x, (lowest + highest) * 0.5f, mapAt.y), new Vector3(half * 2f, 1e4f, half * 2f));
            foreach ((Renderer r, bool isGrass) in surfaces)
            {
                if (r == null || !r.bounds.Intersects(area)) continue;
                commands.DrawRenderer(r, isGrass ? captureGrass : captureOther, 0, 0);
            }
            Graphics.ExecuteCommandBuffer(commands);
            mapDrawn = true;
        }

        public void Dispose()
        {
            buffer?.Release();
            commands.Release();
            if (map != null) map.Release();
        }
    }
}
