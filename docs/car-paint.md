# Car paint

Every car model's paint is one material named `body`. Glass, lamps, badges, trim and tyres use other materials and keep their colours.

To paint a car in code:

```csharp
CarPaint.Respray(carModel, colour);
```

`CarBuilder.Spawn(modelId, circuit, keys, paint)` already does this with `paint`. Raceday passes the class colour from `CircuitScene.PaintFor`, so classes stay told apart on track.

The respray copies the material for that car, so other cars using the same model are not changed.

Models are exported amber (see `docs/car-modelling.md`). Leave out `paint` to keep it.

## Liveries

A team with a livery wears it instead of the class paint. `CarBuilder.Spawn(modelId, circuit, keys, paint, team, raceNumber)` looks the team up in `CarArt.LiveryPath` and calls `CarLivery.Apply`; Raceday passes each grid car's team name and race number.

- A livery is drawn on one model only. Its `livery.json` names that model, and `CarLivery` leaves any other model in the class paint.
- Each livery lives in `unity/Assets/Art/Liveries/<Team>/` as `atlas.png` and `livery.json`, baked from the team's folder in `tools/liveries/`.
- The `TarmacGauntlet/Livery` shader (`Raceday/Resources/Livery/`) replaces the `body` material. It projects the five views (left, right, top, front, rear) onto the paint by which way each face points, and draws the team's race number into the livery's number boxes from `Digits.png`. Side faces turned in towards the middle of the car (vent walls, inner door skins) keep the base colour, so side art never shows on them back to front.
- The race number is never painted into the art. It changes each season (`BaseTeam.RaceNumber`).
- A livery can carry its own digits: `digits.js --font <ttf> --edge <outline> --step <spacing>` bakes `Digits.png` and `Digits.json` into the livery's folder. The face takes each number box's `ink` and the outline its `edge`; with `--step` under 1, digits sit closer and overlap. Without them the livery uses the shared digits.

To make a livery:

1. `node tools/liveries/common/views.js <model.obj> <team>/out/views` draws the car's five views at true scale, with every part edge as a guide.
2. The team's `design.py` draws the art over them with `tools/liveries/common/livery.py`, in the car's own metres.
3. `node tools/liveries/common/bake.js <views> <art> <out> --sample 8` bakes a preview; `python3 tools/liveries/common/viewer.py` shows it on the 3D car. With `--number`, the viewer draws the number the game's way on the game's atlas.
4. `bake.js ... unity/Assets/Art/Liveries/<Team> --model <model asset path>` bakes the game's atlas. Add the team to `CarArt.LiveryPath`.
