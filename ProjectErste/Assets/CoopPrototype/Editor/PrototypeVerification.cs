using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace ShapeCoop.Editor
{
    public static class PrototypeVerification
    {
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        public static void Run()
        {
            try
            {
                var clock = new PrototypeClock(); clock.Begin(100); Check(Math.Abs(clock.Elapsed(120) - 20) < .001, "clock before restart");
                // Restart deliberately does not call Begin; time continues through multiple resets.
                Check(Math.Abs(clock.Elapsed(160) - 60) < .001, "clock accumulated across restart");
                clock.Complete(160); clock.Complete(200); Check(Math.Abs(clock.Elapsed(250) - 60) < .001, "completion must be final");
                clock.Begin(300); Check(Math.Abs(clock.Elapsed(305) - 5) < .001, "new game clock");
                var oldMode = Physics2D.simulationMode; Physics2D.simulationMode = SimulationMode2D.Script;
                var settingsGo = new GameObject("verification settings"); var settings = settingsGo.AddComponent<PrototypeSettings>();
                var obj = new GameObject("verification player"); var player = obj.AddComponent<PrototypePlayer>();
                player.Initialize(settings, new PlayerData { id = "1", name = "test", shape = 1, color = 0 }, new Vector2(0,2), true);
                var tick = typeof(PrototypePlayer).GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic);
                player.body.rotation = 180; player.move = -1; tick.Invoke(player, null);
                Check(player.body.linearVelocity.x < 0, "rotated player must move world left");
                player.move = 0; player.dashPending = true; tick.Invoke(player, null);
                Check(player.body.linearVelocity.x < -1, "dash must use last world direction");
                player.body.position = new Vector2(0,2); player.body.rotation = 37; player.freezePending = true; tick.Invoke(player, null);
                Check(player.frozen && player.body.bodyType == RigidbodyType2D.Kinematic, "freeze in air");
                for (int i = 0; i < 10; i++) Physics2D.Simulate(.02f);
                Check(Vector2.Distance(player.body.position, new Vector2(0,2)) < .001 && Math.Abs(player.body.rotation - 37) < .01, "frozen pose changed");
                // Use a level platform here so the ball does not roll off its slope.
                player.body.rotation = 0;
                var ballGo = new GameObject("verification ball"); var rb = ballGo.AddComponent<Rigidbody2D>(); rb.position = new Vector2(0,4); rb.gravityScale = 2; ballGo.AddComponent<CircleCollider2D>().radius = .5f;
                for (int i = 0; i < 100; i++) Physics2D.Simulate(.02f);
                Check(rb.position.y > 2.5f, "frozen body should support a ball, y=" + rb.position.y);
                player.freezePending = true; tick.Invoke(player, null); Check(!player.frozen && player.body.bodyType == RigidbodyType2D.Dynamic, "unfreeze");
                player.body.linearVelocity = Vector2.one; player.body.angularVelocity = 3; player.ResetBody();
                Check(player.body.position == player.spawn && player.body.rotation == 0 && player.body.linearVelocity == Vector2.zero && player.body.angularVelocity == 0 && !player.frozen, "reset physics state");
                UnityEngine.Object.DestroyImmediate(ballGo); UnityEngine.Object.DestroyImmediate(obj); UnityEngine.Object.DestroyImmediate(settingsGo); Physics2D.simulationMode = oldMode;
                Debug.Log("SHAPE_COOP_VERIFICATION_OK: clock, rotated movement, dash, airborne freeze, support, unfreeze, reset");
                Directory.CreateDirectory("Build/ShapeCoop");
                var report = BuildPipeline.BuildPlayer(EditorBuildSettings.scenes, "Build/ShapeCoop/ShapeCoop.exe", BuildTarget.StandaloneWindows64, BuildOptions.None);
                Check(report.summary.result == BuildResult.Succeeded, "Windows build failed: " + report.summary.result);
                Check(File.Exists("Build/ShapeCoop/steam_appid.txt"), "build App ID missing");
                Debug.Log("SHAPE_COOP_BUILD_OK"); EditorApplication.Exit(0);
            }
            catch (Exception e) { Physics2D.simulationMode = SimulationMode2D.FixedUpdate; Debug.LogException(e); EditorApplication.Exit(1); }
        }
    }
}
