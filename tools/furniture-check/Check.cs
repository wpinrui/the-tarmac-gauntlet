// Places and builds the trackside furniture exactly as the game does (TrackFurniture and FurnitureBuilder on the
// circuit CircuitBuilder lays from circuit.json) and runs race control through set pieces. Each rule prints one
// line: ok, or FAIL with details. Exit code 1 if any rule fails. --list prints every post and board. --export
// <file> writes the furniture in each look, with the track round it, for the look preview. --props-export <file>
// writes the trackside props and everything round them for props-preview.
//
// Rules:
//   posts round the lap      no stretch flagged by one post is longer than 460 m
//   every corner guarded     each corner has a post within 120 m before its turn-in
//   posts beside the track   each post stands behind its runoff, clear of any other road, runoff, pit lane or
//                            grandstand, with its floor within 2.5 m of the road's height
//   panels by the track      each post's flag panel stands past the verge, in front of the barrier, clear of
//                            the distance boards
//   boards before braking    each hard braking corner has its 300, 200 and 100 boards on one side, that far
//                            before the turn-in, off the road and in front of the barrier
//   gantry over the line     the gantry spans the start line with its feet off the road and verge and its legs
//                            clear of the pit wall, and its light box clears the road by at least 5 m
//   armco on posts           every armco run has its rails, through every bend, with a post at least every
//                            4.5 m; rails, posts and catch fences stand behind the armco line, off every road and
//                            runoff, and reach into the ground
//   armco runs whole         no run shorter than 90 m, none within 30 m of the next on its side, and both
//                            ends of every run turn away and bury their rails' top under the ground
//   armco on flat ground     along every run the line climbs no more than 8% and its slope changes by no
//                            more than 4.5% from one post's span to the next; the ground right behind the line
//                            is within 5 cm under it and never reaches the rails' bottom
//   catch fences             behind every tyre wall, clear of the grandstands' own fences and the gantry's feet
//   props off the track      every hoarding, camera tower and cabin stands at least 1 m behind the runoff's edge
//                            and within 13 m of it, off every road, pit lane, paddock and grandstand, and at least
//                            3 m from the marshal posts, boards and gantry
//   props on level ground    the ground under each prop's footprint varies by at most 0.6 m and lies within 3 m
//                            of the road's height, and every leg, tube and plinth reaches into it
//   props clear of trees     no prop stands under a tree's crown in the current landscape look
//   props round the lap      at least 30 hoarding panels, 4 camera towers and 4 cabins
//   cheap to draw            at most 120k triangles in all and 20k in any one mesh, armco included; props at
//                            most 40k, at most 3k in any one cell's mesh
//   race control             SC behind the pace car, red then green lights at the start, green panels on
//                            the opening lap, yellow before a stopped car and green after it, clearing once
//                            it goes, a crashed car flagged until recovered, nothing for a car in the pit lane
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using TarmacGauntlet.Raceday;
using UnityEngine;

static class Check
{
    static bool failed;
    static Circuit c;
    static CircuitBuilder b;
    static List<StandFootprint> footprints;
    static List<CircuitPart> standParts;

    static int Main(string[] args)
    {
        var data = JsonSerializer.Deserialize<CircuitData>(File.ReadAllText(args[0]), new JsonSerializerOptions { IncludeFields = true });
        c = new Circuit(data);
        b = new CircuitBuilder(c, null);
        List<CircuitPart> track = b.Build();
        int at = Array.IndexOf(args, "--export");
        if (at >= 0) Export.Write(args[at + 1], c, b, track);
        var stands = new Grandstands(c, b, null, StandLook.Main, StandLook.Corners);
        standParts = stands.Build();
        footprints = stands.Footprints;
        int pe = Array.IndexOf(args, "--props-export");
        if (pe >= 0) PropsExport.Write(args[pe + 1], c, b, track, standParts, footprints);
        if (args.Contains("--list")) foreach (var s in footprints) Console.WriteLine($"stand {s.Side} {s.From:F0}-{s.To:F0} far {s.Far:F0}");
        var f = new TrackFurniture(c, b, footprints);
        var built = new FurnitureBuilder(c, f, null);
        if (args.Contains("--list"))
        {
            foreach (var p in f.Posts) Console.WriteLine($"post  {p.Distance,7:F0} m {(p.Side == 0 ? "left" : "right")} {Math.Abs(Lateral(p.Base, c.IndexOf(p.Distance))) - c.HalfWidth(c.IndexOf(p.Distance)):F0} m out");
            foreach (var d in f.Boards) Console.WriteLine($"board {d.Distance,7:F0} m {(d.Side == 0 ? "left" : "right")} {d.Metres}");
        }
        Posts(f);
        Boards(f);
        Gantry(f, built, track);
        var armco = new ArmcoBuilder(c, b.ArmcoRuns, footprints, f.GantryLeft, f.GantryRight);
        Armco(armco);
        ArmcoWhole(armco);
        ArmcoGrounded();
        CatchFences(armco, f);
        Budget(built, armco);
        Props(f, armco);
        Flags(f);
        return failed ? 1 : 0;
    }

