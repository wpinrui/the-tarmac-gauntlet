using UnityEditor;

namespace TarmacGauntlet.Management.Editor
{
    /// <summary>
    /// Keeps every car model's meshes readable in a built game, so the wheel blur (WheelBlur) can work out each rim's
    /// look from its triangles. Covers every model under Assets/Art/Vehicles, including ones added later.
    /// </summary>
    public sealed class CarModelImport : AssetPostprocessor
    {
        private const string Vehicles = "Assets/Art/Vehicles/";

        public override uint GetVersion() => 1;

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Vehicles)) return;
            if (assetImporter is ModelImporter model) model.isReadable = true;
        }
    }
}
