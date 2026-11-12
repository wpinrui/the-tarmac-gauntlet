using System.Collections.Generic;
using TarmacGauntlet.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The pit box kit in the race (<see cref="PitBoxKit"/>): in every garage with a car, the fuel nozzle and four
    /// wheel guns hanging on their lines from the boom, the air jack's lance, the lollipop, the fire extinguisher and
    /// four fresh tyres in the team's blankets on their trolley.
    ///
    /// Kit at rest costs one instanced draw for each shape and paint across all 100 garages, and is drawn only for
    /// the garages near enough the camera to show it. A crew that moves a piece takes it with <see cref="Take"/>:
    /// the piece leaves the instanced draw and comes back as a GameObject of its own, put where the crew carry it.
    /// While it is taken its line, if it hangs on one, is slung live from its anchor on the boom to the piece. It goes
    /// back to its rest with <see cref="PutBack"/>. Nothing here has a collider, and nothing here sets the stop's clock.
    /// </summary>
    public sealed class PitBoxKitView : MonoBehaviour
    {
        /// <summary>A garage's kit is drawn while a span of <see cref="Zone"/> at its box is at least this share of the screen's height.</summary>
        private const float Cull = 0.035f;
        /// <summary>Size of the space the kit stands in, garage front and box, m.</summary>
        private const float Zone = 9f;
        private const int MaxBatch = 1023;
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private PitBoxKit kit;
        private PitComplex complex;
        // By car: its garage (-1 for none) and team colour.
        private int[] garages;
        private Color[] colours;
        private KitPose[,] rests;
        private Vector3[,] anchors;
        private Transform[,] taken;
        private Mesh[,] liveLines;

        private Mesh[] models;
        private KitPaint[][] modelPaints;
        private Material[] paints;
        private readonly List<int> shown = new List<int>();
        private readonly Plane[] frustum = new Plane[6];
        private readonly Matrix4x4[] batch = new Matrix4x4[MaxBatch];
        private readonly Vector4[] batchColours = new Vector4[MaxBatch];
        private MaterialPropertyBlock block;

        /// <summary>Lays out the kit in the garage of every car: car i in garages[i] (-1 for none), its blankets in colours[i].</summary>
        public void Build(PitComplex pc, int[] carGarages, Color[] teamColours)
        {
            complex = pc;
            kit = new PitBoxKit(pc);
            garages = carGarages;
            colours = teamColours;
            int n = garages.Length;
            rests = new KitPose[n, PitBoxKit.ItemCount];
            anchors = new Vector3[n, PitBoxKit.ItemCount];
            taken = new Transform[n, PitBoxKit.ItemCount];
            liveLines = new Mesh[n, PitBoxKit.ItemCount];
            for (int i = 0; i < n; i++)
            {
                if (garages[i] < 0) continue;
                for (int k = 0; k < PitBoxKit.ItemCount; k++)
                {
                    var item = (KitItem)k;
                    rests[i, k] = kit.Rest(garages[i], item);
                    if (PitBoxKit.Hangs(item)) anchors[i, k] = kit.Anchor(garages[i], item);
                }
            }
            block = new MaterialPropertyBlock();
            Paints();
            Models();
        }

        // ---------------------------------------------------------------- for the crews

        /// <summary>Where a car's piece of kit rests, and which way it faces.</summary>
        public KitPose Rest(int car, KitItem item) => rests[car, (int)item];

        /// <summary>Where a car's hanging piece's line leaves the boom.</summary>
        public Vector3 Anchor(int car, KitItem item) => anchors[car, (int)item];

        /// <summary>
        /// Takes a car's piece of kit off its rest to be carried: it stops being drawn at rest and comes back as its
        /// own object, at its rest place, for the crew to move. A hanging piece's position is where its line joins it.
        /// Taking a piece already taken returns the same object.
        /// </summary>
        public Transform Take(int car, KitItem item)
        {
            int k = (int)item;
            if (garages == null || garages[car] < 0) return null;
            if (taken[car, k] != null) return taken[car, k];
            KitModel model = PitBoxKit.ModelOf(item);
            var go = new GameObject($"Car {car + 1} {item}");
            go.transform.SetParent(transform, false);
            KitPose rest = rests[car, k];
            go.transform.SetPositionAndRotation(rest.Position, Quaternion.LookRotation(rest.Forward, Vector3.up));
            go.AddComponent<MeshFilter>().sharedMesh = models[(int)model];
            var r = go.AddComponent<MeshRenderer>();
            var mats = new Material[modelPaints[(int)model].Length];
            for (int s = 0; s < mats.Length; s++) mats[s] = paints[(int)modelPaints[(int)model][s]];
            r.sharedMaterials = mats;
            var own = new MaterialPropertyBlock();
            own.SetColor(ColorId, colours[car]);
            r.SetPropertyBlock(own);
            taken[car, k] = go.transform;
            return go.transform;
        }

        /// <summary>Puts a taken piece back on its rest, drawn with the rest of the kit again.</summary>
        public void PutBack(int car, KitItem item)
        {
            int k = (int)item;
            if (taken[car, k] == null) return;
            Destroy(taken[car, k].gameObject);
            taken[car, k] = null;
            if (liveLines[car, k] != null) liveLines[car, k].Clear();
        }

        // ---------------------------------------------------------------- materials and shapes

        private void Paints()
        {
            paints = new Material[5];
            Shader kitShader = Resources.Load<Shader>("PitKit/PitKit");
            paints[(int)KitPaint.Metal] = Make("Kit metal", new Color(0.62f, 0.63f, 0.65f), 0.6f, 0.8f);
            paints[(int)KitPaint.Dark] = Make("Kit rubber", new Color(0.05f, 0.05f, 0.055f), 0.25f, 0f);
            paints[(int)KitPaint.Red] = Make("Kit red", new Color(0.78f, 0.06f, 0.05f), 0.45f, 0f);
            paints[(int)KitPaint.White] = Make("Kit white", new Color(0.92f, 0.92f, 0.9f), 0.4f, 0f);
            var team = new Material(kitShader != null ? kitShader : Lit.Shader) { name = "Kit team colour", enableInstancing = true };
            team.SetFloat("_Smoothness", 0.3f);
            paints[(int)KitPaint.Team] = team;
        }

        private static Material Make(string name, Color colour, float smoothness, float metallic)
        {
            var m = new Material(Lit.Shader) { name = name, enableInstancing = true, color = colour };
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            return m;
        }

        /// <summary>Each shape as one mesh, a submesh for each paint it is drawn in.</summary>
        private void Models()
        {
            int count = System.Enum.GetValues(typeof(KitModel)).Length;
            models = new Mesh[count];
            modelPaints = new KitPaint[count][];
            for (int m = 0; m < count; m++)
            {
                Dictionary<KitPaint, MeshKit> parts = PitBoxKit.Model((KitModel)m);
                var verts = new List<Vector3>();
                var norms = new List<Vector3>();
                var subs = new List<List<int>>();
                var order = new List<KitPaint>();
                foreach (KeyValuePair<KitPaint, MeshKit> p in parts)
                {
                    int start = verts.Count;
                    verts.AddRange(p.Value.Vertices);
                    norms.AddRange(p.Value.Normals);
                    var tris = new List<int>();
                    foreach (int t in p.Value.Triangles) tris.Add(start + t);
                    subs.Add(tris);
                    order.Add(p.Key);
                }
                var mesh = new Mesh { name = "Kit " + (KitModel)m };
                mesh.SetVertices(verts);
                mesh.SetNormals(norms);
                mesh.subMeshCount = subs.Count;
                for (int s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s], s);
                mesh.RecalculateBounds();
                models[m] = mesh;
                modelPaints[m] = order.ToArray();
            }
        }

        // ---------------------------------------------------------------- drawing

        private void LateUpdate()
        {
            Camera cam = Camera.main;
            if (cam == null || garages == null) return;
            Shown(cam);
            for (int m = 0; m < models.Length; m++) Draw((KitModel)m);
            Lines();
        }

        /// <summary>The cars whose garage's kit is near enough the camera, and in view, to draw.</summary>
        private void Shown(Camera cam)
        {
            shown.Clear();
            GeometryUtility.CalculateFrustumPlanes(cam, frustum);
            float tan = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            Vector3 eye = cam.transform.position;
            for (int i = 0; i < garages.Length; i++)
            {
                if (garages[i] < 0) continue;
                Vector3 mid = complex.Frame(garages[i]).At(0f, 2f, PitComplex.ClearDepth + 4f);
                float d = Vector3.Distance(eye, mid);
                if (Zone / (2f * Mathf.Max(d, 0.1f) * tan) < Cull) continue;
                if (!GeometryUtility.TestPlanesAABB(frustum, new Bounds(mid, Vector3.one * (Zone * 2f)))) continue;
                shown.Add(i);
            }
        }

        /// <summary>Every piece of one shape at rest in the shown garages, and the rest lines of the hanging ones.</summary>
        private void Draw(KitModel model)
        {
            bool line = model == KitModel.FuelLine || model == KitModel.AirLine;
            int n = 0;
            foreach (int car in shown)
            {
                for (int k = 0; k < PitBoxKit.ItemCount; k++)
                {
                    var item = (KitItem)k;
                    if (taken[car, k] != null) continue;
                    KitPose rest = rests[car, k];
                    if (line)
                    {
                        if (!PitBoxKit.Hangs(item) || PitBoxKit.LineOf(item) != model) continue;
                        float drop = anchors[car, k].y - rest.Position.y;
                        batch[n] = Matrix4x4.TRS(anchors[car, k], Quaternion.LookRotation(rest.Forward, Vector3.up), new Vector3(1f, drop, 1f));
                    }
                    else
                    {
                        if (PitBoxKit.ModelOf(item) != model) continue;
                        batch[n] = Matrix4x4.TRS(rest.Position, Quaternion.LookRotation(rest.Forward, Vector3.up), Vector3.one);
                    }
                    Color c = colours[car];
                    batchColours[n] = new Vector4(c.r, c.g, c.b, 1f);
                    if (++n == MaxBatch) { Flush(model, n); n = 0; }
                }
            }
            if (n > 0) Flush(model, n);
        }

        private void Flush(KitModel model, int n)
        {
            Mesh mesh = models[(int)model];
            KitPaint[] subs = modelPaints[(int)model];
            bool small = model == KitModel.AirLine || model == KitModel.Gun || model == KitModel.Nozzle;
            ShadowCastingMode shadows = small ? ShadowCastingMode.Off : ShadowCastingMode.On;
            for (int s = 0; s < subs.Length; s++)
            {
                MaterialPropertyBlock props = null;
                if (subs[s] == KitPaint.Team)
                {
                    block.Clear();
                    block.SetVectorArray(ColorId, batchColours);
                    props = block;
                }
                Graphics.DrawMeshInstanced(mesh, s, paints[(int)subs[s]], batch, n, props, shadows, true, gameObject.layer);
            }
        }

        /// <summary>The lines of taken hanging pieces, slung from the boom to wherever the piece is now.</summary>
        private void Lines()
        {
            for (int i = 0; i < garages.Length; i++)
            {
                if (garages[i] < 0) continue;
                for (int k = 0; k <= (int)KitItem.Lance; k++)
                {
                    Transform piece = taken[i, k];
                    if (piece == null) continue;
                    var item = (KitItem)k;
                    Vector3 a = anchors[i, k], b = piece.position;
                    float length = PitBoxKit.LineLength(item);
                    var line = new MeshKit();
                    PitBoxKit.Pipe(line, PitBoxKit.Slung(a, b, PitBoxKit.Sag(a, b, length)), PitBoxKit.LineRadius(item), item == KitItem.Nozzle ? 6 : 4);
                    Mesh mesh = liveLines[i, k];
                    if (mesh == null) liveLines[i, k] = mesh = new Mesh { name = "Live line" };
                    mesh.Clear();
                    mesh.SetVertices(new List<Vector3>(line.Vertices));
                    mesh.SetNormals(new List<Vector3>(line.Normals));
                    mesh.SetTriangles(new List<int>(line.Triangles), 0);
                    mesh.RecalculateBounds();
                    Graphics.DrawMesh(mesh, Matrix4x4.identity, paints[(int)KitPaint.Dark], gameObject.layer, null, 0, null, item == KitItem.Nozzle);
                }
            }
        }
    }
}
