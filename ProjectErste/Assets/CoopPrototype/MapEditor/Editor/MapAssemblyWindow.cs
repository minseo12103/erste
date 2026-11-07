using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShapeCoop.Editor
{
    public sealed class MapAssemblyWindow : EditorWindow
    {
        const string RootPath = "Assets/CoopMaps/Assemblies";
        const string SystemPath = "Assets/CoopPrototype/Prefabs/PrototypeSystem.prefab";
        string draftName = "Map";
        MapSection toAdd;
        Vector2 scroll;
        bool snap = true;
        float grid = .5f;

        [MenuItem("Tools/Shape Coop/Map Editor/Map Assembly")]
        public static void Open() { GetWindow<MapAssemblyWindow>("Map Assembly").Show(); }
        void OnEnable() { minSize = new Vector2(360, 440); SceneView.duringSceneGui += SceneGUI; }
        void OnDisable() { SceneView.duringSceneGui -= SceneGUI; }
        public static MapAssemblyDraft CurrentDraft()
        {
            var scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return null;
            foreach (var root in scene.GetRootGameObjects()) { var draft = root.GetComponent<MapAssemblyDraft>(); if (draft != null) return draft; }
            return null;
        }
        void OnGUI()
        {
            EditorGUILayout.LabelField("맵 구간 조합", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("구간 프리팹을 추가한 뒤 Scene에서 이동하세요. 구간 순서는 시작 마커 선택 순서이기도 합니다.", MessageType.Info);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                draftName = EditorGUILayout.TextField("새 조합 이름", draftName);
                if (GUILayout.Button("새 조합 작업 씬 만들기")) CreateDraft(draftName, true);
                var draft = CurrentDraft();
                if (draft == null) { EditorGUILayout.HelpBox("새 조합을 만들거나 기존 Assembly.unity를 여세요.", MessageType.Warning); return; }
                using (new EditorGUILayout.HorizontalScope())
                {
                    toAdd = (MapSection)EditorGUILayout.ObjectField("구간 프리팹", toAdd, typeof(MapSection), false);
                    if (GUILayout.Button("추가", GUILayout.Width(55)) && toAdd != null) AddSection(draft, toAdd, Vector2.zero);
                }
                EditorGUILayout.LabelField("Project의 구간 프리팹을 Scene에 끌어 놓을 수도 있습니다.", EditorStyles.wordWrappedMiniLabel);
                snap = EditorGUILayout.Toggle("끌어 놓기 격자", snap);
                if (snap) grid = Mathf.Max(.05f, EditorGUILayout.FloatField("격자 간격", grid));
                scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(100));
                for (int i = 0; i < draft.transform.childCount; i++)
                {
                    var child = draft.transform.GetChild(i);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button(child.name, EditorStyles.miniButton)) { Selection.activeGameObject = child.gameObject; SceneView.lastActiveSceneView?.FrameSelected(); }
                        if (GUILayout.Button("↑", GUILayout.Width(26)) && i > 0) { Undo.SetTransformParent(child, draft.transform, "Reorder sections"); child.SetSiblingIndex(i - 1); EditorSceneManager.MarkSceneDirty(draft.gameObject.scene); }
                        if (GUILayout.Button("↓", GUILayout.Width(26)) && i + 1 < draft.transform.childCount) { Undo.SetTransformParent(child, draft.transform, "Reorder sections"); child.SetSiblingIndex(i + 1); EditorSceneManager.MarkSceneDirty(draft.gameObject.scene); }
                        if (GUILayout.Button("×", GUILayout.Width(26))) { Undo.DestroyObjectImmediate(child.gameObject); GUIUtility.ExitGUI(); }
                    }
                }
                EditorGUILayout.EndScrollView();
                if (GUILayout.Button("선택 구간의 입구를 이전 구간 출구에 맞추기"))
                {
                    if (!AlignToPrevious(draft, Selection.activeTransform)) ShowNotification(new GUIContent("선택 구간의 입구와 이전 구간의 출구가 필요합니다"));
                }
                if (GUILayout.Button("조합 문제 검사"))
                {
                    var check = MapValidation.Assembly(draft);
                    EditorUtility.DisplayDialog("조합 문제 검사", check.errors.Count + check.warnings.Count == 0 ? "발견된 문제가 없습니다." : check.Message(), "확인");
                }
                if (GUILayout.Button("조합 씬 저장")) SaveDraft(draft, true);
                if (GUILayout.Button("현재 조합을 게임에 적용", GUILayout.Height(32))) ApplyToGame(draft, true);
                EditorGUILayout.HelpBox("게임 적용은 공통 PrototypeSystem 프리팹을 수정합니다. 팀에서 담당자를 정해 적용하세요.", MessageType.None);
            }
        }
        public static MapAssemblyDraft CreateDraft(string name, bool prompt)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || (prompt && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())) return null;
            Directory.CreateDirectory(RootPath); AssetDatabase.Refresh();
            string safe = ""; foreach (char c in (name ?? "").Trim()) if (char.IsLetterOrDigit(c) || c == '_' || c == '-') safe += c;
            if (string.IsNullOrEmpty(safe)) safe = "Map"; if (safe.Length > 40) safe = safe.Substring(0, 40);
            string folder = RootPath + "/" + safe + "_" + Guid.NewGuid().ToString("N");
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(RootPath, Path.GetFileName(folder)))) throw new IOException("Could not create assembly folder.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var draft = new GameObject("Map Assembly - " + safe).AddComponent<MapAssemblyDraft>();
            var camera = new GameObject("Main Camera", typeof(Camera)); camera.tag = "MainCamera"; camera.transform.position = new Vector3(0, 2, -10);
            var cam = camera.GetComponent<Camera>(); cam.orthographic = true; cam.orthographicSize = 8; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(.09f, .13f, .18f);
            if (!EditorSceneManager.SaveScene(scene, folder + "/Assembly.unity")) throw new IOException("Could not save assembly scene.");
            Selection.activeGameObject = draft.gameObject;
            if (SceneView.lastActiveSceneView != null) SceneView.lastActiveSceneView.in2DMode = true;
            return draft;
        }
        public static GameObject AddSection(MapAssemblyDraft draft, MapSection asset, Vector2 position)
        {
            if (draft == null || asset == null || !EditorUtility.IsPersistent(asset) || !PrefabUtility.IsPartOfPrefabAsset(asset)) return null;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset.gameObject, draft.gameObject.scene);
            Undo.RegisterCreatedObjectUndo(instance, "Add map section"); Undo.SetTransformParent(instance.transform, draft.transform, "Add map section");
            instance.transform.localPosition = position; instance.transform.localRotation = Quaternion.identity; instance.transform.localScale = Vector3.one;
            EditorSceneManager.MarkSceneDirty(draft.gameObject.scene); Selection.activeGameObject = instance; return instance;
        }
        public static bool AlignToPrevious(MapAssemblyDraft draft, Transform selected)
        {
            if (draft == null || selected == null) return false;
            while (selected.parent != null && selected.parent != draft.transform) selected = selected.parent;
            if (selected.parent != draft.transform || selected.GetSiblingIndex() == 0) return false;
            var previous = draft.transform.GetChild(selected.GetSiblingIndex() - 1);
            MapConnector entrance = null, exit = null;
            foreach (var connector in selected.GetComponentsInChildren<MapConnector>(true)) if (connector.kind == MapConnectorKind.Entrance) { entrance = connector; break; }
            foreach (var connector in previous.GetComponentsInChildren<MapConnector>(true)) if (connector.kind == MapConnectorKind.Exit) { exit = connector; break; }
            if (entrance == null || exit == null) return false;
            Undo.RecordObject(selected, "Align map sections"); selected.position += exit.transform.position - entrance.transform.position;
            EditorSceneManager.MarkSceneDirty(draft.gameObject.scene); return true;
        }
        public static bool SaveDraft(MapAssemblyDraft draft, bool showDialog)
        {
            if (draft == null || !MapValidation.Confirm(MapValidation.Assembly(draft), "조합 저장", showDialog)) return false;
            return EditorSceneManager.SaveScene(draft.gameObject.scene);
        }
        public static bool ApplyToGame(MapAssemblyDraft draft, bool showDialog) => ApplyToPrefab(draft, SystemPath, showDialog);
        public static bool ApplyToPrefab(MapAssemblyDraft draft, string systemPath, bool showDialog)
        {
            if (draft == null || !MapValidation.Confirm(MapValidation.Assembly(draft), "게임에 적용", showDialog)) return false;
            var stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.assetPath == systemPath) { if (showDialog) EditorUtility.DisplayDialog("게임에 적용", "게임 프리팹 편집을 먼저 종료하세요.", "확인"); return false; }
            if (!EditorSceneManager.SaveScene(draft.gameObject.scene)) return false;
            var placements = new List<MapSectionPlacement>();
            foreach (Transform child in draft.transform)
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(child.GetComponent<MapSection>());
                placements.Add(new MapSectionPlacement { prefab = source, position = child.localPosition });
            }
            var contents = PrefabUtility.LoadPrefabContents(systemPath);
            try
            {
                var game = contents.GetComponent<PrototypeGame>();
                if (game == null) { if (showDialog) EditorUtility.DisplayDialog("게임에 적용 실패", "PrototypeGame 컴포넌트를 찾을 수 없습니다.", "확인"); return false; }
                game.mapSections = placements.ToArray();
                PrefabUtility.SaveAsPrefabAsset(contents, systemPath, out bool success);
                if (success) AssetDatabase.SaveAssets();
                if (showDialog) EditorUtility.DisplayDialog(success ? "게임에 적용 완료" : "게임에 적용 실패", success ? "현재 조합을 게임에 등록했습니다." : "Unity Console을 확인하세요.", "확인");
                return success;
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }
        void SceneGUI(SceneView view)
        {
            var draft = CurrentDraft(); var e = Event.current;
            if (draft == null || EditorApplication.isPlaying || (e.type != EventType.DragUpdated && e.type != EventType.DragPerform)) return;
            if (DragAndDrop.objectReferences.Length != 1 || !(DragAndDrop.objectReferences[0] is GameObject prefab) || prefab.GetComponent<MapSection>() == null || !EditorUtility.IsPersistent(prefab)) return;
            var ray = HandleUtility.GUIPointToWorldRay(e.mousePosition); var plane = new Plane(Vector3.forward, Vector3.zero);
            if (!plane.Raycast(ray, out float distance)) return;
            var position = ray.GetPoint(distance); if (snap) { position.x = Mathf.Round(position.x / grid) * grid; position.y = Mathf.Round(position.y / grid) * grid; }
            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragPerform) { DragAndDrop.AcceptDrag(); AddSection(draft, prefab.GetComponent<MapSection>(), position); }
            e.Use();
        }
    }
}
