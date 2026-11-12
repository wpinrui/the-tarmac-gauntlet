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

If a tree's size changes, copy its height, radius and middle from `<bake folder>/<name>.json` into
`LandscapePlan.ModelHeight`, `LandscapePlan.ModelRadius` and `Landscape.CentreAbove`.

## Previews

    bash tools/landscape/check/run.sh --out <plan folder>
    node preview.js <plan folder> <bake folder> <out folder>

Renders each look from the cameras in `shots.json`, in a browser, with the same impostors.
