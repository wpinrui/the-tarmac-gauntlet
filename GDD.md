# Game Design Document: The 24h Tarmac Gauntlet

**Genre:** Endurance Racing Management Simulator
**Tech Stack:** Electron / React / TypeScript
**Version:** 1.0 (v1 Scope)

---

## 1. Game Overview

### Concept

The player builds an endurance racing team from nothing. Starting with a junkyard car, no crew, and no co-drivers, they compete in an annual 24-hour race — The 24h Tarmac Gauntlet — against a 100-car field spanning the full performance spectrum. Each year, prize money funds progress: better cars, hired drivers, pit crew, and personal skill development. Over a ~15-year career, the player climbs from the back of the grid to the front.

The player can own **multiple cars** but enters **one car per race**. They manage everything: car purchases, upgrades, driver hiring, crew size, skill allocation, and real-time race strategy. The race plays out in 24 real-time minutes — a compressed simulation of a full 24-hour endurance event.

### Win Condition

Win The 24h Tarmac Gauntlet. Credits roll, but the player can continue racing indefinitely.

### Core Loop

1. **Prepare** — Buy/sell cars, apply upgrades, hire drivers, set crew size, buy spare parts and tyre sets, allocate skill points
2. **Race** — 24 minutes of real-time racing with pit stop and driver instruction decisions
3. **Review** — Race summary, newspaper, prize money awarded
4. Repeat

---

## 2. The Race

### Duration & Time Compression

- **24 real-time minutes** per race, no time acceleration
- Visual lap time: **~30 seconds** per lap = **48 laps** per race
- All degradation systems (tyre wear, fuel consumption, driver fatigue, car condition) are compressed at **60:1** — they behave as though a full 24-hour race is occurring

### Pausing

The player can pause the game during a race. While paused, the **screen goes completely black** — no track map, no standings, no car status, no strategy information. This prevents using pause as a free decision-making window. Unpausing resumes the race exactly where it left off.

### Starting Grid

Grid positions are determined by **team prestige** (see §8: Team Prestige). Higher prestige = further up the grid. The player starts at the back in year 1 with zero prestige.

### Lap Time Simulation

Each car's lap time is calculated every lap:

```
base_lap_time (from car Power and Handling)
  × driver_pace_modifier (from driver Pace stat)
  × instruction_mode_modifier (Push = faster, Normal = 1.0, Conserve = slower)
  × tyre_wear_modifier (degraded tyres = slower)
  × fuel_load_modifier (each litre adds a fixed percentage to lap time)
  × car_condition_modifier (lower condition = slower)
  + random_variance (inversely scaled by driver Consistency; worse on Push, better on Conserve)
```

All modifiers are multiplicative on the base lap time. Random variance is additive.

### Driver Instructions

A three-state toggle — **Push / Normal / Conserve** — gives the player continuous strategic control during the race. It applies to whoever is currently driving the player's car, including the player character.

| Dimension | Push | Normal | Conserve |
|-----------|------|--------|----------|
| Lap time | Faster | Baseline | Slower |
| Tyre wear rate | Increased | Baseline | Reduced |
| Fuel consumption rate | Increased | Baseline | Reduced |
| Fatigue buildup rate | Increased | Baseline | Reduced |
| Consistency (random variance) | Worse | Baseline | Better |
| Issue/failure probability | Elevated | Baseline | Reduced |

**Timing:** Mode changes take effect on the **next lap**, not mid-lap.

**Pit stop reset:** The mode **resets to Normal on every pit stop**. The player must actively re-select Push or Conserve after each stop, forcing re-evaluation with fresh tyres, fuel, and potentially a new driver.

**Flat modifiers:** The same multipliers apply regardless of driver stats. Driver stats determine the base rates; instruction mode scales them uniformly.

**Push mode is dangerous.** The elevated issue/failure probability should be noticeable, especially on low-condition or old cars. Pushing a fatigued driver in a fragile car should carry real risk of mechanical failure or crash — not just faster degradation.

**Real-time advisory recalculation:** When the player changes modes, all resource estimates (e.g., "tyres: 6 laps remaining") update immediately to reflect the new wear rates.

**Player-only.** AI teams race on Normal at all times. Driver instructions are a player agency mechanic, not a simulation-wide system.

### Strategy Factors

The game displays **advisory information** showing how many laps each resource will last at the current rate (accounting for the active instruction mode). The player always knows which resource is the current limiting factor.

**Tyres** — Degrade over laps, progressively increasing lap time. Degradation rate is reduced by the driver's Smoothness stat and the car's Tyre Durability stat. Changed during pit stops by consuming a **tyre set** (see §7: Tyre Sets). If the player has no remaining sets, tyres cannot be changed — the car continues on worn rubber. One compound (dry) in v1.

