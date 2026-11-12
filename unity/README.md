# Unity project

Unity 6000.6.7f1. Open this `unity/` folder in the Unity Hub.

`Assets/Simulation/` is the headless C# port of the TypeScript simulation. It has its own
assembly (`TarmacGauntlet.Simulation.asmdef`) with no UnityEngine references.

Every new file and folder under `Assets/` needs a committed `.meta` file with its own random guid.

## Building

`Tarmac Gauntlet > Build` writes the Windows game to `unity/Builds/Windows`. `Tarmac Gauntlet > Build and Install` also
copies it to `%LOCALAPPDATA%\Programs\The Tarmac Gauntlet`, replacing the last install, with Start menu and desktop
shortcuts (`WindowsBuild.cs`).

## Rendering

The project renders with the Universal Render Pipeline. Each quality level has its own pipeline asset in
`Assets/Rendering/Resources/Quality`, sharing `Renderer.asset`:

| Level | Sun shadows | Render scale | Edges |
| --- | --- | --- | --- |
| Low | 1 cascade, 1024, 150 m | 67%, STP | STP |
| Medium | 2 cascades, 2048, 250 m | 80%, STP | STP |
| High | 4 cascades, 4096, 500 m | 100%, STP | STP |
| Ultra | 4 cascades, 4096, 600 m | 100%, STP | STP |

Medium is the level for an Intel Arc B390 at 1920 x 1200 and 60 fps, and the default. `QualityLevels.cs` puts the chosen
level's asset in place as the game starts and gives every game camera post-processing and SMAA, which URP swaps for STP
wherever STP runs. At 100% STP upscales nothing and works as temporal anti-aliasing. ProjectSettings is not
committed, so `Assets/Rendering/Editor/UsePipeline.cs` puts the Medium asset in the graphics settings and sets Linear
colour space whenever the editor loads, and the build sets Linear again. Textures made in code that hold data rather
than colour (noise, normals, masks, lookups) are created linear. The game's colours and lights were first tuned in Gamma, so what Unity doesn't convert itself is converted where it is used: `LinearLight` gives the sun, moon and lamps the intensity that lights a surface as brightly as before, `GammaColour` and `GammaScale` in `TarmacLit.hlsl` convert colours and multipliers written into shaders, the sky shader mixes its colours in sRGB as it always did, `UiBlend` and the style sheet give the menus' translucent colours the alphas that look as they did over the glass, and `GammaGui` draws the debug and race HUD's IMGUI as before. `tools/linear-preview` renders scenes both ways to compare them. Materials made in code use URP Lit through `Assets/Rendering/Scripts/Lit.cs`, and the game's own lit shaders share
`Assets/Rendering/Shaders/TarmacLit.hlsl`; the ones that move (car paint and carbon, liveries, pit crews, pit kit) also draw motion vectors
(`TarmacMotion.hlsl`) so the upscaler follows them, and the crowd draws its own for its fans turning and bouncing. The menus' frosted glass is `GlassBlurFeature` on
the renderer, beside URP's ambient occlusion at half resolution, after the opaque pass.

`ScreenReflectionsFeature` on the renderer adds screen-space reflections before the transparent pass. A shader opts in
with a `TarmacReflect` pass (`TarmacReflect.hlsl`) that draws its normal, roughness, environment weight and probe
reflection over itself: the track (`TrackSurface.hlsl`, shared with its forward pass) and the cars' clear coat
(`CarPaintReflect.hlsl`). Each such pixel traces its reflection through the depth buffer, a pixel a step up to a step
count set by the quality level, and the hit swaps out the probe's reflection as far as it is trusted. Off screen, past
80 m, or with the option off, the probes stay. Its row is on at High and above. The track shades with
`ShadeDielectric`, which adds the full GGX Fresnel URP's fit leaves out, so the road shines towards a low sun or a
floodlight, and its smoothness varies along the lap from a polish map in the patches texture's alpha.

Each scene's global volume carries `PostEffects`: the ACES tone curve and a colour grade, half resolution bloom, and
Gaussian depth of field for a camera that calls `PostEffects.Focus` that frame. The
broadcast director does for every shot but the onboard; a replay camera should too. A driving view never does, so it is
never blurred. The race grades for the time of day and the weather (`SkyGrade.cs`). A camera cut, or a camera that
did not draw last frame, clears the upscaler's history (`CameraCuts.cs`). The tarmac shimmer is drawn after the post
effects, so the upscaler never smears it. There is no motion blur. Colour grading, bloom, ambient occlusion, depth of field,
sky detail and the crowd each have a row in the graphics settings (`GraphicsOptions.cs`). URP's point and spot lights fall off with the square of the distance;
`LightFalloff.Urp` turns an intensity set for the built-in pipeline into one as bright a third of the way out to its range.

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