    static void Report(string rule, List<string> bad, string count)
    {
        if (bad.Count == 0) Console.WriteLine($"ok    {rule}: {count}");
        else
        {
            failed = true;
            Console.WriteLine($"FAIL  {rule}: {string.Join("; ", bad.Take(8))}");
        }
    }

    /// <summary>Half the width of a gantry leg tower, tube to tube outside.</summary>
    static float LegHalf => FurnitureBuilder.LegHalf + 0.05f;

    static float Lateral(Vector3 p, int k) => Vector3.Dot(p - c.Centre[k], c.Left[k]);

    static void Posts(TrackFurniture f)
    {
        var bad = new List<string>();
        float worst = 0f;
        for (int i = 0; i < f.Posts.Count; i++)
        {
            float gap = c.Ahead(f.Posts[i].Distance, f.Posts[(i + 1) % f.Posts.Count].Distance);
            worst = Math.Max(worst, gap);
            if (gap > 460f) bad.Add($"{gap:F0} m from the post at {f.Posts[i].Distance:F0} m");
        }
        Report("posts round the lap", bad, $"{f.Posts.Count} posts, longest stretch {worst:F0} m");

        bad = new List<string>();
        foreach (CornerData corner in c.Data.corners)
            if (!f.Posts.Any(p => c.Ahead(p.Distance, corner.s) <= 120f || c.Ahead(corner.s, p.Distance) <= 30f))
                bad.Add($"T{corner.number} at {corner.s:F0} m");
        Report("every corner guarded", bad, $"{c.Data.corners.Length} corners");

        bad = new List<string>();
        foreach (MarshalPost p in f.Posts)
        {
            int k = c.IndexOf(p.Distance);
            float out_ = Math.Abs(Lateral(p.Base, k)) - c.HalfWidth(k);
            if (out_ < b.RunoffReach(p.Side, k) + 1f) bad.Add($"post at {p.Distance:F0} m stands on its runoff");
            if (Math.Abs(p.Base.y - c.Centre[k].y) > TrackFurniture.MaxDrop) bad.Add($"post at {p.Distance:F0} m is {p.Base.y - c.Centre[k].y:F1} m off the road's height");
            foreach (StandFootprint stand in footprints)
                if (stand.Covers(c.Lap, p.Distance, Lateral(p.Base, k))) bad.Add($"post at {p.Distance:F0} m stands on a grandstand");
            for (int j = 0; j < c.Count; j++)
            {
                if (Math.Abs(c.Wrap(j - k + c.Count / 2) - c.Count / 2) * c.Step <= 60f) continue;
                Vector3 v = p.Base - c.Centre[j];
                if (Math.Abs(v.x * c.Forward[j].x + v.z * c.Forward[j].z) > c.Step) continue;
                float lat = Lateral(p.Base, j);
                if (Math.Abs(lat) < c.HalfWidth(j) + Math.Max(b.RunoffReach(lat > 0 ? 0 : 1, j), 2f) + 2f) { bad.Add($"post at {p.Distance:F0} m stands on the track at {j * c.Step:F0} m"); break; }
            }
        }
        Report("posts beside the track", bad, $"{f.Posts.Count} posts");

        bad = new List<string>();
        foreach (MarshalPost p in f.Posts)
        {
            int k = c.IndexOf(p.Distance);
            float out_ = Math.Abs(Lateral(p.Panel, k)) - c.HalfWidth(k);
            if (out_ < 2.5f || out_ > b.RunoffReach(p.Side, k) - 0.5f) bad.Add($"panel at {p.Distance:F0} m stands {out_:F1} m off the road, barrier at {b.RunoffReach(p.Side, k):F1} m");
            foreach (DistanceBoard d in f.Boards)
                if ((d.Base - p.Panel).magnitude < TrackFurniture.BoardRoom) bad.Add($"panel at {p.Distance:F0} m crowds the {d.Metres} board");
        }
        Report("panels by the track", bad, $"{f.Posts.Count} panels");
    }

