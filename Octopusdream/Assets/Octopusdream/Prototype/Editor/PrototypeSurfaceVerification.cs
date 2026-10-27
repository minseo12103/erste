using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Octopusdream.Editor
{
    public static class PrototypeSurfaceVerification
    {
        static readonly List<GameObject> Fixtures = new List<GameObject>();
        static readonly List<Shape> Shapes = new List<Shape>();
        static readonly List<Check> Checks = new List<Check>();

        [MenuItem("Octopusdream/Run Surface Verification")]
        public static void Run()
        {
            Fixtures.Clear(); Shapes.Clear(); Checks.Clear();
            DateTime began = DateTime.UtcNow;
            var errors = new List<string>();
            Application.LogCallback logger = (message, trace, type) => { if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message + "\n" + trace); };
            Application.logMessageReceived += logger;
            try
            {
                RunCheck("flat_ground_contact_and_softness_range", Flat);
                RunCheck("step_edge_drape_without_upper_step_penetration", Edge);
                RunCheck("deformed_rim_paint_and_empty_air_hit_test", PaintRay);
                RunCheck("airborne_body_recovers_round_shape", Airborne);
            }
            finally
            {
                foreach (GameObject fixture in Fixtures)
                {
                    if (fixture == null) continue;
                    foreach (var appearance in fixture.GetComponentsInChildren<PrototypeAppearance>(true))
                        foreach (FieldInfo field in typeof(PrototypeAppearance).GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
                            if (field.GetValue(appearance) is Mesh || field.GetValue(appearance) is Material || field.GetValue(appearance) is Texture2D)
                                UnityEngine.Object.DestroyImmediate((UnityEngine.Object)field.GetValue(appearance));
                    UnityEngine.Object.DestroyImmediate(fixture);
                }
                Application.logMessageReceived -= logger;
            }
            var report = new Report { passed = Checks.All(check => check.passed) && errors.Count == 0, utc = began.ToString("O"),
                seconds = (float)(DateTime.UtcNow - began).TotalSeconds, checks = Checks.ToArray(), shapes = Shapes.ToArray(), runtimeErrors = errors.ToArray() };
            Directory.CreateDirectory("Logs/Prototype");
            File.WriteAllText("Logs/Prototype/surface-verification.json", JsonUtility.ToJson(report, true));
            Debug.Log("OCTOPUS_SURFACE_VERIFY: " + JsonUtility.ToJson(report));
            if (Application.isBatchMode) EditorApplication.Exit(report.passed ? 0 : 1);
        }

        static void RunCheck(string name, Action action)
        {
            foreach (GameObject fixture in Fixtures) if (fixture != null) fixture.SetActive(false);
            try { action(); Checks.Add(new Check { name = name, passed = true, detail = "Passed" }); }
            catch (Exception error) { Checks.Add(new Check { name = name, passed = false, detail = error.ToString() }); }
        }

        static PrototypeAppearance Create(float softness, bool edge = false)
        {
            GameObject root = new GameObject("Temporary surface verifier") { hideFlags = HideFlags.HideAndDontSave };
            Fixtures.Add(root);
            if (edge)
            {
                Cube(root, new Vector3(998f, 9.5f, 0f), new Vector3(4f, 1f, 6f));
                Cube(root, new Vector3(1002f, 8.95f, 0f), new Vector3(4f, 1f, 6f));
            }
            else Cube(root, new Vector3(1000f, 9.5f, 0f), new Vector3(8f, 1f, 8f));
            var body = new GameObject("Fixture body");
            body.transform.SetParent(root.transform);
            body.transform.position = new Vector3(edge ? 999.75f : 1000f, 10.8f, 0f);
            var appearance = body.AddComponent<PrototypeAppearance>();
            appearance.Initialize(false);
            appearance.SetSoftness(softness);
            Physics.SyncTransforms();
            Settle(appearance);
            return appearance;
        }

        static void Cube(GameObject parent, Vector3 position, Vector3 size)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.transform.SetParent(parent.transform);
            cube.transform.position = position; cube.transform.localScale = size;
        }

        static void Settle(PrototypeAppearance appearance)
        {
            for (int frame = 0; frame < 180; frame++) appearance.Simulate(Vector3.zero, 1f / 60f);
        }

        static Shape Measure(PrototypeAppearance appearance, string name)
        {
            Bounds bounds = appearance.WorldBounds;
            var shape = new Shape { name = name, softness = appearance.Softness, size = bounds.size, center = bounds.center,
                floorGap = bounds.min.y - 10f, contact = appearance.SurfaceContact };
            Shapes.Add(shape); return shape;
        }

        static Vector3[] Vertices(PrototypeAppearance appearance)
        {
            var filter = appearance.GetComponentsInChildren<MeshFilter>().First(item => item.name == "Octopus sphere");
            return filter.sharedMesh.vertices.Select(filter.transform.TransformPoint).ToArray();
        }

        static void Flat()
        {
            Shape zero = Measure(Create(0f), "flat_0");
            Fixtures.Last().SetActive(false);
            Shape middle = Measure(Create(5f), "flat_5");
            Fixtures.Last().SetActive(false);
            Shape maximum = Measure(Create(10f), "flat_10");
            foreach (Shape shape in new[] { zero, middle, maximum })
                Require(shape.floorGap >= -.012f && shape.floorGap <= .025f && shape.contact > .95f, "Body floats or penetrates flat ground: " + JsonUtility.ToJson(shape));
            Require(zero.size.y > 1.25f && zero.size.y < 1.35f, "SOFTNESS0 must remain round.");
            Require(middle.size.y > .8f && middle.size.y < 1.1f, "SOFTNESS5 should retain a rounded body.");
            Require(maximum.size.y < .25f && maximum.size.y > .07f && maximum.size.x > middle.size.x * 1.4f, "SOFTNESS10 did not become a thin spread body.");
        }

        static void Edge()
        {
            foreach (float softness in new[] { 5f, 10f })
            {
                var appearance = Create(softness, true);
                Shape shape = Measure(appearance, "edge_" + softness);
                Vector3[] vertices = Vertices(appearance);
                float lowestRim = vertices.Where(v => v.x > 1000.12f).Min(v => v.y);
                float lowestSupported = vertices.Where(v => v.x < 999.95f).Min(v => v.y);
                shape.lowestRim = lowestRim; shape.lowestSupported = lowestSupported;
                Require(lowestRim < 9.93f, "Unsupported rim did not drape over the step: " + JsonUtility.ToJson(shape));
                Require(lowestSupported >= 9.985f, "Body penetrates the supported upper step: " + lowestSupported);
                Require(vertices.All(v => v.y >= 9.435f), "Body penetrates the lower step.");
                if (softness == 10f) Require(appearance.WorldBounds.max.y < 10.3f, "SOFTNESS10 retains excessive height over the step.");
                Fixtures.Last().SetActive(false);
            }
        }

        static void PaintRay()
        {
            var appearance = Create(10f);
            Bounds bounds = appearance.WorldBounds;
            Vector3 rim = bounds.center + Vector3.right * bounds.extents.x * .75f;
            Ray ray = new Ray(new Vector3(rim.x, 13f, rim.z), Vector3.down);
            Require(Mathf.Abs(rim.x - appearance.transform.position.x) > PrototypeAppearance.OctopusRadius, "Ray fixture is not outside the original rigid sphere.");
            Require(appearance.TryRaycastSurface(ray, out float distance, out Vector3 direction), "Spread rim is not paintable.");
            Require(distance > 0f && direction.sqrMagnitude > .9f, "Surface ray did not return a valid hit/direction.");
            string before = appearance.ExportPaint();
            appearance.Paint(direction, Color.green, .25f);
            Require(appearance.ExportPaint() != before, "Painting the deformed rim did not change its texture.");
            Ray emptyAir = new Ray(new Vector3(1000f, 10.65f, -3f), Vector3.forward);
            Require(!appearance.TryRaycastSurface(emptyAir, out _, out _), "Empty air above the flattened body is incorrectly hittable.");
        }

        static void Airborne()
        {
            var appearance = Create(10f);
            appearance.transform.position += Vector3.up * 4f;
            Physics.SyncTransforms(); Settle(appearance);
            Shape shape = Measure(appearance, "airborne_10");
            Require(shape.contact < .01f && shape.size.y > 1.2f && shape.size.y < 1.4f && shape.size.x < 1.4f,
                "Swimming away from the surface did not restore a round body: " + JsonUtility.ToJson(shape));
        }

        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        [Serializable] sealed class Check { public string name, detail; public bool passed; }
        [Serializable] sealed class Shape { public string name; public float softness, floorGap, contact, lowestRim, lowestSupported; public Vector3 center, size; }
        [Serializable] sealed class Report { public bool passed; public string utc; public float seconds; public Check[] checks; public Shape[] shapes; public string[] runtimeErrors; }
    }
}