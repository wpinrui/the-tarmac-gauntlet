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

    /// <summary>
    /// The pit complex laid out from the circuit, with no Unity objects so it can be tested outside Unity: each
    /// garage's frame on the floor heights the circuit builder laid, the stations along the row that the pit
    /// building is built on, where the race offices stand, how far the paddock reaches, and where a pit crew
    /// waits. Inside every garage the front <see cref="ClearDepth"/> metres are kept clear of fit-out,
    /// so the crew can stand there and run out to the box.
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
        /// <summary>Middle of the crew's waiting rows across the garage, m: clear of the car parked on the +x side.</summary>
        public const float CrewMiddle = -1.6f;

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
        /// Where the crew wait inside the garage, in rows of six across the clear front, facing the door, off to the
        /// garage's -x side: the +x side is where the car waits before the race (<see cref="GarageStart.Parked"/>).
        /// Spot 0 is nearest the door in the middle of the rows; later spots work outwards and back.
        /// </summary>
        public CrewSpot Standby(int garage, int spot)
        {
            GarageFrame f = frames[garage];
            int row = spot / 6, col = spot % 6;
            float x = CrewMiddle + (col % 2 == 0 ? 1f : -1f) * (0.6f + 1.2f * (col / 2));
            float z = Depth * 0.5f - 1.4f - row * 1.6f;
            return new CrewSpot(f.At(x, 0f, z), f.Out);
        }

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
