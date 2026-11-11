using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShapeCoop
{
    public enum MapGimmickKind { Sand, Vent, Pulley, Oil, Cement, Sparks, Crane, Sprinkler, BrickWall, Magnet, Nitrogen, Seesaw }

    // The host alone evaluates contacts and forces. Clients display host snapshots.
    public sealed class MapGimmick : MonoBehaviour
    {
        public MapGimmickKind kind;
        [Header("Timing and force")]
        [Min(0f)] public float activationDelay = 1.5f;
        [Min(0f)] public float force = 18f;
        [Min(.02f)] public float activeSeconds = 1f;
        [Min(.02f)] public float inactiveSeconds = 2f;
        [Min(0f)] public float effectSeconds = 1.5f;
        [Min(0f)] public float impactSpeed = 8f;
        [Header("Sand animation")]
        [Tooltip("Four intact/tearing frames followed by four burst/dust frames.")]
        public Sprite[] sandFrames = Array.Empty<Sprite>();
        [Header("Moving parts")]
        public Rigidbody2D platformA, platformB, rotatingBeam;
        public Transform visual;
        public Transform beamVisual;
        public Transform ropeA, ropeB;
        [Min(.1f)] public float travel = 2f;
        [Min(0f)] public float pulleySpeed = 1.5f;
        public float rotationSpeed = -45f;
        [Min(.1f)] public float beamRange = 5f;
        bool authority, initialized, active, broken, triggered;
        float timer, offset, targetOffset, angle, targetAngle, beamLength, targetBeam, pulleyVelocity;
        bool sparkHit;
        Vector3 initialA, initialB;
        Vector3 initialBeamPosition;
        Quaternion initialRotation;
        Collider2D solid;
        SpriteRenderer[] renderers;
        Color[] initialColors;
        PhysicsMaterial2D oilMaterial;
        SpriteRenderer sandRenderer;
        WindVentEffect windEffect;
        CementImpactVisual cementVisual;
        WeldingSparkVisual weldingVisual;
        WaterBarrierVisual waterVisual;
        List<Collider2D> waterContacts;
        HashSet<Rigidbody2D> waterBalls;
        internal WaterBarrierFlow WaterFlow { get; private set; }
        BrickWallVisual brickVisual;
        MagnetFieldVisual magnetVisual;
        NitrogenGeyserVisual nitrogenVisual;
        float nitrogenDisplayDuration;
        RotatingCranePlatforms cranePlatforms;
        float progressReceivedAt;
        readonly HashSet<Rigidbody2D> windBodiesThisStep = new HashSet<Rigidbody2D>();
        const float SandBurstDuration = .58f;

        public void Initialize(bool host)
        {
            authority = host; initialized = true; solid = GetComponent<Collider2D>();
            renderers = visual != null ? visual.GetComponentsInChildren<SpriteRenderer>(true) : Array.Empty<SpriteRenderer>();
            initialColors = new Color[renderers.Length]; for (int i = 0; i < renderers.Length; i++) initialColors[i] = renderers[i].color;
            var sandVisual = kind == MapGimmickKind.Sand && visual != null ? visual.Find("Sand") : null;
            sandRenderer = sandVisual != null ? sandVisual.GetComponent<SpriteRenderer>() : null;
            windEffect = kind == MapGimmickKind.Vent && visual != null ? visual.GetComponentInChildren<WindVentEffect>(true) : null;
            cementVisual = kind == MapGimmickKind.Cement && visual != null ? visual.GetComponentInChildren<CementImpactVisual>(true) : null;
            weldingVisual = kind == MapGimmickKind.Sparks && visual != null ? visual.GetComponentInChildren<WeldingSparkVisual>(true) : null;
            brickVisual = kind == MapGimmickKind.BrickWall && visual != null ? visual.GetComponentInChildren<BrickWallVisual>(true) : null;
            magnetVisual = kind == MapGimmickKind.Magnet && visual != null ? visual.GetComponentInChildren<MagnetFieldVisual>(true) : null;
            nitrogenVisual = kind == MapGimmickKind.Nitrogen && visual != null ? visual.GetComponentInChildren<NitrogenGeyserVisual>(true) : null;
            nitrogenDisplayDuration = activeSeconds;
            if (platformA != null) initialA = platformA.transform.localPosition;
            if (platformB != null) initialB = platformB.transform.localPosition;
            if (rotatingBeam != null) { initialRotation = rotatingBeam.transform.localRotation; initialBeamPosition = rotatingBeam.transform.localPosition; }
            cranePlatforms = kind == MapGimmickKind.Crane ? GetComponent<RotatingCranePlatforms>() : null;
            if (cranePlatforms != null) cranePlatforms.Initialize(host, rotatingBeam);
            if (kind == MapGimmickKind.Oil) oilMaterial = new PhysicsMaterial2D("Map oil") { friction = 0, bounciness = 0 };
            if (kind == MapGimmickKind.Sprinkler)
            {
                WaterFlow = new WaterBarrierFlow();
                waterContacts = new List<Collider2D>();
                waterBalls = new HashSet<Rigidbody2D>();
                if (visual != null)
                {
                    var water = visual.Find("Water");
                    waterVisual = visual.GetComponentInChildren<WaterBarrierVisual>(true);
                    if (waterVisual == null && water != null) waterVisual = visual.gameObject.AddComponent<WaterBarrierVisual>();
                    if (waterVisual != null) waterVisual.Bind(water, transform);
                }
            }
            if (!host)
            {
                if (platformA != null) platformA.simulated = false;
                if (platformB != null) platformB.simulated = false;
                if (rotatingBeam != null) rotatingBeam.simulated = false;
            }
            ResetGimmick();
        }
        public void ResetGimmick()
        {
            windBodiesThisStep.Clear();
            timer = offset = targetOffset = angle = targetAngle = pulleyVelocity = 0; beamLength = targetBeam = beamRange;
            progressReceivedAt = Time.time;
            broken = triggered = sparkHit = false; active = kind != MapGimmickKind.Sparks && kind != MapGimmickKind.Magnet && kind != MapGimmickKind.Nitrogen;
            if (WaterFlow != null && solid is BoxCollider2D waterBox) WaterFlow.Set(transform, WaterArea(waterBox), default);
            if (solid != null && (kind == MapGimmickKind.Sand || kind == MapGimmickKind.BrickWall)) solid.enabled = true;
            if (windEffect != null) windEffect.ResetAnimation();
            if (cementVisual != null) cementVisual.ResetAnimation();
            if (kind == MapGimmickKind.Seesaw && rotatingBeam != null)
            {
                rotatingBeam.transform.localPosition = initialBeamPosition;
                if (authority)
                {
                    rotatingBeam.position = transform.TransformPoint(initialBeamPosition);
                    rotatingBeam.rotation = transform.eulerAngles.z + initialRotation.eulerAngles.z;
                    rotatingBeam.linearVelocity = Vector2.zero; rotatingBeam.angularVelocity = 0;
                }
            }
            ApplyMovingParts(true); RefreshVisual();
        }
        void FixedUpdate()
        {
            if (!initialized || !authority) return;
            if (kind == MapGimmickKind.Vent) windBodiesThisStep.Clear();
            float dt = Time.fixedDeltaTime;
            if (kind == MapGimmickKind.Sand && triggered)
            {
                float delay = Mathf.Max(.02f, activationDelay);
                timer = Mathf.Min(timer + dt, delay + SandBurstDuration);
                if (!broken && timer >= delay) { broken = true; if (solid != null) solid.enabled = false; }
            }
            if (kind == MapGimmickKind.Sprinkler) timer = Mathf.Repeat(timer + dt, 120f);
            if (kind == MapGimmickKind.BrickWall && broken && brickVisual != null) timer = Mathf.Min(timer + dt, brickVisual.Duration);
            if (kind == MapGimmickKind.Sparks || kind == MapGimmickKind.Magnet || kind == MapGimmickKind.Nitrogen)
            {
                timer += dt; float duration = active ? Mathf.Max(.02f, activeSeconds) : Mathf.Max(.02f, inactiveSeconds);
                if (timer >= duration) { timer -= duration; active = !active; }
            }
            if (kind == MapGimmickKind.Pulley) TickPulley(dt);
            if (kind == MapGimmickKind.Crane && rotatingBeam != null)
            {
                angle += rotationSpeed * dt;
                rotatingBeam.MoveRotation(transform.eulerAngles.z + initialRotation.eulerAngles.z + angle);
                if (cranePlatforms != null) cranePlatforms.Apply(angle, false);
            }
            if (kind == MapGimmickKind.Sparks) TickSpark();
            if (kind == MapGimmickKind.Sprinkler) TickWater();
            RefreshVisual();
        }
        void Update()
        {
            if (!initialized || authority) return;
            float t = 1f - Mathf.Exp(-15f * Time.deltaTime);
            offset = Mathf.Lerp(offset, targetOffset, t); angle = Mathf.LerpAngle(angle, targetAngle, t); beamLength = Mathf.Lerp(beamLength, targetBeam, t);
            ApplyMovingParts(false); RefreshVisual();
        }
        void LateUpdate()
        {
            if (!initialized || kind != MapGimmickKind.Pulley) return;
            UpdateRope(ropeA, platformA); UpdateRope(ropeB, platformB);
        }
        void UpdateRope(Transform rope, Rigidbody2D platform)
        {
            if (rope == null || platform == null) return;
            var renderer = rope.GetComponent<SpriteRenderer>();
            bool tiled = renderer != null && renderer.drawMode == SpriteDrawMode.Tiled;
            float top = 2.5f, bottom = Mathf.Min(top, platform.transform.localPosition.y + (tiled ? .42f : .175f));
            rope.localPosition = new Vector3(platform.transform.localPosition.x, (top + bottom) * .5f, 0);
            float length = Mathf.Max(.01f, top - bottom);
            if (tiled) { var size = renderer.size; size.y = length; renderer.size = size; }
            else { var scale = rope.localScale; scale.y = length; rope.localScale = scale; }
        }
        void TickPulley(float dt)
        {
            if (platformA == null || platformB == null) return;
            float loadA = LoadOn(platformA), loadB = LoadOn(platformB);
            float target = Mathf.Clamp((loadA - loadB) * pulleySpeed, -pulleySpeed, pulleySpeed);
            pulleyVelocity = Mathf.MoveTowards(pulleyVelocity, target, pulleySpeed * 5f * dt);
            offset = Mathf.Clamp(offset + pulleyVelocity * dt, -travel, travel);
            if (Mathf.Abs(offset) >= travel && Mathf.Sign(pulleyVelocity) == Mathf.Sign(offset)) pulleyVelocity = 0;
            var a = transform.TransformPoint(initialA + Vector3.down * offset);
            var b = transform.TransformPoint(initialB + Vector3.up * offset);
            platformA.MovePosition(a); platformB.MovePosition(b);
        }
        static float LoadOn(Rigidbody2D platform)
        {
            var col = platform.GetComponent<Collider2D>(); if (col == null) return 0;
            var bounds = col.bounds; float load = 0;
            var hits = Physics2D.OverlapBoxAll(new Vector2(bounds.center.x, bounds.max.y + .11f), new Vector2(Mathf.Max(.05f,bounds.size.x - .05f), .22f), 0);
            foreach (var hit in hits)
            {
                var body = hit.attachedRigidbody;
                if (body != null && body != platform && (body.GetComponent<PrototypePlayer>() != null || body.GetComponent<MapGoalBall>() != null) && hit.bounds.center.y >= bounds.max.y - .05f) load += body.mass;
            }
            return load;
        }
        void TickSpark()
        {
            beamLength = beamRange; sparkHit = false;
            if (!active) return;
            foreach (var hit in Physics2D.RaycastAll(transform.position, Vector2.down, beamRange))
            {
                if (hit.collider == null || hit.collider.isTrigger || hit.collider.transform.IsChildOf(transform)) continue;
                beamLength = hit.distance;
                sparkHit = true;
                var player = hit.collider.GetComponentInParent<PrototypePlayer>();
                if (player != null) player.Stun(effectSeconds);
                break;
            }
        }
        static Rect WaterArea(BoxCollider2D box) => new Rect(box.offset - box.size * .5f, box.size);

        WaterBarrierFlow.Obstacle WaterObstacleFor(PrototypePlayer player)
        {
            var collider = player.shapeCollider;
            var body = player.body;
            var scale = (Vector2)collider.transform.lossyScale;
            var rotation = Quaternion.Euler(0, 0, body.rotation);
            var center = body.position + (Vector2)(rotation * Vector2.Scale(collider.offset, scale));
            var halfSize = new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y)) * .5f;
            if (collider is BoxCollider2D box) halfSize = Vector2.Scale(halfSize, box.size);
            if (collider is CircleCollider2D circle)
            {
                float radius = circle.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
                halfSize = Vector2.one * radius;
            }
            return new WaterBarrierFlow.Obstacle
            {
                Shape = player.shape,
                Center = transform.InverseTransformPoint(center),
                HalfSize = halfSize,
                Angle = collider is CircleCollider2D ? 0 : Mathf.DeltaAngle(transform.eulerAngles.z, body.rotation)
            };
        }

        void TickWater()
        {
            if (WaterFlow == null || !(solid is BoxCollider2D box) || !box.enabled) return;
            var scene = gameObject.scene.GetPhysicsScene2D();
            var area = WaterArea(box);
            var filter = new ContactFilter2D();
            filter.SetLayerMask(Physics2D.GetLayerCollisionMask(gameObject.layer));
            filter.useTriggers = false;
            var bounds = box.bounds;
            scene.OverlapBox(bounds.center, bounds.size, 0, filter, waterContacts);
            WaterBarrierFlow.Obstacle blocker = default;
            float highest = float.NegativeInfinity;
            foreach (var hit in waterContacts)
            {
                var body = hit.attachedRigidbody;
                if (body == null || !body.TryGetComponent<PrototypePlayer>(out var player) ||
                    player.shapeCollider != hit || Physics2D.GetIgnoreCollision(box, hit)) continue;
                var candidate = WaterObstacleFor(player);
                if (!WaterFlow.TryGetImpact(transform, area, candidate, out var impact) || impact.y <= highest) continue;
                highest = impact.y;
                blocker = candidate;
            }
            WaterFlow.Set(transform, area, blocker);
            if (WaterFlow.Shielded)
            {
                // Redirected water can travel outside the original rectangular trigger.
                var footprint = WaterFlow.Bounds;
                var x = transform.TransformVector(Vector3.right * footprint.width);
                var y = transform.TransformVector(Vector3.up * footprint.height);
                var size = new Vector2(Mathf.Abs(x.x) + Mathf.Abs(y.x), Mathf.Abs(x.y) + Mathf.Abs(y.y));
                scene.OverlapBox(transform.TransformPoint(footprint.center), size, 0, filter, waterContacts);
            }
            waterBalls.Clear();
            foreach (var hit in waterContacts)
            {
                var body = hit.attachedRigidbody;
                if (body == null || body.bodyType != RigidbodyType2D.Dynamic || waterBalls.Contains(body) ||
                    !body.TryGetComponent<MapGoalBall>(out _) || Physics2D.GetIgnoreCollision(box, hit)) continue;
                Vector2 point = transform.InverseTransformPoint(body.worldCenterOfMass);
                if (!WaterFlow.TryGetCurrent(point, out var direction)) continue;
                waterBalls.Add(body);
                Vector2 worldDirection = transform.TransformVector(direction);
                body.AddForce(worldDirection.normalized * force * body.mass);
            }
        }
        void OnCollisionEnter2D(Collision2D collision)
        {
            if (!initialized || !authority || broken) return;
            var ball = collision.collider.GetComponentInParent<MapGoalBall>();
            var player = collision.collider.GetComponentInParent<PrototypePlayer>();
            if (kind == MapGimmickKind.Sand && (ball != null || player != null) && solid != null && collision.collider.bounds.center.y > solid.bounds.max.y - .1f) triggered = true;
            if (kind == MapGimmickKind.Cement && ball != null)
            {
                var rb = ball.GetComponent<Rigidbody2D>();
                if (cementVisual != null) { Vector2 point = collision.contactCount > 0 ? collision.GetContact(0).point : rb.position; cementVisual.Impact(transform.InverseTransformPoint(point)); }
                rb.linearVelocity = Vector2.zero; rb.angularVelocity = 0;
            }
            if (kind == MapGimmickKind.BrickWall && (ball != null || player != null) && collision.relativeVelocity.magnitude >= impactSpeed)
            { broken = true; timer = 0; if (solid != null) solid.enabled = false; RefreshVisual(); }
        }
        void OnCollisionStay2D(Collision2D collision)
        {
            if ((kind == MapGimmickKind.Sand && !triggered) || kind == MapGimmickKind.BrickWall) OnCollisionEnter2D(collision);
        }
        void OnTriggerStay2D(Collider2D other)
        {
            if (!initialized || !authority || other.isTrigger || kind == MapGimmickKind.Sprinkler) return;
            var rb = other.attachedRigidbody; if (rb == null || rb.bodyType != RigidbodyType2D.Dynamic) return;
            if (kind == MapGimmickKind.Vent)
            {
                if (rb.transform.IsChildOf(transform) || !windBodiesThisStep.Add(rb)) return;
                Vector3 direction = transform.TransformVector(Vector3.up);
                if (direction.sqrMagnitude < .000001f || solid == null) return;
                // Apply the same force to each body: heavier bodies accelerate less.
                // The overlap's horizontal center lets an off-center beam rotate naturally.
                var area = solid.bounds; var target = other.bounds;
                float lo = Mathf.Max(area.min.x, target.min.x), hi = Mathf.Min(area.max.x, target.max.x);
                float contactX = lo <= hi ? (lo + hi) * .5f : rb.worldCenterOfMass.x;
                rb.AddForceAtPosition((Vector2)direction.normalized * force, new Vector2(contactX, rb.worldCenterOfMass.y));
                return;
            }
            var player = rb.GetComponent<PrototypePlayer>(); var ball = rb.GetComponent<MapGoalBall>();
            if (player == null && ball == null) return;
            switch (kind)
            {
                case MapGimmickKind.Oil: if (player != null) player.TouchOil(oilMaterial); if (ball != null) ball.TouchOil(oilMaterial); break;
                case MapGimmickKind.Magnet: if (active) rb.AddForce(((Vector2)transform.position - rb.position).normalized * force * rb.mass); break;
                case MapGimmickKind.Nitrogen: if (active && player != null && !player.forcedFrozen) player.ForceFreeze(effectSeconds); break;
            }
        }
        void ApplyMovingParts(bool snap)
        {
            if (platformA != null) platformA.transform.localPosition = initialA + Vector3.down * offset;
            if (platformB != null) platformB.transform.localPosition = initialB + Vector3.up * offset;
            if (rotatingBeam != null) rotatingBeam.transform.localRotation = initialRotation * Quaternion.Euler(0, 0, angle);
            if (cranePlatforms != null)
            {
                if (snap && authority && rotatingBeam != null)
                {
                    rotatingBeam.position = transform.TransformPoint(initialBeamPosition);
                    rotatingBeam.rotation = transform.eulerAngles.z + initialRotation.eulerAngles.z + angle;
                    rotatingBeam.linearVelocity = Vector2.zero; rotatingBeam.angularVelocity = 0;
                }
                cranePlatforms.Apply(angle, snap);
            }
        }
        void RefreshVisual()
        {
            bool animatedSand = kind == MapGimmickKind.Sand && sandRenderer != null && sandFrames != null && sandFrames.Length >= 8;
            if (animatedSand)
            {
                float delay = Mathf.Max(.02f, activationDelay);
                float displayTime = timer;
                if (!authority && triggered) displayTime += Mathf.Max(0, Time.time - progressReceivedAt);
                if (!broken) displayTime = Mathf.Min(displayTime, delay - .0001f);
                int frame;
                if (!triggered) frame = 0;
                else if (!broken)
                {
                    float p = Mathf.Clamp01(displayTime / delay);
                    frame = p < .9f / 1.95f ? 0 : p < 1.2f / 1.95f ? 1 : p < 1.55f / 1.95f ? 2 : 3;
                }
                else
                {
                    float burst = Mathf.Max(0, displayTime - delay);
                    frame = displayTime + .0001f >= delay + SandBurstDuration ? -1 : burst < .08f ? 4 : burst < .18f ? 5 : burst < .34f ? 6 : 7;
                }
                sandRenderer.sprite = frame >= 0 ? sandFrames[frame] : null;
            }
            float alpha = !animatedSand && kind == MapGimmickKind.Sand && triggered && !broken ? Mathf.Clamp01(1f - timer / Mathf.Max(.02f, activationDelay)) : 1f;
            if ((kind == MapGimmickKind.Magnet && magnetVisual == null) || (kind == MapGimmickKind.Nitrogen && nitrogenVisual == null)) alpha = active ? 1f : .2f;
            for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null) { var c = initialColors[i]; c.a *= broken && brickVisual == null && !(animatedSand && renderers[i] == sandRenderer) ? 0f : alpha; renderers[i].color = c; }
            if (brickVisual != null) brickVisual.Show(broken, timer + (!authority && broken ? Mathf.Max(0, Time.time - progressReceivedAt) : 0));
            if (magnetVisual != null) magnetVisual.Show(active, timer + (!authority && active ? Mathf.Max(0, Time.time - progressReceivedAt) : 0));
            if (nitrogenVisual != null) nitrogenVisual.Show(active, timer + (!authority && active ? Mathf.Max(0, Time.time - progressReceivedAt) : 0), authority ? activeSeconds : nitrogenDisplayDuration);
            if (beamVisual != null)
            {
                beamVisual.gameObject.SetActive(active && !broken);
                var scale = beamVisual.localScale; scale.y = Mathf.Max(.01f, beamLength); beamVisual.localScale = scale;
                beamVisual.localPosition = new Vector3(0, -beamLength * .5f, 0);
            }
            if (weldingVisual != null)
            {
                // Spark gameplay is a world-down ray, even when the prefab is rotated or scaled.
                Vector3 endWorld = transform.position + Vector3.down * beamLength;
                if (beamVisual != null && beamVisual.parent != null)
                {
                    Vector3 startLocal = beamVisual.parent.InverseTransformPoint(transform.position);
                    Vector3 endLocal = beamVisual.parent.InverseTransformPoint(endWorld);
                    Vector3 direction = startLocal - endLocal;
                    beamVisual.localPosition = (startLocal + endLocal) * .5f;
                    if (direction.sqrMagnitude > .000001f) beamVisual.localRotation = Quaternion.FromToRotation(Vector3.up, direction);
                    var scale = beamVisual.localScale; scale.y = Mathf.Max(.001f, direction.magnitude); beamVisual.localScale = scale;
                }
                float phase = timer + (authority ? 0 : Mathf.Max(0, Time.time - progressReceivedAt));
                weldingVisual.Show(active && !broken, sparkHit, endWorld, phase);
            }
            if (waterVisual != null && WaterFlow != null)
            {
                float phase = timer + (authority ? 0 : Mathf.Max(0, Time.time - progressReceivedAt));
                waterVisual.Show(phase, WaterFlow);
            }
        }
        public GimmickData Capture(int index)
        {
            float stateAngle = kind == MapGimmickKind.Seesaw && authority && rotatingBeam != null
                ? Mathf.DeltaAngle(transform.eulerAngles.z + initialRotation.eulerAngles.z, rotatingBeam.rotation) : angle;
            var state = new GimmickData { index = index, active = active, broken = broken, progress = (kind == MapGimmickKind.Sand || kind == MapGimmickKind.Sparks || kind == MapGimmickKind.Sprinkler || kind == MapGimmickKind.BrickWall || kind == MapGimmickKind.Magnet || kind == MapGimmickKind.Nitrogen) ? timer : 0, offset = offset, angle = stateAngle, beamLength = beamLength, sparkHit = sparkHit };
            if (WaterFlow != null && WaterFlow.Shielded)
            {
                var blocker = WaterFlow.Blocker;
                // Sprinklers reuse the moving-part fields for shape and rotation. Peers rebuild the same water ribbons.
                state.shielded = true;
                state.shieldX = blocker.Center.x;
                state.shieldY = blocker.Center.y;
                state.shieldHalfWidth = blocker.HalfSize.x;
                state.shieldHeight = blocker.HalfSize.y * 2;
                state.offset = blocker.Shape;
                state.angle = blocker.Angle;
            }
            if (kind == MapGimmickKind.Nitrogen) state.nitrogenActiveSeconds = Mathf.Max(.02f, activeSeconds);
            if (cementVisual != null) cementVisual.WriteState(state);
            if (cranePlatforms != null)
            {
                state.hasCraneDimensions = true; state.craneRadius = cranePlatforms.Radius;
                state.craneWidth = cranePlatforms.PlatformSize.x; state.craneHeight = cranePlatforms.PlatformSize.y; state.craneSpeed = rotationSpeed;
            }
            return state;
        }
        public void Apply(GimmickData state, bool snap)
        {
            if (state == null || authority) return;
            if (kind == MapGimmickKind.Nitrogen && state.nitrogenActiveSeconds >= .02f && !float.IsInfinity(state.nitrogenActiveSeconds)) nitrogenDisplayDuration = state.nitrogenActiveSeconds;
            if (cranePlatforms != null && state.hasCraneDimensions)
            {
                if (!Mathf.Approximately(cranePlatforms.Radius, state.craneRadius) || !Mathf.Approximately(cranePlatforms.PlatformSize.x, state.craneWidth) || !Mathf.Approximately(cranePlatforms.PlatformSize.y, state.craneHeight))
                    cranePlatforms.SetDimensions(state.craneRadius, new Vector2(state.craneWidth, state.craneHeight));
                if (!float.IsNaN(state.craneSpeed) && !float.IsInfinity(state.craneSpeed)) rotationSpeed = state.craneSpeed;
            }
            active = state.active; broken = state.broken; triggered = kind == MapGimmickKind.Sand && state.progress > 0; timer = state.progress;
            progressReceivedAt = Time.time;
            targetOffset = WaterFlow == null ? state.offset : 0;
            targetAngle = WaterFlow == null ? state.angle : 0;
            targetBeam = state.beamLength;
            if (kind == MapGimmickKind.Sparks && (state.beamLength < beamLength || state.sparkHit != sparkHit)) beamLength = state.beamLength;
            sparkHit = state.sparkHit;
            if (WaterFlow != null && solid is BoxCollider2D waterBox)
            {
                var blocker = state.shielded ? new WaterBarrierFlow.Obstacle
                {
                    Shape = (int)state.offset,
                    Center = new Vector2(state.shieldX, state.shieldY),
                    HalfSize = new Vector2(state.shieldHalfWidth, state.shieldHeight * .5f),
                    Angle = state.angle
                } : default;
                WaterFlow.Set(transform, WaterArea(waterBox), blocker);
            }
            if (solid != null && (kind == MapGimmickKind.Sand || kind == MapGimmickKind.BrickWall)) solid.enabled = !broken;
            if (snap && windEffect != null) windEffect.ResetAnimation();
            if (cementVisual != null) cementVisual.ApplyState(state);
            if (snap) { offset = targetOffset; angle = targetAngle; beamLength = targetBeam; ApplyMovingParts(true); }
            RefreshVisual();
        }
        void OnDestroy() { if (oilMaterial != null) Destroy(oilMaterial); }
    }
}
