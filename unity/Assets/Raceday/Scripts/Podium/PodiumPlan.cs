using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The podium's frame: a balcony on the race offices' front, over the pit lane, centred on the middle of the
    /// offices. x runs along the row (+x is the crowd's right, looking up at the podium), y is up from the garage floor
    /// there, z is out from the garages' middle line towards the pit lane, as <see cref="PitComplex.RowPoint"/> has it.
    /// </summary>
    public readonly struct PodiumFrame
    {
        public readonly Vector3 Origin, Along, Out;

        public PodiumFrame(Vector3 origin, Vector3 along, Vector3 @out)
        {
            Origin = origin;
            Along = along;
            Out = @out;
        }

        public Vector3 At(float x, float y, float z) => Origin + Along * x + Vector3.up * y + Out * z;
        public Vector3 At(Vector3 local) => At(local.x, local.y, local.z);
        public Vector3 Dir(float x, float y, float z) => Along * x + Vector3.up * y + Out * z;
        public Vector3 Dir(Vector3 local) => Dir(local.x, local.y, local.z);

        /// <summary>A world point in the podium's frame.</summary>
        public Vector3 Local(Vector3 world)
        {
            Vector3 d = world - Origin;
            return new Vector3(Vector3.Dot(d, Along), d.y, Vector3.Dot(d, Out));
        }
    }

    /// <summary>
    /// Where everything on the podium stands, with no Unity objects so it can be checked outside Unity: the balcony and
    /// its three steps, the winner in the middle, second on the crowd's left and third on its right, each car's drivers
    /// side by side on its step, the presenters in front with the trophies, the ice buckets with the champagne, and the
    /// podium teams' crews in the pit lane below. All in the <see cref="PodiumFrame"/>.
    /// </summary>
    public sealed class PodiumPlan
    {
        /// <summary>Top of the balcony floor over the garage floor, on the fascia beam, m.</summary>
        public const float Floor = 7.9f;
        /// <summary>The offices' glass front, where the balcony meets the building, and the balcony's front edge, z.</summary>
        public const float Back = 8.0f, Front = 12.2f;
        /// <summary>Half the balcony's width, m.</summary>
        public const float HalfWidth = 7.4f;
        /// <summary>Width of each step, m.</summary>
        public const float StepWidth = 3.0f;
        /// <summary>The steps' back and front edges, z, and the half-height stair behind each.</summary>
        public const float StepBack = 9.45f, StepFront = 10.75f, StairBack = 9.0f;
        /// <summary>Where the drivers stand on the steps, z.</summary>
        public const float StandZ = 10.25f;
        /// <summary>Gap between drivers standing side by side, m.</summary>
        public const float Spacing = 0.95f;
        /// <summary>Height of the balustrade over the balcony floor, m.</summary>
        public const float RailHeight = 1.05f;
        /// <summary>The canopy over the podium: its underside over the balcony floor, and how far out it reaches, z.</summary>
        public const float CanopyHeight = 4.7f, CanopyFront = 11.6f;
        /// <summary>The doorway in the glass behind the winner's step the drivers walk out of: half its width and its height.</summary>
        public const float DoorHalf = 0.8f, DoorHeight = 2.4f;
        /// <summary>The presenters wait in front of the steps here, z, and go to the balcony's ends after.</summary>
        public const float PresenterZ = 10.92f, PresenterEnd = 6.4f;
        /// <summary>The ice bucket's top over the step, on its stand, m.</summary>
        public const float BucketTop = 1.15f;

        /// <summary>Height of each place's step, winner first.</summary>
        public static readonly float[] StepHeights = { 0.6f, 0.4f, 0.25f };
        /// <summary>Across the row, the middle of each place's step: the winner in the middle, second on the crowd's left.</summary>
        public static readonly float[] StepX = { 0f, -StepWidth, StepWidth };

        public readonly PodiumFrame Frame;

        public PodiumPlan(PodiumFrame frame)
        {
            Frame = frame;
        }

        /// <summary>
        /// The podium on a pit complex: on the race offices, centred on their middle, and level. The garage floors fall
        /// along the row, so it is lifted to clear the fascia beam at its higher end; at the lower end its back
        /// upstand reaches down over the beam.
        /// </summary>
        public static PodiumPlan On(PitComplex pc)
        {
            float s = Station(pc);
            Vector3 origin = pc.RowPoint(s, 0f, 0f);
            float reach = HalfWidth / pc.Width;
            float lift = Mathf.Max(0f, Mathf.Max(pc.RowPoint(s - reach, 0f, Back).y, pc.RowPoint(s + reach, 0f, Back).y) - origin.y);
            origin.y += lift;
            return new PodiumPlan(new PodiumFrame(origin, pc.RowAlong(s), pc.RowOut(s)));
        }

        /// <summary>How far the back upstand reaches down under the balcony floor, over the beam at the podium's lower end, m.</summary>
        public const float Upstand = 0.6f;

        /// <summary>The station the podium is centred on.</summary>
        public static float Station(PitComplex pc) => (pc.OfficeFrom + pc.OfficeTo) * 0.5f;

        /// <summary>
        /// Across the step, where each of a car's drivers stands, the lead driver first: in the middle with three, on
        /// the crowd's left with two. The lead driver takes the trophy and the champagne.
        /// </summary>
        public static float DriverX(int drivers, int k)
        {
            if (drivers <= 1) return 0f;
            if (drivers == 2) return k == 0 ? -Spacing * 0.5f : Spacing * 0.5f;
            return k == 0 ? 0f : k == 1 ? -Spacing : Spacing;
        }

        /// <summary>Where a driver stands on their step, on its top.</summary>
        public static Vector3 Stand(int place, int drivers, int k) =>
            new Vector3(StepX[place] + DriverX(drivers, k), Floor + StepHeights[place], StandZ);

        /// <summary>
        /// The ice bucket at the lead driver's right hand, between them and a teammate with three, out to the step's end
        /// with two. Facing the crowd, a driver's right is -x.
        /// </summary>
        public static Vector3 Bucket(int place, int drivers)
        {
            float lead = StepX[place] + DriverX(drivers, 0);
            return new Vector3(lead - Spacing * 0.5f, Floor + StepHeights[place], StandZ - 0.12f);
        }

        /// <summary>Where a place's presenter waits with the trophy, on the balcony floor in front of the lead driver.</summary>
        public static Vector3 PresenterStart(int place, int drivers) => new Vector3(StepX[place] + DriverX(drivers, 0), Floor, PresenterZ);

        /// <summary>Where a place's presenter goes once the trophy is handed over: second's and the winner's to the left end, third's to the right.</summary>
        public static Vector3 PresenterRest(int place) =>
            new Vector3(place == 2 ? PresenterEnd : place == 1 ? -PresenterEnd : -PresenterEnd + 1.0f, Floor, PresenterZ + 0.3f);

        /// <summary>Where the drivers come out: the doorway behind the winner's step.</summary>
        public static Vector3 Door => new Vector3(0f, Floor, Back + 0.05f);

        /// <summary>The walkway behind the steps, z, that the drivers walk along to their places.</summary>
        public const float WalkwayZ = 8.62f;

        // ------------------------------------------------------------------ the crowd below

        /// <summary>
        /// Where the podium teams' crews stand in the pit lane below, in the fast lane between the cars parked in their
        /// boxes and the pit wall: two loose rows, second's crew on the left, the winner's in the middle, third's on the
        /// right. Positions are in the frame, on the lane's surface.
        /// </summary>
        public static List<(Vector3 at, int place)> Crowd(PitLane lane, PodiumFrame frame, PitComplex pc)
        {
            var spots = new List<(Vector3, int)>();
            var rng = new System.Random(77);
            int g = Mathf.Clamp(Mathf.RoundToInt(Station(pc)), 0, lane.Garages - 1);
            for (int row = 0; row < 2; row++)
                for (int c = 0; c < 12; c++)
                {
                    // A loose huddle rather than two neat rows: each stands well off a grid spot.
                    float x = -6.6f + c * 1.2f + (row == 1 ? 0.6f : 0f) + (float)(rng.NextDouble() - 0.5) * 0.6f;
                    // Across the fast lane: lane offsets from the wall side, the first row nearer the boxes.
                    float lateral = (row == 0 ? -2.95f : -4.15f) + (float)(rng.NextDouble() - 0.5) * 0.8f;
                    Vector3 world = OnLane(lane, frame, g, x, lateral);
                    int place = x < -2.2f ? 1 : x > 2.2f ? 2 : 0;
                    spots.Add((frame.Local(world), place));
                }
            return spots;
        }

        /// <summary>A point on the pit lane at x along the podium and a lane offset, found near garage g's box.</summary>
        public static Vector3 OnLane(PitLane lane, PodiumFrame frame, int g, float x, float lateral)
        {
            float best = float.MaxValue, at = lane.BoxLane[g];
            Vector3 target = frame.At(x, 0f, 0f);
            for (float i = lane.BoxLane[g] - 20f; i <= lane.BoxLane[g] + 20f; i += 0.05f)
            {
                Vector3 p = lane.LanePoint(i, lateral);
                Vector3 d = p - target;
                float along = Vector3.Dot(d, frame.Along);
                if (Mathf.Abs(along) < best) { best = Mathf.Abs(along); at = i; }
            }
            return lane.LanePoint(at, lateral);
        }
    }
}
