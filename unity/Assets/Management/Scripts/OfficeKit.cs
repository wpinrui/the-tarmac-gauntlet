using System;
using System.Linq;
using TarmacGauntlet.Career;
using TarmacGauntlet.Simulation;
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

        /// <summary>
        /// A driver's name that opens the Drivers screen on them: the market, or the roster for one of the player's own.
        /// </summary>
        internal static Label DriverLink(Menus menus, string name, string driverId, string cls)
        {
            Label link = menus.Text(name, cls, true);
            link.AddToClassList("of-link");
            link.RegisterCallback<PointerEnterEvent>(_ => link.text = "<u>" + name + "</u>");
            link.RegisterCallback<PointerLeaveEvent>(_ => link.text = name);
            link.RegisterCallback<ClickEvent>(e =>
            {
                e.StopPropagation();
                DriversPanel.Show(menus, driverId);
            });
            return link;
        }

        /// <summary>The team's nationality flag, sized by the of-flag class.</summary>
        internal static VisualElement Flag(BaseTeam team)
        {
            var flag = new VisualElement { pickingMode = PickingMode.Ignore };
            flag.AddToClassList("of-flag");
            flag.style.backgroundImage = Resources.Load<Texture2D>("Flags/" + TeamNationality.Of(team));
            return flag;
        }

        /// <summary>A podium team: its name, and under it the car's brand badge and model.</summary>
        internal static VisualElement PodiumTeam(ClassifiedCar r, Func<string, string, bool, Label> text)
        {
            VisualElement info = Box("rs-podium-team");
            VisualElement name = Box("of-named");
            BaseTeam team = GameSession.State?.Teams.FirstOrDefault(t => t.Id == r.TeamId);
            if (team != null) name.Add(Flag(team));
            name.Add(text(r.TeamName, "rs-team-name", true));
            info.Add(name);
            CarModel model = GameSession.State?.CarModels.FirstOrDefault(m => m.Id == r.ModelId) ?? CarModels.All.FirstOrDefault(m => m.Id == r.ModelId);
            if (model == null) return info;
            VisualElement car = Box("rs-podium-car");
            VisualElement badge = Box("rs-podium-badge");
            badge.pickingMode = PickingMode.Ignore;
            badge.style.backgroundImage = Resources.Load<Texture2D>("Brands/" + Garage.Brand(model));
            car.Add(badge);
            car.Add(text(model.Name, "rs-podium-model", false));
            info.Add(car);
            return info;
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
