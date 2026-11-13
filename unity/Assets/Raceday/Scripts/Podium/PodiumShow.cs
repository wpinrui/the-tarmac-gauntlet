using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>How one person on or below the podium stands at a moment, in the <see cref="PodiumFrame"/>.</summary>
    public struct PodiumPose
    {
        /// <summary>Where their feet are, and the flat way they face.</summary>
        public Vector3 At, Face;
        /// <summary>The clip they play and how far in, with the clip before it fading out under it.</summary>
        public string Clip, Under;
        public float ClipTime, UnderTime, UnderWeight;
        public bool Loop, UnderLoop;
        /// <summary>Where each wrist reaches, and how much the reach overrides the clip, 0 to 1.</summary>
        public Vector3 LeftHand, RightHand;
        public float LeftWeight, RightWeight;
        /// <summary>
        /// The bar each hand closes round, where it reaches for something: the way it runs, out past the thumb (a
        /// bottle's axis, base to neck; a trophy's handle, upward). Zero where the hand holds nothing.
        /// </summary>
        public Vector3 LeftGrip, RightGrip;
        /// <summary>The radius of the bar each hand closes round, m.</summary>
        public float LeftBar, RightBar;
    }

    /// <summary>Who someone on the podium is.</summary>
    public enum PodiumRole { Driver, Presenter, Crew }

    /// <summary>One person in the ceremony.</summary>
    public sealed class PodiumActor
    {
        public PodiumRole Role;
        /// <summary>The place they belong to: 0 the winner, 1 second, 2 third.</summary>
        public int Place;
        /// <summary>Their slot: a driver's place in the line-up (0 is the lead), or a crew member's number.</summary>
        public int Slot;
        /// <summary>Where a crew member stands, in the frame.</summary>
        public Vector3 Spot;
        /// <summary>A crew member's own phase, so the crowd doesn't cheer in step.</summary>
        public float Phase;
        /// <summary>
        /// Their own manner, so no two people move in step: how fast their clips play (1 is the clip's own pace), how
        /// far into a looping clip they start, s, how late they react, s, how far they turn from straight ahead,
        /// degrees, how far off their mark they stand, m, how fast they walk, as a share of the pace, and their size.
        /// </summary>
        public float Rate = 1f, Offset, Lag, Turn, Pace = 1f, Size = 1f;
        public Vector3 Nudge;
    }

    /// <summary>A camera shot: where the camera is, what it looks at, and its vertical field of view.</summary>
    public struct PodiumShot
    {
        public Vector3 Eye, Look;
        public float Fov;
    }

    /// <summary>
    /// The podium ceremony as a timeline, with no Unity objects so it can be checked and previewed outside Unity. The
    /// drivers walk out of the doorway, third's first, and up onto their steps, and cheer as they arrive. Each place's
    /// presenter hands its lead driver the trophy, third first, and the driver lifts it over their head and keeps it
    /// at their side. The lead drivers take the champagne from the ice buckets beside them, shake it and spray it over
    /// each other and the crews below. Confetti falls from the canopy, the trophies go up again, and the ceremony ends
    /// on a wide shot. Everything is a function of the time since it began.
    /// </summary>
    public sealed class PodiumShow
    {
        /// <summary>How long the ceremony runs, s.</summary>
        public const float Length = 43f;
        public const float WalkSpeed = 1.3f;
        /// <summary>The walking clip's own pace, m/s (<see cref="GarageCrew.WalkSpeed"/>).</summary>
        public const float ClipWalkSpeed = 1.56f;
        public const float Fade = 0.3f;
        public const float CheerLength = 2.1667f;
        /// <summary>When each place's drivers start walking out, winner first: third's go first.</summary>
        public static readonly float[] WalkOut = { 3.6f, 2.0f, 0.5f };
        public const float DriverGap = 0.55f;
        /// <summary>When each place's trophy is handed over, winner first: third's first.</summary>
        public static readonly float[] Present = { 16.0f, 12.5f, 9.0f };
        public const float Grip = 0.9f, Handed = 1.3f, Lifted = 2.3f, Lowering = 3.8f, AtSide = 4.6f;
        public const float BottleAt = 23.0f, BottleGrabbed = 23.8f, PopAt = 25.6f, SprayEnd = 35.5f;
        public const float ConfettiAt = 36.0f, RaiseAgain = 36.3f, Raised = 37.3f;

        public readonly int[] Drivers;
        public readonly List<PodiumActor> Actors = new List<PodiumActor>();

        /// <param name="drivers">Drivers in each place's car, winner first; 0 where the place is empty.</param>
        /// <param name="crowd">Where the crews stand below, from <see cref="PodiumPlan.Crowd"/>.</param>
        public PodiumShow(int[] drivers, IList<(Vector3 at, int place)> crowd)
        {
            Drivers = drivers;
            for (int p = 0; p < 3; p++)
            {
                if (drivers[p] <= 0) continue;
                for (int k = 0; k < drivers[p]; k++) Actors.Add(Manner(new PodiumActor { Role = PodiumRole.Driver, Place = p, Slot = k }));
                Actors.Add(Manner(new PodiumActor { Role = PodiumRole.Presenter, Place = p }));
            }
            if (crowd == null) return;
            for (int c = 0; c < crowd.Count; c++)
                if (drivers[crowd[c].place] > 0)
                    Actors.Add(Manner(new PodiumActor { Role = PodiumRole.Crew, Place = crowd[c].place, Slot = c, Spot = crowd[c].at, Phase = (c * 0.618f) % 1f * 2.1f }));
        }

        /// <summary>A number from 0 to 1 that is the same every time for one person and one use.</summary>
        private static float Hash(PodiumActor a, int use)
        {
            uint h = (uint)((int)a.Role * 7919 + a.Place * 104729 + a.Slot * 15485863 + use * 32452843);
            h ^= h >> 16; h *= 0x7feb352d; h ^= h >> 15; h *= 0x846ca68b; h ^= h >> 16;
            return (h & 0xffffff) / 16777216f;
        }

        private static float Between(PodiumActor a, int use, float lo, float hi) => lo + (hi - lo) * Hash(a, use);

        /// <summary>
        /// Gives a person their own manner. The lead drivers and the presenters keep their marks and facing, as the
        /// trophy and the champagne pass between their hands; everyone else stands a little off their mark and turned.
        /// </summary>
        private static PodiumActor Manner(PodiumActor a)
        {
            a.Rate = Between(a, 1, 0.86f, 1.16f);
            a.Offset = Between(a, 2, 0f, 4f);
            a.Pace = Between(a, 3, 0.94f, 1.06f);
            bool lead = a.Role == PodiumRole.Driver && a.Slot == 0;
            switch (a.Role)
            {
                case PodiumRole.Driver:
                    a.Lag = Between(a, 4, 0f, 0.45f);
                    if (!lead)
                    {
                        a.Turn = Between(a, 5, -14f, 14f);
                        a.Nudge = new Vector3(Between(a, 6, -0.06f, 0.06f), 0f, Between(a, 7, -0.12f, 0.08f));
                    }
                    break;
                case PodiumRole.Presenter:
                    a.Lag = Between(a, 4, 0f, 0.6f);
                    a.Turn = Between(a, 5, -10f, 10f);
                    break;
                default:
                    a.Lag = Between(a, 4, 0f, 1.1f);
                    a.Turn = Between(a, 5, -24f, 24f);
                    a.Size = Between(a, 8, 0.93f, 1.06f);
                    break;
            }
            return a;
        }

        // ------------------------------------------------------------------ helpers

        private static readonly Vector3 Up = Vector3.up;
        private static readonly Vector3 Toward = new Vector3(0f, 0f, 1f);
        private static readonly Vector3 Away = new Vector3(0f, 0f, -1f);

        /// <summary>A person's right in the frame, facing face. The frame is mirrored from Unity's axes, so it is the opposite of Unity's.</summary>
        public static Vector3 RightOf(Vector3 face) => new Vector3(-face.z, 0f, face.x);

        private static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }
        private static float Span(float t, float a, float b) => Smooth((t - a) / (b - a));

        /// <summary>A flat direction turned by degrees, clockwise seen from above in the world.</summary>
        private static Vector3 Turn(Vector3 face, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad;
            Vector3 right = RightOf(face);
            return (face * Mathf.Cos(r) + right * Mathf.Sin(r)).normalized;
        }

        /// <summary>Sets the clip from a list of (start, clip, loop) segments, the one before fading out under it.</summary>
        private static void Clips(ref PodiumPose pose, float t, IList<(float start, string clip, bool loop, float rate)> segs, float offset = 0f)
        {
            int i = 0;
            while (i + 1 < segs.Count && t >= segs[i + 1].start) i++;
            var s = segs[i];
            pose.Clip = s.clip;
            pose.Loop = s.loop;
            pose.ClipTime = Mathf.Max(0f, t - s.start) * s.rate + (s.loop ? offset : 0f);
            pose.UnderWeight = 0f;
            if (i > 0 && t - s.start < Fade)
            {
                var b = segs[i - 1];
                pose.Under = b.clip;
                pose.UnderLoop = b.loop;
                pose.UnderTime = (t - b.start) * b.rate + (b.loop ? offset : 0f);
                pose.UnderWeight = 1f - (t - s.start) / Fade;
            }
        }

        // ------------------------------------------------------------------ the drivers

        /// <summary>The way a driver walks out: from the doorway to the walkway behind their spot, then forward up onto the step.</summary>
        public static Vector3[] DriverPath(int place, int drivers, int k, Vector3 nudge = default)
        {
            Vector3 stand = PodiumPlan.Stand(place, drivers, k) + nudge;
            Vector3 door = PodiumPlan.Door;
            return new[] { door, new Vector3(stand.x, PodiumPlan.Floor, PodiumPlan.WalkwayZ), new Vector3(stand.x, PodiumPlan.Floor, stand.z) };
        }

        private static float PathLength(Vector3[] path)
        {
            float l = 0f;
            for (int i = 1; i < path.Length; i++) l += Flat(path[i] - path[i - 1]).magnitude;
            return l;
        }

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        /// <summary>The point d metres along a path, flat.</summary>
        private static Vector3 Along(Vector3[] path, float d)
        {
            for (int i = 1; i < path.Length; i++)
            {
                float seg = Flat(path[i] - path[i - 1]).magnitude;
                if (d <= seg || i == path.Length - 1) return Vector3.Lerp(path[i - 1], path[i], seg > 0f ? d / seg : 1f);
                d -= seg;
            }
            return path[path.Length - 1];
        }

        /// <summary>The height of the steps' tops under a point, smoothed over the stair so a stride carries a driver up it.</summary>
        public static float Ground(int place, Vector3 at)
        {
            float h = PodiumPlan.StepHeights[place];
            return PodiumPlan.Floor + h * Smooth((at.z - (PodiumPlan.StairBack - 0.15f)) / (PodiumPlan.StepBack + 0.2f - (PodiumPlan.StairBack - 0.15f)));
        }

        public float StartOf(PodiumActor a) => WalkOut[a.Place] + a.Slot * DriverGap + a.Lag * 0.4f;
        public float ArrivalOf(PodiumActor a) => StartOf(a) + PathLength(DriverPath(a.Place, Drivers[a.Place], a.Slot, a.Nudge)) / (WalkSpeed * a.Pace);

        /// <summary>When the last of a place's drivers is on the step.</summary>
        public float Arrived(int place)
        {
            float last = 0f;
            foreach (PodiumActor a in Actors)
                if (a.Role == PodiumRole.Driver && a.Place == place) last = Mathf.Max(last, ArrivalOf(a));
            return last;
        }

        public PodiumPose Pose(PodiumActor a, float t)
        {
            switch (a.Role)
            {
                case PodiumRole.Driver: return DriverPose(a, t);
                case PodiumRole.Presenter: return PresenterPose(a, t);
                default: return CrewPose(a, t);
            }
        }

        private PodiumPose DriverPose(PodiumActor a, float t)
        {
            int p = a.Place, n = Drivers[p];
            Vector3[] path = DriverPath(p, n, a.Slot, a.Nudge);
            float speed = WalkSpeed * a.Pace;
            float start = StartOf(a), length = PathLength(path), arrive = start + length / speed;
            var pose = new PodiumPose();
            float d = Mathf.Clamp((t - start) * speed, 0f, length);
            Vector3 at = Along(path, d);
            at.y = Ground(p, at);
            pose.At = at;
            // Facing the way they walk, looking a little ahead so they turn into the corner; the crowd once there.
            Vector3 ahead = Flat(Along(path, Mathf.Min(d + 0.45f, length)) - Along(path, d));
            // Once there, turned a little their own way, settling into it over a moment.
            Vector3 settled = Turn(Toward, a.Turn * Span(t, arrive, arrive + 0.8f));
            pose.Face = t < arrive - 0.05f && ahead.sqrMagnitude > 1e-4f ? ahead.normalized : settled;
            if (t < start) pose.Face = Toward;

            float presented = Present[p];
            bool lead = a.Slot == 0;
            float group = Arrived(p);
            float r = a.Rate, cheer = CheerLength / r, lag = a.Lag;
            var segs = new List<(float, string, bool, float)>
            {
                (-1f, "Idle", true, r),
                (start, "Walk", true, speed / ClipWalkSpeed),
                (arrive, "Idle", true, r),
                (group + 0.2f + lag, "Cheer", false, r),
                (group + 0.2f + lag + cheer, "Idle", true, r),
            };
            if (!lead)
            {
                // Teammates cheer the trophy, the champagne and the end, each in their own time.
                segs.Add((presented + Handed + lag, "Cheer", false, r));
                segs.Add((presented + Handed + lag + cheer, "Idle", true, r));
                for (float c = PopAt + 0.4f + a.Slot * 0.5f + lag * 2f; c < SprayEnd; c += cheer + 0.8f + a.Offset * 0.3f)
                {
                    segs.Add((c, "Cheer", false, r));
                    segs.Add((c + cheer, "Idle", true, r));
                }
                segs.Add((ConfettiAt + a.Slot * 0.3f + lag, "Cheer", true, r));
            }
            segs.Sort((x, y) => x.Item1.CompareTo(y.Item1));
            Clips(ref pose, t, segs, a.Offset);
            if (!lead) return pose;

            // The lead driver's hands: the trophy, then the champagne.
            Vector3 face = pose.Face, right = RightOf(face);
            (Vector3 trophy, _) = Trophy(p, t);
            if (t >= presented)
            {
                // Both hands on the handles as the trophy comes over; the right lets go as it comes down to the side.
                float w = Span(t, presented + 0.5f, presented + Grip + 0.05f);
                pose.LeftHand = trophy + Up * TrophyHandle(p) - right * TrophySide(p);
                pose.RightHand = trophy + Up * TrophyHandle(p) + right * TrophySide(p);
                pose.LeftWeight = w;
                pose.RightWeight = w * (1f - Span(t, presented + Lowering, presented + Lowering + 0.4f));
                pose.LeftGrip = pose.RightGrip = Up;
                pose.LeftBar = pose.RightBar = HandleBar * TrophyScale(p);
            }
            if (t >= BottleAt)
            {
                (Vector3 hand, Vector3 axis) = BottleHand(p, n, pose.At, face, t);
                pose.RightHand = hand;
                pose.RightGrip = axis;
                pose.RightBar = BottleBar;
                pose.RightWeight = Mathf.Max(pose.RightWeight, Span(t, BottleAt, BottleAt + 0.5f));
            }
            return pose;
        }

        /// <summary>The trophies' handles: how high over the base, and how far out either side, m.</summary>
        public static float TrophyHandle(int place) => 0.38f * TrophyScale(place);
        public static float TrophySide(int place) => 0.165f * TrophyScale(place);
        /// <summary>The radius of a full-size trophy's handle, and of the bottle where it is held, m.</summary>
        public const float HandleBar = 0.009f, BottleBar = 0.045f;
        public static float TrophyScale(int place) => place == 0 ? 1f : place == 1 ? 0.88f : 0.8f;

        /// <summary>Where the lead driver holds the trophy at their side by its left handle, lifting it again at the end.</summary>
        private static Vector3 LeftHold(Vector3 root, Vector3 face, float t)
        {
            Vector3 left = -RightOf(face);
            Vector3 side = root + face * 0.24f + left * 0.24f + Up * 1.18f;
            Vector3 high = root + face * 0.1f + left * 0.2f + Up * 1.84f;
            float up = t < RaiseAgain ? 0f : Span(t, RaiseAgain, Raised);
            return Vector3.Lerp(side, high, up);
        }

        /// <summary>
        /// Where the lead driver's right hand is with the champagne, and the bottle's axis, base to neck: in the bucket,
        /// shaken in front of the chest, sprayed side to side, then held up once the spray is done.
        /// </summary>
        public (Vector3 hand, Vector3 axis) BottleHand(int place, int drivers, Vector3 root, Vector3 face, float t)
        {
            Vector3 bucket = PodiumPlan.Bucket(place, drivers);
            Vector3 inBucket = bucket + Up * (PodiumPlan.BucketTop - 0.22f + BottleGripHeight);
            Vector3 right = RightOf(face);
            if (t < BottleGrabbed) return (inBucket, Up);
            Vector3 chest = root + face * 0.32f + right * 0.06f + Up * 1.2f;
            if (t < PopAt)
            {
                float k = Span(t, BottleGrabbed, BottleGrabbed + 0.5f);
                Vector3 shake = Up * (0.07f * Mathf.Sin((t - BottleGrabbed) * 2f * Mathf.PI * 4.5f) * k);
                Vector3 axis = (Up * 0.9f + face * 0.3f).normalized;
                return (Vector3.Lerp(inBucket, chest, k) + shake, Vector3.Lerp(Up, axis, k).normalized);
            }
            if (t < SprayEnd + 0.6f)
            {
                Vector3 aim = SprayAim(place, face, t);
                Vector3 flat = Flat(aim).normalized;
                Vector3 hand = root + Up * 1.42f + flat * 0.39f + right * 0.04f;
                float k = Span(t, PopAt, PopAt + 0.3f) * (1f - Span(t, SprayEnd, SprayEnd + 0.6f));
                Vector3 axis = (Up * 0.9f + face * 0.3f).normalized;
                return (Vector3.Lerp(chest, hand, k), Vector3.Lerp(axis, aim, k).normalized);
            }
            Vector3 low = root + face * 0.26f + right * 0.2f + Up * 1.1f;
            return (Vector3.Lerp(chest, low, Span(t, SprayEnd + 0.6f, SprayEnd + 1.4f)), (Up * 0.95f + face * 0.2f).normalized);
        }

        /// <summary>Where the bottle is gripped, over its base, m.</summary>
        public const float BottleGripHeight = 0.12f;
        /// <summary>The bottle's height, base to mouth, m.</summary>
        public const float BottleLength = 0.31f;

        /// <summary>The way the champagne is sprayed: forward and up, swept from side to side over the steps and the crowd.</summary>
        public static Vector3 SprayAim(int place, Vector3 face, float t)
        {
            float phase = place * 1.7f;
            // The winner sweeps across both sides; second and third aim more at the winner and the crowd.
            float centre = place == 1 ? 20f : place == 2 ? -20f : 0f;
            float sweep = centre + 55f * Mathf.Sin((t - PopAt) * 2f * Mathf.PI * 0.32f + phase);
            Vector3 flat = Turn(face, sweep);
            float lift = 18f + 10f * Mathf.Sin((t - PopAt) * 2f * Mathf.PI * 0.21f + phase);
            float r = lift * Mathf.Deg2Rad;
            return (flat * Mathf.Cos(r) + Up * Mathf.Sin(r)).normalized;
        }

        /// <summary>True while place's champagne sprays, and where from and which way.</summary>
        public bool Spray(int place, float t, out Vector3 mouth, out Vector3 aim)
        {
            mouth = aim = Vector3.zero;
            if (Drivers[place] <= 0 || t < PopAt || t > SprayEnd) return false;
            Vector3 root = PodiumPlan.Stand(place, Drivers[place], 0);
            (Vector3 hand, Vector3 axis) = BottleHand(place, Drivers[place], root, Toward, t);
            (Vector3 bottleBase, _) = Bottle(place, t);
            mouth = bottleBase + axis * BottleLength;
            aim = axis;
            return true;
        }

        /// <summary>The champagne bottle: its base and its axis, base to neck. In the bucket until the lead driver takes it.</summary>
        public (Vector3 bottom, Vector3 axis) Bottle(int place, float t)
        {
            int n = Drivers[place];
            Vector3 root = PodiumPlan.Stand(place, n, 0);
            (Vector3 hand, Vector3 axis) = BottleHand(place, n, root, Toward, t);
            return (hand - axis * BottleGripHeight, axis);
        }

        // ------------------------------------------------------------------ the presenters and the trophies

        /// <summary>Where the trophy is handed over, between the presenter's hands and the driver's, over the step's front edge.</summary>
        public static Vector3 Handover(int place)
        {
            return new Vector3(0f, PodiumPlan.Floor + 1.24f + PodiumPlan.StepHeights[place] * 0.75f - TrophyHandle(place), 10.47f);
        }

        /// <summary>The trophy's base and the way its handles point: with the presenter, handed over, lifted, then at the lead driver's side.</summary>
        public (Vector3 bottom, Vector3 side) Trophy(int place, float t)
        {
            int n = Drivers[place];
            float t0 = Present[place];
            Vector3 lead = PodiumPlan.Stand(place, n, 0);
            Vector3 presenter = PodiumPlan.PresenterStart(place, n);
            Vector3 side = RightOf(Toward);
            // The presenter holds it in front of their chest, facing the driver.
            Vector3 held = presenter + Away * 0.3f + Up * (1.12f - TrophyHandle(place));
            Vector3 handover = Handover(place);
            handover.x = lead.x;
            if (t < t0) return (held, side);
            if (t < t0 + Grip) return (Vector3.Lerp(held, handover, Span(t, t0, t0 + Grip)), side);
            if (t < t0 + Handed) return (handover, side);
            Vector3 chest = lead + Toward * 0.3f + Up * (1.15f - TrophyHandle(place));
            Vector3 overhead = lead + Toward * 0.12f + Up * (1.96f - TrophyHandle(place));
            if (t < t0 + Lifted)
            {
                float k = Span(t, t0 + Handed, t0 + Lifted);
                return (k < 0.5f ? Vector3.Lerp(handover, chest, k * 2f) : Vector3.Lerp(chest, overhead, k * 2f - 1f), side);
            }
            if (t < t0 + Lowering) return (overhead + Up * (0.03f * Mathf.Sin((t - t0) * 6f)), side);
            // At the driver's side, held by its left handle: the trophy's middle a handle's reach in from the hand.
            Vector3 hand = LeftHold(lead, Toward, t);
            Vector3 atSide = hand - Up * TrophyHandle(place) + RightOf(Toward) * TrophySide(place);
            if (t < t0 + AtSide) return (Vector3.Lerp(overhead, atSide, Span(t, t0 + Lowering, t0 + AtSide)), side);
            return (atSide, side);
        }

        private PodiumPose PresenterPose(PodiumActor a, float t)
        {
            int p = a.Place;
            float t0 = Present[p];
            Vector3 from = PodiumPlan.PresenterStart(p, Drivers[p]), to = PodiumPlan.PresenterRest(p);
            float leave = t0 + Handed + 0.3f;
            float speed = WalkSpeed * a.Pace;
            float length = Flat(to - from).magnitude;
            float walk = length / speed;
            var pose = new PodiumPose();
            float d = Mathf.Clamp((t - leave) * speed, 0f, length);
            pose.At = Vector3.Lerp(from, to, length > 0f ? d / length : 1f);
            Vector3 way = Flat(to - from).normalized;
            Vector3 inward = Turn(new Vector3(-Mathf.Sign(to.x), 0f, -0.6f).normalized, a.Turn);
            if (t < leave) pose.Face = Away;
            else if (t < leave + walk - 0.3f) pose.Face = Vector3.Lerp(Away, way, Span(t, leave, leave + 0.4f)).normalized;
            else pose.Face = Vector3.Lerp(way, inward, Span(t, leave + walk - 0.3f, leave + walk + 0.4f)).normalized;
            float r = a.Rate;
            Clips(ref pose, t, new List<(float, string, bool, float)>
            {
                (-1f, "Idle", true, r),
                (leave, "Walk", true, speed / ClipWalkSpeed),
                (leave + walk, "Idle", true, r),
                (ConfettiAt + 1f + a.Lag, "Cheer", false, r),
                (ConfettiAt + 1f + a.Lag + CheerLength / r, "Idle", true, r),
            }, a.Offset);
            if (t < t0 + Handed + 0.2f)
            {
                (Vector3 trophy, Vector3 side) = Trophy(p, t);
                pose.LeftHand = trophy + Up * TrophyHandle(p) + side * TrophySide(p) * 1.05f;
                pose.RightHand = trophy + Up * TrophyHandle(p) - side * TrophySide(p) * 1.05f;
                pose.LeftWeight = pose.RightWeight = 1f - Span(t, t0 + Handed - 0.1f, t0 + Handed + 0.2f);
                pose.LeftGrip = pose.RightGrip = Up;
                pose.LeftBar = pose.RightBar = HandleBar * TrophyScale(p);
            }
            return pose;
        }

        // ------------------------------------------------------------------ the crowd

        private PodiumPose CrewPose(PodiumActor a, float t)
        {
            var pose = new PodiumPose { At = a.Spot };
            // Looking up at their drivers, each a little their own way, and turning to follow the trophy as it goes up.
            float follow = Span(t, Present[a.Place], Present[a.Place] + 2f) * (1f - Span(t, Present[a.Place] + 6f, Present[a.Place] + 9f));
            Vector3 look = Flat(new Vector3(PodiumPlan.StepX[a.Place] * (0.6f + 0.4f * follow), 0f, PodiumPlan.StandZ) - a.Spot);
            pose.Face = Turn(look.sqrMagnitude > 1e-4f ? look.normalized : Away, a.Turn * (1f - 0.5f * follow));
            // Cheering as their drivers come out and get the trophy, through the champagne and at the end; between,
            // waiting. Each reacts in their own time, cheers for their own length, and some sit out the trophy.
            float r = a.Rate, lag = a.Lag;
            float cheerFrom = Arrived(a.Place) - 0.6f + lag;
            var segs = new List<(float, string, bool, float)> { (-1f, "Idle", true, r) };
            void Burst(float from, float until)
            {
                segs.Add((from, "Cheer", true, r));
                segs.Add((until, "Idle", true, r));
            }
            Burst(cheerFrom, cheerFrom + 1.8f + 2.2f * Hash(a, 10));
            if (Hash(a, 11) > 0.25f)
                Burst(Present[a.Place] + Handed + lag * 0.6f, Present[a.Place] + Handed + 2.2f + 2.4f * Hash(a, 12));
            // Through the champagne, a few stop for breath and go again.
            float pop = PopAt - 0.2f + lag * 0.5f;
            if (Hash(a, 13) > 0.6f)
            {
                float rest = PopAt + 3f + 4f * Hash(a, 14);
                Burst(pop, rest);
                Burst(rest + 1.2f + 1.5f * Hash(a, 15), Length + 1f);
            }
            else Burst(pop, Length + 1f);
            segs.Sort((x, y) => x.Item1.CompareTo(y.Item1));
            Clips(ref pose, t, segs, a.Offset + a.Phase);
            return pose;
        }

        // ------------------------------------------------------------------ the camera

        /// <summary>The shot at time t: cut from one to the next, some moving through their time.</summary>
        public PodiumShot Shot(float t, float lane)
        {
            float F = PodiumPlan.Floor;
            // The drivers walk out, from the pit wall below.
            if (t < 6.5f) return Move(t, 0f, 6.5f,
                new PodiumShot { Eye = new Vector3(-6.5f, lane + 4.6f, 21.4f), Look = new Vector3(0.5f, F + 1.4f, 9.6f), Fov = 40f },
                new PodiumShot { Eye = new Vector3(-4.0f, lane + 4.9f, 21.4f), Look = new Vector3(0.5f, F + 1.5f, 9.6f), Fov = 36f });
            // The winners up on their step.
            if (t < Present[2] - 0.4f) return Move(t, 6.5f, Present[2] - 0.4f,
                new PodiumShot { Eye = new Vector3(3.5f, F + 0.9f, 18.4f), Look = new Vector3(0f, F + 1.4f, 10.2f), Fov = 24f },
                new PodiumShot { Eye = new Vector3(2.5f, F + 1.1f, 18.4f), Look = new Vector3(0f, F + 1.5f, 10.2f), Fov = 22f });
            // Each trophy as it is handed over and lifted.
            for (int p = 2; p >= 0; p--)
            {
                float end = p == 0 ? 20.8f : Present[p - 1] - 0.4f;
                if (t < end)
                {
                    float x = PodiumPlan.StepX[p];
                    float h = PodiumPlan.StepHeights[p];
                    return Move(t, Present[p] - 0.4f, end,
                        new PodiumShot { Eye = new Vector3(x + 1.6f, F + h + 1.9f, 16.6f), Look = new Vector3(x, F + h + 1.35f, 10.4f), Fov = 19f },
                        new PodiumShot { Eye = new Vector3(x + 1.1f, F + h + 2.1f, 16.6f), Look = new Vector3(x, F + h + 1.65f, 10.3f), Fov = 21f });
                }
            }
            // Along the line-up from the balcony's end.
            if (t < BottleAt) return Move(t, 20.8f, BottleAt,
                new PodiumShot { Eye = new Vector3(7.0f, F + 1.9f, 11.7f), Look = new Vector3(-1.5f, F + 1.5f, 10.1f), Fov = 40f },
                new PodiumShot { Eye = new Vector3(7.0f, F + 2.1f, 11.4f), Look = new Vector3(-1.5f, F + 1.5f, 10.1f), Fov = 38f });
            // The champagne from below, drifting across.
            if (t < 31f) return Move(t, BottleAt, 31f,
                new PodiumShot { Eye = new Vector3(-2.2f, F + 1.2f, 18.6f), Look = new Vector3(0f, F + 1.6f, 10.3f), Fov = 34f },
                new PodiumShot { Eye = new Vector3(2.2f, F + 1.5f, 18.6f), Look = new Vector3(0f, F + 1.6f, 10.3f), Fov = 32f });
            // Over the winners' shoulders, the spray going out over the crews.
            if (t < ConfettiAt) return Move(t, 31f, ConfettiAt,
                new PodiumShot { Eye = new Vector3(-1.2f, F + 3.6f, 9.0f), Look = new Vector3(0.4f, lane + 2.6f, 19.0f), Fov = 50f },
                new PodiumShot { Eye = new Vector3(-0.6f, F + 3.7f, 9.0f), Look = new Vector3(0.6f, lane + 2.6f, 19.0f), Fov = 48f });
            // The confetti, pulling back to the whole pit building.
            return Move(t, ConfettiAt, Length,
                new PodiumShot { Eye = new Vector3(0.5f, lane + 4.2f, 18.6f), Look = new Vector3(0f, F + 1.8f, 10.2f), Fov = 36f },
                new PodiumShot { Eye = new Vector3(1.5f, lane + 9.5f, 27.0f), Look = new Vector3(0f, F + 1.2f, 10.2f), Fov = 40f });
        }

        private static PodiumShot Move(float t, float from, float to, PodiumShot a, PodiumShot b)
        {
            float k = Mathf.Clamp01((t - from) / (to - from));
            k = k * k * (3f - 2f * k) * 0.5f + k * 0.5f;
            return new PodiumShot { Eye = Vector3.Lerp(a.Eye, b.Eye, k), Look = Vector3.Lerp(a.Look, b.Look, k), Fov = Mathf.Lerp(a.Fov, b.Fov, k) };
        }
    }
}
