using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>One garage's frame: its floor middle, the row's direction, and the way out to the pit lane.</summary>
    public readonly struct GarageFrame
    {
        /// <summary>Middle of the garage floor.</summary>
        public readonly Vector3 Floor;
        /// <summary>Unit vector along the garage row, the way the garages are numbered.</summary>
        public readonly Vector3 Along;
        /// <summary>Unit vector from the back wall out through the door, towards the pit lane.</summary>
        public readonly Vector3 Out;

        public GarageFrame(Vector3 floor, Vector3 along, Vector3 @out)
        {
            Floor = floor;
            Along = along;
            Out = @out;
        }

        /// <summary>Unit vector across the garage, from its +x side wall to its -x side wall: minus <see cref="Along"/>.</summary>
        public Vector3 Side => -Along;

        /// <summary>
        /// A point in the garage: x across the bay (0 in the middle, ±width/2 at the side walls), y up from
        /// the floor, z out from the middle towards the door (+depth/2 at the door, -depth/2 at the back wall).
        /// </summary>
        public Vector3 At(float x, float y, float z) => Floor + Side * x + Vector3.up * y + Out * z;
    }

    /// <summary>A place to stand: where and which way to face.</summary>
    public readonly struct CrewSpot
    {
        public readonly Vector3 Position;
        /// <summary>Flat unit vector the crew member faces.</summary>
        public readonly Vector3 Facing;

        public CrewSpot(Vector3 position, Vector3 facing)
        {
            Position = position;
            Facing = facing;
        }
    }

    /// <summary>How a crew member waits at home: standing, talking in a group, sitting on a stool, or writing at the engineers' desk.</summary>
    public enum HomePose { Stand, Talk, Sit, Desk }

    /// <summary>Where a crew member waits in the garage, the way they face, how they wait, and their talking group (-1 for none).</summary>
    public readonly struct CrewHome
    {
        public readonly Vector3 Position, Facing;
        public readonly HomePose Pose;
        public readonly int Group;
        /// <summary>A point walked through on the way from and back to the spot, or null to walk straight.</summary>
        public readonly Vector3? Via;

        public CrewHome(Vector3 position, Vector3 facing, HomePose pose, int group, Vector3? via)
        {
            Position = position;
            Facing = facing;
            Pose = pose;
            Group = group;
            Via = via;
        }

        public bool Sits => Pose == HomePose.Sit || Pose == HomePose.Desk;
    }

    /// <summary>
    /// The pit complex laid out from the circuit, with no Unity objects so it can be tested outside Unity: each
    /// garage's frame on the floor heights the circuit builder laid, the stations along the row that the pit
    /// building is built on, where the race offices stand, how far the paddock reaches, and where a pit crew
    /// waits. Inside every garage the front <see cref="ClearDepth"/> metres are kept clear of fit-out,
    /// so the crew can run out to the box.
    /// </summary>
    public sealed class PitComplex
    {
        /// <summary>Height of a garage, floor to the top of its roof slab, m.</summary>
        public const float RoofTop = 7.3f;
        /// <summary>Depth of the paddock behind the garages' back wall, m.</summary>
        public const float PaddockDepth = 30f;
        /// <summary>The front of each garage, this deep from the door, holds no fit-out: it is the crew's, m.</summary>
        public const float ClearDepth = 6.5f;
        /// <summary>Bays the race offices span, centred on the garage beside the start line.</summary>
        public const int OfficeBays = 6;
        /// <summary>Most crew a garage has spots for, waiting inside: as many as a team can hire.</summary>
        public const int StandbySpots = 16;
        /// <summary>Half a garage side wall's thickness, as the circuit builder lays them.</summary>
        public const float WallHalf = 0.15f;

        public readonly PitData Data;
        public readonly float Width, Depth;
        /// <summary>Garage floor heights, as the circuit builder laid them.</summary>
        public readonly float[] Floors;
        /// <summary>The garage beside the start line, where the race offices stand.</summary>
        public readonly int StartGarage;
        /// <summary>First and one past the last station the race offices span.</summary>
        public readonly int OfficeFrom, OfficeTo;

        private readonly GarageFrame[] frames;
        private readonly Vector3[] stationBase, stationOut;
        private readonly float[] stationFloor;

        public PitComplex(Circuit circuit, float[] floors)
        {
            Data = circuit.Data.pit;
            Width = Data.garageWidth;
            Depth = Data.garageDepth;
            Floors = floors;
            int n = Data.garages.Length;
            frames = new GarageFrame[n];
            for (int g = 0; g < n; g++)
            {
                GarageData gd = Data.garages[g];
                float r = gd.heading * Mathf.Deg2Rad;
                var along = new Vector3(Mathf.Sin(r), 0f, Mathf.Cos(r));
                frames[g] = new GarageFrame(new Vector3(gd.x, floors[g], gd.z), along, new Vector3(along.z, 0f, -along.x));
            }

            // Station j is the corner between garage j-1 and garage j, shared halfway as the garage floors share it.
            stationBase = new Vector3[n + 1];
            stationOut = new Vector3[n + 1];
            stationFloor = new float[n + 1];
            for (int j = 0; j <= n; j++)
            {
                if (j == 0) Set(j, frames[0], Width * 0.5f, null, 0f);
                else if (j == n) Set(j, frames[n - 1], -Width * 0.5f, null, 0f);
                else Set(j, frames[j - 1], -Width * 0.5f, frames[j], Width * 0.5f);
            }
            // The floors follow the lane's fall along the row: the two end corners carry it on half a bay.
            if (n > 1)
            {
                stationFloor[0] = floors[0] - (floors[1] - floors[0]) * 0.5f;
                stationFloor[n] = floors[n - 1] + (floors[n - 1] - floors[n - 2]) * 0.5f;
            }

            Vector3 start = circuit.Centre[0];
            float best = float.MaxValue;
            for (int g = 0; g < n; g++)
            {
                float d = (Flat(frames[g].Floor) - Flat(start)).sqrMagnitude;
                if (d < best) { best = d; StartGarage = g; }
            }
            OfficeFrom = Mathf.Clamp(StartGarage - OfficeBays / 2, 0, n - OfficeBays);
            OfficeTo = OfficeFrom + OfficeBays;
        }

        private void Set(int j, GarageFrame a, float ax, GarageFrame? b, float bx)
        {
            Vector3 pa = a.Floor + a.Side * ax;
            if (b is GarageFrame other)
            {
                Vector3 pb = other.Floor + other.Side * bx;
                stationBase[j] = (pa + pb) * 0.5f;
                stationOut[j] = (a.Out + other.Out) * 0.5f;
                stationFloor[j] = (a.Floor.y + other.Floor.y) * 0.5f;
            }
            else
            {
                stationBase[j] = pa;
                stationOut[j] = a.Out;
                stationFloor[j] = a.Floor.y;
            }
        }

        public int Garages => frames.Length;
        public int Stations => stationBase.Length;

        public GarageFrame Frame(int garage) => frames[garage];

        /// <summary>
        /// A point on the row at a fractional station: z out from the garages' middle line towards the lane, y up
        /// from the floor there. Between stations the row is straight; at each station it bends.
        /// </summary>
        public Vector3 RowPoint(float station, float y, float z)
        {
            int a = Mathf.Clamp((int)station, 0, Stations - 2);
            float t = Mathf.Clamp01(station - a);
            Vector3 b = Vector3.Lerp(stationBase[a], stationBase[a + 1], t);
            Vector3 o = Vector3.Lerp(stationOut[a], stationOut[a + 1], t);
            float f = Mathf.Lerp(stationFloor[a], stationFloor[a + 1], t);
            Vector3 p = b + o * z;
            p.y = f + y;
            return p;
        }

        /// <summary>Flat unit vector out towards the lane at a fractional station.</summary>
        public Vector3 RowOut(float station)
        {
            int a = Mathf.Clamp((int)station, 0, Stations - 2);
            return Vector3.Lerp(stationOut[a], stationOut[a + 1], Mathf.Clamp01(station - a)).normalized;
        }

        /// <summary>Flat unit vector along the row at a fractional station.</summary>
        public Vector3 RowAlong(float station)
        {
            Vector3 o = RowOut(station);
            return new Vector3(-o.z, 0f, o.x);
        }

        /// <summary>
        /// Where the crew wait inside the garage, spread through it as people are: talking in small groups, watching
        /// the timing screens, at the engineers' desk and on the stools of the crew's corner. Spot 0 is nearest the
        /// door, as the first jobs handed out are the first needed at a stop; a group fills before the next one starts,
        /// so no one waits on their own facing a gap. All of it keeps out of the car bay (<see cref="CarBayFrom"/>), where the car waits
        /// before the race (<see cref="GarageStart.Parked"/>) and drives out of the door.
        /// </summary>
        public CrewHome Home(int garage, int spot)
        {
            GarageFrame f = frames[garage];
            HomeSpot h = HomeSpots[spot];
            Vector3 face = (f.Side * h.Face.x + f.Out * h.Face.y).normalized;
            Vector3? via = h.Via is Vector2 v ? f.At(v.x, 0f, v.y) : (Vector3?)null;
            return new CrewHome(f.At(h.At.x, 0f, h.At.y), face, h.Pose, h.Group, via);
        }

        /// <summary>A waiting place in a garage's own frame: x across, z out towards the door.</summary>
        public readonly struct HomeSpot
        {
            public readonly Vector2 At, Face;
            public readonly HomePose Pose;
            public readonly int Group;
            /// <summary>A point walked through on the way from and back to the spot, or null to walk straight.</summary>
            public readonly Vector2? Via;

            public HomeSpot(Vector2 at, Vector2 face, HomePose pose, int group = -1, Vector2? via = null)
            {
                At = at;
                Face = face * (1f / Mathf.Sqrt(face.x * face.x + face.y * face.y));
                Pose = pose;
                Group = group;
                Via = via;
            }

            /// <summary>The middle of the stool a seated spot sits on: behind the feet, where the sitting clip lands the hips.</summary>
            public Vector2 Seat => At - Face * SeatBack;
        }

        /// <summary>The stool's middle is this far behind a sitter's feet, m: the sitting clip carries the hips back 0.48 m.</summary>
        public const float SeatBack = 0.38f;

        /// <summary>The middle of each talking group, in the garage's frame: its people face it.</summary>
        private static readonly Vector2[] Groups = { new Vector2(-3.35f, 4.0f), new Vector2(2.55f, -0.6f), new Vector2(0f, -4.4f) };

        /// <summary>The crew's corner: stools on an arc round this point, the sitters turned to it.</summary>
        private static readonly Vector2 Corner = new Vector2(-5.0f, -1.4f);

        private static HomeSpot Talker(int group, float x, float z) =>
            new HomeSpot(new Vector2(x, z), Groups[group] - new Vector2(x, z), HomePose.Talk, group);

        /// <summary>A stool in the crew's corner, this many degrees round from straight back, the sitter's feet 1.6 m from the corner's middle.</summary>
        private static HomeSpot Lounger(float degrees)
        {
            float a = degrees * Mathf.Deg2Rad;
            var d = new Vector2(-Mathf.Sin(a), -Mathf.Cos(a));
            return new HomeSpot(Corner + d * 1.6f, new Vector2(-d.x, -d.y), HomePose.Sit);
        }

        /// <summary>A stool at the engineers' desk, facing it, reached from the side so the walk misses the stool.</summary>
        private static HomeSpot Engineer(float x, float side) =>
            new HomeSpot(new Vector2(x, DeskZ + DeskHalfDepth + 0.3f), new Vector2(0f, -1f), HomePose.Desk, -1, new Vector2(x + side * 0.7f, DeskZ + DeskHalfDepth + 0.3f));

        /// <summary>The engineers' desk: its middle across and out, and half its depth, m.</summary>
        public const float DeskX = 4.0f, DeskZ = -5.0f, DeskHalfDepth = 0.47f;

        /// <summary>Every waiting place in a garage, in the order they are taken: <see cref="StandbySpots"/> of them.</summary>
        public static readonly HomeSpot[] HomeSpots =
        {
            Talker(0, -3.2f, 4.72f),
            Talker(0, -2.7f, 3.5f),
            Talker(0, -4.05f, 3.65f),
            new HomeSpot(new Vector2(-6.5f, 0.9f), new Vector2(-1f, 0.25f), HomePose.Stand),
            Talker(1, 2.35f, 0.15f),
            Talker(1, 1.9f, -1.05f),
            new HomeSpot(new Vector2(-6.0f, 2.3f), new Vector2(-1f, -0.35f), HomePose.Stand),
            Talker(1, 3.25f, -1.0f),
            Engineer(DeskX - 0.5f, -1f),
            Lounger(75f),
            Lounger(45f),
            Talker(2, -0.45f, -4.3f),
            Talker(2, 0.45f, -4.5f),
            Engineer(DeskX + 0.5f, 1f),
            Lounger(15f),
            Lounger(-15f),
        };

        /// <summary>
        /// Where the car stands before the race and the way it drives out of the door, in the garage's frame: across
        /// from and to, and out from, to the door. No crew wait and no fit-out stands in it.
        /// </summary>
        public const float CarBayFrom = -0.6f, CarBayTo = 6.8f, CarBayBack = 1.3f;

        /// <summary>Inner face of a garage's -x wall, its own.</summary>
        public float LeftWall => -Width * 0.5f + WallHalf;

        /// <summary>
        /// Inner face of a garage's +x wall this far out towards the door. That wall is the next garage's -x wall,
        /// and the row's garages overlap, by close to three metres at the door round the bend, so it can stand well
        /// inside half the width.
        /// </summary>
        public float RightWall(int garage, float z)
        {
            float x = Width * 0.5f - WallHalf;
            if (garage == 0) return x;
            GarageFrame f = frames[garage], n = frames[garage - 1];
            // Where this garage's crosswise line at z meets the wall's centre line, which runs along n.Out.
            Vector3 gap = Flat(f.At(0f, 0f, z) - n.At(-Width * 0.5f, 0f, 0f));
            float s = -Vector3.Dot(gap, n.Side) / Vector3.Dot(f.Side, n.Side);
            return Mathf.Min(x, s - WallHalf);
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
