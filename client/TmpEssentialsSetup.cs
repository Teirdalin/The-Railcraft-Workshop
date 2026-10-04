using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace EcoMinecarts.Editor
{
    // TextMeshPro's popup imports this same local package. Batch builds need
    // the font/settings assets too, otherwise destination boards have no font.
    public static class TmpEssentialsSetup
    {
        private const string Settings="Assets/TextMesh Pro/Resources/TMP Settings.asset";
        private static DateTime deadline;
        public static void Ensure()
        {
            if(File.Exists(Settings))return;
            throw new InvalidOperationException("TextMeshPro Essentials are missing. Run tools/Import-TmpEssentials.ps1 before building the client bundle.");
        }

        // ImportPackage is asynchronous. Keep Unity open until its completion
        // creates the settings asset, then close it before normal bundle builds.
        public static void ImportEssentials()
        {
            if(File.Exists(Settings)){EditorApplication.Exit(0);return;}
            var cache="Library/PackageCache";
            if(!Directory.Exists(cache))throw new DirectoryNotFoundException(cache);
            var package=Directory.GetFiles(cache,"TMP Essential Resources.unitypackage",SearchOption.AllDirectories)
                .SingleOrDefault(path=>path.Contains("com.unity.ugui@",StringComparison.Ordinal));
            if(package==null)throw new FileNotFoundException("Unity TextMeshPro Essentials package not found in the local package cache.");
            deadline=DateTime.UtcNow.AddMinutes(3);
            EditorApplication.update+=CheckImport;
            AssetDatabase.ImportPackage(Path.GetFullPath(package),false);
        }
        private static void CheckImport()
        {
            if(File.Exists(Settings))
            {
                EditorApplication.update-=CheckImport;
                AssetDatabase.SaveAssets();
                Debug.Log("ECO_TMP_ESSENTIALS_OK: local TextMeshPro font and settings assets imported.");
                EditorApplication.Exit(0);
            }
            else if(DateTime.UtcNow>=deadline)
            {
                EditorApplication.update-=CheckImport;
                Debug.LogError("TextMeshPro Essentials import timed out.");
                EditorApplication.Exit(1);
            }
        }
    }
}