    static void Boards(TrackFurniture f)
    {
        var bad = new List<string>();
        int corners = 0;
        CornerData[] all = c.Data.corners;
        for (int i = 0; i < all.Length; i++)
        {
            CornerData corner = all[i], before = all[(i + all.Length - 1) % all.Length];
            if (corner.radius > TrackFurniture.BrakingRadius || c.Ahead(before.e, corner.s) < TrackFurniture.BrakingRunUp) continue;
            corners++;
            var mine = f.Boards.Where(d => c.Ahead(d.Distance, corner.s) <= 301f).ToList();
            foreach (int m in TrackFurniture.BoardMetres)
            {
                var board = mine.FirstOrDefault(d => d.Metres == m);
                if (board == null) { bad.Add($"T{corner.number} has no {m} board"); continue; }
                if (Math.Abs(c.Ahead(board.Distance, corner.s) - m) > 0.5f) bad.Add($"T{corner.number}'s {m} board is {c.Ahead(board.Distance, corner.s):F0} m out");
                int k = c.IndexOf(board.Distance);
                float out_ = Math.Abs(Lateral(board.Base, k)) - c.HalfWidth(k);
                if (out_ < 2.5f || out_ > b.RunoffReach(board.Side, k) - 0.5f) bad.Add($"T{corner.number}'s {m} board stands {out_:F1} m off the road, barrier at {b.RunoffReach(board.Side, k):F1} m");
            }
            if (mine.Select(d => d.Side).Distinct().Count() > 1) bad.Add($"T{corner.number}'s boards are on both sides");
        }
        Report("boards before braking", bad, $"{f.Boards.Count} boards before {corners} corners");
    }

    static void Gantry(TrackFurniture f, FurnitureBuilder built, List<CircuitPart> track)
    {
        var bad = new List<string>();
        var wall = new List<float>();
        int k = f.GantrySample;
        float l = Lateral(f.GantryLeft, k), r = Lateral(f.GantryRight, k);
        float edge = c.HalfWidth(k) + 2f;
        if (l < edge || -r < edge) bad.Add($"feet at {l:F1} m and {r:F1} m, road and verge out to {edge:F1} m");
        float road = c.Centre[k].y;
        float lowest = built.RedLenses.Vertices.Concat(built.GreenLenses.Vertices).Min(v => v.y) - road;
        if (lowest < 5f) bad.Add($"lights {lowest:F1} m over the road");
        foreach (CircuitPart part in standParts)
            foreach (Vector3 v in part.Kit.Vertices)
            {
                if (Mathf.Abs(Vector3.Dot(v - c.Centre[k], c.Forward[k])) > LegHalf + 0.1f) continue;
                if (v.y > c.Centre[k].y + TrackFurniture.GantryClearance) continue;
                float lat = Lateral(v, k);
                foreach (float foot in new[] { l, r })
                    if (Mathf.Abs(lat - foot) < LegHalf) { bad.Add($"a leg meets the {part.Name} at {lat:F1} m"); break; }
            }
        foreach (CircuitPart part in track.Where(p => p.Name.StartsWith("Pit wall")))
            foreach (Vector3 v in part.Kit.Vertices)
                if (Mathf.Abs(Vector3.Dot(v - c.Centre[k], c.Forward[k])) < 3f) wall.Add(Lateral(v, k));
        if (wall.Count > 0)
        {
            float wMin = wall.Min(), wMax = wall.Max();
            foreach (float foot in new[] { l, r })
            {
                float near = foot - LegHalf, far = foot + LegHalf;
                bool inside = near >= wMin - 0.01f && far <= wMax + 0.01f;
                if (!inside && near < wMax && far > wMin) bad.Add($"a leg from {near:F2} m to {far:F2} m cuts the pit wall from {wMin:F2} m to {wMax:F2} m");
            }
        }
        Report("gantry over the line", bad, $"span {l - r:F1} m, lowest light {lowest:F1} m over the road");
    }

    static void Budget(FurnitureBuilder built, ArmcoBuilder armco)
    {
        var armcoChunks = new[] { armco.Steel, armco.Fence, armco.FencePosts }.SelectMany(k => k.Indexed().Chunks(250f)).ToList();
        int total = built.Parts.Sum(p => p.Kit.Triangles.Count / 3) + built.Panels.Sum(p => p.Triangles.Count / 3)
            + (built.RedLenses.Triangles.Count + built.GreenLenses.Triangles.Count) / 3 + armcoChunks.Sum(k => k.Triangles.Count / 3);
        int biggest = built.Parts.Select(p => p.Kit).Concat(armcoChunks).Max(k => k.Triangles.Count / 3);
        var bad = new List<string>();
        if (total > 120000) bad.Add($"{total} triangles");
        if (biggest > 20000) bad.Add($"a mesh of {biggest} triangles");
        Report("cheap to draw", bad, $"{total} triangles in {built.Parts.Count + built.Panels.Count + 2} meshes, {built.Marshals.Count} marshals");
    }