**Fuel** — Depletes over laps. Each litre in the tank adds a fixed percentage to lap time (heavier car = slower). The car's Fuel Efficiency stat determines litres consumed per lap. Fuel Capacity sets the maximum fuel load. Refuelled during pit stops.

**Driver fatigue** — Builds over continuous driving time. Rate is reduced by the car's Comfort stat and the driver's Stamina stat. Fatigued drivers are slower (worse pace modifier) and less consistent (higher random variance). Relieved by swapping to another driver during a pit stop.

**Car condition** — Degrades continuously during the race. Degradation rate increases with car age and is reduced by the player's Engineer skill. Lower condition reduces all effective car stats and increases issue/failure probability. Cannot be restored during a race; repaired between races (see §3: Car Condition).

### Pit Stops

The player presses a **"Pit Next Lap"** button to call their car in. Before the car arrives, the player configures the stop:

- **Fuel:** How many litres to add (up to remaining tank capacity)
- **Tyres:** Whether to change to fresh tyres (consumes one tyre set; unavailable if no sets remain)
- **Driver:** Which driver takes over next (if the team has multiple drivers)

Configuration can be adjusted up until the car enters the pit. Once the stop begins, the configuration is locked.

**Pit stop duration** is determined by:

| Factor | Effect |
|--------|--------|
| Car's Pit Stop Time stat | Base duration (lower = faster) |
| Tasks performed | Each task (refuel, tyre change, driver swap, issue fixes) adds time |
| Crew size | More crew = faster. 0 crew = player character does everything solo (very slow) |
| Player's Engineer skill | Reduces total pit duration |

Issue repairs add time based on their **work units** — a labour measure that scales with crew size and Engineer skill just like other pit tasks. Minor issues are quick to fix; major issues and crash damage take much longer. Multiple fixes in a single stop stack additively.

A typical race involves **8–12 pit stops**.

The player is the **team strategist at all times** — they make all pit stop and driver instruction decisions regardless of whether the player character is currently behind the wheel.

### Issues & Failures

#### Issues

Issues are mechanical problems that occur randomly during the race. They degrade car performance until fixed in the pit.

**37 issue types** span 11 mechanical systems (engine, brakes, suspension, cooling, etc.) plus crash damage, across three severity tiers:

| Severity | Count | Debuff Strength | Spare Parts | Work Units | Relative Frequency |
|----------|-------|----------------|-------------|------------|-------------------|
| Minor | 16 | Small (4–12%) | 1 | 8–20 | Common |
| Medium | 7 | Moderate (12–20%) | 2–3 | 28–40 | Uncommon |
| Major | 10 | Severe (25–35%) | 4–5 | 45–60 | Rare |

Each issue type has:

| Property | Description |
|----------|-------------|
| Description | What went wrong (e.g., "engine misfire," "cracked exhaust manifold") |
| Stat debuffs | Percentage reductions to one or more car stats (e.g., −15% Power, −10% Handling) |
| Weight | Relative likelihood of being drawn when an issue occurs (higher = more common) |
| Spare parts cost | Number of spare parts consumed to fix (see §7: Spare Parts) |
| Work units | Labour required to fix — converted to pit time by crew size and Engineer skill |

**Stat debuffs replace flat lap time penalties.** An issue like "engine misfire" doesn't add a fixed number of seconds per lap — it reduces the car's effective Power stat by a percentage, which flows through the lap time formula naturally. This means issues interact with the car's existing stats: the same issue on a powerful car costs more raw pace than on a slow one.

**Multiple issues stack.** Debuffs to the same stat are summed, then applied multiplicatively: `effective_stat = base_stat × (1 − total_debuff)`. A car with a 10% Power debuff from one issue and a 20% Power debuff from another runs at 70% effective Power.

**Fixing is always optional.** The player is never forced to spend parts. If the player lacks sufficient spare parts, the issue simply cannot be fixed — the car continues with reduced stats.

#### Issue Probability (Two-Step System)

Issues use a **two-step probability system** each lap:

**Step 1 — Base roll:** A single roll determines whether any issue occurs this lap. The base probability is modified by:
- **Car condition** — lower condition significantly increases risk
- **Car Reliability stat** — higher Reliability reduces risk
- **Instruction mode** — Push increases risk; Conserve reduces it

**Step 2 — Weighted pick:** If the base roll succeeds, a weighted draw selects which issue occurs from the full catalogue. Higher-weight issues (minor problems) are drawn far more often than low-weight issues (major problems). Issues already active on the car are excluded from the draw — the same issue cannot occur twice simultaneously.

This two-step system means the *frequency* of issues is governed by car state and driving mode, while the *severity distribution* is governed by the catalogue weights.

#### Failures

Failures are checked independently from issues each lap. The base failure probability is much lower than issues but is more strongly affected by car condition and age.

Failure probability is influenced by: car condition (strongest factor), car age, car Reliability stat, driver Safety stat, and instruction mode (Push doubles failure risk; Conserve reduces it by 40%).

