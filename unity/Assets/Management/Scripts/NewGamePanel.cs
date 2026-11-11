using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TarmacGauntlet.Simulation;
using UnityEngine;
using UnityEngine.UIElements;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// The new game panel over the empty garage: team name and badge, the player's name and nationality, and the
    /// starting skill points. Start builds the career and opens the hub. Unspent points are forfeited, so Start asks first.
    /// </summary>
    public sealed class NewGamePanel : MonoBehaviour
    {
        private const int MaxName = 28;

        private Menus menus;
        private VisualElement body, big, shapes, colours, pips, skills, start, countries;
        private TextField team, player, nation;
        private ScrollView countryList;

        private string shape, colour, nationality;
        private readonly Dictionary<PlayerSkillType, int> levels = new Dictionary<PlayerSkillType, int>();
        private readonly List<(string Code, string Name)> matches = new List<(string Code, string Name)>();
        private int highlight;

        private void Awake()
        {
            menus = GetComponent<Menus>();
            menus.ScreenOpened += Opened;
        }

        private void OnDestroy()
        {
            menus.ScreenOpened -= Opened;
        }

        private void Opened(string slug, VisualElement panelBody)
        {
            body = slug == Menus.NewGameSlug ? panelBody : null;
            if (body == null) return;
            shape = Badges.Shapes[0];
            colour = Badges.Colours[0];
            nationality = null;
            foreach ((PlayerSkillType type, _, _) in RaceSummaryScreen.Skills) levels[type] = 0;
            Build();
            RenderBadge();
            RenderSkills();
            team.schedule.Execute(() => team.Focus());
        }

        private int Left => GameInit.StartingSkillPoints - levels.Values.Sum();

        // ------------------------------------------------------------------ building

        private void Build()
        {
            // Team: the badge preview beside the name, shapes and colours.
            VisualElement teamBox = Box("ng-team");
            big = new VisualElement();
            big.AddToClassList("ng-big");
            var bigBadge = new VisualElement { pickingMode = PickingMode.Ignore };
            bigBadge.AddToClassList("ng-big-badge");
            big.Add(bigBadge);
            teamBox.Add(big);
            var column = new VisualElement();
            column.AddToClassList("ng-team-column");
            team = Field("Team name", "ng-team-name");
            column.Add(team);
            shapes = new VisualElement();
            shapes.AddToClassList("ng-shapes");
            foreach (string s in Badges.Shapes)
            {
                var tile = new VisualElement { userData = s };
                tile.AddToClassList("ng-shape");
                var image = new VisualElement { pickingMode = PickingMode.Ignore };
                image.AddToClassList("ng-shape-image");
                image.style.backgroundImage = Badges.Shape(s);
                tile.Add(image);
                tile.RegisterCallback<ClickEvent>(_ =>
                {
                    shape = s;
                    RenderBadge();
                });
                shapes.Add(tile);
            }
            column.Add(shapes);
            colours = new VisualElement();
            colours.AddToClassList("ng-colours");
            foreach (string c in Badges.Colours)
            {
                var swatch = new VisualElement { userData = c };
                swatch.AddToClassList("ng-swatch");
                var fill = new VisualElement { pickingMode = PickingMode.Ignore };
                fill.AddToClassList("ng-swatch-fill");
                fill.style.backgroundColor = Badges.Colour(c);
                swatch.Add(fill);
                swatch.RegisterCallback<ClickEvent>(_ =>
                {
                    colour = c;
                    RenderBadge();
                });
                colours.Add(swatch);
            }
            column.Add(colours);
            teamBox.Add(column);
            body.Add(teamBox);

            // Player: name and nationality.
            VisualElement playerBox = Box("ng-player");
            player = Field("Your name", "ng-half");
            player.RegisterValueChangedCallback(_ => RenderStart());
            playerBox.Add(player);
            nation = Field("Nationality", "ng-half");
            nation.AddToClassList("last");
            BuildNationality();
            playerBox.Add(nation);
            body.Add(playerBox);
            team.RegisterValueChangedCallback(_ => RenderStart());

            // Skills: the points left as pips, then each skill's level.
            VisualElement skillBox = Box("ng-skills");
            pips = new VisualElement();
            pips.AddToClassList("ng-pips");
            skillBox.Add(pips);
            skills = new VisualElement();
            skills.AddToClassList("ng-skill-row");
            skillBox.Add(skills);
            body.Add(skillBox);

            start = menus.RedButton("Start", "panel-button", PressStart);
            body.Add(start);
        }

        private static VisualElement Box(string cls)
        {
            var box = new VisualElement();
            box.AddToClassList("ng-box");
            box.AddToClassList(cls);
            return box;
        }

        private TextField Field(string placeholder, string cls)
        {
            var field = new TextField { maxLength = MaxName };
            field.AddToClassList("ng-field");
            field.AddToClassList(cls);
            field.textEdition.placeholder = placeholder.ToUpperInvariant();
            return field;
        }

        // ------------------------------------------------------------------ nationality

        /// <summary>Typing filters the countries, names that start with the text first. Arrows and Enter pick one.</summary>
        private void BuildNationality()
        {
            countries = new VisualElement();
            countries.AddToClassList("ng-countries");
            countryList = new ScrollView(ScrollViewMode.Vertical);
            countryList.AddToClassList("ng-country-list");
            countryList.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            countryList.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            countries.Add(countryList);
            countries.style.display = DisplayStyle.None;
            menus.Popups.Add(countries);

            nation.RegisterCallback<FocusInEvent>(_ =>
            {
                Filter(nationality == null ? nation.value : "");
                nation.schedule.Execute(() => nation.SelectAll());
            });
            nation.RegisterValueChangedCallback(e =>
            {
                if (nationality != null && e.newValue == CountryName(nationality)) return;
                nationality = null;
                Filter(e.newValue);
                RenderStart();
            });
            nation.RegisterCallback<KeyDownEvent>(e =>
            {
                if (countries.style.display == DisplayStyle.None) return;
                if (e.keyCode == KeyCode.DownArrow) Highlight(highlight + 1, true);
                else if (e.keyCode == KeyCode.UpArrow) Highlight(highlight - 1, true);
                else if ((e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter) && highlight < matches.Count) Pick(matches[highlight]);
                else return;
                e.StopPropagation();
            }, TrickleDown.TrickleDown);
            // A click on the list lands after the field loses focus, so the list waits a moment before closing.
            nation.RegisterCallback<FocusOutEvent>(_ => nation.schedule.Execute(() =>
            {
                HideCountries();
                if (nationality == null) nation.SetValueWithoutNotify("");
            }).StartingIn(150));
        }

        private static string CountryName(string code) => Countries.All.FirstOrDefault(c => c.Code == code).Name ?? "";

        private void Filter(string text)
        {
            string q = (text ?? "").Trim();
            matches.Clear();
            matches.AddRange(Countries.All
                .Where(c => q.Length == 0 || c.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(c => q.Length > 0 && !c.Name.StartsWith(q, StringComparison.OrdinalIgnoreCase)));
            countryList.Clear();
            foreach ((string Code, string Name) c in matches)
            {
                Label item = menus.Text(c.Name, "ng-country");
                item.RegisterCallback<PointerDownEvent>(e =>
                {
                    e.StopPropagation();
                    Pick(c);
                });
                countryList.Add(item);
            }
            countryList.scrollOffset = Vector2.zero;
            Highlight(0, false);
            ShowCountries(matches.Count > 0);
        }

        private void Highlight(int index, bool scroll)
        {
            if (matches.Count == 0) return;
            highlight = Mathf.Clamp(index, 0, matches.Count - 1);
            for (int i = 0; i < countryList.childCount; i++) countryList[i].EnableInClassList("on", i == highlight);
            if (scroll) countryList.ScrollTo(countryList[highlight]);
        }

        private void Pick((string Code, string Name) country)
        {
            nationality = country.Code;
            nation.SetValueWithoutNotify(country.Name);
            HideCountries();
            nation.Blur();
            RenderStart();
        }

        /// <summary>Opens the list under the field, on the layer above the panel so the panel's edge doesn't cut it off.</summary>
        private void ShowCountries(bool on)
        {
            if (!on)
            {
                HideCountries();
                return;
            }
            Rect r = nation.worldBound;
            Vector2 at = menus.Popups.WorldToLocal(new Vector2(r.xMin, r.yMax));
            countries.style.left = Mathf.Round(at.x);
            countries.style.top = Mathf.Round(at.y + 4);
            countries.style.width = Mathf.Round(r.width);
            countries.style.display = DisplayStyle.Flex;
        }

        private void HideCountries() => countries.style.display = DisplayStyle.None;

        // ------------------------------------------------------------------ rendering

        private void RenderBadge()
        {
            Badges.Paint(big[0], shape, colour);
            foreach (VisualElement tile in shapes.Children()) tile.EnableInClassList("on", (string)tile.userData == shape);
            foreach (VisualElement swatch in colours.Children()) swatch.EnableInClassList("on", (string)swatch.userData == colour);
        }

        private void RenderSkills()
        {
            menus.HideTooltip();
            pips.Clear();
            for (int i = 0; i < GameInit.StartingSkillPoints; i++)
            {
                var pip = new VisualElement { pickingMode = PickingMode.Ignore };
                pip.AddToClassList("rs-pip");
                pip.AddToClassList("ng-pip");
                pip.EnableInClassList("used", i >= Left);
                pips.Add(pip);
            }

            skills.Clear();
            foreach ((PlayerSkillType type, string name, string tip) in RaceSummaryScreen.Skills)
            {
                int level = levels[type];
                var skill = new VisualElement();
                skill.AddToClassList("rs-skill");
                var head = new VisualElement();
                head.AddToClassList("ng-skill-head");
                head.Add(menus.Text(name.ToUpperInvariant(), "rs-skill-name"));
                var info = new VisualElement();
                info.AddToClassList("ng-info");
                var infoImage = new VisualElement { pickingMode = PickingMode.Ignore };
                infoImage.AddToClassList("ng-info-image");
                infoImage.style.backgroundImage = Resources.Load<Texture2D>("MenuIcons/info");
                info.Add(infoImage);
                menus.Tooltip(info, tip);
                head.Add(info);
                skill.Add(head);
                var row = new VisualElement();
                row.AddToClassList("rs-level");
                row.Add(Step("minus", level > 0, () => Spend(type, -1)));
                row.Add(menus.Text(level.ToString(CultureInfo.InvariantCulture), "rs-skill-value", true));
                row.Add(Step("plus", Left > 0 && level < RaceSummaryScreen.MaxSkill, () => Spend(type, 1)));
                skill.Add(row);
                skills.Add(skill);
            }
            RenderStart();
        }

        private static VisualElement Step(string icon, bool on, Action onClick)
        {
            var button = new VisualElement();
            button.AddToClassList("rs-step");
            var image = new VisualElement { pickingMode = PickingMode.Ignore };
            image.AddToClassList("rs-step-icon");
            image.style.backgroundImage = Resources.Load<Texture2D>("MenuIcons/" + icon);
            button.Add(image);
            button.style.visibility = on ? Visibility.Visible : Visibility.Hidden;
            button.RegisterCallback<ClickEvent>(_ => onClick());
            return button;
        }

        private void Spend(PlayerSkillType type, int points)
        {
            levels[type] = Mathf.Clamp(levels[type] + points, 0, RaceSummaryScreen.MaxSkill);
            RenderSkills();
        }

        private bool Ready => team.value.Trim().Length > 0 && player.value.Trim().Length > 0 && nationality != null;

        private void RenderStart() => start?.EnableInClassList("disabled", !Ready);

        // ------------------------------------------------------------------ start

        private void PressStart()
        {
            if (!Ready) return;
            if (Left == 0)
            {
                Begin();
                return;
            }
            int left = Left;
            menus.Dialog(dialogBody =>
            {
                dialogBody.Add(menus.Text("UNALLOCATED SKILL POINTS", "dialog-title", true));
                dialogBody.Add(menus.Text(left.ToString(CultureInfo.InvariantCulture) + " skill points will be lost.", "dialog-text"));
            }, "Cancel", "Confirm", Begin);
        }

        private void Begin()
        {
            menus.StartCareer(new NewGameOptions
            {
                PlayerName = player.value.Trim(),
                Nationality = nationality,
                TeamName = team.value.Trim(),
                Logo = shape,
                TeamColour = colour,
                Skills = new PlayerSkills
                {
                    Driver = levels[PlayerSkillType.Driver],
                    Engineer = levels[PlayerSkillType.Engineer],
                    Business = levels[PlayerSkillType.Business],
                },
            });
        }
    }
}
