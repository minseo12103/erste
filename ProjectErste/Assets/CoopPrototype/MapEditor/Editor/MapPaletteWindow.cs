using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ShapeCoop.Editor
{
    public sealed class MapPaletteWindow : EditorWindow
    {
        readonly List<MapEditorWindow.PaletteItem> items = new List<MapEditorWindow.PaletteItem>();
        readonly List<string> folders = new List<string>();
        readonly List<string> tags = new List<string>();
        string search = "", customTag = "";
        int folderIndex, tagIndex;
        Vector2 scroll, tagScroll, dragStart;
        GameObject selected, dragged;

        [MenuItem("Tools/Shape Coop/Map Editor/Palette")]
        public static void Open()
        {
            MapEditorWindow.Open();
            bool already = HasOpenInstances<MapPaletteWindow>();
            var window = GetWindow<MapPaletteWindow>("맵 팔레트");
            if (!already) { var r = window.position; r.width = Mathf.Max(550, r.width); r.height = Mathf.Max(650, r.height); window.position = r; }
            window.Show();
        }
        void OnEnable() { minSize = new Vector2(400, 450); EditorApplication.projectChanged += OnProjectChanged; Refresh(false); }
        void OnDisable() { EditorApplication.projectChanged -= OnProjectChanged; }
        void OnProjectChanged() { Refresh(false); Repaint(); }
        void Refresh(bool import)
        {
            string oldFolder = folderIndex < folders.Count ? folders[folderIndex] : "전체 폴더";
            string oldTag = tagIndex < tags.Count ? tags[tagIndex] : "전체 태그";
            if (import) AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            items.Clear(); items.AddRange(MapEditorWindow.FindPaletteItems());
            folders.Clear(); folders.Add("전체 폴더");
            var allTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                if (!folders.Contains(item.category)) folders.Add(item.category);
                foreach (string tag in MapPaletteTags.GetTags(item.prefab)) allTags.Add(tag);
            }
            folders.Sort(1, folders.Count - 1, StringComparer.CurrentCultureIgnoreCase);
            tags.Clear(); tags.Add("전체 태그"); tags.Add("태그 없음");
            tags.AddRange(allTags.OrderBy(tag => tag, StringComparer.CurrentCultureIgnoreCase));
            folderIndex = Mathf.Max(0, folders.IndexOf(oldFolder)); tagIndex = Mathf.Max(0, tags.IndexOf(oldTag));
            if (selected != null && !items.Exists(item => item.prefab == selected)) selected = null;
        }
        void OnGUI()
        {
            EditorGUILayout.LabelField("맵 오브젝트 팔레트", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("새로고침")) Refresh(true);
                if (GUILayout.Button("배치용 폴더 보기")) EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(MapEditorInstaller.PalettePath));
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                folderIndex = EditorGUILayout.Popup(folderIndex, folders.ToArray());
                tagIndex = EditorGUILayout.Popup(tagIndex, tags.ToArray());
            }
            search = EditorGUILayout.TextField("검색", search);
            EditorGUILayout.LabelField("프리팹을 Scene에 끌어 놓거나 클릭하여 연속 배치할 오브젝트로 선택하세요.", EditorStyles.wordWrappedMiniLabel);
            var visible = new List<MapEditorWindow.PaletteItem>();
            foreach (var item in items)
            {
                if (item.prefab == null || (folderIndex > 0 && item.category != folders[folderIndex])) continue;
                var itemTags = MapPaletteTags.GetTags(item.prefab);
                if (tagIndex == 1 && itemTags.Length != 0) continue;
                if (tagIndex > 1 && !itemTags.Contains(tags[tagIndex], StringComparer.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrWhiteSpace(search) && item.label.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 && item.path.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0 && !itemTags.Any(tag => tag.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                visible.Add(item);
            }
            // Reserve all remaining vertical space for the list. A fixed-size layout
            // rectangle avoids the scroll content imposing a maximum viewport height.
            Rect paletteRect = GUILayoutUtility.GetRect(1, 1, GUILayout.MinHeight(120), GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUI.Box(paletteRect, GUIContent.none, EditorStyles.helpBox);
            Rect viewport = new Rect(paletteRect.x + 2, paletteRect.y + 2, Mathf.Max(1, paletteRect.width - 4), Mathf.Max(1, paletteRect.height - 4));
            float contentWidth = Mathf.Max(1, viewport.width - 16);
            int columns = Mathf.Max(1, Mathf.FloorToInt((contentWidth + 4) / 130));
            int rows = (visible.Count + columns - 1) / columns;
            Rect content = new Rect(0, 0, contentWidth, Mathf.Max(30, rows * 134));
            scroll = GUI.BeginScrollView(viewport, scroll, content, false, false);
            for (int i = 0; i < visible.Count; i++)
                DrawItem(visible[i], new Rect((i % columns) * 130, (i / columns) * 134, 126, 130));
            if (visible.Count == 0) EditorGUI.HelpBox(new Rect(2, 2, Mathf.Max(1, contentWidth - 4), 28), "조건에 맞는 프리팹이 없습니다.", MessageType.Info);
            GUI.EndScrollView();
            EditorGUILayout.Space();
            DrawSelected();
        }
        void DrawItem(MapEditorWindow.PaletteItem item, Rect rect)
        {
            var asset = item.prefab;
            GUI.Box(rect, new GUIContent("", item.path), selected == asset ? EditorStyles.helpBox : GUI.skin.box);
            var image = AssetPreview.GetAssetPreview(asset) ?? AssetPreview.GetMiniThumbnail(asset);
            if (image != null) GUI.DrawTexture(new Rect(rect.x + 23, rect.y + 4, 80, 80), image, ScaleMode.ScaleToFit, true);
            if (AssetPreview.IsLoadingAssetPreview(asset.GetInstanceID())) Repaint();
            GUI.Label(new Rect(rect.x + 4, rect.y + 85, 118, 20), new GUIContent(item.label, item.path), EditorStyles.centeredGreyMiniLabel);
            GUI.Label(new Rect(rect.x + 4, rect.y + 105, 118, 20), string.Join(", ", MapPaletteTags.GetTags(asset)), EditorStyles.centeredGreyMiniLabel);
            var e = Event.current; int control = GUIUtility.GetControlID(FocusType.Passive, rect); var type = e.GetTypeForControl(control);
            if (type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition)) { GUIUtility.hotControl = control; dragged = asset; dragStart = e.mousePosition; e.Use(); }
            else if (type == EventType.MouseDrag && GUIUtility.hotControl == control && dragged == asset && Vector2.Distance(e.mousePosition, dragStart) > 3)
            { selected = asset; MapEditorWindow.SetPaletteSelection(asset); GUIUtility.hotControl = 0; DragAndDrop.PrepareStartDrag(); DragAndDrop.objectReferences = new UnityEngine.Object[] { asset }; DragAndDrop.StartDrag(item.label); dragged = null; e.Use(); }
            else if (type == EventType.MouseUp && GUIUtility.hotControl == control)
            { GUIUtility.hotControl = 0; dragged = null; if (rect.Contains(e.mousePosition)) { selected = asset; MapEditorWindow.SetPaletteSelection(asset); } e.Use(); }
        }
        void DrawSelected()
        {
            if (selected == null) { EditorGUILayout.HelpBox("오브젝트를 클릭하면 태그를 설정할 수 있습니다.", MessageType.None); return; }
            EditorGUILayout.LabelField("선택: " + selected.name, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(AssetDatabase.GetAssetPath(selected), EditorStyles.wordWrappedMiniLabel);
            var current = new List<string>(MapPaletteTags.GetTags(selected));
            var choices = new List<string>(MapPaletteTags.Presets);
            foreach (var tag in current) if (!choices.Contains(tag, StringComparer.OrdinalIgnoreCase)) choices.Add(tag);
            tagScroll = EditorGUILayout.BeginScrollView(tagScroll, GUILayout.Height(70), GUILayout.ExpandHeight(false));
            foreach (string tag in choices)
            {
                bool old = current.Contains(tag, StringComparer.OrdinalIgnoreCase);
                bool next = EditorGUILayout.ToggleLeft(tag, old);
                if (next != old)
                {
                    if (next) current.Add(tag); else current.RemoveAll(value => string.Equals(value, tag, StringComparison.OrdinalIgnoreCase));
                    MapPaletteTags.SetTags(selected, current); Refresh(false); GUIUtility.ExitGUI();
                }
            }
            EditorGUILayout.EndScrollView();
            using (new EditorGUILayout.HorizontalScope())
            {
                customTag = EditorGUILayout.TextField("새 태그", customTag);
                if (GUILayout.Button("추가", GUILayout.Width(55)) && !string.IsNullOrWhiteSpace(customTag))
                { current.Add(customTag.Trim()); MapPaletteTags.SetTags(selected, current); customTag = ""; Refresh(false); GUIUtility.ExitGUI(); }
            }
            EditorGUILayout.LabelField("태그는 프리팹의 .meta에 저장되며 게임의 Unity Tag와는 별개입니다.", EditorStyles.wordWrappedMiniLabel);
        }
    }
}
