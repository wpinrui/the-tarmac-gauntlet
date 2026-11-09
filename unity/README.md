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

`Assets/Raceday/Raceday.unity` opens when the editor starts. Press Play to fly round the circuit.

Camera: move the mouse to look, `W` `A` `S` `D` move along the ground, `Q` and `E` go down and up,
`Shift` is 5x faster and the scroll wheel changes speed. Click to capture the mouse, `Esc` frees it.

The circuit is built at runtime from `Assets/Raceday/Resources/Tracks/circuit.json`, which
`tools/circuit/generate.py` writes (`python3 tools/circuit/generate.py`, needs numpy). Change the
layout there, never by hand in the JSON.

Markings follow FIA Appendix O and race-director practice: white edge lines (part of the track),
red and white kerbs outside them, a 15 m wide start straight through turn 1, 30 grid slots (white
outline open at the back, yellow front-wheel line sticking out sideways), safety car lines, and a
12 m pit lane with a 3.5 m fast lane, speed limit lines, pit box outlines, red and white hatching at
the entry and a solid white line along the exit. Everything beside the track is a placeholder.
