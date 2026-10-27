using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Octopusdream.Editor
{
    /// <summary>
    /// Executes behavioral checks against the real Play Mode components. No production
    /// test hooks and no saved-scene mutations. Batch invocation must omit -quit because
    /// verification exits the editor after the asynchronous Play Mode run completes.
    /// </summary>
    [InitializeOnLoad]
    public static class PrototypeVerification
    {
        private const string PendingKey = "Octopusdream.Verification.Pending";
        private const string DeadlineKey = "Octopusdream.Verification.Deadline";
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly List<CheckResult> Results = new List<CheckResult>();
        private static readonly List<string> RuntimeErrors = new List<string>();
        private static Queue<KeyValuePair<string, Action>> _checks;
        private static PrototypeGame _game;
        private static GameObject _fixture;
        private static GameObject _wall;
        private static Material _floorMaterial;
        private static Texture2D _floorTexture;
        private static int _waitFrames;
        private static bool _finishing;
        private static DateTime _started;

        static PrototypeVerification()
        {
            if (SessionState.GetBool(PendingKey, false))
                Register();
        }

        [MenuItem("Octopusdream/Run Prototype Verification")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Start verification from Edit Mode.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            Results.Clear();
            RuntimeErrors.Clear();
            _checks = null;
            _game = null;
            _fixture = null;
            _wall = null;
            _floorMaterial = null;
            _floorTexture = null;
            _finishing = false;
            _waitFrames = 0;
            EditorSceneManager.OpenScene(PrototypeSceneSetup.ScenePath, OpenSceneMode.Single);
            SessionState.SetBool(PendingKey, true);
            SessionState.SetString(DeadlineKey, DateTime.UtcNow.AddMinutes(3).Ticks.ToString());
            Register();
            EditorApplication.EnterPlaymode();
        }

        private static void Register()
        {
            EditorApplication.update -= Update;
            EditorApplication.update += Update;
            Application.logMessageReceived -= Log;
            Application.logMessageReceived += Log;
            _started = DateTime.UtcNow;
        }

        private static void Log(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                RuntimeErrors.Add(condition + "\n" + stackTrace);
        }

        private static void Update()
        {
            if (_finishing || !SessionState.GetBool(PendingKey, false))
                return;
            long deadline;
            if (long.TryParse(SessionState.GetString(DeadlineKey, "0"), out deadline) && DateTime.UtcNow.Ticks > deadline)
            {
                Results.Add(new CheckResult { name = "play_mode_lifecycle", passed = false, detail = "Timed out waiting for Play Mode or the gameplay controller." });
                Finish();
                return;
            }
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling)
                return;
            if (_game == null)
            {
                _game = UnityEngine.Object.FindAnyObjectByType<PrototypeGame>();
                if (_game == null || _game.Transport == null)
                {
                    _game = null;
                    return;
                }
                _game.enabled = false; // Drive authority ticks explicitly for deterministic mechanics.
                CreateChecks();
                _waitFrames = 3;
            }
            if (_waitFrames-- > 0)
                return;
            if (_checks == null || _checks.Count == 0)
            {
                Finish();
                return;
            }
            KeyValuePair<string, Action> check = _checks.Dequeue();
            try
            {
                check.Value();
                Results.Add(new CheckResult { name = check.Key, passed = true, detail = "Passed" });
                Debug.Log("OCTOPUS_VERIFY_PASS: " + check.Key);
            }
            catch (Exception exception)
            {
                if (exception is TargetInvocationException && exception.InnerException != null)
                    exception = exception.InnerException;
                Results.Add(new CheckResult { name = check.Key, passed = false, detail = exception.ToString() });
                Debug.LogWarning("OCTOPUS_VERIFY_FAIL: " + check.Key + " | " + exception.Message);
                Finish();
                return;
            }
            _waitFrames = 2; // Allow deferred Destroy calls and physics transform updates to complete.
        }

        private static void CreateChecks()
        {
            _checks = new Queue<KeyValuePair<string, Action>>();
            Add("authored_park_colliders_and_twenty_spawns", CheckMap);
            Add("offline_session_and_host_role_allocation", CheckSession);
            Add("hiding_locks_hunter_and_releases_octopus", CheckHidingMovement);
            Add("hiding_disallows_capture_and_scan", CheckHidingActions);
            Add("oxygen_clock_starts_after_hide_period", CheckHuntingTransition);
            Add("infrared_requires_view_cone_and_line_of_sight", CheckScan);
            Add("hidden_score_requires_visible_but_not_center_aimed", CheckHiddenScore);
            Add("ink_unlock_radius_and_single_use", CheckInk);
            Add("paint_seam_and_png_roundtrip", CheckPaint);
            Add("invalid_paint_payloads_preserve_canvas", CheckInvalidPaint);
            Add("softness_zero_rigid_and_ten_deforms", CheckSoftness);
            Add("surface_color_copy_fills_octopus", CheckColorCopy);
            Add("harpoon_capture_all_awards_hunters", CheckCaptureWin);
            Add("oxygen_expiry_awards_surviving_octopus", CheckOxygenWin);
            Add("return_to_lobby_resets_roles_and_skills", CheckLobbyReset);
        }

        private static void Add(string name, Action check) => _checks.Enqueue(new KeyValuePair<string, Action>(name, check));

        private static void CheckMap()
        {
            Require(_game.gameObject.scene.path == PrototypeSceneSetup.ScenePath, "Gameplay is not running in the authored WOC park.");
            Require(_game.spawnPoints != null && _game.spawnPoints.Length >= 20, "Expected at least 20 prepared spawn points.");
            Require(_game.maxPlayers == 20, "Room maximum must be 20.");
            var park = _game.gameObject.scene.GetRootGameObjects().FirstOrDefault(item => item.name.IndexOf("Park", StringComparison.OrdinalIgnoreCase) >= 0 && item.GetComponentsInChildren<MeshRenderer>(true).Length > 10);
            Require(park != null, "Original authored park prefab is absent.");
            MeshRenderer[] renderers = park.GetComponentsInChildren<MeshRenderer>().Where(renderer => renderer.enabled).ToArray();
            Require(renderers.Length > 100, "Expected the full authored park geometry, not a placeholder floor.");
            foreach (MeshRenderer renderer in renderers)
            {
                if (renderer.GetComponent<MeshFilter>() != null)
                    Require(renderer.GetComponents<Collider>().Any(collider => collider.enabled && !collider.isTrigger), "Park mesh has no collider: " + renderer.name);
                Require(renderer.sharedMaterials.All(material => material != null), "Missing park material: " + renderer.name);
            }
            Physics.SyncTransforms();
            foreach (Vector3 spawn in _game.spawnPoints)
            {
                RaycastHit hit;
                Require(Physics.Raycast(spawn + Vector3.up * 0.4f, Vector3.down, out hit, 1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), "Spawn has no ground support: " + spawn);
                Require(hit.normal.y > 0.7f, "Spawn is not on usable ground: " + spawn);
                Require(!Physics.CheckCapsule(spawn + Vector3.up * 0.56f, spawn + Vector3.up * 1.18f, 0.5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), "Spawn capsule is blocked: " + spawn);
            }
        }

        private static void CheckSession()
        {
            _game.Transport.StartOffline();
            Require(_game.Transport.Connected && _game.Transport.IsHost && _game.Transport.LocalId == 1, "Offline authority was not established.");
            Require(_game.Players.Count == 1, "Offline session should initially contain one local player.");
            Invoke("AddPlayer", 2UL, "Verifier A", false);
            Invoke("AddPlayer", 3UL, "Verifier B", false);
            Invoke("AddPlayer", 4UL, "Verifier C", false);
            Set("practice", false);
            _fixture = new GameObject("Verification fixture - runtime only");
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Verification ground";
            floor.transform.SetParent(_fixture.transform);
            floor.transform.position = new Vector3(0f, 999.8f, 0f);
            floor.transform.localScale = new Vector3(40f, 0.4f, 40f);
            _floorMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _floorMaterial.SetColor("_BaseColor", Color.white);
            floor.GetComponent<Renderer>().sharedMaterial = _floorMaterial;
            _game.arenaCenter = new Vector3(0f, 1008f, 0f);
            _game.arenaSize = new Vector3(40f, 20f, 40f);
            _game.spawnPoints = new[] { new Vector3(-5f, 1000.08f, 0f), new Vector3(5f, 1000.08f, 0f), new Vector3(-5f, 1000.08f, 6f), new Vector3(5f, 1000.08f, 6f) };
            _game.hunterCount = 2;
            Player(1).State.volunteer = true;
            Player(2).State.volunteer = true;
            Invoke("StartRound");
            Require(_game.Players.Count == 4, "Expected four test players.");
            Require(_game.Phase == RoundPhase.Hiding, "StartRound must begin in the hiding phase.");
            Require(_game.Players.Values.Count(player => player.State.diver) == 2, "Host did not allocate the requested hunter count.");
            Require(Player(1).State.diver && Player(2).State.diver, "Available volunteers must be selected before non-volunteers.");
            Require(Player(1).Appearance.IsDiver && !Player(3).Appearance.IsDiver, "Placeholder visuals do not match assigned roles.");
        }

        private static void CheckHidingMovement()
        {
            PrototypePlayer hunter = Player(1), octopus = Player(3);
            hunter.InputMove = octopus.InputMove = Vector3.right;
            hunter.InputAt = octopus.InputAt = Time.unscaledTime;
            Vector3 hunterBefore = hunter.transform.position, octopusBefore = octopus.transform.position;
            Invoke("HostUpdate", 0.05f);
            Require(Mathf.Abs(hunter.transform.position.x - hunterBefore.x) < 0.001f, "Hunter moved during dive preparation.");
            Require(octopus.transform.position.x > octopusBefore.x + 0.1f, "Octopus could not move during hiding time.");
            hunter.InputMove = octopus.InputMove = Vector3.zero;
        }

        private static void CheckHidingActions()
        {
            Position(Player(1), new Vector3(0f, 1000.08f, 0f));
            Position(Player(3), new Vector3(0f, 1000.08f, 6f));
            Invoke("Shoot", Player(1));
            Invoke("Scan", Player(1));
            Require(!Player(3).State.caught && Player(1).State.shotCooldown == 0f, "Harpoon is active before hunting starts.");
            Require(Player(3).State.revealed == 0f && Player(1).State.scanCooldown == 0f, "Infrared is active during hiding time.");
        }

        private static void CheckHuntingTransition()
        {
            Invoke("ReturnToLobby");
            _game.hunterCount = 1;
            Player(1).State.volunteer = true;
            Invoke("StartRound");
            Set("remaining", 0.01f);
            Invoke("HostUpdate", 0.05f);
            Require(_game.Phase == RoundPhase.Hunting, "Hide countdown did not release hunters.");
            Require(Mathf.Abs(_game.Remaining - _game.huntDuration) < 0.001f, "Oxygen clock must start at the full hunt duration.");
        }

        private static void CheckScan()
        {
            Position(Player(1), new Vector3(0f, 1000.08f, 0f));
            Position(Player(2), new Vector3(0f, 1000.63f, 6f));
            Position(Player(3), new Vector3(0f, 1000.63f, -6f));
            Position(Player(4), new Vector3(4f, 1000.63f, 7f));
            _wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _wall.name = "Verification scan occluder";
            _wall.transform.SetParent(_fixture.transform);
            _wall.transform.position = new Vector3(2f, 1001.48f, 3.5f);
            _wall.transform.localScale = new Vector3(1.5f, 3f, 1f);
            Physics.SyncTransforms();
            Invoke("Scan", Player(1));
            Require(Player(2).State.revealed > 0f, "Visible forward octopus was not revealed.");
            Require(Player(3).State.revealed == 0f, "Rear octopus was revealed outside the view cone.");
            Require(Player(4).State.revealed == 0f, "Occluded octopus was revealed through a wall.");
            Require(Player(1).State.scanCooldown > 0f, "Scan did not start its cooldown.");
            Player(2).State.revealed = 0f;
            Invoke("Scan", Player(1));
            Require(Player(2).State.revealed == 0f, "Scan could be repeated during cooldown.");
            _wall.SetActive(false);
            UnityEngine.Object.Destroy(_wall);
        }

        private static void CheckHiddenScore()
        {
            Position(Player(1), new Vector3(0f, 1000.08f, 0f));
            Position(Player(2), new Vector3(3f, 1000.63f, 8f));
            Player(2).State.hiddenScore = Player(2).State.revealed = 0f;
            Invoke("HostUpdate", 0.05f);
            Require(Player(2).State.hiddenScore > 0f, "Visible off-center octopus did not gain evasion score.");
            Position(Player(2), new Vector3(0f, 1000.63f, 8f));
            Player(2).State.hiddenScore = 0f;
            Invoke("HostUpdate", 0.05f);
            Require(Player(2).State.hiddenScore == 0f, "Directly aimed-at octopus gained hidden score.");
        }

        private static void CheckInk()
        {
            PrototypePlayer caster = Player(2), near = Player(1), far = Player(3);
            far.State.diver = true;
            far.Appearance.SetRole(true);
            Position(caster, new Vector3(0f, 1000.08f, 0f));
            Position(near, new Vector3(3f, 1000.08f, 0f));
            Position(far, new Vector3(11f, 1000.08f, 0f));
            caster.State.hiddenScore = 99f;
            Invoke("Ink", caster);
            Require(!caster.State.inkUsed && near.State.blinded == 0f, "Ink activated before its score threshold.");
            caster.State.hiddenScore = 100f;
            Invoke("Ink", caster);
            Require(caster.State.inkUsed && near.State.blinded > 0f, "Earned ink did not blind the nearby diver.");
            Require(far.State.blinded == 0f, "Ink affected a diver outside its radius.");
            near.State.blinded = 0f;
            Position(far, new Vector3(2f, 1000.08f, 0f));
            Invoke("Ink", caster);
            Require(near.State.blinded == 0f && far.State.blinded == 0f, "A second ink charge was usable in the same round.");
        }

        private static void CheckPaint()
        {
            PrototypeAppearance appearance = Player(2).Appearance;
            appearance.SetBaseColor(Color.blue);
            appearance.Paint(Vector3.right, Color.red, 0.32f);
            Color first = appearance.PaintTexture.GetPixel(0, PrototypeAppearance.PaintHeight / 2);
            Color last = appearance.PaintTexture.GetPixel(PrototypeAppearance.PaintWidth - 1, PrototypeAppearance.PaintHeight / 2);
            Require(first.r > 0.8f && last.r > 0.8f && Mathf.Abs(first.r - last.r) < 0.05f, "Brush did not paint continuously across the sphere UV seam.");
            Require(appearance.PaintTexture.GetPixel(PrototypeAppearance.PaintWidth / 2, PrototypeAppearance.PaintHeight / 2).b > 0.9f, "Brush changed the opposite side of the sphere.");
            string exported = appearance.ExportPaint();
            Require(!string.IsNullOrEmpty(exported), "Paint export was empty.");
            PrototypeAppearance copy = Player(4).Appearance;
            Require(copy.ImportPaint(exported), "Valid paint PNG could not be imported.");
            Require(appearance.PaintTexture.GetPixels32().SequenceEqual(copy.PaintTexture.GetPixels32()), "Paint colors changed across a network PNG round trip.");
        }

        private static void CheckInvalidPaint()
        {
            PrototypeAppearance appearance = Player(2).Appearance;
            Color32[] before = appearance.PaintTexture.GetPixels32();
            Require(!appearance.ImportPaint("not-base64"), "Invalid Base64 paint was accepted.");
            byte[] oversized = Convert.FromBase64String(appearance.ExportPaint());
            oversized[16] = 0; oversized[17] = 1; oversized[18] = 0; oversized[19] = 0;
            Require(!appearance.ImportPaint(Convert.ToBase64String(oversized)), "A PNG declaring an oversized image was accepted.");
            Require(before.SequenceEqual(appearance.PaintTexture.GetPixels32()), "Rejected paint changed the existing canvas.");
        }

        private static void CheckSoftness()
        {
            PrototypePlayer player = Player(2);
            Vector3 savedPosition = player.transform.position;
            PrototypeAppearance appearance = player.Appearance;
            try
            {
                Position(player, savedPosition + Vector3.up * 5f);
                appearance.SetRole(false);
                appearance.SetSoftness(0f);
                appearance.Simulate(Vector3.zero, 1f / 60f);
                Mesh mesh = appearance.GetComponentsInChildren<MeshFilter>(true).First(filter => filter.name == "Octopus sphere").sharedMesh;
                Vector3[] rigid = mesh.vertices;
                appearance.Impact(Vector3.right, 10f);
                appearance.Simulate(Vector3.right * 8f, 1f / 60f);
                Require(rigid.SequenceEqual(mesh.vertices), "Airborne SOFTNESS 0 deformed after impact.");
                appearance.SetSoftness(10f);
                appearance.Impact(Vector3.right, 10f);
                for (int frame = 0; frame < 12; frame++) appearance.Simulate(Vector3.left * 8f, 1f / 60f);
                Vector3[] soft = mesh.vertices;
                Require(soft.Where((vertex, index) => (vertex - rigid[index]).sqrMagnitude > 0.000001f).Any(), "SOFTNESS 10 did not respond to impact.");
                appearance.SetSoftness(0f);
                appearance.Simulate(Vector3.zero, 1f / 60f);
                Require(rigid.SequenceEqual(mesh.vertices), "Returning to SOFTNESS 0 did not restore the airborne sphere.");
            }
            finally { Position(player, savedPosition); }
        }
        private static void CheckColorCopy()
        {
            Color expected = new Color(0.2f, 0.72f, 0.43f, 1f);
            _floorTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            _floorTexture.SetPixels(new[] { expected, expected, expected, expected });
            _floorTexture.Apply();
            _floorMaterial.SetTexture("_BaseMap", _floorTexture);
            Position(Player(2), new Vector3(0f, 1000.08f, 0f));
            Invoke("SampleUnder", Player(2));
            foreach (Color32 pixel in Player(2).Appearance.PaintTexture.GetPixels32())
            {
                Color actual = pixel;
                Require(Mathf.Abs(actual.r - expected.r) < 0.015f && Mathf.Abs(actual.g - expected.g) < 0.015f && Mathf.Abs(actual.b - expected.b) < 0.015f, "V color copy did not fill the avatar with the surface texture color.");
            }
        }

        private static void CheckCaptureWin()
        {
            Invoke("ReturnToLobby");
            _game.hunterCount = 1;
            Player(1).State.volunteer = true;
            Invoke("StartRound");
            Set("remaining", 0.01f);
            Invoke("HostUpdate", 0.05f);
            Position(Player(1), new Vector3(0f, 1000.08f, 0f));
            Position(Player(2), new Vector3(0f, 1000.08f, 6f));
            Position(Player(3), new Vector3(9f, 1000.08f, 6f));
            Position(Player(4), new Vector3(-9f, 1000.08f, 6f));
            foreach (ulong id in new[] { 2UL, 3UL, 4UL })
            {
                Position(Player(id), new Vector3(0f, 1000.08f, 6f));
                for (int frame = 0; frame < 120; frame++) Player(id).Appearance.Simulate(Vector3.zero, 1f / 60f);
                Vector3 aim = (Player(id).Eye - Player(1).Eye).normalized;
                Player(1).State.yaw = Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg;
                Player(1).State.pitch = -Mathf.Asin(aim.y) * Mathf.Rad2Deg;
                Player(1).State.shotCooldown = 0f;
                Invoke("Shoot", Player(1));
                Require(Player(id).State.caught && !Player(id).Controller.enabled, "Harpoon did not capture player " + id);
                if (id != 4)
                    Require(_game.Phase == RoundPhase.Hunting, "Hunters won while an octopus was still alive.");
            }
            Require(_game.Phase == RoundPhase.Finished && Get<string>("result").Contains("잠수부"), "Capturing all octopuses did not award the hunter team.");
        }

        private static void CheckOxygenWin()
        {
            Invoke("ReturnToLobby");
            Player(1).State.volunteer = true;
            Invoke("StartRound");
            Set("remaining", 0.01f);
            Invoke("HostUpdate", 0.05f);
            Set("remaining", 0.01f);
            Invoke("HostUpdate", 0.05f);
            Require(_game.Phase == RoundPhase.Finished && Get<string>("result").Contains("문어"), "Oxygen expiry did not award surviving octopuses.");
        }

        private static void CheckLobbyReset()
        {
            Invoke("ReturnToLobby");
            Require(_game.Phase == RoundPhase.Lobby, "ReturnToLobby did not restore lobby phase.");
            Require(_game.Players.Values.All(player => !player.State.diver && !player.State.caught && !player.State.inkUsed && player.State.hiddenScore == 0f && player.Controller.enabled), "Lobby retained a role, capture, or used-skill state.");
        }

        private static PrototypePlayer Player(ulong id) => _game.Players[id];

        private static void Position(PrototypePlayer player, Vector3 position)
        {
            player.Teleport(position);
            player.State.position = position;
            player.State.yaw = player.State.pitch = 0f;
            player.InputMove = Vector3.zero;
            player.InputAt = Time.unscaledTime;
            Physics.SyncTransforms();
        }

        private static object Invoke(string name, params object[] arguments)
        {
            MethodInfo method = typeof(PrototypeGame).GetMethod(name, PrivateInstance);
            if (method == null)
                throw new MissingMethodException(typeof(PrototypeGame).FullName, name);
            return method.Invoke(_game, arguments);
        }

        private static void Set(string name, object value)
        {
            FieldInfo field = typeof(PrototypeGame).GetField(name, PrivateInstance);
            if (field == null)
                throw new MissingFieldException(typeof(PrototypeGame).FullName, name);
            field.SetValue(_game, value);
        }

        private static T Get<T>(string name) => (T)typeof(PrototypeGame).GetField(name, PrivateInstance).GetValue(_game);

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private static void Finish()
        {
            _finishing = true;
            SessionState.SetBool(PendingKey, false);
            EditorApplication.update -= Update;
            Application.logMessageReceived -= Log;
            if (_fixture != null) UnityEngine.Object.DestroyImmediate(_fixture);
            if (_floorMaterial != null) UnityEngine.Object.DestroyImmediate(_floorMaterial);
            if (_floorTexture != null) UnityEngine.Object.DestroyImmediate(_floorTexture);
            bool passed = Results.Count == 15 && Results.All(check => check.passed) && RuntimeErrors.Count == 0;
            VerificationReport report = new VerificationReport
            {
                passed = passed,
                utc = DateTime.UtcNow.ToString("o"),
                durationSeconds = (DateTime.UtcNow - _started).TotalSeconds,
                scene = PrototypeSceneSetup.ScenePath,
                checks = Results.ToArray(),
                runtimeErrors = RuntimeErrors.ToArray(),
                scope = "Play Mode behavior on prepared park plus isolated runtime physics fixtures; Steam cross-account connectivity and 20-client load are separate checks."
            };
            Directory.CreateDirectory("Logs/Prototype");
            File.WriteAllText("Logs/Prototype/verification.json", JsonUtility.ToJson(report, true));
            Debug.Log("OCTOPUS_VERIFY_RESULT: " + JsonUtility.ToJson(report));
            if (Application.isBatchMode)
                EditorApplication.Exit(passed ? 0 : 1);
            else
                EditorApplication.ExitPlaymode();
        }

        [Serializable]
        private sealed class CheckResult
        {
            public string name;
            public bool passed;
            public string detail;
        }

        [Serializable]
        private sealed class VerificationReport
        {
            public bool passed;
            public string utc;
            public double durationSeconds;
            public string scene;
            public string scope;
            public CheckResult[] checks;
            public string[] runtimeErrors;
        }
    }
}
