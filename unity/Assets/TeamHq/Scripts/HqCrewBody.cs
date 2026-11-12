using System;
using System.Collections.Generic;
using TarmacGauntlet.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.TeamHq
{
    /// <summary>
    /// One crew body packed by tools/team-hq/crew/pack_crew.py into Resources/Crew: a skinned mesh, its skeleton,
    /// its clips and its clothing colourways, already in Unity's frame. Builds rigs that <see cref="HqCrew"/> poses.
    /// </summary>
    internal sealed class HqCrewBody
    {
        // Filled by JsonUtility.
#pragma warning disable 0649
        [Serializable]
        private sealed class Info
        {
            public NodeInfo[] nodes;
            public int[] joints;
            public float[] ibm;
            public MatInfo[] mats;
            public PartInfo[] parts;
            public ClipInfo[] clips;
        }

        [Serializable]
        private sealed class NodeInfo
        {
            public string name;
            public int parent;
            public float[] t, r, s;
        }

        [Serializable]
        private sealed class MatInfo
        {
            public string name, col, nrm;
            public float[] color;
            public float metal, rough;
            public string[] colours;
        }

        [Serializable]
        private sealed class PartInfo
        {
            public int mat, n, i, pos, nrm, uv, joints, weights, idx;
        }

        [Serializable]
        private sealed class ClipInfo
        {
            public string name;
            public int frames, fps, off, posOff;
            public int[] rot, pos;
        }
#pragma warning restore 0649

        /// <summary>A clip sampled at a fixed rate: per frame, the rotation of each node in RotNodes and the position of each node in PosNodes.</summary>
        internal sealed class Clip
        {
            public string Name;
            public float Duration, Fps;
            public int Frames;
            public int[] RotNodes, PosNodes;
            public Quaternion[] Rot;
            public Vector3[] Pos;
        }

        public readonly string Id;
        public readonly Dictionary<string, Clip> Clips = new Dictionary<string, Clip>();
        public readonly string[] NodeNames;
        public readonly int[] Parents;
        public readonly Vector3[] RestT, RestS;
        public readonly Quaternion[] RestR;

        private readonly Info info;
        private readonly Mesh mesh;
        private readonly Texture2D[] maps, normals;
        private readonly Dictionary<int, Material[]> palettes = new Dictionary<int, Material[]>();

        /// <summary>Submeshes in the mesh, one a part: a material can have more than one.</summary>
        internal int Submeshes => info.parts.Length;

        /// <summary>The name of the material a submesh is drawn with.</summary>
        internal string MaterialName(int submesh) => info.mats[info.parts[submesh].mat].name;

        /// <summary>One material a submesh, from a material a body material.</summary>
        internal Material[] PartMaterials(Material[] byMaterial)
        {
            var m = new Material[info.parts.Length];
            for (int s = 0; s < m.Length; s++) m[s] = byMaterial[info.parts[s].mat];
            return m;
        }

        private HqCrewBody(string id, Info info, byte[] meshData, byte[] animData)
        {
            Id = id;
            this.info = info;
            int n = info.nodes.Length;
            NodeNames = new string[n];
            Parents = new int[n];
            RestT = new Vector3[n];
            RestR = new Quaternion[n];
            RestS = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                NodeInfo node = info.nodes[i];
                NodeNames[i] = node.name;
                Parents[i] = node.parent;
                RestT[i] = new Vector3(node.t[0], node.t[1], node.t[2]);
                RestR[i] = new Quaternion(node.r[0], node.r[1], node.r[2], node.r[3]);
                RestS[i] = new Vector3(node.s[0], node.s[1], node.s[2]);
            }
            mesh = BuildMesh(meshData);
            maps = new Texture2D[info.mats.Length];
            normals = new Texture2D[info.mats.Length];
            for (int m = 0; m < info.mats.Length; m++)
            {
                maps[m] = Tex(info.mats[m].col);
                normals[m] = Tex(info.mats[m].nrm);
            }
            foreach (ClipInfo c in info.clips) Clips[c.name] = ReadClip(c, animData);
        }

        /// <summary>Loads a packed body, or returns null when its files are missing.</summary>
        public static HqCrewBody Load(string id)
        {
            var json = Resources.Load<TextAsset>("Crew/" + id);
            var meshBytes = Resources.Load<TextAsset>("Crew/" + id + "_mesh");
            var animBytes = Resources.Load<TextAsset>("Crew/" + id + "_anim");
            if (json == null || meshBytes == null || animBytes == null)
            {
                Debug.LogWarning("Team HQ: no packed files for crew body " + id);
                return null;
            }
            return new HqCrewBody(id, JsonUtility.FromJson<Info>(json.text), meshBytes.bytes, animBytes.bytes);
        }

        /// <summary>A new copy of the body under parent, in its rest pose. Without a colourway it has no mesh: a rig to measure poses on.</summary>
        public HqCrewRig Build(Transform parent, int colour, bool visible)
        {
            var root = new GameObject(Id);
            root.transform.SetParent(parent, false);
            var nodes = new Transform[NodeNames.Length];
            nodes[0] = root.transform;
            for (int i = 1; i < nodes.Length; i++)
            {
                var go = new GameObject(NodeNames[i]);
                go.transform.SetParent(nodes[Parents[i]], false);
                go.transform.localPosition = RestT[i];
                go.transform.localRotation = RestR[i];
                go.transform.localScale = RestS[i];
                nodes[i] = go.transform;
            }
            var rig = new HqCrewRig(this, root, nodes);
            if (!visible) return rig;

            var skin = new GameObject("Skin");
            skin.transform.SetParent(root.transform, false);
            SkinnedMeshRenderer r = skin.AddComponent<SkinnedMeshRenderer>();
            var bones = new Transform[info.joints.Length];
            for (int j = 0; j < bones.Length; j++) bones[j] = nodes[info.joints[j]];
            r.bones = bones;
            r.rootBone = rig.Find("Hips") ?? root.transform;
            r.sharedMesh = mesh;
            r.sharedMaterials = PartMaterials(Palette(colour));
            r.shadowCastingMode = ShadowCastingMode.On;
            r.receiveShadows = true;
            // The root moves under the clips (sitting, crouching), so keep the bounds following the bones.
            r.updateWhenOffscreen = true;
            return rig;
        }

        private Mesh BuildMesh(byte[] data)
        {
            var vertices = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var weights = new List<BoneWeight>();
            var submeshes = new List<int[]>();
            foreach (PartInfo p in info.parts)
            {
                int start = vertices.Count;
                for (int v = 0; v < p.n; v++)
                {
                    vertices.Add(new Vector3(F(data, p.pos, v * 3), F(data, p.pos, v * 3 + 1), F(data, p.pos, v * 3 + 2)));
                    norms.Add(new Vector3(F(data, p.nrm, v * 3), F(data, p.nrm, v * 3 + 1), F(data, p.nrm, v * 3 + 2)).normalized);
                    uvs.Add(new Vector2(F(data, p.uv, v * 2), F(data, p.uv, v * 2 + 1)));
                    int j = p.joints + v * 4;
                    weights.Add(new BoneWeight
                    {
                        boneIndex0 = data[j], boneIndex1 = data[j + 1], boneIndex2 = data[j + 2], boneIndex3 = data[j + 3],
                        weight0 = F(data, p.weights, v * 4), weight1 = F(data, p.weights, v * 4 + 1),
                        weight2 = F(data, p.weights, v * 4 + 2), weight3 = F(data, p.weights, v * 4 + 3),
                    });
                }
                var tris = new int[p.i];
                for (int k = 0; k < p.i; k++) tris[k] = start + (int)BitConverter.ToUInt32(data, p.idx + k * 4);
                submeshes.Add(tris);
            }
            var m = new Mesh { name = Id, indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            m.SetVertices(vertices);
            m.SetNormals(norms);
            m.SetUVs(0, uvs);
            m.boneWeights = weights.ToArray();
            var binds = new Matrix4x4[info.joints.Length];
            for (int j = 0; j < binds.Length; j++)
            {
                var b = new Matrix4x4();
                for (int r = 0; r < 4; r++)
                    for (int c = 0; c < 4; c++) b[r, c] = info.ibm[j * 16 + r * 4 + c];
                binds[j] = b;
            }
            m.bindposes = binds;
            m.subMeshCount = submeshes.Count;
            for (int s = 0; s < submeshes.Count; s++) m.SetTriangles(submeshes[s], s);
            m.RecalculateBounds();
            m.RecalculateTangents();
            return m;
        }

        private static float F(byte[] data, int offset, int index) => BitConverter.ToSingle(data, offset + index * 4);

        private static Clip ReadClip(ClipInfo c, byte[] data)
        {
            var clip = new Clip
            {
                Name = c.name, Frames = c.frames, Fps = c.fps, Duration = (c.frames - 1) / (float)c.fps,
                RotNodes = c.rot, PosNodes = c.pos,
                Rot = new Quaternion[c.frames * c.rot.Length], Pos = new Vector3[c.frames * c.pos.Length],
            };
            const float q = 1 / 32767f;
            for (int k = 0; k < clip.Rot.Length; k++)
            {
                int o = c.off + k * 8;
                clip.Rot[k] = new Quaternion(BitConverter.ToInt16(data, o) * q, BitConverter.ToInt16(data, o + 2) * q,
                    BitConverter.ToInt16(data, o + 4) * q, BitConverter.ToInt16(data, o + 6) * q).normalized;
            }
            for (int k = 0; k < clip.Pos.Length; k++)
                clip.Pos[k] = new Vector3(F(data, c.posOff, k * 3), F(data, c.posOff, k * 3 + 1), F(data, c.posOff, k * 3 + 2));
            return clip;
        }

        /// <summary>The materials for one colourway, made the first time someone wears it. 0 is the body's own look.</summary>
        internal Material[] Palette(int colour)
        {
            if (palettes.TryGetValue(colour, out Material[] p)) return p;
            p = new Material[info.mats.Length];
            for (int m = 0; m < p.Length; m++)
            {
                MatInfo mi = info.mats[m];
                var mat = new Material(Lit.Shader) { name = Id + "/" + m + "/" + colour };
                mat.color = new Color(mi.color[0], mi.color[1], mi.color[2], mi.color.Length > 3 ? mi.color[3] : 1).gamma;
                string recoloured = colour > 0 && mi.colours != null && colour < mi.colours.Length ? mi.colours[colour] : "";
                Texture2D map = string.IsNullOrEmpty(recoloured) ? maps[m] : Tex(recoloured);
                if (map != null) mat.mainTexture = map;
                if (normals[m] != null) Lit.NormalMap(mat, normals[m]);
                mat.SetFloat("_Metallic", mi.metal);
                mat.SetFloat("_Smoothness", 1 - mi.rough);
                p[m] = mat;
            }
            return palettes[colour] = p;
        }

        private static Texture2D Tex(string name) => string.IsNullOrEmpty(name) ? null : Resources.Load<Texture2D>("Crew/" + name);
    }

    /// <summary>One built copy of a crew body: its root and every node, posed by blending clips.</summary>
    internal sealed class HqCrewRig
    {
        public readonly HqCrewBody Body;
        public readonly GameObject Root;
        public readonly Transform[] Nodes;

        // Per node: the blend so far and its total weight.
        private readonly Quaternion[] rot;
        private readonly Vector3[] pos;
        private readonly float[] rotW, posW;

        public HqCrewRig(HqCrewBody body, GameObject root, Transform[] nodes)
        {
            Body = body;
            Root = root;
            Nodes = nodes;
            rot = new Quaternion[nodes.Length];
            pos = new Vector3[nodes.Length];
            rotW = new float[nodes.Length];
            posW = new float[nodes.Length];
        }

        public Transform Find(string name)
        {
            for (int i = 0; i < Nodes.Length; i++) if (Body.NodeNames[i] == name) return Nodes[i];
            return null;
        }

        /// <summary>
        /// Poses the rig from weighted clips, the way three.js mixes actions: each clip is slerped in by its share of the
        /// weight so far, and while the weights add up to less than one the rest pose fills the gap.
        /// </summary>
        public void Pose(IReadOnlyList<(HqCrewBody.Clip clip, float time, float weight)> layers)
        {
            Array.Clear(rotW, 0, rotW.Length);
            Array.Clear(posW, 0, posW.Length);
            foreach (var (clip, time, weight) in layers)
            {
                if (weight <= 0) continue;
                float f = Mathf.Clamp(time * clip.Fps, 0, clip.Frames - 1);
                int a = Mathf.Min((int)f, clip.Frames - 1), b = Mathf.Min(a + 1, clip.Frames - 1);
                float k = f - a;
                int nr = clip.RotNodes.Length, np = clip.PosNodes.Length;
                for (int i = 0; i < nr; i++)
                {
                    int node = clip.RotNodes[i];
                    Quaternion q = Quaternion.Slerp(clip.Rot[a * nr + i], clip.Rot[b * nr + i], k);
                    if (rotW[node] == 0) { rot[node] = q; rotW[node] = weight; }
                    else { rotW[node] += weight; rot[node] = Quaternion.Slerp(rot[node], q, weight / rotW[node]); }
                }
                for (int i = 0; i < np; i++)
                {
                    int node = clip.PosNodes[i];
                    Vector3 p = Vector3.Lerp(clip.Pos[a * np + i], clip.Pos[b * np + i], k);
                    if (posW[node] == 0) { pos[node] = p; posW[node] = weight; }
                    else { posW[node] += weight; pos[node] = Vector3.Lerp(pos[node], p, weight / posW[node]); }
                }
            }
            for (int n = 1; n < Nodes.Length; n++)
            {
                Nodes[n].localRotation = rotW[n] >= 1 ? rot[n] : rotW[n] <= 0 ? Body.RestR[n] : Quaternion.Slerp(rot[n], Body.RestR[n], 1 - rotW[n]);
                Nodes[n].localPosition = posW[n] >= 1 ? pos[n] : posW[n] <= 0 ? Body.RestT[n] : Vector3.Lerp(pos[n], Body.RestT[n], 1 - posW[n]);
            }
        }
    }
}