When a failure occurs, it is either a **mechanical failure** (75%) or a **crash** (25%):

**Terminal mechanical failure** (e.g., blown engine) — the car stops permanently. The team still receives prize money based on total laps completed.

**Crash** — the driver's **Safety stat** determines whether they survive:
- A base survival chance is boosted by higher Safety — a high-Safety driver survives far more often than a low-Safety one
- **Survived crash:** The car is not retired. Instead, a **crash issue** is spawned (e.g., "front wing damage," "bent suspension arm") — a severe stat debuff that can be fixed in the pit like any other issue. The car continues racing, damaged but alive.
- **Terminal crash:** The driver did not survive the crash. The car is retired permanently.

Non-terminal crashes create dramatic moments: the player's car spins, picks up heavy damage, but limps back to the pits for emergency repairs — if they have the spare parts and can afford the pit time.

#### Crash Issues

| Count | Debuff Strength | Spare Parts | Work Units | Frequency |
|-------|----------------|-------------|------------|-----------|
| 4 | Severe (10–35%) | 3–5 | 45–60 | Only from survived crashes |

Crash issues (e.g., "front wing damage," "rear impact damage," "bent suspension arm") behave identically to mechanical issues once spawned — fixable in the pit, stat debuffs stack with other active issues, optional to repair.

### Race Results

- All 100 cars are ranked by **total laps completed**
- There is no distinction between finishers and retired cars — a car that fails at lap 30 simply has 30 laps on the board
- Prize money is distributed based on this ranking (see §7: Prize Money)

### Race UI Layout

**Left side:**

- **Track map** (top) — Overhead view of the circuit. All 100 cars are represented as solid coloured discs moving in real time. Disc speed varies by track geometry: faster on straights, slower through corners.
- **Standings list** (below) — Scrollable list of all 100 cars showing overall position, class, class position, team, laps, and gap. Scroll position is player-relative: the list keeps the player's car at whatever position the player last scrolled to. The list only auto-adjusts if the player's car would move off the visible area.

**Right side (all panels visible simultaneously):**

- **Commentary feed** — Scrolling text ticker of race events
- **Pit stop panel** — "Pit Next Lap" button and stop configuration (fuel, tyres, driver)
- **Car status** — Tyre wear bar, fuel bar, car condition bar
- **Driver status** — Current driver name, fatigue bar, current instruction mode (colour-coded)
- **Driver instruction toggle** — Three-state selector: Push / Normal / Conserve

---

## 3. Cars

### Overview

- **31 car models** across **7 performance classes** (F, E, D, C, B, A, F1)
- All 100 cars race for **overall position** — classes are not separate races
- Classes give players **intermediate goals**: finishing top of your class is a milestone even if overall position is mid-pack
- New car dealer prices range from **$4,500** (Class F) to **$3,000,000** (Class A). F1 is not sold new (see §3: F1 Class Rules). Second-hand prices can be much lower — a heavily aged Class F car can cost under $1,000.
- The player can own **multiple cars** but enters **one car per race**
- The player is expected to **win with a Class A car**. The F1 car is an achievement/trophy unlock — outrageously fast but fragile, expensive, and impractical. Reaching it is aspirational, not required.

### Car Stats

Every car has eight stats. Each stat has a **base value** (available at purchase) and a **potential value** (unlocked through upgrade packs). Stats not targeted by any upgrade pack have potential equal to base.

| Stat | Description | Direction |
|------|-------------|-----------|
| **Power** | Straight-line speed | Higher = faster |
| **Handling** | Cornering ability | Higher = faster |
| **Fuel Efficiency** | How efficiently the car uses fuel per lap | Higher = less fuel consumed |
| **Tyre Durability** | How gently the car treats its tyres per lap | Higher = less tyre wear |
| **Comfort** | How little fatigue the car inflicts on the driver | Higher = slower fatigue buildup |
| **Reliability** | Resistance to mechanical issues and failures | Higher = fewer problems |
| **Pit Stop Time** | Base duration for pit work | **Lower = faster** |
| **Fuel Capacity** | Maximum litres of fuel the car can carry | Higher = more fuel |

Power and Handling determine the base lap time. Fuel Efficiency, Tyre Durability, Comfort, and Reliability modify degradation and risk rates during the race. Pit Stop Time and Fuel Capacity affect pit strategy.

### Classes & Pricing

All prices below are **new car dealer (MSRP)** prices. Second-hand prices are derived from the sale price formula and can be significantly lower.

