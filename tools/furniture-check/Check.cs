// Places and builds the trackside furniture exactly as the game does (TrackFurniture and FurnitureBuilder on the
// circuit CircuitBuilder lays from circuit.json) and runs race control through set pieces. Each rule prints one
// line: ok, or FAIL with details. Exit code 1 if any rule fails. --list prints every post and board. --export
// <file> writes the furniture in each look, with the track round it, for the look preview.
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
//   cheap to draw            at most 120k triangles in all and 20k in any one mesh
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
        Budget(built);
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

    static void Budget(FurnitureBuilder built)
    {
        int total = built.Parts.Sum(p => p.Kit.Triangles.Count / 3) + built.Panels.Sum(p => p.Triangles.Count / 3)
            + (built.RedLenses.Triangles.Count + built.GreenLenses.Triangles.Count) / 3;
        int biggest = built.Parts.Max(p => p.Kit.Triangles.Count / 3);
        var bad = new List<string>();
        if (total > 120000) bad.Add($"{total} triangles");
        if (biggest > 20000) bad.Add($"a mesh of {biggest} triangles");
        Report("cheap to draw", bad, $"{total} triangles in {built.Parts.Count + built.Panels.Count + 2} meshes, {built.Marshals.Count} marshals");
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
