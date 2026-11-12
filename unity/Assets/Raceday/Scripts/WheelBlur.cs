using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Keeps a turning wheel reading as turning at every speed. Past a walking pace the spokes turn too far between frames
    /// for the eye to follow, and seem to stand still or run backwards. So as the wheel speeds up, a disc fades in over its
    /// face showing the rim as a blur: each ring the average of the spokes and gaps that pass through it
    /// (<see cref="WheelBlurProfile"/>), with faint streaks that turn the way the wheel does, at a pace the eye can follow.
    /// Once the disc is fully in, the rim itself is hidden. The tyre always stays.
    /// </summary>
    public sealed class WheelBlur : MonoBehaviour
    {
        /// <summary>The disc sits this far outside the rim's face, m.</summary>
        private const float Gap = 0.002f;

        private sealed class Disc
        {
            public Renderer[] Parts;
            public MeshRenderer View;
            public Material Material;
            public float Phase;
            public bool PartsHidden;
        }

        private readonly Disc[] discs = new Disc[4];
        private static readonly int ProfileId = Shader.PropertyToID("_Profile"), FadeId = Shader.PropertyToID("_Fade");
        private static readonly Dictionary<string, Profile> profiles = new Dictionary<string, Profile>();
        private static Shader shader;
        private static Mesh quad;
        private static bool warned;

        private sealed class Profile
        {
            public Texture2D Texture;
            public float Radius, Face;
        }

        /// <summary>Gives a car's wheels their blur discs: every wheel whose pivot holds model parts named wheel_* past its tyre.</summary>
        public static void Fit(CarController car, Transform[] pivots)
        {
            if (pivots == null) return;
            if (shader == null) shader = Resources.Load<Shader>("Effects/WheelBlur");
            if (shader == null) return;
            WheelBlur blur = null;
            for (int i = 0; i < 4 && i < pivots.Length; i++)
            {
                Transform pivot = pivots[i];
                if (pivot == null) continue;
                var parts = new List<Renderer>();
                foreach (MeshRenderer r in pivot.GetComponentsInChildren<MeshRenderer>(true))
                    if (r.name.StartsWith("wheel_") && !r.name.StartsWith("wheel_tyre_")) parts.Add(r);
                if (parts.Count == 0) continue;
                float outward = Mathf.Sign(pivot.InverseTransformDirection(pivot.position - car.transform.position).x);
                Profile profile = ProfileOf(pivot, parts, outward);
                if (profile == null) continue;

                if (blur == null) blur = car.gameObject.AddComponent<WheelBlur>();
                var material = new Material(shader) { name = "Wheel blur" };
                material.SetTexture(ProfileId, profile.Texture);
                material.SetFloat(FadeId, 0f);
                var go = new GameObject("Wheel blur") { layer = pivot.gameObject.layer };
                go.transform.SetParent(pivot, false);
                go.transform.localPosition = new Vector3(outward * (profile.Face + Gap), 0f, 0f);
                go.transform.localScale = Vector3.one * profile.Radius;
                go.AddComponent<MeshFilter>().sharedMesh = Quad();
                MeshRenderer view = go.AddComponent<MeshRenderer>();
                view.sharedMaterial = material;
                view.shadowCastingMode = ShadowCastingMode.Off;
                view.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                view.enabled = false;
                blur.discs[i] = new Disc { Parts = parts.ToArray(), View = view, Material = material };
            }
            if (blur != null) car.Blur = blur;
        }

        /// <summary>Wheel i's disc for this frame, turning at a rate, rad/s, its pivot just turned to an angle about the axle, radians.</summary>
        internal void Show(int i, float rate, float spin)
        {
            Disc d = discs[i];
            if (d == null || d.View == null) return;
            float dt = Mathf.Max(Time.smoothDeltaTime, 1e-4f);
            float fade = WheelBlurProfile.Fade(rate, dt);
            bool on = fade > 0.002f;
            if (d.View.enabled != on) d.View.enabled = on;
            bool hide = fade > 0.998f;
            if (hide != d.PartsHidden)
            {
                d.PartsHidden = hide;
                foreach (Renderer r in d.Parts)
                    if (r != null) r.enabled = !hide;
            }
            if (!on) return;
            d.Material.SetFloat(FadeId, fade);
            // The disc keeps its own turn, not the wheel's: back against the pivot's spin, then on by a step the eye can follow.
            d.Phase = Mathf.Repeat(d.Phase + WheelBlurProfile.StreakStep(rate, dt), 2f * Mathf.PI);
            d.View.transform.localRotation = Quaternion.Euler((d.Phase - spin) * Mathf.Rad2Deg, 0f, 0f);
        }

        private void OnDestroy()
        {
            foreach (Disc d in discs)
                if (d?.Material != null) Destroy(d.Material);
        }

        /// <summary>The blur of a wheel's parts, worked out once for each model's wheel and shared by every car with it.</summary>
        private static Profile ProfileOf(Transform pivot, List<Renderer> parts, float outward)
        {
            var key = new StringBuilder();
            key.Append(outward > 0f ? '+' : '-');
            foreach (Renderer r in parts)
            {
                MeshFilter mf = r.GetComponent<MeshFilter>();
                key.Append(mf != null && mf.sharedMesh != null ? mf.sharedMesh.GetHashCode() : 0).Append(',');
                foreach (Material m in r.sharedMaterials) key.Append(m != null ? m.GetHashCode() : 0).Append(';');
            }
            string k = key.ToString();
            // A profile from an earlier scene whose texture was unloaded since is worked out again.
            if (profiles.TryGetValue(k, out Profile cached) && (cached == null || cached.Texture != null)) return cached;

            var corners = new List<Vector3>();
            var colours = new List<Vector3>();
            foreach (Renderer r in parts)
            {
                MeshFilter mf = r.GetComponent<MeshFilter>();
                Mesh mesh = mf != null ? mf.sharedMesh : null;
                if (mesh == null) continue;
                if (!mesh.isReadable)
                {
                    if (!warned) Debug.LogWarning($"Wheel blur: {mesh.name} is not readable, so its wheel has no blur.");
                    warned = true;
                    return profiles[k] = null;
                }
                Vector3[] vertices = mesh.vertices;
                Material[] mats = r.sharedMaterials;
                Matrix4x4 toWheel = pivot.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                for (int sub = 0; sub < mesh.subMeshCount; sub++)
                {
                    if (mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                    Material m = mats.Length > 0 ? mats[Mathf.Min(sub, mats.Length - 1)] : null;
                    Color c = m != null ? CarMaterials.ColourOf(m) : Color.grey;
                    var rgb = new Vector3(c.r, c.g, c.b);
                    int[] tris = mesh.GetTriangles(sub);
                    for (int t = 0; t + 2 < tris.Length; t += 3)
                    {
                        for (int j = 0; j < 3; j++)
                        {
                            Vector3 p = toWheel.MultiplyPoint3x4(vertices[tris[t + j]]);
                            corners.Add(new Vector3(p.x * outward, p.y, p.z));
                        }
                        colours.Add(rgb);
                    }
                }
            }
            WheelBlurProfile.Result result = WheelBlurProfile.Build(corners, colours);
            if (result.Radius <= 1e-3f) return profiles[k] = null;

            var texture = new Texture2D(WheelBlurProfile.Rings, 1, TextureFormat.RGBA32, false, false)
            {
                name = "Wheel blur profile",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[WheelBlurProfile.Rings];
            for (int i = 0; i < pixels.Length; i++)
            {
                float[] v = result.Rgba;
                pixels[i] = new Color(v[i * 4], v[i * 4 + 1], v[i * 4 + 2], v[i * 4 + 3]);
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return profiles[k] = new Profile { Texture = texture, Radius = result.Radius, Face = result.Face };
        }

        /// <summary>A square across the wheel face, two units wide and facing along the axle; its UVs run -1 to 1 from the centre.</summary>
        private static Mesh Quad()
        {
            if (quad != null) return quad;
            quad = new Mesh { name = "Wheel blur disc" };
            quad.vertices = new[] { new Vector3(0f, -1f, -1f), new Vector3(0f, 1f, -1f), new Vector3(0f, -1f, 1f), new Vector3(0f, 1f, 1f) };
            quad.uv = new[] { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(-1f, 1f), new Vector2(1f, 1f) };
            quad.triangles = new[] { 0, 1, 2, 2, 1, 3 };
            quad.RecalculateBounds();
            return quad;
        }
    }
}
