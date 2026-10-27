using UnityEngine;

/// <summary>Spring-smoothed vertex sculpting and seabed tint for the octopus mantle.</summary>
public sealed class OctopusMold : MonoBehaviour
{
    private Mesh _mesh;
    private MeshCollider _collider;
    private Vector3[] _targets;
    private Vector3[] _positions;
    private Vector3[] _velocities;
    private Vector3[] _original;
    private Color[] _colors;
    private bool _colliderDirty;
    private readonly Color _baseColor = new Color(0.86f, 0.47f, 0.63f);
    private Color _sampleColor = new Color(0.07f, 0.31f, 0.37f);

    public float Smoothness { get; set; }
    public Color SampleColor
    {
        get { return _sampleColor; }
        set { _sampleColor = value; }
    }

    public void Initialize(Mesh mesh)
    {
        _mesh = mesh;
        _collider = GetComponent<MeshCollider>();
        _original = mesh.vertices;
        _targets = (Vector3[])_original.Clone();
        _positions = (Vector3[])_original.Clone();
        _velocities = new Vector3[_original.Length];
        _colors = new Color[_original.Length];
    }

    public void Sculpt(Vector3 worldPoint, Vector3 worldNormal, float amount)
    {
        Vector3 localPoint = transform.InverseTransformPoint(worldPoint);
        Vector3 localNormal = transform.localToWorldMatrix.transpose.MultiplyVector(worldNormal).normalized;
        float radius = Mathf.Lerp(0.23f, 0.72f, Smoothness);

        for (int i = 0; i < _targets.Length; i++)
        {
            float distance = Vector3.Distance(_targets[i], localPoint);
            if (distance >= radius)
                continue;

            float influence = 1f - distance / radius;
            influence = influence * influence * (3f - 2f * influence);
            Vector3 candidate = _targets[i] + localNormal * amount * influence;
            float magnitude = Mathf.Clamp(candidate.magnitude, 0.58f, 1.55f);
            _targets[i] = candidate.normalized * magnitude;
        }

        _colliderDirty = true;
    }

    public void ResetShape()
    {
        for (int i = 0; i < _targets.Length; i++)
            _targets[i] = _original[i];
        _colliderDirty = true;
    }

    public void Tick(float deltaTime)
    {
        float spring = Mathf.Lerp(92f, 26f, Smoothness);
        float damping = Mathf.Lerp(7f, 13f, Smoothness);
        float dt = Mathf.Min(deltaTime, 0.04f);
        for (int i = 0; i < _positions.Length; i++)
        {
            Vector3 acceleration = (_targets[i] - _positions[i]) * spring - _velocities[i] * damping;
            _velocities[i] += acceleration * dt;
            _positions[i] += _velocities[i] * dt;

            float underside = Mathf.Clamp01((0.2f - _positions[i].y) * 1.5f);
            underside = underside * underside * (3f - 2f * underside);
            _colors[i] = Color.Lerp(_baseColor, _sampleColor, underside * 0.9f);
        }

        _mesh.vertices = _positions;
        _mesh.colors = _colors;
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
        if (_colliderDirty)
        {
            _collider.sharedMesh = null;
            _collider.sharedMesh = _mesh;
            _colliderDirty = false;
        }
    }
}