    static void Props(TrackFurniture f, ArmcoBuilder armco)
    {
        var props = new TracksideProps(c, b, footprints, f, armco.Ends, new LandscapePlan(c, b.RunoffReach, LandscapeLooks.Current, footprints).Trees);
        var built = new PropBuilder(c, props, null);
        var parts = built.Cells.SelectMany(cell => cell.Coarse.Concat(cell.Fine)).ToList();
        if (Environment.GetCommandLineArgs().Contains("--list"))
            foreach (PropSpot s in props.Spots) Console.WriteLine($"prop  {s.Kind,-11} {s.Distance,7:F0} m {(s.Side == 0 ? "left" : "right")} {Where(s.Base)} advert {s.Variant}");

        // Off the track: every vertex of every prop.
        var bad = new List<string>();
        float nearest = float.MaxValue, furthest = 0f;
        var furniture = f.Posts.Select(p => p.Base).Concat(f.Posts.Select(p => p.Panel)).Concat(f.Boards.Select(d => d.Base)).Concat(new[] { f.GantryLeft, f.GantryRight }).ToList();
        foreach (PropSpot s in props.Spots)
        {
            float front = float.MinValue, back = float.MaxValue;
            foreach (CircuitPart part in parts)
                foreach (Vector3 v in part.Kit.Vertices)
                {
                    if (Flat(v - s.Base) > 6f) continue;
                    float on = OnRunoff(v);
                    front = Math.Max(front, on);
                    back = Math.Min(back, on);
                }
            nearest = Math.Min(nearest, -front);
            furthest = Math.Max(furthest, -back);
            if (front > -1f) bad.Add($"{s.Kind} at {Where(s.Base)} reaches {front + 1f:F1} m too near the runoff");
            if (-back > TracksideProps.TreeFree) bad.Add($"{s.Kind} at {Where(s.Base)} stands {-back:F1} m behind the runoff");
            foreach (StandFootprint stand in footprints)
            {
                int k = Nearest(s.Base);
                if (stand.Covers(c.Lap, k * c.Step, Lateral(s.Base, k))) bad.Add($"{s.Kind} at {Where(s.Base)} stands on a grandstand");
            }
            float lane = c.Data.pit.laneWidth * 0.5f + c.Data.pit.garageDepth;
            if (c.Lane.Any(q => Flat(q - s.Base) < lane)) bad.Add($"{s.Kind} at {Where(s.Base)} stands on the pit lane");
            if (c.Data.pit.garages.Any(g => Flat(new Vector3(g.x, 0, g.z) - s.Base) < 100f)) bad.Add($"{s.Kind} at {Where(s.Base)} stands in the paddock");
            float r = s.Kind == PropKind.Cabin ? 3.4f : s.Kind == PropKind.CameraTower ? 1.8f : 2.2f;
            foreach (Vector3 q in furniture)
                if (Flat(q - s.Base) < r + 3f) bad.Add($"{s.Kind} at {Where(s.Base)} crowds the furniture at {Where(q)}");
        }
        for (int i = 0; i < props.Spots.Count; i++)
            for (int j = i + 1; j < props.Spots.Count; j++)
            {
                PropSpot p = props.Spots[i], q = props.Spots[j];
                float min = p.Kind == PropKind.Hoarding && q.Kind == PropKind.Hoarding ? TracksideProps.HoardingWidth : 6f;
                if (Flat(p.Base - q.Base) < min) bad.Add($"{p.Kind} and {q.Kind} overlap at {Where(p.Base)}");
            }
        Report("props off the track", bad, $"{props.Spots.Count} props, {nearest:F1} to {furthest:F1} m behind the runoff");

        // Level ground, and grounded.
        bad = new List<string>();
        float worst = 0f;
        foreach (PropSpot s in props.Spots)
        {
            (float hl, float hd) = s.Kind == PropKind.Cabin ? (TracksideProps.CabinHalfLength, TracksideProps.CabinHalfDepth)
                : s.Kind == PropKind.CameraTower ? (TracksideProps.TowerHalf, TracksideProps.TowerHalf) : (TracksideProps.HoardingWidth / 2f, 0.9f);
            float low = float.MaxValue, high = float.MinValue;
            for (int i = -4; i <= 4; i++)
                for (int j = -2; j <= 2; j++)
                {
                    Vector3 p = s.Base + s.Along * (hl * i / 4f) + s.ToRoad * (hd * j / 2f);
                    float h = c.TerrainHeight(p.x, p.z);
                    low = Math.Min(low, h);
                    high = Math.Max(high, h);
                }
            worst = Math.Max(worst, high - low);
            if (high - low > 0.6f) bad.Add($"{s.Kind} at {Where(s.Base)} on ground varying {high - low:F2} m");
            int k = c.IndexOf(s.Distance);
            if (Math.Abs(low - c.Centre[k].y) > 3f || Math.Abs(high - c.Centre[k].y) > 3f) bad.Add($"{s.Kind} at {Where(s.Base)} sits {low - c.Centre[k].y:F1} m off the road's height");
            if (s.High < high - 0.05f) bad.Add($"{s.Kind} at {Where(s.Base)} has its floor {high - s.High:F2} m under the ground");
        }
        // Every upright's foot is under the ground beside it: no vertex of a prop hangs below its lowest part in the air.
        foreach (PropCell cell in built.Cells)
            foreach (CircuitPart part in cell.Coarse)
            {
                var v = part.Kit.Vertices;
                var t = part.Kit.Triangles;
                // The lowest point of each prop's parts within 3 m in plan must be at or under the ground there.
                foreach (PropSpot s in props.Spots)
                {
                    if (Flat(s.Base - cell.Centre) > PropBuilder.CellSize) continue;
                    float lowest = float.MaxValue;
                    Vector3 at = default;
                    foreach (Vector3 p in v)
                        if (Flat(p - s.Base) < 3f && p.y < lowest) { lowest = p.y; at = p; }
                    if (lowest == float.MaxValue) continue;
                    float g = c.TerrainHeight(at.x, at.z);
                    if (lowest > g + 0.02f && part.Name != "Hoarding backs" && part.Name != "Hoardings" && !part.Name.EndsWith("roofs") && !part.Name.EndsWith("decks") && part.Name != "Cabin walls")
                        bad.Add($"{part.Name} at {Where(s.Base)} stands {lowest - g:F2} m off the ground");
                }
            }
        Report("props on level ground", bad, $"ground under a prop varies at most {worst:F2} m");

        // Trees, in every look.
        bad = new List<string>();
        int trees = 0;
        foreach (var (name, look) in new[] { ("the current look", LandscapeLooks.Current) })
        {
            var plan = new LandscapePlan(c, b.RunoffReach, look, footprints);
            foreach (TreeSpot t in plan.Trees)
            {
                float crown = LandscapePlan.ModelRadius[(int)t.Kind] * t.Scale;
                foreach (PropSpot s in props.Spots)
                {
                    float r = s.Kind == PropKind.Cabin ? 3.5f : s.Kind == PropKind.CameraTower ? 1.8f : 2.2f;
                    if (Flat(t.Base - s.Base) < crown + r) { bad.Add($"{s.Kind} at {Where(s.Base)} under a {t.Kind} in {name}"); trees++; }
                }
            }
        }
        Report("props clear of trees", bad, "the current landscape look");

        bad = new List<string>();
        int panels = props.Spots.Count(s => s.Kind == PropKind.Hoarding), towers = props.Spots.Count(s => s.Kind == PropKind.CameraTower), cabins = props.Spots.Count(s => s.Kind == PropKind.Cabin);
        if (panels < 30) bad.Add($"only {panels} hoarding panels");
        if (towers < 4) bad.Add($"only {towers} camera towers");
        if (cabins < 4) bad.Add($"only {cabins} cabins");
        Report("props round the lap", bad, $"{panels} hoarding panels, {towers} camera towers, {cabins} cabins, {built.Operators.Count} camera operators in {built.Cells.Count} cells");

        bad = new List<string>();
        int total = parts.Sum(p => p.Kit.Triangles.Count / 3), biggest = parts.Count == 0 ? 0 : parts.Max(p => p.Kit.Triangles.Count / 3);
        if (total > 40000) bad.Add($"{total} triangles");
        if (biggest > 3000) bad.Add($"a cell's mesh of {biggest} triangles");
        int coarse = built.Cells.Sum(cell => cell.Coarse.Sum(p => p.Kit.Triangles.Count / 3));
        Report("props cheap to draw", bad, $"{total} triangles near, {coarse} far, in {parts.Count} meshes, biggest {biggest}");
    }

