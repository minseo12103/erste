using UnityEngine;
using UnityEngine.Rendering;

namespace Octopusdream
{
    /// <summary>
    /// Samples the hit surface's albedo (texture multiplied by material tint), independent
    /// of scene lighting. Original texture import settings are never changed.
    /// </summary>
    public static class SurfaceColorSampler
    {
        private static Texture2D _singlePixel;
        private static readonly string[] TextureProperties = { "_BaseMap", "_MainTex", "_BaseColorMap" };

        public static bool TrySample(RaycastHit hit, out Color color)
        {
            color = Color.white;
            if (hit.collider == null)
                return false;

            Renderer renderer = hit.collider.GetComponent<Renderer>();
            if (renderer == null)
                renderer = hit.collider.GetComponentInParent<Renderer>();
            if (renderer == null)
                return false;
            Material[] materials = renderer.sharedMaterials;
            if (materials == null || materials.Length == 0)
                return false;
            int materialIndex = FindMaterialIndex(hit);
            Material material = materials[Mathf.Clamp(materialIndex, 0, materials.Length - 1)];
            if (material == null)
                return false;

            Color tint = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") :
                material.HasProperty("_Color") ? material.GetColor("_Color") : Color.white;
            Texture texture = null;
            string textureProperty = null;
            for (int i = 0; i < TextureProperties.Length; i++)
            {
                string property = TextureProperties[i];
                if (!material.HasProperty(property))
                    continue;
                texture = material.GetTexture(property);
                if (texture != null)
                {
                    textureProperty = property;
                    break;
                }
            }

            color = tint;
            color.a = 1f;
            if (texture == null)
                return true;

            Vector2 uv = hit.textureCoord;
            if (!(hit.collider is MeshCollider))
            {
                // MeshCollider UVs are exact. For a simple box surface, use its dominant face.
                Bounds bounds = renderer.localBounds;
                Vector3 local = renderer.transform.InverseTransformPoint(hit.point) - bounds.min;
                Vector3 normal = renderer.transform.InverseTransformDirection(hit.normal);
                Vector3 size = bounds.size;
                if (Mathf.Abs(normal.y) >= Mathf.Abs(normal.x) && Mathf.Abs(normal.y) >= Mathf.Abs(normal.z))
                    uv = new Vector2(local.x / Mathf.Max(size.x, 0.001f), local.z / Mathf.Max(size.z, 0.001f));
                else if (Mathf.Abs(normal.x) > Mathf.Abs(normal.z))
                    uv = new Vector2(local.z / Mathf.Max(size.z, 0.001f), local.y / Mathf.Max(size.y, 0.001f));
                else
                    uv = new Vector2(local.x / Mathf.Max(size.x, 0.001f), local.y / Mathf.Max(size.y, 0.001f));
            }
            uv = Vector2.Scale(uv, material.GetTextureScale(textureProperty)) + material.GetTextureOffset(textureProperty);
            Color sample;
            if (TryReadTexture(texture, uv, out sample))
            {
                color = sample * tint;
                color.a = 1f;
                return true;
            }
            return false;
        }

        private static int FindMaterialIndex(RaycastHit hit)
        {
            MeshCollider collider = hit.collider as MeshCollider;
            if (collider == null || collider.sharedMesh == null || hit.triangleIndex < 0)
                return 0;
            Mesh mesh = collider.sharedMesh;
            int triangle = hit.triangleIndex;
            for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                if (mesh.GetTopology(subMesh) != MeshTopology.Triangles)
                    continue;
                int count = (int)mesh.GetIndexCount(subMesh) / 3;
                if (triangle < count)
                    return subMesh;
                triangle -= count;
            }
            return 0;
        }

        private static bool TryReadTexture(Texture texture, Vector2 uv, out Color color)
        {
            color = Color.white;
            if (float.IsNaN(uv.x) || float.IsNaN(uv.y) || float.IsInfinity(uv.x) || float.IsInfinity(uv.y))
                return false;
            uv.x = Wrap(uv.x, texture.wrapModeU);
            uv.y = Wrap(uv.y, texture.wrapModeV);
            Texture2D readable = texture as Texture2D;
            if (readable != null && readable.isReadable)
            {
                color = readable.GetPixelBilinear(uv.x, uv.y);
                return true;
            }

            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                return false;

            RenderTexture previousTarget = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            RenderTexture temporary = null;
            try
            {
                if (_singlePixel == null)
                    _singlePixel = new Texture2D(1, 1, TextureFormat.RGBA32, false, false)
                    {
                        name = "Surface color readback",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                temporary = RenderTexture.GetTemporary(1, 1, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                GL.sRGBWrite = QualitySettings.activeColorSpace == ColorSpace.Linear;
                Graphics.Blit(texture, temporary, Vector2.zero, uv);
                RenderTexture.active = temporary;
                _singlePixel.ReadPixels(new Rect(0f, 0f, 1f, 1f), 0, 0, false);
                _singlePixel.Apply(false, false);
                color = _singlePixel.GetPixel(0, 0);
                return true;
            }
            catch (UnityException)
            {
                return false;
            }
            finally
            {
                RenderTexture.active = previousTarget;
                GL.sRGBWrite = previousSrgb;
                if (temporary != null)
                    RenderTexture.ReleaseTemporary(temporary);
            }
        }

        private static float Wrap(float value, TextureWrapMode mode)
        {
            switch (mode)
            {
                case TextureWrapMode.Clamp: return Mathf.Clamp01(value);
                case TextureWrapMode.Mirror: return Mathf.PingPong(value, 1f);
                case TextureWrapMode.MirrorOnce: return Mathf.Clamp01(Mathf.Abs(value));
                default: return Mathf.Repeat(value, 1f);
            }
        }

        public static void ReleaseResources()
        {
            if (_singlePixel == null)
                return;
            if (Application.isPlaying)
                Object.Destroy(_singlePixel);
            else
                Object.DestroyImmediate(_singlePixel);
            _singlePixel = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetResources() => ReleaseResources();
    }
}
