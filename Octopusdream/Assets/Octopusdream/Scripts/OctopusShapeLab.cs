using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Small offline sandbox for tuning the octopus' third-person movement,
/// soft sculpting, and seabed color pickup before networking is added.
/// </summary>
public sealed class OctopusShapeLab : MonoBehaviour
{
    private static readonly Color[] SeabedPalette =
    {
        new Color(0.07f, 0.31f, 0.37f),
        new Color(0.21f, 0.39f, 0.30f),
        new Color(0.31f, 0.25f, 0.38f),
        new Color(0.42f, 0.34f, 0.22f),
        new Color(0.10f, 0.25f, 0.39f),
        new Color(0.32f, 0.42f, 0.37f)
    };

    [SerializeField] private float swimSpeed = 4.2f;
    [SerializeField] private float lookSensitivity = 0.12f;

    private Camera _camera;
    private CharacterController _controller;
    private Transform _player;
    private Transform _body;
    private MeshCollider _bodyCollider;
    private OctopusMold _mold;
    private readonly List<Transform> _tentacleSegments = new List<Transform>();
    private readonly List<Material> _seabedMaterials = new List<Material>();
    private readonly HashSet<Collider> _seabedColliders = new HashSet<Collider>();
    private Material _rockMaterial;
    private Material _coralMaterial;
    private Material _plantMaterial;
    private Color _sampledColor = new Color(0.07f, 0.31f, 0.37f);
    private float _sampleTimer;
    private float _yaw = 0f;
    private float _pitch = 16f;
    private float _smoothness = 0.62f;
    private string _status = "바닥 색을 읽으며 몸통을 조형해 보세요.";

    private void Start()
    {
        _camera = GetComponent<Camera>();
        if (_camera == null)
            _camera = Camera.main;

        if (_camera == null)
        {
            Debug.LogError("Octopus Shape Lab needs a camera in the scene.");
            enabled = false;
            return;
        }

        BuildMaterials();
        BuildSeabed();
        BuildScenery();
        BuildOctopus();
        ConfigureScene();
        UpdateCamera(1f);
    }

    private void Update()
    {
        if (_player == null || _camera == null)
            return;

        ReadInput();
        UpdateOctopusMotion();
        UpdateTentacles();

        _sampleTimer -= Time.deltaTime;
        if (_sampleTimer <= 0f)
        {
            _sampleTimer = 0.12f;
            SampleSeabedColor();
        }

        if (_mold != null)
        {
            _mold.Smoothness = _smoothness;
            _mold.SampleColor = _sampledColor;
            _mold.Tick(Time.deltaTime);
        }

        UpdateCamera(Time.deltaTime);
    }

