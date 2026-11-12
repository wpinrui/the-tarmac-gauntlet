using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// The armco as it looks: two W-section steel rails, one over the other, on posts every
    /// <see cref="PostSpacing"/>, along every run the <see cref="CircuitBuilder"/> laid, and catch fencing behind the armco
    /// round the corners with tyre walls. The rails' front face stands on the line where the runoff meets the armco,
    /// which is where the circuit's own plain armco stands and what cars hit, and everything else stands behind it, so
    /// nothing reaches onto the runoff. None of it is solid: the plain armco stays as the collider.
    /// Free of Unity's runtime, so the offline checks build it too.
    /// </summary>
    public sealed class ArmcoBuilder
    {
        /// <summary>The lower rail's bottom edge above the runoff, and each rail's height, m. Two rails top out at 0.84 m.</summary>
        public const float RailBottom = 0.22f, RailHeight = 0.31f;
        /// <summary>How far the rail's edges stand back from its two ridges, m.</summary>
        public const float RailDepth = 0.06f;
        public const float PostSpacing = 4f;
        public const float PostWidth = 0.12f, PostDepth = 0.15f, PostTop = 0.8f;
        /// <summary>Posts, rails and fences reach this far below what they stand on, so no gap shows on a slope.</summary>
        public const float Footing = 1.6f;
        /// <summary>The rails follow the armco line to within this, m, with no straight piece longer than <see cref="MaxPiece"/>.</summary>
        public const float Tolerance = 0.03f, MaxPiece = 8f;

        /// <summary>Catch fence: how far behind the armco line, how tall, and how far its posts stand apart, m.</summary>
        public const float FenceBack = 0.8f, FenceHeight = 3.5f, FencePostSpacing = 4f;
        /// <summary>A catch fence carries on this far past each end of a tyre wall, m.</summary>
        public const float FenceReach = 30f;
        /// <summary>A catch fence keeps this far along the track from a grandstand's own fence, and this far from the gantry's feet, m.</summary>
        public const float StandGap = 3f, GantryGap = 12f;
        /// <summary>Catch fence pieces shorter than this are dropped, m.</summary>
        public const float MinFence = 20f;

        /// <summary>The rails and posts.</summary>
        public readonly MeshKit Steel = new MeshKit();
        /// <summary>The catch fences' mesh and their posts.</summary>
        public readonly MeshKit Fence = new MeshKit(), FencePosts = new MeshKit();
        /// <summary>Each run's rail line as built: the points the rails pass through, on the armco line.</summary>
        public readonly List<Vector3[]> Rails = new List<Vector3[]>();
        /// <summary>Each post's foot on the armco line.</summary>
        public readonly List<Vector3> Posts = new List<Vector3>();
        /// <summary>Each catch fence's foot line, with its side of the track.</summary>
        public readonly List<(int side, Vector3[] line)> Fences = new List<(int, Vector3[])>();

        private readonly Circuit c;
        private readonly IReadOnlyList<StandFootprint> stands;
        private readonly Vector3[] gantry;

        /// <summary>The rail's front face, bottom to top: (back from the armco line, up from the runoff).</summary>
        private static readonly Vector2[] Profile = MakeProfile();

        public ArmcoBuilder(Circuit circuit, IReadOnlyList<ArmcoRun> runs, IReadOnlyList<StandFootprint> stands, Vector3 gantryLeft, Vector3 gantryRight)
        {
            c = circuit;
            this.stands = stands;
            gantry = new[] { gantryLeft, gantryRight };
            foreach (ArmcoRun run in runs)
            {
                if (run.Line.Length < 2) continue;
                Vector3[] back = Backs(run);
                BuildRails(run, back);
                BuildPosts(run, back);
                BuildFences(run, back);
            }
        }

        private static Vector2[] MakeProfile()
        {
            var p = new List<Vector2>();
            for (int r = 0; r < 2; r++)
            {
                float b = RailBottom + r * RailHeight;
                if (r == 0) p.Add(new Vector2(RailDepth, b));
                p.Add(new Vector2(0f, b + RailHeight * 0.26f));
                p.Add(new Vector2(RailDepth * 0.75f, b + RailHeight * 0.5f));
                p.Add(new Vector2(0f, b + RailHeight * 0.74f));
                p.Add(new Vector2(RailDepth, b + RailHeight));
            }
            return p.ToArray();
        }

        /// <summary>Flat unit vectors square to the line at each point, pointing away from the road.</summary>
        private Vector3[] Backs(ArmcoRun run)
        {
            Vector3[] line = run.Line;
            var back = new Vector3[line.Length];
            for (int i = 0; i < line.Length; i++)
            {
                Vector3 along = Flat(line[Mathf.Min(i + 1, line.Length - 1)] - line[Mathf.Max(i - 1, 0)]).normalized;
                Vector3 b = Vector3.Cross(Vector3.up, along).normalized;
                if (Vector3.Dot(b, Flat(line[i] - c.Centre[run.Samples[i]])) < 0) b = -b;
                back[i] = b;
            }
            return back;
        }

        // ---------------------------------------------------------------- rails

        private void BuildRails(ArmcoRun run, Vector3[] back)
        {
            List<int> kept = Simplify(run.Line, back);
            var line = new Vector3[kept.Count];
            for (int i = 0; i < kept.Count; i++) line[i] = run.Line[kept[i]];
            Rails.Add(line);

            int m = Profile.Length;
            var normals = new Vector2[m];
            for (int t = 0; t < m; t++)
            {
                Vector2 n = Vector2.zero;
                if (t > 0) n += EdgeNormal(Profile[t - 1], Profile[t]);
                if (t < m - 1) n += EdgeNormal(Profile[t], Profile[t + 1]);
                normals[t] = n.normalized;
            }
            for (int i = 0; i + 1 < kept.Count; i++)
            {
                int a = kept[i], b = kept[i + 1];
                Vector3 la = run.Line[a], lb = run.Line[b], ba = back[a], bb = back[b];
                for (int t = 0; t + 1 < m; t++)
                {
                    // Rounded over the ridges and the valley between them, creased at the edges, so the seam where the
                    // two rails meet shows as a dark line.
                    Vector2 edge = EdgeNormal(Profile[t], Profile[t + 1]);
                    Vector2 n0 = Lip(t) ? edge : normals[t], n1 = Lip(t + 1) ? edge : normals[t + 1];
                    Quad(Steel, At(la, ba, Profile[t]), At(la, ba, Profile[t + 1]), At(lb, bb, Profile[t]), At(lb, bb, Profile[t + 1]),
                        Dir(ba, n0), Dir(ba, n1), Dir(bb, n0), Dir(bb, n1));
                }
                // The back of the rails, flat from the top edge down to the bottom one.
                Vector2 top = Profile[m - 1], bottom = Profile[0];
                Quad(Steel, At(la, ba, top), At(la, ba, bottom), At(lb, bb, top), At(lb, bb, bottom), ba, ba, bb, bb);
            }
            Cap(run.Line[kept[0]], back[kept[0]], Flat(run.Line[kept[0]] - run.Line[kept[1]]).normalized);
            int last = kept[kept.Count - 1];
            Cap(run.Line[last], back[last], Flat(run.Line[last] - run.Line[kept[kept.Count - 2]]).normalized);
        }

        /// <summary>
        /// The points of the line the rails pass through: every one where the line bends or climbs, fewer along a
        /// straight, so no point left out lies further than <see cref="Tolerance"/> off the rails.
        /// </summary>
        private static List<int> Simplify(Vector3[] line, Vector3[] back)
        {
            var kept = new List<int> { 0 };
            int i = 0, last = line.Length - 1;
            while (i < last)
            {
                int j = i + 1;
                while (j < last && Fits(line, back, i, j + 1)) j++;
                kept.Add(j);
                i = j;
            }
            return kept;
        }

        private static bool Fits(Vector3[] line, Vector3[] back, int from, int to)
        {
            Vector3 a = line[from], b = line[to];
            if ((b - a).magnitude > MaxPiece) return false;
            if (Vector3.Dot(back[from], back[to]) < Mathf.Cos(2f * Mathf.Deg2Rad)) return false;
            for (int k = from + 1; k < to; k++)
            {
                Vector3 ab = b - a;
                float t = Mathf.Clamp01(Vector3.Dot(line[k] - a, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
                if ((a + ab * t - line[k]).magnitude > Tolerance) return false;
            }
            return true;
        }

        /// <summary>A plate across the rails' open end, facing out along the line.</summary>
        private void Cap(Vector3 l, Vector3 back, Vector3 outward)
        {
            Vector2 mid = Vector2.zero;
            foreach (Vector2 p in Profile) mid += p;
            mid /= Profile.Length;
            Vector3 centre = At(l, back, mid);
            for (int t = 0; t < Profile.Length; t++)
            {
                Vector3 p0 = At(l, back, Profile[t]), p1 = At(l, back, Profile[(t + 1) % Profile.Length]);
                Tri(Steel, centre, p0, p1, outward);
            }
        }

        // ---------------------------------------------------------------- posts

        private void BuildPosts(ArmcoRun run, Vector3[] back)
        {
            Vector3[] line = run.Line;
            float[] lengths = Lengths(line);
            float total = lengths[line.Length - 1];
            int spans = Mathf.Max(1, Mathf.RoundToInt(total / PostSpacing));
            int j = 0;
            for (int i = 0; i <= spans; i++)
            {
                float d = Mathf.Lerp(PostWidth, total - PostWidth, i / (float)spans);
                while (j < line.Length - 2 && lengths[j + 1] < d) j++;
                float t = Mathf.Clamp01((d - lengths[j]) / Mathf.Max(1e-4f, lengths[j + 1] - lengths[j]));
                Vector3 foot = Vector3.Lerp(line[j], line[j + 1], t);
                Vector3 b = Vector3.Lerp(back[j], back[j + 1], t).normalized;
                Posts.Add(foot);
                Post(foot, b);
            }
        }

        /// <summary>One post standing right behind the rails: four sides and a top.</summary>
        private void Post(Vector3 foot, Vector3 back)
        {
            Vector3 along = Vector3.Cross(back, Vector3.up).normalized * (PostWidth * 0.5f);
            Vector3 near = foot + back * RailDepth, far = foot + back * (RailDepth + PostDepth);
            Vector3 lo = Vector3.down * Footing, hi = Vector3.up * PostTop;
            Vector3 centre = (near + far) * 0.5f + Vector3.up * (PostTop - Footing) * 0.5f;
            Steel.Face(near - along + lo, near + along + lo, near - along + hi, near + along + hi, centre);
            Steel.Face(far - along + lo, far + along + lo, far - along + hi, far + along + hi, centre);
            Steel.Face(near - along + lo, far - along + lo, near - along + hi, far - along + hi, centre);
            Steel.Face(near + along + lo, far + along + lo, near + along + hi, far + along + hi, centre);
            Steel.Face(near - along + hi, near + along + hi, far - along + hi, far + along + hi, centre);
        }

        // ---------------------------------------------------------------- catch fences

        /// <summary>
        /// Catch fencing behind the armco from <see cref="FenceReach"/> before each tyre wall to as far past it,
        /// broken where a grandstand has its own or the start gantry stands.
        /// </summary>
        private void BuildFences(ArmcoRun run, Vector3[] back)
        {
            Vector3[] line = run.Line;
            float[] lengths = Lengths(line);
            var want = new bool[line.Length];
            for (int i = 0; i < line.Length; i++)
            {
                if (!run.Tyres[i]) continue;
                for (int k = 0; k < line.Length; k++)
                    if (Mathf.Abs(lengths[k] - lengths[i]) <= FenceReach) want[k] = true;
            }
            for (int i = 0; i < line.Length; i++)
                if (want[i] && (InStand(run.Side, run.Samples[i]) || NearGantry(line[i]))) want[i] = false;

            int start = -1;
            for (int i = 0; i <= line.Length; i++)
            {
                bool on = i < line.Length && want[i];
                if (on && start < 0) start = i;
                if (on || start < 0) continue;
                if (lengths[i - 1] - lengths[start] >= MinFence) FencePiece(run.Side, line, back, start, i - 1);
                start = -1;
            }
        }

        private bool InStand(int side, int k)
        {
            float d = k * c.Step;
            foreach (StandFootprint s in stands)
                if (s.Side == side && Mathf.Repeat(d - (s.From - StandGap), c.Lap) <= Mathf.Repeat(s.To + StandGap - (s.From - StandGap), c.Lap)) return true;
            return false;
        }

        private bool NearGantry(Vector3 p)
        {
            foreach (Vector3 g in gantry)
                if (Flat(p - g).magnitude < GantryGap) return true;
            return false;
        }

        private void FencePiece(int side, Vector3[] line, Vector3[] back, int from, int to)
        {
            var feet = new List<Vector3>();
            var backs = new List<Vector3>();
            for (int i = from; i <= to; i++)
            {
                Vector3 p = line[i] + back[i] * FenceBack;
                p.y = c.TerrainHeight(p.x, p.z);
                feet.Add(p);
                backs.Add(back[i]);
            }
            float[] lengths = Lengths(feet.ToArray());
            float total = lengths[feet.Count - 1];
            int spans = Mathf.Max(1, Mathf.RoundToInt(total / FencePostSpacing));
            var at = new Vector3[spans + 1];
            var outAt = new Vector3[spans + 1];
            int j = 0;
            for (int i = 0; i <= spans; i++)
            {
                float d = total * i / spans;
                while (j < feet.Count - 2 && lengths[j + 1] < d) j++;
                float t = Mathf.Clamp01((d - lengths[j]) / Mathf.Max(1e-4f, lengths[j + 1] - lengths[j]));
                at[i] = Vector3.Lerp(feet[j], feet[j + 1], t);
                outAt[i] = Vector3.Lerp(backs[j], backs[j + 1], t).normalized;
            }
            Fences.Add((side, at));
            Vector3 Top(int i) => at[i] + Vector3.up * FenceHeight;
            Vector3 Bottom(int i) => at[i] + Vector3.down * Footing;
            for (int i = 0; i < spans; i++)
            {
                Vector3 mid = (at[i] + at[i + 1]) * 0.5f + Vector3.up * 1.5f;
                Fence.Face(Bottom(i), Bottom(i + 1), Top(i), Top(i + 1), mid + outAt[i]);
                Fence.Face(Bottom(i), Bottom(i + 1), Top(i), Top(i + 1), mid - outAt[i]);
            }
            const float half = 0.045f;
            // A rail along the top from post to post.
            for (int i = 0; i < spans; i++)
            {
                Vector3 u = Vector3.up * 0.025f;
                Vector3 o0 = outAt[i] * 0.025f, o1 = outAt[i + 1] * 0.025f;
                Vector3 a = Top(i), e = Top(i + 1);
                Vector3 centre = (a + e) * 0.5f;
                FencePosts.Face(a - o0 + u, e - o1 + u, a + o0 + u, e + o1 + u, centre);
                FencePosts.Face(a - o0 - u, e - o1 - u, a + o0 - u, e + o1 - u, centre);
                FencePosts.Face(a - o0 - u, e - o1 - u, a - o0 + u, e - o1 + u, centre);
                FencePosts.Face(a + o0 - u, e + o1 - u, a + o0 + u, e + o1 + u, centre);
            }
            for (int i = 0; i <= spans; i++)
            {
                Vector3 along = Vector3.Cross(outAt[i], Vector3.up).normalized * half, o = outAt[i] * half;
                Vector3 lo = Bottom(i), hi = Top(i) + Vector3.up * 0.05f;
                Vector3 centre = (lo + hi) * 0.5f;
                FencePosts.Face(lo - along - o, lo + along - o, hi - along - o, hi + along - o, centre);
                FencePosts.Face(lo - along + o, lo + along + o, hi - along + o, hi + along + o, centre);
                FencePosts.Face(lo - along - o, lo - along + o, hi - along - o, hi - along + o, centre);
                FencePosts.Face(lo + along - o, lo + along + o, hi + along - o, hi + along + o, centre);
                FencePosts.Face(hi - along - o, hi + along - o, hi - along + o, hi + along + o, centre);
            }
        }

        // ---------------------------------------------------------------- shapes

        private static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);

        private static float[] Lengths(Vector3[] line)
        {
            var lengths = new float[line.Length];
            for (int i = 1; i < line.Length; i++) lengths[i] = lengths[i - 1] + Flat(line[i] - line[i - 1]).magnitude;
            return lengths;
        }

        private static Vector3 At(Vector3 l, Vector3 back, Vector2 p) => l + back * p.x + Vector3.up * p.y;

        private static Vector3 Dir(Vector3 back, Vector2 n) => (back * n.x + Vector3.up * n.y).normalized;

        /// <summary>True for a profile point on a rail's edge, where the steel creases.</summary>
        private static bool Lip(int t) => Profile[t].x >= RailDepth - 1e-4f;

        /// <summary>The normal of a profile edge on the rail's front, pointing at the road (less back).</summary>
        private static Vector2 EdgeNormal(Vector2 a, Vector2 b)
        {
            Vector2 e = b - a;
            Vector2 n = new Vector2(e.y, -e.x).normalized;
            return n.x > 0 ? -n : n;
        }

        /// <summary>A quad a0-a1 across one end, b0-b1 across the other, wound to face its corners' normals.</summary>
        private static void Quad(MeshKit kit, Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1, Vector3 na0, Vector3 na1, Vector3 nb0, Vector3 nb1)
        {
            Vector3 facing = na0 + na1 + nb0 + nb1;
            Vector2 u = Vector2.zero;
            if (Vector3.Dot(Vector3.Cross(b0 - a0, a1 - a0), facing) > 0)
            {
                kit.Triangle(a0, b0, a1, na0, nb0, na1, u, u, u);
                kit.Triangle(a1, b0, b1, na1, nb0, nb1, u, u, u);
            }
            else
            {
                kit.Triangle(a0, a1, b0, na0, na1, nb0, u, u, u);
                kit.Triangle(a1, b1, b0, na1, nb1, nb0, u, u, u);
            }
        }

        private static void Tri(MeshKit kit, Vector3 a, Vector3 b, Vector3 d, Vector3 facing)
        {
            Vector2 u = Vector2.zero;
            if (Vector3.Dot(Vector3.Cross(b - a, d - a), facing) > 0) kit.Triangle(a, b, d, facing, facing, facing, u, u, u);
            else kit.Triangle(a, d, b, facing, facing, facing, u, u, u);
        }
    }
}
