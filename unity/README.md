# Unity project

Unity 6000.6.7f1. Open this `unity/` folder in the Unity Hub.

`Assets/Simulation/` is the headless C# port of the TypeScript simulation. It has its own
assembly (`TarmacGauntlet.Simulation.asmdef`) with no UnityEngine references.

Every new file and folder under `Assets/` needs a committed `.meta` file with its own random guid.

## Running the simulation tests

The same `.cs` files also build as a plain .NET library, so the tests run without Unity.
Needs the .NET 8 SDK.

```
dotnet test unity/dotnet/TarmacGauntlet.Simulation.Tests
```

`dotnet/TarmacGauntlet.Simulation` builds the sources against netstandard2.1 and C# 9,
the same API level and language version Unity uses, so anything that passes here also
compiles in Unity.

## Raceday scene

`Assets/Raceday/Raceday.unity` is the race. The editor opens it on start; press Play, pick a car,
and the race starts at the back of a 100-car grid.

- `Assets/Simulation/Raceday/` holds the headless race-day logic: `LiveRace` (live pit calls and
  instruction modes on top of `RaceLoop`), `RacePlayback` (where each car is at any moment),
  `TestField` (a ready-to-race year-one game) and `Advisory` (laps left on tyres and fuel).
- `Assets/Raceday/Scripts/` is the scene: track meshes from `Resources/Tracks/gauntlet.json`
  (a copy of `src/renderer/assets/track.json`), cars, cameras and the race screen.

Keys: `C` camera view, `Q`/`E` previous/next car, `F` back to your car, `Esc` pause.
Play-testing only: `1` to `4` run the race at 1x, 4x, 16x or 64x.

### Car models

A car model with a folder at `Assets/Raceday/Resources/CarModels/<model id>/` holding a prefab
named `Car` uses that prefab: metres, +Z forward, origin on the ground at the car's centre.
Every other model draws as a car-sized cuboid in its class colour.

### Compile check without Unity

```
dotnet build unity/dotnet/TarmacGauntlet.Raceday
```

This builds the scene scripts against Unity's engine reference assemblies. It catches compile
errors only; nothing runs.
