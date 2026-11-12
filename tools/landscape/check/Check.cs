using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using TarmacGauntlet.Raceday;
using UnityEngine;

/// <summary>
/// Lays out the landscape for each look with the game's own code, checks that nothing stands where it
/// must not, prints what it costs, and with --out writes it for the preview (tools/landscape/preview.html).
/// </summary>
public static class Check
{
    private static int failed;

    public static int Main(string[] args)
    {
        var data = JsonSerializer.Deserialize<CircuitData>(File.ReadAllText(args[0]), new JsonSerializerOptions { IncludeFields = true });
        var circuit = new Circuit(data);
        var builder = new CircuitBuilder(circuit, null);
        builder.Build();
        int o = Array.IndexOf(args, "--out");
        string outDir = o >= 0 ? args[o + 1] : null;
        var looks = new Dictionary<string, LandscapeLook>
        {
            ["woodland"] = LandscapeLooks.Woodland, ["farmland"] = LandscapeLooks.Farmland, ["highland"] = LandscapeLooks.Highland,
        };
        var stands = new Grandstands(circuit, builder, null, StandLook.Main, StandLook.Corners);
        stands.Build();
        foreach (var (name, look) in looks)
        {
            var watch = Stopwatch.StartNew();
            var plan = new LandscapePlan(circuit, builder.RunoffReach, look, stands.Footprints);
            var horizon = new Horizon(circuit, look);
            long ms = watch.ElapsedMilliseconds;
            var counts = new int[8];
            foreach (TreeSpot t in plan.Trees) counts[(int)t.Kind]++;
            Console.WriteLine($"{name}: {plan.Trees.Count} trees and bushes in {ms} ms (" + string.Join(", ", Enum.GetNames(typeof(TreeKind)).Select((k, i) => $"{k} {counts[i]}")) + ")");
            Rules(name, circuit, builder, plan, stands);
            Stands(name, circuit, plan, stands);
            Cost(name, plan);
            if (outDir != null) Write(Path.Combine(outDir, name), circuit, plan, horizon);
        }
        return failed > 0 ? 1 : 0;
    }

    private static void Rules(string name, Circuit c, CircuitBuilder b, LandscapePlan plan, Grandstands stands)
    {
        // Brute force over every track sample, separately from the plan's own bucketed search.
        float worst = float.MaxValue;
        Vector3 at = default;
        int paddock = 0, forward = 0, inside = 0;
        string insideAt = "";
        float pitSide = c.Data.pit.side == "right" ? -1f : 1f;
        foreach (TreeSpot t in plan.Trees)
        {
            float best = float.MaxValue, side = 1f, roadBest = float.MaxValue;
            int near = 0;
            for (int k = 0; k < c.Count; k++)
            {
                float dx = t.Base.x - c.Centre[k].x, dz = t.Base.z - c.Centre[k].z;
                float lat = dx * c.Left[k].x + dz * c.Left[k].z;
                float r = (float)Math.Sqrt(dx * dx + dz * dz) - c.HalfWidth(k);
                float e = r - b.RunoffReach(lat >= 0 ? 0 : 1, k);
                if (e < best) { best = e; side = lat >= 0 ? 1f : -1f; }
                if (r < roadBest) { roadBest = r; near = k; }
            }
            if (best < worst) { worst = best; at = t.Base; }
            if (best < LandscapePlan.KeepClear - 0.01f)
            {
                forward++;
                // On the inside of a bend, measured the same way as the plan, from the nearest stretch of road.
                int n = Math.Max(1, (int)Math.Round(80f / c.Step));
                Vector3 mid = (c.Centre[c.Wrap(near - n)] + c.Centre[c.Wrap(near + n)]) * 0.5f - c.Centre[near];
                float inward = mid.x * c.Left[near].x + mid.z * c.Left[near].z, half = n * c.Step;
                float lat = (t.Base.x - c.Centre[near].x) * c.Left[near].x + (t.Base.z - c.Centre[near].z) * c.Left[near].z;
                if (Math.Abs(inward) > 1e-3f && half * half / (2f * Math.Abs(inward)) < LandscapePlan.InsideRadius - 1f && lat * inward > 0f)
                {
                    inside++;
                    insideAt = $"{t.Base.x:0} {t.Base.z:0}";
                }
            }
            if (side == pitSide)
                foreach (GarageData g in c.Data.pit.garages)
                    if (Math.Sqrt((g.x - t.Base.x) * (g.x - t.Base.x) + (g.z - t.Base.z) * (g.z - t.Base.z)) < LandscapePlan.PaddockClear - 0.01) { paddock++; break; }
            if (float.IsNaN(t.Base.y) || t.Scale <= 0f) Expect($"{name}: tree sizes are sane", false, $"{t.Base.x:0} {t.Base.z:0}");
        }
        Console.WriteLine($"  {forward} trees and bushes stand forward, within {LandscapePlan.KeepClear} m of the armco");
        Expect($"{name}: nothing within {LandscapePlan.NearClear} m behind the armco", worst >= LandscapePlan.NearClear - 0.01f, $"nearest {worst:0.0} m at {at.x:0} {at.z:0}");
        Expect($"{name}: none within {LandscapePlan.KeepClear} m on the inside of a tight bend", inside == 0, $"{inside} trees, last at {insideAt}");
        Expect($"{name}: nothing in the paddock", paddock == 0, $"{paddock} trees");
        Expect($"{name}: same look gives the same trees", new LandscapePlan(c, b.RunoffReach, LandscapeLooks.Current, stands.Footprints).Trees.Count == new LandscapePlan(c, b.RunoffReach, LandscapeLooks.Current, stands.Footprints).Trees.Count, "");
    }

