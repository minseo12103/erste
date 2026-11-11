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
        static float MeasureJump(PrototypePlayer player, MethodInfo tick, bool hold)
        {
            player.spawn = new Vector2(0, .51f); player.ResetBody();
            for (int i = 0; i < 20; i++) Physics2D.Simulate(.02f);
            float start = player.body.position.y, highest = start;
            player.jumpPending = true; player.jumpHeld = hold;
            for (int i = 0; i < 100; i++)
            {
                if (i == 20) player.jumpHeld = false;
                tick.Invoke(player, null); Physics2D.Simulate(.02f);
                highest = Mathf.Max(highest, player.body.position.y);
            }
            return highest - start;
        }
        static float MeasureJumpImpulse(PrototypePlayer player, MethodInfo tick, bool hold)
        {
            player.spawn = new Vector2(0, .51f); player.ResetBody();
            for (int i = 0; i < 20; i++) Physics2D.Simulate(.02f);
            player.body.linearVelocity = Vector2.zero; player.jumpPending = true; player.jumpHeld = hold;
            for (int i = 0; i < 20; i++) tick.Invoke(player, null);
            return player.body.linearVelocity.y;
        }
        static (float height, float distance, float startVelocity, float landingGravity) MeasureMovingJump(PrototypePlayer player, PrototypeSettings settings, MethodInfo tick, bool hold)
        {
            player.spawn = new Vector2(0, .51f); player.ResetBody();
            for (int i = 0; i < 20; i++) Physics2D.Simulate(.02f);
            player.move = 1; player.body.linearVelocity = new Vector2(settings.moveSpeed, 0);
            float startY = player.body.position.y, startX = player.body.position.x, height = 0;
            player.jumpPending = true; player.jumpHeld = hold; tick.Invoke(player, null);
            float startVelocity = player.body.linearVelocity.y;
            bool landed = false, leftGround = false;
            var contacts = new ContactPoint2D[16];
            for (int i = 0; i < 100; i++)
            {
                Physics2D.Simulate(.02f);
                height = Mathf.Max(height, player.body.position.y - startY);
                int count = player.shapeCollider.GetContacts(contacts); bool grounded = false;
                for (int j = 0; j < count; j++) if (contacts[j].normal.y > .45f) { grounded = true; break; }
                if (!grounded && player.body.position.y > startY + .02f) leftGround = true;
                if (leftGround && grounded && player.body.linearVelocity.y <= .05f) { landed = true; break; }
                tick.Invoke(player, null);
            }
            Check(landed, "jump should land on the ground: hold=" + hold + ", left=" + leftGround + ", position=" + player.body.position + ", velocity=" + player.body.linearVelocity + ", gravity=" + player.body.gravityScale);
            float distance = player.body.position.x - startX;
            tick.Invoke(player, null);
            return (height, distance, startVelocity, player.body.gravityScale);
        }
        static (float playerSpeed, float objectSpeed, float objectDistance, float gravity) DashIntoBody(PrototypePlayer player, MethodInfo tick, float mass)
        {
            player.spawn = new Vector2(0, 2); player.ResetBody();
            var target = new GameObject("dash test body " + mass);
            var targetBody = target.AddComponent<Rigidbody2D>();
            targetBody.mass = mass; targetBody.gravityScale = 0; targetBody.position = new Vector2(1.5f, 2);
            target.AddComponent<BoxCollider2D>();
            player.dashPending = true;
            for (int i = 0; i < 6; i++) { tick.Invoke(player, null); Physics2D.Simulate(.02f); }
            var result = (player.body.linearVelocity.x, targetBody.linearVelocity.x, targetBody.position.x - 1.5f, player.body.gravityScale);
            UnityEngine.Object.DestroyImmediate(target);
            return result;
        }
        public static void Run()
        {
            // This legacy verifier creates temporary objects and exits the Editor.
            // Never run it against a developer's open scene.
            if (!Application.isBatchMode)
            {
                Debug.LogError("PrototypeVerification.Run is batch-only; use an isolated project copy.");
                return;
            }
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
                Check(player.body.gravityScale == settings.gravityScale, "gravity must stay active during a dash");
                float dashStartY = player.body.position.y;
                player.move = 1; // Steering cannot reverse a dash already in progress.
                for (int i = 0; i < 5; i++) { tick.Invoke(player, null); Physics2D.Simulate(.02f); }
                Check(player.body.position.x < -.5f && player.body.position.y < dashStartY - .05f && player.body.linearVelocity.x < 0, "air dash must keep its horizontal direction and fall under gravity");
                for (int i = 0; i < 20; i++) { tick.Invoke(player, null); Physics2D.Simulate(.02f); }
                Check(Math.Abs(player.body.gravityScale - settings.gravityScale) < .001f && player.body.position.y < dashStartY - .1f, "gravity must resume after dash");
                player.ResetBody(); player.body.rotation = 0; player.move = -1; tick.Invoke(player, null);
                var dashWall = new GameObject("dash collision wall"); dashWall.transform.position = new Vector2(-1.5f, 2); dashWall.AddComponent<BoxCollider2D>().size = new Vector2(.2f, 8);
                player.dashPending = true;
                for (int i = 0; i < 10; i++) { tick.Invoke(player, null); Physics2D.Simulate(.02f); }
                Check(player.body.position.x > -1.3f, "dash must not pass through a wall");
                Check(Math.Abs(player.body.gravityScale - settings.gravityScale) < .001f, "wall impact should end dash early");
                UnityEngine.Object.DestroyImmediate(dashWall);
                var light = DashIntoBody(player, tick, .2f);
                var heavy = DashIntoBody(player, tick, 50f);
                Check(light.objectSpeed > 2f && light.objectDistance > .1f && light.playerSpeed > 2f, "light body should move and player should continue: " + light);
                Check(Math.Abs(heavy.objectSpeed) < 1f && Math.Abs(heavy.playerSpeed) < 1f && heavy.gravity == settings.gravityScale,
                    "heavy body should stop dash and restore gravity early: " + heavy);
                Debug.Log("SHAPE_COOP_DASH_IMPACT_OK: light " + light + ", heavy " + heavy);
                player.ResetBody(); player.body.linearVelocity = new Vector2(0, -3); player.dashPending = true; tick.Invoke(player, null);
                Check(Math.Abs(player.body.linearVelocity.y + 3) < .01f, "dash must preserve falling velocity");
                Physics2D.Simulate(.02f);
                Check(player.body.linearVelocity.y < -3, "gravity must accelerate a falling dash");
                player.ResetBody(); player.dashPending = true; tick.Invoke(player, null); player.ResetBody();
                Check(Math.Abs(player.body.gravityScale - settings.gravityScale) < .001f && player.body.linearVelocity == Vector2.zero, "reset during dash restores gravity and velocity");
                player.dashPending = true; tick.Invoke(player, null);
                player.body.position = new Vector2(0,2); player.body.rotation = 37; player.freezePending = true; tick.Invoke(player, null);
                Check(player.frozen && player.body.bodyType == RigidbodyType2D.Kinematic && Math.Abs(player.body.gravityScale - settings.gravityScale) < .001f, "freeze in air cancels dash and restores gravity");
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
                UnityEngine.Object.DestroyImmediate(ballGo);
                var ground = new GameObject("jump ground"); ground.transform.position = new Vector2(0, -.5f); ground.AddComponent<BoxCollider2D>().size = new Vector2(20, 1);
                var restoredTap = MeasureMovingJump(player, settings, tick, false);
                var restoredHeld = MeasureMovingJump(player, settings, tick, true);
                Check(Math.Abs(restoredTap.startVelocity - 7f) < .02f && Math.Abs(restoredTap.distance - 3.486f) < .15f && Math.Abs(restoredTap.height - 1.179f) < .1f,
                      "tap jump should match its previous trajectory: " + restoredTap);
                Check(Math.Abs(restoredHeld.distance - 5.024f) < .15f && Math.Abs(restoredHeld.height - 2.215f) < .1f,
                      "held jump should match its previous trajectory: " + restoredHeld);
                Check(Math.Abs(restoredTap.landingGravity - settings.gravityScale) < .001f && Math.Abs(restoredHeld.landingGravity - settings.gravityScale) < .001f,
                      "normal gravity must return on landing");
                Debug.Log("SHAPE_COOP_JUMP_RESTORED_OK: tap " + restoredTap + "; held " + restoredHeld);
                float tapHeight = MeasureJump(player, tick, false), holdHeight = MeasureJump(player, tick, true);
                Check(tapHeight > .5f && holdHeight > tapHeight * 1.2f, "held jump should rise higher: tap=" + tapHeight + ", hold=" + holdHeight);
                float tapImpulse = MeasureJumpImpulse(player, tick, false), holdImpulse = MeasureJumpImpulse(player, tick, true);
                Check(Math.Abs(tapImpulse - settings.jumpImpulse / settings.playerMass) < .02f && Math.Abs(holdImpulse / tapImpulse - 1.5f) < .02f,
                    "jump impulse range must be 1.0 to 1.5: " + tapImpulse + ", " + holdImpulse);
                player.spawn = new Vector2(0, .51f); player.ResetBody();
                for (int i = 0; i < 20; i++) Physics2D.Simulate(.02f);
                float jumpStartY = player.body.position.y;
                player.jumpPending = true; player.dashPending = true; player.jumpHeld = true; tick.Invoke(player, null);
                Check(player.body.linearVelocity.x > 0 && player.body.linearVelocity.y > 0 && Math.Abs(player.body.gravityScale - settings.gravityScale) < .001f,
                    "jump and dash on the same tick should produce diagonal velocity with gravity");
                for (int i = 0; i < 4; i++) { tick.Invoke(player, null); Physics2D.Simulate(.02f); }
                Check(player.body.position.x > .3f && player.body.position.y > jumpStartY + .2f, "jump dash should move diagonally upward");
                Debug.Log("SHAPE_COOP_DIAGONAL_DASH_OK");
                var wall = new GameObject("bounce wall"); wall.transform.position = new Vector2(3, 2); wall.AddComponent<BoxCollider2D>().size = new Vector2(.5f, 4);
                ballGo = new GameObject("bounce ball"); rb = ballGo.AddComponent<Rigidbody2D>(); rb.gravityScale = 0; rb.position = new Vector2(1,2); rb.linearVelocity = new Vector2(8,0); rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                var bounceCollider = ballGo.AddComponent<CircleCollider2D>(); bounceCollider.radius = .5f;
                var material = PrototypeGame.CreateBallMaterial(settings); bounceCollider.sharedMaterial = material;
                bool bounced = false; for (int i = 0; i < 40; i++) { Physics2D.Simulate(.02f); if (rb.linearVelocity.x < -1) bounced = true; }
                Check(bounced, "ball must bounce off wall");
                player.body.position = new Vector2(0,2); player.body.rotation = 0; player.freezePending = true; tick.Invoke(player, null);
                rb.position = new Vector2(-2,2); rb.linearVelocity = new Vector2(8,0);
                bounced = false; for (int i = 0; i < 35; i++) { Physics2D.Simulate(.02f); if (rb.linearVelocity.x < -1) bounced = true; }
                Check(bounced, "ball must bounce off player");
                UnityEngine.Object.DestroyImmediate(material); UnityEngine.Object.DestroyImmediate(ballGo); UnityEngine.Object.DestroyImmediate(wall); UnityEngine.Object.DestroyImmediate(ground); UnityEngine.Object.DestroyImmediate(obj); UnityEngine.Object.DestroyImmediate(settingsGo); Physics2D.simulationMode = oldMode;
                Debug.Log("SHAPE_COOP_VERIFICATION_OK: clock, gravity and diagonal dash, collision, freeze, reset, jump, ball bounce");
                EditorApplication.Exit(0);
            }
            catch (Exception e) { Physics2D.simulationMode = SimulationMode2D.FixedUpdate; Debug.LogException(e); EditorApplication.Exit(1); }
        }

        // Explicitly opt in to the Windows build; Run only verifies gameplay.
        public static void BuildWindows()
        {
            if (!Application.isBatchMode)
            {
                Debug.LogError("PrototypeVerification.BuildWindows is batch-only; use an isolated project copy.");
                return;
            }
            try
            {
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