The camera starts on the broadcast when the race comes from the career, and on the chase camera when the scene runs on
its own. `C` cycles the car cameras: TV, chase, bonnet and bumper. The TV camera is coverage locked to the car: a second
director with `Lock` set cuts between trackside shots and onboards from the roll hoop, the nose, the wing mirror, the back of
the car and the driver's seat. `V` puts the broadcast on, and `V` or `C` takes it off. Holding `B` looks back on every
camera. While you are not driving your own car, the car camera is yours to move and still rides along with the car. Hold
the right mouse button and drag to look, `W` `A` `S` `D` move, `Q` and `E` go down and up, `Shift` is 5x faster and the
wheel changes speed. On the TV camera the drag orbits the car and the wheel zooms, within each shot. Switching camera or
car, or taking the wheel, puts the view back. Camera in the race's pause menu sets the chase, bonnet and bumper cameras'
field of view, position and tilt (`Visuals/CameraPresets.cs`), with the race seen through the camera being tuned beside the
panel, changing as each slider moves. On the broadcast the director (`BroadcastDirector.cs`) picks
the story once a real second: a car stopped off the track, a fight for a class lead, any fight within a class under
0.5 s, your car, a class leader's stop or yours, a car rejoining, the leader. It cuts between trackside, pan,
helicopter, onboard and pit lane shots.

The director's shots carry the broadcast graphics (`BroadcastGraphics.cs`): a battle strip with the gap in a
fight, a driver strip the first time a car is on air in a stint, a pit timer with fuel, tyres and driver
change lit as each is done, and the track map during helicopter shots, or always with Track map on in the settings.

`T` opens live timing while the AI drives your car (`LiveTiming.cs`), with the race at 16x behind it: every
car in race order by class, your car's onboard and the track map, its driver, fuel, tyres and condition with
the laps each lasts on the engine map in use, the engine map, and the next stop. While you drive, the HUD shows
the engine maps on their keys, `1`, `2` and `3`, with the one in use lit. Pit Now calls the car in at the
next pit entry, and the stop puts in the litres and tyres asked for and the next driver picked (`PitOrders` in
`Consumables.cs`). Left alone the car pits and chooses for itself, and each stop puts the orders back to a full
tank and new tyres. The red button top right, or `T`, returns to the race. Over the race the same corner has a
button into live timing and, while a teammate drives, the skip, each showing its key.

While a teammate drives, `N` or the button beside the red one skips to your next stint: the race runs at 32x until
the stop that puts you in is over and the car is back on track, then hands you the wheel. `F` or `I` stops the
skip. When a stop puts a teammate in while you drive, the AI takes the car once you have driven out of the pit lane.

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
reverse). The settings menu picks an automatic or manual gearbox; in manual `A` and `Z` shift up and down. The
bottom right shows speed in km/h and the gear, blue in automatic and white in manual. Steering help
is a setting too. Anti-lock brakes are always on. `R` puts the car back on track, which you
need after a hard hit on a barrier.

The car's physics come from its game stats (`Raceday/Scripts/CarSpec.cs`): Power sets the engine,
Handling sets tyre grip and downforce. Grass and gravel have less grip and slow the car.

Every race rolls its own weather (`Weather.cs`, `WeatherPlan`): a late October day at 50 degrees north whose kind of
weekend (settled, thundery, changeable or unsettled) sets the cloud and how ready the air is to shower. Fronts come often, with
hours of thickening cloud, a band of rain and showers behind; showers build most in the early afternoon; low cloud can drizzle
through the night. The track holds a film of water that rain fills, the surplus drains off and the sun, wind and cars dry. A wet
track (`RaceWeather.cs`) costs grip, about a quarter when fully wet, through `Consumables.GripScale`, so every car, driven
or gliding, laps about 13% slower, wears its tyres less and makes more mistakes. `WeatherView.cs` plays it: the sky greys
and loses its sun and shadows (`WeatherSky.cs`), the track darkens and shines, rain falls round the camera and the dozen
cars nearest it throw spray. `weatherSeed` on `CircuitScene` replays one race's weather; 0 rolls a new one.
`bash tools/weather-check/run.sh` rolls 4,000 races and checks how often it rains, when, how fast the track dries and what
the grip does (`--races 40` draws a strip per race, `--table SEED` one race hour by hour). `run.sh wet` in the AI check
drives every class on a wet track.

