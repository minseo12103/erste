using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShapeCoop.Editor
{
    [InitializeOnLoad]
    public sealed class MapEditorWindow : EditorWindow
    {
        const string SectionsPath = MapSectionBrowser.SectionsPath;
        public sealed class PaletteItem
        {
            public GameObject prefab;
            public string path, label, category;
        }
        readonly List<MapSectionBrowser.Entry> sections = new List<MapSectionBrowser.Entry>();
        string sectionName = "Section", sectionSearch = "";
        Vector2 sectionScroll, mainScroll;
        bool showSections = true;
        GameObject paintPrefab, previewPrefab;
        Vector3 previewPosition;
        enum SceneTool { None, Paint, PlayerStart, BallStart }
        SceneTool sceneTool;
        float placementRotation;
        bool snap;
        float grid;
        static MapEditorWindow snapSettingsOwner;
        bool ownsMoveSnap;
        bool previousGridSnap, previousSnap;
        Vector3 previousGridSize, previousMoveSnap;
        PivotRotation previousPivotRotation;
        Transform[] rectDragTargets;
        Transform rectDragAnchor;
        bool rectDragging, rectResizing, rectSnapPending, applyingRectSnap;
        int rectUndoGroup;

        static MapEditorWindow()
        {
            EditorSceneManager.sceneOpened += (scene, mode) => { if (scene.path == MapEditorInstaller.ScenePath) Open(); };
            EditorApplication.delayCall += () => { if (SceneManager.GetActiveScene().path == MapEditorInstaller.ScenePath) Open(); };
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingEditMode)
                {
                    bool mapTest = CurrentTest() != null;
                    SessionState.SetBool("ShapeCoop.MapTestPlay", mapTest);
                    var windows = Resources.FindObjectsOfTypeAll<MapEditorWindow>();
                    SessionState.SetBool("ShapeCoop.MapEditorReopen", mapTest && windows.Length > 0);
                    var palettes = Resources.FindObjectsOfTypeAll<MapPaletteWindow>();
                    SessionState.SetBool("ShapeCoop.MapPaletteReopen", mapTest && palettes.Length > 0);
                    if (mapTest) foreach (var window in windows) window.Close();
                    if (mapTest) foreach (var palette in palettes) palette.Close();
                }
                else if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool("ShapeCoop.MapEditorReopen", false))
                {
                    SessionState.SetBool("ShapeCoop.MapEditorReopen", false);
                    EditorApplication.delayCall += Open;
                }
                if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool("ShapeCoop.MapPaletteReopen", false))
                { SessionState.SetBool("ShapeCoop.MapPaletteReopen", false); EditorApplication.delayCall += MapPaletteWindow.Open; }
            };
        }
        [MenuItem("Tools/Shape Coop/Map Editor/Open")]
        public static void Open()
        {
            bool alreadyOpen = HasOpenInstances<MapEditorWindow>();
            var window = GetWindow<MapEditorWindow>("Shape Map Editor");
            if (!alreadyOpen) { var r = window.position; r.width = Mathf.Max(r.width, 340); r.height = Mathf.Max(r.height, 760); window.position = r; }
            window.Show();
        }
        void OnEnable()
        {
            minSize = new Vector2(340, 500);
            snap = EditorPrefs.GetBool("ShapeCoop.MapSnap", true); grid = Mathf.Max(.05f, EditorPrefs.GetFloat("ShapeCoop.MapGrid", .5f));
            SceneView.duringSceneGui += HandleSceneDrag;
            EditorApplication.projectChanged += OnProjectChanged;
            EditorApplication.update += SyncMoveSnapping;
            AssemblyReloadEvents.beforeAssemblyReload += RestoreMoveSnapping;
            Undo.postprocessModifications += PostprocessRectMove;
            ReloadSections();
        }
        void OnDisable() { SceneView.duringSceneGui -= HandleSceneDrag; EditorApplication.projectChanged -= OnProjectChanged; EditorApplication.update -= SyncMoveSnapping; AssemblyReloadEvents.beforeAssemblyReload -= RestoreMoveSnapping; Undo.postprocessModifications -= PostprocessRectMove; CancelRectDrag(); RestoreMoveSnapping(); }
        void SyncMoveSnapping()
        {
            var section = CurrentSection();
            var selection = Selection.transforms;
            bool moveTool = Tools.current == Tool.Move || Tools.current == Tool.Rect;
            bool eligible = !EditorApplication.isPlayingOrWillChangePlaymode && section != null && sceneTool == SceneTool.None && moveTool && selection.Length > 0;
            if (eligible)
                foreach (var item in selection) if (item == null || item == section.transform || !item.IsChildOf(section.transform)) { eligible = false; break; }
            if (!eligible) { CancelRectDrag(); RestoreMoveSnapping(); return; }
            if (snapSettingsOwner != null && snapSettingsOwner != this) return;
            if (!ownsMoveSnap)
            {
                snapSettingsOwner = this; ownsMoveSnap = true;
                previousGridSnap = EditorSnapSettings.gridSnapEnabled; previousSnap = EditorSnapSettings.snapEnabled;
                previousGridSize = EditorSnapSettings.gridSize; previousMoveSnap = EditorSnapSettings.move; previousPivotRotation = Tools.pivotRotation;
            }
            if (snap && Tools.current == Tool.Move)
            {
                float size = Mathf.Max(.05f, grid);
                EditorSnapSettings.gridSize = new Vector3(size, size, previousGridSize.z);
                EditorSnapSettings.move = new Vector3(size, size, previousMoveSnap.z);
                Tools.pivotRotation = PivotRotation.Global;
                EditorSnapSettings.gridSnapEnabled = true; EditorSnapSettings.snapEnabled = true;
            }
            else
            {
                if (!snap) CancelRectDrag();
                EditorSnapSettings.gridSnapEnabled = false; EditorSnapSettings.snapEnabled = false;
                EditorSnapSettings.gridSize = previousGridSize; EditorSnapSettings.move = previousMoveSnap; Tools.pivotRotation = previousPivotRotation;
            }
        }
        void BeginRectDrag(MapSection section)
        {
            CancelRectDrag();
            if (!snap || Tools.current != Tool.Rect || sceneTool != SceneTool.None || section == null) return;
            SyncMoveSnapping(); if (!ownsMoveSnap) return;
            rectDragTargets = Selection.GetTransforms(SelectionMode.TopLevel | SelectionMode.Editable);
            if (rectDragTargets.Length == 0) return;
            foreach (var item in rectDragTargets) if (item == null || item == section.transform || !item.IsChildOf(section.transform)) { rectDragTargets = null; return; }
            var active = Selection.activeTransform;
            rectDragAnchor = rectDragTargets[0];
            foreach (var item in rectDragTargets) if (active != null && (active == item || active.IsChildOf(item))) { rectDragAnchor = item; break; }
            rectDragging = true;
        }
        UndoPropertyModification[] PostprocessRectMove(UndoPropertyModification[] modifications)
        {
            if (!rectDragging || rectResizing || !snap || !ownsMoveSnap || Tools.current != Tool.Rect || applyingRectSnap || Undo.isProcessing || rectDragTargets == null) return modifications;
            bool moved = false, resized = false;
            foreach (var modification in modifications)
            {
                if (modification.currentValue == null) continue;
                var transform = modification.currentValue.target as Transform;
                if (transform == null || Array.IndexOf(rectDragTargets, transform) < 0) continue;
                string path = modification.currentValue.propertyPath;
                if (path.StartsWith("m_LocalScale.", StringComparison.Ordinal) || path.StartsWith("m_LocalRotation.", StringComparison.Ordinal) || path.StartsWith("m_LocalEulerAnglesHint.", StringComparison.Ordinal)) resized = true;
                else if (path.StartsWith("m_LocalPosition.", StringComparison.Ordinal)) moved = true;
            }
            if (resized) { rectResizing = true; if (rectSnapPending) { EditorApplication.delayCall -= ApplyPendingRectSnap; rectSnapPending = false; } }
            else if (moved)
            {
                rectUndoGroup = Undo.GetCurrentGroup();
                if (!rectSnapPending) { rectSnapPending = true; EditorApplication.delayCall += ApplyPendingRectSnap; }
            }
            return modifications;
        }
        void ApplyPendingRectSnap()
        {
            rectSnapPending = false;
            var active = Selection.activeTransform;
            if (this == null || !snap || !ownsMoveSnap || rectResizing || rectDragAnchor == null || rectDragTargets == null || Tools.current != Tool.Rect || Undo.isProcessing || Undo.GetCurrentGroup() != rectUndoGroup || active == null || (active != rectDragAnchor && !active.IsChildOf(rectDragAnchor))) return;
            foreach (var item in rectDragTargets) if (item == null) return;
            Vector3 p = rectDragAnchor.position; float size = Mathf.Max(.05f, grid);
            Vector3 adjustment = new Vector3(Mathf.Round(p.x / size) * size - p.x, Mathf.Round(p.y / size) * size - p.y, 0);
            if (adjustment.sqrMagnitude < 1e-10f) return;
            applyingRectSnap = true;
            try
            {
                Undo.RecordObjects(rectDragTargets, "Move map objects");
                foreach (var item in rectDragTargets) { item.position += adjustment; if (PrefabUtility.IsPartOfPrefabInstance(item)) PrefabUtility.RecordPrefabInstancePropertyModifications(item); }
                Undo.FlushUndoRecordObjects(); Undo.CollapseUndoOperations(rectUndoGroup);
            }
            finally { applyingRectSnap = false; if (!rectDragging) { rectDragTargets = null; rectDragAnchor = null; } }
        }
        void EndRectDrag() { rectDragging = false; rectResizing = false; if (!rectSnapPending) { rectDragTargets = null; rectDragAnchor = null; } }
        void CancelRectDrag()
        {
            EditorApplication.delayCall -= ApplyPendingRectSnap; EditorApplication.delayCall -= EndRectDrag;
            rectSnapPending = rectDragging = rectResizing = false; rectDragTargets = null; rectDragAnchor = null;
        }
        void RestoreMoveSnapping()
        {
            if (!ownsMoveSnap) return;
            EditorSnapSettings.gridSnapEnabled = previousGridSnap; EditorSnapSettings.snapEnabled = previousSnap;
            EditorSnapSettings.gridSize = previousGridSize; EditorSnapSettings.move = previousMoveSnap; Tools.pivotRotation = previousPivotRotation;
            ownsMoveSnap = false; if (snapSettingsOwner == this) snapSettingsOwner = null;
        }
        void OnProjectChanged() { ReloadSections(); Repaint(); }
        void ReloadSections() { sections.Clear(); sections.AddRange(MapSectionBrowser.FindSections()); }
        public static void SetPaletteSelection(GameObject prefab)
        {
            var windows = Resources.FindObjectsOfTypeAll<MapEditorWindow>();
            if (windows.Length == 0) { Open(); windows = Resources.FindObjectsOfTypeAll<MapEditorWindow>(); }
            foreach (var window in windows) { window.paintPrefab = prefab; window.Repaint(); }
        }
        public static List<PaletteItem> FindPaletteItems()
        {
            var items = new List<PaletteItem>();
            if (!AssetDatabase.IsValidFolder(MapEditorInstaller.PalettePath)) return items;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { MapEditorInstaller.PalettePath }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null) continue;
                string relative = path.Substring(MapEditorInstaller.PalettePath.Length + 1);
                int slash = relative.IndexOf('/');
                string group = slash >= 0 ? relative.Substring(0, slash) : "기타";
                string label = relative.Substring(0, relative.Length - ".prefab".Length);
                if (group == "기본 오브젝트") label = label.Substring(slash + 1);
                if (slash < 0)
                {
                    switch (label)
                    {
                        case "Terrain_Box": group = "지형"; label = "사각형 지형"; break;
                        case "Terrain_Triangle": group = "지형"; label = "삼각형 지형"; break;
                        case "Terrain_Circle": group = "지형"; label = "원형 지형"; break;
                        case "Marker_TestStart": group = "시작 지점"; label = "테스트 시작"; break;
                        case "Marker_PlayerStart": group = "시작 지점"; label = "플레이어 시작"; break;
                        case "Marker_BallStart": group = "시작 지점"; label = "공 시작"; break;
                        case "Goal_Area": group = "목표"; label = "목표 구역"; break;
                        case "Connector_Entrance": group = "연결점"; label = "구간 입구"; break;
                        case "Connector_Exit": group = "연결점"; label = "구간 출구"; break;
                    }
                }
                items.Add(new PaletteItem { prefab = prefab, path = path, label = label, category = group });
            }
            items.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.path, b.path));
            return items;
        }
        static MapSection CurrentSection()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return null;
            foreach (var root in scene.GetRootGameObjects()) { var section = root.GetComponent<MapSection>(); if (section != null) return section; }
            return null;
        }
        static MapTestRunner CurrentTest()
        {
            var scene = SceneManager.GetActiveScene(); if (!scene.IsValid() || !scene.isLoaded) return null;
            foreach (var root in scene.GetRootGameObjects()) { var test = root.GetComponent<MapTestRunner>(); if (test != null) return test; }
            return null;
        }
        void OnGUI()
        {
            using (var view = new EditorGUILayout.ScrollViewScope(mainScroll))
            { mainScroll = view.scrollPosition; DrawContents(); }
        }
        void DrawContents()
        {
            EditorGUILayout.LabelField("맵 구간 에디터", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("각 구간은 별도 폴더의 작업 씬과 프리팹으로 저장됩니다. 목록의 항목을 Scene 화면으로 끌어다 놓으세요.", MessageType.Info);
            if (GUILayout.Button("맵 구간 조합 화면 열기")) MapAssemblyWindow.Open();
            if (GUILayout.Button("큰 팔레트 창 열기")) MapPaletteWindow.Open();
            if (GUILayout.Button("캐릭터 크기 비교")) CharacterComparisonWindow.Open();
            if (!AssetDatabase.IsValidFolder(MapEditorInstaller.PalettePath))
            {
                EditorGUILayout.HelpBox("팔레트 리소스가 없습니다.", MessageType.Warning);
                if (GUILayout.Button("팔레트 리소스 설치")) MapEditorInstaller.EnsureAssets();
                return;
            }
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                sectionName = EditorGUILayout.TextField("새 구간 이름", sectionName);
                if (GUILayout.Button("새 구간 만들기", GUILayout.Height(26))) CreateSection(sectionName, true);
                DrawSectionList();
                var section = CurrentSection(); var test = CurrentTest();
                if (section == null || test == null)
                {
                    EditorGUILayout.HelpBox("새 구간을 만들거나 기존 구간의 작업 씬을 여세요. 이 안내 씬 자체에는 배치하지 않습니다.", MessageType.Warning);
                    return;
                }
                EditorGUILayout.Space(); EditorGUILayout.LabelField("현재 구간", section.displayName, EditorStyles.boldLabel);
                if (GUILayout.Button("현재 구간을 프리팹으로 저장", GUILayout.Height(28))) ExportSection(section, true);
                EditorGUILayout.LabelField("저장 위치", section.prefabPath, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(); EditorGUILayout.LabelField("구간 테스트", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                var shape = (MapTestShape)EditorGUILayout.EnumPopup("도형", test.shape);
                var marker = (MapMarker)EditorGUILayout.ObjectField("테스트 시작 지점", test.testStart, typeof(MapMarker), true);
                bool near = EditorGUILayout.Toggle("테스트 위치에 공 생성", test.spawnBallNearTestStart);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(test, "Change map test settings"); test.shape = shape; test.testStart = marker; test.spawnBallNearTestStart = near;
                    EditorUtility.SetDirty(test); EditorSceneManager.MarkSceneDirty(test.gameObject.scene);
                }
                if (GUILayout.Button("선택한 마커를 테스트 시작으로"))
                {
                    var selected = Selection.activeGameObject == null ? null : Selection.activeGameObject.GetComponent<MapMarker>();
                    if (selected != null && selected.transform.IsChildOf(section.transform))
                    { Undo.RecordObject(test, "Set test start"); test.testStart = selected; EditorSceneManager.MarkSceneDirty(test.gameObject.scene); }
                    else ShowNotification(new GUIContent("이 구간의 시작 마커를 선택하세요"));
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("플레이어 위치 클릭 지정")) { sceneTool = SceneTool.PlayerStart; previewPrefab = null; SceneView.RepaintAll(); }
                    if (GUILayout.Button("공 위치 클릭 지정")) { sceneTool = SceneTool.BallStart; previewPrefab = null; SceneView.RepaintAll(); }
                }
                if (!test.spawnBallNearTestStart && test.useCustomBallStart) EditorGUILayout.LabelField("테스트 공 시작", test.customBallStart.ToString("F2"));
                if (GUILayout.Button("선택 도형으로 테스트 실행", GUILayout.Height(28)))
                {
                    if (test.testStart == null || !test.testStart.transform.IsChildOf(section.transform)) EditorUtility.DisplayDialog("테스트 시작 지점", "현재 구간의 시작 마커를 지정하세요.", "확인");
                    else if (EditorSceneManager.SaveScene(section.gameObject.scene)) EditorApplication.EnterPlaymode();
                }
                EditorGUILayout.Space(); EditorGUILayout.LabelField("배치 팔레트", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck(); snap = EditorGUILayout.Toggle("격자 맞춤", snap);
                if (snap) grid = Mathf.Max(.05f, EditorGUILayout.FloatField("격자 간격", grid));
                if (EditorGUI.EndChangeCheck()) { EditorPrefs.SetBool("ShapeCoop.MapSnap", snap); EditorPrefs.SetFloat("ShapeCoop.MapGrid", grid); SyncMoveSnapping(); }
                EditorGUILayout.LabelField("선택한 오브젝트도 이동 도구(W) 또는 사각형 도구(T)에서 격자에 맞춰 이동합니다.", EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool painting = GUILayout.Toggle(sceneTool == SceneTool.Paint, "연속 배치", "Button");
                    if (painting && sceneTool != SceneTool.Paint) sceneTool = SceneTool.Paint;
                    else if (!painting && sceneTool == SceneTool.Paint) { sceneTool = SceneTool.None; previewPrefab = null; SceneView.RepaintAll(); }
                    placementRotation = EditorGUILayout.FloatField("회전 °", placementRotation);
                }
                if (paintPrefab != null) EditorGUILayout.LabelField("선택된 오브젝트", paintPrefab.name);
                if (sceneTool != SceneTool.None) EditorGUILayout.HelpBox(sceneTool == SceneTool.Paint ? "팔레트에서 프리팹을 선택하고 Scene을 클릭하여 반복 배치하세요. [ / ]로 15도 회전, Esc로 종료합니다." : "Scene을 클릭해 테스트 시작점을 지정하세요. Esc로 취소합니다.", MessageType.Info);
            }
        }
        void DrawSectionList()
        {
            showSections = EditorGUILayout.Foldout(showSections, "기존 구간 열기 (" + sections.Count + ")", true);
            if (!showSections) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                sectionSearch = EditorGUILayout.TextField("검색", sectionSearch);
                if (GUILayout.Button("갱신", GUILayout.Width(48))) { AssetDatabase.Refresh(); ReloadSections(); }
            }
            sectionScroll = EditorGUILayout.BeginScrollView(sectionScroll, GUILayout.Height(Mathf.Clamp(sections.Count * 24, 40, 120)));
            int shown = 0;
            foreach (var entry in sections)
            {
                if (!string.IsNullOrWhiteSpace(sectionSearch) && entry.displayName.IndexOf(sectionSearch, StringComparison.OrdinalIgnoreCase) < 0 && entry.scenePath.IndexOf(sectionSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
                shown++;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(new GUIContent(entry.displayName + (string.IsNullOrEmpty(entry.id) ? "" : "  [" + entry.id + "]"), entry.scenePath));
                    if (GUILayout.Button("열기", GUILayout.Width(48))) { MapSectionBrowser.OpenSection(entry.scenePath, true); GUIUtility.ExitGUI(); }
                }
            }
            if (shown == 0) EditorGUILayout.LabelField("표시할 구간이 없습니다.", EditorStyles.miniLabel);
            EditorGUILayout.EndScrollView();
        }
        void HandleSceneDrag(SceneView sceneView)
        {
            var section = CurrentSection(); var e = Event.current;
            if (section == null || EditorApplication.isPlaying) return;
            if (e.rawType == EventType.MouseDown && e.button == 0 && !e.alt && Tools.current == Tool.Rect) BeginRectDrag(section);
            if (e.rawType == EventType.MouseUp && e.button == 0 && rectDragging) { EditorApplication.delayCall -= EndRectDrag; EditorApplication.delayCall += EndRectDrag; }
            if (sceneTool != SceneTool.None) sceneView.wantsMouseMove = true;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && sceneTool != SceneTool.None) { sceneTool = SceneTool.None; previewPrefab = null; e.Use(); Repaint(); return; }
            if (sceneTool == SceneTool.Paint && e.type == EventType.KeyDown && (e.keyCode == KeyCode.LeftBracket || e.keyCode == KeyCode.RightBracket))
            { placementRotation += e.keyCode == KeyCode.LeftBracket ? -15 : 15; e.Use(); Repaint(); sceneView.Repaint(); }
            if (e.type == EventType.DragExited) { previewPrefab = null; sceneView.Repaint(); return; }
            bool dragging = (e.type == EventType.DragUpdated || e.type == EventType.DragPerform) && DragAndDrop.objectReferences.Length == 1 && DragAndDrop.objectReferences[0] is GameObject;
            GameObject asset = dragging ? DragAndDrop.objectReferences[0] as GameObject : sceneTool == SceneTool.Paint ? paintPrefab : null;
            if (dragging && !AssetDatabase.GetAssetPath(asset).StartsWith(MapEditorInstaller.PalettePath + "/", StringComparison.Ordinal)) return;
            if (e.type == EventType.Repaint && previewPrefab != null) DrawPreview(previewPrefab, previewPosition);
            bool clickTool = sceneTool != SceneTool.None && !e.alt;
            if (clickTool && e.type == EventType.Layout) HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            if (!dragging && !clickTool) return;
            var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            var plane = new Plane(Vector3.forward, Vector3.zero);
            if (!plane.Raycast(ray, out float distance)) return;
            var position = ray.GetPoint(distance); position.z = 0;
            if (snap) { position.x = Mathf.Round(position.x / grid) * grid; position.y = Mathf.Round(position.y / grid) * grid; }
            if (asset != null && (dragging || sceneTool == SceneTool.Paint) && (e.type == EventType.MouseMove || e.type == EventType.DragUpdated))
            { previewPrefab = asset; previewPosition = position; sceneView.Repaint(); }
            if (dragging) DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragPerform || (!dragging && clickTool && e.type == EventType.MouseDown && e.button == 0))
            {
                if (dragging) DragAndDrop.AcceptDrag();
                if (asset != null) PlacePrefab(section, asset, position, placementRotation);
                else if (sceneTool == SceneTool.PlayerStart || sceneTool == SceneTool.BallStart)
                {
                    var test = CurrentTest();
                    if (test != null && sceneTool == SceneTool.PlayerStart)
                    {
                        if (test.testStart == null || test.testStart.kind != MapMarkerKind.TestStart || !test.testStart.transform.IsChildOf(section.transform))
                        {
                            var markerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MapEditorInstaller.PalettePath + "/Marker_TestStart.prefab");
                            var marker = markerPrefab == null ? null : PlacePrefab(section, markerPrefab, position, 0);
                            if (marker != null) { Undo.RecordObject(test, "Set test start"); test.testStart = marker.GetComponent<MapMarker>(); }
                        }
                        if (test.testStart != null && test.testStart.kind == MapMarkerKind.TestStart && test.testStart.transform.IsChildOf(section.transform))
                        { Undo.RecordObject(test.testStart.transform, "Move test start"); test.testStart.transform.position = position; }
                    }
                    else if (test != null && sceneTool == SceneTool.BallStart)
                    { Undo.RecordObject(test, "Set test ball start"); test.spawnBallNearTestStart = false; test.useCustomBallStart = true; test.customBallStart = position; }
                    EditorSceneManager.MarkSceneDirty(section.gameObject.scene); sceneTool = SceneTool.None; Repaint();
                }
                if (dragging) previewPrefab = null;
                e.Use();
            }
            else if (dragging) e.Use();
        }
        public static GameObject PlacePrefab(MapSection section, GameObject asset, Vector3 position, float rotation)
        {
            if (section == null || asset == null) return null;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, section.gameObject.scene);
            Undo.RegisterCreatedObjectUndo(instance, "Place map object"); Undo.SetTransformParent(instance.transform, section.transform, "Place map object");
            instance.transform.position = position; instance.transform.rotation = Quaternion.Euler(0, 0, rotation) * asset.transform.rotation;
            Selection.activeGameObject = instance; EditorSceneManager.MarkSceneDirty(section.gameObject.scene); return instance;
        }
        void DrawPreview(GameObject asset, Vector3 position)
        {
            var renderer = asset.GetComponentInChildren<Renderer>(true);
            var size = renderer != null ? renderer.bounds.size : Vector3.one;
            if (size.x < .01f || size.y < .01f) size = Vector3.one;
            var previous = Handles.matrix; Handles.matrix = Matrix4x4.TRS(position, Quaternion.Euler(0, 0, placementRotation), Vector3.one);
            Handles.color = Color.cyan; Handles.DrawWireCube(Vector3.zero, new Vector3(size.x, size.y, 0)); Handles.matrix = previous; Handles.color = Color.white;
            var image = AssetPreview.GetAssetPreview(asset);
            if (image == null) { SceneView.RepaintAll(); return; }
            var center = HandleUtility.WorldToGUIPoint(position);
            float width = Mathf.Clamp(Vector2.Distance(center, HandleUtility.WorldToGUIPoint(position + Vector3.right * size.x)), 20, 600);
            float height = Mathf.Clamp(Vector2.Distance(center, HandleUtility.WorldToGUIPoint(position + Vector3.up * size.y)), 20, 600);
            Handles.BeginGUI(); var old = GUI.color; GUI.color = new Color(1, 1, 1, .55f);
            var matrix = GUI.matrix; GUIUtility.RotateAroundPivot(-placementRotation, center);
            GUI.DrawTexture(new Rect(center.x - width / 2, center.y - height / 2, width, height), image, ScaleMode.ScaleToFit, true);
            GUI.matrix = matrix; GUI.color = old; Handles.EndGUI();
        }
        public static MapSection CreateSection(string name, bool prompt)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return null;
            if (prompt && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return null;
            if (!AssetDatabase.IsValidFolder(SectionsPath)) { Directory.CreateDirectory(SectionsPath); AssetDatabase.Refresh(); }
            string safe = ""; foreach (char c in name.Trim()) if (char.IsLetterOrDigit(c) || c == '_' || c == '-') safe += c;
            if (string.IsNullOrEmpty(safe)) safe = "Section"; if (safe.Length > 40) safe = safe.Substring(0, 40);
            string id = Guid.NewGuid().ToString("N"); string folder = SectionsPath + "/" + safe + "_" + id;
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(SectionsPath, safe + "_" + id))) throw new IOException("Failed to create map folder: " + folder);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Map Section - " + safe); var section = root.AddComponent<MapSection>();
            section.sectionId = id; section.displayName = safe; section.prefabPath = folder + "/Section.prefab";
            var camera = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)); camera.tag = "MainCamera"; camera.transform.position = new Vector3(0,2,-10);
            var cam = camera.GetComponent<Camera>(); cam.orthographic = true; cam.orthographicSize = 7; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.09f,.13f,.18f);
            var testObject = new GameObject("Map Test Settings"); var test = testObject.AddComponent<MapTestRunner>(); test.section = section;
            var system = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/CoopPrototype/Prefabs/PrototypeSystem.prefab");
            if (system != null) test.settingsSource = system.GetComponent<PrototypeSettings>();
            var markerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MapEditorInstaller.PalettePath + "/Marker_TestStart.prefab");
            if (markerPrefab != null)
            {
                var marker = (GameObject)PrefabUtility.InstantiatePrefab(markerPrefab, scene); marker.transform.SetParent(root.transform, false);
                test.testStart = marker.GetComponent<MapMarker>();
            }
            if (!EditorSceneManager.SaveScene(scene, folder + "/Work.unity")) throw new IOException("Failed to save map work scene.");
            Selection.activeGameObject = root;
            if (SceneView.lastActiveSceneView != null) { SceneView.lastActiveSceneView.in2DMode = true; SceneView.lastActiveSceneView.Focus(); SceneView.lastActiveSceneView.FrameSelected(); }
            return section;
        }
        public static bool ExportSection(MapSection section, bool showDialog)
        {
            if (section == null || EditorApplication.isPlayingOrWillChangePlaymode) return false;
            var scene = section.gameObject.scene; var scenePath = scene.path;
            string directory = Path.GetDirectoryName(scenePath)?.Replace('\\', '/');
            string expected = directory + "/Section.prefab";
            if (string.IsNullOrEmpty(scenePath) || !scenePath.StartsWith(SectionsPath + "/", StringComparison.Ordinal) || section.prefabPath != expected)
            {
                if (showDialog) EditorUtility.DisplayDialog("저장할 수 없음", "새 구간 만들기로 생성한 작업 씬에서 해당 구간을 저장하세요.", "확인");
                return false;
            }
            if (!MapValidation.Confirm(MapValidation.Section(section), "구간 저장", showDialog)) return false;
            if (!EditorSceneManager.SaveScene(scene)) return false;
            PrefabUtility.SaveAsPrefabAsset(section.gameObject, expected, out bool success);
            if (success) { AssetDatabase.SaveAssets(); EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(expected)); }
            if (showDialog) EditorUtility.DisplayDialog(success ? "저장 완료" : "저장 실패", success ? "구간 프리팹: " + expected : "Unity Console에서 오류를 확인하세요.", "확인");
            return success;
        }
    }
}
