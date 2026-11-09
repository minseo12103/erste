using System;
using UnityEngine;

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
        bool shielded, sparkHit;
        float shieldX, shieldY, shieldHalfWidth, shieldHeight;
        Transform waterMain, waterLeft, waterRight;
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
        BrickWallVisual brickVisual;
        MagnetFieldVisual magnetVisual;
        NitrogenGeyserVisual nitrogenVisual;
        float nitrogenDisplayDuration;
        RotatingCranePlatforms cranePlatforms;
        float progressReceivedAt;
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
            if (kind == MapGimmickKind.Sprinkler && visual != null)
            {
                waterMain = visual.Find("Water");
                if (waterMain != null)
                {
                    waterLeft = Instantiate(waterMain, visual); waterLeft.name = "Water deflected left (runtime)";
                    waterRight = Instantiate(waterMain, visual); waterRight.name = "Water deflected right (runtime)";
                }
                waterVisual = visual.GetComponentInChildren<WaterBarrierVisual>(true);
                if (waterVisual != null) waterVisual.Bind(waterMain, waterLeft, waterRight);
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
            timer = offset = targetOffset = angle = targetAngle = pulleyVelocity = 0; beamLength = targetBeam = beamRange;
            progressReceivedAt = Time.time;
            broken = triggered = shielded = sparkHit = false; active = kind != MapGimmickKind.Sparks && kind != MapGimmickKind.Magnet && kind != MapGimmickKind.Nitrogen;
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
            if (kind == MapGimmickKind.Sprinkler) FindWaterShield();
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
        bool TriangleShields(Vector2 target)
        {
            float height = transform.position.y - target.y;
            if (height <= 0) return false;
            var start = new Vector2(target.x, transform.position.y);
            foreach (var hit in Physics2D.RaycastAll(start, Vector2.down, height))
            {
                if (hit.collider == null || hit.collider.isTrigger || hit.collider.transform.IsChildOf(transform)) continue;
                var player = hit.collider.GetComponentInParent<PrototypePlayer>();
                if (player != null && player.shape == 0) return true;
            }
            return false;
        }
        void FindWaterShield()
        {
            shielded = false;
            if (!(solid is BoxCollider2D box)) return;
            var bounds = box.bounds; float highest = float.NegativeInfinity;
            foreach (var hit in Physics2D.OverlapBoxAll(bounds.center, bounds.size, 0))
            {
                if (hit == null || hit.isTrigger) continue;
                var player = hit.GetComponentInParent<PrototypePlayer>();
                if (player == null || player.shape != 0 || hit.bounds.max.y <= highest) continue;
                highest = hit.bounds.max.y; shielded = true;
                var center = transform.InverseTransformPoint(hit.bounds.center);
                var top = transform.InverseTransformPoint(new Vector3(hit.bounds.center.x, hit.bounds.max.y));
                var left = transform.InverseTransformPoint(new Vector3(hit.bounds.min.x, hit.bounds.center.y));
                var bottom = transform.InverseTransformPoint(new Vector3(hit.bounds.center.x, hit.bounds.min.y));
                shieldX = center.x; shieldY = top.y; shieldHalfWidth = Mathf.Abs(center.x - left.x); shieldHeight = Mathf.Abs(top.y - bottom.y);
            }
        }
        void RefreshWater()
        {
            if (waterMain == null || !(solid is BoxCollider2D box)) return;
            float left = box.offset.x - box.size.x * .5f, right = box.offset.x + box.size.x * .5f;
            float top = box.offset.y + box.size.y * .5f, bottom = box.offset.y - box.size.y * .5f;
            float split = shielded ? Mathf.Clamp(shieldY, bottom, top) : bottom;
            SetWaterPart(waterMain, left, right, split, top);
            float gapLeft = Mathf.Clamp(shieldX - shieldHalfWidth, left, right), gapRight = Mathf.Clamp(shieldX + shieldHalfWidth, left, right);
            SetWaterPart(waterLeft, left, shielded ? gapLeft : left, bottom, split);
            SetWaterPart(waterRight, shielded ? gapRight : right, right, bottom, split);
        }
        static void SetWaterPart(Transform part, float left, float right, float bottom, float top)
        {
            if (part == null) return;
            part.gameObject.SetActive(right - left > .01f && top - bottom > .01f);
            part.localPosition = new Vector3((left + right) * .5f, (bottom + top) * .5f, 0);
            part.localScale = new Vector3(Mathf.Max(.01f, right-left), Mathf.Max(.01f, top-bottom), 1);
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
            if (kind == MapGimmickKind.BrickWall && ball != null && collision.relativeVelocity.magnitude >= impactSpeed)
            { broken = true; timer = 0; if (solid != null) solid.enabled = false; RefreshVisual(); }
        }
        void OnCollisionStay2D(Collision2D collision)
        {
            if (kind == MapGimmickKind.Sand && !triggered) OnCollisionEnter2D(collision);
        }
        void OnTriggerStay2D(Collider2D other)
        {
            if (!initialized || !authority || other.isTrigger) return;
            var rb = other.attachedRigidbody; if (rb == null || rb.bodyType != RigidbodyType2D.Dynamic) return;
            var player = rb.GetComponent<PrototypePlayer>(); var ball = rb.GetComponent<MapGoalBall>();
            if (player == null && ball == null) return;
            switch (kind)
            {
                case MapGimmickKind.Vent: if (ball != null) rb.AddForce(Vector2.up * force * rb.mass); break;
                case MapGimmickKind.Oil: if (player != null) player.TouchOil(oilMaterial); if (ball != null) ball.TouchOil(oilMaterial); break;
                case MapGimmickKind.Magnet: if (active) rb.AddForce(((Vector2)transform.position - rb.position).normalized * force * rb.mass); break;
                case MapGimmickKind.Nitrogen: if (active && player != null) player.ForceFreeze(effectSeconds); break;
                case MapGimmickKind.Sprinkler: if (ball != null && !TriangleShields(rb.position)) rb.AddForce(Vector2.down * force * rb.mass); break;
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
            if (kind == MapGimmickKind.Sprinkler)
            {
                RefreshWater();
                if (waterVisual != null && solid is BoxCollider2D box)
                {
                    float left = box.offset.x - box.size.x * .5f, right = box.offset.x + box.size.x * .5f;
                    float top = box.offset.y + box.size.y * .5f, bottom = box.offset.y - box.size.y * .5f;
                    float gapLeft = Mathf.Clamp(shieldX - shieldHalfWidth, left, right), gapRight = Mathf.Clamp(shieldX + shieldHalfWidth, left, right);
                    float level = Mathf.Clamp(shieldY, bottom, top);
                    float phase = timer + (authority ? 0 : Mathf.Max(0, Time.time - progressReceivedAt));
                    waterVisual.Show(phase, shielded, (gapLeft + gapRight) * .5f, level, gapRight - gapLeft, Mathf.Min(shieldHeight, level - bottom));
                }
            }
        }
        public GimmickData Capture(int index)
        {
            float stateAngle = kind == MapGimmickKind.Seesaw && authority && rotatingBeam != null
                ? Mathf.DeltaAngle(transform.eulerAngles.z + initialRotation.eulerAngles.z, rotatingBeam.rotation) : angle;
            var state = new GimmickData { index = index, active = active, broken = broken, progress = (kind == MapGimmickKind.Sand || kind == MapGimmickKind.Sparks || kind == MapGimmickKind.Sprinkler || kind == MapGimmickKind.BrickWall || kind == MapGimmickKind.Magnet || kind == MapGimmickKind.Nitrogen) ? timer : 0, offset = offset, angle = stateAngle, beamLength = beamLength, shielded = shielded, sparkHit = sparkHit, shieldX = shieldX, shieldY = shieldY, shieldHalfWidth = shieldHalfWidth, shieldHeight = shieldHeight };
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
            targetOffset = state.offset; targetAngle = state.angle; targetBeam = state.beamLength;
            if (kind == MapGimmickKind.Sparks && (state.beamLength < beamLength || state.sparkHit != sparkHit)) beamLength = state.beamLength;
            sparkHit = state.sparkHit;
            shielded = state.shielded; shieldX = state.shieldX; shieldY = state.shieldY; shieldHalfWidth = state.shieldHalfWidth; shieldHeight = state.shieldHeight;
            if (solid != null && (kind == MapGimmickKind.Sand || kind == MapGimmickKind.BrickWall)) solid.enabled = !broken;
            if (snap && windEffect != null) windEffect.ResetAnimation();
            if (cementVisual != null) cementVisual.ApplyState(state);
            if (snap) { offset = targetOffset; angle = targetAngle; beamLength = targetBeam; ApplyMovingParts(true); }
            RefreshVisual();
        }
        void OnDestroy() { if (oilMaterial != null) Destroy(oilMaterial); }
    }
}
