using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.TeamHq
{
    /// <summary>Triangles for one material of the team building.</summary>
    public sealed class HqPart
    {
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> Uvs = new List<Vector2>();
        public readonly List<int> Triangles = new List<int>();
    }

    /// <summary>A light in the building: point lights hang under the lamps, spots light the trophy cases.</summary>
    public struct HqLight
    {
        public Vector3 Position;
        public float Range, Intensity;
        public bool Warm;
    }

    /// <summary>A car bay: where the car stands, and the stand-in paint when no model is loaded.</summary>
    public struct HqBay
    {
        public Vector3 Position;
        public string Paint;
    }

    /// <summary>
    /// The team's home workshop as triangles, built in code like the circuit. Layout and looks match
    /// the approved viewer (Team HQ Blockout) with Ivan's picks: grey epoxy floor, painted block walls
    /// with a dark dado, charcoal trim, rolling tool chests, jacks and stands, round high-bay lamps,
    /// glazed bay doors, walnut office furniture and glass trophy cabinets.
    ///
    /// Everything is laid out in the viewer's frame (right-handed, +Z towards the bay doors) and
    /// mirrored into Unity's frame (+Z away from the doors) at the end, so the numbers here read the
    /// same as the viewer's. Metres throughout.
    /// </summary>
    public sealed class HqGeometry
    {
        public readonly Dictionary<string, HqPart> Parts = new Dictionary<string, HqPart>();
        public readonly List<HqLight> Lights = new List<HqLight>();
        public readonly List<HqBay> Bays = new List<HqBay>();

        // Footprint 26 x 14 m, ceiling 7.5 m, office floor 3.5 m.
        public const float W = 26f, D = 14f, H = 7.5f, MZ = 3.5f;
        private const float X0 = -W / 2, X1 = W / 2, Z0 = -D / 2, Z1 = D / 2;
        /// <summary>The office's glass front, and the mezzanine's front edge.</summary>
        private const float MZF = -2.5f;
        /// <summary>Bay centres, left to right seen from the doors. The race car stands in the second.</summary>
        public static readonly float[] BayX = { -8f, -3f, 2f, 7f };
        private const float CarZ = 1.5f;
        private const float DoorW = 3.6f, DoorH = 3.4f, DadoH = 1.2f;
        private static readonly float[] PostX = { -10.5f, -5.5f, -0.5f, 4.5f, 9.5f };

        public HqGeometry()
        {
            Shell();
            Mezzanine();
            Stairs();
            Garage();
            Office();
            TrophyRoom();
            MirrorIntoUnity();
        }

        private void Shell()
        {
            Plane("floor", W, D, V(0, 0, 0), Rot.X(-90), 2f);
            Plane("ceiling", W, D, V(0, H, 0), Rot.X(90), 1f);
            for (float x = X0 + 2; x < X1; x += 4) Box("steel", 0.18f, 0.35f, D, V(x, H - 0.175f, 0));

            // Front wall: a glazed door per bay, block piers between, high windows above.
            float cx = X0;
            foreach (float b in BayX)
            {
                float a = b - DoorW / 2;
                WallRun(a - cx, DoorH, V((cx + a) / 2, DoorH / 2, Z1), 180);
                Plane("door", DoorW, DoorH, V(b, DoorH / 2, Z1 - 0.02f), Rot.Y(180), 0);
                for (int c = 0; c < 4; c++) Plane("window", 0.8f, 0.5f, V(b - 1.35f + c * 0.9f, DoorH / 2 + 0.55f, Z1 - 0.03f), Rot.Y(180), 0);
                for (int r = 1; r < 4; r++) Box("steel", DoorW, 0.03f, 0.03f, V(b, r * DoorH / 4, Z1 - 0.04f));
                Box("trim", 0.14f, DoorH + 0.14f, 0.12f, V(b - DoorW / 2 - 0.07f, (DoorH + 0.14f) / 2, Z1 - 0.06f));
                Box("trim", 0.14f, DoorH + 0.14f, 0.12f, V(b + DoorW / 2 + 0.07f, (DoorH + 0.14f) / 2, Z1 - 0.06f));
                Box("trim", DoorW + 0.28f, 0.14f, 0.12f, V(b, DoorH + 0.07f, Z1 - 0.06f));
                cx = b + DoorW / 2;
            }
            WallRun(X1 - cx, DoorH, V((cx + X1) / 2, DoorH / 2, Z1), 180);
            Plane("wall", W, 1f, V(0, DoorH + 0.5f, Z1), Rot.Y(180), 2f);
            Plane("window", W - 1, 2f, V(0, 5.4f, Z1), Rot.Y(180), 0);
            Plane("wall", 0.5f, 2f, V(X0 + 0.25f, 5.4f, Z1), Rot.Y(180), 2f);
            Plane("wall", 0.5f, 2f, V(X1 - 0.25f, 5.4f, Z1), Rot.Y(180), 2f);
            Plane("wall", W, H - 6.4f, V(0, (H + 6.4f) / 2, Z1), Rot.Y(180), 2f);
            for (float x = X0 + 2.5f; x < X1 - 1; x += 2.5f) Box("dark", 0.1f, 2f, 0.1f, V(x, 5.4f, Z1 - 0.06f));

            // Back wall, with high windows over the office only. End walls.
            WallRun(W, 6.2f, V(0, 3.1f, Z0), 0);
            Plane("wall", 6, 0.9f, V(-10, 6.65f, Z0), Rot.Y(0), 2f);
            Plane("window", 19, 0.9f, V(2.5f, 6.65f, Z0), Rot.Y(0), 0);
            Plane("wall", W, H - 7.1f, V(0, (H + 7.1f) / 2, Z0), Rot.Y(0), 2f);
            WallRun(D, H, V(X0, H / 2, 0), 90);
            WallRun(D, H, V(X1, H / 2, 0), -90);
        }

        /// <summary>A painted block wall facing along its yaw, with the dark dado along its foot.</summary>
        private void WallRun(float w, float h, Vector3 c, float yaw)
        {
            Plane("wall", w, h, c, Rot.Y(yaw), 2f);
            float lh = Mathf.Min(DadoH, h);
            Vector3 n = Rot.Y(yaw).Apply(V(0, 0, 0.012f));
            Plane("dado", w, lh, V(c.x + n.x, lh / 2, c.z + n.z), Rot.Y(yaw), 0);
        }

        private void Mezzanine()
        {
            Box("slab", W, 0.3f, -Z0 + MZF, V(0, MZ - 0.15f, (Z0 + MZF) / 2));
            Box("trim", W, 0.3f, 0.04f, V(0, MZ - 0.15f, MZF + 0.02f));
            foreach (float x in PostX) Box("trim", 0.22f, MZ - 0.3f, 0.22f, V(x, (MZ - 0.3f) / 2, MZF + 0.15f));
            Plane("glass", 16.5f, H - MZ, V(1.25f, MZ + (H - MZ) / 2, MZF), Rot.Y(0), 0);
            Plane("glass", 16.5f, H - MZ, V(1.25f, MZ + (H - MZ) / 2, MZF), Rot.Y(180), 0);
            for (float x = -7; x <= 9.5f; x += 2.75f) Box("dark", 0.06f, H - MZ, 0.06f, V(x, MZ + (H - MZ) / 2, MZF));
            Box("dark", 16.5f, 0.06f, 0.06f, V(1.25f, MZ + 1.05f, MZF));
            Box("trim", 1.1f, 0.05f, 0.05f, V(10.05f, MZ + 1.05f, MZF));
            Box("trim", 0.8f, 0.05f, 0.05f, V(12.6f, MZ + 1.05f, MZF));
        }

        private void Stairs()
        {
            const int steps = 18;
            const float run = 6.5f, sx = 11.4f;
            float rise = MZ / steps, tread = run / steps;
            for (int i = 0; i < steps; i++) Box("steel", 1.5f, 0.05f, tread - 0.02f, V(sx, (i + 1) * rise - 0.025f, 4 - (i + 0.5f) * tread));
            float len = Mathf.Sqrt(run * run + MZ * MZ), pitch = Mathf.Atan2(MZ, run) * Mathf.Rad2Deg;
            foreach (float x in new[] { sx - 0.8f, sx + 0.8f })
            {
                Box("trim", 0.06f, 0.3f, len, V(x, MZ / 2 + 0.15f, 4 - run / 2), Rot.X(pitch));
                Box("chrome", 0.045f, 0.045f, len, V(x, MZ / 2 + 1, 4 - run / 2), Rot.X(pitch));
                for (int k = 0; k < 5; k++) { float t = k / 4f; Box("chrome", 0.03f, 1, 0.03f, V(x, t * MZ + 0.5f, 4 - t * run)); }
            }
        }

        private void Garage()
        {
            // Bay lines.
            foreach (float x in PostX) Box("line", 0.1f, 0.004f, 8.6f, V(x, 0.002f, 2.2f));
            foreach (float b in BayX) Box("line", 4.8f, 0.004f, 0.1f, V(b, 0.002f, 6.4f));

            // Rolling tool chests under the mezzanine, three per bay.
            foreach (float b in BayX)
                for (int k = 0; k < 3; k++)
                {
                    float x = b + (k - 1) * 1.2f;
                    Box("trim", 1.05f, 0.85f, 0.5f, V(x, 0.5f, Z0 + 0.4f));
                    for (int d = 0; d < 5; d++) Box("dark", 0.95f, 0.012f, 0.01f, V(x, 0.2f + d * 0.15f, Z0 + 0.655f));
                    Box("chrome", 0.6f, 0.025f, 0.03f, V(x, 0.8f, Z0 + 0.67f));
                    if (k != 1)
                    {
                        Box("trim", 1.05f, 0.4f, 0.48f, V(x, 1.15f, Z0 + 0.4f));
                        Box("chrome", 0.6f, 0.025f, 0.03f, V(x, 1.2f, Z0 + 0.66f));
                    }
                    foreach (float o in new[] { -0.45f, 0.45f }) Cylinder("rubber", 0.04f, 0.04f, 0.03f, V(x + o, 0.04f, Z0 + 0.6f), Rot.Z(90), 10);
                }

            // A trolley jack and two axle stands in front of every car.
            foreach (float b in BayX)
            {
                Box("trim", 0.35f, 0.18f, 1.1f, V(b + 1.3f, 0.14f, CarZ + 3));
                Box("chrome", 0.06f, 0.06f, 0.9f, V(b + 1.3f, 0.45f, CarZ + 3.6f), Rot.X(28.6f));
                foreach (float s in new[] { -1f, 1f })
                {
                    Cylinder("chrome", 0.025f, 0.025f, 0.42f, V(b + s * 1.15f, 0.21f, CarZ + 1.4f), Rot.None, 8);
                    Cylinder("trim", 0.05f, 0.11f, 0.22f, V(b + s * 1.15f, 0.11f, CarZ + 1.4f), Rot.None, 4);
                }
            }

            // Round high-bay lamps over the garage floor, discs in the office ceiling.
            foreach (float z in new[] { 0.5f, 4.5f })
                for (int i = 0; i < 5; i++)
                {
                    float x = -10 + i * 5;
                    Box("dark", 0.02f, 0.9f, 0.02f, V(x, H - 0.45f, z));
                    Cylinder("dark", 0.12f, 0.36f, 0.3f, V(x, H - 1.05f, z), Rot.None, 24);
                    Cylinder("lamp", 0.34f, 0.34f, 0.02f, V(x, H - 1.21f, z), Rot.None, 24);
                }
            foreach (float x in new[] { -4f, 1f, 6f }) Cylinder("lamp", 0.25f, 0.25f, 0.04f, V(x, H - 0.05f, -4.6f), Rot.None, 24);
            foreach (float z in new[] { 0.5f, 4.5f })
                foreach (float x in new[] { -7f, 0f, 7f })
                    Lights.Add(new HqLight { Position = V(x, H - 1.4f, z), Range = 14, Intensity = 0.6f, Warm = true });

            // Crew corner: workbench, tool board, lockers and three crew.
            Box("walnut", 0.8f, 0.08f, 4.5f, V(X0 + 0.5f, 0.9f, 1.5f));
            Box("cabinet", 0.7f, 0.85f, 4.4f, V(X0 + 0.48f, 0.43f, 1.5f));
            Plane("dark", 4.5f, 1.4f, V(X0 + 0.02f, 1.85f, 1.5f), Rot.Y(90), 0);
            var rng = new System.Random(7);
            for (int i = 0; i < 14; i++)
                Box("chrome", 0.02f, 0.18f + (float)rng.NextDouble() * 0.25f, 0.05f, V(X0 + 0.05f, 1.6f + (float)rng.NextDouble() * 0.6f, -0.5f + i * 0.3f));
            for (float z = -5.9f; z <= -3.4f; z += 0.55f)
            {
                Box("trim", 0.5f, 1.9f, 0.5f, V(X0 + 0.3f, 0.95f, z));
                Box("dark", 0.01f, 0.25f, 0.12f, V(X0 + 0.555f, 1.5f, z + 0.12f));
            }
            Person(-11.6f, 0.4f, 90);
            Person(-11.9f, 2.8f, 75);
            Person(-10.9f, -1.6f, 126);

            string[] paint = { "owned", "race", "owned2", "spare" };
            for (int i = 0; i < BayX.Length; i++) Bays.Add(new HqBay { Position = V(BayX[i], 0, CarZ), Paint = paint[i] });
        }

        /// <summary>A crew stand-in: two legs, a body and a head.</summary>
        private void Person(float x, float z, float yaw)
        {
            Rot r = Rot.Y(yaw);
            foreach (float dx in new[] { -0.14f, 0.14f }) Capsule("legs", 0.08f, 0.78f, V(x, 0, z) + r.Apply(V(dx, 0.42f, 0)));
            Capsule("overalls", 0.2f, 0.8f, V(x, 1.15f, z), 0.65f, r);
            Sphere("skin", 0.12f, V(x, 1.62f, z));
        }

        private void Office()
        {
            const float f = MZ;
            Plane("carpet", 16.5f, 4.5f, V(1.25f, f + 0.005f, -4.75f), Rot.X(-90), 0);

            // Drivers desk, looking out over the race car.
            Desk(-3, -4.4f, 1.8f, 0.8f);
            Chair(-3, -5.1f, 0);
            Box("paper", 0.24f, 0.06f, 0.32f, V(-3.5f, f + 0.81f, -4.3f));
            Box("folder", 0.24f, 0.04f, 0.32f, V(-3.45f, f + 0.85f, -4.32f));
            Box("screen", 0.5f, 0.32f, 0.03f, V(-2.5f, f + 0.95f, -4.6f));
            Box("dark", 0.04f, 0.18f, 0.04f, V(-2.5f, f + 0.85f, -4.62f));

            // Standings screen on the back wall.
            Box("dark", 3.3f, 1.9f, 0.06f, V(1, f + 1.9f, Z0 + 0.04f));
            Box("screen", 3.15f, 1.75f, 0.01f, V(1, f + 1.9f, Z0 + 0.075f));

            // Finances desk with two monitors.
            Desk(5, -6.35f, 2.2f, 0.8f);
            Chair(5, -5.6f, 180);
            foreach (float x in new[] { 4.55f, 5.25f })
            {
                Box("screen", 0.62f, 0.38f, 0.03f, V(x, f + 1.05f, -6.55f));
                Box("dark", 0.05f, 0.2f, 0.05f, V(x, f + 0.85f, -6.57f));
            }

            // Scouting pinboard.
            Box("frame", 2.3f, 1.4f, 0.05f, V(8.5f, f + 1.7f, Z0 + 0.03f));
            Box("cork", 2.2f, 1.3f, 0.01f, V(8.5f, f + 1.7f, Z0 + 0.06f));
            for (int i = 0; i < 8; i++) Box("paper", 0.32f, 0.24f, 0.005f, V(7.8f + (i % 4) * 0.48f, f + 1.95f - (i / 4) * 0.5f, Z0 + 0.068f));

            // Sofa and coffee table with the newspaper, facing the glass.
            Box("fabric", 2.2f, 0.42f, 0.9f, V(8, f + 0.21f, -4.7f));
            Box("fabric", 2.2f, 0.5f, 0.22f, V(8, f + 0.6f, -5.05f));
            foreach (float s in new[] { -1f, 1f }) Box("fabric", 0.2f, 0.55f, 0.9f, V(8 + s * 1.1f, f + 0.28f, -4.7f));
            Box("walnut", 1.1f, 0.05f, 0.6f, V(8, f + 0.4f, -3.4f));
            foreach (float s in new[] { -1f, 1f }) Box("dark", 0.04f, 0.38f, 0.5f, V(8 + s * 0.5f, f + 0.19f, -3.4f));
            Box("paper", 0.45f, 0.01f, 0.32f, V(8.1f, f + 0.43f, -3.4f));

            // A plant by the trophy room door.
            Cylinder("pot", 0.18f, 0.14f, 0.4f, V(-6.4f, f + 0.2f, -6.5f), Rot.None, 20);
            Sphere("leaves", 0.4f, V(-6.4f, f + 0.75f, -6.5f));

            // Race history photos on the office side of the trophy room wall.
            for (int r = 0; r < 2; r++)
                for (int c = 0; c < 3; c++)
                {
                    Box("frame", 0.04f, 0.55f, 0.75f, V(-6.91f, f + 1.3f + r * 0.8f, -6.2f + c * 0.95f));
                    Box("paper", 0.01f, 0.47f, 0.67f, V(-6.885f, f + 1.3f + r * 0.8f, -6.2f + c * 0.95f));
                }
        }

        private void Desk(float x, float z, float w, float d)
        {
            Box("walnut", w, 0.05f, d, V(x, MZ + 0.75f, z));
            foreach (float s in new[] { -1f, 1f }) Box("dark", 0.05f, 0.72f, d - 0.1f, V(x + s * (w / 2 - 0.08f), MZ + 0.37f, z));
        }

        private void Chair(float x, float z, float yaw)
        {
            Rot r = Rot.Y(yaw);
            Vector3 o = V(x, MZ, z);
            Box("leather", 0.5f, 0.08f, 0.5f, o + V(0, 0.48f, 0), r);
            Box("leather", 0.48f, 0.6f, 0.06f, o + r.Apply(V(0, 0.82f, -0.24f)), r);
            Cylinder("chrome", 0.03f, 0.03f, 0.42f, o + V(0, 0.24f, 0), Rot.None, 8);
            for (int i = 0; i < 5; i++)
            {
                float a = i * 72;
                Rot leg = Rot.Y(yaw + a);
                Box("chrome", 0.03f, 0.03f, 0.32f, o + leg.Apply(V(0, 0.04f, 0.16f)), leg);
            }
        }

        private void TrophyRoom()
        {
            const float f = MZ;
            // Partition with the door to the office, and the front wall over the garage.
            Box("partition", 0.15f, H - f, 3.3f, V(-7, f + (H - f) / 2, Z0 + 1.65f));
            Box("partition", 0.15f, H - f, 0.2f, V(-7, f + (H - f) / 2, MZF - 0.1f));
            Box("partition", 0.15f, H - f - 2.1f, 1, V(-7, f + 2.1f + (H - f - 2.1f) / 2, -3.2f));
            Box("partition", 6, H - f, 0.15f, V(-10, f + (H - f) / 2, MZF));
            Plane("velvet", 6, 4.5f, V(-10, f + 0.006f, -4.75f), Rot.X(-90), 0);

            string[] metal = { "gold", "silver", "gold", "bronze" };
            // Glass cabinets along the back wall.
            for (int i = 0; i < 5; i++)
            {
                float x = -12.4f + i * 1.1f;
                Box("plinth", 1, 0.5f, 0.5f, V(x, f + 0.25f, Z0 + 0.3f));
                Box("plinth", 1, 0.06f, 0.5f, V(x, f + 2.3f, Z0 + 0.3f));
                GlassBox(1, 1.74f, 0.5f, V(x, f + 1.4f, Z0 + 0.3f));
                for (int s = 0; s < 3; s++)
                {
                    Box("glass", 0.96f, 0.02f, 0.46f, V(x, f + 0.9f + s * 0.5f, Z0 + 0.3f));
                    Trophy(x - 0.25f + s * 0.12f, f + 0.5f + s * 0.5f, Z0 + 0.3f, 0.9f, metal[(i + s) % 4]);
                    Trophy(x + 0.22f, f + 0.5f + s * 0.5f, Z0 + 0.3f, 0.7f, metal[(i + s + 1) % 4]);
                }
                Box("lamp", 0.9f, 0.02f, 0.02f, V(x, f + 2.24f, Z0 + 0.5f));
            }
            // And along the end wall.
            for (int i = 0; i < 3; i++)
            {
                float z = -6 + i * 1.1f;
                Box("plinth", 0.5f, 0.5f, 1, V(X0 + 0.3f, f + 0.25f, z));
                GlassBox(0.5f, 1.74f, 1, V(X0 + 0.3f, f + 1.4f, z));
                for (int s = 0; s < 3; s++) Trophy(X0 + 0.3f, f + 0.5f + s * 0.5f, z, 0.85f, metal[(i + s) % 4]);
            }
            Box("leather", 1.4f, 0.45f, 0.5f, V(-10, f + 0.225f, -4.2f));
            // Press clippings above the cabinets.
            for (int i = 0; i < 4; i++)
            {
                Box("frame", 0.04f, 0.5f, 0.66f, V(X0 + 0.03f, f + 2.9f, -6.2f + i));
                Box("paper", 0.01f, 0.44f, 0.6f, V(X0 + 0.055f, f + 2.9f, -6.2f + i));
            }
            Lights.Add(new HqLight { Position = V(-10, f + 3, -4.8f), Range = 8, Intensity = 0.8f, Warm = true });
            Lights.Add(new HqLight { Position = V(-10, f + 2.2f, -6.2f), Range = 3.5f, Intensity = 0.7f, Warm = true });
        }

        private void GlassBox(float w, float h, float d, Vector3 c)
        {
            Box("glass", w, h, d, c);
        }

        /// <summary>A cup on a stem and a black base, s times the standard size.</summary>
        private void Trophy(float x, float y, float z, float s, string metal)
        {
            Cylinder("plinth", 0.07f * s, 0.1f * s, 0.08f * s, V(x, y + 0.04f * s, z), Rot.None, 12);
            Cylinder(metal, 0.025f * s, 0.03f * s, 0.15f * s, V(x, y + 0.155f * s, z), Rot.None, 10);
            Lathe(metal, new[] { 0f, 0.06f, 0.11f, 0.13f, 0.12f }, new[] { 0f, 0f, 0.06f, 0.16f, 0.22f }, s, V(x, y + 0.23f * s, z), 20);
        }

        // ---------- Primitives (viewer frame) ----------

        private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        private HqPart Part(string key)
        {
            if (!Parts.TryGetValue(key, out HqPart p)) Parts[key] = p = new HqPart();
            return p;
        }

        /// <summary>A one-sided rectangle facing local +Z. Tile is metres per texture repeat; 0 stretches it once.</summary>
        private void Plane(string key, float w, float h, Vector3 c, Rot r, float tile)
        {
            HqPart p = Part(key);
            int s = p.Vertices.Count;
            Vector3 n = r.Apply(V(0, 0, 1));
            float u = tile > 0 ? w / tile : 1, v = tile > 0 ? h / tile : 1;
            p.Vertices.Add(c + r.Apply(V(-w / 2, -h / 2, 0))); p.Uvs.Add(new Vector2(0, 0));
            p.Vertices.Add(c + r.Apply(V(w / 2, -h / 2, 0))); p.Uvs.Add(new Vector2(u, 0));
            p.Vertices.Add(c + r.Apply(V(w / 2, h / 2, 0))); p.Uvs.Add(new Vector2(u, v));
            p.Vertices.Add(c + r.Apply(V(-w / 2, h / 2, 0))); p.Uvs.Add(new Vector2(0, v));
            for (int i = 0; i < 4; i++) p.Normals.Add(n);
            // Counter-clockwise seen from the front, as in the viewer.
            p.Triangles.Add(s); p.Triangles.Add(s + 1); p.Triangles.Add(s + 2);
            p.Triangles.Add(s); p.Triangles.Add(s + 2); p.Triangles.Add(s + 3);
        }

        private void Box(string key, float w, float h, float d, Vector3 c) => Box(key, w, h, d, c, Rot.None);

        private void Box(string key, float w, float h, float d, Vector3 c, Rot r)
        {
            float x = w / 2, y = h / 2, z = d / 2;
            Plane(key, w, h, c + r.Apply(V(0, 0, z)), r, 0);
            Plane(key, w, h, c + r.Apply(V(0, 0, -z)), r * Rot.Y(180), 0);
            Plane(key, d, h, c + r.Apply(V(x, 0, 0)), r * Rot.Y(90), 0);
            Plane(key, d, h, c + r.Apply(V(-x, 0, 0)), r * Rot.Y(-90), 0);
            Plane(key, w, d, c + r.Apply(V(0, y, 0)), r * Rot.X(-90), 0);
            Plane(key, w, d, c + r.Apply(V(0, -y, 0)), r * Rot.X(90), 0);
        }

        /// <summary>A capped cylinder or cone standing on local Y, centred on c.</summary>
        private void Cylinder(string key, float rTop, float rBottom, float h, Vector3 c, Rot r, int seg)
        {
            HqPart p = Part(key);
            float slope = (rBottom - rTop) / h;
            int s = p.Vertices.Count;
            for (int i = 0; i <= seg; i++)
            {
                float a = i * 2 * Mathf.PI / seg, sin = Mathf.Sin(a), cos = Mathf.Cos(a);
                Vector3 n = r.Apply(V(sin, slope, cos).normalized);
                p.Vertices.Add(c + r.Apply(V(rTop * sin, h / 2, rTop * cos))); p.Normals.Add(n); p.Uvs.Add(new Vector2((float)i / seg, 1));
                p.Vertices.Add(c + r.Apply(V(rBottom * sin, -h / 2, rBottom * cos))); p.Normals.Add(n); p.Uvs.Add(new Vector2((float)i / seg, 0));
            }
            for (int i = 0; i < seg; i++)
            {
                int a = s + i * 2, b = a + 1, e = a + 2, g = a + 3;
                p.Triangles.Add(a); p.Triangles.Add(b); p.Triangles.Add(g);
                p.Triangles.Add(a); p.Triangles.Add(g); p.Triangles.Add(e);
            }
            Cap(p, rTop, h / 2, c, r, seg, true);
            Cap(p, rBottom, -h / 2, c, r, seg, false);
        }

        private void Cap(HqPart p, float radius, float y, Vector3 c, Rot r, int seg, bool up)
        {
            if (radius <= 0) return;
            int s = p.Vertices.Count;
            Vector3 n = r.Apply(V(0, up ? 1 : -1, 0));
            p.Vertices.Add(c + r.Apply(V(0, y, 0))); p.Normals.Add(n); p.Uvs.Add(new Vector2(0.5f, 0.5f));
            for (int i = 0; i < seg; i++)
            {
                float a = i * 2 * Mathf.PI / seg;
                p.Vertices.Add(c + r.Apply(V(radius * Mathf.Sin(a), y, radius * Mathf.Cos(a)))); p.Normals.Add(n);
                p.Uvs.Add(new Vector2(0.5f + 0.5f * Mathf.Sin(a), 0.5f + 0.5f * Mathf.Cos(a)));
            }
            for (int i = 0; i < seg; i++)
            {
                int a = s + 1 + i, b = s + 1 + (i + 1) % seg;
                p.Triangles.Add(s);
                if (up) { p.Triangles.Add(a); p.Triangles.Add(b); } else { p.Triangles.Add(b); p.Triangles.Add(a); }
            }
        }

        /// <summary>A surface of revolution about local Y through (radius, height) points, scaled by s. Seen from both sides.</summary>
        private void Lathe(string key, float[] radius, float[] height, float s, Vector3 c, int seg)
        {
            HqPart p = Part(key);
            int rows = radius.Length, start = p.Vertices.Count;
            for (int i = 0; i <= seg; i++)
            {
                float a = i * 2 * Mathf.PI / seg, sin = Mathf.Sin(a), cos = Mathf.Cos(a);
                for (int k = 0; k < rows; k++)
                {
                    int k0 = Mathf.Max(k - 1, 0), k1 = Mathf.Min(k + 1, rows - 1);
                    float dr = radius[k1] - radius[k0], dh = height[k1] - height[k0];
                    p.Vertices.Add(c + V(radius[k] * s * sin, height[k] * s, radius[k] * s * cos));
                    p.Normals.Add(V(dh * sin, -dr, dh * cos).normalized);
                    p.Uvs.Add(new Vector2((float)i / seg, (float)k / (rows - 1)));
                }
            }
            for (int i = 0; i < seg; i++)
                for (int k = 0; k < rows - 1; k++)
                {
                    int a = start + i * rows + k, b = a + 1, e = a + rows, g = e + 1;
                    p.Triangles.Add(a); p.Triangles.Add(e); p.Triangles.Add(g);
                    p.Triangles.Add(a); p.Triangles.Add(g); p.Triangles.Add(b);
                    p.Triangles.Add(a); p.Triangles.Add(g); p.Triangles.Add(e);
                    p.Triangles.Add(a); p.Triangles.Add(b); p.Triangles.Add(g);
                }
        }

        private void Sphere(string key, float radius, Vector3 c) => Ellipsoid(key, radius, radius, radius, c, Rot.None);

        private void Ellipsoid(string key, float rx, float ry, float rz, Vector3 c, Rot r)
        {
            HqPart p = Part(key);
            const int seg = 16, rings = 10;
            int s = p.Vertices.Count;
            for (int j = 0; j <= rings; j++)
            {
                float t = j * Mathf.PI / rings, st = Mathf.Sin(t), ct = Mathf.Cos(t);
                for (int i = 0; i <= seg; i++)
                {
                    float a = i * 2 * Mathf.PI / seg;
                    Vector3 unit = V(st * Mathf.Sin(a), ct, st * Mathf.Cos(a));
                    p.Vertices.Add(c + r.Apply(V(unit.x * rx, unit.y * ry, unit.z * rz)));
                    p.Normals.Add(r.Apply(V(unit.x / rx, unit.y / ry, unit.z / rz).normalized));
                    p.Uvs.Add(new Vector2((float)i / seg, 1 - (float)j / rings));
                }
            }
            for (int j = 0; j < rings; j++)
                for (int i = 0; i < seg; i++)
                {
                    int a = s + j * (seg + 1) + i, b = a + 1, e = a + seg + 1, g = e + 1;
                    p.Triangles.Add(a); p.Triangles.Add(e); p.Triangles.Add(b);
                    p.Triangles.Add(b); p.Triangles.Add(e); p.Triangles.Add(g);
                }
        }

        /// <summary>A capsule standing on local Y: a cylinder with half-sphere ends, flattened front to back by depth.</summary>
        private void Capsule(string key, float radius, float length, Vector3 c, float depth = 1, Rot? rot = null)
        {
            Rot r = rot ?? Rot.None;
            float mid = Mathf.Max(length - 2 * radius, 0.01f);
            HqPart p = Part(key);
            const int seg = 16;
            int s = p.Vertices.Count;
            for (int i = 0; i <= seg; i++)
            {
                float a = i * 2 * Mathf.PI / seg, sin = Mathf.Sin(a), cos = Mathf.Cos(a) * depth;
                Vector3 n = r.Apply(V(sin, 0, cos).normalized);
                p.Vertices.Add(c + r.Apply(V(radius * sin, mid / 2, radius * cos))); p.Normals.Add(n); p.Uvs.Add(new Vector2((float)i / seg, 1));
                p.Vertices.Add(c + r.Apply(V(radius * sin, -mid / 2, radius * cos))); p.Normals.Add(n); p.Uvs.Add(new Vector2((float)i / seg, 0));
            }
            for (int i = 0; i < seg; i++)
            {
                int a = s + i * 2, b = a + 1, e = a + 2, g = a + 3;
                p.Triangles.Add(a); p.Triangles.Add(b); p.Triangles.Add(g);
                p.Triangles.Add(a); p.Triangles.Add(g); p.Triangles.Add(e);
            }
            Ellipsoid(key, radius, radius, radius * depth, c + r.Apply(V(0, mid / 2, 0)), r);
            Ellipsoid(key, radius, radius, radius * depth, c + r.Apply(V(0, -mid / 2, 0)), r);
        }

        /// <summary>Flips Z and the winding, so the building reads the same in Unity's left-handed frame.</summary>
        private void MirrorIntoUnity()
        {
            foreach (HqPart p in Parts.Values)
            {
                for (int i = 0; i < p.Vertices.Count; i++)
                {
                    Vector3 v = p.Vertices[i], n = p.Normals[i];
                    p.Vertices[i] = V(v.x, v.y, -v.z);
                    p.Normals[i] = V(n.x, n.y, -n.z);
                }
                for (int i = 0; i < p.Triangles.Count; i += 3)
                {
                    int t = p.Triangles[i + 1];
                    p.Triangles[i + 1] = p.Triangles[i + 2];
                    p.Triangles[i + 2] = t;
                }
            }
            for (int i = 0; i < Lights.Count; i++) { HqLight l = Lights[i]; l.Position = V(l.Position.x, l.Position.y, -l.Position.z); Lights[i] = l; }
            for (int i = 0; i < Bays.Count; i++) { HqBay b = Bays[i]; b.Position = V(b.Position.x, b.Position.y, -b.Position.z); Bays[i] = b; }
        }

        /// <summary>A rotation as a 3x3 matrix, in degrees, with the viewer's (three.js) sense of positive angles.</summary>
        private struct Rot
        {
            private float a, b, c, d, e, f, g, h, i;

            public static Rot None => new Rot { a = 1, e = 1, i = 1 };

            public static Rot X(float deg)
            {
                float s = Mathf.Sin(deg * Mathf.Deg2Rad), co = Mathf.Cos(deg * Mathf.Deg2Rad);
                return new Rot { a = 1, e = co, f = -s, h = s, i = co };
            }

            public static Rot Y(float deg)
            {
                float s = Mathf.Sin(deg * Mathf.Deg2Rad), co = Mathf.Cos(deg * Mathf.Deg2Rad);
                return new Rot { a = co, c = s, e = 1, g = -s, i = co };
            }

            public static Rot Z(float deg)
            {
                float s = Mathf.Sin(deg * Mathf.Deg2Rad), co = Mathf.Cos(deg * Mathf.Deg2Rad);
                return new Rot { a = co, b = -s, d = s, e = co, i = 1 };
            }

            public Vector3 Apply(Vector3 v) => new Vector3(a * v.x + b * v.y + c * v.z, d * v.x + e * v.y + f * v.z, g * v.x + h * v.y + i * v.z);

            public static Rot operator *(Rot m, Rot n) => new Rot
            {
                a = m.a * n.a + m.b * n.d + m.c * n.g, b = m.a * n.b + m.b * n.e + m.c * n.h, c = m.a * n.c + m.b * n.f + m.c * n.i,
                d = m.d * n.a + m.e * n.d + m.f * n.g, e = m.d * n.b + m.e * n.e + m.f * n.h, f = m.d * n.c + m.e * n.f + m.f * n.i,
                g = m.g * n.a + m.h * n.d + m.i * n.g, h = m.g * n.b + m.h * n.e + m.i * n.h, i = m.g * n.c + m.h * n.f + m.i * n.i,
            };
        }
    }
}
