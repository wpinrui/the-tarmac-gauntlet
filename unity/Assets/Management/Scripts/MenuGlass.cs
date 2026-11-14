using System.Collections.Generic;
using TarmacGauntlet.Rendering;
using UnityEngine;
using UnityEngine.UIElements;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// Makes frosted glass panels: the blurred scene behind them, a near-black tint at 66% under a 140 degree white sheen from 8% to 0%, a 1px edge
    /// and a soft drop shadow. <see cref="Refresh"/> keeps every panel's slice of the blurred scene lined up each frame.
    /// </summary>
    public sealed class MenuGlass
    {
        private sealed class Pane
        {
            public VisualElement Glass, Blur, Shadow;
            /// <summary>The blurred scene this pane last showed: each pane catches up on its own, so one hidden while the screen changed size doesn't keep the old one.</summary>
            public RenderTexture Shown;
            /// <summary>The corner radii the shadow was made for, px: top left, top right, bottom right, bottom left.</summary>
            public (int, int, int, int) Corners = (-1, -1, -1, -1);
        }

        private readonly VisualElement root;
        private readonly List<Pane> panes = new List<Pane>();
        private readonly Texture2D sheen = Sheen();
        /// <summary>Shadows by corner radii, so every pane casts one the shape of its own corners.</summary>
        private readonly Dictionary<(int, int, int, int), Texture2D> shadows = new Dictionary<(int, int, int, int), Texture2D>();

        public MenuGlass(VisualElement root)
        {
            this.root = root;
        }

        /// <summary>A glass panel with the given classes. Add children to it as usual.</summary>
        public VisualElement Make(params string[] classes)
        {
            var glass = new VisualElement();
            glass.AddToClassList("glass");
            foreach (string c in classes) glass.AddToClassList(c);
            var blur = new VisualElement { pickingMode = PickingMode.Ignore };
            blur.AddToClassList("glass-blur");
            var sheenLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            sheenLayer.AddToClassList("glass-sheen");
            sheenLayer.style.backgroundImage = sheen;
            glass.Add(blur);
            glass.Add(sheenLayer);
            panes.Add(new Pane { Glass = glass, Blur = blur });
            return glass;
        }

        /// <summary>Lines up the blurred scene behind every shown panel and moves the shadows with them.</summary>
        public void Refresh()
        {
            RenderTexture texture = GlassBackdrop.Texture;
            float width = root.layout.width, height = root.layout.height;

            foreach (Pane p in panes)
            {
                VisualElement g = p.Glass;
                if (g.panel == null) continue;
                if (p.Shadow == null && g.parent != null) AddShadow(p);

                bool shown = g.resolvedStyle.display != DisplayStyle.None;
                if (p.Shadow != null)
                {
                    p.Shadow.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
                    Rect r = g.layout;
                    FitShadow(p, r);
                    // No offset: centred on the pane, reaching ShadowReach px past every side.
                    p.Shadow.style.left = r.x - ShadowReach;
                    p.Shadow.style.top = r.y - ShadowReach;
                    p.Shadow.style.width = r.width + 2 * ShadowReach;
                    p.Shadow.style.height = r.height + 2 * ShadowReach;
                }
                if (!shown) continue;

                if (texture != p.Shown)
                {
                    p.Shown = texture;
                    p.Blur.style.backgroundImage = texture != null ? Background.FromRenderTexture(texture) : new StyleBackground(StyleKeyword.None);
                }
                Rect world = g.worldBound;
                p.Blur.style.left = -world.x - g.resolvedStyle.borderLeftWidth;
                p.Blur.style.top = -world.y - g.resolvedStyle.borderTopWidth;
                p.Blur.style.width = width;
                p.Blur.style.height = height;
            }
        }

        private void AddShadow(Pane p)
        {
            var s = new VisualElement { pickingMode = PickingMode.Ignore };
            s.AddToClassList("glass-shadow");
            p.Glass.parent.Insert(p.Glass.parent.IndexOf(p.Glass), s);
            p.Shadow = s;
        }

        /// <summary>White at 8% in the top left corner fading to nothing by 60% of the way, at 140 degrees.</summary>
        private static Texture2D Sheen()
        {
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            // CSS angles start pointing up and turn clockwise; in screen axes (y down) 140 degrees points down and right.
            var dir = new Vector2(Mathf.Sin(140 * Mathf.Deg2Rad), -Mathf.Cos(140 * Mathf.Deg2Rad));
            float halfLine = 0.5f * (Mathf.Abs(dir.x) + Mathf.Abs(dir.y));
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // Texture rows run bottom to top, so the top row is the screen's top edge.
                var p = new Vector2(x / (n - 1f) - 0.5f, 0.5f - y / (n - 1f));
                float k = Mathf.Clamp01(Vector2.Dot(p, dir) / halfLine * 0.5f + 0.5f);
                px[y * n + x] = UiBlend.Alpha(new Color(1, 1, 1, Mathf.Lerp(0.08f, 0f, Mathf.Clamp01(k / 0.6f))));
            }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        /// <summary>How far the shadow reaches past the pane (and in under it), px, and how dark it is at its darkest.</summary>
        private const int ShadowReach = 16;
        private const float ShadowAlpha = 0.30f;

        /// <summary>Gives the pane's shadow the corners the pane has: a round button casts a round shadow.</summary>
        private void FitShadow(Pane p, Rect r)
        {
            IResolvedStyle st = p.Glass.resolvedStyle;
            float most = Mathf.Max(0, Mathf.Min(r.width, r.height) / 2);
            int Corner(float radius) => Mathf.RoundToInt(Mathf.Clamp(radius, 0, most));
            (int, int, int, int) corners = (Corner(st.borderTopLeftRadius), Corner(st.borderTopRightRadius),
                Corner(st.borderBottomRightRadius), Corner(st.borderBottomLeftRadius));
            if (corners == p.Corners) return;
            p.Corners = corners;
            if (!shadows.TryGetValue(corners, out Texture2D texture)) shadows[corners] = texture = ShadowTexture(corners);
            int slice = texture.width / 2 - 1;
            p.Shadow.style.backgroundImage = texture;
            p.Shadow.style.unitySliceLeft = slice;
            p.Shadow.style.unitySliceRight = slice;
            p.Shadow.style.unitySliceTop = slice;
            p.Shadow.style.unitySliceBottom = slice;
        }

        /// <summary>
        /// Black at <see cref="ShadowAlpha"/> under a rounded box with these corner radii, blurred across its edge: full
        /// <see cref="ShadowReach"/>px inside it, half at the edge, nothing <see cref="ShadowReach"/>px outside it. For
        /// 9-slicing: each corner tile holds its own rounded corner and the middle two pixels stretch.
        /// </summary>
        private static Texture2D ShadowTexture((int TopLeft, int TopRight, int BottomRight, int BottomLeft) c)
        {
            int most = Mathf.Max(Mathf.Max(c.TopLeft, c.TopRight), Mathf.Max(c.BottomRight, c.BottomLeft));
            // The box reaches at least ShadowReach in from each corner tile's inner edge, so the fade fits inside the tile.
            int inset = Mathf.Max(most, ShadowReach);
            int n = 2 * (ShadowReach + inset + 1);
            float centre = n / 2f, half = inset + 1;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                // Texture rows run bottom to top, so the top row is the screen's top edge.
                float dx = x + 0.5f - centre, dy = (n - y - 0.5f) - centre;
                int radius = dy < 0 ? (dx < 0 ? c.TopLeft : c.TopRight) : (dx < 0 ? c.BottomLeft : c.BottomRight);
                // Distance outside the rounded box, negative inside.
                float qx = Mathf.Abs(dx) - (half - radius), qy = Mathf.Abs(dy) - (half - radius);
                float outside = new Vector2(Mathf.Max(qx, 0), Mathf.Max(qy, 0)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0) - radius;
                float k = Mathf.SmoothStep(0, 1, Mathf.Clamp01((ShadowReach - outside) / (2f * ShadowReach)));
                px[y * n + x] = UiBlend.Alpha(new Color(0, 0, 0, ShadowAlpha * k));
            }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }
    }
}