| Class | Cars | New Dealer Price Range | Character |
|-------|------|----------------------|-----------|
| **F** | 6 | $4,500 – $7,500 | Slow, efficient, reliable. Junkyard shitboxes when aged. |
| **E** | 5 | $20,000 – $30,000 | Everyday cars. Slightly faster, still forgiving. |
| **D** | 4 | $70,000 – $85,000 | Sports cars. Faster but less efficient, less comfortable. |
| **C** | 5 | $190,000 – $280,000 | Performance cars and GTs. Diverse trade-offs. |
| **B** | 5 | $750,000 – $950,000 | Supercars. Very fast, expensive to run, low reliability and comfort. |
| **A** | 5 | $2,500,000 – $3,000,000 | Le Mans prototypes. Near-maximum performance. The class you win with. |
| **F1** | 1 | $15,000,000 | Trophy car. Not sold new — only available used (see F1 Class Rules). |

The full car roster with base stats and upgrade potentials is defined in `car_roster.md`.

### Upgrade Packs

Upgrade packs are all-or-nothing purchases. Each pack immediately raises the affected stats from base to potential.

| Pack | Primary Effect | Secondary Effect |
|------|---------------|-----------------|
| **Power Pack** | Power → potential | Fuel Efficiency → potential |
| **Handling Pack** | Handling → potential | Tyre Durability → potential |
| **Comfort Pack** | Comfort → potential | — |

**Not every car has all three packs.** Pack availability varies by class:

| Class | Power Pack | Handling Pack | Comfort Pack |
|-------|-----------|--------------|-------------|
| F | Yes | Yes | Yes |
| E | Yes | Yes | Yes |
| D | Yes | Yes | Yes |
| C | Yes | Yes | Yes |
| B | Yes | Yes | Some cars only |
| A | — | — | Yes |
| F1 | — | — | — |

Class A cars are already near-maximum performance — the only upgrade path is Comfort. The F1 car has no upgrades at all; it is what it is.

Upgrade packs are a cheaper path to more performance than buying a new car, but each car has a ceiling. Once all available packs are installed, the only way forward is to buy a higher-class car.

### F1 Class Rules

The F1 class has unique handling that differs from every other class. This section is the canonical reference.

- **Not sold new.** F1 cars never appear in the new car dealer.
- **Rare second-hand appearance.** An F1 car may appear in the second-hand dealer only if **all three conditions** are met:
  1. The player has **won The 24h Tarmac Gauntlet** at least once (finished P1 in any past race)
  2. The player's current budget is **≥ $15,000,000** (the F1 MSRP — a wealth gate, even though the used listing price is lower)
  3. A **20% random roll** succeeds during that year's inventory generation
- **No AI teams run F1 cars.** The F1 class exists exclusively for the player.
- **Zero upgrade packs.** The F1 car cannot be improved — it is what it is.
- **Trophy, not target.** The player is expected to win the race with a Class A car. The F1 car is an achievement unlock for players who have already won and want to experience the ultimate machine — outrageously fast but catastrophically fragile, thirsty, and uncomfortable.

### Car Age

Every car has an **age** in years. A new car starts at age 0. Age increments by 1 each year and **cannot be reversed**.

Effects of age:
- A small penalty applied across all effective car stats (gradual, not dramatic)
- Faster car condition degradation during races

### Car Condition

Condition is a **percentage scale** representing the car's current mechanical health.

**During a race:**
- Condition degrades continuously each lap
- Degradation rate depends on car age (older = faster) and the player's Engineer skill (higher = slower)
- Condition acts as a **multiplier on all effective car stats** — lower condition means proportionally reduced performance
- Low condition increases issue and failure probability
- Condition **cannot be restored mid-race**

**Between races:**
- **Repair** restores condition fully, accounting for the car's current age — it undoes race-related wear but cannot reverse permanent age-based stat decline
- Repair consumes **spare parts** (see §7: Spare Parts). The cost in parts scales with the condition deficit and the car's class — lower-class cars require fewer parts, higher-class cars require more
- The spare parts cost for repair is reduced by the player's **Business skill**
- Repair is optional — the player can skip it to save parts, accepting worse performance and higher risk. If the player has no spare parts, they cannot repair

### Car Market

