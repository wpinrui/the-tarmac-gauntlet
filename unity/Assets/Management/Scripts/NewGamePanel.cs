using System;
using System.Collections.Generic;
using System.Linq;
using TarmacGauntlet.Simulation;
using UnityEngine;
using UnityEngine.UIElements;
using static TarmacGauntlet.Management.MenuKit;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// The new game panel over the empty garage: team name and badge, the player's name and nationality, and the
    /// starting skill points. Start builds the career and opens the hub. Unspent points are forfeited, so Start asks first.
    /// </summary>
    public sealed class NewGamePanel : MenuPanel
    {
        private const int MaxName = 28;

        private VisualElement big, shapes, colours, skills, start, countries;
        private TextField team, player, nation;
        private ScrollView countryList;

        private string shape, colour, nationality;
        private readonly Dictionary<PlayerSkillType, int> levels = new Dictionary<PlayerSkillType, int>();
        private readonly List<(string Code, string Name)> matches = new List<(string Code, string Name)>();
        private int highlight;

        protected override bool Shows(string slug) => slug == Menus.NewGameSlug;

        protected override bool FollowsGame => false;

        protected override void OnOpened()
        {
            shape = Badges.Shapes[0];
            colour = Badges.Colours[0];
            nationality = null;
            foreach ((PlayerSkillType type, _, _) in Skills) levels[type] = 0;
            Build();
            team.schedule.Execute(() => team.Focus());
        }

        protected override void Render()
        {
            RenderBadge();
            RenderSkills();
        }

        private int Left => GameInit.StartingSkillPoints - levels.Values.Sum();

        // ------------------------------------------------------------------ building

        private void Build()
        {
            // Team: the badge preview beside the name, shapes and colours.
            VisualElement teamBox = Group("ng-team");
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
            Body.Add(teamBox);

            // Player: name and nationality.
            VisualElement playerBox = Group("ng-player");
            player = Field("Your name", "ng-half");
            player.RegisterValueChangedCallback(_ => RenderStart());
            playerBox.Add(player);
            nation = Field("Nationality", "ng-half");
            nation.AddToClassList("last");
            BuildNationality();
            playerBox.Add(nation);
            Body.Add(playerBox);
            team.RegisterValueChangedCallback(_ => RenderStart());

            // Skills: the points left, then each skill, what it does and its level.
            skills = Group("ng-skills");
            Body.Add(skills);

            start = RedButton("Start", "panel-button", PressStart);
            Body.Add(start);
        }

        private static VisualElement Group(string cls) => Box("inset", cls);

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
            countries = Box("popup", "ng-countries");
            countryList = Scroll("popup-list");
            countries.Add(countryList);
            countries.style.display = DisplayStyle.None;
            Menus.Popups.Add(countries);

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
                Label item = Text(c.Name, "popup-item");
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
            Vector2 at = Menus.Popups.WorldToLocal(new Vector2(r.xMin, r.yMax));
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
            skills.Clear();
            skills.Add(SkillPicker(Left, t => levels[t], _ => 0, Spend, true));
            RenderStart();
        }

        private void Spend(PlayerSkillType type, int points)
        {
            levels[type] = Mathf.Clamp(levels[type] + points, 0, MaxSkill);
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
            Menus.Dialog(dialogBody =>
            {
                dialogBody.Add(Text(Num(left) + " UNSPENT SKILL POINTS", "dialog-title", true));
            }, "Cancel", "Start", Begin);
        }

        private void Begin()
        {
            Menus.StartCareer(new NewGameOptions
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