    /// <summary>The nearest track sample to a point, in plan.</summary>
    static int Nearest(Vector3 p)
    {
        int best = 0;
        float bestD = float.MaxValue;
        for (int k = 0; k < c.Count; k++)
        {
            float dx = c.Centre[k].x - p.x, dz = c.Centre[k].z - p.z, d = dx * dx + dz * dz;
            if (d < bestD) { bestD = d; best = k; }
        }
        return best;
    }

    /// <summary>
    /// How far a point stands in front of the runoff's outer edge (the armco line where there is armco) beside the nearest
    /// stretch of track: above 0 is on the runoff. The edge is taken as straight between track samples, as laid.
    /// </summary>
    static float OnRunoff(Vector3 p)
    {
        int k = Nearest(p);
        int side = Lateral(p, k) > 0 ? 0 : 1;
        float sign = side == 0 ? 1f : -1f;
        if (b.RunoffReach(side, k) <= 0f) return c.HalfWidth(k) - Math.Abs(Lateral(p, k));
        Vector3 Edge(int j) => c.Centre[j] + c.Left[j] * (sign * (c.HalfWidth(j) + b.RunoffReach(side, j)));
        float best = float.MaxValue, front = 0f;
        for (int d = -2; d <= 1; d++)
        {
            int j0 = c.Wrap(k + d), j1 = c.Wrap(k + d + 1);
            Vector3 a = Edge(j0), e = Edge(j1), ab = e - a;
            ab.y = 0;
            Vector3 ap = p - a;
            ap.y = 0;
            float t = Mathf.Clamp01(Vector3.Dot(ap, ab) / Math.Max(ab.sqrMagnitude, 1e-6f));
            Vector3 off = ap - ab * t;
            if (off.magnitude >= best) continue;
            best = off.magnitude;
            // Towards the road is the side of the edge the centreline lies on.
            Vector3 perp = Vector3.Cross(Vector3.up, ab).normalized;
            Vector3 toCentre = c.Centre[j0] - a;
            toCentre.y = 0;
            if (Vector3.Dot(perp, toCentre) < 0) perp = -perp;
            front = Vector3.Dot(ap, perp);
        }
        return front;
    }

