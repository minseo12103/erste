using UnityEngine;

namespace ShapeCoop
{
    public sealed class PrototypePlayer : MonoBehaviour
    {
        public string playerId, playerName;
        public int shape, colorIndex;
        public bool frozen;
        public Rigidbody2D body;
        public Collider2D shapeCollider;
        public Vector2 spawn;
        public Vector2 targetPosition;
        public float targetAngle;
        public float move;
        public bool jumpPending, dashPending, freezePending;
        public bool jumpHeld;
        public int lastJump, lastDash, lastFreeze;
        int facing = 1, dashDirection = 1;
        float nextDash;
        float dashRemaining, jumpBoostRemaining;
        PrototypeSettings settings;
        bool authority;

        public void Initialize(PrototypeSettings s, PlayerData data, Vector2 start, bool host)
        {
            settings = s; authority = host; spawn = start;
            playerId = data.id; playerName = data.name; shape = data.shape; colorIndex = data.color;
            body = gameObject.AddComponent<Rigidbody2D>();
            body.mass = settings.playerMass; body.gravityScale = settings.gravityScale;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            if (shape == 0)
            {
                var poly = gameObject.AddComponent<PolygonCollider2D>();
                poly.points = new[] { new Vector2(-.5f, -.5f), new Vector2(.5f, -.5f), new Vector2(0, .5f) };
                shapeCollider = poly;
            }
            else if (shape == 1) shapeCollider = gameObject.AddComponent<BoxCollider2D>();
            else { var circle = gameObject.AddComponent<CircleCollider2D>(); circle.radius = .5f; shapeCollider = circle; }
            PrototypeVisuals.Shape(transform, shape, PrototypeVisuals.PlayerColor(colorIndex));
            if (!authority) body.simulated = false;
            ResetBody();
        }
        public void ResetBody()
        {
            frozen = false; move = 0; jumpPending = dashPending = freezePending = jumpHeld = false;
            dashRemaining = jumpBoostRemaining = 0;
            facing = dashDirection = 1; nextDash = 0; lastJump = lastDash = lastFreeze = 0;
            body.gravityScale = settings.gravityScale;
            if (authority) { body.bodyType = RigidbodyType2D.Dynamic; body.position = spawn; body.rotation = 0; body.linearVelocity = Vector2.zero; body.angularVelocity = 0; }
            else { transform.position = spawn; transform.rotation = Quaternion.identity; }
            targetPosition = spawn; targetAngle = 0;
        }
        bool Grounded()
        {
            var contacts = new ContactPoint2D[16]; int count = shapeCollider.GetContacts(contacts);
            for (int i = 0; i < count; i++) if (contacts[i].normal.y > .45f) return true;
            return false;
        }
        void FixedUpdate()
        {
            if (!authority) return;
            if (freezePending)
            {
                freezePending = false; frozen = !frozen;
                jumpBoostRemaining = dashRemaining = 0;
                body.gravityScale = settings.gravityScale;
                body.linearVelocity = Vector2.zero; body.angularVelocity = 0;
                body.bodyType = frozen ? RigidbodyType2D.Kinematic : RigidbodyType2D.Dynamic;
            }
            if (frozen) { jumpPending = dashPending = false; return; }
            if (Mathf.Abs(move) > .01f) facing = move < 0 ? -1 : 1;
            // Process a jump first so a dash on the same physics tick keeps it.
            if (jumpPending && Grounded())
            {
                body.AddForce(Vector2.up * settings.jumpImpulse, ForceMode2D.Impulse);
                jumpBoostRemaining = settings.jumpHoldTime;
            }
            if (!jumpHeld || body.linearVelocity.y < 0) jumpBoostRemaining = 0;
            if (jumpBoostRemaining > 0)
            {
                float duration = Mathf.Min(jumpBoostRemaining, Time.fixedDeltaTime);
                float fraction = duration / Mathf.Max(.02f, settings.jumpHoldTime);
                body.AddForce(Vector2.up * settings.jumpImpulse * (Mathf.Clamp(settings.maxJumpMultiplier, 1f, 1.5f) - 1f) * fraction, ForceMode2D.Impulse);
                jumpBoostRemaining -= duration;
            }
            bool dashStarted = false;
            if (dashPending && dashRemaining <= 0 && Time.time >= nextDash)
            {
                dashDirection = facing;
                dashRemaining = Mathf.Max(Time.fixedDeltaTime, settings.dashDuration);
                body.linearVelocity = new Vector2(dashDirection * Mathf.Max(0, settings.dashSpeed), body.linearVelocity.y);
                nextDash = Time.time + settings.dashCooldown;
                dashStarted = true;
            }
            if (dashRemaining > 0)
            {
                // Once started, let the physics solver change velocity on impact.
                // An obstacle that stops or reverses us ends the dash early.
                float stopSpeed = Mathf.Max(.01f, settings.dashSpeed * Mathf.Clamp01(settings.dashStopSpeedRatio));
                if (!dashStarted && body.linearVelocity.x * dashDirection <= stopSpeed)
                {
                    dashRemaining = 0;
                    jumpPending = dashPending = false;
                    return;
                }
                dashRemaining = Mathf.Max(0, dashRemaining - Time.fixedDeltaTime);
                jumpPending = dashPending = false;
                return;
            }
            body.gravityScale = settings.gravityScale;
            Vector2 v = body.linearVelocity;
            v.x = Mathf.MoveTowards(v.x, move * settings.moveSpeed, settings.moveAcceleration * Time.fixedDeltaTime);
            body.linearVelocity = v;
            jumpPending = dashPending = false;
        }
        void OnDisable() { if (authority && body != null) { dashRemaining = 0; body.gravityScale = settings.gravityScale; } }
        void Update()
        {
            if (authority) return;
            float t = 1f - Mathf.Exp(-settings.interpolationSpeed * Time.deltaTime);
            transform.position = Vector3.Lerp(transform.position, targetPosition, t);
            transform.rotation = Quaternion.Lerp(transform.rotation, Quaternion.Euler(0, 0, targetAngle), t);
        }
        public BodyData Capture() => new BodyData { x = body.position.x, y = body.position.y, angle = body.rotation, frozen = frozen };
        public void Apply(BodyData data, bool snap)
        {
            if (data == null) return;
            frozen = data.frozen; targetPosition = new Vector2(data.x, data.y); targetAngle = data.angle;
            if (snap) { transform.position = targetPosition; transform.rotation = Quaternion.Euler(0, 0, targetAngle); }
        }
    }
}
