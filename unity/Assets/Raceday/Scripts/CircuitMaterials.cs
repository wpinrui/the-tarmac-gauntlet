using TarmacGauntlet.Rendering;
using TarmacGauntlet.Visuals;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Materials for the circuit, made in code so the scene needs no asset files. The paved surfaces (road, its edges,
    /// pit lane, kerbs) use the track shader with textures from <see cref="TrackTextures"/>; their tarmac detail and
    /// repairs, rubbered line and rubber marks, and kerb wear follow the graphics options through <see cref="ShowOptions"/>, and
    /// <see cref="WeatherView"/> wets them through <see cref="ShowWet"/>.
    /// </summary>
    public sealed class CircuitMaterials
    {
        public readonly Material Asphalt, RoadEdge, PitAsphalt, White, Yellow, Kerb, Verge, Grass, Gravel,
            Armco, Tyres, Concrete, Hatch, Terrain, GarageInside, Bollard;

        private static readonly int MainTexId = Shader.PropertyToID("_MainTex"), BumpMapId = Shader.PropertyToID("_BumpMap"),
            BumpScaleId = Shader.PropertyToID("_BumpScale"), MacroId = Shader.PropertyToID("_Macro"),
            MacroStrengthId = Shader.PropertyToID("_MacroStrength"), LineId = Shader.PropertyToID("_Line"),
            LineLengthId = Shader.PropertyToID("_LineLength"), UvMetresId = Shader.PropertyToID("_UvMetres"),
            RepairsId = Shader.PropertyToID("_Repairs"), RepairAmountId = Shader.PropertyToID("_RepairAmount"), MarksId = Shader.PropertyToID("_Marks"),
            PoolsId = Shader.PropertyToID("_Pools"), PorosityId = Shader.PropertyToID("_Porosity"), PuddlesId = Shader.PropertyToID("_Puddles");
        private const string DetailKeyword = "_TRACK_DETAIL", RubberKeyword = "_TRACK_RUBBER", WetKeyword = "_TRACK_WET", StripesKeyword = "_GROUND_STRIPES";

        private readonly Texture2D kerbClean, kerbWorn, kerbCleanNormals, kerbWornNormals;
        private bool lineLaid, wet;

        /// <summary>The paved surfaces on the track shader, which draws their wet look itself.</summary>
        public Material[] Paved => new[] { Asphalt, RoadEdge, PitAsphalt, Kerb };

        public CircuitMaterials()
        {
            Shader track = Resources.Load<Shader>("TrackSurface/Track");
            TrackTextures.Tarmac tarmac = TrackTextures.MakeTarmac(1);
            Texture2D normals = Texture("Tarmac normals", TrackTextures.TarmacNormals(tarmac, TrackTextures.TarmacBump), TrackTextures.TarmacSize, TrackTextures.TarmacSize, true);
            Texture2D macro = Texture("Tarmac patches", TrackTextures.Macro(8), TrackTextures.MacroSize, TrackTextures.MacroSize, true);
            Texture2D road = Texture("Asphalt", TrackTextures.TarmacColour(tarmac, new Color(0.17f, 0.17f, 0.18f), 0.42f), TrackTextures.TarmacSize, TrackTextures.TarmacSize, false);
            Texture2D pit = Texture("Pit asphalt", TrackTextures.TarmacColour(tarmac, new Color(0.21f, 0.21f, 0.22f), 0.34f), TrackTextures.TarmacSize, TrackTextures.TarmacSize, false);
            Texture2D repairs = Texture("Tarmac repairs", TrackTextures.Repairs(12), TrackTextures.RepairsAcross, TrackTextures.RepairsAlong, true);
            Asphalt = Track(track, "Asphalt", road, normals, macro, 0.25f, 0.35f, 1f);
            RoadEdge = Track(track, "Road edge", road, normals, macro, 0.25f, 0.35f, 1f);
            PitAsphalt = Track(track, "Pit asphalt", pit, normals, macro, 0.2f, 0.25f, 0.9f);
            Asphalt.SetTexture(RepairsId, repairs);
            Asphalt.SetFloat(RepairAmountId, 1f);
            PitAsphalt.SetTexture(RepairsId, repairs);
            PitAsphalt.SetFloat(RepairAmountId, 0.6f);
            Texture2D marks = Texture("Rubber marks", TrackTextures.Marks(14), TrackTextures.MarksAcross, TrackTextures.MarksAlong, true);
            marks.wrapModeU = TextureWrapMode.Clamp;
            Asphalt.SetTexture(MarksId, marks);
            kerbClean = Texture("Kerb", TrackTextures.Kerb(false, 9), TrackTextures.KerbAcross, TrackTextures.KerbAlong, false);
            kerbWorn = Texture("Worn kerb", TrackTextures.Kerb(true, 9), TrackTextures.KerbAcross, TrackTextures.KerbAlong, false);
            kerbCleanNormals = Texture("Kerb normals", TrackTextures.KerbNormals(false, 9), TrackTextures.KerbAcross, TrackTextures.KerbAlong, true);
            kerbWornNormals = Texture("Worn kerb normals", TrackTextures.KerbNormals(true, 9), TrackTextures.KerbAcross, TrackTextures.KerbAlong, true);
            // Painted kerbs hardly soak water up: they only shine.
            Kerb = Track(track, "Kerb", kerbClean, kerbCleanNormals, macro, 0.4f, 0f, 0.25f);
            White = Make("White paint", null, 0.35f, new Color(0.92f, 0.92f, 0.9f));
            Yellow = Make("Yellow paint", null, 0.35f, new Color(0.95f, 0.78f, 0.1f));
            Verge = Make("Verge", Noise(new Color(0.24f, 0.36f, 0.16f), 0.06f, 3), 0.05f);
            Grass = Make("Grass", Noise(new Color(0.23f, 0.41f, 0.15f), 0.08f, 4), 0.05f);
            Gravel = Make("Gravel", Noise(new Color(0.63f, 0.58f, 0.48f), 0.12f, 5), 0.1f);
            Armco = Make("Armco", null, 0.6f, new Color(0.72f, 0.74f, 0.76f), 0.7f);
            Tyres = Make("Tyre wall", TyreWall(), 0.3f);
            Concrete = Make("Concrete", Noise(new Color(0.74f, 0.73f, 0.7f), 0.03f, 6), 0.15f);
            Hatch = Make("Red and white hatching", Stripes(new Color(0.8f, 0.1f, 0.1f), new Color(0.93f, 0.93f, 0.91f), true), 0.35f);
            Terrain = Make("Terrain", Noise(new Color(0.2f, 0.35f, 0.13f), 0.1f, 7), 0.05f);
            GarageInside = Make("Garage", null, 0.2f, new Color(0.32f, 0.33f, 0.35f));
            Bollard = Make("Bollard", Stripes(new Color(0.95f, 0.78f, 0.1f), new Color(0.08f, 0.08f, 0.08f), false), 0.3f);
            ShowOptions();
        }

        /// <summary>
        /// Lays the rubbered racing line on the road: the line's distance from the road's left edge and how much
        /// rubber lies on it, along the lap. Also where water pools on the road in the wet, so the road holds puddles.
        /// </summary>
        public void LayRubber(Circuit circuit, RacingLine line)
        {
            float[] along = CircuitBuilder.RoadAlong(circuit);
            var fromLeft = new float[circuit.Count];
            for (int k = 0; k < circuit.Count; k++) fromLeft[k] = circuit.HalfWidth(k) - line.Lateral[k];
            float total = along[circuit.Count];
            int texels = Mathf.Clamp(Mathf.CeilToInt(total), 16, SystemInfo.maxTextureSize);
            var map = new Texture2D(texels, 1, TextureFormat.RGBAFloat, false, true)
            {
                name = "Racing line", wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            };
            map.SetPixels(TrackTextures.Line(along, fromLeft, line.Curvature, texels));
            map.Apply(false, true);
            Asphalt.SetTexture(LineId, map);
            Asphalt.SetFloat(LineLengthId, total);

            var height = new float[circuit.Count];
            var crossfall = new float[circuit.Count];
            var width = new float[circuit.Count];
            for (int k = 0; k < circuit.Count; k++)
            {
                height[k] = circuit.Centre[k].y;
                crossfall[k] = circuit.Data.crossfall[k];
                width[k] = 2f * circuit.HalfWidth(k);
            }
            var pools = new Texture2D(texels, 1, TextureFormat.RGBAFloat, false, true)
            {
                name = "Pools", wrapModeU = TextureWrapMode.Repeat, wrapModeV = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear,
            };
            pools.SetPixels(TrackTextures.Pools(along, height, crossfall, width, texels));
            pools.Apply(false, true);
            Asphalt.SetTexture(PoolsId, pools);
            Asphalt.SetFloat(PuddlesId, 1f);
            lineLaid = true;
            ShowOptions();
        }

        /// <summary>Sets the paved surfaces and the mown grass to the graphics options as they stand.</summary>
        public void ShowOptions()
        {
            bool detail = GraphicsOptions.TarmacDetail.On;
            foreach (Material m in new[] { Asphalt, RoadEdge, PitAsphalt, Kerb })
                if (detail) m.EnableKeyword(DetailKeyword); else m.DisableKeyword(DetailKeyword);
            if (lineLaid && GraphicsOptions.RubberedLine.On) Asphalt.EnableKeyword(RubberKeyword);
            else Asphalt.DisableKeyword(RubberKeyword);
            foreach (Material m in Paved)
                if (wet) m.EnableKeyword(WetKeyword); else m.DisableKeyword(WetKeyword);
            bool worn = GraphicsOptions.KerbWear.On;
            Kerb.SetTexture(MainTexId, worn ? kerbWorn : kerbClean);
            Kerb.SetTexture(BumpMapId, worn ? kerbWornNormals : kerbCleanNormals);
            bool stripes = GraphicsOptions.Mowing.On;
            foreach (Material m in new[] { Terrain, Grass, Verge })
                if (stripes) m.EnableKeyword(StripesKeyword); else m.DisableKeyword(StripesKeyword);
        }

        /// <summary>
        /// Draws the paved surfaces wet or dry. Wet, the track shader darkens and glosses them, stands puddles and ripples
        /// them from the weather's globals (<see cref="WeatherView"/>); dry, that part of the shader costs nothing.
        /// </summary>
        public void ShowWet(bool on)
        {
            if (on == wet) return;
            wet = on;
            ShowOptions();
        }

        /// <summary>
        /// A paved surface on the track shader. Its mesh's uvs are metres over 4 m, as the road's are. Porosity is how
        /// much it darkens wet. It holds no puddles until <see cref="LayRubber"/> lays where they pool.
        /// </summary>
        private static Material Track(Shader shader, string name, Texture2D albedo, Texture2D normals, Texture2D macro, float smoothness, float patchiness, float porosity)
        {
            var m = new Material(shader) { name = name, color = Color.white };
            m.SetTexture(MainTexId, albedo);
            m.SetTexture(BumpMapId, normals);
            m.SetFloat(BumpScaleId, 1f);
            m.SetTexture(MacroId, macro);
            m.SetFloat(MacroStrengthId, patchiness);
            m.SetVector(UvMetresId, new Vector4(4f, 4f, 0f, 0f));
            m.SetFloat(Lit.SmoothnessId, smoothness);
            m.SetFloat(PorosityId, porosity);
            m.SetFloat(PuddlesId, 0f);
            return m;
        }

        private static Texture2D Texture(string name, Color[] pixels, int width, int height, bool linear)
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, true, linear)
            {
                name = name, wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8,
            };
            tex.SetPixels(pixels);
            tex.Apply(true, true);
            return tex;
        }

        private static Material Make(string name, Texture2D texture, float smoothness, Color? colour = null, float metallic = 0f)
        {
            var m = new Material(Lit.Shader) { name = name };
            m.color = colour ?? Color.white;
            if (texture != null) m.mainTexture = texture;
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", metallic);
            return m;
        }

        /// <summary>A tileable grainy texture, one tile per metre-ish surface patch.</summary>
        private static Texture2D Noise(Color baseColour, float amount, int seed)
        {
            const int size = 128;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            var rng = new System.Random(seed);
            var px = new Color[size * size];
            for (int i = 0; i < px.Length; i++)
            {
                float v = (float)(rng.NextDouble() - 0.5) * 2f * amount;
                px[i] = new Color(Mathf.Clamp01(baseColour.r + v), Mathf.Clamp01(baseColour.g + v), Mathf.Clamp01(baseColour.b + v), 1);
            }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>
        /// Stacked tyres seen from the side, as <see cref="CircuitBuilder"/> lays the tyre walls' faces: across the
        /// texture runs down the stack from its top, one tile its whole height, and along it two tyres side by side.
        /// Each tyre's tread darkens towards its rounded shoulders and into the gap between neighbours. The top course
        /// is painted red and white by turns so the wall reads from across the gravel; below ground it is plain rubber.
        /// </summary>
        private static Texture2D TyreWall()
        {
            const int across = 96, along = 64;   // texels down the stack, texels along two tyres
            const float course = 0.25f;          // one tyre's tread width, m
            float height = CircuitBuilder.TyreTop + CircuitBuilder.TyreFoot;
            var tex = new Texture2D(across, along, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Trilinear, anisoLevel = 8 };
            var px = new Color[across * along];
            Color rubber = new Color(0.07f, 0.07f, 0.075f), red = new Color(0.72f, 0.1f, 0.08f), white = new Color(0.86f, 0.86f, 0.84f);
            var rng = new System.Random(15);
            for (int y = 0; y < along; y++)
                for (int x = 0; x < across; x++)
                {
                    float down = (x + 0.5f) / across * height;               // m below the top of the stack
                    float a = (y + 0.5f) / along * 2f;                        // tyres along, 0 to 2
                    int tyre = Mathf.FloorToInt(a);
                    float round = Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow((a - tyre) * 2f - 1f, 2f)));   // 1 at a tyre's front, 0 where two meet
                    float inCourse = Mathf.Repeat(down, course) / course;
                    float shoulder = Mathf.Sqrt(Mathf.Max(0f, 1f - Mathf.Pow(inCourse * 2f - 1f, 4f)));
                    Color c = down >= CircuitBuilder.TyreTop ? rubber : down < course ? (tyre == 0 ? red : white) : rubber;
                    float shade = down >= CircuitBuilder.TyreTop ? 1f : Mathf.Lerp(0.25f, 1f, round) * Mathf.Lerp(0.45f, 1f, shoulder);
                    shade *= 1f + (float)(rng.NextDouble() - 0.5) * 0.12f;
                    px[y * across + x] = new Color(c.r * shade, c.g * shade, c.b * shade, 1f);
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>Two-colour stripes, one pair per texture tile; diagonal for hatching.</summary>
        private static Texture2D Stripes(Color a, Color b, bool diagonal)
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 8 };
            var px = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int t = diagonal ? (x + y) % size : y;
                    px[y * size + x] = t < size / 2 ? a : b;
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }
    }
}
