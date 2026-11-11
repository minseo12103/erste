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
        enum SceneTool { None, Paint, PlayerStart, BallStart, Erase }
        SceneTool sceneTool;
        float placementRotation;
        bool snap;
        float grid;
        float brushSpacing;
        bool paintBasicPrefab;
        MapBrushStroke brushStroke;
        MapSection brushSection;
        SceneView brushView;
        int brushControl;
        static MapEditorWindow snapSettingsOwner;
        bool ownsMoveSnap;
        bool previousGridSnap, previousSnap;
        Vector3 previousGridSize, previousMoveSnap;
        PivotRotation previousPivotRotation;
        Transform[] rectDragTargets;
        Transform rectDragAnchor;
        bool rectDragging, rectResizing, rectSnapPending, applyingRectSnap;
        int rectUndoGroup;
        Transform directMoveTarget;
        SceneView directMoveView;
        Transform directMoveHover;
        SceneView directMoveHoverView;
        Vector2 directMoveHoverMouse;
        Vector3 directMoveStartPosition, directMoveStartPointer;
        Vector3 directMovePaintPosition;
        Vector2 directMoveStartMouse;
        int directMoveControl, directMoveUndoGroup;
        bool directMoveStarted, directMoveDragged;
        GameObject directMovePaintPrefab;

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
            brushSpacing = MapBrushStroke.ValidSpacing(EditorPrefs.GetFloat("ShapeCoop.MapBrushSpacing", 1f));
            SceneView.duringSceneGui += HandleSceneDrag;
            EditorApplication.projectChanged += OnProjectChanged;
            EditorApplication.update += SyncMoveSnapping;
            AssemblyReloadEvents.beforeAssemblyReload += RestoreMoveSnapping;
            Undo.postprocessModifications += PostprocessRectMove;
            Undo.undoRedoPerformed += Repaint;
            ReloadSections();
        }
        void OnDisable()
        {
            SceneView.duringSceneGui -= HandleSceneDrag;
            EditorApplication.projectChanged -= OnProjectChanged;
            EditorApplication.update -= SyncMoveSnapping;
            AssemblyReloadEvents.beforeAssemblyReload -= RestoreMoveSnapping;
            Undo.postprocessModifications -= PostprocessRectMove;
            Undo.undoRedoPerformed -= Repaint;
            EndBasicBrush();
            EndDirectMove(false);
            CancelRectDrag();
            RestoreMoveSnapping();
        }
        void OnSelectionChange() { Repaint(); }
        void OnInspectorUpdate()
        {
            var selected = Selection.activeTransform;
            if (selected != null && selected.GetComponentInParent<MapGimmick>() != null) Repaint();
        }
        void SyncMoveSnapping()
        {
            var section = CurrentSection();
            if (brushStroke != null && (EditorApplication.isPlayingOrWillChangePlaymode || brushSection == null || brushSection != section || brushView == null)) EndBasicBrush();
            if (directMoveControl != 0 && (EditorApplication.isPlayingOrWillChangePlaymode || section == null || directMoveTarget == null || !directMoveTarget.IsChildOf(section.transform))) EndDirectMove(false);
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
        void SetSceneTool(SceneTool tool)
        {
            if (sceneTool == tool) return;
            EndBasicBrush(); EndDirectMove(false); CancelRectDrag(); sceneTool = tool; previewPrefab = null;
            SyncMoveSnapping(); Repaint(); SceneView.RepaintAll();
        }
        void OnProjectChanged() { ReloadSections(); Repaint(); }
        void ReloadSections() { sections.Clear(); sections.AddRange(MapSectionBrowser.FindSections()); }
        public static void SetPaletteSelection(GameObject prefab) => SetPaletteSelection(prefab, true);

        internal static void SetPaletteSelection(GameObject prefab, bool activateBrush, bool activatePlacement = false)
        {
            var windows = Resources.FindObjectsOfTypeAll<MapEditorWindow>();
            if (windows.Length == 0) { Open(); windows = Resources.FindObjectsOfTypeAll<MapEditorWindow>(); }
            bool basic = IsBasicBrushPrefab(prefab);
            foreach (var window in windows)
            {
                window.EndBasicBrush();
                window.paintPrefab = prefab;
                window.paintBasicPrefab = basic;
                window.previewPrefab = null;
                if (activatePlacement || (basic && activateBrush)) window.SetSceneTool(SceneTool.Paint);
                window.Repaint();
            }
            SceneView.RepaintAll();
        }
        internal static bool IsBasicBrushPrefab(GameObject prefab)
        {
            return prefab != null && IsBasicPalettePath(AssetDatabase.GetAssetPath(prefab));
        }
        internal static bool IsBasicPalettePath(string path) =>
            !string.IsNullOrEmpty(path) && path.StartsWith(MapEditorInstaller.PalettePath + "/기본 오브젝트/", StringComparison.Ordinal);
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
                var selection = Selection.activeTransform;
                var selectedObject = DirectMoveTarget(section, Selection.activeGameObject);
                if (selectedObject != null)
                {
                    float angle = Mathf.DeltaAngle(0, selectedObject.localEulerAngles.z);
                    float next = DrawRotationField("선택 오브젝트 회전 (°)", angle);
                    if (next != angle) SetObjectRotation(selectedObject, next);
                }
                var gimmick = selection != null ? selection.GetComponentInParent<MapGimmick>() : null;
                if (gimmick != null && gimmick.transform.IsChildOf(section.transform))
                {
                    EditorGUILayout.Space(); GimmickSettingsGUI.Draw(gimmick); EditorGUILayout.Space();
                }
                if (GUILayout.Button("현재 구간을 프리팹으로 저장", GUILayout.Height(28))) ExportSection(section, true);
                EditorGUILayout.LabelField("저장 위치", section.prefabPath, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.Space(); EditorGUILayout.LabelField("구간 테스트", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck();
                var shape = (MapTestShape)EditorGUILayout.EnumPopup("도형", test.shape);
                bool near = EditorGUILayout.Toggle("테스트 위치에 공 생성", test.spawnBallNearTestStart);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(test, "Change map test settings"); test.shape = shape; test.spawnBallNearTestStart = near;
                    EditorUtility.SetDirty(test); EditorSceneManager.MarkSceneDirty(test.gameObject.scene);
                }
                EditorGUI.BeginChangeCheck();
                int mode = GUILayout.Toolbar((int)test.startMode, new[] { "자동: 플레이어 시작 우선", "별도 테스트 시작" });
                if (EditorGUI.EndChangeCheck())
                { Undo.RecordObject(test, "Change test start mode"); test.startMode = (MapTestStartMode)mode; EditorUtility.SetDirty(test); EditorSceneManager.MarkSceneDirty(test.gameObject.scene); }
                EditorGUI.BeginChangeCheck();
                var marker = (MapMarker)EditorGUILayout.ObjectField("별도 테스트 마커", test.testStart, typeof(MapMarker), true);
                if (EditorGUI.EndChangeCheck())
                { Undo.RecordObject(test, "Set test start"); test.testStart = marker; test.startMode = MapTestStartMode.SelectedMarker; EditorUtility.SetDirty(test); EditorSceneManager.MarkSceneDirty(test.gameObject.scene); }
                var resolvedStart = test.ResolveStart();
                using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField("실제 테스트 시작", resolvedStart, typeof(MapMarker), true);
                if (resolvedStart != null) EditorGUILayout.LabelField("시작 좌표", ((Vector2)resolvedStart.transform.position).ToString("F2"));
                else EditorGUILayout.HelpBox("현재 구간의 시작 마커를 지정하세요.", MessageType.Warning);
                EditorGUILayout.HelpBox("자동 모드는 플레이어 시작이 있으면 그 위치를 사용합니다. 원하는 구간을 따로 시험하려면 별도 테스트 시작을 선택하세요. 실제 게임에는 구간 프리팹을 저장하고 맵 조합을 게임에 적용해야 합니다.", MessageType.None);
                if (GUILayout.Button("선택한 마커를 테스트 시작으로"))
                {
                    var selected = Selection.activeGameObject == null ? null : Selection.activeGameObject.GetComponent<MapMarker>();
                    if (selected != null && selected.transform.IsChildOf(section.transform))
                    { Undo.RecordObject(test, "Set test start"); test.testStart = selected; test.startMode = MapTestStartMode.SelectedMarker; EditorUtility.SetDirty(test); EditorSceneManager.MarkSceneDirty(test.gameObject.scene); }
                    else ShowNotification(new GUIContent("이 구간의 시작 마커를 선택하세요"));
                }
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("테스트 위치 클릭 지정")) SetSceneTool(SceneTool.PlayerStart);
                    if (GUILayout.Button("공 위치 클릭 지정")) SetSceneTool(SceneTool.BallStart);
                }
                if (!test.spawnBallNearTestStart && test.useCustomBallStart) EditorGUILayout.LabelField("테스트 공 시작", test.customBallStart.ToString("F2"));
                if (GUILayout.Button("선택 도형으로 테스트 실행", GUILayout.Height(28)))
                {
                    if (test.ResolveStart() == null) EditorUtility.DisplayDialog("테스트 시작 지점", "현재 구간의 시작 마커를 지정하세요.", "확인");
                    else if (EditorSceneManager.SaveScene(section.gameObject.scene)) EditorApplication.EnterPlaymode();
                }
                EditorGUILayout.Space(); EditorGUILayout.LabelField("배치 팔레트", EditorStyles.boldLabel);
                EditorGUI.BeginChangeCheck(); snap = EditorGUILayout.Toggle("격자 맞춤", snap);
                if (snap) grid = Mathf.Max(.05f, EditorGUILayout.FloatField("격자 간격", grid));
                if (EditorGUI.EndChangeCheck()) { EditorPrefs.SetBool("ShapeCoop.MapSnap", snap); EditorPrefs.SetFloat("ShapeCoop.MapGrid", grid); SyncMoveSnapping(); }
                EditorGUILayout.LabelField("배치된 오브젝트를 클릭하여 끌면 같은 격자 기준으로 이동합니다.", EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool painting = GUILayout.Toggle(sceneTool == SceneTool.Paint, "연속 배치", "Button");
                    if (painting && sceneTool != SceneTool.Paint) SetSceneTool(SceneTool.Paint);
                    else if (!painting && sceneTool == SceneTool.Paint) SetSceneTool(SceneTool.None);
                    bool erasing = GUILayout.Toggle(sceneTool == SceneTool.Erase, "지우개", "Button");
                    if (erasing && sceneTool != SceneTool.Erase) SetSceneTool(SceneTool.Erase);
                    else if (!erasing && sceneTool == SceneTool.Erase) SetSceneTool(SceneTool.None);
                }
                SetPlacementRotation(DrawRotationField("배치 회전 (°)", placementRotation));
                if (paintPrefab != null) EditorGUILayout.LabelField("선택된 오브젝트", paintPrefab.name);
                if (sceneTool == SceneTool.Erase || (paintBasicPrefab && paintPrefab != null))
                {
                    EditorGUI.BeginChangeCheck();
                    brushSpacing = MapBrushStroke.ValidSpacing(EditorGUILayout.DelayedFloatField("브러시 간격", brushSpacing));
                    if (EditorGUI.EndChangeCheck())
                    {
                        EditorPrefs.SetFloat("ShapeCoop.MapBrushSpacing", brushSpacing);
                        previewPrefab = null;
                        SceneView.RepaintAll();
                    }
                }
                if (sceneTool == SceneTool.Paint)
                {
                    string hint = paintBasicPrefab
                        ? "Scene의 빈 곳을 누르고 끌어 배치하세요. 이미 놓인 오브젝트를 잡고 끌면 이동합니다."
                        : "팔레트에서 프리팹을 선택하고 Scene을 클릭하여 반복 배치하거나, 배치된 오브젝트를 끌어 이동하세요.";
                    EditorGUILayout.HelpBox(hint + " [ / ]로 15도, Shift와 함께 누르면 90도 회전합니다. 우클릭 또는 Esc로 배치를 종료합니다.", MessageType.Info);
                }
                else if (sceneTool == SceneTool.Erase)
                    EditorGUILayout.HelpBox("Scene에서 끌어서 기본 오브젝트를 지웁니다. Ctrl+Z로 한 번의 드래그를 되돌리고, Esc로 지우개를 종료합니다.", MessageType.Info);
                else if (sceneTool != SceneTool.None) EditorGUILayout.HelpBox("Scene을 클릭해 테스트 시작점을 지정하세요. Esc로 취소합니다.", MessageType.Info);
            }
        }

        static float DrawRotationField(string label, float angle)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                float next = EditorGUILayout.DelayedFloatField(label, angle);
                if (GUILayout.Button("-90°", GUILayout.Width(46))) next -= 90f;
                if (GUILayout.Button("+90°", GUILayout.Width(46))) next += 90f;
                return next;
            }
        }

        void SetPlacementRotation(float angle)
        {
            if (!float.IsFinite(angle)) return;
            float next = Mathf.DeltaAngle(0, angle);
            if (Mathf.Approximately(next, placementRotation)) return;
            placementRotation = next;
            Repaint();
            SceneView.RepaintAll();
        }

        internal static void SetObjectRotation(Transform target, float angle)
        {
            if (target == null || !float.IsFinite(angle)) return;
            var euler = target.localEulerAngles;
            if (Mathf.Abs(Mathf.DeltaAngle(euler.z, angle)) < .0001f) return;
            euler.z = Mathf.DeltaAngle(0, angle);
            Undo.RecordObject(target, "Rotate map object");
            target.localRotation = Quaternion.Euler(euler);
            if (PrefabUtility.IsPartOfPrefabInstance(target))
                PrefabUtility.RecordPrefabInstancePropertyModifications(target);
            EditorSceneManager.MarkSceneDirty(target.gameObject.scene);
            SceneView.RepaintAll();
        }

        bool HandleRotationShortcut(MapSection section, Event e)
        {
            if (e.type != EventType.KeyDown || e.alt || e.control || e.command || GUIUtility.hotControl != 0)
                return false;
            bool leftBrace = e.keyCode == KeyCode.LeftCurlyBracket;
            bool rightBrace = e.keyCode == KeyCode.RightCurlyBracket;
            bool left = e.keyCode == KeyCode.LeftBracket || leftBrace;
            bool right = e.keyCode == KeyCode.RightBracket || rightBrace;
            if (!left && !right) return false;

            float step = e.shift || leftBrace || rightBrace ? 90f : 15f;
            float delta = left ? -step : step;
            if (sceneTool == SceneTool.Paint && paintPrefab != null)
                SetPlacementRotation(placementRotation + delta);
            else if (sceneTool == SceneTool.None)
            {
                var target = DirectMoveTarget(section, Selection.activeGameObject);
                if (target == null) return false;
                SetObjectRotation(target, target.localEulerAngles.z + delta);
                Repaint();
            }
            else return false;

            e.Use();
            return true;
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
            sceneView.wantsMouseMove = true;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && directMoveControl != 0) { EndDirectMove(true); e.Use(); sceneView.Repaint(); return; }
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape && sceneTool != SceneTool.None) { SetSceneTool(SceneTool.None); e.Use(); return; }
            if (sceneTool == SceneTool.Paint && e.type == EventType.MouseDown && e.button == 1 && !e.alt)
            {
                SetSceneTool(SceneTool.None);
                e.Use();
                return;
            }
            if (MapPaletteWindow.HandleFavoriteShortcut(e)) return;
            if (HandleRotationShortcut(section, e)) return;
            bool prefabDragEvent = e.type == EventType.DragUpdated || e.type == EventType.DragPerform || e.type == EventType.DragExited;
            bool canMove = sceneTool == SceneTool.None || sceneTool == SceneTool.Paint;
            bool erasing = sceneTool == SceneTool.Erase;
            int control = (canMove || erasing) && !prefabDragEvent ? GUIUtility.GetControlID("ShapeCoop.MapDirectMove".GetHashCode(), FocusType.Passive) : 0;
            if (e.type == EventType.Layout && GimmickSettingsGUI.HasNearestRangeHandle(sceneView)) return;
            if (erasing && e.type == EventType.Repaint)
            {
                if (previewPrefab != null) DrawPreview(previewPrefab, previewPosition);
                else DrawErasePreview(sceneView, section, e);
                return;
            }
            if (brushStroke != null)
            {
                ContinueBasicBrush(sceneView, section, e);
                return;
            }
            if (erasing && !prefabDragEvent)
            {
                if (e.type == EventType.Layout && !e.alt && !e.shift && !e.control && !e.command && !Tools.viewToolActive)
                    HandleUtility.AddControl(control, 0);
                else if (e.type == EventType.MouseMove) sceneView.Repaint();
                else if (e.type == EventType.MouseDown) StartBrush(sceneView, section, e, control, true);
                return;
            }
            bool basicPainting = sceneTool == SceneTool.Paint && paintBasicPrefab && paintPrefab != null;
            if (canMove && !prefabDragEvent)
            {
                HandleDirectMove(sceneView, section, e, control, basicPainting);
                if (e.type == EventType.Used) return;
                if (basicPainting && (e.type == EventType.MouseDown || e.type == EventType.MouseMove))
                {
                    HandleBasicBrush(sceneView, section, e, control);
                    return;
                }
            }
            if (e.rawType == EventType.MouseDown && e.button == 0 && !e.alt && Tools.current == Tool.Rect) BeginRectDrag(section);
            if (e.rawType == EventType.MouseUp && e.button == 0 && rectDragging) { EditorApplication.delayCall -= EndRectDrag; EditorApplication.delayCall += EndRectDrag; }
            if (e.type == EventType.DragExited) { previewPrefab = null; sceneView.Repaint(); return; }
            bool dragging = (e.type == EventType.DragUpdated || e.type == EventType.DragPerform) && DragAndDrop.objectReferences.Length == 1 && DragAndDrop.objectReferences[0] is GameObject;
            GameObject asset = dragging ? DragAndDrop.objectReferences[0] as GameObject : sceneTool == SceneTool.Paint ? paintPrefab : null;
            if (dragging && !AssetDatabase.GetAssetPath(asset).StartsWith(MapEditorInstaller.PalettePath + "/", StringComparison.Ordinal)) return;
            if (e.type == EventType.Repaint && previewPrefab != null && directMoveControl == 0) DrawPreview(previewPrefab, previewPosition);
            if (basicPainting && !dragging) return;
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
                if (asset != null)
                {
                    var placed = PlacePrefab(section, asset, position, placementRotation);
                    if (placed != null && (sceneTool == SceneTool.None || sceneTool == SceneTool.Paint))
                    { directMoveHover = placed.transform; directMoveHoverView = sceneView; directMoveHoverMouse = e.mousePosition; }
                }
                else if (sceneTool == SceneTool.PlayerStart || sceneTool == SceneTool.BallStart)
                {
                    var test = CurrentTest();
                    if (test != null && sceneTool == SceneTool.PlayerStart)
                    {
                        Undo.RecordObject(test, "Use custom test start"); test.startMode = MapTestStartMode.SelectedMarker; EditorUtility.SetDirty(test);
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
                    EditorSceneManager.MarkSceneDirty(section.gameObject.scene); SetSceneTool(SceneTool.None);
                }
                if (dragging) previewPrefab = null;
                e.Use();
            }
            else if (dragging) e.Use();
        }
        void HandleBasicBrush(SceneView sceneView, MapSection section, Event e, int control)
        {
            if (e.type == EventType.MouseMove)
            {
                GameObject nextPreview = null;
                Vector3 nextPosition = previewPosition;
                if (!e.alt && !Tools.viewToolActive && directMoveControl == 0 && directMoveHover == null && TryMapPlanePoint(e.mousePosition, 0, out var hoverPoint))
                {
                    nextPreview = paintPrefab;
                    nextPosition = MapBrushStroke.SnapToCell(section.transform.position, brushSpacing, hoverPoint);
                }
                if (previewPrefab != nextPreview || previewPosition != nextPosition)
                {
                    previewPrefab = nextPreview;
                    previewPosition = nextPosition;
                    sceneView.Repaint();
                }
                return;
            }

            StartBrush(sceneView, section, e, control, false);
        }

        void DrawErasePreview(SceneView sceneView, MapSection section, Event e)
        {
            if (e.alt || Tools.viewToolActive || !new Rect(Vector2.zero, sceneView.position.size).Contains(e.mousePosition) ||
                !TryMapPlanePoint(e.mousePosition, 0, out var point)) return;
            var center = MapBrushStroke.SnapToCell(section.transform.position, brushSpacing, point);
            using (new Handles.DrawingScope(new Color(1f, .3f, .3f)))
                Handles.DrawWireCube(center, new Vector3(brushSpacing, brushSpacing, 0));
        }

        void StartBrush(SceneView sceneView, MapSection section, Event e, int control, bool erasing)
        {
            if (e.button != 0 || e.alt || e.shift || e.control || e.command || Tools.viewToolActive || directMoveControl != 0 ||
                HandleUtility.nearestControl != control || GUIUtility.hotControl != 0 || !TryMapPlanePoint(e.mousePosition, 0, out var point)) return;

            brushStroke = erasing ? new MapBrushStroke(section, brushSpacing) : new MapBrushStroke(section, paintPrefab, brushSpacing, placementRotation);
            brushSection = section;
            brushView = sceneView;
            brushControl = control;
            GUIUtility.hotControl = control;
            previewPrefab = null;
            brushStroke.AddPoint(point);
            e.Use();
            sceneView.Repaint();
        }
        void ContinueBasicBrush(SceneView sceneView, MapSection section, Event e)
        {
            if (sceneView != brushView) return;
            if (GUIUtility.hotControl != brushControl || section != brushSection)
            {
                EndBasicBrush();
                return;
            }

            bool released = e.rawType == EventType.MouseUp && e.button == 0;
            if (!released && (e.type != EventType.MouseDrag || e.button != 0)) return;
            var viewRect = new Rect(Vector2.zero, sceneView.position.size);
            if (viewRect.Contains(e.mousePosition) && TryMapPlanePoint(e.mousePosition, 0, out var point))
            {
                brushStroke.AddPoint(point);
                if (brushStroke.SkippedLongSegment)
                {
                    sceneView.ShowNotification(new GUIContent("브러시가 한 번에 이동한 거리가 너무 깁니다. 확대하거나 짧게 끌어 주세요."));
                    released = true;
                }
            }
            if (released) EndBasicBrush();
            e.Use();
            sceneView.Repaint();
        }
        void EndBasicBrush()
        {
            if (brushStroke == null) return;
            brushStroke.Finish();
            if (brushStroke.LastPlaced != null) Selection.activeGameObject = brushStroke.LastPlaced;
            if (GUIUtility.hotControl == brushControl) GUIUtility.hotControl = 0;
            brushStroke = null;
            brushSection = null;
            brushView = null;
            brushControl = 0;
        }
        static Transform DirectMoveTarget(MapSection section, GameObject picked)
        {
            if (section == null || picked == null || picked.scene != section.gameObject.scene) return null;
            var target = picked.transform;
            while (target.parent != null && target.parent != section.transform) target = target.parent;
            return target.parent == section.transform ? target : null;
        }
        static bool TryMapPlanePoint(Vector2 mouse, float depth, out Vector3 point)
        {
            var ray = HandleUtility.GUIPointToWorldRay(mouse);
            var plane = new Plane(Vector3.forward, new Vector3(0, 0, depth));
            if (plane.Raycast(ray, out float distance)) { point = ray.GetPoint(distance); return true; }
            point = default; return false;
        }
        static Vector3 DirectMovePosition(Vector3 start, Vector3 startPointer, Vector3 pointer, bool snapToGrid, float gridSize)
        {
            var position = start + pointer - startPointer;
            if (snapToGrid)
            {
                float size = Mathf.Max(.05f, gridSize);
                position.x = Mathf.Round(position.x / size) * size; position.y = Mathf.Round(position.y / size) * size;
            }
            position.z = start.z; return position;
        }
        void HandleDirectMove(SceneView sceneView, MapSection section, Event e, int control, bool basicPainting)
        {
            if (directMoveControl != 0 && directMoveView != sceneView) return;
            if (e.type == EventType.MouseMove && directMoveControl == 0)
            {
                var hovered = DirectMoveTarget(section, HandleUtility.PickGameObject(e.mousePosition, false));
                if (hovered != directMoveHover || directMoveHoverView != sceneView) sceneView.Repaint();
                directMoveHover = hovered; directMoveHoverView = sceneView; directMoveHoverMouse = e.mousePosition;
            }
            if (e.type == EventType.Layout && !e.alt && !e.shift && !e.control && !e.command && !Tools.viewToolActive)
            {
                if (directMoveHoverView == sceneView && directMoveHover != null && directMoveHover.IsChildOf(section.transform) && (e.mousePosition - directMoveHoverMouse).sqrMagnitude < 4)
                {
                    if (Tools.current == Tool.Move || Tools.current == Tool.Rect) HandleUtility.AddControl(control, 0);
                    else HandleUtility.AddDefaultControl(control);
                }
                else if (basicPainting) HandleUtility.AddDefaultControl(control);
            }
            if (directMoveControl != 0)
            {
                if (directMoveTarget == null || GUIUtility.hotControl != directMoveControl) { EndDirectMove(false); return; }
                if (e.type == EventType.MouseDrag && e.button == 0)
                {
                    if (Vector2.Distance(e.mousePosition, directMoveStartMouse) >= 3)
                    {
                        directMoveDragged = true;
                        if (TryMapPlanePoint(e.mousePosition, directMoveStartPosition.z, out var pointer)) { ApplyDirectMove(pointer); sceneView.Repaint(); }
                    }
                    e.Use();
                }
                else if (e.rawType == EventType.MouseUp && e.button == 0)
                {
                    var prefab = !directMoveDragged && sceneTool == SceneTool.Paint ? directMovePaintPrefab : null;
                    var position = directMovePaintPosition; var releasedTarget = directMoveTarget;
                    EndDirectMove(false);
                    if (prefab != null)
                    {
                        var placed = PlacePrefab(section, prefab, position, placementRotation);
                        if (placed != null) releasedTarget = placed.transform;
                    }
                    directMoveHover = releasedTarget; directMoveHoverView = sceneView; directMoveHoverMouse = e.mousePosition;
                    e.Use(); sceneView.Repaint();
                }
                return;
            }
            if (e.type != EventType.MouseDown || e.button != 0 || e.alt || e.shift || e.control || e.command || Tools.viewToolActive || HandleUtility.nearestControl != control || GUIUtility.hotControl != 0) return;
            var target = DirectMoveTarget(section, HandleUtility.PickGameObject(e.mousePosition, false));
            if (target == null || !TryMapPlanePoint(e.mousePosition, target.position.z, out var startPointer)) return;
            directMovePaintPrefab = null;
            if (sceneTool == SceneTool.Paint && !basicPainting && paintPrefab != null && TryMapPlanePoint(e.mousePosition, 0, out var paintPosition))
            {
                if (snap) { paintPosition.x = Mathf.Round(paintPosition.x / grid) * grid; paintPosition.y = Mathf.Round(paintPosition.y / grid) * grid; }
                paintPosition.z = 0; directMovePaintPosition = paintPosition; directMovePaintPrefab = paintPrefab;
            }
            CancelRectDrag();
            directMoveTarget = target; directMoveView = sceneView; directMoveStartPosition = target.position;
            directMoveStartPointer = startPointer; directMoveStartMouse = e.mousePosition; directMoveControl = control; directMoveStarted = directMoveDragged = false;
            Selection.activeGameObject = target.gameObject; GUIUtility.hotControl = control; e.Use(); sceneView.Repaint();
        }
        void ApplyDirectMove(Vector3 pointer)
        {
            if (directMoveTarget == null) return;
            var position = DirectMovePosition(directMoveStartPosition, directMoveStartPointer, pointer, snap, grid);
            if (position == directMoveTarget.position) return;
            if (!directMoveStarted)
            {
                Undo.IncrementCurrentGroup(); directMoveUndoGroup = Undo.GetCurrentGroup();
                Undo.SetCurrentGroupName("Move map object"); Undo.RegisterCompleteObjectUndo(directMoveTarget, "Move map object"); directMoveStarted = true;
            }
            directMoveTarget.position = position;
            if (PrefabUtility.IsPartOfPrefabInstance(directMoveTarget)) PrefabUtility.RecordPrefabInstancePropertyModifications(directMoveTarget);
            EditorSceneManager.MarkSceneDirty(directMoveTarget.gameObject.scene);
        }
        void EndDirectMove(bool cancel)
        {
            if (directMoveControl == 0) return;
            if (directMoveStarted && directMoveTarget != null)
            {
                if (cancel && Undo.GetCurrentGroup() == directMoveUndoGroup) Undo.RevertAllDownToGroup(directMoveUndoGroup);
                else if (cancel)
                {
                    Undo.RecordObject(directMoveTarget, "Cancel map object move"); directMoveTarget.position = directMoveStartPosition;
                    if (PrefabUtility.IsPartOfPrefabInstance(directMoveTarget)) PrefabUtility.RecordPrefabInstancePropertyModifications(directMoveTarget);
                }
                else if (Undo.GetCurrentGroup() == directMoveUndoGroup) Undo.CollapseUndoOperations(directMoveUndoGroup);
            }
            if (GUIUtility.hotControl == directMoveControl) GUIUtility.hotControl = 0;
            directMoveTarget = null; directMoveView = null; directMovePaintPrefab = null; directMoveControl = 0; directMoveStarted = directMoveDragged = false;
        }
        public static GameObject PlacePrefab(MapSection section, GameObject asset, Vector3 position, float rotation)
        {
            var instance = InstantiatePlacedPrefab(section, asset, position, rotation, "Place map object");
            if (instance == null) return null;
            Selection.activeGameObject = instance;
            EditorSceneManager.MarkSceneDirty(section.gameObject.scene);
            return instance;
        }
        internal static GameObject InstantiatePlacedPrefab(MapSection section, GameObject asset, Vector3 position, float rotation, string undoName)
        {
            if (section == null || asset == null) return null;
            var instance = PrefabUtility.InstantiatePrefab(asset, section.gameObject.scene) as GameObject;
            if (instance == null) return null;
            instance.transform.SetParent(section.transform, true);
            instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0, 0, rotation) * asset.transform.rotation);
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            Undo.RegisterCreatedObjectUndo(instance, undoName);
            return instance;
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
            bool success = SaveGameSection(section, expected, out int baked);
            if (success) { AssetDatabase.SaveAssets(); EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(expected)); }
            if (showDialog)
            {
                string message = success ? "구간 프리팹: " + expected : "Unity Console에서 오류를 확인하세요.";
                if (success && baked > 0) message += $"\n기본 오브젝트 {baked}개를 타일맵으로 묶었습니다.";
                EditorUtility.DisplayDialog(success ? "저장 완료" : "저장 실패", message, "확인");
            }
            return success;
        }

        static bool SaveGameSection(MapSection section, string path, out int baked)
        {
            baked = 0;
            string stagingPath = null;
            GameObject stagedRoot = null;
            try
            {
                if (MapTerrainBake.HasCandidates(section))
                {
                    string directory = Path.GetDirectoryName(path).Replace('\\', '/');
                    stagingPath = AssetDatabase.GenerateUniqueAssetPath(directory + "/__SectionExport.prefab");
                    // Saving and loading a staging prefab retains the nested prefab links and overrides.
                    PrefabUtility.SaveAsPrefabAsset(section.gameObject, stagingPath, out bool staged);
                    if (!staged) return false;

                    stagedRoot = PrefabUtility.LoadPrefabContents(stagingPath);
                    baked = MapTerrainBake.Bake(section, stagedRoot.GetComponent<MapSection>());
                    if (baked > 0)
                    {
                        PrefabUtility.SaveAsPrefabAsset(stagedRoot, path, out bool optimized);
                        return optimized;
                    }
                }

                PrefabUtility.SaveAsPrefabAsset(section.gameObject, path, out bool saved);
                return saved;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, section);
                return false;
            }
            finally
            {
                if (stagedRoot != null) PrefabUtility.UnloadPrefabContents(stagedRoot);
                if (stagingPath != null) AssetDatabase.DeleteAsset(stagingPath);
            }
        }
    }
}
