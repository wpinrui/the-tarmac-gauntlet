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
        }

        private readonly VisualElement root;
        private readonly List<Pane> panes = new List<Pane>();
        private readonly Texture2D sheen = Sheen(), shadow = ShadowTexture();
        private RenderTexture shownTexture;

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
            bool newTexture = texture != shownTexture;
            shownTexture = texture;
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
                    // box-shadow: 0 24px 60px; the texture's soft edge is 32px of a 64px tile.
                    p.Shadow.style.left = r.x - 40;
                    p.Shadow.style.top = r.y - 16;
                    p.Shadow.style.width = r.width + 80;
                    p.Shadow.style.height = r.height + 80;
                }
                if (!shown) continue;

                if (newTexture) p.Blur.style.backgroundImage = texture != null ? Background.FromRenderTexture(texture) : new StyleBackground(StyleKeyword.None);
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
            s.style.backgroundImage = shadow;
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
                px[y * n + x] = new Color(1, 1, 1, Mathf.Lerp(0.08f, 0f, Mathf.Clamp01(k / 0.6f)));
            }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }

        /// <summary>A 64px tile: black at 35% in the middle, fading out over 32px on every side, for 9-slicing.</summary>
        private static Texture2D ShadowTexture()
        {
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = Mathf.Min(x + 0.5f, n - x - 0.5f) / 32f, dy = Mathf.Min(y + 0.5f, n - y - 0.5f) / 32f;
                float k = Mathf.SmoothStep(0, 1, Mathf.Clamp01(dx)) * Mathf.SmoothStep(0, 1, Mathf.Clamp01(dy));
                px[y * n + x] = new Color(0, 0, 0, 0.35f * k);
            }
            t.SetPixels32(px);
            t.Apply();
            return t;
        }
    }
}
