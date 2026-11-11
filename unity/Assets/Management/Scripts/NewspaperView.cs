using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TarmacGauntlet.Simulation;
using TarmacGauntlet.TeamHq;
using UnityEngine;
using UnityEngine.UIElements;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// The paper on the office coffee table, read full screen. Once the camera has glided to the table, the page grows
    /// out of the paper in the scene until it fills the screen; closing shrinks it back onto the table. The page is a
    /// broadsheet laid out on a 1600 x 900 sheet and scaled to fit, so the text never reflows while it moves.
    /// Which stories go where depends on how much of the paper the player's team gets (<see cref="PlayerCoverage"/>).
    /// </summary>
    internal sealed class NewspaperView
    {
        private const float SheetWidth = 1600, SheetHeight = 900, OpenSeconds = 0.45f, CloseSeconds = 0.35f;

        private static readonly Color Paper = new Color32(0xf7, 0xf5, 0xef, 0xff);
        private static readonly Color Ink = new Color32(0x12, 0x12, 0x12, 0xff);
        private static readonly Color InkSoft = new Color32(0x4a, 0x4a, 0x48, 0xff);
        private static readonly Color Hairline = new Color(0, 0, 0, 0.25f);

        private readonly VisualElement layer, page, sheet, close;
        private readonly Camera cam;
        private readonly HqCamera hqCamera;
        private readonly Font black, boldDisplay, italic, regular, semiBold, regularItalic;

        private enum State { Closed, Waiting, Opening, Open, Closing }
        private State state = State.Closed;
        private float started;
        private Action closed;

        public bool IsOpen => state != State.Closed;

        public NewspaperView(VisualElement layer, HqCamera hqCamera, MenuGlass glass)
        {
            this.layer = layer;
            this.hqCamera = hqCamera;
            cam = hqCamera.GetComponent<Camera>();
            black = Resources.Load<Font>("Fonts/PlayfairDisplay-Black");
            boldDisplay = Resources.Load<Font>("Fonts/PlayfairDisplay-Bold");
            italic = Resources.Load<Font>("Fonts/PlayfairDisplay-Italic");
            regular = Resources.Load<Font>("Fonts/SourceSerif4-Regular");
            semiBold = Resources.Load<Font>("Fonts/SourceSerif4-SemiBold");
            regularItalic = Resources.Load<Font>("Fonts/SourceSerif4-Italic");

            page = new VisualElement { pickingMode = PickingMode.Position };
            page.AddToClassList("np-page");
            page.style.backgroundColor = Paper;
            sheet = new VisualElement { pickingMode = PickingMode.Ignore };
            sheet.AddToClassList("np-sheet");
            sheet.style.width = SheetWidth;
            sheet.style.height = SheetHeight;
            sheet.style.transformOrigin = new TransformOrigin(0, 0);
            page.Add(sheet);
            layer.Add(page);

            close = glass.Make("np-close");
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList("np-close-icon");
            icon.style.backgroundImage = Resources.Load<Texture2D>("MenuIcons/close");
            close.Add(icon);
            close.RegisterCallback<ClickEvent>(_ => Close());
            layer.Add(close);

            layer.style.display = DisplayStyle.None;
        }

        /// <summary>Shows the paper once the camera reaches the table. <paramref name="onClosed"/> runs when it is put down.</summary>
        public void Open(Newspaper paper, Action onClosed)
        {
            closed = onClosed;
            sheet.Clear();
            Build(paper);
            state = State.Waiting;
            layer.style.display = DisplayStyle.Flex;
            page.style.opacity = 0;
            close.style.display = DisplayStyle.None;
        }

        /// <summary>Puts the paper back on the table.</summary>
        public void Close()
        {
            if (state == State.Closed || state == State.Closing) return;
            if (state == State.Waiting)
            {
                Finish();
                return;
            }
            close.style.display = DisplayStyle.None;
            state = State.Closing;
            started = Time.unscaledTime;
        }

        /// <summary>Hides the paper at once, without running the close action.</summary>
        public void Hide()
        {
            state = State.Closed;
            closed = null;
            layer.style.display = DisplayStyle.None;
        }

        public void Tick()
        {
            if (state == State.Closed || layer.panel == null) return;
            Rect full = layer.layout;
            if (float.IsNaN(full.width) || full.width <= 0) return;
            switch (state)
            {
                case State.Waiting:
                    if (hqCamera.Gliding) return;
                    state = State.Opening;
                    started = Time.unscaledTime;
                    break;
                case State.Opening:
                {
                    float t = Ease((Time.unscaledTime - started) / OpenSeconds);
                    Place(Lerp(OnTable(full), new Rect(0, 0, full.width, full.height), t), Mathf.Clamp01(t * 3));
                    if (t >= 1)
                    {
                        state = State.Open;
                        close.style.display = DisplayStyle.Flex;
                    }
                    break;
                }
                case State.Open:
                    Place(new Rect(0, 0, full.width, full.height), 1);
                    break;
                case State.Closing:
                {
                    float t = Ease((Time.unscaledTime - started) / CloseSeconds);
                    Place(Lerp(new Rect(0, 0, full.width, full.height), OnTable(full), t), Mathf.Clamp01((1 - t) * 3));
                    if (t >= 1) Finish();
                    break;
                }
            }
        }

        private void Finish()
        {
            Action done = closed;
            Hide();
            done?.Invoke();
        }

        private static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t < 0.5f ? 2 * t * t : 1 - Mathf.Pow(-2 * t + 2, 2) / 2;
        }

        private static Rect Lerp(Rect a, Rect b, float t) => new Rect(
            Mathf.Lerp(a.x, b.x, t), Mathf.Lerp(a.y, b.y, t), Mathf.Lerp(a.width, b.width, t), Mathf.Lerp(a.height, b.height, t));

        /// <summary>Lays the page over <paramref name="r"/>, with the sheet scaled to fit inside it.</summary>
        private void Place(Rect r, float opacity)
        {
            page.style.left = r.x;
            page.style.top = r.y;
            page.style.width = r.width;
            page.style.height = r.height;
            page.style.opacity = opacity;
            float k = Mathf.Min(r.width / SheetWidth, r.height / SheetHeight);
            sheet.style.scale = new Scale(new Vector3(k, k, 1));
            sheet.style.left = (r.width - SheetWidth * k) / 2;
            sheet.style.top = (r.height - SheetHeight * k) / 2;
        }

        /// <summary>The paper on the coffee table, as a rectangle on the panel.</summary>
        private Rect OnTable(Rect full)
        {
            // The geometry is in the viewer's frame; Unity's Z is the negative.
            Vector3 c = HqGeometry.PaperCentre;
            var centre = new Vector3(c.x, c.y, -c.z);
            float hx = HqGeometry.PaperSize.x / 2, hz = HqGeometry.PaperSize.y / 2;
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (Vector3 corner in new[] { new Vector3(-hx, 0, -hz), new Vector3(hx, 0, -hz), new Vector3(-hx, 0, hz), new Vector3(hx, 0, hz) })
            {
                Vector3 s = cam.WorldToScreenPoint(centre + corner);
                if (s.z <= 0) return new Rect(full.width / 2, full.height / 2, 0, 0);
                Vector2 p = RuntimePanelUtils.ScreenToPanel(layer.panel, new Vector2(s.x, Screen.height - s.y));
                minX = Mathf.Min(minX, p.x);
                minY = Mathf.Min(minY, p.y);
                maxX = Mathf.Max(maxX, p.x);
                maxY = Mathf.Max(maxY, p.y);
            }
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        // ------------------------------------------------------------------ the page

        private void Build(Newspaper paper)
        {
            var mast = Box("np-mast");
            var masthead = Box("np-masthead");
            masthead.style.backgroundImage = Resources.Load<Texture2D>("Wordmark/gazette-masthead");
            mast.Add(masthead);
            mast.Add(Box("np-thick"));
            mast.Add(Box("np-rule"));
            mast.Add(Text("YEAR " + paper.Year.ToString(CultureInfo.InvariantCulture), "np-dateline", semiBold, Ink));
            mast.Add(Box("np-double"));
            sheet.Add(mast);

            var grid = Box("np-grid");
            sheet.Add(grid);
            switch (paper.Coverage)
            {
                case PlayerCoverage.Front:
                {
                    var top = Box("np-row", "np-top");
                    top.Add(Cell(Article(paper.Lead, "np-h1", 3), 7));
                    top.Add(Cell(Article(paper.PlayerStory, "np-h2", 2), 5, true));
                    grid.Add(top);
                    grid.Add(Article(paper.Feature, "np-h3", 4));
                    var strip = Box("np-strip");
                    strip.Add(Podiums(paper, 6));
                    grid.Add(strip);
                    sheet.AddToClassList("np-front");
                    break;
                }
                case PlayerCoverage.Column:
                {
                    var row = Box("np-row", "np-fill");
                    var left = Cell(null, 8);
                    left.Add(Lead(paper));
                    var bottom = Box("np-row", "np-fill");
                    bottom.Add(Cell(Article(paper.Feature, "np-h3", 1), 4));
                    bottom.Add(Cell(Article(paper.PlayerStory, "np-h3", 1), 4, true));
                    left.Add(bottom);
                    row.Add(left);
                    row.Add(Cell(Podiums(paper, 2), 4, true));
                    grid.Add(row);
                    break;
                }
                default:
                {
                    var row = Box("np-row", "np-fill");
                    var left = Cell(null, 8);
                    left.Add(Lead(paper));
                    left.Add(Article(paper.Feature, "np-h3", 3));
                    row.Add(left);
                    row.Add(Cell(Podiums(paper, 2), 4, true));
                    grid.Add(row);
                    break;
                }
            }

            if (paper.Coverage == PlayerCoverage.Footnote && paper.PlayerStory != null && paper.PlayerStory.Paragraphs.Count > 0)
            {
                var brief = Box("np-brief");
                brief.Add(Text(paper.PlayerStory.Paragraphs[0], "np-brief-text", regularItalic, Ink));
                sheet.Add(brief);
            }
        }

        private VisualElement Lead(Newspaper paper)
        {
            VisualElement lead = Article(paper.Lead, "np-h1", 3);
            lead.AddToClassList("np-top");
            return lead;
        }

        /// <summary>A headline, standfirst and paragraphs set in <paramref name="columns"/> columns.</summary>
        private VisualElement Article(Story story, string headline, int columns)
        {
            var box = Box("np-story");
            if (story == null) return box;
            if (story.Headline.Length > 0) box.Add(Text(story.Headline, headline, headline == "np-h1" ? black : boldDisplay, Ink));
            if (story.Standfirst.Length > 0) box.Add(Text(story.Standfirst, "np-stand", italic, InkSoft));
            var cols = Box("np-cols");
            List<List<string>> split = Split(story.Paragraphs, columns);
            for (int i = 0; i < columns; i++)
            {
                var col = Box("np-col");
                if (i > 0)
                {
                    col.AddToClassList("np-col-ruled");
                    col.style.borderLeftColor = Hairline;
                }
                if (i < split.Count)
                    foreach (string p in split[i]) col.Add(Text(p, "np-p", regular, Ink));
                cols.Add(col);
            }
            box.Add(cols);
            return box;
        }

        /// <summary>Whole paragraphs into columns, in order, to even lengths.</summary>
        internal static List<List<string>> Split(IReadOnlyList<string> paragraphs, int columns)
        {
            var result = new List<List<string>>();
            int n = Math.Min(columns, paragraphs.Count);
            if (n == 0) return result;
            int total = paragraphs.Sum(p => p.Length), length = 0;
            var col = new List<string>();
            for (int i = 0; i < paragraphs.Count; i++)
            {
                col.Add(paragraphs[i]);
                length += paragraphs[i].Length;
                int left = paragraphs.Count - i - 1;
                if (result.Count < n - 1 && (length >= total / (float)n || left <= n - 1 - result.Count))
                {
                    result.Add(col);
                    col = new List<string>();
                    length = 0;
                }
            }
            if (col.Count > 0) result.Add(col);
            return result;
        }

        private VisualElement Podiums(Newspaper paper, int across)
        {
            var box = Box("np-pods");
            if (across > 2) box.AddToClassList("np-pods-wide");
            foreach (ClassPodium podium in paper.Podiums)
            {
                var pod = Box("np-pod");
                pod.style.width = Length.Percent(100f / across);
                var label = Text("CLASS " + NewspaperWriter.ClassName(podium.CarClass), "np-pod-class", semiBold, Ink);
                pod.Add(label);
                for (int i = 0; i < podium.Places.Count; i++)
                {
                    PodiumPlace place = podium.Places[i];
                    bool me = place.TeamId == GameActions.PlayerTeamId;
                    var row = Box("np-pod-row");
                    row.Add(Text((i + 1).ToString(CultureInfo.InvariantCulture), "np-pod-place", semiBold, Ink));
                    row.Add(Text(place.TeamName, "np-pod-team", me ? semiBold : regular, Ink));
                    row.Add(Text(place.Laps.ToString(CultureInfo.InvariantCulture), "np-pod-laps", regular, InkSoft));
                    pod.Add(row);
                }
                box.Add(pod);
            }
            return box;
        }

        /// <summary>A column of the 12-column page.</summary>
        private static VisualElement Cell(VisualElement content, int span, bool ruled = false)
        {
            var cell = Box("np-cell");
            cell.style.flexGrow = span;
            cell.style.flexBasis = 0;
            if (ruled) cell.AddToClassList("np-ruled");
            if (content != null) cell.Add(content);
            return cell;
        }

        private static VisualElement Box(params string[] classes)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            foreach (string c in classes) e.AddToClassList(c);
            return e;
        }

        private static Label Text(string text, string cls, Font font, Color colour)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore, enableRichText = false };
            label.AddToClassList(cls);
            label.style.unityFontDefinition = FontDefinition.FromFont(font);
            label.style.color = colour;
            return label;
        }
    }
}