    /// <summary>No tree's crown reaches over a grandstand's footprint (Grandstands.Footprints).</summary>
    private static void Stands(string name, Circuit c, LandscapePlan plan, Grandstands stands)
    {
        int bad = 0;
        string where = "";
        foreach (TreeSpot t in plan.Trees)
        {
            int best = 0;
            float bd = float.MaxValue;
            for (int k = 0; k < c.Count; k += 1)
            {
                float dx = t.Base.x - c.Centre[k].x, dz = t.Base.z - c.Centre[k].z, d = dx * dx + dz * dz;
                if (d < bd) { bd = d; best = k; }
            }
            float lx = t.Base.x - c.Centre[best].x, lz = t.Base.z - c.Centre[best].z;
            float lateral = lx * c.Left[best].x + lz * c.Left[best].z;
            float crown = LandscapePlan.ModelRadius[(int)t.Kind] * t.Scale * 0.5f;
            float inward = lateral - Math.Sign(lateral) * crown;   // the crown's edge nearest the track
            foreach (StandFootprint f in stands.Footprints)
                if (f.Covers(c.Lap, best * c.Step, inward)) { bad++; where = $"{t.Base.x:0} {t.Base.z:0}"; break; }
        }
        Expect($"{name}: no tree over a grandstand ({stands.Footprints.Count} stands)", bad == 0, $"{bad} trees, last at {where}");
    }

    /// <summary>Quads drawn and chunks (one draw call each, plus shadow cascades for the near ones).</summary>
    private static void Cost(string name, LandscapePlan plan)
    {
        var chunks = new Dictionary<long, int>();
        foreach (TreeSpot t in plan.Trees)
        {
            long key = ((long)Mathf.FloorToInt(t.Base.x / LandscapePlan.ChunkSize) << 32) ^ (uint)Mathf.FloorToInt(t.Base.z / LandscapePlan.ChunkSize);
            chunks[key] = chunks.TryGetValue(key, out int n) ? n + 1 : 1;
        }
        int most = 0;
        foreach (int n in chunks.Values) most = Math.Max(most, n);
        Console.WriteLine($"  {chunks.Count} chunks, busiest {most} quads; {plan.Trees.Count * 2} triangles if every tree drew");
        Expect($"{name}: a chunk fits in 16-bit indices", most * 4 < 65536, $"{most * 4} vertices");
    }

