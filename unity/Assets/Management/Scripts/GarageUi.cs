using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TarmacGauntlet.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace TarmacGauntlet.Management
{
    /// <summary>Pieces the workshop and dealer panels share: buttons, steppers, stat rows and the panel frame.</summary>
    internal sealed class GarageUi
    {
        private readonly Menus menus;
        private readonly Font bold = Resources.Load<Font>("Fonts/TitilliumWeb-Bold");
        private static Texture2D tick;

        internal GarageUi(Menus menus)
        {
            this.menus = menus;
        }

        internal static Texture2D Tick => tick != null ? tick : tick = Resources.Load<Texture2D>("MenuIcons/check");

        internal Font Bold => bold;

        internal Label Text(string text, string cls, bool isBold = false) => menus.Text(text, cls, isBold);

        /// <summary>The panel's frame: a scrolling body over a footer that stays at the bottom.</summary>
        internal (VisualElement Body, VisualElement Foot) Frame(VisualElement panelBody, float keepScroll = 0)
        {
            var root = new VisualElement();
            root.AddToClassList("g-root");
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("g-scroll");
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            root.Add(scroll);
            var foot = new VisualElement();
            foot.AddToClassList("g-foot");
            root.Add(foot);
            panelBody.Add(root);
            if (keepScroll > 0) scroll.schedule.Execute(() => scroll.scrollOffset = new Vector2(0, keepScroll));
            return (scroll.contentContainer, foot);
        }

        /// <summary>The body's scroll position, to keep it when the panel is drawn again.</summary>
        internal static float ScrollOf(VisualElement panelBody) => panelBody.Q<ScrollView>(className: "g-scroll")?.scrollOffset.y ?? 0;

        /// <summary>A small glass button: a verb, then a price in green.</summary>
        internal VisualElement Button(string verb, double? price, bool enabled, Action onClick)
        {
            var b = new VisualElement();
            b.AddToClassList("g-btn");
            b.EnableInClassList("disabled", !enabled);
            Label label = Text(verb.ToUpperInvariant(), "g-btn-label", true);
            label.pickingMode = PickingMode.Ignore;
            b.Add(label);
            if (price.HasValue)
            {
                Label m = Text(Menus.Money(price.Value), "g-btn-money", true);
                m.pickingMode = PickingMode.Ignore;
                b.Add(m);
            }
            b.RegisterCallback<ClickEvent>(_ => { if (enabled) onClick(); });
            return b;
        }

        /// <summary>‹ value ›, with an end greyed out where the value can't go further.</summary>
        internal VisualElement Stepper(int value, bool canDown, bool canUp, Action down, Action up)
        {
            var q = new VisualElement();
            q.AddToClassList("g-qty");
            VisualElement left = Menus.Chevron("chevron-left", () => { if (canDown) down(); });
            left.EnableInClassList("no", !canDown);
            q.Add(left);
            q.Add(Text(value.ToString(CultureInfo.InvariantCulture), "g-qty-value", true));
            VisualElement right = Menus.Chevron("chevron-right", () => { if (canUp) up(); });
            right.EnableInClassList("no", !canUp);
            q.Add(right);
            return q;
        }

        internal Label Section(string text) => Text(text.ToUpperInvariant(), "g-sec", true);

        internal VisualElement TickIcon()
        {
            var t = new VisualElement { pickingMode = PickingMode.Ignore };
            t.AddToClassList("g-tick");
            t.style.backgroundImage = Tick;
            return t;
        }

        internal VisualElement Badge(CarModel m)
        {
            var b = new VisualElement { pickingMode = PickingMode.Ignore };
            b.AddToClassList("g-badge");
            b.style.backgroundImage = Resources.Load<Texture2D>("Brands/" + Garage.Brand(m));
            return b;
        }

        internal Label ClassLabel(CarClass c, string size) => ClassLabels.Make(c, size, bold);

        internal void Tooltip(VisualElement target, string text) => menus.Tooltip(target, text);

        // ------------------------------------------------------------------ stat rows

        /// <summary>The eight stat rows, kept so a hovered upgrade can light up what it raises without a redraw.</summary>
        internal sealed class StatRows
        {
            public VisualElement Root;
            public readonly Dictionary<CarStatKey, VisualElement> Ghosts = new Dictionary<CarStatKey, VisualElement>();
        }

        /// <summary>
        /// Stat bars for <paramref name="stats"/>. A ghost shows what the open upgrades would add, and the last column
        /// the change against <paramref name="reference"/>, the race car.
        /// </summary>
        internal StatRows Stats(CarStats stats, CarStats potential, CarStats reference)
        {
            var rows = new StatRows { Root = new VisualElement() };
            rows.Root.AddToClassList("g-block");
            foreach ((CarStatKey key, string name, string tip) in Garage.Stats)
            {
                bool pit = key == CarStatKey.PitStopTime;
                double v = stats.Get(key);
                var row = new VisualElement();
                row.AddToClassList("g-stat");
                Label n = Text(name.ToUpperInvariant(), "g-stat-name");
                Tooltip(n, tip);
                row.Add(n);

                var bar = new VisualElement();
                bar.AddToClassList("g-stat-bar");
                bar.EnableInClassList("plain", pit);
                var fill = new VisualElement();
                fill.AddToClassList("g-stat-fill");
                fill.AddToClassList("f-" + Garage.Band(v));
                fill.style.width = Length.Percent((float)Math.Min(100, Math.Max(0, v)));
                bar.Add(fill);
                if (!pit && potential != null && potential.Get(key) > v + 0.5)
                {
                    var ghost = new VisualElement();
                    ghost.AddToClassList("g-ghost");
                    ghost.style.left = Length.Percent((float)Math.Min(100, v));
                    ghost.style.width = Length.Percent((float)(Math.Min(100, potential.Get(key)) - Math.Min(100, v)));
                    bar.Add(ghost);
                    rows.Ghosts[key] = ghost;
                }
                row.Add(bar);

                long shown = (long)Math.Round(v);
                row.Add(Text(shown.ToString(CultureInfo.InvariantCulture) + (pit ? "s" : ""), "g-stat-value", true));

                Label delta = Text("", "g-stat-delta", true);
                if (reference != null)
                {
                    // Lower pit stop times are better.
                    long d = (long)Math.Round(pit ? reference.Get(key) - v : v - reference.Get(key));
                    if (d != 0)
                    {
                        delta.text = (d > 0 ? "+" : "−") + Math.Abs(d).ToString(CultureInfo.InvariantCulture);
                        delta.AddToClassList(d > 0 ? "v-hi" : "v-lo");
                    }
                }
                row.Add(delta);
                rows.Root.Add(row);
            }
            return rows;
        }

        /// <summary>Brightens the ghosts of the stats an upgrade raises, or none.</summary>
        internal static void Light(StatRows rows, UpgradePackType? pack)
        {
            if (rows == null) return;
            CarStatKey[] lit = pack.HasValue ? Garage.Packs.First(p => p.Type == pack.Value).Raises : Array.Empty<CarStatKey>();
            foreach (KeyValuePair<CarStatKey, VisualElement> g in rows.Ghosts) g.Value.EnableInClassList("lit", lit.Contains(g.Key));
        }

        /// <summary>Upgrade tiles light their stats while hovered.</summary>
        internal static void Previews(VisualElement tile, UpgradePackType type, Func<StatRows> rows)
        {
            tile.RegisterCallback<PointerEnterEvent>(_ =>
            {
                tile.AddToClassList("pv");
                Light(rows(), type);
            });
            tile.RegisterCallback<PointerLeaveEvent>(_ =>
            {
                tile.RemoveFromClassList("pv");
                Light(rows(), null);
            });
        }

        /// <summary>Lays tiles out in a row of equal widths.</summary>
        internal static VisualElement Tiles(IList<VisualElement> tiles)
        {
            var row = new VisualElement();
            row.AddToClassList("g-tiles");
            for (int i = 0; i < tiles.Count; i++)
            {
                if (i == tiles.Count - 1) tiles[i].AddToClassList("last");
                row.Add(tiles[i]);
            }
            return row;
        }
    }
}
