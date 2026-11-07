using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ShapeCoop.Editor
{
    // Batch smoke test. Temporary sections have unique IDs and are deleted afterwards.
    [InitializeOnLoad]
    public static class MapEditorVerification
    {
        const string Prefix = "ShapeCoop.MapCheck.";
        static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static MapEditorVerification()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.update += OnUpdate;
        }
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        static void Set(string key, string value) => SessionState.SetString(Prefix + key, value);
        static string Get(string key) => SessionState.GetString(Prefix + key, "");
        static void Int(string key, int value) => SessionState.SetInt(Prefix + key, value);
        static int Int(string key) => SessionState.GetInt(Prefix + key, 0);
        static T Field<T>(object instance, string name) => (T)instance.GetType().GetField(name, Private).GetValue(instance);
        static GameObject Place(MapSection parent, string name, Vector2 pos, Vector2 scale)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(MapEditorInstaller.PalettePath + "/" + name + ".prefab");
            Check(asset != null, "missing palette prefab " + name);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(asset, parent.gameObject.scene);
            instance.transform.SetParent(parent.transform, false); instance.transform.localPosition = pos; instance.transform.localScale = scale;
            return instance;
        }
        static void VerifyPaletteDiscovery()
        {
            string folderName = "__PaletteValidation_" + Guid.NewGuid().ToString("N");
            string folder = MapEditorInstaller.PalettePath + "/" + folderName;
            Check(!string.IsNullOrEmpty(AssetDatabase.CreateFolder(MapEditorInstaller.PalettePath, folderName)), "could not create palette verification folder");
            try
            {
                var source = new GameObject("CustomObject");
                string path = folder + "/CustomObject.prefab";
                PrefabUtility.SaveAsPrefabAsset(source, path); Object.DestroyImmediate(source);
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var items = MapEditorWindow.FindPaletteItems();
                var custom = items.Find(item => item.path == path);
                Check(custom != null && custom.prefab != null && custom.category == folderName && custom.label.Contains("CustomObject"), "custom prefab discovery or category failed");
                Check(items.Exists(item => item.path == MapEditorInstaller.PalettePath + "/Terrain_Box.prefab" && item.category == "지형"), "existing palette category missing");
                AssetDatabase.SetLabels(custom.prefab, new[] { "unrelated-existing-label" });
                MapPaletteTags.SetTags(custom.prefab, new[] { "기능 오브젝트", "장식", "장식" });
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                var reloaded = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var savedTags = MapPaletteTags.GetTags(reloaded);
                Check(savedTags.Length == 2 && Array.IndexOf(savedTags, "기능 오브젝트") >= 0 && Array.IndexOf(savedTags, "장식") >= 0, "prefab tags did not persist");
                Check(Array.IndexOf(AssetDatabase.GetLabels(reloaded), "unrelated-existing-label") >= 0, "unrelated labels were overwritten");
                MapPaletteTags.SetTags(reloaded, Array.Empty<string>());
                Check(MapPaletteTags.GetTags(reloaded).Length == 0, "explicitly cleared tags should remain empty");
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(custom.prefab);
                Check(instance != null && PrefabUtility.GetCorrespondingObjectFromSource(instance) == custom.prefab, "discovered prefab cannot be instantiated");
                Object.DestroyImmediate(instance);
                Debug.Log("SHAPE_MAP_PALETTE_DISCOVERY_OK");
            }
            finally { AssetDatabase.DeleteAsset(folder); }
        }
        public static void Run()
        {
            try
            {
                MapEditorInstaller.EnsureAssets();
                VerifyPaletteDiscovery();
                var first = MapEditorWindow.CreateSection("__MapValidation", false);
                string firstScene = first.gameObject.scene.path, firstPrefab = first.prefabPath;
                Set("folder1", Path.GetDirectoryName(firstScene).Replace('\\', '/')); Set("scene", firstScene); Set("prefab1", firstPrefab);
                var test = Object.FindFirstObjectByType<MapTestRunner>();
                Check(test != null && test.testStart != null, "new section should have a test start");
                test.testStart.transform.localPosition = new Vector3(0,.55f,0);
                Place(first, "Terrain_Box", new Vector2(0,-.5f), new Vector2(20,1));
                Place(first, "Marker_PlayerStart", new Vector2(-3,.6f), Vector2.one);
                Place(first, "Marker_BallStart", new Vector2(-1,.6f), Vector2.one);
                Place(first, "Goal_Area", new Vector2(5,1), new Vector2(2,2));
                Place(first, "Connector_Exit", new Vector2(9,0), Vector2.one);
                Check(MapEditorWindow.ExportSection(first, false), "first prefab export failed");
                string firstGuid = AssetDatabase.AssetPathToGUID(firstPrefab);
                Check(!string.IsNullOrEmpty(firstGuid), "exported prefab missing GUID");
                Check(MapEditorWindow.ExportSection(first, false) && AssetDatabase.AssetPathToGUID(firstPrefab) == firstGuid, "re-export must preserve prefab GUID");
                var firstAsset = AssetDatabase.LoadAssetAtPath<GameObject>(firstPrefab)?.GetComponent<MapSection>();
                Check(firstAsset != null && firstAsset.GetComponentsInChildren<MapMarker>(true).Length == 3 && firstAsset.GetComponentInChildren<MapGoalArea>() != null, "exported section content");
                Check(firstAsset.GetComponentInChildren<MapTestRunner>(true) == null && firstAsset.GetComponentInChildren<Camera>(true) == null, "editor test objects must not be in the prefab");
                var second = MapEditorWindow.CreateSection("__MapValidation", false);
                string secondScene = second.gameObject.scene.path, secondPrefab = second.prefabPath;
                Set("folder2", Path.GetDirectoryName(secondScene).Replace('\\', '/')); Set("prefab2", secondPrefab);
                Check(second.sectionId != firstAsset.sectionId && secondScene != firstScene, "same-name sections need unique paths and IDs");
                var found = MapSectionBrowser.FindSections();
                Check(found.Exists(entry => entry.scenePath == firstScene && entry.displayName == "__MapValidation") && found.Exists(entry => entry.scenePath == secondScene && entry.displayName == "__MapValidation"), "section browser must find both same-name scenes");
                Place(second, "Terrain_Triangle", Vector2.zero, Vector2.one);
                Place(second, "Connector_Entrance", new Vector2(-1,0), Vector2.one);
                Check(MapEditorWindow.ExportSection(second, false), "second prefab export failed");
                Check(AssetDatabase.LoadAssetAtPath<GameObject>(firstPrefab)?.GetComponent<MapSection>() != null && AssetDatabase.LoadAssetAtPath<GameObject>(secondPrefab)?.GetComponent<MapSection>() != null, "both sections must survive independent export");
                var draft = MapAssemblyWindow.CreateDraft("__MapValidation", false);
                string draftFolder = Path.GetDirectoryName(draft.gameObject.scene.path).Replace('\\', '/'); Set("folder3", draftFolder);
                var a = AssetDatabase.LoadAssetAtPath<GameObject>(firstPrefab).GetComponent<MapSection>();
                var b = AssetDatabase.LoadAssetAtPath<GameObject>(secondPrefab).GetComponent<MapSection>();
                var aInstance = MapAssemblyWindow.AddSection(draft, a, Vector2.zero);
                var bInstance = MapAssemblyWindow.AddSection(draft, b, new Vector2(25, 3));
                Check(aInstance != null && bInstance != null && MapAssemblyWindow.AlignToPrevious(draft, bInstance.transform), "assembly connector alignment failed");
                Check(Vector2.Distance(bInstance.transform.localPosition, new Vector2(10, 0)) < .001f, "aligned section position incorrect");
                var valid = MapValidation.Assembly(draft); Check(valid.errors.Count == 0, "valid assembly rejected: " + valid.Message());
                var misplaced = new GameObject("not a section"); misplaced.transform.SetParent(draft.transform);
                Check(MapValidation.Assembly(draft).errors.Count > 0, "invalid assembly should be rejected"); Object.DestroyImmediate(misplaced);
                Check(MapAssemblyWindow.SaveDraft(draft, false), "assembly scene save failed");
                string copy = draftFolder + "/SystemCopy.prefab";
                Check(AssetDatabase.CopyAsset("Assets/CoopPrototype/Prefabs/PrototypeSystem.prefab", copy), "could not create disposable system prefab");
                Check(MapAssemblyWindow.ApplyToPrefab(draft, copy, false), "applying assembly to system failed");
                var saved = AssetDatabase.LoadAssetAtPath<GameObject>(copy).GetComponent<PrototypeGame>().mapSections;
                Check(saved.Length == 2 && saved[0].prefab == a && saved[1].prefab == b && Vector2.Distance(saved[1].position, new Vector2(10, 0)) < .001f, "game prefab assembly mismatch");
                Debug.Log("SHAPE_MAP_ASSEMBLY_VALIDATION_OK");
                Check(MapSectionBrowser.OpenSection(firstScene, false), "section browser could not open an existing scene");
                test = Object.FindFirstObjectByType<MapTestRunner>(); Check(test != null && test.testStart != null, "reopened scene must retain test marker");
                test.shape = MapTestShape.Triangle; test.spawnBallNearTestStart = false; test.useCustomBallStart = true; test.customBallStart = new Vector2(2, 1.5f);
                var initialCamera = Object.FindFirstObjectByType<Camera>();
                if (initialCamera != null) Object.DestroyImmediate(initialCamera.gameObject);
                EditorSceneManager.MarkSceneDirty(test.gameObject.scene); EditorSceneManager.SaveScene(test.gameObject.scene);
                Int("shape", 0); Int("phase", 0); Set("error", ""); SessionState.SetBool(Prefix + "active", true);
                Debug.Log("SHAPE_MAP_ASSET_CHECK_OK");
                EditorApplication.EnterPlaymode();
            }
            catch (Exception e) { Debug.LogException(e); Finish(false); }
        }
        static void OnPlayMode(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(Prefix + "active", false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode) { Int("phase", 1); SessionState.SetFloat(Prefix + "until", (float)EditorApplication.timeSinceStartup + .2f); }
            if (state != PlayModeStateChange.EnteredEditMode) return;
            if (!string.IsNullOrEmpty(Get("error"))) { Finish(false); return; }
            int next = Int("shape") + 1;
            if (next >= 3) { Debug.Log("SHAPE_MAP_PLAYMODE_ALL_SHAPES_OK"); Finish(true); return; }
            Int("shape", next);
            var test = Object.FindFirstObjectByType<MapTestRunner>();
            if (test == null) { Debug.LogError("Map test runner missing after play mode."); Finish(false); return; }
            if (next == 1)
            {
                var untagged = new GameObject("Existing untagged test camera", typeof(Camera));
                untagged.GetComponent<Camera>().orthographic = true;
            }
            else if (next == 2)
            {
                var camera = Object.FindFirstObjectByType<Camera>();
                if (camera != null) camera.enabled = false;
            }
            test.shape = (MapTestShape)next; EditorSceneManager.MarkSceneDirty(test.gameObject.scene); EditorSceneManager.SaveScene(test.gameObject.scene);
            EditorApplication.delayCall += EditorApplication.EnterPlaymode;
        }
        static void OnUpdate()
        {
            if (!SessionState.GetBool(Prefix + "active", false) || !EditorApplication.isPlaying || Int("phase") == 0 || EditorApplication.timeSinceStartup < SessionState.GetFloat(Prefix + "until", 0)) return;
            try
            {
                var test = Object.FindFirstObjectByType<MapTestRunner>(); Check(test != null, "test runner missing during play");
                var player = Field<PrototypePlayer>(test, "player"); var ball = Field<Rigidbody2D>(test, "ball");
                Check(player != null && ball != null, "test player or ball not spawned");
                int phase = Int("phase");
                if (phase == 1)
                {
                    Check(player.shape == Int("shape") && player.body.simulated && player.body.bodyType == RigidbodyType2D.Dynamic, "selected shape or physics incorrect");
                    Check(Vector2.Distance(player.spawn, test.testStart.transform.position) < .01f, "test start position ignored");
                    Check(Vector2.Distance(Field<Vector2>(test, "ballSpawn"), test.customBallStart) < .01f, "custom ball test start ignored");
                    var camera = Field<Camera>(test, "testCamera");
                    Check(camera != null && camera.isActiveAndEnabled && camera.targetTexture == null, "test camera missing or not rendering");
                    Check(camera.name == (Int("shape") == 1 ? "Existing untagged test camera" : "Map test camera (runtime)"), "wrong camera selected for test");
                    Check(Vector2.Distance(camera.transform.position, (Vector2)player.transform.position + Vector2.up) < .5f, "test camera does not follow player");
                    Check((player.shape == 0 && player.shapeCollider is PolygonCollider2D) || (player.shape == 1 && player.shapeCollider is BoxCollider2D) || (player.shape == 2 && player.shapeCollider is CircleCollider2D), "incorrect player collider");
                    player.freezePending = true; Int("phase", 2); SessionState.SetFloat(Prefix + "until", (float)EditorApplication.timeSinceStartup + .15f); return;
                }
                if (phase == 2)
                {
                    Check(player.frozen, "test player did not freeze");
                    test.ResetTest(); Check(!player.frozen && Vector2.Distance(player.body.position, player.spawn) < .01f, "test restart did not reset player");
                    var goal = test.section.GetComponentInChildren<MapGoalArea>(); Check(goal != null, "goal missing");
                    ball.position = goal.transform.position; ball.linearVelocity = Vector2.zero; Physics2D.SyncTransforms();
                    Int("phase", 3); SessionState.SetFloat(Prefix + "until", (float)EditorApplication.timeSinceStartup + .15f); return;
                }
                Check(Field<bool>(test, "complete"), "goal ball did not trigger test goal");
                test.ResetTest(); Check(!Field<bool>(test, "complete"), "test restart must clear goal state");
                var a = AssetDatabase.LoadAssetAtPath<GameObject>(Get("prefab1"))?.GetComponent<MapSection>(); var b = AssetDatabase.LoadAssetAtPath<GameObject>(Get("prefab2"))?.GetComponent<MapSection>();
                var root = new GameObject("map assembly verification");
                var placements = new[] { new MapSectionPlacement { prefab = a, position = new Vector2(10,0) }, new MapSectionPlacement { prefab = b, position = new Vector2(25,0) } };
                bool assembled = MapSectionAssembler.Build(placements, root.transform, null, out var playerStart, out var ballStart);
                Check(assembled && root.transform.childCount == 2 && playerStart.HasValue && ballStart.HasValue && Vector2.Distance(playerStart.Value, new Vector2(7,.6f)) < .01f && Vector2.Distance(ballStart.Value, new Vector2(9,.6f)) < .01f, "composed sections or spawn marker offsets incorrect");
                Object.Destroy(root); Debug.Log("SHAPE_MAP_TEST_SHAPE_OK: " + (MapTestShape)Int("shape")); Int("phase", 0); EditorApplication.ExitPlaymode();
            }
            catch (Exception e) { Debug.LogException(e); Set("error", e.ToString()); Int("phase", 0); EditorApplication.ExitPlaymode(); }
        }
        static void Finish(bool success)
        {
            SessionState.SetBool(Prefix + "active", false);
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            foreach (string key in new[] { "folder1", "folder2", "folder3" })
            {
                string folder = Get(key);
                if ((folder.StartsWith("Assets/CoopMaps/Sections/__MapValidation_", StringComparison.Ordinal) || folder.StartsWith("Assets/CoopMaps/Assemblies/__MapValidation_", StringComparison.Ordinal)) && AssetDatabase.IsValidFolder(folder)) AssetDatabase.DeleteAsset(folder);
            }
            AssetDatabase.SaveAssets(); Debug.Log(success ? "SHAPE_MAP_VERIFICATION_OK" : "SHAPE_MAP_VERIFICATION_FAILED"); EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
