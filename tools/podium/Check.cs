// Builds the podium on the circuit's race offices exactly as the game does (PodiumPlan, PodiumBuilder) and plays its
// ceremony (PodiumShow) with every line-up of one to three drivers in each place, and checks them. Each rule prints one
// line: ok, or FAIL with the worst cases. Exit code 1 if any rule fails.
//
// Rules:
//   on the offices      the balcony's back edge meets the offices' glass front along its width (within 0.15 m), the
//                       fascia beam's top is under its floor and over the foot of its back upstand all along, and it
//                       spans no garage beyond the offices
//   faces outward       every triangle of the podium, trophies, bottles and buckets faces the way its normals say
//   crowd in the lane   the crews below stand on the pit lane, clear of the pit wall, 1 m clear of every car parked in
//                       its box, and 0.55 m from each other
//   room for everyone   on the podium no two people come within 0.42 m of each other, at any moment
//   feet on the podium  everyone on the podium stands on the balcony, inside its balustrade, and each driver's feet are
//                       on the step or the floor under them
//   hands reach         every reach a hand makes (weight over 0.5) is within the arm's length from the shoulder,
//                       measured on the crew body's own skeleton (2 cm allowed)
//   props held          the trophies and bottles are never inside a step or the floor, and a held trophy or bottle is
//                       never further than 0.12 m from the hand holding it
//   camera clear        every shot's camera stands outside the building, the balcony and the canopy, 0.6 m from
//                       every person, over the lane, and sees what it looks at past the balcony and the canopy
//   cheap to draw       the podium's fixed meshes are at most 25k triangles, a trophy, bottle and bucket 3k each
//
// With a folder as the third argument it also writes the preview's data there (podium.js): the podium and the pit
// building and lane round it, and every person, prop and shot of the two sample ceremonies at 30 frames a second.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using TarmacGauntlet.Raceday;
using UnityEngine;

namespace TarmacGauntlet.Raceday
{
    public sealed class PitMaterials
    {
        public Material Concrete, Glass, Steel, Palette, Fit, Metal, Rubber, Lamps;
    }
}

static class Check
{
    static bool failed;
    const float Dt = 1f / 30f;

