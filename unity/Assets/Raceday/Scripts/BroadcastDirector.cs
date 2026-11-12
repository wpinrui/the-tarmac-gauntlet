using TarmacGauntlet.Rendering;
using TarmacGauntlet.Simulation;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The TV director: picks the story, then the shot. Once a second, on the real clock so cuts keep
    /// a TV pace at any race speed, it scores every car. An off for your car or a car near the front of
    /// its class scores highest, then a fight for a class lead, a fight for the podium, an off further
    /// back, any close fight within a class, your car, a class leader's stop or yours, a car rejoining
    /// from the runoff, and the leader. On the first lap offs are common, so one further back drops
    /// below every fight and your car. It cuts when the top story changes
    /// and the shot has run its least length, or when a shot runs its longest. Shots: a trackside camera
    /// ahead of the car that zooms as it comes, a pan from close beside the track, the helicopter, an
    /// onboard, and the pit lane. <see cref="RaceCameras"/> hands it the camera in its TV view. Every shot but the onboard
    /// focuses on its car, so the background blurs (<see cref="PostEffects.Focus"/>).
    /// </summary>
    public sealed class BroadcastDirector : MonoBehaviour
    {
        public Circuit Circuit;
        public RaceCar[] Cars;
        public AiDriver[] Drivers;
        public CarClass[] Classes;
        /// <summary>Index of the player's car, or -1 for none.</summary>
        public int Player = -1;

        private enum Story { None, Leader, Rejoin, Stop, Mine, Battle, LeadBattle, Crash }
        private enum Shot { Trackside, Pan, Helicopter, Onboard, PitLane }

        /// <summary>A fight is two cars of a class this close, s.</summary>
        private const float BattleGap = 0.5f;
        /// <summary>A fight for a class lead counts from this far apart, s.</summary>
        private const float LeadBattleGap = 1f;
        /// <summary>The front of a class is its top this many cars: their offs and fights matter most.</summary>
        private const int Front = 3;
        /// <summary>A car slower than this off the track has stopped there, m/s.</summary>
        private const float StoppedSpeed = 3f;
        /// <summary>The story on air scores this much more, so the director doesn't flick between two.</summary>
        private const float Stick = 15f;

        private Story story;
        private int subject = -1, other = -1;
        private Shot shot;
        private int shotCount;
        private float shotStart = -100f, nextThink;
        private Vector3 anchor, heli;
        private bool placed;

        private int onAir = -10;

        private float Now => Time.unscaledTime;

        /// <summary>True while the director has the camera: the broadcast graphics show only then.</summary>
        public bool OnAir => onAir >= Time.frameCount - 1;
        /// <summary>The car the story is about, or -1.</summary>
        public int Subject => subject;
        /// <summary>The chasing car in a fight, or -1.</summary>
        public int Other => story == Story.Battle || story == Story.LeadBattle ? other : -1;
        /// <summary>True while the story is a stop.</summary>
        public bool Stopping => story == Story.Stop;
        /// <summary>True during a helicopter shot.</summary>
        public bool Helicopter => shot == Shot.Helicopter;
        /// <summary>True during an onboard shot.</summary>
        public bool Onboard => shot == Shot.Onboard;
        /// <summary>Counts the cuts to a new story or car, so graphics can tell a new one has started.</summary>
        public int Stories { get; private set; }

        /// <summary>Puts the camera where the current shot wants it, thinking first if it is time to.</summary>
        public void Frame(Camera cam)
        {
            if (Cars == null || Circuit == null) return;
            onAir = Time.frameCount;
            if (Now >= nextThink)
            {
                nextThink = Now + 1f;
                Think();
            }
            if (subject < 0 || !Live(subject)) return;
            if (ShotDone()) Cut(story, subject, other, false);
            Place(cam);
        }

        private bool Live(int i) => Cars[i] != null && Cars[i].isActiveAndEnabled;

        private void Think()
        {
            Story best = Story.None;
            int bestCar = -1, bestOther = -1;
            float bestScore = float.MinValue;

            void Offer(Story s, int car, int with, float score)
            {
                if (s == story && car == subject) score += Stick;
                if (score > bestScore) (best, bestCar, bestOther, bestScore) = (s, car, with, score);
            }

            int leader = -1;
            float lead = float.MinValue;
            for (int i = 0; i < Cars.Length; i++)
                if (Live(i) && Cars[i].Traffic.Distance > lead) (lead, leader) = (Cars[i].Traffic.Distance, i);
            bool lapOne = leader >= 0 && Cars[leader].Laps <= 0;

            for (int i = 0; i < Cars.Length; i++)
            {
                if (!Live(i)) continue;
                RaceCar c = Cars[i];
                AiRacer racer = Drivers[i] != null ? Drivers[i].Racer : null;
                if (racer != null && racer.Rejoin.Busy)
                {
                    bool stopped = Mathf.Abs(c.Traffic.Speed) < StoppedSpeed && !c.InPitLane;
                    bool key = i == Player || ClassPlace(i) <= Front;
                    float score = stopped
                        ? (key ? 100f : lapOne ? 30f : 75f)
                        : (key ? 50f : lapOne ? 10f : 40f);
                    Offer(stopped ? Story.Crash : Story.Rejoin, i, -1, score);
                }
                if (racer?.Pit != null && racer.Pit.State == PitPlan.Stage.Stopped && (i == Player || ClassLeader(i)))
                    Offer(Story.Stop, i, -1, 55f);
            }
            if (Player >= 0 && Live(Player)) Offer(Story.Mine, Player, -1, 60f);
            if (leader >= 0) Offer(Story.Leader, leader, -1, 20f);

            // Fights: each car and the nearest car of its class ahead of it on the track.
            for (int j = 0; j < Cars.Length; j++)
            {
                if (!Live(j) || Cars[j].InPitLane) continue;
                TrafficCar b = Cars[j].Traffic;
                int ahead = -1;
                float gap = float.MaxValue;
                for (int i = 0; i < Cars.Length; i++)
                {
                    if (i == j || !Live(i) || Cars[i].InPitLane || Classes[i] != Classes[j]) continue;
                    float d = Cars[i].Traffic.Distance - b.Distance;
                    if (d > 0f && d < gap) (gap, ahead) = (d, i);
                }
                if (ahead < 0) continue;
                float seconds = gap / Mathf.Max(b.Speed, 10f);
                int place = ClassPlace(ahead);
                bool forLead = place == 1;
                float within = forLead ? LeadBattleGap : BattleGap;
                if (seconds > within) continue;
                // The nearer the front the better the story, faster classes better again, and a closer
                // fight better still.
                float score = (forLead ? 90f : place <= Front ? 80f : 70f) + (int)Classes[j] + 5f * (1f - seconds / within);
                Offer(forLead ? Story.LeadBattle : Story.Battle, ahead, j, score);
            }

            if (best == Story.None) return;
            bool changed = best != story || bestCar != subject;
            if (changed && Now - shotStart >= MinLength(shot)) Cut(best, bestCar, bestOther, true);
            else if (!changed) other = bestOther;
        }

        private bool ClassLeader(int i) => ClassPlace(i) == 1;

        /// <summary>Car i's place in its class on the road, from 1.</summary>
        private int ClassPlace(int i)
        {
            float mine = Cars[i].Traffic.Distance;
            int place = 1;
            for (int k = 0; k < Cars.Length; k++)
                if (k != i && Live(k) && Classes[k] == Classes[i] && Cars[k].Traffic.Distance > mine) place++;
            return place;
        }

        /// <summary>True once the shot on air has run its longest, or its car has gone past a trackside camera.</summary>
        private bool ShotDone()
        {
            float on = Now - shotStart;
            if (on >= MaxLength(shot)) return true;
            if (on < MinLength(shot)) return false;
            if (shot != Shot.Trackside && shot != Shot.Pan) return false;
            Vector3 car = Cars[subject].transform.position;
            Vector3 fwd = Cars[subject].transform.forward;
            return Vector3.Dot(car - anchor, fwd) > 40f;   // gone by
        }

        private static float MinLength(Shot s) => s switch
        {
            Shot.Helicopter => 8f,
            Shot.Onboard => 6f,
            Shot.Pan => 3f,
            Shot.PitLane => 4f,
            _ => 4f,
        };

        private static float MaxLength(Shot s) => s switch
        {
            Shot.Helicopter => 15f,
            Shot.Onboard => 12f,
            Shot.Pan => 6f,
            Shot.PitLane => 40f,
            _ => 8f,
        };

        private void Cut(Story s, int car, int with, bool newStory)
        {
            story = s;
            subject = car;
            other = with;
            shotCount = newStory ? 0 : shotCount + 1;
            if (newStory) Stories++;
            shot = Pick(s, shotCount);
            // A stop is shown until it's over: once the car leaves its box the story moves on.
            if (s == Story.Stop && shotCount > 0) shot = Shot.Trackside;
            shotStart = Now;
            placed = false;
        }

        private static Shot Pick(Story s, int n)
        {
            switch (s)
            {
                case Story.Crash:
                case Story.Rejoin:
                    return Shot.Trackside;
                case Story.LeadBattle:
                case Story.Battle:
                    return new[] { Shot.Trackside, Shot.Onboard, Shot.Pan }[n % 3];
                case Story.Mine:
                    return new[] { Shot.Pan, Shot.Onboard, Shot.Helicopter, Shot.Trackside }[n % 4];
                case Story.Stop:
                    return Shot.PitLane;
                default:
                    return new[] { Shot.Helicopter, Shot.Pan }[n % 2];
            }
        }

        private void Place(Camera cam)
        {
            Transform t = Cars[subject].transform;
            int k = Circuit.IndexOf(Mathf.Repeat(Cars[subject].Traffic.Distance, Circuit.Lap));
            Vector3 target = t.position + Vector3.up * 0.8f;
            switch (shot)
            {
                case Shot.Trackside:
                case Shot.Pan:
                {
                    if (!placed)
                    {
                        bool pan = shot == Shot.Pan;
                        float speed = Mathf.Max(Cars[subject].Traffic.Speed, 15f);
                        float ahead = pan ? 30f : Mathf.Clamp(speed * 3f, 60f, 200f);
                        int a = Circuit.Wrap(k + Mathf.RoundToInt(ahead / Circuit.Step));
                        Side(a, out float side, out float clear);
                        float lateral = Circuit.HalfWidth(a) + Mathf.Min(clear, pan ? 4f : 20f) + (pan ? 2f : 4f);
                        anchor = Circuit.Centre[a] + Circuit.Left[a] * side * lateral + Vector3.up * (pan ? 1.5f : 4.5f);
                        placed = true;
                    }
                    cam.transform.position = anchor;
                    Aim(cam, target, 8f);
                    float distance = Vector3.Distance(anchor, target);
                    // Zoom so the car fills about the same share of the frame as it comes.
                    float view = shot == Shot.Pan ? 10f : 7f;
                    cam.fieldOfView = Mathf.Clamp(2f * Mathf.Atan(view / Mathf.Max(distance, 1f)) * Mathf.Rad2Deg, 4f, 60f);
                    break;
                }
                case Shot.Helicopter:
                {
                    Vector3 flat = Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
                    Vector3 side = Vector3.Cross(Vector3.up, flat);
                    Vector3 want = t.position - flat * 70f + side * 40f + Vector3.up * 55f;
                    heli = placed ? Vector3.Lerp(heli, want, 1f - Mathf.Exp(-0.8f * Time.unscaledDeltaTime)) : want;
                    placed = true;
                    cam.transform.position = heli;
                    Aim(cam, target, 4f);
                    cam.fieldOfView = 30f;
                    break;
                }
                case Shot.Onboard:
                {
                    Transform from = other >= 0 && Live(other) ? Cars[other].transform : t;
                    cam.transform.SetPositionAndRotation(from.TransformPoint(new Vector3(0f, 1.2f, 0.3f)), from.rotation);
                    cam.fieldOfView = 70f;
                    break;
                }
                case Shot.PitLane:
                {
                    if (!placed)
                    {
                        anchor = t.position + Circuit.Forward[k] * 14f + Vector3.up * 3f;
                        placed = true;
                    }
                    cam.transform.position = anchor;
                    Aim(cam, target, 6f);
                    cam.fieldOfView = 40f;
                    break;
                }
            }
            if (shot != Shot.Onboard) PostEffects.Focus(cam, Vector3.Distance(cam.transform.position, target));
        }

        /// <summary>Turns the camera to the target, smoothly after the first frame of a shot.</summary>
        private void Aim(Camera cam, Vector3 target, float rate)
        {
            Quaternion want = Quaternion.LookRotation(target - cam.transform.position, Vector3.up);
            float on = Now - shotStart;
            cam.transform.rotation = on < 0.05f ? want : Quaternion.Slerp(cam.transform.rotation, want, 1f - Mathf.Exp(-rate * Time.unscaledDeltaTime));
        }

        /// <summary>Which side of the track has more room for a camera at sample k: 1 left, -1 right.</summary>
        private void Side(int k, out float side, out float clear)
        {
            float[] left = Circuit.Data.clearLeft, right = Circuit.Data.clearRight;
            float l = left != null && k < left.Length ? left[k] : 10f;
            float r = right != null && k < right.Length ? right[k] : 10f;
            side = l >= r ? 1f : -1f;
            clear = Mathf.Max(l, r);
        }
    }
}