    private static void Write(string dir, Circuit circuit, LandscapePlan plan, Horizon h)
    {
        Directory.CreateDirectory(dir);
        using (var w = new BinaryWriter(File.Create(Path.Combine(dir, "trees.bin"))))
            foreach (TreeSpot t in plan.Trees)
            {
                Color tint = LandscapeLooks.Leaves(t.Kind, t.Hue) * t.Shade;
                w.Write(t.Base.x); w.Write(t.Base.y); w.Write(t.Base.z); w.Write(t.Scale); w.Write(t.Squash); w.Write(t.Yaw); w.Write((float)t.Kind); w.Write(tint.r); w.Write(tint.g); w.Write(tint.b);
            }
        // The trees in their chunks, in the order Landscape draws them: per chunk its near and far counts, then the trees.
        using (var w = new BinaryWriter(File.Create(Path.Combine(dir, "chunks.bin"))))
            foreach (TreeChunk chunk in plan.Chunks())
            {
                w.Write((float)chunk.Near.Count); w.Write((float)chunk.Far.Count);
                foreach (TreeSpot t in chunk.Near.Concat(chunk.Far))
                {
                    Color tint = LandscapeLooks.Leaves(t.Kind, t.Hue) * t.Shade;
                    w.Write(t.Base.x); w.Write(t.Base.y); w.Write(t.Base.z); w.Write(t.Scale); w.Write(t.Squash); w.Write(t.Yaw); w.Write((float)t.Kind); w.Write(tint.r); w.Write(tint.g); w.Write(tint.b);
                }
            }
        // The ground's colours and mowing, as Landscape.Fields makes them.
        GroundData g = circuit.Data.terrain;
        float cell = g.spacing / LandscapePlan.MapPerCell;
        var fields = new byte[plan.Map.Length * 3];
        using (var w = new BinaryWriter(File.Create(Path.Combine(dir, "mowing.bin"))))
            for (int i = 0; i < plan.Map.Length; i++)
            {
                int x = i % plan.MapX, z = i / plan.MapX;
                Color col = LandscapeLooks.Field(plan.Map[i], g.originX + x * cell, g.originZ + z * cell) * (0.94f + 0.12f * CloudField.Hash(x, z, 91));
                fields[i * 3] = (byte)Math.Clamp(col.r * 255f + 0.5f, 0f, 255f);
                fields[i * 3 + 1] = (byte)Math.Clamp(col.g * 255f + 0.5f, 0f, 255f);
                fields[i * 3 + 2] = (byte)Math.Clamp(col.b * 255f + 0.5f, 0f, 255f);
                float mown = plan.Mowing(i, out float fromRoad, out float ax, out float az);
                w.Write(fromRoad); w.Write(mown); w.Write(ax); w.Write(az);
            }
        File.WriteAllBytes(Path.Combine(dir, "fields.bin"), fields);
        File.WriteAllBytes(Path.Combine(dir, "map.bin"), Array.ConvertAll(plan.Map, u => (byte)u));
        File.WriteAllBytes(Path.Combine(dir, "ringmap.bin"), Array.ConvertAll(h.Map, u => (byte)u));
        using (var w = new BinaryWriter(File.Create(Path.Combine(dir, "ring.bin"))))
        {
            w.Write(h.Vertices.Count); w.Write(h.Triangles.Count);
            foreach (Vector3 v in h.Vertices) { w.Write(v.x); w.Write(v.y); w.Write(v.z); }
            foreach (Vector2 v in h.Uvs) { w.Write(v.x); w.Write(v.y); }
            foreach (int i in h.Triangles) w.Write(i);
        }
        var colours = new List<float[]>();
        foreach (LandUse u in Enum.GetValues(typeof(LandUse))) { Color col = LandscapeLooks.Ground(u); colours.Add(new[] { col.r, col.g, col.b }); }
        var leaves = new List<float[]>();
        foreach (TreeKind k in Enum.GetValues(typeof(TreeKind))) { Color col = LandscapeLooks.Leaves(k); leaves.Add(new[] { col.r, col.g, col.b }); }
        File.WriteAllText(Path.Combine(dir, "plan.json"), JsonSerializer.Serialize(new { trees = plan.Trees.Count, mapX = plan.MapX, mapZ = plan.MapZ, ringAround = Horizon.MapAround, ringOut = Horizon.MapOut, ground = colours, leaves }));
    }

    private static void Expect(string what, bool ok, string detail)
    {
        Console.WriteLine((ok ? "ok    " : "FAIL  ") + what + (ok || detail == "" ? "" : ": " + detail));
        if (!ok) failed++;
    }
}
