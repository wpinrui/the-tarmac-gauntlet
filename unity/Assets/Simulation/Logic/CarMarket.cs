#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace TarmacGauntlet.Simulation
{
    /// <summary>Port of simulation/carMarket.ts.</summary>
    public static class CarMarketLogic
    {
        // ---------------------------------------------------------------------------
        // Tunable constants
        // ---------------------------------------------------------------------------

        /// <summary>Fraction of original value lost per year of age.</summary>
        private const double AgeDepreciationPerYear = 0.03;
        /// <summary>Minimum age factor: a car never drops below this due to age alone.</summary>
        private const double MinAgeFactor = 0.50;
        /// <summary>Minimum condition factor: even a 0%-condition car retains this fraction of aged value.</summary>
        private const double ConditionFloor = 0.50;
        /// <summary>Fraction of each installed upgrade pack's cost recovered in the used listing price.</summary>
        private const double UpgradeResaleFraction = 0.50;
        /// <summary>Fraction of market value the selling team receives (dealer margin).</summary>
        private const double BaseResaleFraction = 0.60;
        /// <summary>Maximum additional sale price from max Business skill (15% above base resale).</summary>
        private const double MaxSaleSkillBonus = 0.15;
        /// <summary>Maximum discount on car purchases at max Business skill.</summary>
        private const double MaxPurchaseDiscount = 0.15;
        /// <summary>Maximum Business skill level.</summary>
        private const double MaxBusinessSkill = 20;
        /// <summary>Oldest car that can appear in a generated used inventory (years).</summary>
        private const int MaxUsedAge = 10;
        /// <summary>Minimum condition of a generated used car.</summary>
        private const double UsedConditionMin = 30;
        /// <summary>Maximum condition of a generated used car.</summary>
        private const double UsedConditionMax = 90;
        /// <summary>Per-pack probability that a randomly generated used car has that pack installed.</summary>
        private const double UsedUpgradeProbability = 0.33;
        /// <summary>Default number of used listings generated per year.</summary>
        public const int DefaultUsedInventoryCount = 20;

        // ---------------------------------------------------------------------------
        // New car listings
        // ---------------------------------------------------------------------------

        /// <summary>Returns one listing per model, all at list price (age 0, full condition, no upgrades).</summary>
        public static IReadOnlyList<NewCarListing> GetNewCarListings(IReadOnlyList<CarModel> models) =>
            models.Select(m => new NewCarListing { ModelId = m.Id, Price = m.Price }).ToList();

        // ---------------------------------------------------------------------------
        // Market value (intrinsic value of a used car instance)
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Calculates the intrinsic market value of a used car.
        ///
        /// Formula:
        ///   ageFactor    = max(MIN_AGE_FACTOR, 1 − age × AGE_DEPRECIATION_PER_YEAR)
        ///   condFactor   = CONDITION_FLOOR + (1 − CONDITION_FLOOR) × (condition / 100)
        ///   baseValue    = model.price × ageFactor × condFactor
        ///   upgradeBonus = sum of (pack.cost × UPGRADE_RESALE_FRACTION) for installed packs
        ///   marketValue  = round(baseValue + upgradeBonus)
        /// </summary>
        public static double CalculateCarMarketValue(CarInstance car, CarModel model)
        {
            var ageFactor = Math.Max(
                MinAgeFactor,
                1 - car.Age * AgeDepreciationPerYear);
            var conditionFactor =
                ConditionFloor + (1 - ConditionFloor) * (car.Condition / 100);
            var baseValue = model.Price * ageFactor * conditionFactor;

            double upgradeBonus = 0;
            foreach (var pack in model.UpgradePacks)
            {
                if (IsInstalled(car.InstalledUpgrades, pack.Type))
                {
                    upgradeBonus += pack.Cost * UpgradeResaleFraction;
                }
            }

            return JsMath.Round(baseValue + upgradeBonus);
        }

        /// <summary>C# stand-in for TS <c>installedUpgrades[pack.type]</c>.</summary>
        private static bool IsInstalled(InstalledUpgrades upgrades, UpgradePackType type) => type switch
        {
            UpgradePackType.Power => upgrades.Power,
            UpgradePackType.Handling => upgrades.Handling,
            UpgradePackType.Comfort => upgrades.Comfort,
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };

        // ---------------------------------------------------------------------------
        // Sale price (what a team receives when selling)
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Calculates the sale price a team receives when selling a car.
        ///
        /// salePrice = marketValue × BASE_RESALE_FRACTION × (1 + skillBonus)
        /// </summary>
        /// <param name="businessSkill">Seller's Business skill (0 = no bonus; typically 0 for AI teams).</param>
        public static double CalculateSalePrice(CarInstance car, CarModel model, int businessSkill = 0)
        {
            var marketValue = CalculateCarMarketValue(car, model);
            var skillBonus = (businessSkill / MaxBusinessSkill) * MaxSaleSkillBonus;
            return JsMath.Round(marketValue * BaseResaleFraction * (1 + skillBonus));
        }

        // ---------------------------------------------------------------------------
        // Purchase price (what a team pays after Business discount)
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Applies a Business skill discount to any car purchase (new or used).
        ///
        /// discount = (businessSkill / MAX_BUSINESS_SKILL) × MAX_PURCHASE_DISCOUNT
        /// </summary>
        /// <param name="businessSkill">Buyer's Business skill (0 = no discount; typically 0 for AI teams).</param>
        public static double CalculatePurchasePrice(double listPrice, int businessSkill = 0)
        {
            var discount = (businessSkill / MaxBusinessSkill) * MaxPurchaseDiscount;
            return JsMath.Round(listPrice * (1 - discount));
        }

        // ---------------------------------------------------------------------------
        // Used car generation
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Generates a single used car listing from a model with randomised age, condition,
        /// and upgrade state.
        /// </summary>
        public static UsedCarListing GenerateUsedListing(CarModel model, Func<double> random)
        {
            var age = 1 + (int)Math.Floor(random() * MaxUsedAge); // [1, MAX_USED_AGE]
            var condition = JsMath.Round(
                UsedConditionMin +
                    random() * (UsedConditionMax - UsedConditionMin)); // [USED_CONDITION_MIN, USED_CONDITION_MAX]
            var installedUpgrades = new InstalledUpgrades
            {
                Power    = random() < UsedUpgradeProbability,
                Handling = random() < UsedUpgradeProbability,
                Comfort  = random() < UsedUpgradeProbability,
            };

            var tempInstance = new CarInstance
            {
                Id = "",
                ModelId = model.Id,
                Age = age,
                Condition = condition,
                InstalledUpgrades = installedUpgrades,
            };
            var price = CalculateCarMarketValue(tempInstance, model);
            var id = $"used-{model.Id}-{RandomIdSuffix(random)}";

            return new UsedCarListing
            {
                Id = id,
                ModelId = model.Id,
                Age = age,
                Condition = condition,
                InstalledUpgrades = installedUpgrades,
                Price = price,
            };
        }

        /// <summary>TS <c>Math.floor(random() * 1_000_000_000)</c>, formatted as a JS template literal would.</summary>
        private static string RandomIdSuffix(Func<double> random) =>
            ((long)Math.Floor(random() * 1_000_000_000)).ToString(System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>
        /// Generates a rotating used car inventory by randomly sampling from all models.
        /// </summary>
        /// <param name="models">Full car model catalogue.</param>
        /// <param name="count">Number of listings to generate.</param>
        /// <param name="random">Injectable random source.</param>
        public static IReadOnlyList<UsedCarListing> GenerateUsedInventory(
            IReadOnlyList<CarModel> models,
            int count,
            Func<double> random)
        {
            if (models.Count == 0) return Array.Empty<UsedCarListing>();
            var listings = new List<UsedCarListing>();
            for (var i = 0; i < count; i++)
            {
                var model = models[(int)Math.Floor(random() * models.Count)];
                listings.Add(GenerateUsedListing(model, random));
            }
            return listings;
        }

        /// <summary>TS default for <c>count</c>: DEFAULT_USED_INVENTORY_COUNT.</summary>
        public static IReadOnlyList<UsedCarListing> GenerateUsedInventory(
            IReadOnlyList<CarModel> models,
            Func<double> random) =>
            GenerateUsedInventory(models, DefaultUsedInventoryCount, random);

        /// <summary>Probability that the F1 car appears in the used market (when eligible).</summary>
        public const double F1UsedProbability = 0.20;

        /// <summary>
        /// Generates a used car inventory with 2 cars per class.
        ///
        /// For classes F–A: picks a random model from that class and generates a used listing.
        /// For F1: only included with F1_USED_PROBABILITY (20%) chance, and only if
        /// <c>includeF1</c> is true (caller checks win condition and affordability).
        /// </summary>
        public static IReadOnlyList<UsedCarListing> GenerateUsedInventoryByClass(
            IReadOnlyList<CarModel> models,
            bool includeF1,
            Func<double> random)
        {
            if (models.Count == 0) return Array.Empty<UsedCarListing>();

            var classes = new[] { CarClass.F, CarClass.E, CarClass.D, CarClass.C, CarClass.B, CarClass.A };
            var listings = new List<UsedCarListing>();

            // Guaranteed shitbox: Neutrino Sago (f-01), max age, minimum condition, no upgrades.
            // Always available, always purchasable (plot armour lives here).
            var shitboxModel = models.FirstOrDefault(m => m.Id == "f-01");
            if (shitboxModel != null)
            {
                var shitbox = new CarInstance
                {
                    Id = "",
                    ModelId = shitboxModel.Id,
                    Age = 15 + (int)Math.Floor(random() * 11), // 15–25 years old
                    Condition = UsedConditionMin,
                    InstalledUpgrades = new InstalledUpgrades { Power = false, Handling = false, Comfort = false },
                };
                var price = CalculateCarMarketValue(shitbox, shitboxModel);
                listings.Add(new UsedCarListing
                {
                    Id = $"used-shitbox-{RandomIdSuffix(random)}",
                    ModelId = shitboxModel.Id,
                    Age = shitbox.Age,
                    Condition = UsedConditionMin,
                    InstalledUpgrades = new InstalledUpgrades { Power = false, Handling = false, Comfort = false },
                    Price = price,
                });
            }

            foreach (var cls in classes)
            {
                // Exclude f-01 (Neutrino Sago) from randomisation: it's the designated shitbox above
                var classModels = models.Where(m => m.CarClass == cls && m.Id != "f-01").ToList();
                if (classModels.Count == 0) continue;
                for (var i = 0; i < 2; i++)
                {
                    var model = classModels[(int)Math.Floor(random() * classModels.Count)];
                    listings.Add(GenerateUsedListing(model, random));
                }
            }

            // F1: 20% chance, only if eligible
            if (includeF1 && random() < F1UsedProbability)
            {
                var f1Models = models.Where(m => m.CarClass == CarClass.F1).ToList();
                if (f1Models.Count > 0)
                {
                    listings.Add(GenerateUsedListing(f1Models[0], random));
                }
            }

            return listings;
        }
    }
}
