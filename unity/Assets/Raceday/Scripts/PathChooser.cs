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
        /// <summary>Gap to a car alongside, side to side, inside which TORCS's parallel steering still takes over, m (spec: 2 m).</summary>
        public const float SideFilterGap = 2f;
        /// <summary>Seconds of score per second spent lifting or braking for a car ahead below its lane's speed.</summary>
        private static readonly float LiftCost = Knob("LIFT", 0.1f);
        /// <summary>Deceleration, m/s², braking plans to open a short gap with.</summary>
        /// <summary>Inside the margin behind a car, how much closer a path may not get, m.</summary>
        private static readonly float TouchClose = Knob("TOUCH", 0f);
        private static readonly float BrakeOpen = Knob("BOPEN", 12f), BesideBrake = Knob("BESIDE", 2f), BesideDrop = Knob("BDROP", 6f);
        /// <summary>The slowest car alongside this plan, m/s.</summary>
        private float besideSpeed = float.PositiveInfinity;
        /// <summary>The nearest car ahead not quite in this car's lane, which braking slows for when none is.</summary>
        private int aheadWide = -1;
        private static readonly float FallBehind = Knob("FALLBEHIND", 0f), WideAhead = Knob("WIDE", 3f), ZoneSwitch = Knob("ZSWITCH", 0f), SideSwitch = Knob("SSWITCH", 0.3f), ZoneHyst = Knob("ZHYST", 0f);
        /// <summary>Share of the side risk a car further back than <see cref="BehindFrom"/> car lengths carries: one behind is its own to miss.</summary>
        private static readonly float BehindRisk = Knob("BRISK", 0f), BehindFrom = Knob("BFROM", 0.5f), ToLine = Knob("TOLINE", 1f), OffLine = Knob("OFFLINE", OffLineCost);
        /// <summary>How far the room left to a car alongside on the inside goes from the passing margin to the room owed (H2: no more than owed, Ivan).</summary>
        private static readonly float OweTight = Knob("OWETIGHT", 0.7f);
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
            public float Shift;
            public Pedal Pedal;
            /// <summary>Seconds it stays where it is across the track before it moves: staying in the tow until the last safe moment.</summary>
            public float Delay;
            /// <summary>Keeps where it is across the track, whatever the racing line does.</summary>
            public bool Stay;
            /// <summary>Takes its line, but no nearer the inside than leaves a car alongside there exactly the width it is owed (H2).</summary>
            public bool Owe;
            /// <summary>How far its offset from the racing line moves in the first step: where an owing path steers now.</summary>
            public float SoonOff;
            public string Rejected;
            public float Score;
            /// <summary>What the score is made of, in seconds: ground covered, closeness to cars, time stuck behind one.</summary>
            public float Gain, Risk, Stuck;
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

        // The cars near this one, and where they are predicted to be at each step.
        private readonly List<TrafficCar> others = new List<TrafficCar>();
        /// <summary>For each car near: how far its body reaches across the track either side of its middle, m, and whether it is a stopped car.</summary>
        private readonly List<float> reach = new List<float>();
        private readonly List<bool> stopped = new List<bool>();
        private float[,] od = new float[0, 0], ol = new float[0, 0], ov = new float[0, 0];
        private readonly List<int> order = new List<int>();
        private bool placed;

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
            if (clock < every - 1e-4f) return false;
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
            Predict(c, me);

            // What the cars around ask of this one, from where they are now.
            float mySpeed = me.Speed;
            bool lapOne = me.Distance < lap;
            bool defending = false, fastBehind = false, lapped = false, passing = false;
            float lappingLateral = 0f, attackerGap = float.NegativeInfinity, attackerLateral = 0f;
            float aheadInLane = float.PositiveInfinity;
            int ahead = -1;
            float aheadGap = 60f;
            besideSpeed = float.PositiveInfinity;
            aheadWide = -1;
            float wideGap = 60f;
            float freeSpeed = Mathf.Max(mySpeed, pilot.FreeSpeed);
            for (int j = 0; j < others.Count; j++)
            {
                TrafficCar o = others[j];
                float g = Road(o.Distance - me.Distance, lap);
                bool lapping = o.Distance - me.Distance > lap * 0.5f;
                if (g < -CarL && -g < 2f * Mathf.Max(mySpeed, 10f) && lapping) { lapped = true; lappingLateral = o.Lateral; }
                if (g < -CarL && !lapping && -g < mySpeed * 1f && o.Speed > mySpeed - 5f && (!defending || g > attackerGap)) { defending = true; attackerGap = g; attackerLateral = o.Lateral; }
                float closing = o.Speed - mySpeed;
                if (g < -CarL && !lapping && closing > Endurance.NoDefendClosing && -g < closing * 2f) fastBehind = true;
                if (g > CarL && g < mySpeed * 1f && o.Speed < freeSpeed) passing = true;
                if (g > 0f && g < aheadGap && Mathf.Abs(o.Lateral - me.Lateral) < CarW + RoomMargin && !(stopped[j] && mySpeed < GhostCrawl + 1f)) { aheadGap = g; ahead = j; aheadInLane = o.Speed; }
                if (g > CarL && g < wideGap && Mathf.Abs(o.Lateral - me.Lateral) < WideAhead * CarW) { wideGap = g; aheadWide = j; }
                // A car alongside and level or ahead: one this car drops back from. One behind drops back itself.
                if (g > (BesideBrake >= 2f ? 0f : -CarL - 1f) && g < CarL + 1f && Mathf.Abs(o.Lateral - me.Lateral) < 2f * CarW) besideSpeed = Mathf.Min(besideSpeed, o.Speed);
            }
            bool exempt = defending && passing;
            bool zone = TurnAt(c, here) != 0 || BrakingZone(pilot);
            headway = HeadZone >= 0f && (lapOne || zone || Road(NextCornerStart(c, here) - here, lap) < HeadNear * mySpeed) ? HeadZone : Headway;
            int nextTurn = NextTurn(c, here);
            float myOffset = me.Lateral - line.Lateral[pilot.Index];
            float margin = Endurance.MarginScale(pilot.Safety);
            float riskWeight = Mathf.Lerp(0.05f, 0.4f, pilot.Safety / 100f) * (lapOne ? 2f : 1f) * RiskScale;
            float passCost = Mathf.Lerp(0.02f, 0.15f, pilot.Safety / 100f) * PassScale;
            // What it can slow at: the brakes, or the tyres with the downforce if they give out first.
            float brakes = Mathf.Min(pilot.Spec.BrakeDecel * G, pilot.TyreGrip(me.Speed)) * AiPilot.LimitFor(pilot.Pace);

            Candidates.Clear();
            float[] shifts = pilot.Gliding ? FarShifts : NearShifts;
            int pedals = pilot.Gliding ? 2 : 3;
            // A car alongside on the inside into the corner: also the line that leaves it exactly its width.
            bool owedNear = false;
            if (nextTurn != 0)
                foreach (TrafficCar o in others)
                    if (Mathf.Abs(Road(o.Distance - me.Distance, lap)) < 2f * CarL && Mathf.Sign(o.Lateral - me.Lateral) == -nextTurn && Mathf.Abs(o.Speed) >= StoppedSpeed) owedNear = true;
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
                    else if (fastBehind && away) cand.Rejected = "H6";
                    else if (defending && !exempt && zone && blocks) cand.Rejected = "H5";
                    else if (defending && !exempt && !zone && away && (movePhase == 2 || movePhase == 1 && Mathf.Sign(cand.Shift - myOffset) != moveDir)) cand.Rejected = "H4";
                    Simulate(pilot, me, cand, ahead, nextTurn, margin, brakes, riskWeight, passCost, freeSpeed);
                    // A little against changing its mind, so it doesn't dither between two near-equal paths.
                    cand.Score -= (zone ? Hysteresis + ZoneHyst : Hysteresis) * LineChange(cand.Shift, lastShift) + (cand.Pedal != lastPedal ? 0.01f : 0f) + OffLine * Mathf.Abs(cand.Shift);
                    // Changing sides of the car ahead mid-move weaves across the cars behind: only for a clearly better path.
                    if (Mathf.Abs(cand.Shift) > 1f && Mathf.Abs(lastShift) > 1f && Mathf.Sign(cand.Shift) != Mathf.Sign(lastShift)) cand.Score -= SideSwitch;
                    // Once braking for a car in the braking zone, it stays on the brakes rather than flick back on the throttle.
                    if (zone && lastPedal == Pedal.Brake && cand.Pedal == Pedal.Go) cand.Score -= ZoneSwitch;
                    Candidates.Add(cand);
                    // The same move later, flat out: in the tow until then.
                    if (s < 0 || cand.Pedal != Pedal.Go || pilot.Gliding || Mathf.Abs(cand.Shift - myOffset) < 0.5f) continue;
                    foreach (float wait in Delays)
                    {
                        var later = new Candidate { Shift = cand.Shift, Pedal = Pedal.Go, Delay = wait };
                        if (lapped && !zone && Mathf.Abs(later.Shift) < 1.5f) later.Rejected = "H9";
                        else later.Rejected = cand.Rejected;
                        Simulate(pilot, me, later, ahead, nextTurn, margin, brakes, riskWeight, passCost, freeSpeed);
                        later.Score -= (zone ? Hysteresis + ZoneHyst : Hysteresis) * LineChange(later.Shift, lastShift) + OffLine * Mathf.Abs(later.Shift);
                        Candidates.Add(later);
                    }
                    // In the tow on a straight, of near-equal moves the later one wins, while a still later one
                    // is open too: the car moves out at its last safe moment with a step in hand, as it lags its plan.
                    if (!defending && !zone)
                        for (int i = Candidates.Count - Delays.Length; i < Candidates.Count - 1; i++)
                            if (Candidates[i + 1].Rejected == null) Candidates[i].Score += LateBonus * Candidates[i].Delay;
                }

            Candidate best = null;
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
            Target = best.Owe ? pilot.Offset + best.SoonOff : best.Delay > 0f ? pilot.Offset : best.Shift;
            Cap = CapAt(best.Pedal, ahead, 0, mySpeed, me.Distance, margin);
            lastShift = best.Shift;
            lastPedal = best.Pedal;

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
        private float CapAt(Pedal pedal, int ahead, int s, float mySpeed, float d, float margin)
        {
            if (pedal == Pedal.Go) return float.PositiveInfinity;
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
            float over = 0f, risk = 0f, yellow = 0f, lifted = 0f;
            bool onTrack = OnTrack(c, me.Lateral, me.Distance);
            float turnIn = NextCornerStart(c, Mathf.Repeat(d, lap));
            bool turnedIn = false;
            cand.Path[0] = new Vector2(d, lat);
            cand.BrokeAt = Steps + 1;
            for (int s = 1; s <= Steps; s++)
            {
                int k = c.IndexOf(Mathf.Repeat(d, lap));
                // Paths keep to the lanes the pilot steers, which stay clear of the edge.
                float edge = Mathf.Max(c.HalfWidth(k) - RacingLine.EdgeMargin, 0f);
                // The pilot steers the line plus an offset, and the offset is what moves at the overtake rate.
                float wantOff = cand.Stay ? me.Lateral - line.Lateral[k] : s * Tick <= cand.Delay ? holdOffset : cand.Shift;
                off = Mathf.MoveTowards(off, wantOff, Endurance.OvertakeOffsetRate * Tick);
                lat = Mathf.Clamp(line.Lateral[k] + off, -edge, edge);
                if (cand.Owe && nextTurn != 0)
                {
                    // Its width from the edge, and clear of the car there now, wherever that car has got to.
                    float ins = -nextTurn;
                    float bound = c.HalfWidth(k) - 1.5f * CarW - RoomMargin - 0.15f;
                    for (int j = 0; j < others.Count; j++)
                    {
                        float gj = Road(od[j, s] - d, lap);
                        if (stopped[j] || Mathf.Abs(gj) > CarL + 2f || ins * (ol[j, s] - lat) <= 0f) continue;
                        bound = Mathf.Min(bound, ins * ol[j, s] - (Mathf.Lerp(Clear(margin), CarW + RoomMargin, OweTight) + reach[j] - 0.5f * CarW + 0.1f));
                    }
                    if (ins * lat > bound) { lat = ins * bound; off = lat - line.Lateral[k]; }
                }
                if (s == 1) cand.SoonOff = off - holdOffset;
                float lane = pilot.LaneSpeed(k, lat - line.Lateral[k]);
                float cap = CapAt(cand.Pedal, ahead, s, me.Speed, d, margin);
                float limit = Mathf.Min(lane, cap);
                // Off the throttle for a car ahead, short of what its lane allows.
                if (cap < lane - 1f && v >= cap - 0.5f) lifted += Tick;
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
                    float side = ol[j, s] - lat;
                    float apart = Mathf.Abs(side);
                    // H1: never into a car ahead or alongside, with a margin kept: a passing margin past a
                    // car ahead, a little less beside one. A car already closer than that must not get closer.
                    float need = (g > 0.7f * CarL ? Clear(margin) : CarW + AlongsideGap) + reach[j] - 0.5f * CarW;
                    float start = Mathf.Abs(ol[j, 0] - me.Lateral);
                    float from = Road(od[j, 0] - me.Distance, lap);
                    // Nose to tail the margin grows with speed: room for the car ahead to brake harder than expected.
                    float buffer = margin + headway * v;
                    bool touching = from > -0.3f * CarL && from < CarL + margin + headway * me.Speed && start < need;
                    // A car coming from behind is its own to miss: this car only must not move across into it.
                    bool behind = from < -0.3f * CarL;
                    bool into = !behind || apart < Mathf.Abs(ol[j, s] - me.Lateral) - 0.05f;
                    if (g > -0.3f * CarL && g < CarL + (g > 0f ? buffer : 0f) && apart < need && into && (!touching || apart < Mathf.Min(start - 0.1f, need) || apart < CarW + 0.05f && (TouchClose <= 0f || g < from - TouchClose))) { broke = "H1"; by = others[j].Id; brokeBehind = behind; }
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
                    stuck = Mathf.Max(stuck, (1f - ov[j, Steps] / Mathf.Max(freeSpeed, 1f)) * held * StuckScale);
                }
                if (!stopped[j] && Road(od[j, 0] - me.Distance, lap) > 0f && g < 0f) score -= passCost;
            }
            cand.Gain = score; cand.Risk = riskWeight * risk; cand.Stuck = stuck;
            // Lifting to sit behind a car is the last thing to do, not a way to wait (Ivan): a pass that
            // keeps its foot in beats it unless it really is slower.
            cand.Score = score - stuck - riskWeight * risk - YellowWeight * yellow - LiftCost * lifted;
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
                if (o.Ghost && !me.Ghost && !still) continue;
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
        private void Predict(Circuit c, TrafficCar me)
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
                        if (Mathf.Abs(g0) < CarL + 2f && Mathf.Abs(lat - me.Lateral) < Mathf.Abs(ol[j, s - 1] - me.Lateral)) lat = ol[j, s - 1];
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

        /// <summary>The way the next bend within 300 m turns, the one the car is in included, or 0 for none.</summary>
        private static int NextTurn(Circuit c, float here)
        {
            int now = TurnAt(c, here);
            if (now != 0) return now;
            float best = 300f;
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
