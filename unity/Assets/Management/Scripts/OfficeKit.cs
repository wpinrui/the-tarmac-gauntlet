using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace TarmacGauntlet.Management
{
    /// <summary>Small building blocks the office screens share: rows, scroll lists, icons and the year stepper.</summary>
    internal static class OfficeKit
    {
        internal static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);

        /// <summary>A whole-dollar amount with a + in front of income.</summary>
        internal static string Signed(double amount) => (amount > 0 ? "+" : "") + Menus.Money(amount);

        internal static VisualElement Box(params string[] classes)
        {
            var box = new VisualElement();
            foreach (string c in classes) box.AddToClassList(c);
            return box;
        }

        internal static ScrollView Scroll(string cls)
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList(cls);
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            return scroll;
        }

        internal static VisualElement Icon(string file, string cls)
        {
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList(cls);
            icon.style.backgroundImage = Resources.Load<Texture2D>("MenuIcons/" + file);
            return icon;
        }

        internal static Label Tab(Menus menus, string text, bool on, Action onClick)
        {
            Label tab = menus.Text(text, "rs-tab");
            tab.EnableInClassList("on", on);
            tab.RegisterCallback<ClickEvent>(_ => onClick());
            return tab;
        }

        /// <summary>A coloured square for a driver, as the race summary marks stints.</summary>
        internal static VisualElement Swatch(Color colour)
        {
            var swatch = new VisualElement { pickingMode = PickingMode.Ignore };
            swatch.AddToClassList("rs-swatch");
            swatch.style.backgroundColor = colour;
            return swatch;
        }

        /// <summary>
        /// Year N between two chevrons. A chevron hides where there is no year that way.
        /// <paramref name="step"/> gets -1 or +1.
        /// </summary>
        internal static VisualElement YearStepper(Menus menus, int year, bool canBack, bool canOn, Action<int> step)
        {
            VisualElement stepper = Box("of-stepper");
            VisualElement back = Menus.Chevron("chevron-left", () => { if (canBack) step(-1); });
            back.style.visibility = canBack ? Visibility.Visible : Visibility.Hidden;
            stepper.Add(back);
            stepper.Add(menus.Text("YEAR " + Num(year), "of-step-value", true));
            VisualElement on = Menus.Chevron("chevron-right", () => { if (canOn) step(1); });
            on.style.visibility = canOn ? Visibility.Visible : Visibility.Hidden;
            stepper.Add(on);
            return stepper;
        }
    }
}
