using UnityEngine;
using UnityEngine.InputSystem;

namespace ShapeCoop
{
    public enum MapTestShape { Triangle, Square, Circle }
    public enum MapTestStartMode { Automatic, SelectedMarker }

    // Lives in an authoring scene, never inside the exported section prefab.
    public sealed class MapTestRunner : MonoBehaviour
    {
        public MapSection section;
        public MapMarker testStart;
        public MapTestStartMode startMode;
        public MapTestShape shape;
        public PrototypeSettings settingsSource;
        public bool spawnBallNearTestStart = true;
        public bool useCustomBallStart;
        public Vector2 customBallStart;
        public Vector2 ballOffset = new Vector2(2, 0);
        public bool followPlayer = true;
        public float cameraSize = 7f;
        PrototypePlayer player;
        Rigidbody2D ball;
        MapGimmick[] gimmicks = System.Array.Empty<MapGimmick>();
        Camera testCamera;
        PhysicsMaterial2D ballMaterial;
        Vector2 ballSpawn;
        bool complete;
        readonly EmoteInputReader emoteInput = new EmoteInputReader();

        void Start()
        {
            if (section == null) { Debug.LogError("Map test: assign a MapSection."); enabled = false; return; }
            gimmicks = section.GetComponentsInChildren<MapGimmick>(true);
            foreach (var gimmick in gimmicks) gimmick.Initialize(true);
            var config = gameObject.AddComponent<PrototypeSettings>();
            if (settingsSource != null) JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(settingsSource), config);
            var marker = ResolveStart();
            if (marker == null) Debug.LogWarning("Map test: no valid start marker; using the section origin.");
            Vector2 spawn = marker != null ? (Vector2)marker.transform.position : (Vector2)section.transform.position;
            testCamera = FindTestCamera();
            if (testCamera == null)
            {
                var cameraObject = new GameObject("Map test camera (runtime)");
                cameraObject.tag = "MainCamera";
                testCamera = cameraObject.AddComponent<Camera>();
                testCamera.orthographic = true;
                testCamera.clearFlags = CameraClearFlags.SolidColor;
                testCamera.backgroundColor = new Color(.09f, .13f, .18f);
                testCamera.orthographicSize = Mathf.Max(1f, cameraSize);
                cameraObject.transform.position = new Vector3(spawn.x, spawn.y + 1f, -10f);
                if (FindFirstObjectByType<AudioListener>() == null) cameraObject.AddComponent<AudioListener>();
            }
            var playerObject = new GameObject("Test player (runtime)");
            player = playerObject.AddComponent<PrototypePlayer>();
            player.Initialize(config, new PlayerData { id = "local", name = "Test", shape = (int)shape, color = 0 }, spawn, true);
            var ballMarker = FindMarker(MapMarkerKind.BallStart);
            ballSpawn = spawnBallNearTestStart ? spawn + ballOffset : useCustomBallStart ? customBallStart : ballMarker != null ? (Vector2)ballMarker.transform.position : spawn + ballOffset;
            var ballObject = new GameObject("Test goal ball (runtime)");
            ball = ballObject.AddComponent<Rigidbody2D>(); ball.mass = config.ballMass; ball.gravityScale = config.gravityScale;
            ball.interpolation = RigidbodyInterpolation2D.Interpolate; ball.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var collider = ballObject.AddComponent<CircleCollider2D>(); collider.radius = .6f;
            ballMaterial = PrototypeGame.CreateBallMaterial(config); collider.sharedMaterial = ballMaterial;
            ballObject.AddComponent<MapGoalBall>().SetNormalMaterial(ballMaterial);
            PrototypeVisuals.Shape(ballObject.transform, 2, new Color(1f,.8f,.08f), new Vector2(1.2f,1.2f), 1);
            PrototypeVisuals.Shape(ballObject.transform, 2, new Color(.35f,.22f,.05f), new Vector2(.25f,.25f), 2);
            foreach (var goal in section.GetComponentsInChildren<MapGoalArea>(true)) goal.Entered += CheckGoal;
            ResetTest();
        }
        Camera FindTestCamera()
        {
            var main = Camera.main;
            if (IsUsableCamera(main)) return main;
            foreach (var candidate in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (IsUsableCamera(candidate)) return candidate;
            return null;
        }
        bool IsUsableCamera(Camera candidate)
        {
            return candidate != null && candidate.isActiveAndEnabled && candidate.gameObject.scene == gameObject.scene && candidate.targetTexture == null && candidate.targetDisplay == 0;
        }
        MapMarker FindMarker(MapMarkerKind kind)
        {
            if (section == null) return null;
            foreach (var marker in section.GetComponentsInChildren<MapMarker>(true)) if (marker.kind == kind) return marker;
            return null;
        }
        public MapMarker ResolveStart()
        {
            if (section == null) return null;
            bool validSelection = testStart != null && testStart.transform.IsChildOf(section.transform);
            if (startMode == MapTestStartMode.SelectedMarker) return validSelection ? testStart : null;
            return FindMarker(MapMarkerKind.PlayerStart) ?? (validSelection ? testStart : FindMarker(MapMarkerKind.TestStart));
        }
        void CheckGoal(Collider2D other) { if (ball != null && other.gameObject == ball.gameObject) complete = true; }
        public void ResetTest()
        {
            if (player == null || ball == null) return;
            player.ResetBody(); emoteInput.Reset(); foreach (var gimmick in gimmicks) gimmick.ResetGimmick(); ball.GetComponent<MapGoalBall>()?.ResetEffects();
            ball.position = ballSpawn; ball.rotation = 0; ball.linearVelocity = Vector2.zero; ball.angularVelocity = 0; complete = false;
        }
        void Update()
        {
            if (player == null || Keyboard.current == null) return;
            var k = Keyboard.current;
            player.move = (k.dKey.isPressed ? 1 : 0) - (k.aKey.isPressed ? 1 : 0);
            player.jumpHeld = k.spaceKey.isPressed;
            if (k.spaceKey.wasPressedThisFrame) player.jumpPending = true;
            if (k.leftShiftKey.wasPressedThisFrame || k.rightShiftKey.wasPressedThisFrame) player.dashPending = true;
            if (k.fKey.wasPressedThisFrame) player.freezePending = true;
            if (k.rKey.wasPressedThisFrame) ResetTest();
            emoteInput.Read(k, testCamera, player.transform.position, true);
            player.SetEmoteInput(emoteInput.sequence, emoteInput.requested, emoteInput.held, emoteInput.direction);
        }
        void LateUpdate()
        {
            if (!followPlayer || player == null || testCamera == null) return;
            testCamera.orthographicSize = Mathf.Max(1f, cameraSize);
            var p = player.transform.position; testCamera.transform.position = new Vector3(p.x, p.y + 1f, -10f);
        }
        void OnGUI()
        {
            if (player == null) return;
            GUI.Box(new Rect(10, 10, 340, 135), "MAP TEST" + (complete ? " - GOAL" : ""));
            GUI.Label(new Rect(22, 38, 315, 22), "A / D move   Space jump   Shift dash   F freeze");
            GUI.Label(new Rect(22, 60, 315, 22), "1 / 2 / 3 emotes   Mouse: aim");
            GUI.Label(new Rect(22, 80, 315, 22), "R or button: restart at test start");
            if (GUI.Button(new Rect(22, 105, 135, 27), "Restart test")) ResetTest();
        }
        void OnDestroy() { if (ballMaterial != null) Destroy(ballMaterial); }
#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            if (spawnBallNearTestStart || !useCustomBallStart) return;
            Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(customBallStart, .6f);
            UnityEditor.Handles.Label((Vector3)customBallStart + Vector3.up * .7f, "테스트 공 시작");
        }
#endif
    }
}
