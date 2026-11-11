using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>The kit a crew moves at a stop, one of each in every garage with a car.</summary>
    public enum KitItem
    {
        Nozzle, GunFrontLeft, GunFrontRight, GunRearLeft, GunRearRight, Lance, Lollipop, Extinguisher,
        TyreFrontLeft, TyreFrontRight, TyreRearLeft, TyreRearRight, Trolley,
    }

    /// <summary>The shapes the kit is drawn with: the four wheel guns share one, as do the four tyres.</summary>
    public enum KitModel { Nozzle, Gun, Lance, Lollipop, Extinguisher, Tyre, Trolley, FuelLine, AirLine }

    /// <summary>The paints the kit is drawn in. Team takes the garage's colour.</summary>
    public enum KitPaint { Metal, Dark, Red, White, Team }

    /// <summary>Where a piece of kit is and which way it faces: its own z along Forward, its own y up.</summary>
    public readonly struct KitPose
    {
        public readonly Vector3 Position;
        /// <summary>Flat unit vector the piece's own z points along.</summary>
        public readonly Vector3 Forward;

        public KitPose(Vector3 position, Vector3 forward)
        {
            Position = position;
            Forward = forward;
        }
    }

    /// <summary>
    /// The pit box equipment, laid out from the circuit with no Unity objects so it can be checked outside Unity.
    /// Over every box an overhead gantry reaches out from the garage's fascia, carrying the fuel hose from the fuel
    /// rig inside the garage and an air line for each wheel gun and for the air jack lance. Each gun, the lance and
    /// the fuel nozzle hang on their lines below the boom, clear over the car, until the crew pull them down. The
    /// lollipop, the fire extinguisher and the trolley of four fresh tyres stand inside the door, out of the crew's
    /// clear front. Every piece the crew can move has a rest place here (<see cref="Rest"/>); the hanging ones also
    /// have the point their line leaves the boom (<see cref="Anchor"/>), so a line can be drawn to wherever the piece
    /// is carried. The boom and the hose runs never move, so they are built into the garage meshes (<see cref="Boom"/>).
    /// </summary>
    public sealed class PitBoxKit
    {
        /// <summary>Where a car stands in its box: this far out from the garage's middle, along the row's Out, m.</summary>
        public const float BoxOut = 13.1f;
        /// <summary>Underside of the boom over the box, m above the garage floor.</summary>
        public const float BoomUnder = 4.95f;
        /// <summary>A hanging piece's line joins it this high at rest, m: every piece hangs well clear over a car.</summary>
        public const float HangAt = 3.05f;
        /// <summary>Beam section of the boom, m.</summary>
        public const float Beam = 0.16f;
        /// <summary>Front face of the fascia beam over the doors, where the boom is hung, m out from the garage's middle.</summary>
        public const float Fascia = 9.15f;
        /// <summary>Underside of the fascia beam, m.</summary>
        public const float FasciaUnder = 6.9f;
        /// <summary>Hose runs pass under the raised door's bottom rail this high, m.</summary>
        public const float DoorUnder = 3.95f;
        /// <summary>Fresh tyres stand this far apart on their trolley, m.</summary>
        public const float TyrePitch = 0.88f;
        public const float TyreRadius = 0.34f, TyreWidth = 0.31f;
        public const float FuelRadius = 0.035f, AirRadius = 0.013f;
        public const int ItemCount = 13;

        private const int Sides = 8;
        /// <summary>The gantry's two arms out from the fascia, x across the garage, one over each axle.</summary>
        public static readonly float[] Arms = { -1.6f, 1.6f };

        private readonly PitComplex pc;

        public PitBoxKit(PitComplex complex)
        {
            pc = complex;
        }

        // ---------------------------------------------------------------- what each piece is

        public static KitModel ModelOf(KitItem item)
        {
            switch (item)
            {
                case KitItem.Nozzle: return KitModel.Nozzle;
                case KitItem.Lance: return KitModel.Lance;
                case KitItem.Lollipop: return KitModel.Lollipop;
                case KitItem.Extinguisher: return KitModel.Extinguisher;
                case KitItem.Trolley: return KitModel.Trolley;
                case KitItem.TyreFrontLeft: case KitItem.TyreFrontRight: case KitItem.TyreRearLeft: case KitItem.TyreRearRight: return KitModel.Tyre;
                default: return KitModel.Gun;
            }
        }

        /// <summary>The piece hangs on a line from the boom: the nozzle, the wheel guns and the lance.</summary>
        public static bool Hangs(KitItem item) => item <= KitItem.Lance;

        /// <summary>The line's model and radius for a hanging piece: the fuel hose for the nozzle, an air line for the rest.</summary>
        public static KitModel LineOf(KitItem item) => item == KitItem.Nozzle ? KitModel.FuelLine : KitModel.AirLine;

        public static float LineRadius(KitItem item) => item == KitItem.Nozzle ? FuelRadius : AirRadius;

        /// <summary>
        /// How long a hanging piece's line is from the boom, m: long enough to reach the filler, a hub or the jack
        /// socket with a little to spare, so it hangs in a shallow curve there. At rest the spare is gathered up on the boom.
        /// </summary>
        public static float LineLength(KitItem item) => item == KitItem.Nozzle ? 5.2f : item == KitItem.Lance ? 5.4f : 5.1f;

        /// <summary>The wheel a gun or tyre is for: 0 front left, 1 front right, 2 rear left, 3 rear right. Left and right are the car's.</summary>
        public static int Wheel(KitItem item) =>
            item >= KitItem.TyreFrontLeft && item <= KitItem.TyreRearRight ? item - KitItem.TyreFrontLeft : item - KitItem.GunFrontLeft;

        // ---------------------------------------------------------------- where it rests

        /// <summary>
        /// Where a hanging piece's line leaves the gantry, (x across, z out) in the garage's frame: a gun over each
        /// wheel, the nozzle over the filler and the lance behind the car. The car stands
        /// nose towards -x with its left side towards the garage, as <see cref="PitStopSpots"/> has it.
        /// </summary>
        public Vector2 AnchorLocal(KitItem item)
        {
            float near = BoxOut - 1.6f, far = BoxOut + 1.6f;
            switch (item)
            {
                case KitItem.Nozzle: return new Vector2(0.95f, near);
                case KitItem.GunFrontLeft: return new Vector2(-1.6f, near);
                case KitItem.GunFrontRight: return new Vector2(-1.6f, far);
                case KitItem.GunRearLeft: return new Vector2(1.6f, near);
                case KitItem.GunRearRight: return new Vector2(1.6f, far);
                default: return new Vector2(3.25f, BoxOut);
            }
        }

        /// <summary>Where a hanging piece's line leaves the underside of the boom over garage g's box.</summary>
        public Vector3 Anchor(int garage, KitItem item)
        {
            Vector2 a = AnchorLocal(item);
            return pc.Frame(garage).At(a.x, BoomUnder, a.y);
        }

        /// <summary>
        /// Where a piece rests in garage g, and which way it faces. A hanging piece's position is where its line
        /// joins it, straight under its anchor; a standing piece's is the middle of its foot on the floor; a tyre's
        /// is its middle.
        /// </summary>
        public KitPose Rest(int garage, KitItem item)
        {
            GarageFrame f = pc.Frame(garage);
            float door = pc.Depth * 0.5f;
            if (Hangs(item))
            {
                Vector2 a = AnchorLocal(item);
                // The guns on the car's right hang facing the garage, the rest face out: a gun's socket points along its own z.
                bool right = item == KitItem.GunFrontRight || item == KitItem.GunRearRight;
                return new KitPose(f.At(a.x, HangAt, a.y), right ? -f.Out : f.Out);
            }
            switch (item)
            {
                case KitItem.Lollipop: return new KitPose(f.At(TrolleyX + 0.75f, 0f, door - 0.75f), f.Out);
                case KitItem.Extinguisher:
                {
                    float z = door - 0.55f;
                    return new KitPose(f.At(pc.RightWall(garage, z) - 0.3f, 0f, z), f.Out);
                }
                case KitItem.Trolley: return new KitPose(f.At(TrolleyX, 0f, TrolleyZ), f.Out);
                default:
                {
                    // Front tyres nearest the door, each standing on its tread, axle along the row's Out.
                    int w = Wheel(item);
                    float z = TrolleyZ + (1.5f - w) * TyrePitch;
                    return new KitPose(f.At(TrolleyX, TrolleyDeck + TyreRadius, z), f.Out);
                }
            }
        }

        /// <summary>The tyre trolley's middle, across and out, by the -x wall inside the door.</summary>
        public float TrolleyX => pc.LeftWall + 1.05f;
        public float TrolleyZ => pc.Depth * 0.5f - 2.55f;
        /// <summary>Top of the trolley's deck, m.</summary>
        public const float TrolleyDeck = 0.26f;

        // ---------------------------------------------------------------- the boom

        /// <summary>
        /// Builds garage g's boom, a steel gantry, into the garage meshes: its beams in structure (with every corner
        /// on uv, if given), the hangers up to the fascia, and the hose runs in hoses. The fuel hose
        /// runs from the top of the fuel rig inside the garage, under the raised door, up to the boom and along it to
        /// the nozzle's anchor; an air line runs from the garage ceiling the same way to every other anchor.
        /// </summary>
        public void Boom(int garage, MeshKit structure, Vector2? uv, MeshKit hoses)
        {
            GarageFrame f = pc.Frame(garage);
            void Bar(float x0, float z0, float x1, float z1, float y)
            {
                Vector3 a = f.At(x0, y, z0), b = f.At(x1, y, z1);
                float len = (b - a).magnitude;
                bool acrossRow = Mathf.Abs(x1 - x0) > Mathf.Abs(z1 - z0);
                structure.Box((a + b) * 0.5f, acrossRow ? f.Out : f.Side, acrossRow ? f.Side : f.Out, new Vector3(Beam, Beam, len + Beam), uv);
            }
            // Every beam has its underside at BoomUnder, the arms and the beams across them meeting flush.
            float armY = BoomUnder + Beam * 0.5f;
            void Hanger(float x)
            {
                // A strap from the fascia's underside down to the arm, as the arm meets the garage, and a clamp round the arm.
                float y0 = armY + Beam * 0.5f, y1 = FasciaUnder;
                structure.Box(f.At(x, (y0 + y1) * 0.5f, Fascia - 0.12f), f.Side, f.Out, new Vector3(0.1f, y1 - y0, 0.1f), uv);
                structure.Box(f.At(x, armY, Fascia - 0.12f), f.Side, f.Out, new Vector3(0.3f, Beam * 1.4f, 0.3f), uv);
            }
            float near = BoxOut - 1.6f, far = BoxOut + 1.6f;
            // The two arms out from the fascia, and a frame on them over the whole car.
            foreach (float x in Arms) Bar(x, Fascia - 0.2f, x, far + 0.1f, armY);
            Bar(-2.4f, near - 0.08f, 3.4f, near - 0.08f, armY);
            Bar(-2.4f, far + 0.08f, 3.4f, far + 0.08f, armY);
            Bar(-2.4f, near - 0.08f, -2.4f, far + 0.08f, armY);
            Bar(3.4f, near - 0.08f, 3.4f, far + 0.08f, armY);
            foreach (float x in Arms) Hanger(x);

            // Hose runs. Each takes its own place beside the arm it follows so no two lie in each other.
            int k = 0;
            for (KitItem item = KitItem.Nozzle; item <= KitItem.Lance; item++, k++)
            {
                Vector2 a = AnchorLocal(item);
                float arm = a.x < 0f ? Arms[0] : Arms[1];
                float x = arm + (k - 3) * 0.045f;
                float under = BoomUnder - 0.03f - LineRadius(item);
                var run = new List<Vector3>
                {
                    f.At(a.x, BoomUnder, a.y),
                    f.At(a.x, under, a.y),
                    f.At(x, under, a.y),
                    f.At(x, under, Fascia + 0.25f),
                    f.At(x, DoorUnder, pc.Depth * 0.5f + 0.35f),
                    f.At(x, DoorUnder, pc.Depth * 0.5f - 0.6f),
                };
                if (item == KitItem.Nozzle)
                {
                    // Back across the ceiling and down to the top of the fuel rig.
                    Vector3 top = FuelRigTop(garage);
                    float rx = Vector3.Dot(top - f.Floor, f.Side), rz = Vector3.Dot(top - f.Floor, f.Out);
                    run.Add(f.At(rx, DoorUnder, pc.Depth * 0.5f - 0.6f));
                    run.Add(f.At(rx, DoorUnder, rz));
                    run.Add(top);
                }
                else run.Add(f.At(x, 6.8f, pc.Depth * 0.5f - 1.4f));
                Pipe(hoses, run, LineRadius(item), item == KitItem.Nozzle ? 6 : 4);
            }
        }

        /// <summary>The fuel rig's tank tower, by the +x wall inside the door: middle across and out, and its height.</summary>
        public Vector2 FuelRigAt(int garage)
        {
            float z = pc.Depth * 0.5f - 2.4f;
            return new Vector2(pc.RightWall(garage, pc.Depth * 0.5f) - 0.5f, z);
        }

        public const float FuelRigHeight = 2.4f;

        /// <summary>Where the fuel hose leaves the top of the fuel rig.</summary>
        public Vector3 FuelRigTop(int garage)
        {
            Vector2 r = FuelRigAt(garage);
            return pc.Frame(garage).At(r.x, FuelRigHeight, r.y + 0.3f);
        }

        // ---------------------------------------------------------------- the moving kit's shapes

        /// <summary>
        /// A piece's shape in its own frame (x right, y up, z forward), a mesh for each paint it is drawn in. A
        /// hanging piece hangs below its origin, where its line joins it; a line is a metre long, down from its origin,
        /// and is stretched to length.
        /// </summary>
        public static Dictionary<KitPaint, MeshKit> Model(KitModel model)
        {
            var parts = new Dictionary<KitPaint, MeshKit>();
            MeshKit P(KitPaint paint) => parts.TryGetValue(paint, out MeshKit k) ? k : parts[paint] = new MeshKit();
            Vector3 X = Vector3.right, Y = Vector3.up, Z = Vector3.forward;
            switch (model)
            {
                case KitModel.Nozzle:
                    // Hose collar, the coupling's barrel hanging down, and a handle bar across it.
                    Cylinder(P(KitPaint.Dark), Vector3.zero, -Y * 0.12f, 0.05f, Sides);
                    Cylinder(P(KitPaint.Metal), -Y * 0.12f, -Y * 0.5f, 0.065f, Sides);
                    Cylinder(P(KitPaint.Metal), -Y * 0.5f, -Y * 0.58f, 0.045f, Sides);
                    P(KitPaint.Red).Box(-Y * 0.26f + Z * 0.11f, X, Z, new Vector3(0.04f, 0.22f, 0.04f));
                    P(KitPaint.Red).Box(-Y * 0.16f + Z * 0.08f, X, Z, new Vector3(0.04f, 0.04f, 0.1f));
                    P(KitPaint.Red).Box(-Y * 0.37f + Z * 0.08f, X, Z, new Vector3(0.04f, 0.04f, 0.1f));
                    break;
                case KitModel.Gun:
                    // The air line's swivel, the gun's body hanging under it, a grip each side and the socket out front.
                    Cylinder(P(KitPaint.Dark), Vector3.zero, -Y * 0.06f, 0.02f, 6);
                    P(KitPaint.Metal).Box(-Y * 0.15f, X, Z, new Vector3(0.11f, 0.18f, 0.24f));
                    P(KitPaint.Dark).Box(-Y * 0.12f + X * 0.13f, X, Z, new Vector3(0.15f, 0.04f, 0.04f));
                    P(KitPaint.Dark).Box(-Y * 0.12f - X * 0.13f, X, Z, new Vector3(0.15f, 0.04f, 0.04f));
                    Cylinder(P(KitPaint.Dark), -Y * 0.17f + Z * 0.12f, -Y * 0.17f + Z * 0.22f, 0.035f, 6);
                    break;
                case KitModel.Lance:
                    // The air jack's lance: a T handle and a long steel tube with its nose coupling.
                    P(KitPaint.Dark).Box(-Y * 0.05f, X, Z, new Vector3(0.3f, 0.04f, 0.04f));
                    Cylinder(P(KitPaint.Metal), -Y * 0.05f, -Y * 0.95f, 0.022f, 6);
                    Cylinder(P(KitPaint.Red), -Y * 0.95f, -Y * 1.05f, 0.03f, 6);
                    break;
                case KitModel.Lollipop:
                    // A weighted foot, a pole, and the round board facing along z, white rimmed.
                    Cylinder(P(KitPaint.Dark), Vector3.zero, Y * 0.06f, 0.2f, Sides);
                    Cylinder(P(KitPaint.Metal), Y * 0.06f, Y * 1.75f, 0.018f, 6);
                    Cylinder(P(KitPaint.White), Y * 2.05f - Z * 0.012f, Y * 2.05f + Z * 0.012f, 0.3f, 16);
                    Cylinder(P(KitPaint.Red), Y * 2.05f + Z * 0.012f, Y * 2.05f + Z * 0.02f, 0.26f, 16);
                    Cylinder(P(KitPaint.Red), Y * 2.05f - Z * 0.012f, Y * 2.05f - Z * 0.02f, 0.26f, 16);
                    break;
                case KitModel.Extinguisher:
                    Cylinder(P(KitPaint.Red), Vector3.zero, Y * 0.52f, 0.09f, Sides);
                    Cylinder(P(KitPaint.Dark), Y * 0.52f, Y * 0.6f, 0.035f, 6);
                    P(KitPaint.Dark).Box(Y * 0.62f + Z * 0.04f, X, Z, new Vector3(0.03f, 0.03f, 0.14f));
                    Cylinder(P(KitPaint.Dark), Y * 0.58f + X * 0.03f, Y * 0.2f + X * 0.1f, 0.012f, 4);
                    break;
                case KitModel.Tyre:
                    // Rubber sidewalls, the tread wrapped in a blanket of the team's colour.
                    Cylinder(P(KitPaint.Team), -Z * (TyreWidth * 0.5f), Z * (TyreWidth * 0.5f), TyreRadius + 0.005f, 12, false);
                    Cylinder(P(KitPaint.Dark), -Z * (TyreWidth * 0.5f + 0.01f), Z * (TyreWidth * 0.5f + 0.01f), TyreRadius - 0.04f, 12);
                    break;
                case KitModel.Trolley:
                {
                    float len = TyrePitch * 4f + 0.2f;
                    P(KitPaint.Metal).Box(Y * (TrolleyDeck - 0.02f), X, Z, new Vector3(0.5f, 0.04f, len));
                    foreach (float s in new[] { -1f, 1f })
                    {
                        P(KitPaint.Metal).Box(X * (s * 0.27f) + Y * (TrolleyDeck + 0.08f), X, Z, new Vector3(0.03f, 0.03f, len));
                        P(KitPaint.Metal).Box(X * (s * 0.27f) + Y * (TrolleyDeck + 0.04f) + Z * (len * 0.5f - 0.03f), X, Z, new Vector3(0.03f, 0.1f, 0.03f));
                        P(KitPaint.Metal).Box(X * (s * 0.27f) + Y * (TrolleyDeck + 0.04f) - Z * (len * 0.5f - 0.03f), X, Z, new Vector3(0.03f, 0.1f, 0.03f));
                        foreach (float e in new[] { -1f, 1f })
                            Cylinder(P(KitPaint.Dark), X * (s * 0.2f - 0.025f) + Y * 0.08f + Z * (e * (len * 0.5f - 0.15f)),
                                X * (s * 0.2f + 0.025f) + Y * 0.08f + Z * (e * (len * 0.5f - 0.15f)), 0.08f, 8);
                    }
                    // A handle up at the door end.
                    P(KitPaint.Metal).Box(Y * 0.6f + Z * (len * 0.5f), X, Z, new Vector3(0.5f, 0.03f, 0.03f));
                    foreach (float s in new[] { -1f, 1f })
                        P(KitPaint.Metal).Box(X * (s * 0.24f) + Y * 0.42f + Z * (len * 0.5f), X, Z, new Vector3(0.03f, 0.36f, 0.03f));
                    break;
                }
                case KitModel.FuelLine:
                    Cylinder(P(KitPaint.Dark), Vector3.zero, -Y, FuelRadius, Sides);
                    break;
                default:
                    Cylinder(P(KitPaint.Dark), Vector3.zero, -Y, AirRadius, 5);
                    break;
            }
            return parts;
        }

        // ---------------------------------------------------------------- shapes

        /// <summary>A line slung from a to b, sagging by sag metres at its middle: the points it passes, a to b.</summary>
        public static Vector3[] Slung(Vector3 a, Vector3 b, float sag, int points = 12)
        {
            var p = new Vector3[points];
            for (int i = 0; i < points; i++)
            {
                float t = i / (points - 1f);
                p[i] = Vector3.Lerp(a, b, t) + Vector3.down * (4f * sag * t * (1f - t));
            }
            return p;
        }

        /// <summary>
        /// A hose's sag from a to b: a hose a little longer than the gap hangs in a shallow curve, and lies near
        /// straight once stretched.
        /// </summary>
        public static float Sag(Vector3 a, Vector3 b, float length)
        {
            float gap = (b - a).magnitude;
            return gap >= length ? 0f : Mathf.Sqrt(3f / 8f * length * (length - gap));
        }

        /// <summary>
        /// A round pipe of radius r along the points, closed at both ends. Where it bends, the two lengths share one
        /// ring of corners, mitred across the bend, so the pipe stays whole and costs no more than its lengths.
        /// </summary>
        public static void Pipe(MeshKit kit, IList<Vector3> points, float r, int sides = 6)
        {
            var pts = new List<Vector3>();
            foreach (Vector3 p in points)
                if (pts.Count == 0 || (p - pts[pts.Count - 1]).sqrMagnitude > 1e-6f) pts.Add(p);
            if (pts.Count < 2) return;
            int n = pts.Count;
            var dirs = new Vector3[n - 1];
            for (int i = 0; i < n - 1; i++) dirs[i] = (pts[i + 1] - pts[i]).normalized;
            // Each ring's corners: the first ring square to the first length, every later one carried along by
            // projecting the last ring's onto the mitre plane, so the pipe never twists.
            var rings = new Vector3[n][];
            var outs = new Vector3[n][];
            Vector3 d0 = dirs[0];
            Vector3 u = Mathf.Abs(d0.y) < 0.9f ? Vector3.Cross(d0, Vector3.up).normalized : Vector3.Cross(d0, Vector3.right).normalized;
            Vector3 v = Vector3.Cross(d0, u);
            for (int i = 0; i < n; i++)
            {
                rings[i] = new Vector3[sides];
                outs[i] = new Vector3[sides];
                Vector3 din = i == 0 ? dirs[0] : dirs[i - 1], dout = i == n - 1 ? dirs[n - 2] : dirs[i];
                Vector3 plane = (din + dout).normalized;
                if (plane.sqrMagnitude < 1e-6f) plane = dout;
                for (int k = 0; k < sides; k++)
                {
                    float ang = k * Mathf.PI * 2f / sides;
                    Vector3 o = i == 0 ? (u * Mathf.Cos(ang) + v * Mathf.Sin(ang)) * r : rings[i - 1][k] - pts[i - 1];
                    // Slide the last ring's corner along the incoming length onto this joint's mitre plane.
                    float t = i == 0 ? 0f : -Vector3.Dot(o, plane) / Mathf.Max(Vector3.Dot(din, plane), 1e-4f);
                    Vector3 off = o + din * t;
                    rings[i][k] = pts[i] + off;
                    Vector3 radial = off - dout * Vector3.Dot(off, dout);
                    outs[i][k] = radial.sqrMagnitude > 1e-8f ? radial.normalized : o.normalized;
                }
            }
            for (int i = 0; i < n - 1; i++)
                for (int k = 0; k < sides; k++)
                {
                    int k1 = (k + 1) % sides;
                    Vector3 face = outs[i][k] + outs[i][k1];
                    Wound(kit, rings[i][k], rings[i + 1][k], rings[i][k1], outs[i][k], outs[i + 1][k], outs[i][k1], face);
                    Wound(kit, rings[i][k1], rings[i + 1][k], rings[i + 1][k1], outs[i][k1], outs[i + 1][k], outs[i + 1][k1], face);
                }
            for (int k = 0; k < sides; k++)
            {
                int k1 = (k + 1) % sides;
                Wound(kit, pts[0], rings[0][k], rings[0][k1], -dirs[0], -dirs[0], -dirs[0], -dirs[0]);
                Wound(kit, pts[n - 1], rings[n - 1][k], rings[n - 1][k1], dirs[n - 2], dirs[n - 2], dirs[n - 2], dirs[n - 2]);
            }
        }

        /// <summary>A cylinder of radius r from a to b with the given sides, its ends closed unless told not to.</summary>
        public static void Cylinder(MeshKit kit, Vector3 a, Vector3 b, float r, int sides, bool caps = true)
        {
            Vector3 ax = (b - a).normalized;
            Vector3 u = Mathf.Abs(ax.y) < 0.9f ? Vector3.Cross(ax, Vector3.up).normalized : Vector3.Cross(ax, Vector3.right).normalized;
            Vector3 v = Vector3.Cross(ax, u);
            for (int i = 0; i < sides; i++)
            {
                float a0 = i * Mathf.PI * 2f / sides, a1 = (i + 1) * Mathf.PI * 2f / sides;
                Vector3 n0 = u * Mathf.Cos(a0) + v * Mathf.Sin(a0), n1 = u * Mathf.Cos(a1) + v * Mathf.Sin(a1);
                Vector3 p0 = a + n0 * r, p1 = a + n1 * r, q0 = b + n0 * r, q1 = b + n1 * r;
                Wound(kit, p0, q0, p1, n0, n0, n1, n0 + n1);
                Wound(kit, p1, q0, q1, n1, n0, n1, n0 + n1);
                if (!caps) continue;
                Wound(kit, a, p0, p1, -ax, -ax, -ax, -ax);
                Wound(kit, b, q0, q1, ax, ax, ax, ax);
            }
        }

        /// <summary>One triangle, its corners swapped if need be so it faces the given way in Unity's winding.</summary>
        private static void Wound(MeshKit kit, Vector3 a, Vector3 b, Vector3 c, Vector3 na, Vector3 nb, Vector3 nc, Vector3 facing)
        {
            Vector2 uv = new Vector2(0.5f, 0.5f);
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), facing) < 0f) kit.Triangle(a, c, b, na, nc, nb, uv, uv, uv);
            else kit.Triangle(a, b, c, na, nb, nc, uv, uv, uv);
        }
    }
}