**Depreciation & pricing:**
- Used car market value depreciates at **3% per year** of age, with a **price floor of 50% of MSRP** — a car never depreciates below half its new price due to age alone
- Condition further reduces value: a car at 30% condition is worth significantly less than one at 90%
- Installed upgrade packs add value (50% of the pack's cost)

**New car dealer:**
- All **30 car models** (Classes F through A) always available at list price (MSRP)
- F1 is **never sold new** (see §3: F1 Class Rules)
- Purchased cars: age 0, full condition, no upgrade packs

**Second-hand dealer:**
- Rotating inventory that changes each year:
  - **1 guaranteed shitbox** — the Neutrino Sago (cheapest Class F car), aged 15–25 years, 30% condition, no upgrades. Always available. This is the economy's entry point.
  - **2 cars per class** (F through A) — randomly selected models with randomised age (1–10 years), condition (30–90%), and upgrades (33% chance per pack)
  - **F1 conditional** — an F1 car may appear if the player meets eligibility criteria (see §3: F1 Class Rules)
- Total inventory: **13–14 listings per year**
- The Neutrino Sago only appears as the guaranteed shitbox — it is excluded from random Class F generation

**Selling:**
- The player can sell any car from the garage between races
- Sale price is influenced by: car model, age, condition, installed upgrades, and the player's Business skill
- If the player sells all their cars and cannot afford a replacement, the beg mechanic applies (see §7: Beg Mechanic)

---

## 4. Drivers

### Overview

The player's team fields **1 to 4 drivers** per race, including the player character. During the race, drivers rotate via pit stop swaps — only one driver is in the car at a time. The player acts as **team strategist at all times**, making pit stop and driver instruction decisions regardless of who is driving.

### Player Character as Driver

- Always available as a driver option — no contract or salary required
- Stats are determined by the **Driver skill** (see §6: Player Skills)
- At Driver skill level N (out of 20), all five driver stats = **N × 5** (out of 100)
- At Driver skill 0, the player character has 0 in all stats — a terrible driver

### Driver Stats

All drivers (hired and player character) share five stats on a **0–100 scale**:

| Stat | Effect |
|------|--------|
| **Pace** | Lap time modifier. Higher = faster laps. |
| **Consistency** | Lap time variance. Higher = more predictable. |
| **Stamina** | Fatigue buildup rate. Higher = slower fatigue. |
| **Safety** | Crash and failure probability. Higher = fewer incidents. |
| **Smoothness** | Tyre wear rate. Higher = less tyre degradation. |

### Driver Pool & Lifecycle

- **310 drivers** in the global pool at all times
- Each driver's stats follow a **sinusoidal curve** peaking around **age 30** — rising through the 20s, peaking near 30, declining through the 30s and 40s
- Individual stats can have small phase offsets (some peak slightly earlier or later), so no two same-age drivers are identical
- Each driver has a **market value** calculated from current stats

**Annual turnover:**
- The **15 lowest-rated drivers** (by total stat sum) are retired each year
- Replaced by **15 new 18-year-old rookies** with randomly generated low starting stats
- This creates a living market: cheap young talent, expensive peak-age stars, and declining veterans at a discount

### Contracts & Salaries

Drivers are hired on **1, 2, or 3 year contracts**. Salary is paid annually.

| Contract Length | Salary Modifier |
|----------------|----------------|
| 1 year | Full price |
| 2 years | 10% discount |
| 3 years | 20% discount |

The player's **Business skill** further amplifies contract discounts.

**Buyout:** A contract can be terminated early by paying **remaining salary × 1.5**.

**Expiry:** When a contract ends, the driver becomes a free agent — available for re-signing by any team.

AI teams follow the same contract system. A desired driver may be locked into an AI team's contract and unavailable until it expires or is bought out.

| Driver Tier | Annual Salary Range |
|-------------|-------------------|
| Bottom-tier rookies | $500 – $2,000 |
| Solid midfielders | $5,000 – $20,000 |
| Elite peak-age drivers | $50,000 – $150,000 |

### Driver Market

- The player has **first pick** each year — they hire before AI teams make their selections
- AI team signings are determined by a simple algorithm after the player has chosen
- Drivers under contract with any team are unavailable unless bought out

---

## 5. Crew

The pit crew is a **headcount** — no individual identities or stats.

| Property | Value |
|----------|-------|
| Maximum size | 16 |
| Cost | $2,000 per crew member per year |
| Effect | More crew = faster pit stops |
| Minimum | 0 — player character performs all pit work solo (very slow) |

The player's **Business skill** reduces crew costs. The player's **Engineer skill** reduces pit stop duration on top of crew size benefits.

| Crew Size | Annual Cost |
|-----------|------------|
| 0 (solo) | $0 |
| 4 (small) | $8,000 |
| 8 (medium) | $16,000 |
| 16 (full) | $32,000 |

Crew size is set between races and remains fixed for the duration of that race.

---

## 6. Player Skills

### Allocation & Progression

- **15 points** allocated at game start (during new game setup)
- **3 points** earned after every race, regardless of result
- Each skill has a **maximum of 20**
- Total: 3 skills × 20 = **60 points** = 15 starting + (15 years × 3)
- All skills **fully maxed by year 15**, coinciding with the career progression arc
- Points earned after a race are allocated **before any between-race spending**

### Skill Definitions

| Skill | Domain | Effects |
|-------|--------|---------|
| **Driver** | On-track | Sets the player character's five driver stats: Pace, Consistency, Stamina, Safety, Smoothness. Each = skill level × 5. |
| **Engineer** | Garage & race | Reduces pit stop duration (§2). Slows car condition degradation during races (§3). Does **not** affect consumable costs (spare parts, tyres, fuel). |
| **Business** | Wallet | Discounts on car purchases, upgrade packs, driver salaries, crew costs, spare parts, tyre sets, and fuel. Reduces spare parts cost for repairs. Improves car sale prices. |

### Build Archetypes

The initial 15-point allocation defines the early career path:

- **Early Driver** — Personally drag a bad car to results it shouldn't get. High player stats compensate for poor equipment.
- **Early Engineer** — Keep a cheap car running well. Fast pit stops and slow condition degradation offset slower raw pace.
- **Early Business** — Stretch every dollar. Buy smarter, hire cheaper, sell better. Field superior equipment sooner.

---

## 7. Economy

### Prize Money

Prize money is distributed from a **hardcoded static table** — not a formula. The table has 100 entries, one per finishing position.

- **Total fund: $15,000,000** across all 100 positions
- **P1: $750,000** · P2: $700,000 · P3: $675,000 · ... · P10: $500,000
- Mid-pack drops steadily: P50: $50,000 · P75: $5,000
- **P100: $500**
- Cars that complete **0 laps receive nothing**. All others are paid by position.
- The complete prize money schedule is **always visible** to the player in the Finances tab
- Prize money is the **sole source of income**

### Beg Mechanic

If the player has **no cars** and cannot afford the cheapest listing on the second-hand market, the **guaranteed shitbox** (Neutrino Sago) becomes purchasable for **whatever the player currently has** — even $0. The player pays their entire remaining budget and receives the shitbox.

This applies in **any year**, not just year 1. It is the economy's permanent safety net: the player can never be locked out of racing.

The beg mechanic works **silently** — no special UI messaging. The shitbox listing simply becomes affordable. The button reads "Beg" instead of "Buy."

The beg mechanic provides the **car only**. It does not provide spare parts, tyre sets, fuel money, or any other resources. A player who begged races with whatever remains — potentially zero spare parts, zero tyre sets (no tyre changes all race), and no ability to fix anything.

### Starting Budget

The player begins year 1 with **$1,000** in cash and **no car**. Their first action is to visit the second-hand dealer and buy the guaranteed shitbox (a heavily aged Neutrino Sago, typically costing under $1,000). The remaining budget covers initial spare parts, tyre sets, and fuel — barely.

### Spare Parts

**Spare parts** are a consumable resource measured in units (a single quantity, not typed by component).

- Purchased between races with money. **Business skill** reduces the cost.
- Consumed by two systems:
  - **In-race issue fixes** — each issue has a spare parts cost (1 for minor, 2–5 for medium/major/crash). If the player doesn't have enough parts, the issue cannot be fixed; they endure the stat debuffs instead.
  - **Between-race condition repair** — restoring car condition costs spare parts, scaled by the condition deficit and the car's tier (cheap cars cost fewer parts, expensive cars cost more). **Business skill** reduces the parts required.
- Fixing and repairing are **always optional** — the player is never forced to spend parts.
- Spare parts are carried into the race and depleted as issues are fixed. Unspent parts carry over to the next year.

### Tyre Sets

**Tyre sets** are purchased before the race. Each pit stop tyre change consumes one set.

- **Cost per set scales with car class** — cheap tyres for Class F cars, expensive tyres for Class A and F1 (same scaling pattern as spare parts)
- Purchased between races with money. **Business skill** reduces the cost.
- If the player runs out of tyre sets during a race, they **cannot change tyres** — the car continues on worn rubber, with lap times degrading progressively
- If the player cannot afford any tyre sets before the race, they start and race on their initial set for the entire 48 laps with no changes
- Unspent tyre sets carry over to the next year
- Pushing burns tyres faster, requiring more frequent tyre changes and therefore more sets per race — making Push mode more expensive in tyres as well as pace

### Fuel Cost

Fuel has a **per-litre monetary cost**.

- Billed **after the race** — total fuel consumed during the race × per-litre price
- Deducted from the player's balance post-race (after prize money is awarded)
- If the player cannot afford the fuel bill, it is **written off** — no debt, balance stays at $0
- **Business skill** reduces fuel cost. Engineer skill does **not** affect fuel cost.

### Spending Categories

| Category | When Paid | Reduced by |
|----------|-----------|------------|
| Car purchase (new or used) | Between races | Business skill |
| Upgrade packs | Between races | Business skill |
| Spare parts | Between races | Business skill |
| Tyre sets | Between races | Business skill |
| Car repair (condition restoration) | Between races — costs spare parts | Business skill (reduces parts required) |
| Driver salaries | Annually, per contract year | Business skill |
| Crew hiring | Annually | Business skill |
| Fuel | After race — deducted from balance | Business skill |

### Year 1 Experience

- The player starts with **$1,000** and **no car** — they buy the guaranteed shitbox from the second-hand dealer
- Driving solo (player character only), no hired drivers, no crew
- The player does their own pit stops (slow, since crew size is 0)
- After buying the shitbox, remaining budget covers spare parts, tyre sets, and fuel — barely
- Prize money from a back-of-grid finish is enough to do one small thing: a minor upgrade, a cheap co-driver, some spare parts, or start saving toward a better car

### Progression Pacing

- **~15 years** from junkyard shitbox (aged Class F) to competitive Class A
- Full playthrough: ~15 races × 24 minutes = **~6 hours** of race time, plus between-race management
- Economy balance must ensure steady upward progression without trivialising any performance tier

---

## 8. AI Teams

### Structure

- **99 AI teams** + 1 player team = **100 total cars**
- All teams are **permanent and persistent** — no teams enter or leave during a career
- Each AI team has hand-crafted initial conditions: starting budget, car, and driver lineup
- AI teams field **3 drivers** each
- **No AI team runs an F1 car** — F1 exists exclusively for the player

### Simulation

AI teams follow the **same economic rules** as the player each year:

- Earn prize money from race results
- Spend on cars, upgrades, drivers, and crew
- Spending decisions managed by a **greedy algorithm** (highest-value option first)
- Teams naturally rise and fall based on results and spending
- The field is **somewhat predictable** (strong teams tend to stay near the top) yet **dynamic** (lineups change, off-years happen, midfield teams can break out)
- AI teams **always race on Normal** — they do not use driver instructions

### Team Prestige

A composite score reflecting each team's competitive history.

- Calculated from **all historical race results** with **recency bias toward the past 5 races**
- Determines **starting grid order** (see §2: Starting Grid)
- Visible to the player on the **Standings** screen
- Both the player's team and all AI teams have prestige calculated identically
- The player starts year 1 with **zero prestige**

---

## 9. Between-Race Screens

Between-race actions can be performed in **any order**. The player begins the next race when ready.

### Top-Level Navigation

| Tab | Purpose |
|-----|---------|
| **Garage** (default) | Dashboard and access to all management sub-screens |
| **Finances** | Budget breakdown, prize money history, spending log |
| **Race History** | Past results, lap charts, newspaper archive |
| **Standings** | All-time team prestige rankings |
| **Scouting Report** | Preview of the upcoming field: teams, cars, drivers |

### Garage Sub-Screens

Accessed from the garage dashboard:

| Sub-Screen | Purpose |
|------------|---------|
| **New Car Dealer** | Browse and buy new cars at list price |
| **Second-Hand Dealer** | Browse rotating used car inventory |
| **Car Workshop** | Apply upgrade packs and repair car condition |
| **Driver Market** | Browse available drivers, hire, and manage contracts |
| **Team Roster** | View current drivers' stats, contracts, and salary details |
| **Crew Hiring** | Set crew headcount for the upcoming year |

The player can also **sell cars** from the garage.

### Race Summary

Displayed after each race. Should be **comprehensive and interesting enough** that the player wants to review it:

- Detailed position results for all 100 cars (overall and class position)
- **Class podiums** — top 3 in each class
- Key race moments (lead changes, failures, notable overtakes)
- Player team statistics
- **Driver instruction mode summary** (e.g., "Push: 10 laps / Normal: 25 laps / Conserve: 13 laps")

### Newspaper

A post-race newspaper with a classic **broadsheet layout** (white background, serif fonts, black dividing lines).

**Content:**
- **Grand winner headline** — always featured
- **Class podium results** — top 3 in each class reported alongside the overall winner
- **Template-driven feature stories** — an algorithm scans race data to find the best-fitting team for each template:
  - Most Unlucky
  - Most Surprising
  - Biggest Mover
  - Biggest Disappointment
  - Veteran Performance
  - Rookie Debut
  - (Expandable template pool)
- One randomly selected feature story + the player's team story
- **Player coverage scales with prestige** — tiny footnote in early years, dedicated front-page article when significant

### New Game Flow

1. Name the player character
2. Allocate 15 skill points across Driver / Engineer / Business
3. Name the team
4. Choose or upload a team logo
5. Enter the garage to prepare for the first race

---

## 10. Commentary System

An event-driven template system. Each event type has a pool of text templates with variable slots filled from race data. Commentary appears in the scrolling text feed on the race screen.

### Event Types

- **Overtake** — one car passes another
- **Pit stop** — any team enters the pit
- **Issue occurred** — a mechanical issue strikes a car
- **Failure / retirement** — a car suffers a terminal failure
- **Lead change** — a new car takes the lead
- **Lap record** — fastest lap of the race is beaten
- **Large gap change** — a significant position gap opens or closes
- **Driver instruction mode change** — the player switches modes (e.g., *"Team {{team_name}} tells {{driver_name}} to push!"* / *"{{driver_name}} easing off — conserving for the long stint."*)
- **Risky push warning** — the player pushes in dangerous conditions (e.g., *"Risky call from {{team_name}} — pushing hard with worn tyres and fading condition."*)
- **Year 1 contextual hints** — tutorial-style guidance (e.g., *"Your tyres are looking worn — consider pitting soon"*)

### Template Example

```
"Car #{{number}} pits from P{{position}} — {{team_name}} opting for an early stop."
"{{driver_name}} sets the fastest lap! A {{lap_time}} for {{team_name}}."
"Trouble for {{team_name}} — {{issue_description}}. They'll need to pit."
```

---

## 11. Track

- **One fictional circuit** for all races — hand-designed, fixed layout
- Mix of fast straights, tight sections, and sweeping curves (evocative of a Le Mans-style road course)
- Track geometry determines **disc movement speed** on the track map (faster on straights, slower through corners)
- All mechanical simulation (tyres, fuel, condition, issues, failures) is abstracted to the **per-lap level** — individual track sections have no gameplay effect
- The track has no name — the event is **The 24h Tarmac Gauntlet**

---

## 12. Audio (v1 — Minimal)

- Ambient engine drone during races, shifting pitch vaguely with car speed
- Pit stop sound effects (wheel guns, fuel rig, car dropping off jacks)
- Subtle crowd/atmosphere ambient bed during race
- Menu screen music
- No commentary audio — text ticker only

---

## 13. Tutorial & Onboarding

- **No formal tutorial**
- Year 1 **is** the tutorial: almost no money, no crew, one car, driving solo — the only decisions are pit stop timing and driver instructions
- **Tooltips on hover** for all stats, screens, and buttons
- Commentary ticker includes **contextual hints in year 1** (see §10)
- By year 2–3, the player has learned the full loop organically

---

## 14. Save System

- **Autosave** after each race
- **One career at a time** — starting a new career overwrites the existing save
- No difficulty settings — one fixed, balanced experience

---

## 15. Deferred Features (Post-v1)

The following are explicitly **out of scope for v1** but the design accommodates future addition:

- Weather system (rain, wet tyres, dynamic conditions)
- Safety cars / full course yellows
- Traffic / dirty air effects on tyre and engine temperature
- Multiple tracks / race calendar
- Additional tyre compounds (wet, intermediate)
- Sponsor system (additional income source)
- Team reputation / fan following
- Multiplayer
- AI team push/conserve behaviour
- Prestige-based driver market priority (replacing first-pick system)
- Driver personality / flavour traits

---

## Appendix A: Key Numbers Reference

| Parameter | Value |
|-----------|-------|
| Race duration (real-time) | 24 minutes |
| Time compression ratio | 60:1 |
| Lap time (visual) | ~30 seconds |
| Total laps per race | 48 |
| Typical pit stops per race | 8–12 |
| Cars in field | 100 (99 AI + 1 player) |
| Car models | 31 |
| Car classes | 7 (F, E, D, C, B, A, F1) |
| Car stats | 8 (Power, Handling, Fuel Efficiency, Tyre Durability, Comfort, Reliability, Pit Stop Time, Fuel Capacity) |
| Upgrade packs per car | Up to 3 (Power, Handling, Comfort) — varies by class |
| Driver pool size | 310 |
| Drivers per AI team | 3 |
| Drivers per player team | 1–4 (including player character) |
| Driver retirement per year | 15 (replaced by 15 new 18-year-old rookies) |
| Driver stat peak age | ~30 |
| Driver stat scale | 0–100 |
| Driver stats | 5 (Pace, Consistency, Stamina, Safety, Smoothness) |
| Player skills | 3 (Driver, Engineer, Business) |
| Skill max | 20 each |
| Starting skill points | 15 |
| Skill points per year | 3 |
| Player driver stats formula | Skill level × 5 |
| Years to max all skills | 15 |
| Max crew size | 16 |
| Crew cost | $2,000/head/year |
| Contract lengths | 1, 2, or 3 years |
| Contract discount (2yr) | 10% |
| Contract discount (3yr) | 20% |
| Contract buyout cost | Remaining salary × 1.5 |
| Cheapest car (new) | $4,500 (Class F) |
| Cheapest car (second-hand) | ~$675–$1,050 (guaranteed shitbox) |
| Most expensive car (new) | $3,000,000 (Class A) |
| F1 car price | $15,000,000 (MSRP; used market only) |
| Depreciation rate | 3% per year |
| Price floor (age) | 50% of MSRP |
| Used inventory per year | 1 shitbox + 2 per class (F–A) + F1 conditional = 13–14 |
| Prize money total fund | $15,000,000 |
| P1 prize money | $750,000 |
| P100 prize money | $500 |
| Driver instruction modes | 3 (Push, Normal, Conserve) |
| Mode change timing | Takes effect next lap |
| Mode reset | Resets to Normal on every pit stop |
| Starting budget | $1,000 |
| Prize money eligibility | All cars with ≥1 lap completed |
| Race consumables | 3 (spare parts, tyre sets, fuel) |
