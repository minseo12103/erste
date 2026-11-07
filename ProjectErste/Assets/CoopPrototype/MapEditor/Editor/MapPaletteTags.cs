using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ShapeCoop.Editor
{
    // Asset labels are stored in each prefab's .meta file, alongside unrelated labels.
    public static class MapPaletteTags
    {
        const string Prefix = "ShapeMapTag_";
        const string Configured = "ShapeMapTags_Configured";
        public static readonly string[] Presets = { "배치용", "기능 오브젝트", "시작 지점", "목표 지점", "연결점" };
        static string Encode(string tag) => Prefix + BitConverter.ToString(Encoding.UTF8.GetBytes(tag)).Replace("-", "");
        static string Decode(string label)
        {
            if (!label.StartsWith(Prefix, StringComparison.Ordinal)) return null;
            string hex = label.Substring(Prefix.Length);
            if (hex.Length == 0 || hex.Length % 2 != 0) return null;
            try { var bytes = new byte[hex.Length / 2]; for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16); return Encoding.UTF8.GetString(bytes); }
            catch (FormatException) { return null; }
        }
        public static string[] GetTags(GameObject prefab)
        {
            if (prefab == null) return Array.Empty<string>();
            var labels = AssetDatabase.GetLabels(prefab);
            if (labels.Contains(Configured)) return labels.Select(Decode).Where(tag => !string.IsNullOrWhiteSpace(tag)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (prefab.GetComponentInChildren<MapGimmick>(true) != null) return new[] { "기능 오브젝트" };
            if (prefab.GetComponentInChildren<MapMarker>(true) != null) return new[] { "시작 지점" };
            if (prefab.GetComponentInChildren<MapConnector>(true) != null) return new[] { "연결점" };
            if (prefab.GetComponentInChildren<MapGoalArea>(true) != null) return new[] { "기능 오브젝트", "목표 지점" };
            string path = AssetDatabase.GetAssetPath(prefab);
            if (path == MapEditorInstaller.PalettePath + "/Terrain_Box.prefab" || path == MapEditorInstaller.PalettePath + "/Terrain_Triangle.prefab" || path == MapEditorInstaller.PalettePath + "/Terrain_Circle.prefab") return new[] { "배치용" };
            return Array.Empty<string>();
        }
        public static void SetTags(GameObject prefab, IEnumerable<string> tags)
        {
            if (prefab == null || !EditorUtility.IsPersistent(prefab)) return;
            var labels = AssetDatabase.GetLabels(prefab).Where(label => label != Configured && !label.StartsWith(Prefix, StringComparison.Ordinal)).ToList();
            labels.Add(Configured);
            foreach (var tag in tags.Select(value => value == null ? "" : value.Trim()).Where(value => value.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)) labels.Add(Encode(tag));
            AssetDatabase.SetLabels(prefab, labels.ToArray());
        }
    }
}
