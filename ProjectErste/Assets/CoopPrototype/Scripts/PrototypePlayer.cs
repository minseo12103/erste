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
        public int lastJump, lastDash, lastFreeze;
        int facing = 1;
        float nextDash;
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
            frozen = false; move = 0; jumpPending = dashPending = freezePending = false;
            facing = 1; nextDash = 0; lastJump = lastDash = lastFreeze = 0;
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
                body.linearVelocity = Vector2.zero; body.angularVelocity = 0;
                body.bodyType = frozen ? RigidbodyType2D.Kinematic : RigidbodyType2D.Dynamic;
            }
            if (frozen) { jumpPending = dashPending = false; return; }
            if (Mathf.Abs(move) > .01f) facing = move < 0 ? -1 : 1;
            Vector2 v = body.linearVelocity;
            v.x = Mathf.MoveTowards(v.x, move * settings.moveSpeed, settings.moveAcceleration * Time.fixedDeltaTime);
            body.linearVelocity = v;
            if (jumpPending && Grounded()) body.AddForce(Vector2.up * settings.jumpImpulse, ForceMode2D.Impulse);
            if (dashPending && Time.time >= nextDash) { body.AddForce(Vector2.right * facing * settings.dashImpulse, ForceMode2D.Impulse); nextDash = Time.time + settings.dashCooldown; }
            jumpPending = dashPending = false;
        }
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
