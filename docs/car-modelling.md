# Car modelling workflow

Every 3D car is built this way. It needs no 3D model of the real car.

## Priorities
Smooth, recognisable lines come first. That often means simpler geometry.
Tiny details are left out to keep geometry simple and safe from defects.
There is no triangle budget.
Otherwise follow the reference.

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

## 4. Build the body (body.py)
body.py lofts the outer skin from rows.json, in pure numpy.
Build space is metres: x forward from mid wheelbase, y left, z up from the ground.
The skin splits into pieces: body, doors, bonnet, boot or engine cover, glass.
Every piece is a closed shell with thickness. Shut lines are V grooves.
Wheel arches step round at even angles.
The bonnet crown narrows with the nose, so no beak forms at the centre.

## 5. Add the parts (parts.py)
Lamps, grille slats and badges are raised parts lying on the panel.
Trace every headlight and tail light outline closely from the reference. Never approximate a lamp shape.
Lamp interiors are our own, not copied from the real car.
Lamp parts and materials are named so the game can light them: `lamp_head`, `lamp_tail`, `lamp_brake`, `lamp_rain`.
The interior gets a firewall, a dash meeting it, seats, door cards and a mirror.
Door mirrors get a separate recessed glass part.
Every inner face uses an interior material.

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
After Ivan confirms the direction, check.py must find no open or inward shells, no floating parts, no intersecting pairs and no z-fighting.
Close-ups come before posting: the nose, the shut lines, the lamps.
A pillar shut line runs down the pillar's middle, so each door shows the same width of black.
Black window trim is one even band, with no jump at a shut line.
Fix every hit, then show Ivan the fixed version. He must say ok before it goes in.

## 9. Put it in the game
Record Ivan's viewer picks in rows.py, then `export.py <dir> --game` writes only the picked options with Unity axes (forward +Z, up +Y, metres).
Export into `unity/Assets/Art/Vehicles/<Name>/` with the OBJ, MTL and textures.
Every file and folder gets a committed `.meta` with its own random guid.
The build scripts live in `tools/cars/<name>/`: rows.py, rows.json, body.py, parts.py, glyphs.py, glyphs.json, export.py, publish.py, check.py.
Shared primitives live in `tools/cars/common/`: lofting, laying shapes on the body, grooves, booleans, lathing, export, the viewer, checks and glyph outlines. They hold no shapes, sizes, colours or part designs. Every visual decision is written fresh in the car's own scripts. Nothing visual carries over from another car.
Merge to main so Ivan can test it in Unity.
