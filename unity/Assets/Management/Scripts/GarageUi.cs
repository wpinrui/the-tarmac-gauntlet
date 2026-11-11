using System;
using System.Collections.Generic;
using System.Linq;
using TarmacGauntlet.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
using static TarmacGauntlet.Management.MenuKit;

namespace TarmacGauntlet.Management
{
    /// <summary>Pieces only the workshop and dealer panels use: the panel frame, brand badges and car stat rows.</summary>
    internal static class GarageUi
    {
        /// <summary>The panel's frame: a scrolling body over a footer that stays at the bottom.</summary>
        internal static (VisualElement Body, VisualElement Foot) Frame(VisualElement panelBody, float keepScroll = 0)
        {
            VisualElement root = Box("frame");
            ScrollView scroll = Scroll();
            root.Add(scroll);
            VisualElement foot = Box("frame-foot");
            root.Add(foot);
            panelBody.Add(root);
            if (keepScroll > 0) scroll.schedule.Execute(() => scroll.scrollOffset = new Vector2(0, keepScroll));
            return (scroll.contentContainer, foot);
        }

        /// <summary>The body's scroll position, to keep it when the panel is drawn again.</summary>
        internal static float ScrollOf(VisualElement panelBody) => panelBody.Q<ScrollView>(className: "list")?.scrollOffset.y ?? 0;

        internal static VisualElement Badge(CarModel m)
        {
            var b = new VisualElement { pickingMode = PickingMode.Ignore };
            b.AddToClassList("brand");
            b.style.backgroundImage = Resources.Load<Texture2D>("Brands/" + Garage.Brand(m));
            return b;
        }

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
        internal static StatRows Stats(Tooltips tips, CarStats stats, CarStats potential, CarStats reference)
        {
            var rows = new StatRows { Root = Box("stats") };
            foreach ((CarStatKey key, string name, string tip) in Garage.Stats)
            {
                bool pit = key == CarStatKey.PitStopTime;
                double v = stats.Get(key);
                VisualElement bar = Bar(v);
                if (pit) bar.style.visibility = Visibility.Hidden;
                if (!pit && potential != null && potential.Get(key) > v + 0.5)
                {
                    VisualElement ghost = Box("stat-ghost");
                    ghost.style.left = Length.Percent((float)Math.Min(100, v));
                    ghost.style.width = Length.Percent((float)(Math.Min(100, potential.Get(key)) - Math.Min(100, v)));
                    bar.Add(ghost);
                    rows.Ghosts[key] = ghost;
                }
                VisualElement row = StatRow(tips, name, tip, bar, Num((int)Math.Round(v)) + (pit ? "s" : ""));

                Label delta = Text("", "stat-delta", true);
                if (reference != null)
                {
                    // Lower pit stop times are better.
                    long d = (long)Math.Round(pit ? reference.Get(key) - v : v - reference.Get(key));
                    if (d != 0)
                    {
                        delta.text = (d > 0 ? "+" : "−") + Num((int)Math.Abs(d));
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
    }
}
