using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// How the rear axle shares the engine's drive between its wheels, and how a tyre fits the drive, the brakes and
    /// the cornering force into its grip. <see cref="CarController"/> and the AI check's stand-in car both use these.
    /// </summary>
    public static class Traction
    {
        /// <summary>
        /// A driven tyre asked for this share of its grip beyond all of it spins fully: it then keeps 90% of its grip
        /// along the road and 60% of its cornering force. Asked for less, it spins less, and loses less.
        /// </summary>
        private const float SpinRange = 0.5f;

        /// <summary>
        /// Splits the drive between the rear wheels like a limited-slip differential: half each, and whatever one
        /// wheel can't put down goes to the other, as far as that one has grip to spare. <c>leftRoom</c> and
        /// <c>rightRoom</c> are the grip each wheel has left along the road once it has cornered, N.
        /// </summary>
        public static void SplitDrive(float drive, float leftRoom, float rightRoom, out float left, out float right)
        {
            float half = 0.5f * Mathf.Abs(drive), sign = Mathf.Sign(drive);
            float l = half, r = half;
            if (half > leftRoom && rightRoom > half)
            {
                float move = Mathf.Min(half - leftRoom, rightRoom - half);
                l -= move;
                r += move;
            }
            else if (half > rightRoom && leftRoom > half)
            {
                float move = Mathf.Min(half - rightRoom, leftRoom - half);
                r -= move;
                l += move;
            }
            left = sign * l;
            right = sign * r;
        }

        /// <summary>
        /// Traction control lets the engine drive the rear wheels this much harder than they can take: enough to
        /// feel them squirm and leave a little rubber, never enough to spin them up and swing the tail round.
        /// </summary>
        public const float Slack = 1.1f;

        /// <summary>
        /// The engine's drive, N, cut by traction control to what the rear wheels have room for, N, on top of the
        /// rolling resistance it first has to overcome, N.
        /// </summary>
        public static float Control(float drive, float room, float rolling) => Mathf.Min(drive, Slack * room + rolling);

        /// <summary>
        /// Grip a tyre has left along the road once it carries a cornering force, N. Never under 30% of its grip,
        /// as with the anti-lock, so a car sliding sideways in the gravel can still drive itself out.
        /// </summary>
        public static float Room(float cap, float fy) => Mathf.Sqrt(Mathf.Max(cap * cap - fy * fy, 0.09f * cap * cap));

        /// <summary>
        /// Fits a tyre's force along the road, fx, and across it, fy, into its grip, cap, all in N. Anti-lock keeps
        /// some steering under braking. A driven tyre asked for more than all its grip spins, and loses cornering
        /// force the harder it spins. Returns how hard it spins, 0 to 1.
        /// </summary>
        public static float Fit(ref float fx, ref float fy, float cap, float drive, bool braking)
        {
            if (fx * fx + fy * fy <= cap * cap) return 0f;
            float spin = 0f;
            if (braking && drive == 0f)
                fx = Mathf.Sign(fx) * Mathf.Sqrt(Mathf.Max(cap * cap - fy * fy, 0.09f * cap * cap));
            else if (drive != 0f && Mathf.Abs(fx) > cap)
            {
                spin = Mathf.Clamp01((Mathf.Abs(fx) / cap - 1f) / SpinRange);
                fx = Mathf.Sign(fx) * cap * (1f - 0.1f * spin);
                fy *= 1f - 0.4f * spin;
            }
            float total = Mathf.Sqrt(fx * fx + fy * fy);
            if (total > cap)
            {
                fx *= cap / total;
                fy *= cap / total;
            }
            return spin;
        }
    }
}
