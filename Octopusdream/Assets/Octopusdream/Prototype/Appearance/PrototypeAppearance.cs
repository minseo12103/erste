using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Octopusdream
{
    /// <summary>
    /// Replaceable placeholder visuals. Collision and movement belong to the player root;
    /// SOFTNESS deforms this child's painted shell; paint and harpoon rays use that shell.
    /// </summary>
    public sealed class PrototypeAppearance : MonoBehaviour
    {
        public const float OctopusRadius = 0.65f;
        public const int PaintWidth = 256;
        public const int PaintHeight = 128;
        public const int MaximumPaintBytes = 128 * 1024;
        private const int LongitudeSegments = 64;
        private const int LatitudeSegments = 32;

        private Mesh _sphereMesh;
        private Mesh _cylinderMesh;
        private MeshRenderer _sphereRenderer;
        private MeshRenderer _cylinderRenderer;
        private Material _sphereMaterial;
        private Material _cylinderMaterial;
        private Texture2D _paintTexture;
        private Color32[] _paintPixels;
        private Vector3[] _pixelDirections;
        private Vector3[] _restVertices;
        private Vector3[] _renderVertices;
        private Vector3[] _renderNormals;
        private int[] _triangles;
        private readonly PrototypeSurfaceBody _body = new PrototypeSurfaceBody();
        private readonly List<Vector3> _normalBuffer = new List<Vector3>();
        private bool _initialized;
        private bool _visible = true;
        private bool _highlighted;
        private bool _paintDirty;
        private string _cachedPaint;

        public bool IsDiver { get; private set; }
        public float Softness { get; private set; } = 5f;
        public Texture2D PaintTexture => _paintTexture;
        public Bounds WorldBounds => !_initialized ? new Bounds(transform.position, Vector3.one * OctopusRadius * 2f) :
            (IsDiver ? _cylinderRenderer.bounds : _sphereRenderer.bounds);
        public float SurfaceContact => _body.Contact;
        public Vector3 FocusPoint
        {
            get
            {
                Bounds bounds = WorldBounds;
                Vector3 point = bounds.center;
                if (!IsDiver)
                {
                    float h = Softness <= 5f ? Mathf.Lerp(1.3f, .98f, Softness / 5f) : Mathf.Lerp(.98f, .14f, (Softness - 5f) / 5f);
                    // A hanging rim can put the AABB center inside the stair. Aim above
                    // the supporting surface, at the thick part of the visible body.
                    point.y = Mathf.Max(point.y, bounds.max.y - Mathf.Lerp(OctopusRadius, h * .5f, SurfaceContact));
                }
                return point;
            }
        }

        public void Initialize(bool diver)
        {
            EnsureBuilt();
            SetRole(diver);
        }

        private void EnsureBuilt()
        {
            if (_initialized)
                return;
            _initialized = true;

            Shader shader = Resources.Load<Shader>("OctopusPrototypePaint");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new InvalidOperationException("Octopus prototype paint shader was not included in the build.");

            _paintTexture = new Texture2D(PaintWidth, PaintHeight, TextureFormat.RGBA32, false, false)
            {
                name = "Octopus paint canvas",
                wrapModeU = TextureWrapMode.Repeat,
                wrapModeV = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };
            _paintPixels = new Color32[PaintWidth * PaintHeight];
            _pixelDirections = new Vector3[_paintPixels.Length];
            for (int y = 0; y < PaintHeight; y++)
            {
                float theta = (1f - (y + 0.5f) / PaintHeight) * Mathf.PI;
                for (int x = 0; x < PaintWidth; x++)
                {
                    float phi = (x + 0.5f) / PaintWidth * Mathf.PI * 2f;
                    _pixelDirections[y * PaintWidth + x] = new Vector3(
                        Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                }
            }

            _sphereMaterial = new Material(shader) { name = "Octopus paint material" };
            _sphereMaterial.SetColor("_BaseColor", Color.white);
            _sphereMaterial.SetTexture("_BaseMap", _paintTexture);
            _cylinderMaterial = new Material(shader) { name = "Diver placeholder material" };
            _cylinderMaterial.SetColor("_BaseColor", new Color(0.15f, 0.36f, 0.42f, 1f));
            _cylinderMaterial.SetTexture("_BaseMap", Texture2D.whiteTexture);

            _sphereMesh = BuildSphere();
            _sphereMesh.MarkDynamic();
            _restVertices = _sphereMesh.vertices;
            _triangles = _sphereMesh.triangles;
            _renderVertices = (Vector3[])_restVertices.Clone();
            _renderNormals = _sphereMesh.normals;
            _sphereRenderer = CreateVisual("Octopus sphere", _sphereMesh, _sphereMaterial);
            _cylinderMesh = BuildCylinder();
            _cylinderRenderer = CreateVisual("Diver cylinder", _cylinderMesh, _cylinderMaterial);
            SetBaseColor(new Color(0.84f, 0.40f, 0.59f, 1f));
        }

        private MeshRenderer CreateVisual(string visualName, Mesh mesh, Material material)
        {
            GameObject visual = new GameObject(visualName);
            visual.transform.SetParent(transform, false);
            visual.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return renderer;
        }

        public void SetRole(bool diver)
        {
            EnsureBuilt();
            bool changed = IsDiver != diver;
            IsDiver = diver;
            _sphereRenderer.gameObject.SetActive(!diver);
            _cylinderRenderer.gameObject.SetActive(diver);
            SetVisible(_visible);
            SetHighlighted(_highlighted);
            if (changed)
                _body.Reset();
        }

        public void SetSoftness(float value)
        {
            float previous = Softness;
            Softness = float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp(value, 0f, 10f);
            if (Softness <= 0f && _initialized && previous > 0f)
            {
                _body.Reset();
                Array.Copy(_restVertices, _renderVertices, _restVertices.Length);
                for (int i = 0; i < _restVertices.Length; i++)
                    _renderNormals[i] = _restVertices[i].normalized;
                _sphereMesh.vertices = _renderVertices;
                _sphereMesh.normals = _renderNormals;
                _sphereMesh.RecalculateBounds();
            }
        }

        public void SetBaseColor(Color color)
        {
            EnsureBuilt();
            Color32 clean = SanitizeColor(color);
            for (int i = 0; i < _paintPixels.Length; i++)
                _paintPixels[i] = clean;
            ApplyPaintPixels();
        }

        /// <summary>Paint a soft circular brush. Radius is angular distance in radians.</summary>
        public void Paint(Vector3 localDirection, Color color, float radius)
        {
            EnsureBuilt();
            if (!Finite(localDirection) || localDirection.sqrMagnitude < 0.0001f || float.IsNaN(radius))
                return;
            Vector3 direction = localDirection.normalized;
            radius = Mathf.Clamp(radius, 0.025f, 1.2f);
            float cosine = Mathf.Cos(radius);
            float denominator = Mathf.Max(0.0001f, 1f - cosine);
            Color32 clean = SanitizeColor(color);
            bool changed = false;
            for (int i = 0; i < _paintPixels.Length; i++)
            {
                float dot = Vector3.Dot(direction, _pixelDirections[i]);
                if (dot < cosine)
                    continue;
                float weight = Mathf.Clamp01((dot - cosine) / denominator * 2f);
                weight = weight * weight * (3f - 2f * weight);
                _paintPixels[i] = Color32.Lerp(_paintPixels[i], clean, weight);
                changed = true;
            }
            if (changed)
                ApplyPaintPixels();
        }

        private void ApplyPaintPixels()
        {
            _paintTexture.SetPixels32(_paintPixels);
            _paintTexture.Apply(false, false);
            _paintDirty = true;
        }

        /// <summary>Paint follows the actual deformed shell while its original UVs stay anchored.</summary>
        public bool TryGetPaintDirection(Ray worldRay, out Vector3 localDirection)
        {
            if (!TryRaycastSurface(worldRay, out float distance, out localDirection)) return false;
            return !Physics.Raycast(worldRay, Mathf.Max(0f, distance - .003f), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        }

        public bool TryRaycastSurface(Ray worldRay, out float worldDistance, out Vector3 paintDirection)
        {
            worldDistance = float.PositiveInfinity;
            paintDirection = Vector3.zero;
            if (!_initialized || IsDiver || !Finite(worldRay.origin) || !Finite(worldRay.direction) || worldRay.direction.sqrMagnitude < .000001f) return false;
            Vector3 origin = transform.InverseTransformPoint(worldRay.origin);
            Vector3 direction = transform.InverseTransformVector(worldRay.direction.normalized);
            if (!_sphereMesh.bounds.IntersectRay(new Ray(origin, direction))) return false;
            for (int i = 0; i < _triangles.Length; i += 3)
            {
                int a = _triangles[i], b = _triangles[i + 1], c = _triangles[i + 2];
                Vector3 edge1 = _renderVertices[b] - _renderVertices[a], edge2 = _renderVertices[c] - _renderVertices[a];
                Vector3 cross = Vector3.Cross(direction, edge2);
                float determinant = Vector3.Dot(edge1, cross);
                if (Mathf.Abs(determinant) < .0000001f) continue;
                float inverse = 1f / determinant;
                Vector3 offset = origin - _renderVertices[a];
                float u = Vector3.Dot(offset, cross) * inverse;
                if (u < 0f || u > 1f) continue;
                Vector3 q = Vector3.Cross(offset, edge1);
                float v = Vector3.Dot(direction, q) * inverse;
                if (v < 0f || u + v > 1f) continue;
                float distance = Vector3.Dot(edge2, q) * inverse;
                if (distance < 0f || distance >= worldDistance) continue;
                worldDistance = distance;
                paintDirection = (_restVertices[a] * (1f - u - v) + _restVertices[b] * u + _restVertices[c] * v).normalized;
            }
            return !float.IsPositiveInfinity(worldDistance);
        }

        public string ExportPaint()
        {
            EnsureBuilt();
            if (!_paintDirty && _cachedPaint != null)
                return _cachedPaint;
            byte[] png = _paintTexture.EncodeToPNG();
            if (png == null || png.Length > MaximumPaintBytes)
                return string.Empty;
            _cachedPaint = Convert.ToBase64String(png);
            _paintDirty = false;
            return _cachedPaint;
        }

        public bool ImportPaint(string base64Png)
        {
            EnsureBuilt();
            if (string.IsNullOrEmpty(base64Png) || base64Png.Length > ((MaximumPaintBytes + 2) / 3) * 4)
                return false;
            byte[] bytes;
            try { bytes = Convert.FromBase64String(base64Png); }
            catch (FormatException) { return false; }
            if (!ValidPngHeader(bytes))
                return false;
            Texture2D incoming = new Texture2D(2, 2, TextureFormat.RGBA32, false, false);
            try
            {
                if (!incoming.LoadImage(bytes, false) || incoming.width != PaintWidth || incoming.height != PaintHeight)
                    return false;
                _paintPixels = incoming.GetPixels32();
                _paintTexture.SetPixels32(_paintPixels);
                _paintTexture.Apply(false, false);
                _cachedPaint = base64Png;
                _paintDirty = false;
                return true;
            }
            catch (Exception exception) when (exception is UnityException || exception is ArgumentException)
            {
                return false;
            }
            finally
            {
                Destroy(incoming);
            }
        }

        private static bool ValidPngHeader(byte[] png)
        {
            if (png == null || png.Length < 33 || png.Length > MaximumPaintBytes)
                return false;
            if (png[0] != 137 || png[1] != 80 || png[2] != 78 || png[3] != 71 ||
                png[4] != 13 || png[5] != 10 || png[6] != 26 || png[7] != 10 ||
                png[12] != 73 || png[13] != 72 || png[14] != 68 || png[15] != 82)
                return false;
            uint width = ((uint)png[16] << 24) | ((uint)png[17] << 16) | ((uint)png[18] << 8) | png[19];
            uint height = ((uint)png[20] << 24) | ((uint)png[21] << 16) | ((uint)png[22] << 8) | png[23];
            return width == PaintWidth && height == PaintHeight;
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (!_initialized)
                return;
            _sphereRenderer.forceRenderingOff = !visible;
            _cylinderRenderer.forceRenderingOff = !visible;
        }

        public void SetHighlighted(bool highlight)
        {
            _highlighted = highlight;
            if (!_initialized)
                return;
            if (_sphereMaterial.HasProperty("_Highlight"))
                _sphereMaterial.SetFloat("_Highlight", highlight ? 1f : 0f);
            if (_cylinderMaterial.HasProperty("_Highlight"))
                _cylinderMaterial.SetFloat("_Highlight", highlight ? 1f : 0f);
        }

        public void Animate(Vector3 worldVelocity)
        {
            Simulate(worldVelocity, Time.deltaTime);
        }

        public void Simulate(Vector3 worldVelocity, float deltaTime)
        {
            if (!_initialized || IsDiver || !Finite(worldVelocity)) return;
            _body.Step(transform, _restVertices, _renderVertices, Softness, worldVelocity, deltaTime);
            _sphereMesh.vertices = _renderVertices;
            _sphereMesh.RecalculateBounds();
            _sphereMesh.RecalculateNormals();
            _sphereMesh.GetNormals(_normalBuffer);
            // The UV seam and poles are duplicated, but must shade as one closed shell.
            int columns = LongitudeSegments + 1;
            for (int row = 1; row < LatitudeSegments; row++)
            {
                int first = row * columns, last = first + LongitudeSegments;
                Vector3 normal = (_normalBuffer[first] + _normalBuffer[last]).normalized;
                _normalBuffer[first] = _normalBuffer[last] = normal;
            }
            for (int pole = 0; pole < 2; pole++)
            {
                int start = pole == 0 ? 0 : LatitudeSegments * columns;
                Vector3 normal = Vector3.zero;
                for (int i = 0; i < columns; i++) normal += _normalBuffer[start + i];
                normal.Normalize();
                for (int i = 0; i < columns; i++) _normalBuffer[start + i] = normal;
            }
            _sphereMesh.SetNormals(_normalBuffer);
        }

        public void Impact(Vector3 worldNormal, float strength)
        {
            if (!Finite(worldNormal) || float.IsNaN(strength) || IsDiver)
                return;
            _body.Impact(transform, worldNormal, strength, Softness);
        }

        private static Color32 SanitizeColor(Color color)
        {
            return new Color(Channel(color.r), Channel(color.g), Channel(color.b), 1f);
        }

        private static float Channel(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp01(value);
        private static bool Finite(Vector3 vector) => !float.IsNaN(vector.x) && !float.IsNaN(vector.y) && !float.IsNaN(vector.z) &&
            !float.IsInfinity(vector.x) && !float.IsInfinity(vector.y) && !float.IsInfinity(vector.z);

        private static Mesh BuildSphere()
        {
            int columns = LongitudeSegments + 1;
            Vector3[] vertices = new Vector3[(LatitudeSegments + 1) * columns];
            Vector3[] normals = new Vector3[vertices.Length];
            Vector2[] uv = new Vector2[vertices.Length];
            int[] triangles = new int[LatitudeSegments * LongitudeSegments * 6];
            for (int latitude = 0; latitude <= LatitudeSegments; latitude++)
            {
                float theta = latitude / (float)LatitudeSegments * Mathf.PI;
                for (int longitude = 0; longitude <= LongitudeSegments; longitude++)
                {
                    float phi = longitude / (float)LongitudeSegments * Mathf.PI * 2f;
                    int index = latitude * columns + longitude;
                    normals[index] = new Vector3(Mathf.Sin(theta) * Mathf.Cos(phi), Mathf.Cos(theta), Mathf.Sin(theta) * Mathf.Sin(phi));
                    vertices[index] = normals[index] * OctopusRadius;
                    uv[index] = new Vector2(longitude / (float)LongitudeSegments, 1f - latitude / (float)LatitudeSegments);
                }
            }
            int triangle = 0;
            for (int latitude = 0; latitude < LatitudeSegments; latitude++)
                for (int longitude = 0; longitude < LongitudeSegments; longitude++)
                {
                    int a = latitude * columns + longitude;
                    int b = a + 1;
                    int c = a + columns;
                    int d = c + 1;
                    triangles[triangle++] = a; triangles[triangle++] = b; triangles[triangle++] = c;
                    triangles[triangle++] = b; triangles[triangle++] = d; triangles[triangle++] = c;
                }
            return new Mesh { name = "Prototype paint sphere", vertices = vertices, normals = normals, uv = uv,
                triangles = triangles, bounds = new Bounds(Vector3.zero, Vector3.one * 2.1f) };
        }

        private static Mesh BuildCylinder()
        {
            const int count = 40;
            const float radius = 0.5f;
            const float halfHeight = 0.8f;
            Vector3[] vertices = new Vector3[(count + 1) * 4 + 2];
            Vector3[] normals = new Vector3[vertices.Length];
            Vector2[] uv = new Vector2[vertices.Length];
            int[] triangles = new int[count * 12];
            int bottomCenter = vertices.Length - 2;
            int topCenter = vertices.Length - 1;
            vertices[bottomCenter] = Vector3.down * halfHeight;
            vertices[topCenter] = Vector3.up * halfHeight;
            normals[bottomCenter] = Vector3.down;
            normals[topCenter] = Vector3.up;
            uv[bottomCenter] = uv[topCenter] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i <= count; i++)
            {
                float angle = i / (float)count * Mathf.PI * 2f;
                Vector3 radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                int b = i * 4;
                vertices[b] = vertices[b + 2] = radial * radius + Vector3.down * halfHeight;
                vertices[b + 1] = vertices[b + 3] = radial * radius + Vector3.up * halfHeight;
                normals[b] = normals[b + 1] = radial;
                normals[b + 2] = Vector3.down;
                normals[b + 3] = Vector3.up;
                uv[b] = new Vector2(i / (float)count, 0f);
                uv[b + 1] = new Vector2(i / (float)count, 1f);
                uv[b + 2] = uv[b + 3] = new Vector2(radial.x * 0.5f + 0.5f, radial.z * 0.5f + 0.5f);
            }
            int triangle = 0;
            for (int i = 0; i < count; i++)
            {
                int a = i * 4;
                int b = (i + 1) * 4;
                triangles[triangle++] = a; triangles[triangle++] = a + 1; triangles[triangle++] = b;
                triangles[triangle++] = b; triangles[triangle++] = a + 1; triangles[triangle++] = b + 1;
                triangles[triangle++] = bottomCenter; triangles[triangle++] = a + 2; triangles[triangle++] = b + 2;
                triangles[triangle++] = topCenter; triangles[triangle++] = b + 3; triangles[triangle++] = a + 3;
            }
            return new Mesh { name = "Prototype diver cylinder", vertices = vertices, normals = normals, uv = uv,
                triangles = triangles, bounds = new Bounds(Vector3.zero, new Vector3(1f, 1.6f, 1f)) };
        }

        private void OnDestroy()
        {
            if (_sphereMesh != null) Destroy(_sphereMesh);
            if (_cylinderMesh != null) Destroy(_cylinderMesh);
            if (_sphereMaterial != null) Destroy(_sphereMaterial);
            if (_cylinderMaterial != null) Destroy(_cylinderMaterial);
            if (_paintTexture != null) Destroy(_paintTexture);
            SurfaceColorSampler.ReleaseResources();
        }
    }
}