    static void Armco(ArmcoBuilder armco)
    {
        var bad = new List<string>();
        if (armco.Rails.Count != b.ArmcoRuns.Count) bad.Add($"{b.ArmcoRuns.Count} armco runs but {armco.Rails.Count} with rails");
        float length = 0f;
        foreach (ArmcoRun run in b.ArmcoRuns)
            for (int i = 1; i < run.Line.Length; i++) length += Flat(run.Line[i] - run.Line[i - 1]);
        if (armco.Posts.Count * 4.5f < length) bad.Add($"{armco.Posts.Count} posts along {length:F0} m");
        // Every bend of the line is kept: no point of it lies further than the tolerance off the rails.
        for (int r = 0; r < Math.Min(armco.Rails.Count, b.ArmcoRuns.Count); r++)
        {
            Vector3[] rail = armco.Rails[r];
            foreach (Vector3 p in b.ArmcoRuns[r].Line)
            {
                float off = float.MaxValue;
                for (int i = 0; i + 1 < rail.Length; i++) off = Math.Min(off, ToSegment(p, rail[i], rail[i + 1]));
                if (off > ArmcoBuilder.Tolerance + 0.005f) { bad.Add($"rails {off:F2} m off the armco line at {Where(p)}"); break; }
            }
        }
        // Nothing of the steel on the runoff, sampled every few vertices; and the posts reach into the ground.
        foreach (MeshKit kit in new[] { armco.Steel, armco.Fence, armco.FencePosts })
        {
            IReadOnlyList<Vector3> v = kit.Vertices;
            for (int i = 0; i < v.Count; i += 7)
            {
                float over = OnRunoff(v[i]);
                if (over > 0.02f) { bad.Add($"steel {over:F2} m onto the runoff at {Where(v[i])}"); break; }
            }
        }
        foreach (Vector3 foot in armco.Posts)
            if (foot.y - ArmcoBuilder.Footing > c.TerrainHeight(foot.x, foot.z) - 0.1f) { bad.Add($"post off the ground at {Where(foot)}"); break; }
        Report("armco on posts", bad, $"{b.ArmcoRuns.Count} runs, {length / 1000f:F1} km, {armco.Posts.Count} posts, {armco.Steel.Triangles.Count / 3} triangles");
    }

    static void ArmcoWhole(ArmcoBuilder armco)
    {
        var bad = new List<string>();
        float shortest = float.MaxValue, closest = float.MaxValue;
        foreach (ArmcoRun run in b.ArmcoRuns)
        {
            float len = 0f;
            for (int i = 1; i < run.Line.Length; i++) len += Flat(run.Line[i] - run.Line[i - 1]);
            shortest = Math.Min(shortest, len);
            if (len < 90f) bad.Add($"{len:F0} m run at {Where(run.Line[0])}");
            foreach (ArmcoRun other in b.ArmcoRuns)
            {
                if (other == run || other.Side != run.Side) continue;
                float gap = Mathf.Repeat((other.Samples[0] - run.Samples[run.Samples.Length - 1]) * c.Step, c.Lap);
                if (gap > c.Lap * 0.5f) continue;
                closest = Math.Min(closest, gap);
                if (gap < 30f) bad.Add($"runs {gap:F0} m apart at {Where(run.Line[run.Line.Length - 1])}");
            }
        }
        if (armco.Ends.Count != 2 * b.ArmcoRuns.Count) bad.Add($"{armco.Ends.Count} ends for {b.ArmcoRuns.Count} runs");
        foreach (Vector3[] end in armco.Ends)
        {
            Vector3 tip = end[end.Length - 1];
            float over = tip.y - c.TerrainHeight(tip.x, tip.z);
            if (over > -0.05f) bad.Add($"rail end {over:F2} m out of the ground at {Where(tip)}");
            for (int i = 1; i < end.Length; i++)
                if (end[i].y - c.TerrainHeight(end[i].x, end[i].z) > end[i - 1].y - c.TerrainHeight(end[i - 1].x, end[i - 1].z) + 0.02f)
                { bad.Add($"rail end rising out of the ground at {Where(end[i])}"); break; }
            if (Flat(end[end.Length - 1] - end[0]) < ArmcoBuilder.TerminalLength * 0.8f) bad.Add($"rail end too short at {Where(tip)}");
        }
        Report("armco runs whole", bad, $"{b.ArmcoRuns.Count} runs, shortest {shortest:F0} m, closest {(closest == float.MaxValue ? 0 : closest):F0} m apart, {armco.Ends.Count} buried ends");
    }

