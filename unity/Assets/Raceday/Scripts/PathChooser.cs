using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Path choosing, experiment E1 of the racecraft spec. A few times a second of race time the car
    /// tries a handful of paths for the next few seconds: hold where it is going, move to one of
    /// several places either side of its racing line, and for each go, lift or brake. It predicts the
    /// cars near it along each path, throws out every path that breaks a hard limit, and takes the
    /// best of the rest by time, risk and the driver's aggression. Behaviour comes from the limits
    /// and the weights, not from rules per situation. A gliding car, far from the camera, does the
    /// same with fewer paths, less often. Racecraft keeps TORCS's full braking (filterBColl) and side
    /// steering (filterSColl) underneath as the last resort.
    /// </summary>
    public sealed class PathChooser
    {
        public const int Experiment = 1;

        private const int Steps = 30;
        private const float Tick = 0.1f;               // Steps * Tick: the 3 s looked ahead
        private const int DrawEvery = 3;
        private const float NearEvery = 0.1f, FarEvery = 0.25f;
        private static readonly float[] NearShifts = { 0f, -1.5f, 1.5f, -3f, 3f, -4.5f, 4.5f, -6f, 6f };
        private static readonly float[] FarShifts = { 0f, -3f, 3f };
        /// <summary>The waits before moving across that are tried, s.</summary>
        private static readonly float[] Delays = Knob("HALF", 1f) > 0f ? new[] { 0.5f, 1f, 2f } : new[] { 1f, 2f };
        private const float CarL = Racecraft.CarLength, CarW = Racecraft.CarWidth;
        /// <summary>Room a car is owed beside it, on top of a car's width, m.</summary>
        private const float RoomMargin = 0.3f;
        /// <summary>How long a car stuck behind a slower one waits for the next place to pass, s.</summary>
        private const float WaitToPass = 4f;
        /// <summary>How far past the paths' ends the lane they end in is costed, m.</summary>
        private const float TailLook = 300f;
        private const float G = 9.81f;
        /// <summary>Seconds a metre off the racing line costs on top of its lane's speeds: a car clear of traffic gets back on the line, rather than leave the door open (Ivan).</summary>
        private const float OffLineCost = 0.05f;
        /// <summary>Seconds of the car's own speed kept as a gap nose to tail, on top of the margin.</summary>
        private static readonly float Headway = Knob("HEAD", 0.15f);
        /// <summary>The headway kept on lap one and from a few seconds before a corner, when cars ahead brake hard; below 0, <see cref="Headway"/> throughout.</summary>
        private static readonly float HeadZone = Knob("HEADZONE", -1f), HeadNear = Knob("HEADNEAR", 4f);
        /// <summary>The headway this plan keeps, s.</summary>
        private float headway;
        private static readonly float RiskScale = Knob("RISK", 1f), PassScale = Knob("PASS", 1f), Hysteresis = Knob("HYST", 0.02f), StuckScale = Knob("STUCK", 1f);
        private static readonly float SideRisk = Knob("SIDE", 1f), CloseRisk = Knob("CLOSE", 1f);

        /// <summary>A weight, from AI_E1_<name> when it is set (for tuning runs), else its default.</summary>
        private static float Knob(string name, float value) =>
            float.TryParse(System.Environment.GetEnvironmentVariable("AI_E1_" + name), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : value;
        /// <summary>Seconds of the car's speed a following car keeps on top of <see cref="Headway"/>, so following never breaks H1.</summary>
        private static readonly float FollowExtra = Knob("EXTRA", 0f);
        /// <summary>Braking it plans on when closing up behind a car, m/s².</summary>
        private static readonly float CloseDecel = Knob("CLOSEDEC", 4f);
        /// <summary>How fast a following car closes the gap to the one it keeps, per second.</summary>
        private static readonly float FollowGain = Knob("GAIN", 0.5f), BrakeDrop = Knob("DROP", 3f), OpenDecel = Knob("OPEN", 6f), CapTrust = Knob("CAPT", 1f);
        private float lapLength;
        /// <summary>Below this speed a ghost is a stopped car on the track: an obstacle to slow for and go round, m/s.</summary>
        private const float StoppedSpeed = 10f;
        /// <summary>Speed below which a car may pass through a stopped ghost it can't get round, m/s.</summary>
        private static readonly float GhostCrawl = Knob("CRAWL", 6f);
        /// <summary>A moving ghost this close to the track, m, is planned round like any car (T17).</summary>
        private static readonly float GhostNear = Knob("GHOSTNEAR", 3f);
        /// <summary>T17: how far off the track a ghost heading back towards it is already planned round, m.</summary>
        private static readonly float GhostSee = Knob("GHOSTSEE", 15f);
        private static readonly bool Commit = Knob("COMMIT", 1f) > 0f;
        /// <summary>T9: H6 and defending judged on real pace (lap time), not only the closing speed of the moment.</summary>
        private static readonly bool RealPace = Knob("REALPACE", 1f) > 0f;
        /// <summary>T9: how much faster in real pace, m/s, a car behind must be for the car ahead to defend against it.</summary>
        private static readonly float DefendThreat = Knob("DEFENDTHREAT", 0.5f);
        /// <summary>T9: a car whose path takes it out from behind the car ahead moves out at the full rate, not the braking one.</summary>
        public static readonly bool PullOutFree = Knob("PULLOUTFREE", 0f) > 0f;
        private static readonly bool PreBreak = Knob("PREBREAK", 1f) > 0f;
        /// <summary>H1, T9: also try paths at fixed places across the track, beside the cars ahead within <see cref="FixedAhead"/> m and by both edges.</summary>
        private static readonly bool FixedLanes = Knob("FIXED", 1f) > 0f;
        private static readonly float FixedAhead = Knob("FIXEDAHEAD", 80f);
        /// <summary>H15: on the free stretch, places across the whole track this far apart, m; 0 for only beside each car ahead.</summary>
        private static readonly float Grid = Knob("GRID", 0.5f);
        /// <summary>H15: on the free stretch, never off the throttle while a flat-out route is open (Ivan).</summary>
        private static readonly bool FlatFirst = Knob("H15", 1f) > 0f, FlatFollow = Knob("H15F", 1f) > 0f;
        private static readonly bool FollowWhere = Knob("FOLLOWNOW", 1f) > 0f;
        private static readonly bool SideBuffer = Knob("SIDEBUF", 0f) > 0f;
        /// <summary>H15: flat out in the lane up to a car it would catch before the corner is a dead end: a route round it goes first.</summary>
        private static readonly bool CaughtFirst = Knob("H15C", 0f) > 0f;
        /// <summary>H15, H1: on the free stretch, the room past a car ahead beyond the car's own width, m; below 0, the driver's own passing margin.</summary>
        private static readonly float GapWidth = Knob("GAPWIDTH", -1f);
        private static readonly bool AvoidGhosts = Knob("AVOIDGHOSTS", 1f) > 0f;
        /// <summary>H12: a recovering ghost keeps off the racing line, on its own side of it, until it is back up to speed (Ivan).</summary>
        private static readonly float AltLine = Knob("ALTLINE", 5f), AltClear = Knob("ALTCLEAR", CarW + RoomMargin + 1f);
        /// <summary>H12: a stopped car ahead caps the speed, braking to the yellow flag's share by <see cref="YellowReach"/> before it.</summary>
        private static readonly bool YellowCap = Knob("YELLOWCAP", 1f) > 0f;
        /// <summary>Gap to a car alongside, side to side, inside which TORCS's parallel steering still takes over, m (spec: 2 m).</summary>
        public const float SideFilterGap = 2f;
        /// <summary>
        /// H2, H3: under E1.23 side steering is only for a contact about to happen, within this gap, m. Further off,
        /// a car that has left the room it owes keeps its committed line and doesn't open its steering (Ivan).
        /// </summary>
        public static readonly float SideImminent = Knob("SIDEGAP", 0.5f);
        public static readonly bool SideOweOnly = Knob("SIDEOWE", 1f) > 0f;
        public static readonly bool SideInside = Knob("SIDEIN", 1f) > 0f;
        /// <summary>H2, H3: in a turn the side steering may never steer a car out of the corner, at most hold its heading (Ivan).</summary>
        /// <summary>T20: a car ahead on the outside into a corner is predicted to close in down to the lane it owes.</summary>
        private static readonly bool DiveSee = Knob("DIVESEE", 1f) > 0f;
        private static readonly bool DivePredict = Knob("DIVEPRED", 1f) > 0f;
        public static readonly bool SideNoReverse = Knob("SIDEHOLD", 1f) > 0f;
        /// <summary>T1: off the braking zone, the full brakes are for a gap closing faster than they can shed,
        /// not for slowing to the car ahead's speed within the gap. Seconds of closing kept spare (Ivan).</summary>
        public static readonly float RelBrake = Knob("RELBRAKE", 1f);
        /// <summary>The closing speed, m/s, up to which <see cref="RelBrake"/> applies.</summary>
        public static readonly float RelClose = Knob("RELCLOSE", 8f);
        /// <summary>The speed, m/s, the car ahead must be past for <see cref="RelBrake"/>: not in the start's pack.</summary>
        public static readonly float RelFrom = Knob("RELFROM", 55f);
        /// <summary>T1: an escaping path flat out may beat a clean one that lifts.</summary>
        private static readonly bool EscapeGo = Knob("ESCGO", 1f) > 0f;
        private static readonly bool InsideRaces = Knob("INSIDERACES", 0f) > 0f;
        /// <summary>Seconds of score per second spent lifting or braking for a car ahead below its lane's speed.</summary>
        private static readonly float LiftCost = Knob("LIFT", 0.1f);
        /// <summary>
        /// Seconds of score a path pays, on the free stretch, for each second it is held under its own flat-out
        /// speed, as a share of that speed: being held is a cost of its own, not only the ground it loses (Ivan).
        /// </summary>
        private static readonly float HeldCost = Knob("HELDCOST", 1f);
        /// <summary>Whether, behind a car on the free stretch, each lane is also tried flat out following only what is ahead in it.</summary>
        private static readonly bool FollowPaths = Knob("FOLLOW", 1f) > 0f, FollowLapOne = Knob("FOLLOW1", 1f) > 0f;
        /// <summary>Deceleration, m/s², braking plans to open a short gap with.</summary>
        /// <summary>Inside the margin behind a car, how much closer a path may not get, m.</summary>
        private static readonly float TouchClose = Knob("TOUCH", 0.3f);
        private static readonly bool TouchLapOne = Knob("TOUCH1", 1f) > 0f;
        /// <summary>A car following at its gap, or a little behind it, counts as inside it: it may pull out from there, m.</summary>
        private static readonly float TouchSlack = Knob("TOUCHSLACK", 3f);
        private float touchNow;
        private static readonly float BrakeOpen = Knob("BOPEN", 12f), BesideBrake = Knob("BESIDE", 2f), BesideDrop = Knob("BDROP", 6f);
        /// <summary>The slowest car alongside this plan, m/s.</summary>
        private float besideSpeed = float.PositiveInfinity;
        private bool diving;
        private bool closeHeld;
        private static readonly bool CloseLeave = Knob("CLOSELEAVE", 1f) > 0f;
        /// <summary>T3: seconds of closing within which a car coming on the inside is owed its room, and the least closing speed that counts, m/s.</summary>
        private static readonly float CloseOwe = Knob("CLOSEOWE", 1.5f), CloseFrom = Knob("CLOSEFROM", 3f);
        private static readonly bool AlongRoom = Knob("ALONGROOM", 1f) > 0f;
        private float myBrakes = 10f;
        /// <summary>T3: seconds ahead within which a car ahead is followed as if it may brake at its hardest.</summary>
        private static readonly float HardAhead = Knob("HARDAHEAD", 2f);
        private float diveSpeed;
        /// <summary>T20: how far under the outside car's speed a car diving at it brakes to, m/s.</summary>
        /// <summary>How far ahead the outside car must be, as a share of a car length, before the inside car has no claim: its nose short of the other's middle.</summary>
        private static readonly float DiveFrom = Knob("DIVEFROM", 0.4f);
        private static readonly bool DiveCommit = Knob("DIVECOMMIT", 1f) > 0f;
        private static readonly float DiveDrop = Knob("DIVEDROP", 10f);
        /// <summary>The nearest car ahead not quite in this car's lane, which braking slows for when none is.</summary>
        private int aheadWide = -1;
        private static readonly float FallBehind = Knob("FALLBEHIND", 0f), WideAhead = Knob("WIDE", 3f), ZoneSwitch = Knob("ZSWITCH", 0f), SideSwitch = Knob("SSWITCH", 0.3f), ZoneHyst = Knob("ZHYST", 0f);
        /// <summary>Share of the side risk a car further back than <see cref="BehindFrom"/> car lengths carries: one behind is its own to miss.</summary>
        private static readonly float BehindRisk = Knob("BRISK", 0f), BehindFrom = Knob("BFROM", 0.5f), ToLine = Knob("TOLINE", 1f), OffLine = Knob("OFFLINE", OffLineCost);
        /// <summary>How far the room left to a car alongside on the inside goes from the passing margin to the room owed (H2: no more than owed, Ivan).</summary>
        private static readonly float OweTight = Knob("OWETIGHT", 0.7f);
        /// <summary>Score for covering the inside of the next corner with a car close behind (H4), s; how far across counts as covering it, m; and how close behind, s, but no closer than a car it has just passed, m.</summary>
        private static readonly float Defend = Knob("DEFEND", 0.3f), DefendCover = Knob("DCOVER", 3f), DefendWithin = Knob("DWITHIN", 0.45f), DefendFrom = Knob("DFROM", 10f);
        /// <summary>H4: the defensive move waits until the attacker would be on the tail within the move's time plus this, s.</summary>
        private static readonly float DefendLate = Knob("DLATE", 1f), DefendSlack = Knob("DSLACK", 0.2f);
        /// <summary>Gap kept nose to tail when moving across in front of a car behind, m: never across its nose (T8).</summary>
        private static readonly float SwipeGap = Knob("SWIPE", 1f);
        /// <summary>How far ahead of where the car is the line leaving a car its width may aim, m (H2).</summary>
        private static readonly float OweLead = Knob("OWELEAD", 2f), CommitLead = Knob("CLEAD", 1f);
        /// <summary>Room owed only to a car alongside and only on the track (Ivan, H2).</summary>
        /// <summary>H2, H3, T18b: the committed lane leaving a car on the inside its width is held at its place across the track.</summary>
        private static readonly bool OweHold = Knob("OWEHOLD", 1f) > 0f;
        /// <summary>H8, T6b: a path braking for a corner may tuck in behind a car ahead at no less than the gap it has now, and this many seconds clear, s.</summary>
        private static readonly float Tuck = Knob("TUCK", 0.03f);
        /// <summary>T20: in the tow on a straight, a path may close to this many seconds behind a car ahead, no faster than stops short of it if it brakes at its hardest, s.</summary>
        private static readonly float Tow = Knob("TOW", 0.05f);
        /// <summary>Seconds short of the next corner the tow ends.</summary>
        private static readonly float TowNear = Knob("TOWNEAR", 3f);
        /// <summary>H1: a path moving across, away from a car ahead it is already close behind, is not broken by the following gap.</summary>
        private static readonly float Escape = Knob("ESCAPE", 0.85f);
        private static readonly bool OweTrack = Knob("OWETRACK", 0f) > 0f, OweH1 = Knob("OWEH1", 1f) > 0f, OweCommit = Knob("OWECOMMIT", 1f) > 0f, OweEdge = Knob("OWEEDGE", 1f) > 0f;
        /// <summary>Score per second a pull-out waits: of two near-equal moves, the later one, so the car stays in the tow until it must move.</summary>
        private static readonly float LateBonus = Knob("LATE", 0.1f);
        /// <summary>Side-to-side gap H1 keeps to a car alongside, m.</summary>
        private static readonly float AlongsideGap = Knob("SIDEGAP", 0.5f);
        /// <summary>Share of its lane's speed a car may carry towards a stopped car, as under a yellow flag.</summary>
        private const float YellowShare = 0.7f;
        /// <summary>How far ahead a stopped car slows the cars coming, m.</summary>
        private const float YellowReach = 150f;
        /// <summary>Cost per second of running faster than a yellow flag allows, per share of the lane's speed over it.</summary>
        private const float YellowWeight = 20f;

        public enum Pedal { Go, Lift, Brake }

        /// <summary>One path tried: where it heads, how it uses the pedals, and the limit that threw it out, if any.</summary>
        public sealed class Candidate
        {
            /// <summary>Where it was made in the plan: the lane index, below 0 for staying, holding or owing.</summary>
            internal int Wave;
            /// <summary>For the same move made later, the path it is a later copy of.</summary>
            internal Candidate Of;
            public float Shift;
            public Pedal Pedal;
            /// <summary>Seconds it stays where it is across the track before it moves: staying in the tow until the last safe moment.</summary>
            public float Delay;
            /// <summary>Keeps where it is across the track, whatever the racing line does.</summary>
            public bool Stay;
            /// <summary>Takes its line, but no nearer the inside than leaves a car alongside there exactly the width it is owed (H2).</summary>
            public bool Owe;
            /// <summary>H1, T9: heads for a fixed place across the track, <see cref="Lat"/>, whatever the racing line does.</summary>
            public bool Fixed;
            /// <summary>H1: kept only by moving away from a car ahead it is already inside the following gap of.</summary>
            public bool Escaped;
            /// <summary>H15: it ends behind a slower car in that car's lane, and would catch it before the next braking point.</summary>
            public bool EndsCaught;
            /// <summary>H15: it ends behind a slower car in that car's lane.</summary>
            public bool EndsBehind;
            /// <summary>Where a fixed path runs, m left of the track centre.</summary>
            public float Lat;
            /// <summary>How far its offset from the racing line moves in the first step: where an owing path steers now.</summary>
            public float SoonOff;
            public string Rejected;
            public float Score;
            /// <summary>What the score is made of, in seconds: ground covered, closeness to cars, time stuck behind one.</summary>
            public float Gain, Risk, Stuck;
            /// <summary>Flat out, following only the car ahead in the lane it is in at each step, not the one ahead now.</summary>
            public bool Follow;
            /// <summary>For a following path, the car it follows after its first step, or -1.</summary>
            internal int FollowFirst = -1;
            /// <summary>Seconds held under its own flat-out speed on the free stretch, as a share of that speed.</summary>
            public float Below;
            /// <summary>Metres still off the line at the next braking zone, on the free stretch.</summary>
            public float Unplaced;
            /// <summary>Offset from the racing line where the path ends.</summary>
            public float EndOff;
            /// <summary>Step at which it first broke its limit, for when every path breaks one.</summary>
            public int BrokeAt;
            /// <summary>The car whose limit it broke, or -1.</summary>
            public int BrokeBy = -1;
            /// <summary>Whether it broke its limit moving across into a car coming from behind.</summary>
            public bool BrokeBehind;
            /// <summary>Where the path goes, as race distance and metres left of the track centre.</summary>
            public readonly Vector2[] Path = new Vector2[Steps / DrawEvery + 1];
        }

        public readonly List<Candidate> Candidates = new List<Candidate>();
        public Candidate Chosen { get; private set; }
        /// <summary>The offset from the racing line the car is heading for, m, positive left.</summary>
        public float Target { get; private set; }
        /// <summary>H2, H3: where across the track the committed lane leaving a car on the inside its width is.</summary>
        public float OweLane { get; private set; }
        /// <summary>The inside of the corner for <see cref="OweLane"/>, -1 or 1, or 0 when no such lane is held.</summary>
        public int OweInside { get; private set; }

        /// <summary>The pilot's offset, kept no deeper than <see cref="OweLane"/> with the racing line where it is now.</summary>
        public float HoldOwe(float offset, float lineHere, float across = 0f)
        {
            if (OweInside == 0) return offset;
            // Moving in towards the lane, the car runs on past where it is aimed: aim short of it by that much (T3, T20, Ivan).
            float lane = OweLane - OweInside * OweLead2 * Mathf.Max(OweInside * across, 0f);
            return OweInside * (lineHere + offset) > OweInside * lane ? lane - lineHere : offset;
        }
        /// <summary>Seconds of the car's speed across towards the inside it aims short of the owed lane by.</summary>
        private static readonly float OweLead2 = Knob("OWEAHEAD2", 0.5f);
        /// <summary>The speed the chosen path holds the car to, m/s, or infinity.</summary>
        public float Cap { get; private set; } = float.PositiveInfinity;
        /// <summary>Why the car is doing what it does, for the debug menu, or null when it is free on its line.</summary>
        public string Reason { get; private set; }

        private float clock = float.MaxValue;
        // The defending move: 0 none yet, 1 made, 2 coming back.
        private int movePhase;
        private float moveDir;
        private float lastShift;
        private Pedal lastPedal;
        // Braking for a corner at the last choice, and whether that choice left a car its width (H13).
        private bool lastBraking, lastOwe;

        // The cars near this one, and where they are predicted to be at each step.
        private readonly List<TrafficCar> others = new List<TrafficCar>();
        /// <summary>For each car near: how far its body reaches across the track either side of its middle, m, and whether it is a stopped car.</summary>
        private readonly List<float> reach = new List<float>();
        private readonly List<bool> stopped = new List<bool>();
        private float[,] od = new float[0, 0], ol = new float[0, 0], ov = new float[0, 0];
        private readonly List<int> order = new List<int>();
        private bool placed;
        /// <summary>H12: the side of the racing line a recovering car keeps to, or 0 when it is not recovering.</summary>
        private float altSide;
        /// <summary>H1, T9: the chosen path holds a fixed place across the track, <see cref="fixedLat"/>.</summary>
        private bool fixedOn;
        private float fixedLat;
        private readonly List<float> fixedLats = new List<float>();
        private readonly List<Candidate> baseCands = new List<Candidate>(), laterCands = new List<Candidate>();
        /// <summary>Paths driven on worker threads; 0 drives every path on the calling thread, for comparing.</summary>
        private static readonly bool ParallelPaths = Knob("PARALLEL", 1f) > 0f;

        /// <summary>
        /// Drives every path in the list. A path writes only to itself and reads what this plan gathered,
        /// so the threads change nothing but how long it takes.
        /// </summary>
        private void SimulateAll(List<Candidate> cands, AiPilot pilot, TrafficCar me, int ahead, int nextTurn, float margin,
            float brakes, float riskWeight, float passCost, float freeSpeed)
        {
            if (!ParallelPaths || cands.Count < 4)
            {
                foreach (Candidate cand in cands) Simulate(pilot, me, cand, ahead, nextTurn, margin, brakes, riskWeight, passCost, freeSpeed);
                return;
            }
            System.Threading.Tasks.Parallel.For(0, cands.Count, i => Simulate(pilot, me, cands[i], ahead, nextTurn, margin, brakes, riskWeight, passCost, freeSpeed));
        }

        /// <summary>Picks a path if it is time to, and returns whether it did.</summary>
        public bool Plan(AiPilot pilot, TrafficCar me, IReadOnlyList<TrafficCar> field, float dt)
        {
            // A car placed off its line starts steering where it is, not for the line: three placed
            // abreast would otherwise all swerve for it at once (H1).
            if (!placed && pilot.Index >= 0)
            {
                placed = true;
                pilot.Offset = Mathf.Clamp(me.Lateral - pilot.Line.Lateral[pilot.Index], AiPilot.LaneShifts[0], AiPilot.LaneShifts[AiPilot.LaneShifts.Length - 1]);
            }
            clock += dt;
            float every = pilot.Gliding ? FarEvery : NearEvery;
            if (clock < every - 1e-4f)
            {
                // A fixed place across the track: the offset that keeps it there moves with the racing line.
                if (fixedOn && pilot.Index >= 0) Target = Mathf.Clamp(fixedLat - pilot.Line.Lateral[pilot.Index], AiPilot.LaneShifts[0], AiPilot.LaneShifts[AiPilot.LaneShifts.Length - 1]);
                return false;
            }
            clock = 0f;
            Choose(pilot, me, field);
            return true;
        }

        private void Choose(AiPilot pilot, TrafficCar me, IReadOnlyList<TrafficCar> field)
        {
            RacingLine line = pilot.Line;
            Circuit c = line.Circuit;
            float lap = c.Lap;
            float here = Mathf.Repeat(me.Distance, lap);
            lapLength = lap;
            Gather(c, me, field);
            Predict(c, me, NextTurn(c, here));

            // What the cars around ask of this one, from where they are now.
            float mySpeed = me.Speed;
            bool lapOne = me.Distance < lap;
            touchNow = (TouchLapOne || !lapOne) && (TouchLapOne || FreeStretch(pilot)) ? TouchClose : 0f;
            float fastLateral = 0f;
            bool defending = false, fastBehind = false, lapped = false, passing = false;
            float lappingLateral = 0f, attackerGap = float.NegativeInfinity, attackerLateral = 0f, attackerClosing = 0f;
            float aheadInLane = float.PositiveInfinity;
            int ahead = -1;
            float aheadGap = 60f;
            besideSpeed = float.PositiveInfinity;
            diving = false;
            diveSpeed = float.PositiveInfinity;
            aheadWide = -1;
            float wideGap = 60f;
            float freeSpeed = Mathf.Max(mySpeed, pilot.FreeSpeed);
            int turnSoon = NextTurn(c, here);
            for (int j = 0; j < others.Count; j++)
            {
                TrafficCar o = others[j];
                float g = Road(o.Distance - me.Distance, lap);
                bool lapping = o.Distance - me.Distance > lap * 0.5f;
                if (g < -CarL && -g < 2f * Mathf.Max(mySpeed, 10f) && lapping) { lapped = true; lappingLateral = o.Lateral; }
                float closing = o.Speed - mySpeed;
                // T9: how much faster the car behind really is, from its pace over a lap, not only how fast it
                // closes this moment: one braking behind a train is no slower for it (Ivan).
                float threat = RealPace && o.LapPace > 0f ? Mathf.Max(closing, mySpeed * (pilot.Profile.LapTime / o.LapPace - 1f)) : closing;
                // Only a car with a real attacker behind defends: a train of equal cars doesn't all chop (T9).
                bool attacker = !RealPace || threat > DefendThreat;
                if (g < -CarL && !lapping && -g < mySpeed * 1f && o.Speed > mySpeed - 5f && attacker && (!defending || g > attackerGap)) { defending = true; attackerGap = g; attackerLateral = o.Lateral; attackerClosing = closing; }
                if (g < -CarL && !lapping && threat > Endurance.NoDefendClosing && -g < threat * 2f) { fastBehind = true; fastLateral = o.Lateral; }
                if (g > CarL && g < mySpeed * 1f && o.Speed < freeSpeed) passing = true;
                if (g > 0f && g < aheadGap && Mathf.Abs(o.Lateral - me.Lateral) < CarW + RoomMargin && !(stopped[j] && mySpeed < GhostCrawl + 1f)) { aheadGap = g; ahead = j; aheadInLane = o.Speed; }
                if (g > CarL && g < wideGap && Mathf.Abs(o.Lateral - me.Lateral) < WideAhead * CarW) { wideGap = g; aheadWide = j; }
                // A car alongside and level or ahead: one this car drops back from. One behind drops back itself.
                // H2, H3: on the inside into a corner it is owed its room and races wheel to wheel, not dropping back from the car on its outside (Ivan).
                bool onItsInside = InsideRaces && turnSoon != 0 && Mathf.Sign(o.Lateral - me.Lateral) == turnSoon;
                // T20: a car ahead on the outside into the corner, with this one short of the slot it leaves on the inside.
                int ki = c.IndexOf(here);
                if (DiveSee && turnSoon != 0 && g > DiveFrom * CarL && g < CarL + 2f && (o.Lateral - me.Lateral) * turnSoon > CarW
                    && -turnSoon * me.Lateral < c.HalfWidth(ki) - 0.5f * CarW - RoomMargin - 0.15f) { diving = true; diveSpeed = Mathf.Min(diveSpeed, o.Speed); }
                if (!onItsInside && g > (BesideBrake >= 2f ? 0f : -CarL - 1f) && g < CarL + 1f && Mathf.Abs(o.Lateral - me.Lateral) < 2f * CarW) besideSpeed = Mathf.Min(besideSpeed, o.Speed);
            }
            bool exempt = defending && passing;
            bool zone = TurnAt(c, here) != 0 || BrakingZone(pilot);
            // From the corner's exit to the next braking zone the racing line is only the default (Ivan):
            // nothing holds the car to a path it chose, so it takes the open side the moment that is better.
            bool free = FreeStretch(pilot);
            float offLine = free ? FreeOff * OffLine : OffLine, hyst = free ? 0f : Hysteresis;
            headway = HeadZone >= 0f && (lapOne || zone || Road(NextCornerStart(c, here) - here, lap) < HeadNear * mySpeed) ? HeadZone : Headway;
            int nextTurn = NextTurn(c, here);
            float myOffset = me.Lateral - line.Lateral[pilot.Index];
            float margin = Endurance.MarginScale(pilot.Safety);
            float riskWeight = Mathf.Lerp(0.05f, 0.4f, pilot.Safety / 100f) * (lapOne ? 2f : 1f) * RiskScale;
            float passCost = Mathf.Lerp(0.02f, 0.15f, pilot.Safety / 100f) * PassScale;
            // What it can slow at: the brakes, or the tyres with the downforce if they give out first.
            float brakes = Mathf.Min(pilot.Spec.BrakeDecel * G, pilot.TyreGrip(me.Speed)) * AiPilot.LimitFor(pilot.Pace);
            myBrakes = brakes;

            // H12: a ghost getting back up to speed takes the alternative line, clear of the racing line on its
            // own side, and leaves the line to the cars coming (Ivan).
            float lineHere = line.Lateral[pilot.Index], edgeHere = Mathf.Max(c.HalfWidth(pilot.Index) - RacingLine.EdgeMargin, 0f);
            bool recovering = AltLine > 0f && me.Ghost && mySpeed < Rejoin.GhostSpeedShare * pilot.Profile.Speed[pilot.Index];
            // The side it came back from, kept until it is up to speed: it never crosses the line meanwhile.
            if (!recovering) altSide = 0f;
            else if (altSide == 0f) altSide = Mathf.Abs(myOffset) > 0.5f ? Mathf.Sign(myOffset) : -Mathf.Sign(lineHere);
            float mySide = altSide;
            float AltCost(float shift)
            {
                if (!recovering) return 0f;
                float to = Mathf.Clamp(lineHere + shift, -edgeHere, edgeHere) - lineHere;
                return Mathf.Abs(to) < AltClear || mySide != 0f && Mathf.Sign(to) != mySide ? AltLine : 0f;
            }

            Candidates.Clear();
            float[] shifts = pilot.Gliding ? FarShifts : NearShifts;
            int pedals = pilot.Gliding ? 2 : 3;
            // A car alongside on the inside into the corner: also the line that leaves it exactly its width.
            bool owedNear = false;
            float closeDepth = float.PositiveInfinity;
            if (nextTurn != 0)
                foreach (TrafficCar o in others)
                {
                    if (Mathf.Abs(Road(o.Distance - me.Distance, lap)) < (OweTrack ? CarL : 2f * CarL) && Mathf.Sign(o.Lateral - me.Lateral) == -nextTurn && Mathf.Abs(o.Speed) >= StoppedSpeed) owedNear = true;
                    // T3: a car closing fast on the inside, alongside before long, is left its room too, not turned in on (Ivan).
                    if (CloseOwe > 0f && Road(o.Distance - me.Distance, lap) < 0f && -Road(o.Distance - me.Distance, lap) < CarL + CloseOwe * Mathf.Max(o.Speed - mySpeed, 0f)
                        && (o.Speed - mySpeed > CloseFrom || closeHeld && -Road(o.Distance - me.Distance, lap) < CarL + 2f) && Mathf.Sign(o.Lateral - me.Lateral) == -nextTurn && !o.Ghost)
                    {
                        owedNear = true;
                        closeDepth = Mathf.Min(closeDepth, -nextTurn * o.Lateral);
                    }
                    // H3d, H3b: a car overlapping on the inside, not yet down at the edge, is left its room from where it is (Ivan).
                    else if (AlongRoom && Mathf.Abs(Road(o.Distance - me.Distance, lap)) < CarL && Mathf.Sign(o.Lateral - me.Lateral) == -nextTurn && !o.Ghost && Mathf.Abs(o.Speed) >= StoppedSpeed)
                    {
                        owedNear = true;
                        closeDepth = Mathf.Min(closeDepth, -nextTurn * o.Lateral);
                    }
                }
            // Once left room it keeps leaving it while that car is still at its side, slowing or not.
            closeHeld = !float.IsPositiveInfinity(closeDepth);
            // H4: with a car close behind on a straight, one move to cover the inside of the next corner is worth something (Ivan).
            int coverTurn = NextTurn(c, here, float.PositiveInfinity);
            // H4: the move as late as it can be while the attacker still needn't brake for it: once the attacker,
            // closing as it is, would be on this car's tail within the time the move takes plus DefendSlack (Ivan).
            float tail = CarL + margin + headway * mySpeed, catchIn = (-attackerGap - tail) / Mathf.Max(attackerClosing, 0.1f);
            bool late = DefendLate <= 0f || movePhase == 1 || catchIn < DefendCover / Endurance.OvertakeOffsetRate + DefendSlack;
            bool far = DefendLate > 0f ? movePhase == 1 || -attackerGap > tail : -attackerGap < DefendWithin * Mathf.Max(mySpeed, 1f) && -attackerGap > DefendFrom;
            bool cover = Defend > 0f && defending && !exempt && !zone && movePhase != 2 && coverTurn != 0 && late && far;
            float CoverBonus(float shift) => cover ? Defend * Mathf.Clamp01(-coverTurn * shift / DefendCover) : 0f;
            // The paths are made first, driven together (H-limits and scores need nothing from each other), then
            // weighed in the order they were made, so the choice is the same however many threads drive them.
            baseCands.Clear();
            for (int s = owedNear ? -3 : -2; s < shifts.Length; s++)
                for (int p = 0; p < pedals; p++)
                {
                    // -2 stays where it is across the track, -1 holds where it was heading.
                    var cand = new Candidate { Shift = s == -3 ? 0f : s == -2 ? myOffset : s < 0 ? Target : shifts[s], Stay = s == -2, Owe = s == -3, Pedal = pilot.Gliding && p == 1 ? Pedal.Brake : (Pedal)p };
                    if (s == -1 && Mathf.Abs(Target - shifts[0]) < 0.01f) continue;   // holding is heading for the line
                    bool away = !cand.Stay && Mathf.Abs(cand.Shift) > Mathf.Abs(myOffset) + 0.3f;
                    // A defensive move goes off the line towards the attacker's side: moving away from it leaves room.
                    bool blocks = away && Mathf.Sign(cand.Shift - myOffset) == Mathf.Sign(attackerLateral - me.Lateral);
                    if (lapped && !zone && Mathf.Abs(cand.Shift) < 1.5f) cand.Rejected = "H9";
                    else if (lapped && !zone && Mathf.Sign(cand.Shift) == Mathf.Sign(lappingLateral - line.Lateral[pilot.Index]) && Mathf.Abs(lappingLateral - line.Lateral[pilot.Index]) > 1f) cand.Rejected = "H9";
                    // T3: moving away from the fast car's side leaves it room, which H6 doesn't forbid (Ivan: the slow car is the more conservative).
                    else if (fastBehind && away && (!CloseLeave || float.IsPositiveInfinity(closeDepth) || Mathf.Sign(cand.Shift - myOffset) == Mathf.Sign(fastLateral - me.Lateral))) cand.Rejected = "H6";
                    else if (defending && !exempt && zone && blocks) cand.Rejected = "H5";
                    else if (defending && !exempt && !zone && away && (movePhase == 2 || movePhase == 1 && Mathf.Sign(cand.Shift - myOffset) != moveDir)) cand.Rejected = "H4";
                    cand.Wave = s;
                    baseCands.Add(cand);
                }
            // Behind a car on the free stretch: each lane also flat out, following only what is ahead in that lane.
            if (FollowPaths && (FollowLapOne || !lapOne) && ahead >= 0 && !pilot.Gliding && FreeStretch(pilot))
                for (int s = 0; s < shifts.Length; s++)
                {
                    var cand = new Candidate { Shift = shifts[s], Pedal = Pedal.Lift, Follow = true, Wave = -4 };
                    bool away = Mathf.Abs(cand.Shift) > Mathf.Abs(myOffset) + 0.3f;
                    if (lapped && !zone && Mathf.Abs(cand.Shift) < 1.5f) cand.Rejected = "H9";
                    else if (lapped && !zone && Mathf.Sign(cand.Shift) == Mathf.Sign(lappingLateral - line.Lateral[pilot.Index]) && Mathf.Abs(lappingLateral - line.Lateral[pilot.Index]) > 1f) cand.Rejected = "H9";
                    else if (fastBehind && away && (!CloseLeave || float.IsPositiveInfinity(closeDepth) || Mathf.Sign(cand.Shift - myOffset) == Mathf.Sign(fastLateral - me.Lateral))) cand.Rejected = "H6";
                    else if (defending && !exempt && !zone && away && (movePhase == 2 || movePhase == 1 && Mathf.Sign(cand.Shift - myOffset) != moveDir)) cand.Rejected = "H4";
                    baseCands.Add(cand);
                }
            SimulateAll(baseCands, pilot, me, ahead, nextTurn, margin, brakes, riskWeight, passCost, freeSpeed);

            // The same moves later, flat out, in the tow until then: each starts from its own path's verdict.
            laterCands.Clear();
            foreach (Candidate cand in baseCands)
            {
                // H2, T20: diving with no claim, it backs out all the way: braking is the only path, until it is behind (Ivan).
                if (DiveCommit && diving && cand.Pedal != Pedal.Brake && cand.Rejected == null) cand.Rejected = "H8";
                if (cand.Wave < 0 || cand.Pedal != Pedal.Go || pilot.Gliding || Mathf.Abs(cand.Shift - myOffset) < 0.5f) continue;
                // Waiting in the tow is only for a car at full throttle: held below it, it pulls out at once (Ivan).
                if (NoWait && pilot.HeldBy > 0.5f) continue;
                foreach (float wait in Delays)
                {
                    var later = new Candidate { Shift = cand.Shift, Pedal = Pedal.Go, Delay = wait, Of = cand };
                    if (lapped && !zone && Mathf.Abs(later.Shift) < 1.5f) later.Rejected = "H9";
                    else later.Rejected = cand.Rejected;
                    laterCands.Add(later);
                }
            }

            // H1, T9: paths at fixed places across the track, beside each car ahead and by both edges. Where the
            // racing line runs diagonally across the track, every lane off it crosses the cars ahead too, while
            // the track beside them is open: pick a side and go, all the way to the edge if need be (Ivan).
            int fixedFrom = laterCands.Count;
            if (FixedLanes && !pilot.Gliding && !defending && !lapped && !recovering && !owedNear && !BrakingZone(pilot))
            {
                fixedLats.Clear();
                fixedLats.Add(edgeHere);
                fixedLats.Add(-edgeHere);
                // H15: on the free stretch, the route round the cars ahead may be at any width across the track (Ivan).
                if (Grid > 0f && FreeStretch(pilot))
                    for (float g = -edgeHere + Grid; g < edgeHere - 0.25f * Grid; g += Grid) fixedLats.Add(g);
                float clear = Clear(margin) + 0.2f;
                foreach (TrafficCar o in others)
                {
                    float g = Road(o.Distance - me.Distance, lap);
                    if (g < -CarL || g > FixedAhead) continue;
                    fixedLats.Add(Mathf.Clamp(o.Lateral + clear, -edgeHere, edgeHere));
                    fixedLats.Add(Mathf.Clamp(o.Lateral - clear, -edgeHere, edgeHere));
                }
                for (int i = 0; i < fixedLats.Count; i++)
                {
                    float fl = fixedLats[i];
                    bool dup = false;
                    for (int q = 0; q < i; q++) if (Mathf.Abs(fixedLats[q] - fl) < 0.5f) dup = true;
                    if (dup) continue;
                    var fixedCand = new Candidate { Fixed = true, Lat = fl, Shift = fl - lineHere, Pedal = Pedal.Go };
                    if (fastBehind && Mathf.Abs(fixedCand.Shift) > Mathf.Abs(myOffset) + 0.3f && (!CloseLeave || float.IsPositiveInfinity(closeDepth) || Mathf.Sign(fixedCand.Shift - myOffset) == Mathf.Sign(fastLateral - me.Lateral))) fixedCand.Rejected = "H6";
                    laterCands.Add(fixedCand);
                }
            }
            SimulateAll(laterCands, pilot, me, ahead, nextTurn, margin, brakes, riskWeight, passCost, freeSpeed);

            // Weighed in the order made.
            int li = 0;
            foreach (Candidate cand in baseCands)
            {
                // A little against changing its mind, so it doesn't dither between two near-equal paths.
                cand.Score -= (zone ? hyst + ZoneHyst : hyst) * LineChange(cand.Shift, lastShift) + (cand.Pedal != lastPedal ? 0.01f : 0f) + offLine * Mathf.Abs(cand.Shift);
                // Changing sides of the car ahead mid-move weaves across the cars behind: only for a clearly better path.
                if (Mathf.Abs(cand.Shift) > 1f && Mathf.Abs(lastShift) > 1f && Mathf.Sign(cand.Shift) != Mathf.Sign(lastShift)) cand.Score -= SideSwitch;
                cand.Score += CoverBonus(cand.Shift) - AltCost(cand.Stay ? myOffset : cand.Owe ? 0f : cand.Shift);
                // Once braking for a car in the braking zone, it stays on the brakes rather than flick back on the throttle.
                if (zone && lastPedal == Pedal.Brake && cand.Pedal == Pedal.Go) cand.Score -= ZoneSwitch;
                Candidates.Add(cand);
                if (li >= fixedFrom || laterCands[li].Of != cand) continue;
                for (; li < fixedFrom && laterCands[li].Of == cand; li++)
                {
                    Candidate later = laterCands[li];
                    later.Score -= (zone ? hyst + ZoneHyst : hyst) * LineChange(later.Shift, lastShift) + offLine * Mathf.Abs(later.Shift);
                    later.Score += CoverBonus(later.Shift) - Mathf.Max(AltCost(later.Shift), AltCost(myOffset));   // it holds where it is meanwhile
                    Candidates.Add(later);
                }
                // In the tow on a straight, of near-equal moves the later one wins, while a still later one
                // is open too: the car moves out at its last safe moment with a step in hand, as it lags its plan.
                // Only a move off the line waits: a move back to it is made at once.
                // Where it ends up, not the lane it aims for: a lane past the track's edge is the edge (T8).
                if (!defending && !zone && (EndWait ? Mathf.Abs(cand.EndOff) > Mathf.Abs(myOffset) + 0.25f : Mathf.Abs(cand.Shift) > Mathf.Abs(myOffset)))
                    for (int i = Candidates.Count - Delays.Length; i < Candidates.Count - 1; i++)
                        if (Candidates[i + 1].Rejected == null) Candidates[i].Score += LateBonus * Candidates[i].Delay;
            }
            for (int i = fixedFrom; i < laterCands.Count; i++)
            {
                Candidate fixedCand = laterCands[i];
                fixedCand.Score -= (zone ? hyst + ZoneHyst : hyst) * LineChange(fixedCand.Shift, lastShift) + 0.01f + offLine * Mathf.Abs(fixedCand.Shift);
                if (Mathf.Abs(fixedCand.Shift) > 1f && Mathf.Abs(lastShift) > 1f && Mathf.Sign(fixedCand.Shift) != Mathf.Sign(lastShift)) fixedCand.Score -= SideSwitch;
                Candidates.Add(fixedCand);
            }

            Candidate best = null;
            // H13: from the braking point to the apex the car keeps the path it chose, and changes it only
            // when that path breaks a limit (Ivan).
            bool braking = BrakingZone(pilot);
            bool sideBySide = false;
            foreach (TrafficCar o in others)
                if (Mathf.Abs(Road(o.Distance - me.Distance, lap)) < CarL && Mathf.Abs(o.Lateral - me.Lateral) < 3f * CarW) sideBySide = true;
            if (Commit && braking && lastBraking && sideBySide)
                foreach (Candidate k in Candidates)
                {
                    bool same = k.Delay == 0f && !k.Stay && (lastOwe ? k.Owe : !k.Owe && Mathf.Abs(k.Shift - lastShift) < 0.3f);
                    if (same && k.Rejected == null && (best == null || k.Score > best.Score)) best = k;
                }
            if (best == null)
                foreach (Candidate k in Candidates)
                    if (k.Rejected == null && !k.Escaped && (best == null || k.Score > best.Score)) best = k;
            // H15: on the free stretch, lifting is only for when no route is open flat out, at any width: a flat path
            // that ends sitting behind a car it would catch before the corner is no route round it. Moving out at the
            // speed it follows at, then flat once clear, is such a route: out first, then throttle (Ivan).
            if (FlatFirst && best != null && (best.Pedal != Pedal.Go || CaughtFirst && best.EndsCaught) && !braking && !pilot.Gliding && FreeStretch(pilot))
            {
                Candidate flat = null;
                foreach (Candidate k in Candidates)
                    if (k.Rejected == null && !k.Escaped && (k.Pedal == Pedal.Go && !k.EndsCaught || FlatFollow && k.Follow && !k.EndsBehind) && (flat == null || k.Score > flat.Score)) flat = k;
                if (flat != null) best = flat;
            }
            // T1: off the braking zone, flat out moving away from a car that cuts across beats lifting behind it (Ivan).
            if (EscapeGo && best != null && best.Pedal != Pedal.Go && !braking)
                foreach (Candidate k in Candidates)
                    if (k.Rejected == null && k.Escaped && k.Pedal == Pedal.Go && k.Score > best.Score) best = k;
            // H1: boxed in behind a car with every path breaking a limit, the way out is across, away from it (Ivan).
            if (best == null)
                foreach (Candidate k in Candidates)
                    if (k.Rejected == null && (best == null || k.Score > best.Score)) best = k;
            if (best == null)
                // Every path breaks a limit: brake, on the path that keeps the limit longest, moving across as little as it can.
                foreach (Candidate k in Candidates)
                {
                    if (k.Pedal != Pedal.Brake && !pilot.Gliding) continue;
                    // Moving across into a car from behind is worse than braking short of one ahead.
                    float ka = k.BrokeAt - (k.BrokeBehind ? FallBehind : 0f), ba = best == null ? 0f : best.BrokeAt - (best.BrokeBehind ? FallBehind : 0f);
                    if (best == null || ka > ba || ka == ba && Mathf.Abs(k.Shift - myOffset) < Mathf.Abs(best.Shift - myOffset)) best = k;
                }
            if (best == null) best = Candidates[0];
            Chosen = best;
            // The pilot's own offset, not where the car is, as the car lags what its pilot steers.
            // A path that moves later holds where it is for now: in the tow until the last safe moment.
            // Leaving a car its width steps on from the pilot's offset, but no more than OweLead ahead of where
            // the car is: a car wide at the limit lags its pilot, which would otherwise step on and on until it
            // aims across the car it owes (H2).
            float owe = pilot.Offset + best.SoonOff;
            OweInside = 0;
            if (best.Owe && OweLead > 0f) owe = Mathf.Clamp(owe, myOffset + best.SoonOff - OweLead, myOffset + best.SoonOff + OweLead);
            if (best.Owe && OweCommit && nextTurn != 0)
            {
                // Committed to the lane that leaves the car alongside its width from the inside edge, or the
                // racing line where that is wider: steered and taken at that lane's speed, not re-planned wider.
                int kh = pilot.Index;
                float bnd = c.HalfWidth(kh) - 1.5f * CarW - RoomMargin - 0.15f, lineK = line.Lateral[kh];
                // T3: a car closing fast on the inside is left the room where it is, not only from the edge (Ivan: evasive).
                bnd = Mathf.Min(bnd, closeDepth - CarW - RoomMargin);
                owe = -nextTurn * lineK > bnd ? -nextTurn * bnd - lineK : 0f;
                // H2, H3, T18b: that lane is a fixed place across the track, held as the line dives past it, so
                // the car neither lags onto the inside car nor opens its steering (Ivan).
                if (OweHold) { OweLane = -nextTurn * bnd; OweInside = -nextTurn; }
                if (CommitLead > 0f) owe = Mathf.Clamp(owe, myOffset - CommitLead, myOffset + CommitLead);   // no further from where the car is than it can follow
            }
            Target = best.Owe ? owe : best.Delay > 0f ? pilot.Offset : best.Shift;
            fixedOn = best.Fixed;
            fixedLat = best.Lat;
            // T20c: a path that moves later holds the car's place across the track for now, not its offset
            // from a line that sweeps under it (Ivan).
            if (HoldAbs && pilot.Index >= 0 && !best.Owe && !best.Fixed && best.Delay > 0f && (!HoldFree || FreeStretch(pilot)) && Mathf.Abs(pilot.Offset) > HoldOff)
            {
                // On the track: never a place held past its edge.
                float holdEdge = Mathf.Max(c.HalfWidth(pilot.Index) - RacingLine.EdgeMargin, 0f);
                fixedOn = true;
                fixedLat = Mathf.Clamp(line.Lateral[pilot.Index] + pilot.Offset, -holdEdge, holdEdge);
            }
            int followNow = best.FollowFirst;
            if (best.Follow && FollowWhere)
            {
                // Out first, then throttle: it follows whatever is still in its way from where the car is now, not
                // where its plan has it a step on, as the car lags its pilot (Ivan).
                float clearNow = (FreeStretch(pilot) && GapWidth >= 0f ? CarW + GapWidth : Clear(margin)) - 0.5f * CarW + 0.1f, near = float.PositiveInfinity;
                followNow = -1;
                for (int j = 0; j < others.Count; j++)
                {
                    float gj = Road(od[j, 0] - me.Distance, lap);
                    if (gj > 0f && gj < near && Mathf.Abs(ol[j, 0] - me.Lateral) < clearNow + reach[j]) { near = gj; followNow = j; }
                }
            }
            Cap = best.Follow ? (followNow < 0 ? float.PositiveInfinity : CapAt(Pedal.Lift, followNow, 0, mySpeed, me.Distance, margin)) : CapAt(best.Pedal, ahead, 0, mySpeed, me.Distance, margin);
            if (YellowCap)
                for (int j = 0; j < others.Count; j++)
                {
                    // A stopped car on the track ahead: down to the yellow flag's share of its racing speed by
                    // YellowReach before it, until past it (H12).
                    float g = Road(others[j].Distance - me.Distance, lap);
                    if (!stopped[j] || g < -CarL) continue;
                    float yellowSpeed = YellowShare * pilot.Profile.Speed[pilot.Index];
                    Cap = Mathf.Min(Cap, Mathf.Sqrt(yellowSpeed * yellowSpeed + 2f * 0.8f * brakes * Mathf.Max(g - YellowReach, 0f)));
                }
            lastShift = best.Shift;
            lastPedal = best.Pedal;
            lastBraking = braking;
            lastOwe = best.Owe;

            // The one move: noted on the path taken.
            bool movedAway = Mathf.Abs(best.Shift) > Mathf.Abs(myOffset) + 0.3f;
            bool movedBack = Mathf.Abs(best.Shift) < Mathf.Abs(myOffset) - 0.3f;
            if (!defending || exempt) movePhase = 0;
            else if (!zone && movePhase == 0 && movedAway) { movePhase = 1; moveDir = Mathf.Sign(best.Shift - myOffset); }
            else if (movePhase == 1 && movedBack) movePhase = 2;

            bool owed = false;
            foreach (Candidate k in Candidates) if (k.Rejected == "H2" || k.Rejected == "H3") owed = true;
            Reason = best.Pedal == Pedal.Brake ? "braking for the car ahead"
                : best.Pedal == Pedal.Lift ? "lifting"
                : lapped && !zone ? "letting the leader by"
                : defending && !exempt && movePhase == 1 ? "defending move"
                : defending && !exempt && movePhase == 2 ? "defending move used"
                : fastBehind || defending && !exempt && zone ? "holding the line"
                : owed ? "leaving room"
                : Mathf.Abs(best.Shift) > 1f && passing ? "passing"
                : Mathf.Abs(best.Shift) > 1f && !float.IsPositiveInfinity(aheadInLane) ? "pulling out"
                : !float.IsPositiveInfinity(aheadInLane) ? "following"
                : null;
        }

        /// <summary>Runs one path forward, checking the hard limits at each step and scoring it.</summary>
        /// <summary>
        /// The speed a pedal holds the car to at step s: flat out, lifting to the speed of the car
        /// ahead in its lane (or holding its own speed with none), or braking below it for a second.
        /// </summary>
        /// <summary>The most this car may run at, gap metres behind a car at v that brakes at its hardest, stopping short of it.</summary>
        private float SafeSpeed(float v, float itsBrakes, float gap)
        {
            float b = Mathf.Max(myBrakes, 1f), bo = Mathf.Max(itsBrakes, b);
            return Mathf.Sqrt(Mathf.Max(2f * b * (Mathf.Max(gap, 0f) + v * v / (2f * bo)), 0f));
        }

        private float CapAt(Pedal pedal, int ahead, int s, float mySpeed, float d, float margin)
        {
            if (pedal == Pedal.Go) return float.PositiveInfinity;
            // T20: diving at a car on the outside, braking backs out, as hard as the brakes allow for a second (Ivan).
            if (pedal == Pedal.Brake && diving && s * Tick <= 1f) return Mathf.Clamp(diveSpeed - DiveDrop, Mathf.Max(mySpeed - 8f, 0f), mySpeed);
            // Braking with no car in its lane slows for one a little across, which a corner may bring into it.
            if (pedal == Pedal.Brake && ahead < 0 && WideAhead > 0f) ahead = aheadWide;
            // With no car ahead, braking drops back from a car alongside: to a little under its speed, as
            // hard as the brakes allow for a second, and never below what that takes.
            if (ahead < 0) return pedal == Pedal.Lift || s * Tick > 1f ? mySpeed
                : BesideBrake > 0f && (BesideBrake < 3f || besideSpeed < float.PositiveInfinity) ? Mathf.Clamp(besideSpeed - BesideDrop, Mathf.Max(mySpeed - 8f, 0f), mySpeed) : Mathf.Max(mySpeed - 8f, 0f);
            // Following: the speed of the car ahead, closing or opening the gap towards the one it keeps.
            // A queue then settles at the speed of its front instead of slowing a little more at every car.
            float v = ov[ahead, s];
            float gap = Road(od[ahead, s] - d, others.Count > 0 ? lapLength : 1f) - CarL;
            float keep = margin + (headway + FollowExtra) * Mathf.Max(v, 0f);
            // Too close, it drops back gently, as a gap a little short of the one it keeps is no danger:
            // a queue into a slow corner then doesn't fall further below the corner's speed at every car.
            // With the gap open it stays flat until it must slow, as late as moderate braking allows, so it
            // never feathers the throttle on the way up to the car ahead (Ivan).
            float follow = gap >= keep ? Mathf.Sqrt(v * v + 2f * CloseDecel * (gap - keep))
                : Mathf.Sqrt(Mathf.Max(v * v - 2f * OpenDecel * (keep - gap), 0f));
            // T3: within a couple of seconds of a car ahead, no faster than stops short of it if it brakes as hard as
            // its driver can, not only as hard as its line says (Ivan).
            if (HardAhead > 0f && gap < HardAhead * Mathf.Max(mySpeed, 1f))
                follow = Mathf.Min(follow, SafeSpeed(v, others[ahead].Braking, gap - margin));
            // Braking opens a short gap harder, by as much as the gap is short: a fixed drop below the car
            // ahead would add up along a queue until it stops.
            if (pedal == Pedal.Brake && s * Tick <= 1f)
                follow = BrakeOpen > 0f ? (gap >= keep ? follow : Mathf.Sqrt(Mathf.Max(v * v - 2f * BrakeOpen * (keep - gap), 0f)))
                    : Mathf.Min(follow, Mathf.Max(v - BrakeDrop, 0f));
            return follow;
        }

        private void Simulate(AiPilot pilot, TrafficCar me, Candidate cand, int ahead, int nextTurn, float margin,
            float brakes, float riskWeight, float passCost, float freeSpeed)
        {
            RacingLine line = pilot.Line;
            Circuit c = line.Circuit;
            SpeedProfile prof = pilot.Profile;
            float lap = c.Lap;
            float d = me.Distance, lat = me.Lateral, v = me.Speed;
            float holdOffset = me.Lateral - line.Lateral[c.IndexOf(Mathf.Repeat(d, lap))], off = holdOffset;
            float over = 0f, risk = 0f, yellow = 0f, lifted = 0f, below = 0f;
            bool freeRun = FreeStretch(pilot);
            float passClear = freeRun && GapWidth >= 0f ? CarW + GapWidth : Clear(margin);
            bool tuckZone = BrakingZone(pilot);   // H8, T6b: only braking for the corner
            // On the line, holding where it is means keeping to the line (T1).
            bool holdHere = (!HoldFree || FreeStretch(pilot)) && Mathf.Abs(pilot.Offset) > HoldOff;
            bool escapeZone = !tuckZone && TurnAt(c, me.Distance) == 0;   // H1: out of the corner
            // T20: the tow is for the straight, seconds clear of the next corner's braking.
            bool towZone = escapeZone && Road(NextCornerStart(c, Mathf.Repeat(me.Distance, lap)) - Mathf.Repeat(me.Distance, lap), lap) > TowNear * me.Speed;
            bool onTrack = OnTrack(c, me.Lateral, me.Distance);
            float turnIn = NextCornerStart(c, Mathf.Repeat(d, lap));
            bool turnedIn = false;
            // Where the checks against each car below stop reaching, behind and ahead (ahead, plus the margin and headway kept).
            float farBehind = -CarL - Mathf.Max(SwipeGap, 3f) - 0.01f, farAhead = CarL + 15f + 0.01f;
            cand.Path[0] = new Vector2(d, lat);
            // A path ruled out before it is driven breaks its limit at once: last in line when every path breaks one (T9).
            cand.BrokeAt = PreBreak && cand.Rejected != null ? 0 : Steps + 1;
            cand.Escaped = false;
            for (int s = 1; s <= Steps; s++)
            {
                int k = c.IndexOf(Mathf.Repeat(d, lap));
                // Paths keep to the lanes the pilot steers, which stay clear of the edge.
                float edge = Mathf.Max(c.HalfWidth(k) - RacingLine.EdgeMargin, 0f);
                // The pilot steers the line plus an offset, and the offset is what moves at the overtake rate.
                float wantOff = cand.Stay ? me.Lateral - line.Lateral[k] : s * Tick <= cand.Delay ? holdOffset : cand.Fixed ? cand.Lat - line.Lateral[k] : cand.Shift;
                // H1: a fixed place moves across the track at the overtake rate, whatever the line does under it (Ivan).
                if (HoldAbs && holdHere && !cand.Stay && !cand.Owe && !cand.Fixed && s * Tick <= cand.Delay) { lat = Mathf.Clamp(me.Lateral, -edge, edge); off = lat - line.Lateral[k]; }
                else if (cand.Fixed && FixedAbs) { lat = Mathf.Clamp(Mathf.MoveTowards(lat, cand.Lat, Endurance.OvertakeOffsetRate * Tick), -edge, edge); off = lat - line.Lateral[k]; }
                else
                {
                    off = Mathf.MoveTowards(off, wantOff, Endurance.OvertakeOffsetRate * Tick);
                    lat = Mathf.Clamp(line.Lateral[k] + off, -edge, edge);
                }
                if (cand.Owe && nextTurn != 0)
                {
                    // A car's width from the inside edge, measured from the edge and nothing else: once that is
                    // left, it just turns. A car diving deeper is that car's problem (Ivan, H2).
                    float ins = -nextTurn;
                    float bound = c.HalfWidth(k) - 1.5f * CarW - RoomMargin - 0.15f;
                    if (!OweTrack)
                        for (int j = 0; j < others.Count; j++)
                        {
                            float gj = Road(od[j, s] - d, lap);
                            if (stopped[j] || Mathf.Abs(gj) > CarL + 2f || ins * (ol[j, s] - lat) <= 0f) continue;
                            bound = Mathf.Min(bound, ins * ol[j, s] - (Mathf.Lerp(Clear(margin), CarW + RoomMargin, OweTight) + reach[j] - 0.5f * CarW + 0.1f));
                        }
                    if (OweEdge) bound = Mathf.Max(bound, -edge);   // never off the track's far side
                    if (ins * lat > bound) { lat = ins * bound; off = lat - line.Lateral[k]; }
                }
                if (s == 1) cand.SoonOff = off - holdOffset;
                // A fixed or held place across the track goes at that path's own speeds (H1, T20c).
                bool place = PlaceSpeeds && ((cand.Fixed && FixedAbs) || (HoldAbs && holdHere && !cand.Stay && !cand.Owe && s * Tick <= cand.Delay));
                float lane = place ? pilot.PlaceSpeed(k, lat) : pilot.LaneSpeed(k, lat - line.Lateral[k]);
                float cap;
                if (cand.Follow)
                {
                    // Flat out across, then following whichever car is ahead in the lane it is in by then (Ivan: no feathering on a pass).
                    int inLane = -1;
                    float near = float.PositiveInfinity;
                    for (int j = 0; j < others.Count; j++)
                    {
                        float gj = Road(od[j, s] - d, lap);
                        // In its way: as close across as H1 lets it pass a car ahead.
                        if (gj > 0f && gj < near && Mathf.Abs(ol[j, s] - lat) < passClear + reach[j] - 0.5f * CarW + 0.1f) { near = gj; inLane = j; }
                    }
                    if (s == 1) cand.FollowFirst = inLane;
                    cap = inLane < 0 ? float.PositiveInfinity : CapAt(Pedal.Lift, inLane, s, me.Speed, d, margin);
                }
                else cap = CapAt(cand.Pedal, ahead, s, me.Speed, d, margin);
                float limit = Mathf.Min(lane, cap);
                // Off the throttle for a car ahead, short of what its lane allows.
                if (cap < lane - 1f && v >= cap - 0.5f) lifted += Tick;
                // On the free stretch, every second under the path's own flat-out speed is time given away (Ivan).
                if (freeRun && cap < lane - 1f) below += Mathf.Max(lane - v, 0f) / Mathf.Max(lane, 1f) * Tick;
                if (v < limit)
                {
                    float resist = prof.Resist(k, v), drag = resist - prof.Resist(k, 0f);
                    float a = prof.DriveAccel(v) - resist + (1f - Wake(s - 1, d, lat, lap)) * drag;
                    v = Mathf.Min(limit, v + a * Tick);
                }
                else
                {
                    // Slowing: as hard as the brakes allow, or as the lane's own speeds fall if they
                    // ask more, which the car can do there by construction.
                    int k2 = c.Wrap(k + 5);
                    float lane2 = pilot.LaneSpeed(k2, lat - line.Lateral[k2]);
                    float slope = Mathf.Max(lane * lane - lane2 * lane2, 0f) / (2f * 5f * c.Step);
                    v = Mathf.Max(limit, v - Mathf.Max(brakes, 1.05f * slope) * Tick);
                }
                // H7: into a corner faster than the lane it is in can take, with the brakes it has.
                if (v > lane + 2f && TurnAt(c, Mathf.Repeat(d, lap)) != 0) over += Tick;
                float was = d;
                d += v * Tick;
                if (s % DrawEvery == 0) cand.Path[s / DrawEvery] = new Vector2(d, lat);

                string broke = null;
                int by = -1;
                bool brokeBehind = false;
                // H10, with a metre in hand for running wide; a car already off the track is rejoining, and may.
                if (onTrack && Mathf.Abs(lat) - 0.5f * CarW > c.HalfWidth(k) - 1f) broke = "H10";
                if (over > 0.3f) broke = "H7";
                bool crossing = !turnedIn && Road(turnIn - Mathf.Repeat(was, lap), lap) > 0f && Road(turnIn - Mathf.Repeat(d, lap), lap) <= 0f;
                if (crossing) turnedIn = true;
                for (int j = 0; j < others.Count && broke == null; j++)
                {
                    float g = Road(od[j, s] - d, lap);
                    // A stopped ghost with no way round: slowed to a crawl, this car may pass through it (H12's safety net).
                    if (stopped[j] && v < GhostCrawl) continue;
                    // Too far behind for any limit or risk below to reach it: the same result, sooner.
                    if (g <= farBehind) continue;
                    float side = ol[j, s] - lat;
                    float apart = Mathf.Abs(side);
                    // Too far ahead for any limit below: only the risk of closing on it in its lane is left.
                    if (g >= farAhead + margin + headway * v && !stopped[j])
                    {
                        float closingFar = v - ov[j, s];
                        if (apart < CarW + RoomMargin && closingFar > 0.5f) risk += CloseRisk * Mathf.Exp(-Mathf.Max(g - CarL, 0f) / closingFar) * Tick;
                        continue;
                    }
                    // H1: never into a car ahead or alongside, with a margin kept: a passing margin past a
                    // car ahead, a little less beside one. A car already closer than that must not get closer.
                    float need = (g > 0.7f * CarL ? passClear : CarW + AlongsideGap) + reach[j] - 0.5f * CarW;
                    float start = Mathf.Abs(ol[j, 0] - me.Lateral);
                    float from = Road(od[j, 0] - me.Distance, lap);
                    // Nose to tail the margin grows with speed: room for the car ahead to brake harder than expected.
                    float buffer = margin + headway * v;
                    // Off its tail, with no overlap across, a car ahead braking hard is no danger: only the margin is kept, not the headway.
                    if (SideBuffer && apart > CarW + 0.5f && apart >= Mathf.Abs(ol[j, 0] - me.Lateral) - 0.05f) buffer = margin;
                    bool touching = from > -0.3f * CarL && from < CarL + margin + headway * me.Speed + (touchNow > 0f ? TouchSlack : 0f) && start < need;
                    // A car coming from behind is its own to miss: this car only must not move across into it.
                    bool behind = from < -0.3f * CarL;
                    bool into = !behind || apart < Mathf.Abs(ol[j, s] - me.Lateral) - 0.05f;
                    // A car alongside on the inside is owed its width from the inside edge and no more: a path that
                    // leaves that is clear of it, and if it dives deeper still that is its own to miss (Ivan, H2).
                    bool leftRoom = false;
                    if (OweH1 && nextTurn != 0 && !stopped[j] && Mathf.Abs(g) < CarL && Mathf.Sign(side) == -nextTurn)
                    {
                        float owedRoom = side > 0f ? c.HalfWidth(k) - (lat + 0.5f * CarW) : lat - 0.5f * CarW + c.HalfWidth(k);
                        leftRoom = owedRoom >= CarW + RoomMargin && apart > CarW;
                    }
                    // H8, T6b: tucking in behind a car ahead is no closer to it than now, which is all a car already
                    // inside the following gap can keep: it rejoins the line behind rather than holding a lane beside (Ivan).
                    bool tuck = Tuck > 0f && tuckZone && g > CarL + margin + Tuck * v && g >= from - 0.1f
                        && (HardAhead <= 0f || v <= SafeSpeed(ov[j, s], others[j].Braking, g - CarL - margin));
                    // H1: a car ahead already within the following gap is left by moving across, away from its lane:
                    // that path is no worse for being inside the gap, so long as it never closes to the car's tail.
                    // T20: in the tow on a straight it closes right up, as close as the car ahead's hardest braking allows (Ivan: slipstream, then pull out).
                    // T20a, T20c: braking for the corner behind a car already inside the following gap, a path on the
                    // brakes in its own lane that gets no closer is always open: never every path broken, and no swerve or
                    // flicker off the brakes (Ivan).
                    if (!tuck && BrakeLegal && tuckZone && cand.Pedal == Pedal.Brake && !cand.Fixed && Mathf.Abs(cand.Shift - holdOffset) < 0.5f && from > 0.7f * CarL && g > CarL + 0.5f * margin && g >= from - 0.1f
                        && (HardAhead <= 0f || v <= SafeSpeed(ov[j, s], others[j].Braking, g - CarL - 0.5f * margin))) tuck = true;
                    if (!tuck && Tow > 0f && towZone && from > 0.7f * CarL && g > CarL + margin + Tow * v
                        && v <= SafeSpeed(ov[j, s], others[j].Braking, g - CarL - margin)) tuck = true;
                    bool escape = Escape > 0f && escapeZone && from > 0.7f * CarL && apart > start + 0.05f && g > CarL + margin + Escape * headway * v;
                    bool h1 = !leftRoom && !tuck && g > (behind ? -CarL - SwipeGap : -0.3f * CarL) && g < CarL + (g > 0f ? buffer : 0f) && apart < need && into && (!touching || apart < Mathf.Min(start - 0.1f, need) || apart < CarW + 0.05f && (touchNow <= 0f || g < from - touchNow));
                    if (h1 && escape) cand.Escaped = true;
                    if (h1 && !escape) { broke = "H1"; by = others[j].Id; brokeBehind = behind; }
                    else if (!stopped[j] && Mathf.Abs(g) < CarL && OnTrack(c, ol[j, s], od[j, s]))
                    {
                        // H2 and H3: room for a car alongside, on the inside always, on the outside once its front is level or ahead.
                        bool inside = nextTurn != 0 ? Mathf.Sign(side) == -nextTurn : true;
                        bool owed = inside || g >= 0f;
                        float room = side > 0f ? c.HalfWidth(k) - (lat + 0.5f * CarW) : lat - 0.5f * CarW + c.HalfWidth(k);
                        if (owed && room < CarW + RoomMargin) broke = inside ? "H2" : "H3";
                    }
                    // H8: at turn-in, overlapped or backed out, never alongside short of it.
                    if (broke == null && crossing && g > 0.9f * CarL && g < CarL + 15f && apart > 1.2f && apart < 4f) broke = "H8";
                    // Risk: how close the path runs to each car.
                    // A car alongside on its inside into a corner is owed a car's width (H2) and no more:
                    // running close to it there is no risk to weigh, or the car would back off and wave it by.
                    bool owedInside = nextTurn != 0 && !stopped[j] && Mathf.Abs(g) < CarL && Mathf.Sign(side) == -nextTurn;
                    if (!owedInside && Mathf.Abs(g) < CarL + 3f && apart - CarW < 2f) risk += (g < -BehindFrom * CarL ? BehindRisk : 1f) * SideRisk * Mathf.Exp(-(apart - CarW) / 0.5f) * Tick;
                    // Closing on a car in its lane: the sooner it would reach it, the riskier. Following at its speed is not.
                    float closing = v - ov[j, s];
                    if (g > 0f && apart < CarW + RoomMargin && closing > 0.5f) risk += CloseRisk * Mathf.Exp(-Mathf.Max(g - CarL, 0f) / closing) * Tick;
                    // A stopped car ahead: slow as under a yellow flag.
                    if (stopped[j] && g > -CarL && g < YellowReach) yellow += Mathf.Max(v / Mathf.Max(lane, 1f) - YellowShare, 0f) * Tick;
                }
                if (broke != null && cand.Rejected == null) { cand.Rejected = broke; cand.BrokeAt = s; cand.BrokeBy = by; cand.BrokeBehind = brokeBehind; }
                else if (broke != null && cand.BrokeAt > Steps) cand.BrokeAt = s;
            }

            // Time: ground covered, in seconds at the car's own speed here.
            int k0 = c.IndexOf(Mathf.Repeat(me.Distance, lap));
            float vRef = Mathf.Max(prof.Speed[k0], 10f);
            float score = (d - me.Distance) / vRef;
            // The lane it ends in, over the next stretch, against the racing line.
            int kEnd = c.IndexOf(Mathf.Repeat(d, lap));
            float offEnd = lat - line.Lateral[kEnd];
            cand.EndOff = offEnd;
            // It goes back to the line from there, unless a car beside it on the line's side keeps it out.
            bool heldOut = false;
            for (int j = 0; j < others.Count && !heldOut; j++)
            {
                float g = Road(od[j, Steps] - d, lap);
                float toCar = ol[j, Steps] - lat;
                heldOut = !stopped[j] && Mathf.Abs(g) < 2f * CarL && Mathf.Sign(toCar) == -Mathf.Sign(offEnd) && Mathf.Abs(toCar) < Mathf.Abs(offEnd) + CarW;
            }
            float back = heldOut ? 0f : Endurance.OvertakeOffsetRate / Mathf.Max(v, 10f);
            // Up to the same point for every path, so one that gets further isn't charged for more of the corner.
            float tail = me.Distance + Mathf.Max(me.Speed, 10f) * Steps * Tick + TailLook - d;
            for (float x = 0f; x < tail; x += 10f)
            {
                int k = c.IndexOf(Mathf.Repeat(d + x, lap));
                float tailOff = Mathf.Sign(offEnd) * Mathf.Max(Mathf.Abs(offEnd) - back * x, 0f);
                if (tailOff == 0f) break;
                score -= 0.5f * 10f * (1f / Mathf.Max(pilot.LaneSpeed(k, tailOff), 1f) - 1f / Mathf.Max(pilot.LaneSpeed(k, 0f), 1f));
            }
            // Ending behind a slower car, in its lane or beside it but not yet by, with too little of
            // the straight left to get by before braking: the time lost staying there until the next
            // place to pass. Ending ahead of a car that was ahead: a pass, which must gain enough to be worth it.
            float toBrake = ToBraking(pilot, Mathf.Repeat(d, lap));
            // H14, T4, T8: off the line is free between corners, but not being back in place by the next
            // braking zone is not. What it can't get back across in the road left costs (Ivan).
            cand.Unplaced = 0f;
            if (InPlace > 0f && FreeStretch(pilot))
            {
                // From here, not from the path's end, which may already be past it.
                float room = Mathf.Max(ToBraking(pilot, Mathf.Repeat(me.Distance, lap)) - (d - me.Distance) - InPlaceLead * Mathf.Max(v, 10f), 0f);
                cand.Unplaced = Mathf.Max(Mathf.Abs(offEnd) - Endurance.OvertakeOffsetRate / Mathf.Max(v, 10f) * room, 0f);
                score -= InPlace * cand.Unplaced;
            }
            float stuck = 0f;
            for (int j = 0; j < others.Count; j++)
            {
                float g = Road(od[j, Steps] - d, lap);
                // Judged from where the car was at the start, so dropping back from a slow car doesn't hide it.
                float from = Road(od[j, 0] - me.Distance, lap);
                if (!stopped[j] && g > 0f && from > 0f && from < 2f * Mathf.Max(me.Speed, 10f) && ov[j, Steps] < freeSpeed - 1f)
                {
                    float closing = Mathf.Max(v - ov[j, Steps], 0.1f);
                    float needs = (g + CarL + 1f) / closing;
                    // Held to its speed until it gets by: as long as the pass takes from here, or until the
                    // next place to pass if this straight runs out first. A car sitting at the speed of the
                    // one ahead never gets by, so matching its speed costs the most. The slowest car it is
                    // stuck behind is the one that holds it up.
                    float held = needs > toBrake / Mathf.Max(v, 1f) ? 2f * WaitToPass : Mathf.Min(needs, 2f * WaitToPass);
                    bool inItsLane = Mathf.Abs(ol[j, Steps] - lat) < passClear + reach[j] - 0.5f * CarW;
                    if (inItsLane) cand.EndsBehind = true;
                    if (inItsLane && needs < toBrake / Mathf.Max(v, 1f)) cand.EndsCaught = true;
                    stuck = Mathf.Max(stuck, (1f - ov[j, Steps] / Mathf.Max(freeSpeed, 1f)) * held * StuckScale);
                }
                if (!stopped[j] && Road(od[j, 0] - me.Distance, lap) > 0f && g < 0f) score -= passCost;
            }
            cand.Gain = score; cand.Risk = riskWeight * risk; cand.Stuck = stuck;
            // Lifting to sit behind a car is the last thing to do, not a way to wait (Ivan): a pass that
            // keeps its foot in beats it unless it really is slower.
            cand.Below = below;
            cand.Score = score - stuck - riskWeight * risk - YellowWeight * yellow - LiftCost * lifted - HeldCost * below;
        }

        /// <summary>Share of its drag a car at (d, lat) feels at step s, in the wake of the cars ahead.</summary>
        private float Wake(int s, float d, float lat, float lap)
        {
            float scale = 1f;
            for (int j = 0; j < others.Count; j++)
            {
                float g = Road(od[j, s] - d, lap);
                float side = Mathf.Abs(ol[j, s] - lat);
                if (g < 4.8f || g > 80f || side > 2f) continue;
                scale = Mathf.Min(scale, 1f - 0.45f * (1f - g / 80f) * (1f - side / 2f));
            }
            return scale;
        }

        /// <summary>The cars within reach: 50 m behind to 200 m ahead, ghosts left out unless this car is one.</summary>
        private void Gather(Circuit c, TrafficCar me, IReadOnlyList<TrafficCar> field)
        {
            others.Clear();
            reach.Clear();
            stopped.Clear();
            float lap = c.Lap;
            for (int i = 0; i < field.Count; i++)
            {
                TrafficCar o = field[i];
                if (o.Id == me.Id) continue;
                // A ghost is left out, unless it is stopped on the track: then it is an obstacle.
                bool still = o.Ghost && !me.Ghost && Mathf.Abs(o.Speed) < StoppedSpeed
                    && Mathf.Abs(o.Lateral) < c.HalfWidth(c.IndexOf(Mathf.Repeat(o.Distance, lap)));
                // A ghost getting back into the race is a car to plan round too, once near the track: the
                // ghosting is only the safety net (Ivan, T17).
                // T17: seen from when it heads back towards the track, not only once it is near it (Ivan).
                float outBy = Mathf.Abs(o.Lateral) - c.HalfWidth(c.IndexOf(Mathf.Repeat(o.Distance, lap)));
                bool heading = o.Across * Mathf.Sign(o.Lateral) < -0.1f && outBy < GhostSee;
                bool rejoining = AvoidGhosts && o.Ghost && !me.Ghost && (outBy < GhostNear || heading);
                if (o.Ghost && !me.Ghost && !still && !rejoining) continue;
                float g = Road(o.Distance - me.Distance, lap);
                if (g <= -50f || g >= 200f) continue;
                others.Add(o);
                Vector3 f = c.Forward[c.IndexOf(Mathf.Repeat(o.Distance, lap))];
                float side = Mathf.Abs(Mathf.Sin(o.Yaw - Mathf.Atan2(f.z, f.x)));
                reach.Add(0.5f * (CarL * side + CarW * Mathf.Sqrt(Mathf.Max(1f - side * side, 0f))));
                stopped.Add(still);
            }
        }

        /// <summary>
        /// Where each car near will be at each step: at the speeds its own line gives, slowing for a
        /// car ahead of it in its lane, and drifting across as it is now for a second.
        /// </summary>
        private void Predict(Circuit c, TrafficCar me, int nextTurn)
        {
            int n = others.Count;
            if (od.GetLength(0) < n) { od = new float[n, Steps + 1]; ol = new float[n, Steps + 1]; ov = new float[n, Steps + 1]; }
            float lap = c.Lap;
            order.Clear();
            for (int j = 0; j < n; j++)
            {
                od[j, 0] = others[j].Distance;
                ol[j, 0] = others[j].Lateral;
                ov[j, 0] = others[j].Speed;
                order.Add(j);
            }
            order.Sort((a, b) => others[b].Distance.CompareTo(others[a].Distance));
            for (int s = 1; s <= Steps; s++)
            {
                float t = s * Tick;
                for (int oi = 0; oi < n; oi++)
                {
                    int j = order[oi];
                    TrafficCar o = others[j];
                    float v = ov[j, s - 1];
                    if (stopped[j])
                    {
                        od[j, s] = od[j, s - 1];
                        ol[j, s] = ol[j, s - 1];
                        ov[j, s] = 0f;
                        continue;
                    }
                    int k = c.IndexOf(Mathf.Repeat(od[j, s - 1], lap));
                    float limit = o.Profile != null ? o.Profile.Speed[k] : v;
                    // A car whose driver says what it is doing: held to its own cap for a second, heading for its own aim.
                    if (o.Planned && t <= CapTrust) limit = Mathf.Min(limit, o.Cap);
                    float up = o.Profile != null ? Mathf.Max(o.Profile.DriveAccel(v) - o.Profile.Resist(k, v), 0f) : 3f;
                    v = v > limit ? Mathf.Max(limit, v - Mathf.Max(o.Braking, 5f) * Tick) : Mathf.Min(limit, v + up * Tick);
                    float lat;
                    if (o.Planned && o.Profile != null)
                    {
                        // Its offset from its own line moves at the overtake rate, as its pilot steers.
                        float edge = Mathf.Max(c.HalfWidth(k) - RacingLine.EdgeMargin, 0f);
                        float was = ol[j, s - 1] - o.Profile.Line.Lateral[k];
                        lat = Mathf.Clamp(o.Profile.Line.Lateral[k] + Mathf.MoveTowards(was, o.Aim, Endurance.OvertakeOffsetRate * Tick), -edge, edge);
                        // A car alongside doesn't close in on this one: its own limits keep it off, as this car's keep it off that one.
                        float g0 = Road(od[j, 0] - me.Distance, lap);
                        // T20, H7: into a corner, a car ahead on this one's outside keeps off only as far as the lane leaving a
                        // car its width from the inside edge. Short of that slot, this car is diving at it, and must back out (Ivan).
                        bool outside = DivePredict && nextTurn != 0 && g0 > DiveFrom * CarL && (ol[j, 0] - me.Lateral) * nextTurn > CarW;
                        float owedLane = c.HalfWidth(k) - 1.5f * CarW - RoomMargin - 0.15f;
                        if (outside && g0 < CarL + 2f) lat = -nextTurn * Mathf.Min(-nextTurn * lat, Mathf.Max(owedLane, -nextTurn * ol[j, s - 1]));
                        else if (Mathf.Abs(g0) < CarL + 2f && Mathf.Abs(lat - me.Lateral) < Mathf.Abs(ol[j, s - 1] - me.Lateral)) lat = ol[j, s - 1];
                    }
                    else lat = o.Lateral + o.Across * Mathf.Min(t, 1f);
                    float d = od[j, s - 1] + v * Tick;
                    // A car ahead of it in its lane holds it back.
                    for (int fi = 0; fi < oi; fi++)
                    {
                        int f = order[fi];
                        float g = Road(od[f, s] - d, lap);
                        if (g > -0.5f * CarL && g < CarL + 1f && Mathf.Abs(ol[f, s] - lat) < CarW)
                        {
                            v = Mathf.Min(v, ov[f, s]);
                            d = Mathf.Min(d, od[j, s - 1] + v * Tick);
                        }
                    }
                    od[j, s] = d;
                    ol[j, s] = lat;
                    ov[j, s] = v;
                }
            }
        }

        /// <summary>How far apart across the track two cars' middles stay when one passes the other, m: a car's width and a margin.</summary>
        /// <summary>The change of lane the hysteresis costs, m: with <see cref="ToLine"/>, a move back towards the line costs that share less.</summary>
        private static float LineChange(float shift, float last)
        {
            float change = Mathf.Abs(shift - last);
            bool back = Mathf.Abs(shift) < Mathf.Abs(last) && (Mathf.Abs(shift) < 0.5f || Mathf.Sign(shift) == Mathf.Sign(last));
            return back ? change * (1f - ToLine) : change;
        }

        public static float Clear(float margin) => CarW + 0.6f * Endurance.WidthMargin * margin;

        private static bool OnTrack(Circuit c, float lat, float d) => Mathf.Abs(lat) - 0.5f * CarW <= c.HalfWidth(c.IndexOf(Mathf.Repeat(d, c.Lap)));

        /// <summary>A gap in race distance as the road between two cars, a lap either way folded in.</summary>
        private static float Road(float gap, float lap) => Mathf.Repeat(gap + lap * 0.5f, lap) - lap * 0.5f;

        /// <summary>The bend at a lap distance: +1 a right-hander, -1 a left-hander, 0 on a straight.</summary>
        private static int TurnAt(Circuit c, float here)
        {
            foreach (CornerData k in c.Data.corners)
                foreach (ArcData arc in k.arcs)
                    if (c.Ahead(arc.s, here) <= arc.e - arc.s) return arc.dir;
            return 0;
        }

        /// <summary>The way the next bend within 300 m turns, +1 right, the one the car is in included, or 0 for none.</summary>
        public static int NextTurnAt(Circuit c, float here) => NextTurn(c, here);

        /// <summary>The way the next bend within 300 m turns, the one the car is in included, or 0 for none.</summary>
        private static int NextTurn(Circuit c, float here, float within = 300f)
        {
            int now = TurnAt(c, here);
            if (now != 0) return now;
            float best = within;
            int dir = 0;
            foreach (CornerData k in c.Data.corners)
            {
                float ahead = c.Ahead(here, k.s);
                if (ahead < best) { best = ahead; dir = k.arcs[0].dir; }
            }
            return dir;
        }

        /// <summary>Lap distance where the next corner starts.</summary>
        private static float NextCornerStart(Circuit c, float here)
        {
            float best = float.MaxValue, at = here;
            foreach (CornerData k in c.Data.corners)
            {
                float ahead = c.Ahead(here, k.s);
                if (ahead < best) { best = ahead; at = k.s; }
            }
            return at;
        }

        /// <summary>Metres from a lap distance to where the car's speed profile next starts to fall for a corner.</summary>
        private static float ToBraking(AiPilot pilot, float at)
        {
            SpeedProfile p = pilot.Profile;
            Circuit c = pilot.Line.Circuit;
            int k = c.IndexOf(at);
            int look = Mathf.RoundToInt(20f / c.Step);
            for (int i = 0; i < c.Count; i += 5)
            {
                int j = c.Wrap(k + i);
                if (p.Speed[c.Wrap(j + look)] < p.Speed[j] - 1f) return i * c.Step;
            }
            return c.Lap;
        }

        /// <summary>
        /// From the corner's exit to the next braking zone: the racing line is only the default there, with
        /// nothing against changing its mind (Ivan). Past the slowest point of the corner, speeds rising, not braking.
        /// </summary>
        /// <summary>Whether the car is on the free stretch, from a corner's exit to the next braking zone.</summary>
        public static bool FreeStretchAt(AiPilot pilot) => FreeStretch(pilot);
        private static bool FreeStretch(AiPilot pilot)
        {
            if (!Free || BrakingZone(pilot)) return false;
            SpeedProfile p = pilot.Profile;
            Circuit c = pilot.Line.Circuit;
            int k = pilot.Index;
            return TurnAt(c, k * c.Step) == 0 || p.Speed[c.Wrap(k + Mathf.RoundToInt(10f / c.Step))] > p.Speed[k];
        }
        private static readonly bool Free = Knob("FREE", 1f) > 0f;
        private static readonly float FreeOff = Knob("FREEOFF", 0f);
        private static readonly bool BrakeLegal = Knob("BRAKELEGAL", 1f) > 0f;
        private static readonly bool EndWait = Knob("ENDWAIT", 1f) > 0f;
        /// <summary>Cost a metre still off the line at the next braking zone, and how early it must be back, s.</summary>
        private static readonly float InPlace = Knob("INPLACE", 0.1f), InPlaceLead = Knob("INLEAD", 0.3f);
        private static readonly bool NoWait = Knob("NOWAIT", 1f) > 0f;
        /// <summary>H1: a fixed place is reached across the track at the overtake rate, not the line's offset.</summary>
        public static readonly bool FixedAbs = Knob("FIXEDABS", 1f) > 0f;
        /// <summary>T20c: a path that moves later holds a fixed place across the track on the free stretch, once off the line.</summary>
        public static readonly bool HoldAbs = Knob("HOLDABS", 1f) > 0f;
        private static readonly float HoldOff = Knob("HOLDOFF", 0.5f);
        private static readonly bool HoldFree = Knob("HOLDFREE", 1f) > 0f;
        /// <summary>A fixed or held place goes at the speeds of its own path, which bends with the track.</summary>
        public static readonly bool PlaceSpeeds = Knob("PLACESPEED", 1f) > 0f;
        private static readonly bool holdKnobs = SetHold();
        private static bool SetHold() { AiPilot.HoldBend = Knob("HOLDBEND", 1f); AiPilot.HoldHead = Knob("HOLDHEAD", 1f); AiPilot.PlaceSpeeds = PlaceSpeeds; AiPilot.LagSpeeds = Knob("LAGSPEED", 1f) > 0f; return true; }
        /// <summary>The fixed place across the track the chosen path holds, when it holds one.</summary>
        public bool FixedOn => fixedOn;
        public float FixedLat => fixedLat;

        /// <summary>True in the braking zone: the car's speed profile falls over the next 20 m.</summary>
        private static bool BrakingZone(AiPilot pilot)
        {
            SpeedProfile p = pilot.Profile;
            Circuit c = pilot.Line.Circuit;
            int k = pilot.Index;
            return p.Speed[c.Wrap(k + Mathf.RoundToInt(20f / c.Step))] < p.Speed[k] - 1f;
        }
    }
}
