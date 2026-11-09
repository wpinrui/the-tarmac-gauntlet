# Car modelling workflow

Every 3D car is built this way. It needs no 3D model of the real car.

## Priorities
Smooth, recognisable lines come first. That often means simpler geometry.
There is no triangle budget.

## 1. Start
Each car has one locked real-car reference, listed in `car_roster.md`.
Never offer an alternative reference.
Every request to Ivan names the real car the model is based on.
List every open call before building: counts, options, names, placement, colours. Ivan answers first.
Every option set for a feature includes the reference car's own design.

## 2. Reference
Ivan gathers all reference. Never search for it.
Ask him for: a four-view drawing (side, front, rear, top), detail photos, and the wheelbase, length, width and tyre size.
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
Lamp parts and materials are named so the game can light them: `lamp_head`, `lamp_tail`, `lamp_brake`, `lamp_indicator`, `lamp_reverse`, `lamp_rain`.
Indicators, side repeaters and a reversing lamp are open calls for Ivan.
The interior gets a firewall, a dash meeting it, seats, door cards and a mirror.
Door mirrors get a separate recessed glass part.
Every inner face uses an interior material.

## 6. Brand, name and numbers
The brand badge from `src/renderer/assets/brands/<brand>/` goes on the front and rear.
The model name goes on the car in the brand font, as letter outlines (glyphs.py).
Placement of the badge, name, plate and race number are open calls for Ivan.
No real brand or sponsor graphics.

## 7. Show Ivan the direction first
export.py builds the pieces with Blender's Python module (`pip install bpy`) and exports OBJ and MTL. It prints the triangle count for information only.
publish.py writes the orbitable viewer with a wireframe toggle.
Publish the viewer as an Artifact and show Ivan at once.
Run no checks until Ivan confirms the direction.

## 8. Check, then fix
After Ivan confirms the direction, check.py must find no open or inward shells, no floating parts, no intersecting pairs and no z-fighting.
Close-ups come before posting: the nose, the shut lines, the lamps.
A pillar shut line runs down the pillar's middle, so each door shows the same width of black.
Black window trim is one even band, with no jump at a shut line.
Fix every hit, then show Ivan the fixed version. He must say ok before it goes in.

## 9. Put it in the game
Export into `unity/Assets/Art/Vehicles/<Name>/` with the OBJ, MTL, textures and CREDITS.md.
Every file and folder gets a committed `.meta` with its own random guid.
The build scripts live in `tools/cars/<name>/`: rows.py, rows.json, body.py, parts.py, glyphs.py, glyphs.json, export.py, publish.py, check.py.
Copy them from the last car built and change the numbers.
Merge to main so Ivan can test it in Unity.