    static void Rule(string name, bool ok, string detail)
    {
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + name + ": " + detail);
        if (!ok) failed = true;
    }

    // ------------------------------------------------------------------ the body's skeleton at rest

    struct Q
    {
        public float x, y, z, w;
        public Q(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Q operator *(Q a, Q b) => new Q(
            a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
            a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
            a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
            a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z);
        public Vector3 Rotate(Vector3 v)
        {
            var u = new Vector3(x, y, z);
            Vector3 t = Vector3.Cross(u, v) * 2f;
            return v + t * w + Vector3.Cross(u, t);
        }
    }

    /// <summary>Shoulders in the body's own frame (the bodies face -Z as packed), and each arm's length.</summary>
    static (Vector3 left, Vector3 right, float arm) Skeleton(string path)
    {
        using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement nodes = doc.RootElement.GetProperty("nodes");
        int n = nodes.GetArrayLength();
        var pos = new Vector3[n];
        var rot = new Q[n];
        var names = new string[n];
        for (int i = 0; i < n; i++)
        {
            JsonElement e = nodes[i];
            names[i] = e.GetProperty("name").GetString();
            int parent = e.GetProperty("parent").GetInt32();
            float[] t = e.GetProperty("t").EnumerateArray().Select(v => v.GetSingle()).ToArray();
            float[] r = e.GetProperty("r").EnumerateArray().Select(v => v.GetSingle()).ToArray();
            var lt = new Vector3(t[0], t[1], t[2]);
            var lr = new Q(r[0], r[1], r[2], r[3]);
            if (parent < 0 || i == 0) { pos[i] = lt; rot[i] = lr; continue; }
            pos[i] = pos[parent] + rot[parent].Rotate(lt);
            rot[i] = rot[parent] * lr;
        }
        Vector3 P(string name) => pos[Array.IndexOf(names, name)];
        float arm = (P("LeftForeArm") - P("LeftArm")).magnitude + (P("LeftHand") - P("LeftForeArm")).magnitude;
        return (P("LeftArm"), P("RightArm"), arm);
    }

    // ------------------------------------------------------------------ main

    static int Main(string[] args)
    {
        var data = JsonSerializer.Deserialize<CircuitData>(File.ReadAllText(args[0]), new JsonSerializerOptions { IncludeFields = true });
        var circuit = new Circuit(data);
        var builder = new CircuitBuilder(circuit, null);
        List<CircuitPart> track = builder.Build();
        var pc = new PitComplex(circuit, builder.GarageFloors);
        var lane = new PitLane(circuit);
        PodiumPlan plan = PodiumPlan.On(pc);
        PodiumFrame f = plan.Frame;
        var crowd = PodiumPlan.Crowd(lane, f, pc);
        (Vector3 shoulderL, Vector3 shoulderR, float arm) = Skeleton(args[1]);
        float laneHeight = crowd.Average(c => c.at.y);
        if (Environment.GetEnvironmentVariable("CROWD") != null) foreach (var c in crowd) Console.WriteLine($"crowd {c.at.x:0.0} {c.at.y:0.00} {c.at.z:0.00}");

        OnTheOffices(pc, f);
        PodiumBuilder pieces = new PodiumBuilder(plan).Build();
        FacesOutward(pieces);
        CrowdInLane(lane, pc, f, crowd);

        // Every line-up: one, two or three drivers in each place.
        var lineups = new List<int[]>();
        for (int a = 1; a <= 3; a++)
            for (int b = 0; b <= 3; b++)
                for (int c = 0; c <= 3; c++)
                    if (c == 0 || b > 0) lineups.Add(new[] { a, b, c });
        float closest = float.MaxValue; string closestAt = "";
        float worstFoot = 0f; string footAt = "none";
        float worstReach = 0f; string reachAt = "none";
        float worstHold = 0f; string holdAt = "none";
        float worstSink = 0f; string sinkAt = "none";
        float worstEye = 0f; string eyeAt = "none";
        int shots = 0;
        foreach (int[] drivers in lineups)
        {
            var show = new PodiumShow(drivers, crowd);
            string who = string.Join("-", drivers);
            for (float t = 0f; t <= PodiumShow.Length; t += Dt)
            {
                var onPodium = new List<(PodiumActor a, PodiumPose p)>();
                foreach (PodiumActor a in show.Actors)
                {
                    if (a.Role == PodiumRole.Crew) continue;
                    if (a.Role == PodiumRole.Driver && t < show.StartOf(a)) continue;
                    PodiumPose p = show.Pose(a, t);
                    onPodium.Add((a, p));
                    // Feet on the podium.
                    float bad = 0f;
                    Vector3 at = p.At;
                    bad = Mathf.Max(bad, Mathf.Abs(at.x) - (PodiumPlan.HalfWidth - 0.35f));
                    bad = Mathf.Max(bad, PodiumPlan.Back + 0.03f - at.z);
                    bad = Mathf.Max(bad, at.z - (PodiumPlan.Front - 0.3f));
                    if (a.Role == PodiumRole.Driver)
                        bad = Mathf.Max(bad, Mathf.Abs(at.y - Under(at)) - (OnStair(at) ? PodiumPlan.StepHeights[a.Place] * 0.5f + 0.02f : 0.02f));
                    else bad = Mathf.Max(bad, Mathf.Abs(at.y - PodiumPlan.Floor) - 0.01f);
                    if (bad > worstFoot) { worstFoot = bad; footAt = $"{a.Role} {a.Place + 1}.{a.Slot + 1} at {t:0.0} s ({who})"; }
                    // Reach, from the shoulder of a body facing its way: the packed bodies face -Z.
                    Vector3 face = p.Face;
                    Vector3 fwd = -face, right = PodiumShow.RightOf(face) * -1f;
                    Vector3 Body(Vector3 o) => at + new Vector3(0f, 0f, 0f) + Turn(o, face);
                    foreach ((Vector3 target, float w, Vector3 shoulder) in new[] { (p.LeftHand, p.LeftWeight, shoulderL), (p.RightHand, p.RightWeight, shoulderR) })
                    {
                        if (w <= 0.5f) continue;
                        float over = (target - Body(shoulder)).magnitude - arm - 0.02f;
                        if (over > 0f && who == "1-0-0" && Environment.GetEnvironmentVariable("REACH") != null) Console.WriteLine($"reach {a.Role} {t:0.00} {over:0.00} w {w:0.00}");
                        if (over > worstReach) { worstReach = over; reachAt = $"{a.Role} {a.Place + 1}.{a.Slot + 1} at {t:0.0} s ({who}) {over * 100f:0} cm short"; }
                    }
                }
                for (int i = 0; i < onPodium.Count; i++)
                    for (int j = i + 1; j < onPodium.Count; j++)
                    {
                        Vector3 d = onPodium[i].p.At - onPodium[j].p.At;
                        d.y = 0f;
                        float gap = d.magnitude;
                        if (gap < closest) { closest = gap; closestAt = $"{Name(onPodium[i].a)} and {Name(onPodium[j].a)} at {t:0.0} s ({who})"; }
                    }
                // Props: never in a step, and held ones in the hand.
                for (int pl = 0; pl < 3; pl++)
                {
                    if (drivers[pl] == 0) continue;
                    (Vector3 tb, _) = show.Trophy(pl, t);
                    (Vector3 bb, Vector3 axis) = show.Bottle(pl, t);
                    foreach ((string what, Vector3 bottom) in new[] { ("trophy", tb), ("bottle", bb) })
                    {
                        float sink = Under(bottom) - bottom.y - 0.005f;
                        if (sink > worstSink) { worstSink = sink; sinkAt = $"place {pl + 1} {what} at {t:0.0} s ({who}) {sink * 100f:0} cm in"; }
                    }
                    PodiumActor lead = show.Actors.First(x => x.Role == PodiumRole.Driver && x.Place == pl && x.Slot == 0);
                    PodiumPose lp = show.Pose(lead, t);
                    if (t > PodiumShow.Present[pl] + PodiumShow.Handed && lp.LeftWeight > 0.99f)
                    {
                        float off = Mathf.Abs((lp.LeftHand - (tb + Vector3.up * PodiumShow.TrophyHandle(pl))).magnitude - PodiumShow.TrophySide(pl));
                        if (off > 0.12f && off > worstHold) { worstHold = off; holdAt = $"place {pl + 1} trophy at {t:0.0} s ({who}) {off * 100f:0} cm off"; }
                    }
                    if (t > PodiumShow.BottleGrabbed && lp.RightWeight > 0.99f)
                    {
                        float off = (lp.RightHand - (bb + axis * PodiumShow.BottleGripHeight)).magnitude;
                        if (off > 0.12f && off > worstHold) { worstHold = off; holdAt = $"place {pl + 1} bottle at {t:0.0} s ({who}) {off * 100f:0} cm off"; }
                    }
                }
                // The camera.
                PodiumShot s = show.Shot(t, laneHeight);
                shots++;
                float eyeBad = EyeBad(s, onPodium.Select(x => x.p.At).Concat(crowd.Select(c => c.at)), laneHeight, out string why);
                if (eyeBad > worstEye) { worstEye = eyeBad; eyeAt = $"{t:0.0} s ({who}) {why}"; }
                if (eyeBad > 0f && who == "3-3-3" && Environment.GetEnvironmentVariable("CAMS") != null && Math.Abs(t * 2f - Math.Round(t * 2f)) < Dt) Console.WriteLine($"cam {t:0.0} {why}");
            }
        }
        Rule("room for everyone", closest >= 0.42f, $"closest two {closest:0.00} m apart, {closestAt}");
        Rule("feet on the podium", worstFoot <= 0f, worstFoot <= 0f ? "everyone on the balcony and their steps" : $"worst {worstFoot:0.00} m off, {footAt}");
        Rule("hands reach", worstReach <= 0f, worstReach <= 0f ? $"every reach within the {arm:0.000} m arm" : reachAt);
        Rule("props held", worstSink <= 0f && worstHold <= 0f, worstSink > 0f ? sinkAt : worstHold > 0f ? holdAt : "no prop in a step, every held prop in its hand");
        Rule("camera clear", worstEye <= 0f, worstEye <= 0f ? $"{shots} frames of shots" : eyeAt);

        int fixedTris = pieces.Parts().Sum(p => p.kit.Triangles.Count / 3);
        int propTris = new[] { PodiumBuilder.TrophyMesh(1f).gilt.Triangles.Count + PodiumBuilder.TrophyMesh(1f).plinth.Triangles.Count,
            PodiumBuilder.BottleMesh().glass.Triangles.Count + PodiumBuilder.BottleMesh().foil.Triangles.Count,
            PodiumBuilder.BucketMesh().Triangles.Count + PodiumBuilder.BucketStand().Triangles.Count }.Max() / 3;
        Rule("cheap to draw", fixedTris <= 25000 && propTris <= 3000, $"podium {fixedTris / 1000f:0.0}k triangles, largest prop {propTris}");

        if (args.Length > 2) Preview(args[2], f, pieces, track, pc, crowd, laneHeight);
        return failed ? 1 : 0;
    }

    static string Name(PodiumActor a) => $"{a.Role} {a.Place + 1}.{a.Slot + 1}";

    /// <summary>A body offset, in its own frame facing -Z, turned to face the way given, in the podium's frame.</summary>
    static Vector3 Turn(Vector3 o, Vector3 face)
    {
        // The body's -Z is its front; its +X is its left. In the podium's mirrored frame the person's right is RightOf(face).
        Vector3 right = PodiumShow.RightOf(face);
        return face * -o.z + right * -o.x + Vector3.up * o.y;
    }

    /// <summary>The height of whatever is under a point: a step's top or the balcony floor.</summary>
    static float Under(Vector3 at)
    {
        for (int p = 0; p < 3; p++)
        {
            if (Mathf.Abs(at.x - PodiumPlan.StepX[p]) > PodiumPlan.StepWidth * 0.5f) continue;
            if (at.z >= PodiumPlan.StepBack && at.z <= PodiumPlan.StepFront) return PodiumPlan.Floor + PodiumPlan.StepHeights[p];
            if (at.z >= PodiumPlan.StairBack && at.z < PodiumPlan.StepBack) return PodiumPlan.Floor + PodiumPlan.StepHeights[p] * 0.5f;
        }
        return PodiumPlan.Floor;
    }

    static bool OnStair(Vector3 at) => at.z >= PodiumPlan.StairBack - 0.25f && at.z <= PodiumPlan.StepBack + 0.25f;

    // ------------------------------------------------------------------ rules

    static void OnTheOffices(PitComplex pc, PodiumFrame f)
    {
        float worstGap = 0f, worstFloor = 0f;
        float s0 = PodiumPlan.Station(pc);
        float lo = float.MaxValue, hi = float.MinValue;
        for (float x = -PodiumPlan.HalfWidth; x <= PodiumPlan.HalfWidth + 1e-3f; x += 0.5f)
        {
            Vector3 p = f.At(x, 0f, PodiumPlan.Back);
            // The station along the row level with this point.
            float best = float.MaxValue, at = s0;
            for (float s = s0 - 1.5f; s <= s0 + 1.5f; s += 0.002f)
            {
                float d = Mathf.Abs(Vector3.Dot(pc.RowPoint(s, 0f, PodiumPlan.Back) - p, f.Along));
                if (d < best) { best = d; at = s; }
            }
            Vector3 q = pc.RowPoint(at, 0f, PodiumPlan.Back);
            Vector3 gap = q - p;
            gap.y = 0f;
            worstGap = Mathf.Max(worstGap, gap.magnitude);
            // The beam's top never above the floor, nor below the upstand's foot.
            float over = q.y - p.y;
            worstFloor = Mathf.Max(worstFloor, Mathf.Max(over, -over - (PodiumPlan.Upstand - 0.35f)));
            if (Environment.GetEnvironmentVariable("FLOORS") != null) Console.WriteLine($"x {x:0.0} beam over frame {q.y - p.y:0.000}");
            lo = Mathf.Min(lo, at);
            hi = Mathf.Max(hi, at);
        }
        bool inside = lo >= pc.OfficeFrom && hi <= pc.OfficeTo;
        Rule("on the offices", worstGap <= 0.15f && worstFloor <= 0.005f && inside,
            $"back edge {worstGap * 100f:0} cm from the glass at worst, beam {(worstFloor > 0f ? worstFloor * 100f + " cm out of the slab" : "under the slab and its upstand")}, stations {lo:0.00} to {hi:0.00} of the offices' {pc.OfficeFrom} to {pc.OfficeTo}");
    }

    static void FacesOutward(PodiumBuilder pieces)
    {
        var kits = pieces.Parts().Select(p => (p.name, p.kit)).ToList();
        (MeshKit g, MeshKit pl) = PodiumBuilder.TrophyMesh(1f);
        (MeshKit bg, MeshKit bf) = PodiumBuilder.BottleMesh();
        kits.Add(("trophy", g)); kits.Add(("plinth", pl)); kits.Add(("bottle", bg)); kits.Add(("foil", bf));
        kits.Add(("bucket", PodiumBuilder.BucketMesh())); kits.Add(("stand", PodiumBuilder.BucketStand()));
        int bad = 0, all = 0;
        string first = "";
        foreach ((string name, MeshKit kit) in kits)
        {
            IReadOnlyList<Vector3> v = kit.Vertices, n = kit.Normals;
            IReadOnlyList<int> tri = kit.Triangles;
            for (int i = 0; i < tri.Count; i += 3)
            {
                Vector3 a = v[tri[i]], b = v[tri[i + 1]], c = v[tri[i + 2]];
                Vector3 geo = Vector3.Cross(b - a, c - a);
                if (geo.sqrMagnitude < 1e-12f) continue;
                all++;
                Vector3 avg = n[tri[i]] + n[tri[i + 1]] + n[tri[i + 2]];
                if (Vector3.Dot(geo, avg) <= 0f) { bad++; if (first == "") first = name; }
            }
        }
        Rule("faces outward", bad == 0, bad == 0 ? $"{all} triangles" : $"{bad} of {all} triangles face in, first in {first}");
    }

    static void CrowdInLane(PitLane lane, PitComplex pc, PodiumFrame f, List<(Vector3 at, int place)> crowd)
    {
        float worstCar = float.MaxValue, worstGap = float.MaxValue, worstLane = float.MaxValue;
        float laneHalf = lane.Data.laneWidth * 0.5f;
        foreach ((Vector3 at, _) in crowd)
        {
            Vector3 w = f.At(at);
            // Across the lane, from its middle.
            float i = lane.NearestLane(w, NearestTrack(lane, w));
            Vector3 mid = lane.LanePoint(i, 0f);
            float across = Vector3.Dot(w - mid, lane.LaneLeft(i));
            worstLane = Mathf.Min(worstLane, laneHalf - 0.35f - Mathf.Abs(across));
            for (int g = 0; g < lane.Garages; g++)
            {
                Vector3 box = lane.BoxPoint(g);
                if ((box - w).sqrMagnitude > 60f * 60f) continue;
                Vector3 left = lane.LaneLeft(lane.BoxLane[g]);
                var along = new Vector3(left.z, 0f, -left.x);
                Vector3 d = w - box;
                float ax = Mathf.Max(0f, Mathf.Abs(Vector3.Dot(d, along)) - 2.45f), ay = Mathf.Max(0f, Mathf.Abs(Vector3.Dot(d, left)) - 1.0f);
                worstCar = Mathf.Min(worstCar, Mathf.Sqrt(ax * ax + ay * ay));
            }
        }
        for (int a = 0; a < crowd.Count; a++)
            for (int b = a + 1; b < crowd.Count; b++)
            {
                Vector3 d = crowd[a].at - crowd[b].at;
                d.y = 0f;
                worstGap = Mathf.Min(worstGap, d.magnitude);
            }
        Rule("crowd in the lane", worstCar >= 1f && worstGap >= 0.55f && worstLane >= 0f,
            $"{crowd.Count} crew, nearest a parked car {worstCar:0.00} m, nearest each other {worstGap:0.00} m, {worstLane:0.00} m inside the lane's edge");
    }

    static int NearestTrack(PitLane lane, Vector3 w)
    {
        Vector3[] c = lane.Circuit.Centre;
        int best = 0;
        float bd = float.MaxValue;
        for (int k = 0; k < c.Length; k++)
        {
            float d = (c[k] - w).sqrMagnitude;
            if (d < bd) { bd = d; best = k; }
        }
        return best;
    }

    /// <summary>How badly a shot's camera is placed, 0 when it is fine.</summary>
    static float EyeBad(PodiumShot s, IEnumerable<Vector3> people, float laneHeight, out string why)
    {
        string reason = "";
        Vector3 e = s.Eye;
        float F = PodiumPlan.Floor, W = PodiumPlan.HalfWidth;
        float bad = 0f;
        void Worse(float b, string w) { if (b > bad) { bad = b; reason = w; } }
        if (e.z < PodiumPlan.Back + 0.15f && e.y < 18.7f) Worse(PodiumPlan.Back + 0.15f - e.z, "in the building");
        bool overBalcony = Mathf.Abs(e.x) < W + 0.2f && e.z < PodiumPlan.Front + 0.2f;
        if (overBalcony && e.y > F - 0.95f && e.y < F + 0.15f) Worse(0.2f, "in the balcony slab");
        if (Mathf.Abs(e.x) < W + 0.2f && e.z < PodiumPlan.CanopyFront + 0.2f && e.y > F + PodiumPlan.CanopyHeight - 0.5f && e.y < F + PodiumPlan.CanopyHeight + 0.4f) Worse(0.2f, "in the canopy");
        if (overBalcony && e.y > F && e.y < F + PodiumPlan.CanopyHeight && (Mathf.Abs(e.x) > W - 0.25f || e.z > PodiumPlan.Front - 0.25f)) Worse(0.1f, "in the balustrade");
        if (!overBalcony && e.y < laneHeight + 1.0f) Worse(laneHeight + 1.0f - e.y, "under 1 m over the lane");
        foreach (Vector3 p in people)
        {
            // A person as a line from their feet to the top of their head.
            float y = Mathf.Clamp(e.y, p.y, p.y + 1.85f);
            var near = new Vector3(p.x, y, p.z);
            float d = (e - near).magnitude;
            if (d < 0.6f) Worse(0.6f - d, $"{d:0.00} m from someone");
        }
        // The line of sight, past the slab and the canopy.
        for (int k = 1; k < 40; k++)
        {
            Vector3 q = Vector3.Lerp(e, s.Look, k / 40f);
            bool inX = Mathf.Abs(q.x) < W;
            if (inX && q.z > PodiumPlan.Back && q.z < PodiumPlan.Front && q.y > F - 0.75f && q.y < F - 0.02f) { Worse(0.05f, "the balcony slab is in the way"); break; }
            if (inX && q.z > PodiumPlan.Back && q.z < PodiumPlan.CanopyFront && q.y > F + PodiumPlan.CanopyHeight - 0.35f && q.y < F + PodiumPlan.CanopyHeight + 0.22f) { Worse(0.05f, "the canopy is in the way"); break; }
        }
        why = reason;
        return bad;
    }

    // ------------------------------------------------------------------ the preview's data

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static void Preview(string folder, PodiumFrame f, PodiumBuilder pieces, List<CircuitPart> track, PitComplex pc, List<(Vector3 at, int place)> crowd, float laneHeight)
    {
        Directory.CreateDirectory(folder);
        Vector3 origin = f.Origin;
        Vector3 centre = f.At(0f, 0f, 14f);
        var sb = new StringBuilder();
        sb.Append("window.PODIUM={");
        sb.Append("meshes:[");
        bool firstMesh = true;
        void Mesh(string name, MeshKit kit, float cut)
        {
            if (kit.IsEmpty) return;
            var v = kit.Vertices;
            var tri = kit.Triangles;
            var keep = new List<int>();
            for (int i = 0; i < tri.Count; i += 3)
            {
                Vector3 a = v[tri[i]];
                Vector3 d = a - centre;
                d.y = 0f;
                if (d.magnitude < cut) { keep.Add(tri[i]); keep.Add(tri[i + 1]); keep.Add(tri[i + 2]); }
            }
            if (keep.Count == 0) return;
            var map = new Dictionary<int, int>();
            var pos = new StringBuilder();
            var nrm = new StringBuilder();
            var idx = new StringBuilder();
            foreach (int k in keep)
            {
                if (!map.TryGetValue(k, out int m))
                {
                    m = map.Count;
                    map[k] = m;
                    Vector3 p = v[k] - origin, n = kit.Normals[k];
                    pos.Append(F3(p)).Append(',');
                    nrm.Append(F3(n)).Append(',');
                }
                idx.Append(m).Append(',');
            }
            if (!firstMesh) sb.Append(',');
            firstMesh = false;
            sb.Append("{name:\"").Append(name).Append("\",p:[").Append(pos.ToString().TrimEnd(',')).Append("],n:[").Append(nrm.ToString().TrimEnd(','))
              .Append("],i:[").Append(idx.ToString().TrimEnd(',')).Append("]}");
        }
        foreach ((string name, MeshKit kit) in pieces.Parts()) Mesh(name, kit, 1000f);
        foreach (CircuitPart part in new PitComplexBuilder(pc, null).Build()) Mesh(part.Name, part.Kit, 70f);
        foreach (CircuitPart part in track) Mesh(part.Name, part.Kit, 80f);
        sb.Append("],");
        sb.Append("frame:{along:[").Append(F3(f.Along)).Append("],out:[").Append(F3(f.Out)).Append("]},");
        sb.Append("lane:").Append(laneHeight.ToString("0.000", Inv)).Append(',');
        sb.Append("length:").Append(PodiumShow.Length.ToString("0.0", Inv)).Append(',');
        sb.Append("trophy:[").Append(string.Join(",", Enumerable.Range(0, 3).Select(p => PropMesh(PodiumBuilder.TrophyMesh(PodiumShow.TrophyScale(p)).gilt, PodiumBuilder.TrophyMesh(PodiumShow.TrophyScale(p)).plinth)))).Append("],");
        sb.Append("bottle:").Append(PropMesh(PodiumBuilder.BottleMesh().glass, PodiumBuilder.BottleMesh().foil)).Append(',');
        sb.Append("bucket:").Append(PropMesh(PodiumBuilder.BucketStand(), Lift(PodiumBuilder.BucketMesh(), PodiumPlan.BucketTop - 0.22f))).Append(',');
        sb.Append("ceremonies:[");
        int[][] samples = { new[] { 3, 3, 2 }, new[] { 2, 3, 1 } };
        for (int c = 0; c < samples.Length; c++)
        {
            if (c > 0) sb.Append(',');
            Ceremony(sb, new PodiumShow(samples[c], crowd), f, laneHeight);
        }
        sb.Append("]};");
        File.WriteAllText(Path.Combine(folder, "podium.js"), sb.ToString());
        Console.WriteLine("wrote " + Path.Combine(folder, "podium.js"));
    }

    static MeshKit Lift(MeshKit k, float y)
    {
        var o = new MeshKit();
        for (int i = 0; i < k.Triangles.Count; i += 3)
        {
            int a = k.Triangles[i], b = k.Triangles[i + 1], c = k.Triangles[i + 2];
            Vector3 up = Vector3.up * y;
            o.Triangle(k.Vertices[a] + up, k.Vertices[b] + up, k.Vertices[c] + up, k.Normals[a], k.Normals[b], k.Normals[c], k.UVs[a], k.UVs[b], k.UVs[c]);
        }
        return o;
    }

    static string PropMesh(MeshKit a, MeshKit b)
    {
        string One(MeshKit k) => "{p:[" + string.Join(",", k.Vertices.Select(F3)) + "],n:[" + string.Join(",", k.Normals.Select(F3)) + "],i:[" + string.Join(",", k.Triangles) + "]}";
        return "[" + One(a) + "," + One(b) + "]";
    }

    static string F3(Vector3 v) => v.x.ToString("0.###", Inv) + "," + v.y.ToString("0.###", Inv) + "," + v.z.ToString("0.###", Inv);
    static string F(float v) => v.ToString("0.###", Inv);

    static void Ceremony(StringBuilder sb, PodiumShow show, PodiumFrame f, float laneHeight)
    {
        var clips = new List<string>();
        int Clip(string n) { if (n == null) return -1; int i = clips.IndexOf(n); if (i < 0) { clips.Add(n); i = clips.Count - 1; } return i; }
        Vector3 W(Vector3 local) => f.At(local) - f.Origin;
        Vector3 D(Vector3 local) => f.Dir(local);
        var actors = new StringBuilder();
        for (int k = 0; k < show.Actors.Count; k++)
        {
            PodiumActor a = show.Actors[k];
            if (k > 0) actors.Append(',');
            actors.Append("{role:").Append((int)a.Role).Append(",place:").Append(a.Place).Append(",start:").Append(F(a.Role == PodiumRole.Driver ? show.StartOf(a) : -1f)).Append(",size:").Append(F(a.Size)).Append(",f:[");
            bool first = true;
            for (float t = 0f; t <= PodiumShow.Length; t += Dt)
            {
                PodiumPose p = show.Pose(a, t);
                if (!first) actors.Append(',');
                first = false;
                // Root, face; clip, time, under, time, weight; left hand, weight; right hand, weight; left grip, bar; right grip, bar.
                actors.Append(F3(W(p.At))).Append(',').Append(F3(D(p.Face))).Append(',')
                    .Append(Clip(p.Clip)).Append(',').Append(F(p.ClipTime)).Append(',').Append(p.Loop ? 1 : 0).Append(',')
                    .Append(p.UnderWeight > 0f ? Clip(p.Under) : -1).Append(',').Append(F(p.UnderTime)).Append(',').Append(p.UnderLoop ? 1 : 0).Append(',').Append(F(p.UnderWeight)).Append(',')
                    .Append(F3(W(p.LeftHand))).Append(',').Append(F(p.LeftWeight)).Append(',').Append(F3(W(p.RightHand))).Append(',').Append(F(p.RightWeight)).Append(',')
                    .Append(F3(D(p.LeftGrip))).Append(',').Append(F(p.LeftBar)).Append(',').Append(F3(D(p.RightGrip))).Append(',').Append(F(p.RightBar));
            }
            actors.Append("]}");
        }
        var props = new StringBuilder();
        var cams = new StringBuilder();
        bool firstProp = true;
        for (float t = 0f; t <= PodiumShow.Length; t += Dt)
        {
            if (!firstProp) { props.Append(','); cams.Append(','); }
            firstProp = false;
            props.Append('[');
            for (int pl = 0; pl < 3; pl++)
            {
                if (pl > 0) props.Append(',');
                if (show.Drivers[pl] == 0) { props.Append("null"); continue; }
                (Vector3 tb, Vector3 side) = show.Trophy(pl, t);
                (Vector3 bb, Vector3 axis) = show.Bottle(pl, t);
                bool on = show.Spray(pl, t, out Vector3 mouth, out Vector3 aim);
                props.Append('[').Append(F3(W(tb))).Append(',').Append(F3(D(side))).Append(',').Append(F3(W(bb))).Append(',').Append(F3(D(axis))).Append(',')
                    .Append(on ? 1 : 0).Append(',').Append(F3(W(mouth))).Append(',').Append(F3(D(aim))).Append(']');
            }
            props.Append(']');
            PodiumShot s = show.Shot(t, laneHeight);
            cams.Append('[').Append(F3(W(s.Eye))).Append(',').Append(F3(W(s.Look))).Append(',').Append(F(s.Fov)).Append(']');
        }
        sb.Append("{drivers:[").Append(string.Join(",", show.Drivers)).Append("],clips:[").Append(string.Join(",", clips.Select(c => "\"" + c + "\""))).Append("],actors:[")
          .Append(actors).Append("],props:[").Append(props).Append("],cams:[").Append(cams).Append("],buckets:[");
        for (int pl = 0; pl < 3; pl++)
        {
            if (pl > 0) sb.Append(',');
            sb.Append(show.Drivers[pl] == 0 ? "null" : "[" + F3(W(PodiumPlan.Bucket(pl, show.Drivers[pl]))) + "]");
        }
        sb.Append("]}");
    }
}
