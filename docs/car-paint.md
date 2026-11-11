# Car paint

Every car model's paint is one material named `body`. Glass, lamps, badges, trim and tyres use other materials and keep their colours.

To paint a car in code:

```csharp
CarPaint.Respray(carModel, colour);
```

`CarBuilder.Spawn(modelId, circuit, keys, paint)` already does this with `paint`. Raceday passes the class colour from `CircuitScene.PaintFor`, so classes stay told apart on track.

The respray copies the material for that car, so other cars using the same model are not changed.

Models are exported amber (see `docs/car-modelling.md`). Leave out `paint` to keep it.