Engine sound is synthesised live, with no recordings (`Raceday/Scripts/Audio/`). Each class has a
package in `EngineSounds.Tier` and each car is that package with a few values changed in `EngineSounds.Cars`.
`EngineSynth` fires an exhaust pulse per cylinder down a pipe, then adds the intake, valvetrain, gear whine,
turbo and hybrid motor. `EngineAudio` on each car feeds it the revs, throttle and road speed, and only the 10
cars nearest the listener within 350 m play. To hear a change outside Unity, `bash tools/engine-audio/run.sh`
(or `run.sh d-14 b` for one car or class) writes a WAV per car to `out/engine-audio/`.

Free camera: move the mouse to look, `W` `A` `S` `D` move along the ground, `Q` and `E` go down and up,
`Shift` is 5x faster and the scroll wheel changes speed. Click to capture the mouse, `Esc` frees it.
The follow camera is the free camera riding along with the watched car. It starts in the chase spot, and its look
and moves stay relative to the car as it turns.

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

Grandstands (`Grandstands.cs`, materials in `StandMaterials.cs`) stand on the main straight opposite the pits and at
turns 1, 7, 10 and 12, each a walkway behind the furthest armco along its stretch, with a modest seated crowd of about
3,800 fans and catch fencing behind the armco in front. `Grandstands.Sites` places them from the corners and the start
line, so they follow the layout, and `Footprints` gives the ground they take for other scenery to keep off. Nothing in them
is solid. `StandLook` holds the stand shapes and colours Ivan picked. `bash tools/grandstands/run.sh [folder]` (needs the
.NET 8 SDK) builds every stand shape and checks they stand clear of the track and the pits, nothing is buried, every
stand is fenced, the crowd is modest and the meshes are cheap to draw. With a folder it writes the look preview there.
Each fan is one card, two triangles (`CrowdCard`), that `Resources/Crowd/Crowd.shader` turns to the camera and draws a
seated fan on, now and then with arms thrown up; the whole crowd is under 8,000 triangles. `CrowdView` hides it with its
graphics option.

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

The room's light is baked (`HqLightingView.cs`). `bash tools/team-hq/bake/run.sh` (needs the .NET 8 SDK and Blender as a
Python module, `pip install bpy`) bakes it with Cycles into `Resources/Lighting`: a lightmap holding the lamps, the daylight
through the windows and every bounce, one reflection probe image per room (garage, office, trophy room), and the ambient
light for whatever moves. The sun stays live for the cars' and crew's shadows, so only its bounce is baked. The lightmap
layout is `HqLightmapLayout.cs`, run by both the bake and the game, and the lights and probes are in `HqLighting.cs`.
Rebake after changing the building's lit parts, the props' places or the lights: until then the game lights the room
live and logs that the bake is out of date. The Baked lighting and Baked reflections graphics options switch back to
live lamps and one live probe.

Wear (oil under the cars, tyre marks from the doors, grime along the walls, worn patches, scuffed paint) is quads laid
just off the floor and walls in `HqGeometry.AddWear`, drawn by `Resources/Wear/Grime.shader`, which multiplies the
surface under it. `tools/team-hq/props/make_wear.py` draws the textures. The Wear graphics option hides it.

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

Escape in a race opens its pause menu (`RacePauseMenu.cs`): the race stops dead (`RaceWeekend.Paused`) under Resume,
Save, Settings, Retire, Quit to main menu and Quit game. Save writes the race as it stands to `race.json` in the
career's folder (`RaceSave`: the set-up, clocks, weather seed, every car's spot, lap, fuel, tyres and race log), and
Continue on the main menu goes back into it. A car that was in the pit lane comes back on track beside its spot. Race
saves are not migrated: one that no longer reads is deleted. The race save goes once the race is settled, or when
the next race starts from the lineup. Retire takes the player's car out (`FailureType.Withdrawn`) and settles the race
from where every car is.

```
dotnet test unity/dotnet/TarmacGauntlet.Career.Tests
```