    static void ArmcoGrounded()
    {
        var bad = new List<string>();
        float steepest = 0f, sharpest = 0f, deepest = 0f, highest = 0f;
        foreach (ArmcoRun run in b.ArmcoRuns)
        {
            Vector3[] l = run.Line;
            for (int i = 2; i + 2 < l.Length; i++)
            {
                float s1 = (l[i].y - l[i - 2].y) / Math.Max(0.1f, Flat(l[i] - l[i - 2]));
                float s2 = (l[i + 2].y - l[i].y) / Math.Max(0.1f, Flat(l[i + 2] - l[i]));
                steepest = Math.Max(steepest, Math.Abs(s1));
                sharpest = Math.Max(sharpest, Math.Abs(s2 - s1));
                if (Math.Abs(s1) > 0.08f) { bad.Add($"armco climbing {s1 * 100:F0}% at {Where(l[i])}"); break; }
                if (Math.Abs(s2 - s1) > 0.045f) { bad.Add($"armco bending {Math.Abs(s2 - s1) * 100:F1}% at {Where(l[i])}"); break; }
            }
            for (int i = 0; i < l.Length; i++)
            {
                Vector3 back = l[i] - c.Centre[run.Samples[i]];
                back.y = 0f;
                back.Normalize();
                Vector3 q = l[i] + back * (ArmcoBuilder.RailDepth + ArmcoBuilder.PostDepth);
                float rise = c.TerrainHeight(q.x, q.z) - l[i].y;
                deepest = Math.Max(deepest, rise);
                highest = Math.Max(highest, -rise);
                if (rise > ArmcoBuilder.RailBottom - 0.03f) { bad.Add($"ground {rise:F2} m up into the rails at {Where(l[i])}"); break; }
                if (rise < -0.05f) { bad.Add($"rails {-rise:F2} m over the ground at {Where(l[i])}"); break; }
            }
        }
        Report("armco on flat ground", bad, $"steepest {steepest * 100:F1}%, sharpest bend {sharpest * 100:F1}%, ground behind from {highest:F2} m under to {deepest:F2} m over the line");
    }

    static void CatchFences(ArmcoBuilder armco, TrackFurniture f)
    {
        var bad = new List<string>();
        // Every tyre wall has fencing behind it, except where a grandstand's own fence or the gantry stands.
        foreach (ArmcoRun run in b.ArmcoRuns)
            for (int i = 0; i < run.Line.Length; i++)
            {
                if (!run.Tyres[i]) continue;
                int k = run.Samples[i];
                if (InStand(run.Side, k * c.Step, ArmcoBuilder.StandGap + 1f)) continue;
                if (Flat(run.Line[i] - f.GantryLeft) < ArmcoBuilder.GantryGap + 2f || Flat(run.Line[i] - f.GantryRight) < ArmcoBuilder.GantryGap + 2f) continue;
                bool fenced = armco.Fences.Any(fc => fc.side == run.Side && fc.line.Any(q => Flat(q - run.Line[i]) < ArmcoBuilder.FenceBack + ArmcoBuilder.FencePostSpacing));
                if (!fenced) { bad.Add($"tyre wall unfenced at {Where(run.Line[i])}"); break; }
            }
        int points = 0;
        foreach (var (side, line) in armco.Fences)
            foreach (Vector3 p in line)
            {
                points++;
                int k = Nearest(p);
                if (InStand(side, k * c.Step, 0f)) bad.Add($"fence in a grandstand's at {Where(p)}");
                if (Flat(p - f.GantryLeft) < ArmcoBuilder.GantryGap - 0.5f || Flat(p - f.GantryRight) < ArmcoBuilder.GantryGap - 0.5f) bad.Add($"fence by the gantry at {Where(p)}");
                if (Math.Abs(p.y - c.TerrainHeight(p.x, p.z)) > 0.05f) bad.Add($"fence off the ground at {Where(p)}");
            }
        Report("catch fences", bad, $"{armco.Fences.Count} fences, {points} posts");
    }

