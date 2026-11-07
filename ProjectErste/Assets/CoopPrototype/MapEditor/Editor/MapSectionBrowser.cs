using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace ShapeCoop.Editor
{
    public static class MapSectionBrowser
    {
        public const string SectionsPath = "Assets/CoopMaps/Sections";
        public sealed class Entry { public string scenePath, displayName, id; }
        public static List<Entry> FindSections()
        {
            var entries = new List<Entry>();
            if (!AssetDatabase.IsValidFolder(SectionsPath)) return entries;
            foreach (string guid in AssetDatabase.FindAssets("t:Scene", new[] { SectionsPath }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith("/Work.unity", StringComparison.OrdinalIgnoreCase)) continue;
                string folder = Path.GetFileName(Path.GetDirectoryName(path));
                int separator = folder.LastIndexOf('_');
                bool hasId = separator >= 0 && Guid.TryParseExact(folder.Substring(separator + 1), "N", out _);
                entries.Add(new Entry { scenePath = path, displayName = hasId ? folder.Substring(0, separator) : folder, id = hasId ? folder.Substring(separator + 1, 8) : "" });
            }
            entries.Sort((a,b) => { int c = StringComparer.CurrentCultureIgnoreCase.Compare(a.displayName,b.displayName); return c != 0 ? c : StringComparer.Ordinal.Compare(a.scenePath,b.scenePath); });
            return entries;
        }
        public static bool OpenSection(string path, bool prompt)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || string.IsNullOrEmpty(path) || !path.StartsWith(SectionsPath + "/", StringComparison.Ordinal) || !path.EndsWith("/Work.unity", StringComparison.OrdinalIgnoreCase) || AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null) return false;
            if (SceneManager.GetActiveScene().path == path) return true;
            if (prompt && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return false;
            var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
            return scene.IsValid() && scene.isLoaded;
        }
    }
}
