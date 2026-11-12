# Landscape tools

The country round the circuit (`unity/Assets/Raceday/Scripts/Landscape.cs`): fields, hedges, forest,
copses and hills. Layout is in `LandscapePlan.cs` and `Horizon.cs`, the looks in `LandscapeLooks.cs`.

Trees are hemi-octahedral impostors: each tree is one quad showing a view of a Poly Haven model
baked from the nearest of 8 x 8 directions (`Resources/Landscape/Impostor.shader`).

## Checks

    bash tools/landscape/check/run.sh

Lays out every look with the game's own code and checks nothing stands within 15 m behind the armco
or in the paddock, and that the few trees within 35 m stand clear of grandstands and the inside of
tight bends. Needs the .NET 8 SDK.

## Baking the trees again

Needs Node and Python 3 with numpy and Pillow, and the Poly Haven glTF downloads (1k) of the models
named in `trees.json`, each in `<folder>/<id>/<id>_1k.gltf`.

    cd tools/landscape && npm install
    node bake.js <poly haven folder> <bake folder>
    python3 -I pack.py <bake folder> ../../unity/Assets/Raceday/Resources/Landscape oak beech birch pine_a pine_b spruce_a spruce_b bush
    python3 -I frames.py ../../unity/Assets/Raceday/Resources/Landscape oak beech birch pine_a pine_b spruce_a spruce_b bush

`frames.py` writes `crop.bytes`, the leaves' bounds in every frame, which the shader cuts each quad down to.

If a tree's size changes, copy its height, radius and middle from `<bake folder>/<name>.json` into
`LandscapePlan.ModelHeight`, `LandscapePlan.ModelRadius` and `Landscape.CentreAbove`.

## Close trees

The trees nearest the camera are drawn as meshes from the same models: bark cut down with meshoptimizer and the
leaves as cards showing clusters of the model's own leaves. Each kind's `near` settings in `trees.json` set the cube
the cards are gathered in; the cards' size is grown or shrunk until they cover what the impostor does.

    node trees3d.js <poly haven folder>
    node trees3d-view.js <out folder> ../../unity/Assets/Raceday/Resources/Landscape --calibrate
    node trees3d-view.js <out folder> ../../unity/Assets/Raceday/Resources/Landscape

`trees3d.js` writes `Resources/Landscape/Trees3D`. `--calibrate` lights each tree from several suns and scales its
colour until it matches its impostor, so the hand-over doesn't show; run it twice after every bake. Without it the
viewer draws both levels beside the impostor.

## Cost

    bash tools/landscape/check/run.sh --out <plan folder>
    node cost.js <plan folder> [--step 20] [--no-crop] [--compare-crop]

Counts, from the driver's seat every `--step` metres round the lap, the chunks the camera draws and the fragments
the trees make per screen pixel: every fragment of every quad (each one samples the atlas), those that pass the alpha
test, and with the trees' depth drawn first, the ones the lit pass tests and lights. Prints the mean and the worst
views. `--compare-crop` instead counts the pixels where cutting the quads down changes the trees' outline.

## Previews

    bash tools/landscape/check/run.sh --out <plan folder>
    node preview.js <plan folder> <bake folder> <out folder>

Renders each look from the cameras in `shots.json`, in a browser, with the same impostors. `--foliage 1` or `2`
adds the grass and close trees at that setting; `--shots foliage-shots.json` uses cameras close to them. With foliage, each
shot averages 16 frames of the hand-over dither, as the game's upscaler does.