    static bool InStand(int side, float d, float margin)
    {
        foreach (StandFootprint s in footprints)
            if (s.Side == side && Mathf.Repeat(d - (s.From - margin), c.Lap) <= Mathf.Repeat(s.To + margin - (s.From - margin), c.Lap)) return true;
        return false;
    }

    static float Flat(Vector3 v) => (float)Math.Sqrt(v.x * v.x + v.z * v.z);

    static float ToSegment(Vector3 p, Vector3 a, Vector3 bb)
    {
        Vector3 ab = bb - a;
        float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Math.Max(ab.sqrMagnitude, 1e-6f));
        return (a + ab * t - p).magnitude;
    }

    static string Where(Vector3 p)
    {
        int k = Nearest(p);
        float lat = Lateral(p, k);
        return $"lap {k * c.Step:F0} m {Math.Abs(lat):F0} m {(lat > 0 ? "left" : "right")}";
    }

    static void Flags(TrackFurniture f)
    {
        var bad = new List<string>();
        var flags = new RaceFlags(c.Lap, f.Posts.Select(p => p.Distance).ToList());
        var cars = new List<RaceFlags.Car>();
        for (int i = 0; i < 20; i++) cars.Add(new RaceFlags.Car { Distance = Mathf.Repeat(-10f * i, c.Lap), Speed = 30f, OnTrack = true });
        void Run(float seconds, bool pace, bool racing) { for (float t = 0; t < seconds; t += 0.05f) flags.Tick(0.05f, pace, racing, cars); }
        bool All(RaceFlags.Panel p) => flags.Panels.All(x => x == p);

        Run(10f, true, false);
        if (!All(RaceFlags.Panel.SafetyCar)) bad.Add("not SC behind the pace car");
        if (flags.StartLights != RaceFlags.Lights.Red) bad.Add("lights not red behind the pace car");
        Run(1f, false, true);
        if (flags.StartLights != RaceFlags.Lights.Green) bad.Add("lights not green at the start");
        if (!All(RaceFlags.Panel.Green)) bad.Add("panels not green on the opening lap");
        Run(RaceFlags.StartGreen, false, true);
        if (flags.StartLights != RaceFlags.Lights.Off) bad.Add("lights still on after the start");
        if (!All(RaceFlags.Panel.Off)) bad.Add("panels still lit after the opening lap");

        // A car stops halfway between two posts.
        int post = f.Posts.Count / 2;
        float at = f.Posts[post].Distance + c.Ahead(f.Posts[post].Distance, f.Posts[post + 1].Distance) / 2;
        cars[5] = new RaceFlags.Car { Distance = at, Speed = 0f, OnTrack = true };
        Run(1f, false, true);
        if (!All(RaceFlags.Panel.Off)) bad.Add("yellow before the car had stopped for long");
        Run(2f, false, true);
        if (flags.Panels[post] != RaceFlags.Panel.Yellow) bad.Add("no yellow at the post before a stopped car");
        if (flags.Panels[post + 1] != RaceFlags.Panel.Green) bad.Add("no green at the post after a stopped car");
        if (flags.Panels.Count(p => p != RaceFlags.Panel.Off) != 2) bad.Add("panels lit away from the stopped car");
        cars[5] = new RaceFlags.Car { Distance = at, Speed = 30f, OnTrack = true };
        Run(RaceFlags.YellowHold - 1f, false, true);
        if (flags.Panels[post] != RaceFlags.Panel.Yellow) bad.Add("yellow cleared the moment the car moved");
        Run(2f, false, true);
        if (!All(RaceFlags.Panel.Off)) bad.Add("yellow stayed out after the car got going");

        // A car stopped in its pit box is not flagged.
        cars[6] = new RaceFlags.Car { Distance = 10f, Speed = 0f, OnTrack = false };
        Run(10f, false, true);
        if (!All(RaceFlags.Panel.Off)) bad.Add("yellow for a car in the pit lane");

        // A crashed car parked by the track is flagged until it is recovered.
        cars[7] = new RaceFlags.Car { Distance = at, Speed = 0f, OnTrack = true, Parked = true };
        Run(60f, false, true);
        if (flags.Panels[post] != RaceFlags.Panel.Yellow) bad.Add("no yellow for a crashed car");
        Run(RaceFlags.RecoveryTime + RaceFlags.YellowHold, false, true);
        if (!All(RaceFlags.Panel.Off)) bad.Add("crashed car flagged after its recovery");
        Report("race control", bad, "start, opening lap, stopped car, pit lane, crash");
    }
}
