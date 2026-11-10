using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.TeamHq
{
    /// <summary>
    /// Builds the packed props in Resources/Props (written by tools/team-hq/props/pack_props.py) as meshes and
    /// Standard materials. The files are in the viewer's frame, so each mesh is mirrored into Unity's the same
    /// way as the building. The Team HQ viewer reads the same files.
    /// </summary>
    public static class HqProps
    {
        // Filled by JsonUtility.
#pragma warning disable 0649
        [Serializable]
        private sealed class Info
        {
            public float[] min, max;
            public Mat[] mats;
            public Part[] parts;
        }

        [Serializable]
        private sealed class Mat
        {
            public string col, nrm, ms;
            public float[] color;
            public float metal, rough;
            public bool cutout, blend, twoSided;
        }

        [Serializable]
        private sealed class Part
        {
            public int mat, n, i, pos, nrm, uv, idx;
            public bool wide;
        }
#pragma warning restore 0649

        private sealed class Built
        {
            public Mesh Mesh;
            public Material[] Materials;
        }

        private static readonly Dictionary<string, Built> Cache = new Dictionary<string, Built>();

        /// <summary>Places one copy of a prop under parent, or returns null when its files are missing.</summary>
        public static GameObject Place(Transform parent, HqProp prop)
        {
            Built b = Get(prop.Id);
            if (b == null) return null;
            var go = new GameObject(prop.Id);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = prop.Position;
            go.transform.localRotation = Quaternion.Euler(0, prop.Yaw, 0);
            go.transform.localScale = Vector3.one * prop.Scale;
            go.AddComponent<MeshFilter>().sharedMesh = b.Mesh;
            MeshRenderer r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = b.Materials;
            r.shadowCastingMode = ShadowCastingMode.On;
            return go;
        }

        private static Built Get(string id)
        {
            if (Cache.TryGetValue(id, out Built b)) return b;
            var json = Resources.Load<TextAsset>("Props/" + id);
            var bytes = Resources.Load<TextAsset>("Props/" + id + "_mesh");
            if (json == null || bytes == null)
            {
                Debug.LogWarning("Team HQ: no packed files for prop " + id);
                return Cache[id] = null;
            }
            Info info = JsonUtility.FromJson<Info>(json.text);
            byte[] data = bytes.bytes;

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var submeshes = new List<int[]>();
            var materials = new List<Material>();
            Vector3 lo = new Vector3(info.min[0], info.min[1], info.min[2]);
            Vector3 span = new Vector3(Mathf.Max(info.max[0] - lo.x, 1e-6f), Mathf.Max(info.max[1] - lo.y, 1e-6f), Mathf.Max(info.max[2] - lo.z, 1e-6f));
            foreach (Part p in info.parts)
            {
                int start = vertices.Count;
                for (int v = 0; v < p.n; v++)
                {
                    float x = lo.x + (BitConverter.ToInt16(data, p.pos + v * 6) + 32768) / 65535f * span.x;
                    float y = lo.y + (BitConverter.ToInt16(data, p.pos + v * 6 + 2) + 32768) / 65535f * span.y;
                    float z = lo.z + (BitConverter.ToInt16(data, p.pos + v * 6 + 4) + 32768) / 65535f * span.z;
                    // Mirror the viewer's frame into Unity's: flip Z here, and the winding below.
                    vertices.Add(new Vector3(x, y, -z));
                    var n = new Vector3((sbyte)data[p.nrm + v * 3], (sbyte)data[p.nrm + v * 3 + 1], -(sbyte)data[p.nrm + v * 3 + 2]);
                    normals.Add(n.normalized);
                    // glTF's v runs down the image; Unity's runs up.
                    uvs.Add(new Vector2(BitConverter.ToSingle(data, p.uv + v * 8), 1 - BitConverter.ToSingle(data, p.uv + v * 8 + 4)));
                }
                var tris = new int[p.i];
                for (int k = 0; k < p.i; k++)
                    tris[k] = start + (p.wide ? (int)BitConverter.ToUInt32(data, p.idx + k * 4) : BitConverter.ToUInt16(data, p.idx + k * 2));
                for (int k = 0; k < tris.Length; k += 3) (tris[k + 1], tris[k + 2]) = (tris[k + 2], tris[k + 1]);
                submeshes.Add(tris);
                materials.Add(MaterialFor(id, p.mat, info.mats[p.mat]));
            }

            var mesh = new Mesh { name = id, indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = submeshes.Count;
            for (int s = 0; s < submeshes.Count; s++) mesh.SetTriangles(submeshes[s], s);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return Cache[id] = new Built { Mesh = mesh, Materials = materials.ToArray() };
        }

        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();

        private static Material MaterialFor(string id, int index, Mat m)
        {
            string key = id + "/" + index;
            if (Materials.TryGetValue(key, out Material cached)) return cached;
            var mat = new Material(Shader.Find("Standard")) { name = key };
            mat.color = new Color(m.color[0], m.color[1], m.color[2], m.color.Length > 3 ? m.color[3] : 1).gamma;
            Texture2D col = Tex(m.col), nrm = Tex(m.nrm), ms = Tex(m.ms);
            if (col != null) mat.mainTexture = col;
            if (nrm != null)
            {
                mat.SetTexture("_BumpMap", nrm);
                mat.EnableKeyword("_NORMALMAP");
            }
            if (ms != null)
            {
                // R metallic, A smoothness: Unity reads the pack's mask as it is; the factors scale it like glTF's.
                mat.SetTexture("_MetallicGlossMap", ms);
                mat.EnableKeyword("_METALLICGLOSSMAP");
                mat.SetFloat("_GlossMapScale", 1);
            }
            else
            {
                mat.SetFloat("_Metallic", m.metal);
                mat.SetFloat("_Glossiness", 1 - m.rough);
            }
            if (m.cutout) HqMaterials.Cutout(mat);
            else if (m.blend) HqMaterials.Fade(mat);
            Materials[key] = mat;
            return mat;
        }

        private static Texture2D Tex(string file)
        {
            if (string.IsNullOrEmpty(file)) return null;
            return Resources.Load<Texture2D>("Props/" + System.IO.Path.GetFileNameWithoutExtension(file));
        }
    }
}
