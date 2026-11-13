using System.Collections.Generic;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    /// <summary>
    /// Builds the podium's meshes from <see cref="PodiumPlan"/>, with no Unity objects so it can be checked and
    /// previewed outside Unity: the balcony slab off the fascia beam, its glass balustrade on steel posts, the three
    /// steps with a stair behind each and a band of gold, silver or bronze round the top, the dark board behind with
    /// a chequered strip, the doorway in the glass, the canopy over it all with its lamps, and the stands for the ice
    /// buckets. The trophies, bottles and buckets move, so each is its own small mesh about its own base
    /// (<see cref="TrophyMesh"/>, <see cref="BottleMesh"/>, <see cref="BucketMesh"/>).
    /// </summary>
    public sealed class PodiumBuilder
    {
        /// <summary>Kits by material, in the podium's own frame turned into the world.</summary>
        public readonly MeshKit Slab = new MeshKit(), Steel = new MeshKit(), Glass = new MeshKit(), Step = new MeshKit(),
            Board = new MeshKit(), Chequer = new MeshKit(), Door = new MeshKit(), Lamp = new MeshKit();
        /// <summary>The bands round the steps' tops, winner first: gold, silver, bronze.</summary>
        public readonly MeshKit[] Band = { new MeshKit(), new MeshKit(), new MeshKit() };

        private readonly PodiumFrame f;

        public PodiumBuilder(PodiumPlan plan)
        {
            f = plan.Frame;
        }

        /// <summary>Every fixed piece, by name, its kit and whether it casts a shadow.</summary>
        public IEnumerable<(string name, MeshKit kit)> Parts()
        {
            yield return ("Podium slab", Slab);
            yield return ("Podium steel", Steel);
            yield return ("Podium glass", Glass);
            yield return ("Podium steps", Step);
            yield return ("Podium board", Board);
            yield return ("Podium chequer", Chequer);
            yield return ("Podium doorway", Door);
            yield return ("Podium lamps", Lamp);
            for (int p = 0; p < 3; p++) yield return ("Podium band " + (p + 1), Band[p]);
        }

        public PodiumBuilder Build()
        {
            const float F = PodiumPlan.Floor, W = PodiumPlan.HalfWidth;
            float back = PodiumPlan.Back, front = PodiumPlan.Front;

            // The slab: off the fascia beam, out over the pit lane, its front edge a deeper fascia.
            Block(Slab, -W, W, back - 0.1f, front, F - 0.35f, F);
            Block(Slab, -W, W, front - 0.25f, front, F - 0.75f, F - 0.35f);
            // The upstand at the back, down over the fascia beam where the garage floors fall away under the level slab.
            Block(Slab, -W, W, back - 0.1f, back + 0.35f, F - PodiumPlan.Upstand, F - 0.35f);
            // Steel brackets under it, back into the fascia.
            for (int b = -2; b <= 2; b++)
                Block(Steel, b * 3.4f - 0.1f, b * 3.4f + 0.1f, back, front - 0.25f, F - 0.75f, F - 0.35f);

            // The balustrade: glass panes on steel posts along the front and both ends, a steel rail on top.
            float rail = F + PodiumPlan.RailHeight;
            for (float x = -W + 0.05f; x < W; x += 1.48f)
            {
                float x1 = Mathf.Min(x + 1.48f, W - 0.05f);
                Pane(new Vector3(x + 0.04f, F + 0.06f, front - 0.08f), new Vector3(x1 - 0.04f, rail - 0.06f, front - 0.08f));
            }
            for (float z = back + 0.1f; z < front - 0.1f; z += 1.4f)
            {
                float zEnd = Mathf.Min(z + 1.4f, front - 0.12f);
                foreach (float sx in new[] { -W + 0.08f, W - 0.08f })
                    Pane(new Vector3(sx, F + 0.06f, z + 0.04f), new Vector3(sx, rail - 0.06f, zEnd - 0.04f));
            }
            for (float x = -W + 0.05f; x <= W; x += 1.48f) Block(Steel, x - 0.03f, x + 0.03f, front - 0.11f, front - 0.05f, F, rail);
            Block(Steel, -W, W, front - 0.14f, front - 0.02f, rail, rail + 0.06f);
            foreach (float sx in new[] { -W + 0.08f, W - 0.08f })
                Block(Steel, sx - 0.06f, sx + 0.06f, back, front - 0.02f, rail, rail + 0.06f);

            // The steps, a half-height stair behind each, and a band round each top in its colour.
            for (int p = 0; p < 3; p++)
            {
                float x = PodiumPlan.StepX[p], h = PodiumPlan.StepHeights[p], half = PodiumPlan.StepWidth * 0.5f - 0.02f;
                Block(Step, x - half, x + half, PodiumPlan.StepBack, PodiumPlan.StepFront, F, F + h - 0.06f);
                Block(Band[p], x - half - 0.005f, x + half + 0.005f, PodiumPlan.StepBack - 0.005f, PodiumPlan.StepFront + 0.005f, F + h - 0.06f, F + h);
                Block(Step, x - half, x + half, PodiumPlan.StairBack, PodiumPlan.StepBack, F, F + h * 0.5f);
                // A tall numeral-free stripe of the band's colour down the step's front face.
                Block(Band[p], x - 0.22f, x + 0.22f, PodiumPlan.StepFront, PodiumPlan.StepFront + 0.012f, F + 0.04f, F + h - 0.1f);
            }

            // The board behind: dark, a chequered strip along its top, the doorway in the middle.
            float boardTop = F + PodiumPlan.CanopyHeight - 0.2f, z0 = back + 0.04f, z1 = back + 0.1f;
            float door = PodiumPlan.DoorHalf, doorTop = F + PodiumPlan.DoorHeight;
            Block(Board, -W + 0.3f, -door - 0.12f, z0, z1, F, boardTop);
            Block(Board, door + 0.12f, W - 0.3f, z0, z1, F, boardTop);
            Block(Board, -door - 0.12f, door + 0.12f, z0, z1, doorTop + 0.12f, boardTop);
            // The doorway's steel frame and the dark way in behind it.
            Block(Steel, -door - 0.12f, -door, z0 - 0.02f, z1 + 0.04f, F, doorTop + 0.12f);
            Block(Steel, door, door + 0.12f, z0 - 0.02f, z1 + 0.04f, F, doorTop + 0.12f);
            Block(Steel, -door, door, z0 - 0.02f, z1 + 0.04f, doorTop, doorTop + 0.12f);
            // Open, dark inside: the drivers walk out of it.
            Block(Door, -door, door, back + 0.005f, back + 0.03f, F, doorTop);
            const float cell = 0.25f;
            for (int row = 0; row < 2; row++)
                for (int c = 0; ; c++)
                {
                    float x = -W + 0.3f + c * cell;
                    if (x + cell > W - 0.3f + 1e-3f) break;
                    if ((c + row) % 2 == 0) Block(Chequer, x, x + cell, z1, z1 + 0.01f, boardTop - 0.6f + row * cell, boardTop - 0.6f + (row + 1) * cell);
                }

            // The canopy: a thin steel roof on two posts at the front, lamps along its underside.
            float top = F + PodiumPlan.CanopyHeight, cf = PodiumPlan.CanopyFront;
            Block(Steel, -W, W, back - 0.1f, cf, top, top + 0.22f);
            Block(Steel, -W, W, cf - 0.18f, cf, top - 0.35f, top);
            foreach (float px in new[] { -W + 0.25f, W - 0.25f })
                Block(Steel, px - 0.09f, px + 0.09f, cf - 0.27f, cf - 0.09f, F, top);
            for (int p = 0; p < 3; p++)
                for (int l = -1; l <= 1; l++)
                {
                    float x = PodiumPlan.StepX[p] + l * 0.9f;
                    Block(Lamp, x - 0.18f, x + 0.18f, 10.2f, 10.5f, top - 0.04f, top);
                }
            return this;
        }

        /// <summary>The ice bucket's stand, about its foot: a round foot, a post, and a ring under the bucket.</summary>
        public static MeshKit BucketStand()
        {
            var k = new MeshKit();
            float h = PodiumPlan.BucketTop - 0.22f;
            Lathe(k, new[] { new Vector2(0f, 0f), new Vector2(0.15f, 0f), new Vector2(0.15f, 0.02f), new Vector2(0.025f, 0.035f), new Vector2(0.018f, h - 0.03f), new Vector2(0.1f, h - 0.01f), new Vector2(0.1f, h), new Vector2(0f, h) }, 18);
            return k;
        }

        /// <summary>The ice bucket, about the top of its stand: steel, flared, open at the top, ice inside.</summary>
        public static MeshKit BucketMesh()
        {
            var k = new MeshKit();
            Lathe(k, new[] { new Vector2(0f, 0f), new Vector2(0.09f, 0f), new Vector2(0.12f, 0.2f), new Vector2(0.13f, 0.22f), new Vector2(0.115f, 0.22f), new Vector2(0.085f, 0.03f), new Vector2(0f, 0.17f) }, 22);
            return k;
        }

        /// <summary>A champagne bottle about its base, standing up: dark glass, the neck in gold foil (submesh by kit).</summary>
        public static (MeshKit glass, MeshKit foil) BottleMesh()
        {
            var glass = new MeshKit();
            var foil = new MeshKit();
            Lathe(glass, new[] { new Vector2(0f, 0.015f), new Vector2(0.035f, 0f), new Vector2(0.044f, 0.01f), new Vector2(0.045f, 0.19f), new Vector2(0.03f, 0.235f) }, 18);
            Lathe(foil, new[] { new Vector2(0.03f, 0.235f), new Vector2(0.016f, 0.262f), new Vector2(0.0155f, 0.3f), new Vector2(0.018f, 0.305f), new Vector2(0.013f, 0.31f), new Vector2(0f, 0.31f) }, 14);
            return (glass, foil);
        }

        /// <summary>A trophy about its base, standing up, at a scale: a dark plinth, a gilt stem and cup, and a handle either side along x.</summary>
        public static (MeshKit gilt, MeshKit plinth) TrophyMesh(float scale)
        {
            var gilt = new MeshKit();
            var plinth = new MeshKit();
            Vector2 S(float r, float y) => new Vector2(r * scale, y * scale);
            Lathe(plinth, new[] { S(0f, 0f), S(0.085f, 0f), S(0.085f, 0.1f), S(0.07f, 0.11f), S(0f, 0.11f) }, 4, 45f);
            Lathe(gilt, new[] { S(0f, 0.11f), S(0.05f, 0.11f), S(0.045f, 0.13f), S(0.018f, 0.16f), S(0.016f, 0.2f), S(0.03f, 0.215f), S(0.016f, 0.23f),
                S(0.045f, 0.26f), S(0.09f, 0.32f), S(0.125f, 0.42f), S(0.135f, 0.46f), S(0.125f, 0.462f), S(0.11f, 0.43f), S(0f, 0.4f) }, 24);
            // A handle either side: a loop of short bars from the bowl's waist out and up to its rim.
            foreach (float sx in new[] { -1f, 1f })
            {
                Vector3 prev = Vector3.zero;
                for (int i = 0; i <= 8; i++)
                {
                    float a = Mathf.PI * (i / 8f);
                    var p = new Vector3(sx * (0.1f + 0.065f * Mathf.Sin(a)), 0.3f + 0.12f * (1f - Mathf.Cos(a)) * 0.5f + 0.02f, 0f) * scale;
                    if (i > 0) Bar(gilt, prev, p, 0.009f * scale);
                    prev = p;
                }
            }
            return (gilt, plinth);
        }

        // ---------------------------------------------------------------- shapes

        /// <summary>A box between x0..x1, z0..z1, y0..y1 in the frame.</summary>
        private void Block(MeshKit k, float x0, float x1, float z0, float z1, float y0, float y1)
        {
            Vector3 c = f.At((x0 + x1) * 0.5f, (y0 + y1) * 0.5f, (z0 + z1) * 0.5f);
            k.Box(c, f.Along, f.Out, new Vector3(x1 - x0, y1 - y0, z1 - z0));
        }

        /// <summary>A pane of glass between two corners in the frame, flat in x or in z, both faces.</summary>
        private void Pane(Vector3 a, Vector3 b)
        {
            Vector3 p00 = f.At(a.x, a.y, a.z), p11 = f.At(b.x, b.y, b.z);
            Vector3 p01 = f.At(b.x, a.y, b.z), p10 = f.At(a.x, b.y, a.z);
            Vector3 n = Vector3.Cross(p01 - p00, p10 - p00).normalized;
            Glass.Face(p00, p01, p10, p11, (p00 + p11) * 0.5f - n);
            Glass.Face(p00, p01, p10, p11, (p00 + p11) * 0.5f + n);
        }

        /// <summary>A profile (radius, height) turned round the y axis, from the axis at the bottom round to the axis at the top.</summary>
        public static void Lathe(MeshKit k, Vector2[] profile, int sides, float startDegrees = 0f)
        {
            for (int i = 0; i < profile.Length - 1; i++)
            {
                Vector2 a = profile[i], b = profile[i + 1];
                for (int s = 0; s < sides; s++)
                {
                    float t0 = (startDegrees * Mathf.Deg2Rad) + 2f * Mathf.PI * s / sides, t1 = (startDegrees * Mathf.Deg2Rad) + 2f * Mathf.PI * (s + 1) / sides;
                    Vector3 R(Vector2 p, float t) => new Vector3(p.x * Mathf.Cos(t), p.y, p.x * Mathf.Sin(t));
                    Vector3 a0 = R(a, t0), a1 = R(a, t1), b0 = R(b, t0), b1 = R(b, t1);
                    // The profile's outward normal, turned with it.
                    Vector2 d = b - a;
                    var n2 = new Vector2(d.y, -d.x);
                    float len = n2.magnitude;
                    n2 = len > 1e-6f ? n2 / len : new Vector2(0f, 1f);
                    Vector3 N(float t) => new Vector3(n2.x * Mathf.Cos(t), n2.y, n2.x * Mathf.Sin(t));
                    Vector2 u = new Vector2(s / (float)sides, i / (float)profile.Length);
                    if ((a0 - a1).sqrMagnitude > 1e-10f) k.Triangle(a0, b0, a1, N(t0), N(t0), N(t1), u, u, u);
                    if ((b0 - b1).sqrMagnitude > 1e-10f) k.Triangle(a1, b0, b1, N(t1), N(t0), N(t1), u, u, u);
                }
            }
        }

        /// <summary>A thin square bar from a to b.</summary>
        private static void Bar(MeshKit k, Vector3 a, Vector3 b, float half)
        {
            Vector3 along = (b - a).normalized;
            Vector3 side = Vector3.Cross(along, new Vector3(0f, 0f, 1f)).normalized;
            if (side.sqrMagnitude < 0.5f) side = new Vector3(1f, 0f, 0f);
            Vector3 up = Vector3.Cross(side, along).normalized;
            Vector3 mid = (a + b) * 0.5f;
            Vector3[] c = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                float sx = (i & 1) == 0 ? -1f : 1f, sy = (i & 2) == 0 ? -1f : 1f;
                c[i] = ((i & 4) == 0 ? a : b) + side * (sx * half) + up * (sy * half);
            }
            k.Face(c[0], c[1], c[4], c[5], mid);
            k.Face(c[2], c[3], c[6], c[7], mid);
            k.Face(c[0], c[2], c[4], c[6], mid);
            k.Face(c[1], c[3], c[5], c[7], mid);
        }
    }
}
