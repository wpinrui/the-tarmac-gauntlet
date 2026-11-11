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

`Assets/Raceday/Raceday.unity` opens when the editor starts. Press Play and the AI drives the Seorak
Cheonma CX-24Si. `I` hands the car to you, and pressing it again hands it back.

Start on the scene's `CircuitScene` picks how the race starts. Rolling (the default) starts behind the pace car. Pit Lane: the 100 cars
queue in the pit lane's fast lane, quickest first with the Cheonma last, and leave three seconds apart
from the pit exit line (the end of the limiter zone), the queue moving up behind each car that goes
(`PitLaneStart.cs`). The scene fast forwards at 32x until just before the car ahead of the Cheonma
leaves; `F` or `I` ends the fast forward early. Pit stops open once every car is out of the pit lane, and
every car starts on a full tank. Grid races from the grid at once.

The field is the season's 99 AI teams, by the class of their roster entry, and the player's. The timing
tower in the top left (`RaceTower.cs`, order in `Standings.cs`) counts down the 24 hour race clock, which runs
at 10:1 from the green or the first car out of the pit lane. It shows every class leader, the four cars either
side of the player, and the fastest class in the rows left. Gaps are in fake laps, a tenth of the lap: the leader
shows the fake laps it has completed and the rest +N behind it. `run.sh tower` checks the order.

