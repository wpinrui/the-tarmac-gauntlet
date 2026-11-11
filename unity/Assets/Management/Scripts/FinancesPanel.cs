using System.Collections.Generic;
using System.Linq;
using TarmacGauntlet.Career;
using TarmacGauntlet.Simulation;
using UnityEngine.UIElements;
using static TarmacGauntlet.Management.OfficeKit;

namespace TarmacGauntlet.Management
{
    /// <summary>
    /// The Finances screen: one year at a time, with its net, its income and expenses when it has both, and every
    /// transaction, newest first. The side pane holds the full prize money table, the only place it appears, with the
    /// player's last finish marked.
    /// </summary>
    public sealed class FinancesPanel : OfficePanel
    {
        public const string Slug = "finances";
        private const int PrizeColumns = 4;

        private int? year; // kept while the game runs; null shows the current year

        protected override string ScreenSlug => Slug;

        protected override void Render()
        {
            GameState game = GameSession.State;
            PlayerTeam p = Player;
            if (game == null || p == null) return;
            Body.Clear();
            Menus.SideBody.Clear();

            List<int> years = p.Transactions.Select(t => t.Year).Append(game.CurrentYear).Distinct().OrderBy(y => y).ToList();
            int shown = year.HasValue && years.Contains(year.Value) ? year.Value : game.CurrentYear;
            int at = years.IndexOf(shown);
            Body.Add(YearStepper(Menus, shown, at > 0, at < years.Count - 1, d =>
            {
                year = years[at + d];
                Render();
            }));

            TransactionRecord[] list = p.Transactions.Where(t => t.Year == shown).Reverse().ToArray();
            double income = list.Where(t => t.Amount > 0).Sum(t => t.Amount);
            double expenses = list.Where(t => t.Amount < 0).Sum(t => t.Amount);
            double net = income + expenses;

            VisualElement hero = Box("fin-hero");
            hero.Add(Figure(Signed(net), "NET", net >= 0 ? "of-green" : "of-red", "fin-net"));
            hero.Add(Figure(Signed(income), "INCOME", "of-green", "fin-sub-line"));
            hero.Add(Figure(Menus.Money(expenses), "SPENDING", "of-red", "fin-sub-line"));
            Body.Add(hero);

            ScrollView rows = Scroll("rs-scroll");
            foreach (TransactionRecord t in list)
            {
                VisualElement row = Box("of-row");
                row.Add(Icon(IconFor(t.Category), "fin-icon"));
                row.Add(Menus.Text(t.Description, "of-fill"));
                Label amount = Menus.Text(Signed(t.Amount), "fin-amount", true);
                amount.AddToClassList(t.Amount >= 0 ? "of-green" : "of-red");
                row.Add(amount);
                rows.Add(row);
            }
            Body.Add(rows);

            RenderPrizes(game);
        }

        private VisualElement Figure(string value, string label, string colour, string size)
        {
            VisualElement figure = Box("fin-figure");
            Label v = Menus.Text(value, size, true);
            v.AddToClassList(colour);
            figure.Add(v);
            figure.Add(Menus.Text(label, "rs-stat-label"));
            return figure;
        }

        /// <summary>All 100 places in four columns, read down. The player's last finish is marked.</summary>
        private void RenderPrizes(GameState game)
        {
            int last = game.RaceHistory.LastOrDefault()?.Results.FirstOrDefault(r => r.TeamId == GameActions.PlayerTeamId)?.Position ?? 0;
            IReadOnlyList<double> table = PostRace.PrizeTable;
            int perColumn = (table.Count + PrizeColumns - 1) / PrizeColumns;
            Menus.SideBody.Add(Menus.Text("PRIZE MONEY BY FINISH", "section-label"));
            VisualElement grid = Box("prizes");
            for (int c = 0; c < PrizeColumns; c++)
            {
                VisualElement column = Box("prize-col");
                if (c == PrizeColumns - 1) column.AddToClassList("last");
                for (int i = c * perColumn; i < (c + 1) * perColumn && i < table.Count; i++)
                {
                    int place = i + 1;
                    VisualElement cell = Box("prize-cell");
                    cell.EnableInClassList("me", place == last);
                    Label pos = Menus.Text(Num(place), "prize-pos");
                    if (place <= 3) pos.AddToClassList("p" + place);
                    cell.Add(pos);
                    cell.Add(Menus.Text(Menus.Money(table[i]), "prize-amount", true));
                    column.Add(cell);
                }
                grid.Add(column);
            }
            Menus.SideBody.Add(grid);
        }

        private static string IconFor(TransactionCategory category) => category switch
        {
            TransactionCategory.CarPurchase or TransactionCategory.CarSale => "new-car-dealer",
            TransactionCategory.Upgrade => "upgrade",
            TransactionCategory.SpareParts => "car-workshop",
            TransactionCategory.Tyres => "tyre",
            TransactionCategory.DriverSalary => "salary",
            TransactionCategory.DriverBuyout => "buyout",
            TransactionCategory.CrewCost => "crew",
            TransactionCategory.PrizeMoney => "trophy",
            _ => "fuel",
        };
    }
}
