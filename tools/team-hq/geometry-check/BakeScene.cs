// Writes the building as the bake (tools/team-hq/bake) reads it: every part with its lightmap coordinates, the props,
// the lights and the probes, all in Unity's frame.
using System.Globalization;
using System.IO;
using System.Text;
using TarmacGauntlet.Rendering;
using TarmacGauntlet.TeamHq;
using UnityEngine;

internal static class BakeScene
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    public static void Write(HqGeometry g, string path)
    {
        var s = new StringBuilder();
        s.Append("{\"signature\":\"").Append(g.Lightmap.Signature).Append("\",");
        s.Append($"\"width\":{g.Lightmap.Width},\"height\":{g.Lightmap.Height},\"parts\":[");
        bool first = true;
        foreach (var kv in g.Parts)
        {
            HqPart p = kv.Value;
            if (!first) s.Append(',');
            first = false;
            s.Append("{\"key\":\"").Append(kv.Key).Append("\",\"v\":");
            Floats(s, p.Vertices.Count, i => new[] { p.Vertices[i].x, p.Vertices[i].y, p.Vertices[i].z });
            s.Append(",\"uv\":");
            Floats(s, p.Uvs.Count, i => new[] { p.Uvs[i].x, p.Uvs[i].y });
            s.Append(",\"uv2\":");
            Floats(s, p.Uv2s.Count, i => new[] { p.Uv2s[i].x, p.Uv2s[i].y });
            s.Append(",\"t\":[").Append(string.Join(",", p.Triangles)).Append("]}");
        }
        s.Append("],\"props\":[");
        for (int i = 0; i < g.Props.Count; i++)
        {
            HqProp pr = g.Props[i];
            s.Append(i > 0 ? "," : "").Append(string.Format(Ci, "[\"{0}\",{1},{2},{3},{4},{5}]", pr.Id, pr.Position.x, pr.Position.y, pr.Position.z, pr.Yaw, pr.Scale));
        }
        s.Append("],\"lights\":[");
        for (int i = 0; i < g.Lights.Count; i++)
        {
            HqLight l = g.Lights[i];
            Vector3 c = l.Warm ? HqLighting.WarmLamp : HqLighting.WhiteLamp;
            s.Append(i > 0 ? "," : "").Append(string.Format(Ci, "[{0},{1},{2},{3},{4},{5},{6}]", l.Position.x, l.Position.y, l.Position.z, LightFalloff.Urp(l.Intensity, l.Range), c.x, c.y, c.z));
        }
        Vector3 d = HqLighting.SunDirection, sc = HqLighting.SunColour;
        s.Append(string.Format(Ci, "],\"sun\":[{0},{1},{2},{3},{4},{5},{6}],\"probes\":[", d.x, d.y, d.z, sc.x, sc.y, sc.z, HqLighting.SunIntensity));
        for (int i = 0; i < HqLighting.Probes.Length; i++)
        {
            HqProbe p = HqLighting.Probes[i];
            s.Append(i > 0 ? "," : "").Append(string.Format(Ci, "{{\"name\":\"{0}\",\"capture\":[{1},{2},{3}],\"centre\":[{4},{5},{6}],\"size\":[{7},{8},{9}]}}",
                p.Name, p.Capture.x, p.Capture.y, p.Capture.z, p.Centre.x, p.Centre.y, p.Centre.z, p.Size.x, p.Size.y, p.Size.z));
        }
        s.Append("]}");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        File.WriteAllText(path, s.ToString());
    }

    private static void Floats(StringBuilder s, int n, System.Func<int, float[]> get)
    {
        s.Append('[');
        for (int i = 0; i < n; i++)
            foreach (float f in get(i)) s.Append(s[s.Length - 1] == '[' ? "" : ",").Append(f.ToString("R", Ci));
        s.Append(']');
    }
}
