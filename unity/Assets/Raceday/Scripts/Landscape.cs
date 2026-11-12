using System.Collections.Generic;
using TarmacGauntlet.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Builds the country round the circuit from <see cref="LandscapePlan"/> and <see cref="Horizon"/>:
    /// the fields' colours on the ground, the trees and hedges, and the hills out to the haze.
    /// Trees are impostors (Resources/Landscape/Impostor.shader) in square chunks, each drawn twice: depth alone first,
    /// then lit, so only the tree in front of each pixel is lit. Each has a thinner far version past about 900 m that
    /// casts no shadow. Nothing here collides.
    /// </summary>
    public static class Landscape
    {
        private static readonly string[] Kinds = { "oak", "beech", "birch", "pine_a", "pine_b", "spruce_a", "spruce_b", "bush" };
        /// <summary>Chunks switch to the far version below this share of the screen's height.</summary>
        private const float FarAt = 0.25f;

        public static void Build(Circuit circuit, CircuitBuilder builder, IReadOnlyList<StandFootprint> stands, CircuitMaterials materials, LandscapeLook look)
        {
            var root = new GameObject("Landscape");
            var plan = new LandscapePlan(circuit, builder.RunoffReach, look, stands);
            Fields(circuit, plan, materials);
            Trees(plan, root.transform);
            Hills(circuit, look, root.transform);
        }

        // ---------------------------------------------------------------- ground

        /// <summary>
        /// Paints the fields' colours over the ground, with a grass grain over them and stripes mown along the track
        /// (Resources/Landscape/Ground.shader). The grass runoff and verges take the same shader, so the mown grass runs on
        /// unbroken from the road edge out to the fields.
        /// </summary>
        private static void Fields(Circuit circuit, LandscapePlan plan, CircuitMaterials materials)
        {
            GroundData g = circuit.Data.terrain;
            float cell = g.spacing / LandscapePlan.MapPerCell;
            var px = new Color32[plan.MapX * plan.MapZ];
            var mowing = new ushort[px.Length * 4];
            for (int i = 0; i < px.Length; i++)
            {
                int x = i % plan.MapX, z = i / plan.MapX;
                Color col = LandscapeLooks.Field(plan.Map[i], g.originX + x * cell, g.originZ + z * cell) * (0.94f + 0.12f * CloudField.Hash(x, z, 91));
                px[i] = col;
                float mown = plan.Mowing(i, out float fromRoad, out float alongX, out float alongZ);
                mowing[i * 4] = Mathf.FloatToHalf(fromRoad);
                mowing[i * 4 + 1] = Mathf.FloatToHalf(mown);
                mowing[i * 4 + 2] = Mathf.FloatToHalf(alongX);
                mowing[i * 4 + 3] = Mathf.FloatToHalf(alongZ);
            }
            var map = new Texture2D(plan.MapX, plan.MapZ, TextureFormat.RGBA32, true)
                { name = "Fields", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            map.SetPixels32(px);
            map.Apply(true, true);
            var mow = new Texture2D(plan.MapX, plan.MapZ, TextureFormat.RGBAHalf, true, true)
                { name = "Mowing", wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            mow.SetPixelData(mowing, 0);
            mow.Apply(true, true);

            Shader ground = Resources.Load<Shader>("Landscape/Ground");
            if (ground == null) return;
            // The map's texel centres sit on the map points, from the ground's origin.
            var rect = new Vector4(g.originX - cell * 0.5f, g.originZ - cell * 0.5f, 1f / (plan.MapX * cell), 1f / (plan.MapZ * cell));
            Texture2D grain = Grain();
            foreach (Material m in new[] { materials.Terrain, materials.Grass, materials.Verge })
            {
                m.shader = ground;
                m.color = Color.white;
                m.mainTexture = map;
                m.SetTexture("_Grain", grain);
                m.SetTexture("_Mowing", mow);
                m.SetVector("_Map", rect);
                m.SetFloat("_Smoothness", 0.05f);
            }
            materials.ShowOptions();
        }

        /// <summary>Grain about mid grey, so it darkens and lightens the field colour evenly.</summary>
        private static Texture2D Grain()
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = "Ground grain", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            var px = new Color32[size * size];
            float[] soft = CloudField.Fbm(size, 4, 3, 17);
            for (int i = 0; i < px.Length; i++)
            {
                byte v = (byte)Mathf.Clamp(128f + (soft[i] - 0.5f) * 40f + (CloudField.Hash(i % size, i / size, 19) - 0.5f) * 26f, 0f, 255f);
                px[i] = new Color32(v, v, v, 255);
            }
            tex.SetPixels32(px);
            tex.Apply(true, true);
            return tex;
        }

        // ---------------------------------------------------------------- trees

        private static void Trees(LandscapePlan plan, Transform root)
        {
            Material[] materials = TreeMaterials();
            if (materials == null) return;
            foreach (TreeChunk chunk in plan.Chunks())
            {
                var go = new GameObject("Trees") { isStatic = true };
                go.transform.SetParent(root, false);
                Renderer near = Part(go.transform, "Near", chunk.Near, materials, true);
                Renderer distant = Part(go.transform, "Far", chunk.Far, materials, false);
                LODGroup lod = go.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(FarAt, new[] { near }), new LOD(0.002f, new[] { distant }) });
                lod.RecalculateBounds();
            }
        }

        private static Renderer Part(Transform parent, string name, List<TreeSpot> trees, Material[] materials, bool shadows)
        {
            var go = new GameObject(name) { isStatic = true };
            go.transform.SetParent(parent, false);
            int n = trees.Count;
            var verts = new List<Vector3>(n * 4);
            var corners = new List<Vector2>(n * 4);
            var shape = new List<Vector4>(n * 4);
            var squash = new List<Vector2>(n * 4);
            var colours = new List<Color32>(n * 4);
            var tris = new List<int>(n * 6);
            Vector3 lo = Vector3.one * float.MaxValue, hi = Vector3.one * float.MinValue;
            foreach (TreeSpot t in trees)
            {
                int k = (int)t.Kind;
                float radius = LandscapePlan.ModelRadius[k] * t.Scale;
                var s = new Vector4(radius, LandscapePlan.CentreAbove[k] * t.Scale * t.Squash, t.Yaw, k);
                // Halved, so the tint can lighten as well as darken; the shader doubles it.
                Color32 tint = LandscapeLooks.Leaves(t.Kind, t.Hue) * (t.Shade * 0.5f);
                int b = verts.Count;
                for (int c = 0; c < 4; c++)
                {
                    verts.Add(t.Base);
                    corners.Add(new Vector2(c == 1 || c == 2 ? 1f : -1f, c >= 2 ? 1f : -1f));
                    shape.Add(s);
                    squash.Add(new Vector2(t.Squash, 0f));
                    colours.Add(tint);
                }
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
                tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
                lo = Vector3.Min(lo, t.Base - Vector3.one * radius);
                hi = Vector3.Max(hi, t.Base + Vector3.one * radius + Vector3.up * radius);
            }
            var mesh = new Mesh { name = "Trees " + name };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, corners);
            mesh.SetUVs(1, shape);
            mesh.SetUVs(2, squash);
            mesh.SetColors(colours);
            // The same quads twice, one submesh for each material: depth, then lit.
            mesh.subMeshCount = 2;
            mesh.SetTriangles(tris, 0, false);
            mesh.SetTriangles(tris, 1, false);
            // The vertices all sit at the trees' feet; the quads reach out from there in the shader.
            mesh.bounds = new Bounds((lo + hi) * 0.5f, hi - lo);
            mesh.UploadMeshData(true);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = materials;
            r.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return r;
        }

        /// <summary>Queue of the trees' depth material: after the ground and other opaque things, before any lit tree.</summary>
        private const int DepthQueue = (int)RenderQueue.AlphaTest - 1;

        /// <summary>
        /// The impostor materials, its atlases gathered into texture arrays: depth alone, drawn for every tree before
        /// any is lit, then lit. Null if the shader or atlases are missing.
        /// </summary>
        private static Material[] TreeMaterials()
        {
            Shader shader = Resources.Load<Shader>("Landscape/Impostor");
            Texture2DArray colour = Layers("_colour"), normals = Layers("_normal");
            if (shader == null || colour == null || normals == null)
            {
                Debug.LogWarning("Landscape: impostor shader or atlases missing, no trees.");
                return null;
            }
            var lit = new Material(shader) { name = "Trees", enableInstancing = false };
            lit.SetTexture("_Atlas", colour);
            lit.SetTexture("_Normals", normals);
            lit.SetTexture("_Crop", Crop());
            lit.SetFloat("_Cutoff", 0.5f);
            lit.SetFloat("_Frames", Frames);
            var depth = new Material(lit) { name = "Trees depth", renderQueue = DepthQueue };
            depth.SetShaderPassEnabled("UniversalForward", false);
            depth.SetShaderPassEnabled("ShadowCaster", false);
            depth.SetShaderPassEnabled("DepthOnly", false);
            lit.SetShaderPassEnabled("SRPDefaultUnlit", false);
            return new[] { depth, lit };
        }

        private const int Frames = 8;

        /// <summary>
        /// The leaves' bounds in each frame of each kind's atlas (Resources/Landscape/crop.bytes, from tools/landscape/frames.py),
        /// a row a kind: left, bottom, right, top, 0 to 1 across the frame. Whole frames if the file is missing.
        /// </summary>
        private static Texture2D Crop()
        {
            int across = Frames * Frames;
            var bytes = new byte[across * Kinds.Length * 4];
            TextAsset file = Resources.Load<TextAsset>("Landscape/crop");
            if (file != null && file.bytes.Length == bytes.Length) bytes = file.bytes;
            else
            {
                Debug.LogWarning("Landscape: crop.bytes missing or the wrong size, trees drawn on whole frames.");
                for (int i = 0; i < bytes.Length; i += 4) { bytes[i + 2] = 255; bytes[i + 3] = 255; }
            }
            var tex = new Texture2D(across, Kinds.Length, TextureFormat.RGBA32, false, true)
                { name = "Tree crop", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixelData(bytes, 0);
            tex.Apply(false, true);
            return tex;
        }

        private static Texture2DArray Layers(string suffix)
        {
            var layers = new Texture2D[Kinds.Length];
            for (int i = 0; i < Kinds.Length; i++)
            {
                layers[i] = Resources.Load<Texture2D>("Landscape/" + Kinds[i] + suffix);
                if (layers[i] == null) return null;
            }
            Texture2D first = layers[0];
            var array = new Texture2DArray(first.width, first.height, layers.Length, first.format, first.mipmapCount > 1, !first.isDataSRGB)
            {
                name = "Trees" + suffix, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4,
            };
            // Copied on the graphics card: the imported textures are compressed and not readable.
            for (int i = 0; i < layers.Length; i++) Graphics.CopyTexture(layers[i], 0, array, i);
            return array;
        }

        // ---------------------------------------------------------------- hills

        private static void Hills(Circuit circuit, LandscapeLook look, Transform root)
        {
            var h = new Horizon(circuit, look);
            var mesh = new Mesh { name = "Hills", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(h.Vertices);
            mesh.SetUVs(0, h.Uvs);
            mesh.SetTriangles(h.Triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var px = new Color32[h.Map.Length];
            for (int i = 0; i < px.Length; i++)
                px[i] = LandscapeLooks.Ground(h.Map[i]) * (0.92f + 0.16f * CloudField.Hash(i % Horizon.MapAround, i / Horizon.MapAround, 93));
            var map = new Texture2D(Horizon.MapAround, Horizon.MapOut, TextureFormat.RGBA32, true)
                { name = "Hills", wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            map.SetPixels32(px);
            map.Apply(true, true);
            var m = new Material(Lit.Shader) { name = "Hills", mainTexture = map };
            m.SetFloat("_Smoothness", 0.02f);

            var go = new GameObject("Hills") { isStatic = true };
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = m;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }
    }
}
