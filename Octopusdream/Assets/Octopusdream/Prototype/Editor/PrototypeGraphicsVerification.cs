using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Octopusdream.Editor
{
    /// <summary>
    /// A real-GPU check for surface-color readback. Run with -batchmode and a graphics
    /// device, without -nographics. No Play Mode or saved-scene changes are required.
    /// </summary>
    public static class PrototypeGraphicsVerification
    {
        private const float ColorTolerance = .02f;

        [MenuItem("Octopusdream/Run Graphics Verification")]
        public static void Run()
        {
            var started = DateTime.UtcNow;
            var report = new GraphicsReport
            {
                utc = started.ToString("O"),
                unityVersion = Application.unityVersion,
                graphicsDeviceType = SystemInfo.graphicsDeviceType.ToString(),
                graphicsDeviceName = SystemInfo.graphicsDeviceName,
                colorSpace = QualitySettings.activeColorSpace.ToString(),
                tolerance = ColorTolerance
            };
            var errors = new List<string>();
            Application.LogCallback log = (message, trace, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    errors.Add(message + "\n" + trace);
            };
            Application.logMessageReceived += log;
            try
            {
                Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Start graphics verification from Edit Mode.");
                Require(SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null,
                    "A real graphics device is required. Omit -nographics from the Unity invocation.");
                VerifyUnreadableSurface(report);
                Require(errors.Count == 0, "Unity reported an error during GPU verification.");
                report.passed = true;
            }
            catch (Exception exception)
            {
                report.error = exception.ToString();
            }
            finally
            {
                Application.logMessageReceived -= log;
                report.runtimeErrors = errors.ToArray();
                report.durationSeconds = (float)(DateTime.UtcNow - started).TotalSeconds;
            }

            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "Prototype", "graphics.json"));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, JsonUtility.ToJson(report, true));
                if (report.passed)
                    Debug.Log("OCTOPUS_GRAPHICS_VERIFY_PASS: Unreadable texture GPU samples match readable albedo, UV transform and tint. Report: " + path);
                else
                    Debug.LogError("OCTOPUS_GRAPHICS_VERIFY_FAIL: " + report.error + "\nReport: " + path);
            }
            catch (Exception exception)
            {
                report.passed = false;
                Debug.LogError("Could not save GPU verification report: " + exception);
            }

            if (Application.isBatchMode)
                EditorApplication.Exit(report.passed ? 0 : 1);
        }

        private static void VerifyUnreadableSurface(GraphicsReport report)
        {
            GameObject fixture = null;
            Mesh mesh = null;
            Texture2D texture = null;
            Material material = null;
            RenderTexture sentinel = null;
            RenderTexture previousTarget = RenderTexture.active;
            bool previousSrgb = GL.sRGBWrite;
            try
            {
                Shader shader = Resources.Load<Shader>("OctopusPrototypePaint");
                Require(shader != null, "The included prototype paint shader could not be loaded.");
                Require(shader.isSupported, "The prototype paint shader is unsupported on this graphics device.");

                fixture = new GameObject("Temporary GPU surface sampling fixture") { hideFlags = HideFlags.HideAndDontSave };
                mesh = new Mesh { name = "Temporary surface sampling quad", hideFlags = HideFlags.HideAndDontSave };
                mesh.vertices = new[]
                {
                    new Vector3(-1f, 0f, -1f), new Vector3(-1f, 0f, 1f),
                    new Vector3(1f, 0f, 1f), new Vector3(1f, 0f, -1f)
                };
                mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
                mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                fixture.AddComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = fixture.AddComponent<MeshRenderer>();
                var collider = fixture.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;

                var quadrantColors = new[]
                {
                    new Color(.20f, .40f, .60f, 1f), new Color(.80f, .30f, .10f, 1f),
                    new Color(.12f, .70f, .38f, 1f), new Color(.62f, .18f, .77f, 1f)
                };
                texture = new Texture2D(16, 16, TextureFormat.RGBA32, false, false)
                {
                    name = "Temporary unreadable albedo fixture",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Repeat
                };
                var pixels = new Color[16 * 16];
                for (int y = 0; y < 16; y++)
                    for (int x = 0; x < 16; x++)
                        pixels[y * 16 + x] = quadrantColors[(y >= 8 ? 2 : 0) + (x >= 8 ? 1 : 0)];
                texture.SetPixels(pixels);
                texture.Apply(false, false);

                Color tint = new Color(.73f, .59f, .91f, .4f);
                material = new Material(shader) { name = "Temporary UV transform and tint fixture", hideFlags = HideFlags.HideAndDontSave };
                material.SetTexture("_BaseMap", texture);
                material.SetColor("_BaseColor", tint);
                // Reflect X and translate outside 0..1. Ignoring the transform or repeat
                // addressing samples the wrong quadrant, so both are actually exercised.
                Vector2 scale = new Vector2(-.75f, .75f);
                Vector2 offset = new Vector2(1.875f, -.875f);
                material.SetTextureScale("_BaseMap", scale);
                material.SetTextureOffset("_BaseMap", offset);
                renderer.sharedMaterial = material;
                Physics.SyncTransforms();

                var sourceUvs = new[]
                {
                    new Vector2(.125f, .25f), new Vector2(.875f, .25f),
                    new Vector2(.125f, .75f), new Vector2(.875f, .75f)
                };
                var hits = new RaycastHit[sourceUvs.Length];
                report.samples = new SampleResult[sourceUvs.Length];
                for (int index = 0; index < sourceUvs.Length; index++)
                {
                    Vector2 uv = sourceUvs[index];
                    Ray ray = new Ray(new Vector3(uv.x * 2f - 1f, 2f, uv.y * 2f - 1f), Vector3.down);
                    Require(collider.Raycast(ray, out hits[index], 4f), "The fixture ray did not hit its mesh collider.");
                    Require(Vector2.Distance(hits[index].textureCoord, uv) < .001f, "The fixture mesh UV does not match the expected sample point.");
                    Color expected = quadrantColors[index ^ 1] * tint;
                    expected.a = 1f;
                    Color readable;
                    Require(SurfaceColorSampler.TrySample(hits[index], out readable), "Readable baseline sampling failed.");
                    Require(MaxRgbError(readable, expected) < .005f, "Readable sample did not apply the expected UV transform, repeat addressing and tint.");
                    Vector2 transformed = Vector2.Scale(uv, scale) + offset;
                    report.samples[index] = new SampleResult
                    {
                        name = "quadrant_" + index,
                        hitUv = hits[index].textureCoord,
                        transformedUv = transformed,
                        expected = expected,
                        readable = readable
                    };
                }

                texture.Apply(false, true);
                Require(!texture.isReadable, "The test texture must be unreadable before GPU sampling.");
                report.textureWasUnreadable = true;

                // The sampler must restore the caller's active render target and sRGB state.
                sentinel = RenderTexture.GetTemporary(2, 2, 0, RenderTextureFormat.ARGB32);
                RenderTexture.active = sentinel;
                bool sentinelSrgb = GL.sRGBWrite;
                for (int index = 0; index < hits.Length; index++)
                {
                    Color unreadable;
                    Require(SurfaceColorSampler.TrySample(hits[index], out unreadable), "Unreadable GPU sampling failed for sample " + index + ".");
                    var sample = report.samples[index];
                    sample.unreadable = unreadable;
                    sample.maximumRgbError = MaxRgbError(sample.readable, unreadable);
                    sample.passed = sample.maximumRgbError <= ColorTolerance && Mathf.Abs(unreadable.a - 1f) < .001f;
                    Require(RenderTexture.active == sentinel, "Surface sampling did not restore the active render target.");
                    Require(GL.sRGBWrite == sentinelSrgb, "Surface sampling did not restore the sRGB write setting.");
                    Require(sample.passed, "GPU sample " + index + " differs from the readable baseline by " + sample.maximumRgbError + ".");
                }
                report.renderStateRestored = true;
            }
            finally
            {
                RenderTexture.active = previousTarget;
                GL.sRGBWrite = previousSrgb;
                if (sentinel != null) RenderTexture.ReleaseTemporary(sentinel);
                SurfaceColorSampler.ReleaseResources();
                if (fixture != null) UnityEngine.Object.DestroyImmediate(fixture);
                if (material != null) UnityEngine.Object.DestroyImmediate(material);
                if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private static float MaxRgbError(Color left, Color right)
        {
            return Mathf.Max(Mathf.Abs(left.r - right.r), Mathf.Abs(left.g - right.g), Mathf.Abs(left.b - right.b));
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        [Serializable]
        private sealed class GraphicsReport
        {
            public bool passed;
            public string utc;
            public string unityVersion;
            public string graphicsDeviceType;
            public string graphicsDeviceName;
            public string colorSpace;
            public float tolerance;
            public bool textureWasUnreadable;
            public bool renderStateRestored;
            public float durationSeconds;
            public SampleResult[] samples;
            public string[] runtimeErrors;
            public string error;
        }

        [Serializable]
        private sealed class SampleResult
        {
            public string name;
            public Vector2 hitUv;
            public Vector2 transformedUv;
            public Color expected;
            public Color readable;
            public Color unreadable;
            public float maximumRgbError;
            public bool passed;
        }
    }
}
