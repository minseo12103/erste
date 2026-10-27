using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Octopusdream.Editor
{
    /// <summary>Idempotent authoring of the user's park map for the playable prototype.</summary>
    public static class PrototypeSceneSetup
    {
        public const string ScenePath = "Assets/WOC/Scenes/WOC_Ct_Building_Park.unity";
        private const string RootName = "Octopusdream Prototype";
        private const string MaterialFolder = "Assets/Octopusdream/Prototype/Materials";

        [MenuItem("Octopusdream/Prepare Park Prototype")]
        public static void Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Leave Play Mode before preparing the park scene.");
            if (!File.Exists(ScenePath))
                throw new FileNotFoundException("The user's park scene was not found.", ScenePath);
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            Directory.CreateDirectory("Logs");
            const string backupPath = "Logs/WOC_Ct_Building_Park.before-prototype.unity";
            if (!File.Exists(backupPath))
                File.Copy(ScenePath, backupPath);

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            GameObject root = scene.GetRootGameObjects().FirstOrDefault(item => item.name == RootName);
            if (root == null)
                root = new GameObject(RootName);

            Type gameType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("Octopusdream.PrototypeGame"))
                .FirstOrDefault(type => type != null);
            if (gameType == null || !typeof(MonoBehaviour).IsAssignableFrom(gameType))
                throw new InvalidOperationException("Octopusdream.PrototypeGame must compile before preparing the park scene.");
            Component game = root.GetComponent(gameType) ?? root.AddComponent(gameType);

            MeshRenderer[] renderers = SceneComponents<MeshRenderer>(scene)
                .Where(renderer => renderer.gameObject.activeInHierarchy && renderer.enabled && !renderer.transform.IsChildOf(root.transform)).ToArray();
            Bounds parkBounds = FindParkBounds(scene, root.transform, renderers);
            int addedColliders = AddEnvironmentColliders(renderers);
            int convertedMaterials = ConvertUnsupportedMaterials(renderers);
            Physics.SyncTransforms();

            Vector3[] spawnPoints;
            Vector3 lobby;
            FindSafeSpawns(parkBounds, out spawnPoints, out lobby);
            float floor = spawnPoints.Min(point => point.y) - 0.08f;
            float ceiling = Mathf.Max(floor + 16f, parkBounds.max.y + 5f);
            Vector3 arenaCenter = new Vector3(parkBounds.center.x, (floor + ceiling) * 0.5f, parkBounds.center.z);
            Vector3 arenaSize = new Vector3(Mathf.Max(30f, parkBounds.size.x + 4f), ceiling - floor, Mathf.Max(30f, parkBounds.size.z + 4f));
            ConfigureGame(game, arenaCenter, arenaSize, lobby, spawnPoints);
            ConfigureLighting(scene, parkBounds);
            ConfigureBuildScenes();

            EditorUtility.SetDirty(game);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new IOException("Could not save the prepared park scene.");
            AssetDatabase.SaveAssets();
            WriteReport(scene, game, parkBounds, addedColliders, convertedMaterials);
            Debug.Log("OCTOPUS_PARK_READY: " + ScenePath + " | spawns=" + spawnPoints.Length + " | added colliders=" + addedColliders);
        }

        [MenuItem("Octopusdream/Inspect Park Prototype")]
        public static void Inspect()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                    return;
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
            GameObject root = scene.GetRootGameObjects().FirstOrDefault(item => item.name == RootName);
            if (root == null)
                throw new InvalidOperationException("Prepare the park prototype first.");
            Component game = root.GetComponents<Component>().FirstOrDefault(component => component != null && component.GetType().FullName == "Octopusdream.PrototypeGame");
            if (game == null)
                throw new InvalidOperationException("PrototypeGame component is missing.");
            MeshRenderer[] renderers = SceneComponents<MeshRenderer>(scene).Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy).ToArray();
            WriteReport(scene, game, FindParkBounds(scene, root.transform, renderers), 0, 0);
        }

        private static T[] SceneComponents<T>(Scene scene) where T : Component
        {
            return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
        }

        private static Bounds FindParkBounds(Scene scene, Transform prototype, MeshRenderer[] renderers)
        {
            GameObject parkRoot = scene.GetRootGameObjects().FirstOrDefault(item => item.transform != prototype && item.name.IndexOf("Park", StringComparison.OrdinalIgnoreCase) >= 0 && item.GetComponentsInChildren<MeshRenderer>(true).Length > 10);
            MeshRenderer[] park = parkRoot != null
                ? parkRoot.GetComponentsInChildren<MeshRenderer>(true).Where(renderer => renderer.gameObject.activeInHierarchy && renderer.enabled).ToArray()
                : renderers.Where(renderer => renderer.name != "Plane" && !renderer.transform.IsChildOf(prototype)).ToArray();
            if (park.Length == 0)
                throw new InvalidOperationException("No authored park geometry was found.");

            Bounds bounds = park[0].bounds;
            foreach (MeshRenderer renderer in park.Skip(1))
                bounds.Encapsulate(renderer.bounds);
            if (bounds.size.x > 150f || bounds.size.z > 150f)
                throw new InvalidOperationException("Unexpected park bounds. Exclude the large background plane from the playable arena.");
            return bounds;
        }

        private static int AddEnvironmentColliders(IEnumerable<MeshRenderer> renderers)
        {
            int count = 0;
            foreach (MeshRenderer renderer in renderers)
            {
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null)
                    continue;
                Collider[] existing = renderer.GetComponents<Collider>();
                if (existing.Any(item => item.enabled && !item.isTrigger))
                    continue;
                MeshCollider collider = renderer.gameObject.AddComponent<MeshCollider>();
                collider.convex = false;
                collider.sharedMesh = filter.sharedMesh;
                collider.isTrigger = false;
                PrefabUtility.RecordPrefabInstancePropertyModifications(collider);
                count++;
            }
            return count;
        }

        private static int ConvertUnsupportedMaterials(IEnumerable<MeshRenderer> renderers)
        {
            Shader urp = Shader.Find("Universal Render Pipeline/Lit");
            if (urp == null)
                throw new InvalidOperationException("URP Lit shader is unavailable.");
            Dictionary<Material, Material> converted = new Dictionary<Material, Material>();
            Material missingMaterialFallback = null;
            foreach (MeshRenderer renderer in renderers)
            {
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    Material source = materials[i];
                    if (source == null)
                    {
                        if (missingMaterialFallback == null)
                        {
                            EnsureAssetFolder(MaterialFolder);
                            const string fallbackPath = MaterialFolder + "/PrototypeSeafloor.mat";
                            missingMaterialFallback = AssetDatabase.LoadAssetAtPath<Material>(fallbackPath);
                            if (missingMaterialFallback == null)
                            {
                                missingMaterialFallback = new Material(urp) { name = "Prototype Seafloor" };
                                missingMaterialFallback.SetColor("_BaseColor", new Color(0.27f, 0.35f, 0.34f));
                                missingMaterialFallback.SetFloat("_Smoothness", 0.18f);
                                AssetDatabase.CreateAsset(missingMaterialFallback, fallbackPath);
                            }
                        }
                        materials[i] = missingMaterialFallback;
                        changed = true;
                        continue;
                    }
                    if (source.shader != null && (source.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal) || source.shader.name.StartsWith("Shader Graphs/", StringComparison.Ordinal)))
                        continue;
                    Material destination;
                    if (!converted.TryGetValue(source, out destination))
                    {
                        EnsureAssetFolder(MaterialFolder);
                        string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(source));
                        if (string.IsNullOrEmpty(guid))
                            guid = source.GetInstanceID().ToString();
                        string name = string.Concat(source.name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
                        string path = MaterialFolder + "/" + name + "_" + guid + ".mat";
                        destination = AssetDatabase.LoadAssetAtPath<Material>(path);
                        if (destination == null)
                        {
                            destination = new Material(urp) { name = source.name + " (Prototype URP)" };
                            CopyMaterialProperties(source, destination);
                            AssetDatabase.CreateAsset(destination, path);
                        }
                        converted.Add(source, destination);
                    }
                    materials[i] = destination;
                    changed = true;
                }
                if (changed)
                {
                    renderer.sharedMaterials = materials;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                }
            }
            return converted.Count + (missingMaterialFallback != null ? 1 : 0);
        }

        private static void CopyMaterialProperties(Material source, Material target)
        {
            string textureProperty = source.HasProperty("_BaseMap") && source.GetTexture("_BaseMap") != null ? "_BaseMap" : "_MainTex";
            if (source.HasProperty(textureProperty))
            {
                target.SetTexture("_BaseMap", source.GetTexture(textureProperty));
                target.SetTextureScale("_BaseMap", source.GetTextureScale(textureProperty));
                target.SetTextureOffset("_BaseMap", source.GetTextureOffset(textureProperty));
            }
            Color color = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white;
            target.SetColor("_BaseColor", color);
            foreach (string property in new[] { "_BumpMap", "_EmissionMap", "_MetallicGlossMap", "_OcclusionMap" })
            {
                if (source.HasProperty(property) && source.GetTexture(property) != null)
                    target.SetTexture(property, source.GetTexture(property));
            }
            foreach (string property in new[] { "_Metallic", "_BumpScale", "_OcclusionStrength", "_Cutoff", "_Cull" })
            {
                if (source.HasProperty(property))
                    target.SetFloat(property, source.GetFloat(property));
            }
            target.SetFloat("_Smoothness", source.HasProperty("_Smoothness") ? source.GetFloat("_Smoothness") : source.HasProperty("_Glossiness") ? source.GetFloat("_Glossiness") : 0.25f);
            if (target.GetTexture("_BumpMap") != null)
                target.EnableKeyword("_NORMALMAP");
            if (target.GetTexture("_MetallicGlossMap") != null)
                target.EnableKeyword("_METALLICSPECGLOSSMAP");
            if (source.HasProperty("_EmissionColor"))
            {
                Color emission = source.GetColor("_EmissionColor");
                target.SetColor("_EmissionColor", emission);
                if (emission.maxColorComponent > 0f)
                    target.EnableKeyword("_EMISSION");
            }
            bool alphaClip = source.IsKeywordEnabled("_ALPHATEST_ON") || (source.HasProperty("_AlphaClip") && source.GetFloat("_AlphaClip") > 0.5f);
            if (alphaClip)
            {
                target.SetFloat("_AlphaClip", 1f);
                target.EnableKeyword("_ALPHATEST_ON");
                target.SetOverrideTag("RenderType", "TransparentCutout");
                target.renderQueue = (int)RenderQueue.AlphaTest;
            }
        }

        private static void FindSafeSpawns(Bounds bounds, out Vector3[] result, out Vector3 lobby)
        {
            List<Vector3> candidates = new List<Vector3>();
            float rayTop = bounds.max.y + 8f;
            float expectedFloor = -0.02f;
            List<float> floorHeights = new List<float>();
            for (float z = bounds.min.z + 1.8f; z < bounds.max.z - 1.8f; z += 1.8f)
            {
                for (float x = bounds.min.x + 1.8f; x < bounds.max.x - 1.8f; x += 1.8f)
                {
                    RaycastHit[] hits = Physics.RaycastAll(new Vector3(x, rayTop, z), Vector3.down, rayTop - bounds.min.y + 10f, ~0, QueryTriggerInteraction.Ignore);
                    foreach (RaycastHit hit in hits)
                    {
                        if (hit.normal.y > 0.8f && hit.point.y >= bounds.min.y - 0.3f && hit.point.y < bounds.min.y + 1.5f)
                            floorHeights.Add(hit.point.y);
                    }
                }
            }
            if (floorHeights.Count > 0)
            {
                floorHeights.Sort();
                expectedFloor = floorHeights[floorHeights.Count / 3];
            }

            for (float z = bounds.min.z + 1.8f; z < bounds.max.z - 1.8f; z += 1.8f)
            {
                for (float x = bounds.min.x + 1.8f; x < bounds.max.x - 1.8f; x += 1.8f)
                {
                    RaycastHit[] hits = Physics.RaycastAll(new Vector3(x, rayTop, z), Vector3.down, rayTop - bounds.min.y + 10f, ~0, QueryTriggerInteraction.Ignore);
                    Array.Sort(hits, (first, second) => first.distance.CompareTo(second.distance));
                    foreach (RaycastHit hit in hits)
                    {
                        if (hit.normal.y < 0.8f || hit.point.y < expectedFloor - 0.25f || hit.point.y > expectedFloor + 0.75f)
                            continue;
                        Vector3 foot = hit.point + Vector3.up * 0.08f;
                        if (Physics.CheckCapsule(foot + Vector3.up * 0.56f, foot + Vector3.up * 1.18f, 0.52f, ~0, QueryTriggerInteraction.Ignore))
                            continue;
                        candidates.Add(foot);
                        break;
                    }
                }
            }
            if (candidates.Count < 21)
                throw new InvalidOperationException("Could find only " + candidates.Count + " clear ground positions in the authored map. Need 21 for the lobby and 20 players.");

            Vector3 flatCenter = new Vector3(bounds.center.x, expectedFloor, bounds.center.z);
            lobby = candidates.OrderByDescending(point => candidates.Count(other => (other - point).sqrMagnitude < 64f) * 3f - (point - flatCenter).sqrMagnitude * 0.08f).First();
            Vector3 chosenLobby = lobby;
            result = candidates.Where(point => (point - chosenLobby).sqrMagnitude > 5f)
                .OrderBy(point => (point - chosenLobby).sqrMagnitude).Take(20).ToArray();
            if (result.Length != 20)
                throw new InvalidOperationException("Not enough separated spawn locations around the lobby.");
        }

        private static void ConfigureGame(Component game, Vector3 center, Vector3 size, Vector3 lobby, Vector3[] spawns)
        {
            SerializedObject serialized = new SerializedObject(game);
            SetVector(serialized, "arenaCenter", center);
            SetVector(serialized, "arenaSize", size);
            SetVector(serialized, "lobbyPosition", lobby);
            SerializedProperty spawnProperty = RequiredProperty(serialized, "spawnPoints");
            spawnProperty.arraySize = spawns.Length;
            for (int i = 0; i < spawns.Length; i++)
                spawnProperty.GetArrayElementAtIndex(i).vector3Value = spawns[i];
            RequiredProperty(serialized, "maxPlayers").intValue = 20;
            RequiredProperty(serialized, "hunterCount").intValue = 1;
            RequiredProperty(serialized, "hideDuration").floatValue = 30f;
            RequiredProperty(serialized, "huntDuration").floatValue = 180f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetVector(SerializedObject serialized, string name, Vector3 value)
        {
            RequiredProperty(serialized, name).vector3Value = value;
        }

        private static SerializedProperty RequiredProperty(SerializedObject serialized, string name)
        {
            SerializedProperty property = serialized.FindProperty(name);
            if (property == null)
                throw new InvalidOperationException("PrototypeGame is missing expected serialized field: " + name);
            return property;
        }

        private static void ConfigureLighting(Scene scene, Bounds bounds)
        {
            RenderSettings.skybox = null;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.012f;
            RenderSettings.fogColor = new Color(0.055f, 0.22f, 0.29f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.45f, 0.61f, 0.65f);
            RenderSettings.ambientEquatorColor = new Color(0.25f, 0.37f, 0.4f);
            RenderSettings.ambientGroundColor = new Color(0.17f, 0.22f, 0.26f);
            RenderSettings.ambientIntensity = 1f;

            Light mainLight = SceneComponents<Light>(scene).FirstOrDefault(light => light.type == LightType.Directional && light.gameObject.activeInHierarchy);
            if (mainLight == null)
            {
                mainLight = new GameObject("Prototype Sun").AddComponent<Light>();
                mainLight.type = LightType.Directional;
            }
            mainLight.color = new Color(0.76f, 0.9f, 1f);
            mainLight.intensity = 1.35f;
            mainLight.shadows = LightShadows.Soft;
            mainLight.shadowStrength = 0.65f;
            mainLight.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            RenderSettings.sun = mainLight;

            Camera[] cameras = SceneComponents<Camera>(scene);
            Camera mainCamera = cameras.FirstOrDefault(camera => camera.CompareTag("MainCamera") && camera.gameObject.activeInHierarchy) ?? cameras.FirstOrDefault();
            foreach (Camera camera in cameras)
            {
                camera.enabled = camera == mainCamera;
                camera.tag = camera == mainCamera ? "MainCamera" : "Untagged";
                camera.farClipPlane = Mathf.Max(180f, bounds.size.magnitude * 3f);
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = RenderSettings.fogColor;
                AudioListener listener = camera.GetComponent<AudioListener>();
                if (listener != null)
                    listener.enabled = camera == mainCamera;
            }
        }

        private static void ConfigureBuildScenes()
        {
            List<EditorBuildSettingsScene> scenes = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
            scenes.AddRange(EditorBuildSettings.scenes.Where(scene => scene.path != ScenePath));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void EnsureAssetFolder(string folder)
        {
            string[] parts = folder.Split('/');
            string parent = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string path = parent + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(path))
                    AssetDatabase.CreateFolder(parent, parts[i]);
                parent = path;
            }
        }

        [Serializable]
        private sealed class SceneReport
        {
            public string scene;
            public Vector3 parkCenter;
            public Vector3 parkSize;
            public Vector3 arenaCenter;
            public Vector3 arenaSize;
            public Vector3 lobbyPosition;
            public Vector3[] spawnPoints;
            public int meshRenderers;
            public int environmentColliders;
            public int addedColliders;
            public int convertedMaterials;
            public int missingScripts;
            public int missingMaterials;
            public int uncoveredMeshes;
        }

        private static void WriteReport(Scene scene, Component game, Bounds park, int addedColliders, int convertedMaterials)
        {
            SerializedObject serialized = new SerializedObject(game);
            SerializedProperty spawns = RequiredProperty(serialized, "spawnPoints");
            Vector3[] points = new Vector3[spawns.arraySize];
            for (int i = 0; i < points.Length; i++)
                points[i] = spawns.GetArrayElementAtIndex(i).vector3Value;
            int missingScripts = SceneComponents<Transform>(scene).Sum(transform => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject));
            MeshRenderer[] activeRenderers = SceneComponents<MeshRenderer>(scene).Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy).ToArray();
            SceneReport report = new SceneReport
            {
                scene = scene.path,
                parkCenter = park.center,
                parkSize = park.size,
                arenaCenter = RequiredProperty(serialized, "arenaCenter").vector3Value,
                arenaSize = RequiredProperty(serialized, "arenaSize").vector3Value,
                lobbyPosition = RequiredProperty(serialized, "lobbyPosition").vector3Value,
                spawnPoints = points,
                meshRenderers = SceneComponents<MeshRenderer>(scene).Length,
                environmentColliders = SceneComponents<Collider>(scene).Length,
                addedColliders = addedColliders,
                convertedMaterials = convertedMaterials,
                missingScripts = missingScripts,
                missingMaterials = activeRenderers.Sum(renderer => renderer.sharedMaterials.Count(material => material == null)),
                uncoveredMeshes = activeRenderers.Count(renderer => renderer.GetComponent<MeshFilter>() != null && !renderer.GetComponents<Collider>().Any(collider => collider.enabled && !collider.isTrigger))
            };
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/ParkPrototypeScene.json", JsonUtility.ToJson(report, true));
            Debug.Log("OCTOPUS_PARK_REPORT: " + JsonUtility.ToJson(report));
        }
    }
}
