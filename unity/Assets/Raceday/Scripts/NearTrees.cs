using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The trees nearest the race camera drawn as real meshes (Resources/Landscape/NearTree.shader), from the same models as
    /// their impostors (tools/landscape/trees3d.js): a fuller level of detail close in, a lighter one further out, and past
    /// that the impostor, each handed to the next with a screen dither (Foliage.hlsl). Every tree of a kind and level is one
    /// instanced draw, its trees picked afresh each frame from a grid round the camera.
    /// </summary>
    public sealed class NearTrees : IDisposable
    {
        /// <summary>How far out the meshes reach at a setting (<see cref="Visuals.GraphicsOptions.NearTrees"/>), m to a tree's middle.</summary>
        private struct Reach
        {
            /// <summary>The lighter level of detail takes over across a band this wide about LodAt.</summary>
            public float LodAt, LodBand;
            /// <summary>The impostor fades in from Start to End.</summary>
            public float Start, End;
        }

        private static readonly Reach[] Reaches =
        {
            default,
            new Reach { LodAt = 20f, LodBand = 6f, Start = 45f, End = 60f },
            new Reach { LodAt = 40f, LodBand = 10f, Start = 85f, End = 105f },
        };

        private const int Kinds = 8, Lods = 2;
        /// <summary>Most trees drawn in a frame, over every kind and level.</summary>
        private const int MostTrees = 4096;
        /// <summary>The grid the trees are found in, m.</summary>
        private const float Cell = 64f;
        private const int BarkQueue = (int)RenderQueue.Geometry, LeafDepthQueue = (int)RenderQueue.AlphaTest - 1, LeafQueue = (int)RenderQueue.AlphaTest;

        private static readonly int TreesId = Shader.PropertyToID("_Trees"), StartId = Shader.PropertyToID("_Start"), KindId = Shader.PropertyToID("_Kind");
        private static readonly int ColourId = Shader.PropertyToID("_Colour"), BarkColourId = Shader.PropertyToID("_BarkColour");
        private static readonly int NearAtId = Shader.PropertyToID("_NearTreesAt"), NearFadeId = Shader.PropertyToID("_NearTreeFade");

        private readonly Mesh[,] meshes = new Mesh[Kinds, Lods];
        private readonly Material bark, leafDepth, leaves;
        private readonly Vector3[] centres;
        private readonly Vector4[] footScale, turn, tint;
        private readonly TreeKind[] kinds;
        private readonly Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
        private readonly GraphicsBuffer buffer;
        private readonly Vector4[] data = new Vector4[MostTrees * 3];
        private readonly List<(int Tree, float Share)>[,] groups = new List<(int, float)>[Kinds, Lods];
        private readonly MaterialPropertyBlock[,] blocks = new MaterialPropertyBlock[Kinds, Lods];
        private readonly int[,] starts = new int[Kinds, Lods];
        /// <summary>What each level's leaves and bark are multiplied by, so that lit they match the impostor (trees3d-view.js --calibrate).</summary>
        private readonly Vector4[,] colours = new Vector4[Kinds, Lods], barkColours = new Vector4[Kinds, Lods];

        /// <summary>The meshes and materials for the plan's trees, or null if the shader or the baked trees are missing.</summary>
        public static NearTrees Load(IReadOnlyList<TreeSpot> trees)
        {
            Shader shader = Resources.Load<Shader>("Landscape/NearTree");
            TextAsset file = Resources.Load<TextAsset>("Landscape/Trees3D/trees3d");
            if (shader == null || file == null)
            {
                Debug.LogWarning("Landscape: near tree shader or meshes missing, impostors only.");
                return null;
            }
            try
            {
                return new NearTrees(trees, shader, file.bytes);
            }
            catch (Exception e)
            {
                Debug.LogWarning("Landscape: near trees could not be built, impostors only. " + e.Message);
                return null;
            }
        }

        private NearTrees(IReadOnlyList<TreeSpot> trees, Shader shader, byte[] bytes)
        {
            int barkLayers = ReadMeshes(bytes);
            Texture2DArray barkArray = Layers("bark_", barkLayers, i => i.ToString("00"), TextureWrapMode.Repeat);
            Texture2DArray leafArray = Layers("leaves_", Kinds, i => Landscape.KindNames[i], TextureWrapMode.Clamp);
            if (barkArray == null || leafArray == null) throw new InvalidOperationException("textures missing");

            bark = new Material(shader) { name = "Near trees bark", renderQueue = BarkQueue, enableInstancing = false };
            bark.SetTexture("_Bark", barkArray);
            bark.SetFloat("_ZWrite", 1f);
            bark.SetFloat("_Cull", (float)CullMode.Back);
            bark.SetShaderPassEnabled("SRPDefaultUnlit", false);
            leaves = new Material(shader) { name = "Near trees leaves", renderQueue = LeafQueue, enableInstancing = false };
            leaves.EnableKeyword("_LEAVES");
            leaves.SetTexture("_Leaves", leafArray);
            leaves.SetFloat("_Cutoff", 0.5f);
            leaves.SetFloat("_ZWrite", 0f);
            leaves.SetFloat("_Cull", (float)CullMode.Off);
            leafDepth = new Material(leaves) { name = "Near trees leaves depth", renderQueue = LeafDepthQueue };
            leafDepth.SetShaderPassEnabled("UniversalForward", false);
            leafDepth.SetShaderPassEnabled("ShadowCaster", false);
            leafDepth.SetShaderPassEnabled("DepthOnly", false);
            leaves.SetShaderPassEnabled("SRPDefaultUnlit", false);

            int n = trees.Count;
            centres = new Vector3[n];
            footScale = new Vector4[n];
            turn = new Vector4[n];
            tint = new Vector4[n];
            kinds = new TreeKind[n];
            for (int i = 0; i < n; i++)
            {
                TreeSpot t = trees[i];
                int k = (int)t.Kind;
                // The impostor's middle (Landscape.Part), so both measure the same distance.
                centres[i] = t.Base + Vector3.up * (LandscapePlan.CentreAbove[k] * t.Scale * t.Squash);
                footScale[i] = new Vector4(t.Base.x, t.Base.y, t.Base.z, t.Scale);
                turn[i] = new Vector4(t.Yaw, t.Squash, 1f, 0f);
                // As the impostor's vertex colour, at twice its value: the shader scales it for Linear as the impostor does.
                Color c = LandscapeLooks.Leaves(t.Kind, t.Hue) * t.Shade;
                tint[i] = new Vector4(c.r, c.g, c.b, 1f);
                kinds[i] = t.Kind;
                long key = Key(t.Base.x, t.Base.z);
                if (!grid.TryGetValue(key, out List<int> list)) grid[key] = list = new List<int>();
                list.Add(i);
            }
            buffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, data.Length, 16);
            for (int k = 0; k < Kinds; k++)
                for (int l = 0; l < Lods; l++)
                {
                    groups[k, l] = new List<(int, float)>();
                    blocks[k, l] = new MaterialPropertyBlock();
                    blocks[k, l].SetBuffer(TreesId, buffer);
                    blocks[k, l].SetFloat(KindId, k);
                    blocks[k, l].SetVector(ColourId, colours[k, l]);
                    blocks[k, l].SetVector(BarkColourId, barkColours[k, l]);
                }
        }

        private static long Key(float x, float z) => ((long)Mathf.FloorToInt(x / Cell) << 32) ^ (uint)Mathf.FloorToInt(z / Cell);

        /// <summary>Reads trees3d.bytes (tools/landscape/trees3d.js) into the meshes. Returns the number of bark layers.</summary>
        private int ReadMeshes(byte[] bytes)
        {
            int at = 0;
            int Int() { int v = BitConverter.ToInt32(bytes, at); at += 4; return v; }
            float[] Floats(int count)
            {
                var a = new float[count];
                Buffer.BlockCopy(bytes, at, a, 0, count * 4);
                at += count * 4;
                return a;
            }
            int[] Ints(int count)
            {
                var a = new int[count];
                Buffer.BlockCopy(bytes, at, a, 0, count * 4);
                at += count * 4;
                return a;
            }
            if (bytes.Length < 20 || bytes[0] != 'T' || bytes[1] != 'G' || bytes[2] != '3' || bytes[3] != 'D') throw new InvalidOperationException("not a trees3d file");
            at = 4;
            if (Int() != 3) throw new InvalidOperationException("wrong trees3d version");
            int kindCount = Int(), lodCount = Int(), layers = Int();
            if (kindCount != Kinds || lodCount != Lods) throw new InvalidOperationException("wrong number of kinds or levels");
            for (int k = 0; k < Kinds; k++)
                for (int l = 0; l < Lods; l++)
                {
                    int nv = Int(), nb = Int(), nl = Int();
                    float[] colour = Floats(3), barkColour = Floats(3);
                    colours[k, l] = new Vector4(colour[0], colour[1], colour[2], 1f);
                    barkColours[k, l] = new Vector4(barkColour[0], barkColour[1], barkColour[2], 1f);
                    float[] p = Floats(nv * 3), nrm = Floats(nv * 3), uv = Floats(nv * 2), extra = Floats(nv * 3);
                    int[] barkIdx = Ints(nb), leafIdx = Ints(nl);
                    var verts = new Vector3[nv];
                    var normals = new Vector3[nv];
                    var uvs = new Vector2[nv];
                    var extras = new Vector3[nv];
                    for (int i = 0; i < nv; i++)
                    {
                        verts[i] = new Vector3(p[i * 3], p[i * 3 + 1], p[i * 3 + 2]);
                        normals[i] = new Vector3(nrm[i * 3], nrm[i * 3 + 1], nrm[i * 3 + 2]);
                        uvs[i] = new Vector2(uv[i * 2], uv[i * 2 + 1]);
                        extras[i] = new Vector3(extra[i * 3], extra[i * 3 + 1], extra[i * 3 + 2]);
                    }
                    var mesh = new Mesh { name = $"Near tree {Landscape.KindNames[k]} {l}", indexFormat = nv > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
                    mesh.SetVertices(verts);
                    mesh.SetNormals(normals);
                    mesh.SetUVs(0, uvs);
                    mesh.SetUVs(1, extras);
                    mesh.subMeshCount = 2;
                    mesh.SetTriangles(barkIdx, 0, false);
                    mesh.SetTriangles(leafIdx, 1, false);
                    mesh.RecalculateBounds();
                    mesh.UploadMeshData(true);
                    meshes[k, l] = mesh;
                }
            return layers;
        }

        private static Texture2DArray Layers(string prefix, int count, Func<int, string> name, TextureWrapMode wrap)
        {
            var layers = new Texture2D[count];
            for (int i = 0; i < count; i++)
            {
                layers[i] = Resources.Load<Texture2D>("Landscape/Trees3D/" + prefix + name(i));
                if (layers[i] == null) return null;
            }
            Texture2D first = layers[0];
            var array = new Texture2DArray(first.width, first.height, count, first.format, first.mipmapCount > 1, !first.isDataSRGB)
                { name = "Near trees " + prefix, wrapMode = wrap, filterMode = FilterMode.Trilinear, anisoLevel = 4 };
            // Copied on the graphics card: the imported textures are compressed and not readable.
            for (int i = 0; i < count; i++) Graphics.CopyTexture(layers[i], 0, array, i);
            return array;
        }

        /// <summary>
        /// Draws the trees near a point at a setting, 0 for none, and tells the impostors where to fade in. Call once a frame.
        /// </summary>
        public void Draw(Vector3 origin, int setting)
        {
            Reach r = Reaches[Mathf.Clamp(setting, 0, Reaches.Length - 1)];
            if (r.End <= 0f)
            {
                Shader.SetGlobalVector(NearFadeId, Vector4.zero);
                return;
            }
            Shader.SetGlobalVector(NearAtId, origin);
            // w steps the dither on each frame, so the upscaler's history blends the hand-over smoothly, not as a stipple.
            Shader.SetGlobalVector(NearFadeId, new Vector4(r.Start, 1f / (r.End - r.Start), 1f, Time.frameCount % 64));

            foreach (List<(int, float)> g in groups) g.Clear();
            float lod0 = r.LodAt - r.LodBand * 0.5f;
            // The tallest tree's middle stands well within a cell of its foot.
            float search = r.End + 20f;
            int cx0 = Mathf.FloorToInt((origin.x - search) / Cell), cx1 = Mathf.FloorToInt((origin.x + search) / Cell);
            int cz0 = Mathf.FloorToInt((origin.z - search) / Cell), cz1 = Mathf.FloorToInt((origin.z + search) / Cell);
            int count = 0;
            for (int cz = cz0; cz <= cz1 && count < MostTrees; cz++)
                for (int cx = cx0; cx <= cx1 && count < MostTrees; cx++)
                {
                    if (!grid.TryGetValue(((long)cx << 32) ^ (uint)cz, out List<int> list)) continue;
                    foreach (int i in list)
                    {
                        float d = Vector3.Distance(centres[i], origin);
                        if (d >= r.End) continue;
                        int k = (int)kinds[i];
                        // The impostor's share past Start; the mesh shows the rest.
                        float impostor = Mathf.Clamp01((d - r.Start) / (r.End - r.Start));
                        float lod = Mathf.Clamp01((d - lod0) / r.LodBand);
                        if (lod < 1f && count < MostTrees)
                        {
                            groups[k, 0].Add((i, 1f - lod));
                            count++;
                        }
                        if (lod > 0f && count < MostTrees)
                        {
                            // Where it is taking over from the fuller level, the pixels that one leaves; where the impostor is
                            // taking over from it, the pixels the impostor leaves.
                            groups[k, 1].Add((i, impostor > 0f ? 1f - impostor : -lod));
                            count++;
                        }
                    }
                }
            if (count == 0) return;

            int at = 0;
            for (int k = 0; k < Kinds; k++)
                for (int l = 0; l < Lods; l++)
                {
                    starts[k, l] = at;
                    foreach ((int i, float share) in groups[k, l])
                    {
                        data[at * 3] = footScale[i];
                        Vector4 t = turn[i];
                        t.z = share;
                        data[at * 3 + 1] = t;
                        data[at * 3 + 2] = tint[i];
                        at++;
                    }
                }
            buffer.SetData(data, 0, 0, at * 3);

            var bounds = new Bounds(origin, Vector3.one * (search * 2f + 40f));
            for (int k = 0; k < Kinds; k++)
                for (int l = 0; l < Lods; l++)
                {
                    int n = groups[k, l].Count;
                    if (n == 0) continue;
                    MaterialPropertyBlock block = blocks[k, l];
                    block.SetInteger(StartId, starts[k, l]);
                    Mesh mesh = meshes[k, l];
                    if (mesh.GetIndexCount(0) > 0) Graphics.RenderMeshPrimitives(Params(bark, block, bounds, ShadowCastingMode.On), mesh, 0, n);
                    Graphics.RenderMeshPrimitives(Params(leafDepth, block, bounds, ShadowCastingMode.Off), mesh, 1, n);
                    Graphics.RenderMeshPrimitives(Params(leaves, block, bounds, ShadowCastingMode.On), mesh, 1, n);
                }
        }

        private static RenderParams Params(Material material, MaterialPropertyBlock block, Bounds bounds, ShadowCastingMode shadows) =>
            new RenderParams(material)
            {
                matProps = block,
                worldBounds = bounds,
                shadowCastingMode = shadows,
                receiveShadows = true,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
            };

        public void Dispose()
        {
            buffer?.Release();
            Shader.SetGlobalVector(NearFadeId, Vector4.zero);
        }
    }
}