AI driver (`AiDriver` on the car): Pace, Consistency and Safety (0 to 100) can be changed in the
inspector during Play. Pace sets how much of the car's grip and braking it uses (86% at 0, 98% at 100),
Consistency how much its corner speeds wander from lap to lap, and Safety how often it overcooks a
corner. It follows a racing line (`RacingLine.cs`: the minimum-curvature path using the full track width,
outside, apex, outside, then tuned in the background for the car's shortest predicted lap) at a
target speed for every 2 m sample (`SpeedProfile.cs`, from the car's grip, downforce, engine and
brakes). After a hard crash, or when stuck or spun, it puts the car back on track itself. The console
shows its predicted lap time and each lap it drives.

`T` opens live timing while the AI drives your car (`LiveTiming.cs`), with the race at 16x behind it: every
car in race order by class, your car's onboard and the track map, its driver, fuel, tyres and condition with
the laps each lasts on the engine map in use, the engine map, and the next stop. Pit Now calls the car in at the
next pit entry, and the stop puts in the litres and tyres asked for and the next driver picked (`PitOrders` in
`Consumables.cs`). Left alone the car pits and chooses for itself, and each stop puts the orders back to a full
tank and new tyres. The red button top right, or `T`, returns to the race.

`F` cycles normal speed, 4x and 16x while the AI drives, and `I` drops back to normal speed. Only cars
near the camera, and near your car while you drive it, run full physics (`PhysicsRange.cs`: the
nearest 24 within 300 m at normal speed, 8 within 120 m at 4x, none at 16x). The rest glide along
their racing lines with no rigidbody (`AiPilot.Glide`), at the pace and with the racecraft, fuel,
tyre wear and pit stops they would have on physics, and switch back to physics where they are.

After changing the AI or the car physics, run `bash tools/ai-check/run.sh` (needs the .NET 8 SDK). It
checks the line goes outside, apex, outside and that tuning shortens the lap, then drives the AI in a
flat stand-in for `CarController` and checks that every car model laps without
leaving the track and within 5% of its predicted time, that Pace, Consistency and Safety do what they
say, and that overcooked corners cost time. `run.sh glide` checks gliding: every model's gliding lap
is within 1.5% of its driven one, a gliding mistake costs about what a driven one does, cars switch
between gliding and physics without leaving the track or touching, 100 gliding cars at 16x keep their
pace, gliding cars still pit and start rolling with the pack, and the pit lane start sends all 100
cars out cleanly (`run.sh pitstart` for the same with a line per car). `StandInCar.cs` copies `CarController`'s formulas, so keep
it in step when those change.

Driving: left and right arrows steer, up accelerates, down brakes (hold it at a standstill to
reverse). `M` toggles automatic and manual; `A` and `Z` shift up and down and also switch to manual. The
bottom right shows speed in km/h and the gear, blue in automatic and white in manual. `H`
toggles steering help. Anti-lock brakes are always on. `R` puts the car back on track, which you
need after a hard hit on a barrier. `C` cycles the chase, bonnet and bumper views and the free camera.

The car's physics come from its game stats (`Raceday/Scripts/CarSpec.cs`): Power sets the engine,
Handling sets tyre grip and downforce. Grass and gravel have less grip and slow the car.

Engine sound is synthesised live, with no recordings (`Raceday/Scripts/Audio/`). Each class has a
package in `EngineSounds.Tier` and each car is that package with a few values changed in `EngineSounds.Cars`.
`EngineSynth` fires an exhaust pulse per cylinder down a pipe, then adds the intake, valvetrain, gear whine,
turbo and hybrid motor. `EngineAudio` on each car feeds it the revs, throttle and road speed, and only the 10
cars nearest the listener within 350 m play. To hear a change outside Unity, `bash tools/engine-audio/run.sh`
(or `run.sh d-14 b` for one car or class) writes a WAV per car to `out/engine-audio/`.

Free camera: move the mouse to look, `W` `A` `S` `D` move along the ground, `Q` and `E` go down and up,
`Shift` is 5x faster and the scroll wheel changes speed. Click to capture the mouse, `Esc` frees it.

The circuit is built at runtime from `Assets/Raceday/Resources/Tracks/circuit.json`, which
`tools/circuit/generate.py` writes (`python3 tools/circuit/generate.py`, needs numpy). Change the
layout there, never by hand in the JSON.

After changing the generator or `CircuitBuilder`, run `bash tools/circuit/geometry-check/run.sh`
(needs the .NET 8 SDK). It builds the circuit as the game does and checks that every surface lies
over the ground, no surface edge stands more than 0.4 m off what is beside it, every barrier
reaches the ground, no fence crosses another, and no fence kinks or stands as a stray stub. Merge
only when every rule says ok.

Markings follow FIA Appendix O and race-director practice: white edge lines (part of the track),
red and white kerbs outside them, a 15 m wide start straight through turn 1, 30 grid slots (white
outline open at the back, yellow front-wheel line sticking out sideways), safety car lines, and a
12 m pit lane with a 3.5 m fast lane, speed limit lines, pit box outlines, red and white hatching at
the entry and a solid white line along the exit. Everything beside the track is a placeholder.

## Team HQ scene

`Assets/TeamHq/TeamHq.unity` is the team's home workshop, where every management menu sits: four car bays,
the office on the mezzanine behind glass, and the trophy room off the office. Like the circuit, it is built in
code (`HqGeometry.cs`), with the layout and looks Ivan picked in the Team HQ viewer
(https://claude.ai/artifact/N5rq4qwWEYVNA6X37nrZdf). Its numbers are in the viewer's frame and mirrored into
Unity's at the end.

Each menu screen has a camera shot, set by Ivan in that viewer and copied into `Resources/CameraPresets.json`
(viewer frame: metres, +Z towards the bay doors, vertical field of view in degrees). `HqCamera.Show(screen)`
glides to a shot in half a second. Nobody adjusts shots in the editor: when Ivan saves new ones in the viewer,
copy them from the viewer's `presets` store into the JSON.

`bash tools/team-hq/geometry-check/run.sh [folder]` builds the building outside Unity and checks every part
lies inside the shell with sound normals and winding. It also checks every placed prop has its files. With a folder it writes `hq.obj`, `props.json` and
`lights.json` there, which the viewer reads.

The props (tool chests, lamps, furniture, the crew, the trophies) are real models: Poly Haven (CC0) and
Sketchfab (CC BY, credited in `Assets/TeamHq/CREDITS.md`). `tools/team-hq/props/pack_props.py` packs each one
into `Resources/Props` as `<id>.json`, `<id>_mesh.bytes` and its textures; `HqGeometry` places them and
`HqProps.cs` builds them, so the scene needs no import packages. The viewer reads the same files.
`tools/team-hq/props/props.json` holds each model's height, pose and simplification, and `props.txt` the list.
`make_textures.py` turns Poly Haven photo textures into the building's surfaces in `Resources/Textures`.

## Menus and saving

The management menus sit over the Team HQ scene (`Assets/Management`, approved in the viewer
https://claude.ai/artifact/9oLpt153QSeZj1edjd7mHt). `Menus.cs` builds them with UI Toolkit from code, styled by
`Resources/Menus.uss` on a 1600 x 900 reference screen. The main menu is a tall glass column with the wordmark
(`Resources/Wordmark`, https://claude.ai/artifact/DokfsDDo2SPFbaFdKVURdy) over Continue, New game, Settings and Quit
game in Host Grotesk Medium. On game launch the wordmark's car rolls in from the right and brakes to a stop, its rims
turning with the distance travelled. In the game, the hub menu (Garage then Office, told apart by spacing), the top bar, the race car's name and
condition beside its bay, and the Race button. A screen opens as one panel docked right, with the menu collapsed to
an icon strip; Q and E cycle screens and clicking the open screen's icon goes back to the hub. Escape closes a
screen, and on the hub opens Resume, Settings, Quit to main menu and Quit game. Every change of screen calls
`HqCamera.Show`. Screens are empty: fill one from `Menus.ScreenOpened(slug, body)`. Every menu icon in `Resources/MenuIcons` except the team badge is a Tabler outline icon (MIT, https://tabler.io/icons),
rendered white at 96px so USS can tint it.

The frosted glass is `MenuGlass.cs`: `GlassBackdrop` on the camera keeps a quarter size copy of the scene blurred
and saturated (`Resources/MenuGlassBlur.shader`), and every glass panel shows its own slice of it under a white sheen.
Settings (`GameSettings.cs`) apply at once and are kept in PlayerPrefs.

`Assets/Career` (no Unity references) holds the live game. `GameSession.State` is the current `GameState`; change it
only through `GameSession.Apply`, which autosaves in the background. Each career has its own folder under
`persistentDataPath/careers/<id>/` with `autosave.json` and a small `info.json`, so careers never share an autosave.
For now a new game deletes the other careers once it is saved. A finished race (`RaceWeekend.Finished`) is applied
and saved by `GameSession` itself. Saves are JSON (Newtonsoft); raise `SaveJson.Version` and add a step to
`SaveJson.Migrate` when the format changes.

```
dotnet test unity/dotnet/TarmacGauntlet.Career.Tests
```