    private void ReadInput()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard == null || mouse == null)
            return;

        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        Vector2 mouseDelta = mouse.delta.ReadValue();
        if (mouse.rightButton.isPressed)
        {
            _yaw += mouseDelta.x * lookSensitivity;
            _pitch = Mathf.Clamp(_pitch - mouseDelta.y * lookSensitivity, -5f, 48f);
        }

        float wheel = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(wheel) > 0.01f)
            _smoothness = Mathf.Clamp01(_smoothness + Mathf.Sign(wheel) * 0.04f);

        if (keyboard.leftBracketKey.wasPressedThisFrame)
            _smoothness = Mathf.Clamp01(_smoothness - 0.05f);
        if (keyboard.rightBracketKey.wasPressedThisFrame)
            _smoothness = Mathf.Clamp01(_smoothness + 0.05f);
        if (keyboard.rKey.wasPressedThisFrame && _mold != null)
        {
            _mold.ResetShape();
            _status = "문어 몸통 형태를 초기화했습니다.";
        }

        if (mouse.leftButton.isPressed && _mold != null)
        {
            Ray ray = _camera.ScreenPointToRay(mouse.position.ReadValue());
            RaycastHit hit;
            if (_bodyCollider.Raycast(ray, out hit, 100f))
            {
                float drag = Mathf.Abs(mouseDelta.y) + Mathf.Abs(mouseDelta.x);
                float amount = mouse.leftButton.wasPressedThisFrame ? 0.075f : Mathf.Clamp(drag * 0.008f, 0.001f, 0.045f);
                bool pullIn = keyboard.leftShiftKey.isPressed;
                _mold.Sculpt(hit.point, hit.normal, pullIn ? -amount : amount);
                _status = pullIn ? "몸통을 안쪽으로 다듬는 중" : "몸통을 바깥쪽으로 늘리는 중";
            }
        }
    }

    private void UpdateOctopusMotion()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        float forward = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
        float side = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
        float rise = (keyboard.spaceKey.isPressed ? 1f : 0f) - (keyboard.leftCtrlKey.isPressed ? 1f : 0f);
        Vector3 planar = Quaternion.Euler(0f, _yaw, 0f) * new Vector3(side, 0f, forward);
        if (planar.sqrMagnitude > 1f)
            planar.Normalize();

        Vector3 targetForward = planar.sqrMagnitude > 0.01f ? planar : _player.forward;
        targetForward.y = 0f;
        if (targetForward.sqrMagnitude > 0.01f)
            _player.rotation = Quaternion.Slerp(_player.rotation, Quaternion.LookRotation(targetForward), Time.deltaTime * 8f);

        Vector3 velocity = planar * swimSpeed + Vector3.up * (rise * 2.3f);
        if (planar.sqrMagnitude < 0.01f && Mathf.Abs(rise) < 0.01f)
            velocity.y = Mathf.Sin(Time.time * 1.7f) * 0.18f;

        _controller.Move(velocity * Time.deltaTime);
        _body.localPosition = new Vector3(0f, 0.83f + Mathf.Sin(Time.time * 2.2f) * 0.035f, 0f);
    }

    private void UpdateCamera(float deltaTime)
    {
        if (_player == null)
            return;

        Quaternion orbit = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3 target = _player.position + Vector3.up * 0.9f;
        Vector3 desired = target + orbit * new Vector3(0f, 1.25f, -5.2f);
        _camera.transform.position = Vector3.Lerp(_camera.transform.position, desired, Mathf.Clamp01(deltaTime * 10f));
        _camera.transform.rotation = Quaternion.LookRotation(target - _camera.transform.position, Vector3.up);
        _camera.fieldOfView = 66f;
    }

    private void UpdateTentacles()
    {
        float swim = Mathf.Clamp01(new Vector2(_controller.velocity.x, _controller.velocity.z).magnitude / swimSpeed);
        for (int i = 0; i < 8; i++)
        {
            float angle = i * Mathf.PI * 2f / 8f;
            Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            Vector3 p0 = _player.position + Vector3.up * 0.75f + outward * 0.56f;
            float wave = Mathf.Sin(Time.time * (2.4f + swim * 2f) + i * 0.78f);
            Vector3 p3 = _player.position + outward * (1.55f + swim * 0.22f) + Vector3.up * (0.12f + wave * 0.1f);
            Vector3 p1 = Vector3.Lerp(p0, p3, 0.34f) + Vector3.up * (0.08f + wave * 0.1f);
            Vector3 p2 = Vector3.Lerp(p0, p3, 0.68f) + Vector3.up * (0.02f - wave * 0.07f);

            SetSegment(_tentacleSegments[i * 3], p0, p1, 0.18f);
            SetSegment(_tentacleSegments[i * 3 + 1], p1, p2, 0.145f);
            SetSegment(_tentacleSegments[i * 3 + 2], p2, p3, 0.105f);
        }
    }

    private static void SetSegment(Transform segment, Vector3 from, Vector3 to, float radius)
    {
        Vector3 difference = to - from;
        segment.position = (from + to) * 0.5f;
        segment.rotation = Quaternion.FromToRotation(Vector3.up, difference.normalized);
        segment.localScale = new Vector3(radius, difference.magnitude * 0.5f, radius);
    }

    private void SampleSeabedColor()
    {
        RaycastHit[] hits = Physics.RaycastAll(_player.position + Vector3.up * 0.15f, Vector3.down, 5f);
        System.Array.Sort(hits, delegate (RaycastHit a, RaycastHit b) { return a.distance.CompareTo(b.distance); });
        for (int i = 0; i < hits.Length; i++)
        {
            if (!_seabedColliders.Contains(hits[i].collider))
                continue;

            Renderer renderer = hits[i].collider.GetComponent<Renderer>();
            if (renderer == null)
                renderer = hits[i].collider.GetComponentInParent<Renderer>();
            if (renderer == null)
                continue;

            _sampledColor = renderer.sharedMaterial.color;
            return;
        }
    }

    private void BuildMaterials()
    {
        _rockMaterial = CreateLitMaterial(new Color(0.22f, 0.32f, 0.38f), 0f);
        _coralMaterial = CreateLitMaterial(new Color(0.91f, 0.42f, 0.33f), 0.16f);
        _plantMaterial = CreateLitMaterial(new Color(0.16f, 0.52f, 0.37f), 0.08f);
        for (int i = 0; i < SeabedPalette.Length; i++)
            _seabedMaterials.Add(CreateLitMaterial(SeabedPalette[i], 0f));
    }

    private void BuildSeabed()
    {
        GameObject bed = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bed.name = "Seabed - deep teal";
        bed.transform.position = new Vector3(0f, -0.22f, 0f);
        bed.transform.localScale = new Vector3(36f, 0.44f, 36f);
        bed.GetComponent<Renderer>().sharedMaterial = _seabedMaterials[0];
        _seabedColliders.Add(bed.GetComponent<Collider>());

        for (int z = -2; z <= 2; z++)
        {
            for (int x = -2; x <= 2; x++)
            {
                int paletteIndex = Mathf.Abs(x * 3 + z * 5) % _seabedMaterials.Count;
                GameObject patch = GameObject.CreatePrimitive(PrimitiveType.Cube);
                patch.name = "Seabed color patch";
                patch.transform.position = new Vector3(x * 6f, 0.006f, z * 6f);
                patch.transform.localScale = new Vector3(5.9f, 0.016f, 5.9f);
                patch.GetComponent<Renderer>().sharedMaterial = _seabedMaterials[paletteIndex];
                _seabedColliders.Add(patch.GetComponent<Collider>());
            }
        }
    }

    private void BuildScenery()
    {
        Random.State previousRandomState = Random.state;
        Random.InitState(31);

        for (int i = 0; i < 22; i++)
        {
            Vector2 circle = Random.insideUnitCircle * 14f;
            if (circle.magnitude < 3f)
                circle += circle.normalized * 5f;
            GameObject rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rock.name = "Rounded reef stone";
            rock.transform.position = new Vector3(circle.x, Random.Range(0.35f, 1f), circle.y);
            rock.transform.localScale = new Vector3(Random.Range(1.2f, 2.8f), Random.Range(0.6f, 1.4f), Random.Range(1.1f, 2.5f));
            rock.GetComponent<Renderer>().sharedMaterial = _rockMaterial;
        }

        for (int i = 0; i < 12; i++)
        {
            Vector2 circle = Random.insideUnitCircle * 13f;
            Vector3 basePosition = new Vector3(circle.x, 0.5f, circle.y);
            int stalkCount = Random.Range(3, 6);
            for (int stalk = 0; stalk < stalkCount; stalk++)
            {
                float angle = stalk * Mathf.PI * 2f / stalkCount;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * Random.Range(0.2f, 0.55f);
                GameObject coral = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                coral.name = "Soft coral stalk";
                coral.transform.position = basePosition + offset + Vector3.up * Random.Range(0f, 0.25f);
                coral.transform.localScale = new Vector3(0.32f, Random.Range(0.65f, 1.35f), 0.32f);
                coral.transform.rotation = Quaternion.Euler(Random.Range(-10f, 10f), Random.Range(0f, 360f), Random.Range(-12f, 12f));
                coral.GetComponent<Renderer>().sharedMaterial = Random.value > 0.45f ? _coralMaterial : _plantMaterial;
                Collider collider = coral.GetComponent<Collider>();
                if (collider != null)
                    Destroy(collider);
            }
        }

        Random.state = previousRandomState;
    }

    private void BuildOctopus()
    {
        _player = new GameObject("Octopus - third person").transform;
        _player.position = new Vector3(0f, 0.02f, 0f);
        _controller = _player.gameObject.AddComponent<CharacterController>();
        _controller.height = 1.4f;
        _controller.radius = 0.52f;
        _controller.center = new Vector3(0f, 0.7f, 0f);
        _controller.stepOffset = 0.25f;
        _controller.slopeLimit = 65f;

        GameObject body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        body.name = "Soft sculptable mantle";
        body.transform.SetParent(_player, false);
        body.transform.localPosition = new Vector3(0f, 0.83f, 0f);
        body.transform.localScale = new Vector3(1.12f, 0.76f, 1.06f);
        Collider defaultCollider = body.GetComponent<Collider>();
        if (defaultCollider != null)
            Destroy(defaultCollider);

        _body = body.transform;
        MeshFilter filter = body.GetComponent<MeshFilter>();
        Mesh mesh = filter.mesh;
        mesh.name = "Octopus mantle deformable mesh";
        Vector3[] vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++)
            vertices[i] *= 2f;
        mesh.vertices = vertices;
        mesh.RecalculateNormals();
        body.GetComponent<MeshRenderer>().sharedMaterial = CreateMoldMaterial();
        _bodyCollider = body.AddComponent<MeshCollider>();
        _bodyCollider.convex = false;
        _bodyCollider.sharedMesh = mesh;
        Physics.IgnoreCollision(_bodyCollider, _controller, true);
        _mold = body.AddComponent<OctopusMold>();
        _mold.Initialize(mesh);

        Material tentacleMaterial = CreateMoldMaterial();
        for (int i = 0; i < 8 * 3; i++)
        {
            GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            segment.name = "Waving tentacle segment";
            segment.transform.SetParent(_player, true);
            Collider collider = segment.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);
            segment.GetComponent<Renderer>().sharedMaterial = tentacleMaterial;
            _tentacleSegments.Add(segment.transform);
        }
    }

    private void ConfigureScene()
    {
        RenderSettings.fog = true;
        RenderSettings.fogColor = new Color(0.025f, 0.15f, 0.22f);
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = 0.013f;
        RenderSettings.ambientLight = new Color(0.22f, 0.39f, 0.47f);
        _camera.backgroundColor = new Color(0.015f, 0.10f, 0.17f);
        _camera.nearClipPlane = 0.08f;
        _camera.farClipPlane = 100f;

        Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i].type == LightType.Directional)
            {
                lights[i].color = new Color(0.52f, 0.84f, 1f);
                lights[i].intensity = 1.2f;
                lights[i].transform.rotation = Quaternion.Euler(42f, -32f, 0f);
            }
        }
    }

    private void OnGUI()
    {
        GUIStyle title = new GUIStyle(GUI.skin.label);
        title.fontSize = 24;
        title.fontStyle = FontStyle.Bold;
        title.normal.textColor = Color.white;
        GUIStyle body = new GUIStyle(GUI.skin.label);
        body.fontSize = 15;
        body.normal.textColor = new Color(0.85f, 0.95f, 1f);

        GUI.Box(new Rect(18f, 18f, 390f, 212f), GUIContent.none);
        GUI.Label(new Rect(34f, 30f, 360f, 34f), "OCTOPUS DREAM  /  SHAPE LAB", title);
        GUI.Label(new Rect(34f, 70f, 360f, 26f), "조형 부드러움   " + Mathf.RoundToInt(_smoothness * 100f) + "%", body);
        GUI.Label(new Rect(34f, 96f, 360f, 26f), "바닥에서 읽은 색상", body);
        GUI.color = _sampledColor;
        GUI.DrawTexture(new Rect(196f, 101f, 28f, 15f), Texture2D.whiteTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(34f, 127f, 355f, 26f), _status, body);
        GUI.Label(new Rect(34f, 161f, 355f, 24f), "WASD 이동  ·  Space/Ctrl 상승·하강", body);
        GUI.Label(new Rect(34f, 185f, 355f, 24f), "우클릭 드래그 시야  ·  좌클릭 드래그 조형  ·  Shift 당기기", body);

        GUI.Box(new Rect(18f, Screen.height - 54f, 470f, 36f), GUIContent.none);
        GUI.Label(new Rect(32f, Screen.height - 48f, 448f, 24f), "휠 / [ ] 부드러움 조절     R 형태 초기화", body);
    }

    private static Material CreateLitMaterial(Color color, float emission)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        Material material = new Material(shader);
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
        if (emission > 0f && material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * emission);
        }
        return material;
    }

    private static Material CreateMoldMaterial()
    {
        Shader shader = Shader.Find("Octopusdream/VertexColorLit");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Lit");
        Material material = new Material(shader);
        Color baseColor = new Color(0.86f, 0.47f, 0.63f);
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", baseColor);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", baseColor);
        return material;
    }

}
