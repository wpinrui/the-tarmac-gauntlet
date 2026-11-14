# Night lighting

How the circuit is lit at night, why the system before this one failed, and the rules that keep it from failing again.

## What went wrong before

The first night system was never real lighting. Each complaint was answered with another shader trick on top of the last.

- **Pools.** The floodlights were a `LightPool` shader that multiplied whatever was under it. Under an overcast dusk the
  multiply pushed the scene to white.
- **Headlamp stripe.** Each car painted a flat stripe of light about 60 m long onto the ground in the ground's own shader,
  with no falloff. Twenty cars summed with no limit. The fix (#428) was a `saturate`, which only moved the whiteout.
- **Mist beams.** Volumetric beams in the mist had no cap and whited out the screen near a mast.
- **Stopgap, then a map.** #470 turned the pools off, so the floodlights did nothing. #474 replaced them with a 4 m
  `FloodField` map capped at 0.5, which barely showed.
- **Fake proof.** Every proof render was a Python or Blender mock-up made of boxes, with the shader formulas copied
  into it, usually with one to three cars. The "budget" tests checked limits the same code defined. Nothing ever
  checked what the game itself would draw with a full grid.
- **The real cause.** The renderer ran plain Forward, which lights each object with at most 4 to 8 lights. Real lights
  could not light a track mesh hundreds of metres long, so every fix had to fake them.

## The system now

All of it lives in `unity/Assets/Raceday/Scripts/Night/` and `unity/Assets/Rendering/Shaders/Lamps.hlsl`.

- **Forward+.** Every renderer asset uses Forward+ (`m_RenderingMode: 2`), so each pixel takes every light near it,
  with up to 256 lights on screen.
- **Floodlights.** `FloodlightLayout` places the lamp heads on the stands' roofs, the pit building's roof, the start
  gantry and masts along the pit wall. Each mast head and pit roof head is its own URP spot light; neighbouring stand
  and gantry heads share one. Each light's beam fades softly from its axis to its edge, so neighbouring pools blend. `Balance` fits every light's intensity by least squares so the track and the pit lane
  take `NightBudget.Floodlit` as evenly as the lights can give it. `FloodlightRig` turns on the 200 nearest the camera.
  With the light shadows option on, the four nearest the camera's view cast soft shadows.
- **Headlamps.** `HeadlampRig` puts one URP spot light on each of the cars nearest the camera, as many as the "Real
  lights" option allows (4 to 20). A cookie shapes it as a low beam, cut off just above level. A light moving to
  another car fades out and back in.
- **One budget.** Every number that sets how bright the lamps are is in `NightBudget.cs`. `NightLighting` hands the
  caps to every shader as `_TgLampBudget`, and `Lamps.hlsl` applies them in every lit shader in the game (TarmacLit's
  shading, the car paint, the game's copy of URP Lit, the smoke):
  - one lamp gives a surface at most `LampMost` (0.8), however near it is;
  - all lamps together give a surface at most `AllMost` (1.4), however many overlap;
  - a lamp-lit pixel gives back at most `PixelMost` (1.6), its sheen included.

  Each cap eases in from half its value and never quite reaches it, so nothing clips.
- **Grass.** Grass takes the lamps' light dimmer and less green than its own colour (`NightBudget.GrassLamp`,
  `GrassColour`), so a floodlit verge sits near the road's brightness. The ground shader, the grass blades and Lit
  materials with `_TG_GRASS` (verges, runoff, terrain, stand banks) shade it so.
- **Glow.** The lamps themselves glow in linear light (`NightBudget.Glow`): the floodlight lenses, the headlamps and
  the tail lamps at night, and the brake lamps whenever a car brakes, all well over the bloom's threshold, so each shows
  a halo. The night adds `NightBudget.Bloom` to the bloom's intensity. Nothing lamp-lit is meant to bloom, so only the
  lamps do. The pit building's windows glow in each room's own light, warm or cool (`PitComplexBuilder.WindowLight`),
  and the brightest only just pass the threshold, so they bloom faintly. At night the glass loses some of its sheen
  (`NightBudget.WindowSheen`), so the floodlights on the roof above it do not glare off it. It still reflects the
  circuit in front of it through a cheap probe per stretch of glass (`PitComplexView`): small, not HDR, without the
  cars, and redrawn only as the sky's light changes. None of them light anything.
- **Car reflections.** The cars mirror the circuit through HDR probes (`CarReflections`), and the paint makes the
  small bright lights it mirrors brighter again, so the floodlights streak across it (`CarPaint.hlsl`'s glint, its
  numbers mirrored as `NightBudget.Glint*`). Only a lens glints: something brighter than `GlintFrom`, which sits over
  every lamp-lit surface and the pit windows, and more than `GlintSpread` times brighter than the world round it. When
  the glint took everything over 1.2, the pit windows whited out the whole car. The smoke, sparks, haze and glowing
  brake discs are never drawn into a probe (`CarEffects`). At night, from High up, the car nearest the camera has its
  probe redrawn whole every frame, so the lights slide smoothly over its paint.
- **Sheen.** The track shader's full Fresnel highlight is applied to the sun and the moon only. On the lamps it turned
  the whole floodlit road white, because their light grazes the road from every side at once. The lamps keep URP's
  own softer shine.

## Proof

`tools/night-budget` builds the circuit and the floodlights with the game's own code and checks:

- the layout (cheap to draw, aimed down, clear of the cars and the gantry, every light that reaches the road turned on);
- that `Lamps.hlsl` and the C# copy of its maths (`LampLight.cs`) are the same, and that the lamps get no full Fresnel;
- one headlamp at every distance;
- the floodlit straight and pit lane (no dimmer than 0.6 of `Floodlit`, uniformity at least 0.55);
- the full grid of 30 cars, every car's headlamps on with every floodlight: the road, and every car's tail, roof,
  bonnet and sides, under the caps; a white car shown below 0.95 and under the bloom's threshold; the back of the grid
  lit by the floodlights alone;
- a car swept round the whole lap and down the pit lane (`CarSweep.cs`): what its probe sees of the pit windows and the
  floodlights' lenses, and how much of that its paint shows over the bloom's threshold with the glint, at most 2%, with
  the lenses still glinting.

`tools/night-budget/render` renders the full grid at night in headless Chromium: the circuit's real meshes, the real car
models, every floodlight and twenty headlamps as URP spot lights, shaded as `Lamps.hlsl` shades them, then the night's
bloom, exposure and ACES tone curve. It writes the grid head-on, from the stands and from behind, from behind again with
every car braking, the head-on view with the budget off, and the stands with the floodlights alone and with no lamps.

```
bash tools/night-budget/run.sh out/night-scene
python3 -I tools/night-budget/render/cars.py out/night-scene/cars unity/Assets/Art/Vehicles/<Model>/<Model>.obj ...
node tools/night-budget/render/render.js out/night-scene out/night-renders
```

## Rules for changing it

- Change brightness only in `NightBudget.cs`, then run `tools/night-budget` and render the full grid before merging.
  A render with one to three cars proves nothing.
- Never light anything at night with a shader trick: every lamp is a URP light, shaded through `Lamps.hlsl`.
- Keep `Lamps.hlsl` and `LampLight.cs` in step. The check fails if they drift.
- **Never add needless text to the frontend.** Nothing about the night lighting goes on screen. If the game is usable
  without a piece of text, it does not get that text.
