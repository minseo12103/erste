using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ShapeCoop.Editor
{
    public static class MapMoveSnapVerification
    {
        static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        static void Sync(MapEditorWindow window) => typeof(MapEditorWindow).GetMethod("SyncMoveSnapping", Private).Invoke(window, null);
        static void Set(MapEditorWindow window, string field, object value) => typeof(MapEditorWindow).GetField(field, Private).SetValue(window, value);
        public static void Run()
        {
            var oldScene = SceneManager.GetActiveScene(); var oldSelection = Selection.objects; var oldTool = Tools.current;
            var oldPivot = Tools.pivotRotation; var oldGrid = EditorSnapSettings.gridSize; var oldMove = EditorSnapSettings.move;
            bool oldGridEnabled = EditorSnapSettings.gridSnapEnabled, oldSnap = EditorSnapSettings.snapEnabled;
            Scene scene = default; MapEditorWindow window = null;
            try
            {
                if (!Application.isBatchMode) throw new InvalidOperationException("Run this verification only in an isolated batch-mode project.");
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var section = new GameObject("Section").AddComponent<MapSection>();
                var item = new GameObject("Placed object"); item.transform.SetParent(section.transform); item.transform.position = new Vector3(.13f, .17f, 0);
                var other = new GameObject("Outside section");
                Tools.current = Tool.Move; Tools.pivotRotation = PivotRotation.Local;
                EditorSnapSettings.gridSize = new Vector3(.8f, .9f, 1.1f); EditorSnapSettings.move = new Vector3(.7f, .6f, 1.2f);
                EditorSnapSettings.gridSnapEnabled = false; EditorSnapSettings.snapEnabled = false;
                var baselineGrid = EditorSnapSettings.gridSize; var baselineMove = EditorSnapSettings.move;
                window = ScriptableObject.CreateInstance<MapEditorWindow>(); Set(window, "snap", true); Set(window, "grid", .5f);
                Selection.activeGameObject = item; Sync(window);
                Check(EditorSnapSettings.gridSnapEnabled && EditorSnapSettings.snapEnabled && Tools.pivotRotation == PivotRotation.Global, "map move snap was not enabled in world coordinates");
                Check(EditorSnapSettings.gridSnapActive, "Unity move tool did not activate grid snapping");
                Check(Mathf.Approximately(EditorSnapSettings.gridSize.x, .5f) && Mathf.Approximately(EditorSnapSettings.gridSize.y, .5f) && Mathf.Approximately(EditorSnapSettings.move.x, .5f), "move snap does not use the palette grid");
                item.transform.position = new Vector3(-.72f, .74f, 0); Handles.SnapToGrid(new[] { item.transform });
                var snapped = item.transform.position; item.transform.position = new Vector3(.13f, .17f, 0);
                Check(Mathf.Abs(snapped.x + .5f) < .001f && Mathf.Abs(snapped.y - .5f) < .001f, "move and placement grids differ");
                Undo.IncrementCurrentGroup(); Undo.RecordObject(item.transform, "Move map object"); item.transform.position = snapped; Undo.FlushUndoRecordObjects(); Undo.PerformUndo();
                Check(Vector3.Distance(item.transform.position, new Vector3(.13f, .17f, 0)) < .001f, "undo did not restore moved object");
                Set(window, "grid", .25f); Sync(window); Check(Mathf.Approximately(EditorSnapSettings.gridSize.x, .25f), "changing grid spacing did not update move snapping");
                Tools.current = Tool.Rect; Sync(window);
                Check(!EditorSnapSettings.snapEnabled && !EditorSnapSettings.gridSnapEnabled, "2D Rect snapping must not change resize behavior");
                Set(window, "snap", false); Sync(window); Check(!EditorSnapSettings.snapEnabled && !EditorSnapSettings.gridSnapEnabled, "disabling map snap did not allow free movement");
                Set(window, "snap", true); Sync(window); Selection.activeGameObject = other; Sync(window);
                Check(!EditorSnapSettings.gridSnapEnabled && !EditorSnapSettings.snapEnabled && Tools.pivotRotation == PivotRotation.Local && Vector3.Distance(EditorSnapSettings.gridSize, baselineGrid) < .001f && Vector3.Distance(EditorSnapSettings.move, baselineMove) < .001f, "Unity snap settings were not restored outside the section");
                Selection.activeGameObject = item; Sync(window); Tools.current = Tool.Rotate; Sync(window);
                Check(!EditorSnapSettings.gridSnapEnabled && !EditorSnapSettings.snapEnabled, "map move snap leaked into the rotation tool");
                Debug.Log("SHAPE_MAP_MOVE_SNAP_OK");
            }
            catch (Exception error) { Debug.LogException(error); Cleanup(); EditorApplication.Exit(1); return; }
            Cleanup(); EditorApplication.Exit(0);
            void Cleanup()
            {
                if (window != null) Object.DestroyImmediate(window);
                if (scene.IsValid() && scene != SceneManager.GetActiveScene()) EditorSceneManager.CloseScene(scene, true);
                if (oldScene.IsValid() && oldScene.isLoaded) SceneManager.SetActiveScene(oldScene);
                Selection.objects = oldSelection; Tools.current = oldTool; Tools.pivotRotation = oldPivot;
                EditorSnapSettings.gridSize = oldGrid; EditorSnapSettings.move = oldMove;
                EditorSnapSettings.gridSnapEnabled = oldGridEnabled; EditorSnapSettings.snapEnabled = oldSnap;
            }
        }
    }
}
