using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using TarmacGauntlet.Management;
using UnityEngine;
using UnityEngine.UIElements;

namespace MenuPreview
{
    /// <summary>
    /// Writes an element tree as HTML: one tag per element, its classes, its inline styles as CSS and its text.
    /// Images are referenced under assets/ by their path in Assets, and every image used is listed in <see cref="Images"/>.
    /// </summary>
    internal static class Html
    {
        internal static readonly HashSet<string> Images = new HashSet<string>();
        internal static string AssetsRoot = "";

        internal static string Write(VisualElement e)
        {
            var sb = new StringBuilder();
            Element(sb, e);
            return sb.ToString();
        }

        private static void Element(StringBuilder sb, VisualElement e)
        {
            string tag = e.Tag;
            var classes = new List<string>(e.classes);
            string css = Css(e, classes);
            sb.Append('<').Append(tag);
            if (classes.Count > 0) sb.Append(" class=\"").Append(string.Join(" ", classes)).Append('"');
            if (!string.IsNullOrEmpty(e.name)) sb.Append(" id=\"").Append(WebUtility.HtmlEncode(e.name)).Append('"');
            if (css.Length > 0) sb.Append(" style=\"").Append(WebUtility.HtmlEncode(css)).Append('"');
            sb.Append('>');
            if (e is TextElement t && t.text.Length > 0)
                sb.Append("<span class=\"t\">").Append(WebUtility.HtmlEncode(t.text.Replace("<u>", "").Replace("</u>", ""))).Append("</span>");
            if (e is TextField f)
            {
                bool empty = f.value.Length == 0;
                sb.Append("<ve class=\"unity-base-text-field__input").Append(empty ? " unity-base-text-field__input--placeholder" : "").Append("\"><span class=\"t\">")
                    .Append(WebUtility.HtmlEncode(empty ? f.textEdition.placeholder : f.value)).Append("</span></ve>");
            }
            if (e is PositionChart chart) sb.Append(Chart(chart));
            foreach (VisualElement k in e.kids) Element(sb, k);
            sb.Append("</").Append(tag).Append('>');
        }

        private static string Css(VisualElement e, List<string> classes)
        {
            var parts = new List<string>();
            foreach (KeyValuePair<string, object> kv in e.style.Values)
            {
                string name = kv.Key;
                object v = kv.Value;
                switch (name)
                {
                    case "background-image":
                        if (v is Texture2D tex) parts.Add(Image(tex, classes));
                        else if (v is StyleKeyword) parts.Add("background-image:none");
                        break;
                    case "font":
                        if (v is FontDefinition fd && fd.font != null) parts.Add(Font(fd.font.name));
                        break;
                    case "text-align":
                        if (v is TextAnchor a) parts.Add(Align(a));
                        break;
                    case "display":
                        parts.Add("display:" + ((DisplayStyle)v == DisplayStyle.None ? "none" : "flex"));
                        break;
                    case "visibility":
                        parts.Add("visibility:" + ((Visibility)v == Visibility.Hidden ? "hidden" : "visible"));
                        break;
                    case "translate":
                        var tr = (Translate)v;
                        parts.Add("translate:" + tr.x.Css + " " + tr.y.Css);
                        break;
                    case "rotate":
                        parts.Add("rotate:" + Length.Fmt(((Rotate)v).angle.value) + "deg");
                        break;
                    case "scale":
                        var sc = (Scale)v;
                        parts.Add("scale:" + Length.Fmt(sc.value.x) + " " + Length.Fmt(sc.value.y));
                        break;
                    case "transform-origin":
                        var to = (TransformOrigin)v;
                        parts.Add("transform-origin:" + to.x.Css + " " + to.y.Css);
                        break;
                    case "flex-grow":
                    case "opacity":
                        parts.Add(name + ":" + Number(v));
                        break;
                    default:
                        parts.Add(name + ":" + Value(v));
                        break;
                }
            }
            return string.Join(";", parts);
        }

        private static string Number(object v) => v is Length l ? Length.Fmt(l.value) : Value(v);

        private static string Value(object v) => v switch
        {
            Length l => l.Css,
            Color c => c.Css,
            StyleKeyword k => k == StyleKeyword.Auto ? "auto" : k == StyleKeyword.None ? "none" : "initial",
            _ => Convert.ToString(v, CultureInfo.InvariantCulture),
        };

        private static string Align(TextAnchor a)
        {
            string s = a.ToString();
            string v = s.StartsWith("Upper") ? "flex-start" : s.StartsWith("Middle") ? "center" : "flex-end";
            string h = s.EndsWith("Left") ? "left" : s.EndsWith("Right") ? "right" : "center";
            return "text-align:" + h + ";--tv:" + v;
        }

        internal static string Font(string file) => file switch
        {
            "TitilliumWeb-Bold" => "font-family:'Titillium Web';font-weight:700",
            "TitilliumWeb-SemiBold" => "font-family:'Titillium Web';font-weight:600",
            "HostGrotesk-Medium" => "font-family:'Host Grotesk';font-weight:500",
            "HostGrotesk-Regular" => "font-family:'Host Grotesk';font-weight:400",
            _ => "",
        };

        /// <summary>Icons and badges are white shapes tinted in Unity, so here they are masks over the tint colour.</summary>
        private static string Image(Texture2D t, List<string> classes)
        {
            if (t.Path == null)
            {
                if (t.width == 1 && t.height == 32) return "background-image:linear-gradient(#ff2b22,#e10600)";
                if (t.width == 4 && t.height == 4) return "background-image:repeating-conic-gradient(rgba(0,0,0,.9) 0 25%,rgba(255,255,255,.35) 0 50%);background-size:8px 8px";
                return "";
            }
            string rel = "assets/" + System.IO.Path.GetRelativePath(AssetsRoot, t.Path).Replace('\\', '/');
            Images.Add(t.Path);
            bool mask = t.Path.Contains("/MenuIcons/") || t.Path.Contains("/Badges/");
            classes.Add(mask ? "tex-mask" : "tex");
            return "--img:url(" + rel + ")";
        }

        private static string Chart(PositionChart c)
        {
            IReadOnlyList<int> p = c.Positions;
            if (p.Count == 0) return "";
            int max = Math.Max(10, p.Max());
            const float w = 1000, h = 100;
            var sb = new StringBuilder("<svg class=\"chart-svg\" viewBox=\"0 0 1000 100\" preserveAspectRatio=\"none\">");
            foreach ((float from, float to, Color colour) in c.Stints)
            {
                float x0 = from / p.Count * w, x1 = to / p.Count * w;
                sb.Append($"<rect x=\"{F(x0)}\" y=\"0\" width=\"{F(x1 - x0)}\" height=\"{h}\" fill=\"{colour.Css}\" opacity=\".12\"/>");
            }
            sb.Append("<polyline fill=\"none\" stroke=\"#fff\" stroke-width=\"2\" vector-effect=\"non-scaling-stroke\" points=\"");
            for (int i = 0; i < p.Count; i++) sb.Append(F(i / (float)Math.Max(1, p.Count - 1) * w)).Append(',').Append(F((p[i] - 1) / (float)(max - 1) * h)).Append(' ');
            sb.Append("\"/></svg>");
            return sb.ToString();
        }

        private static string F(float v) => Math.Round(v, 1).ToString(CultureInfo.InvariantCulture);
    }
}
