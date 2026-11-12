# Car modelling workflow

Every 3D car is built this way. It needs no 3D model of the real car.

## Priorities
Smooth, recognisable lines come first. That often means simpler geometry.
Tiny details are left out to keep geometry simple and safe from defects.
There is no triangle budget.
Otherwise follow the reference.
The reference is for measurements only. Never chase its exact shapes: a simple smooth shape with small inaccuracies beats a close copy with broken geometry.

## Out of scope
Liveries, sponsor decals and race numbers are not the modeller's job.
Cars carry no number plates.
Race cars get no street-legal lighting: no indicators, side repeaters or reversing lamps.

## 1. Start
Each car has one locked real-car reference, listed in `car_roster.md`.
Never offer an alternative reference.
Every request to Ivan names the real car the model is based on.
List every open call before building: counts, options, names, placement. Ivan answers first.
Every option set for a feature includes the reference car's own design.
Ask every placement or visual choice (badge, name and the like) inside the viewer: show each option on the model and let Ivan toggle between them. Never ask in text alone.

## 2. Reference
Ivan provides the photos: a four-view drawing (side, front, rear, top) and detail photos.
Look up the published wheelbase, length, width and tyre size yourself.
Renders and measurements of a 3D model or scan he supplies replace the drawing.
Reference images stay out of the repo.

## 3. Trace the rows (rows.py)
Pick pixel points along each section row on the side view.
The wheel centres set the scale to the published wheelbase.
Ground sits one wheel radius under the wheel centres.
Half widths come from the front, rear and top views, set to the published width.
Overhangs stretch to the published length.
Each row's height and width becomes a smooth curve (monotone cubic), so no outline is jagged.
rows.py writes rows.json.

### Tracing without perspective
A photo or a render with perspective puts near parts too big and far parts too small. Traced from one, the pillars, overhangs and glass lines land in the wrong places.
When Ivan supplies a 3D model, render it orthographically from the side, front, rear and top on a plain background, and trace the rows off those renders like a drawing.
Never take surfaces or outlines from its meshes. The model is only for checking measurements: wheelbase, overhangs, glass and belt heights.
When there is no model, use orthographic drawings only, never perspective photos. Look up published measurements as well: overhangs, ground to roof, belt line, windscreen base and top, door length, glass heights. Check every traced row against them.

## 4. Build the body (body.py)
body.py lofts the outer skin from rows.json, in pure numpy.
Build space is metres: x forward from mid wheelbase, y left, z up from the ground.
The skin splits into pieces: body, doors, bonnet, boot or engine cover, glass.
Every door that opens is its own closed shell, cut from the body along its shut line, named `door_l` or `door_r`. The door's edges and the jambs of the opening it leaves are painted in `body`, like a real car.
Every piece is a closed shell with thickness. Shut lines are V grooves.
A bonnet shut line starts at the windscreen's base, inboard of its lower corners. It bends out to the bonnet's rear corners, then runs forward along the panels it meets, such as the lamps and the wings, then across the nose. The bonnet reaches back to the windscreen, so its line has no rear edge.
Wheel arches step round at even angles.
The bonnet crown narrows with the nose, so no beak forms at the centre.

## 5. Add the parts (parts.py)
Lamps, grille slats and badges are raised parts lying on the panel.
Trace every headlight and tail light outline closely from the reference. Never approximate a lamp shape.
Lamp interiors are our own, not copied from the real car.
Lamp parts and materials are named so the game can light them: `lamp_head`, `lamp_tail`, `lamp_brake`, `lamp_rain`.
Parts that swing with a door start with its name: `door_l_glass`, `door_l_card`, `door_l_mirror`.
The interior gets a firewall, a dash meeting it, seats, door cards and a mirror.
Door mirrors get a separate recessed glass part.
Every car is left-hand drive: the driver's seat and wheel sit on the left, because the pit lane is on the left. Both doors open, so the car stays symmetrical.
Lay each window's glass, black band and cutter along that window's own normal, never along the car's axes. A shallow window projected from straight behind or above stretches its band.
Rebuild each window outline from plain fits: one even curve for each long edge and a straight line for each side. A measured outline carries the reference's waves into the band.
Every other inner face uses an interior material.
Paint every car amber, whatever colour the reference car is.
The paint material is named `body`, so the game can respray it.

## 6. Badge and name
The brand badge from `src/renderer/assets/brands/<brand>/` goes on the front and rear.
The model name goes on the car in the brand font, as letter outlines (glyphs.py).
Badge and name placement are open calls for Ivan, asked in the viewer.
Lay every badge and name flat on its panel: project it along the panel's normal at its centre, never along the car's axes, so a sloped panel cannot stretch it.
No real brand or sponsor graphics.

## 7. Show Ivan the direction first
export.py builds the pieces with Blender's Python module (`pip install bpy`) and exports OBJ and MTL. It prints the triangle count for information only.
publish.py writes the orbitable viewer with a wireframe toggle and a toggle for each lamp group.
Publish the viewer as an Artifact and show Ivan at once.
Run no checks until Ivan confirms the direction.

## 8. Check, then fix
After Ivan confirms the direction, run check.py. It finds open or inward shells, floating parts, intersecting pairs and z-fighting.
check.py also runs with every door open.
Close-ups come before posting: the nose, the shut lines, the lamps.
A pillar shut line runs down the pillar's middle, so each door shows the same width of black.
Black window trim is one even band, with no jump at a shut line.
Fix only the hits Ivan could see. Ignore hits hidden inside other parts or on options he did not pick. Then show Ivan the fixed version. He must say ok before it goes in.

## 9. Put it in the game
Record Ivan's viewer picks in rows.py, then `export.py <dir> --game` writes only the picked options with Unity axes (forward +Z, up +Y, metres).
Export into `unity/Assets/Art/Vehicles/<Name>/` with the OBJ, MTL and textures.
export.py writes each door's hinge axis and open angle, in Unity axes, to `<Name>.doors.json` beside the OBJ.
Every file and folder gets a committed `.meta` with its own random guid.
The build scripts live in `tools/cars/<name>/`: rows.py, rows.json, body.py, parts.py, glyphs.py, glyphs.json, export.py, publish.py, check.py.
Shared primitives live in `tools/cars/common/`: lofting, laying shapes on the body, grooves, booleans, lathing, export, the viewer, checks and glyph outlines. They hold no shapes, sizes, colours or part designs. Every visual decision is written fresh in the car's own scripts. Nothing visual carries over from another car.
Merge to main so Ivan can test it in Unity.
