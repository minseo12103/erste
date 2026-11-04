using UnityEngine;
using UnityEngine.InputSystem;

namespace ShapeCoop
{
    public enum MapTestShape { Triangle, Square, Circle }

    // Lives in an authoring scene, never inside the exported section prefab.
    public sealed class MapTestRunner : MonoBehaviour
    {
        public MapSection section;
        public MapMarker testStart;
        public MapTestShape shape;
        public PrototypeSettings settingsSource;
        public bool spawnBallNearTestStart = true;
        public Vector2 ballOffset = new Vector2(2, 0);
        public bool followPlayer = true;
        public float cameraSize = 7f;
        PrototypePlayer player;
        Rigidbody2D ball;
        PhysicsMaterial2D ballMaterial;
        Vector2 ballSpawn;
        bool complete;

        void Start()
        {
            if (section == null) { Debug.LogError("Map test: assign a MapSection."); enabled = false; return; }
            var config = gameObject.AddComponent<PrototypeSettings>();
            if (settingsSource != null) JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(settingsSource), config);
            var marker = testStart != null ? testStart : FindMarker(MapMarkerKind.TestStart);
            Vector2 spawn = marker != null ? (Vector2)marker.transform.position : (Vector2)section.transform.position;
            var playerObject = new GameObject("Test player (runtime)");
            player = playerObject.AddComponent<PrototypePlayer>();
            player.Initialize(config, new PlayerData { id = "local", name = "Test", shape = (int)shape, color = 0 }, spawn, true);
            var ballMarker = FindMarker(MapMarkerKind.BallStart);
            ballSpawn = !spawnBallNearTestStart && ballMarker != null ? (Vector2)ballMarker.transform.position : spawn + ballOffset;
            var ballObject = new GameObject("Test goal ball (runtime)");
            ball = ballObject.AddComponent<Rigidbody2D>(); ball.mass = config.ballMass; ball.gravityScale = config.gravityScale;
            ball.interpolation = RigidbodyInterpolation2D.Interpolate; ball.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            var collider = ballObject.AddComponent<CircleCollider2D>(); collider.radius = .6f;
            ballMaterial = PrototypeGame.CreateBallMaterial(config); collider.sharedMaterial = ballMaterial;
            PrototypeVisuals.Shape(ballObject.transform, 2, new Color(1f,.8f,.08f), new Vector2(1.2f,1.2f), 1);
            PrototypeVisuals.Shape(ballObject.transform, 2, new Color(.35f,.22f,.05f), new Vector2(.25f,.25f), 2);
            foreach (var goal in section.GetComponentsInChildren<MapGoalArea>(true)) goal.Entered += CheckGoal;
            ResetTest();
        }
        MapMarker FindMarker(MapMarkerKind kind)
        {
            foreach (var marker in section.GetComponentsInChildren<MapMarker>(true)) if (marker.kind == kind) return marker;
            return null;
        }
        void CheckGoal(Collider2D other) { if (ball != null && other.gameObject == ball.gameObject) complete = true; }
        public void ResetTest()
        {
            if (player == null || ball == null) return;
            player.ResetBody(); ball.position = ballSpawn; ball.rotation = 0; ball.linearVelocity = Vector2.zero; ball.angularVelocity = 0; complete = false;
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
        }
        void LateUpdate()
        {
            if (!followPlayer || player == null || Camera.main == null) return;
            Camera.main.orthographicSize = Mathf.Max(1f, cameraSize);
            var p = player.transform.position; Camera.main.transform.position = new Vector3(p.x, p.y + 1f, -10f);
        }
        void OnGUI()
        {
            if (player == null) return;
            GUI.Box(new Rect(10, 10, 340, 115), "MAP TEST" + (complete ? " - GOAL" : ""));
            GUI.Label(new Rect(22, 38, 315, 22), "A / D move   Space jump   Shift dash   F freeze");
            GUI.Label(new Rect(22, 60, 315, 22), "R or button: restart at test start");
            if (GUI.Button(new Rect(22, 85, 135, 27), "Restart test")) ResetTest();
        }
        void OnDestroy() { if (ballMaterial != null) Destroy(ballMaterial); }
    }
}
