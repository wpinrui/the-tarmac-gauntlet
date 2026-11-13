using System;
using System.Globalization;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace TarmacGauntlet.Management.Editor
{
    /// <summary>
    /// Writes the build time, in UTC, into the BuildStamp resource before every player build, from the Tarmac
    /// Gauntlet menu or a Build Profile. The main menu shows it.
    /// </summary>
    internal sealed class BuildStampWriter : IPreprocessBuildWithReport
    {
        private const string Folder = "Assets/Visuals/Generated/Resources";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            Directory.CreateDirectory(Folder);
            string path = Folder + "/BuildStamp.txt";
            File.WriteAllText(path, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
        }
    }
}
