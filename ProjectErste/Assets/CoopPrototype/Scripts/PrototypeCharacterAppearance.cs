using UnityEngine;

namespace ShapeCoop
{
    // Visual-only children: colliders, rigidbody and controls remain on PrototypePlayer.
    public sealed class PrototypeCharacterAppearance : MonoBehaviour
    {
        const float SourceWidth = 1536f, SourceHeight = 1024f;
        static Texture2D sheet;
        static Sprite[] bodies;
        static Sprite helmet, pupilLeft, pupilRight;
        PrototypePlayer player;
        PrototypeSettings settings;
        Transform leftEye, rightEye;
        Transform helmetObject;
        CharacterStunVisual stunVisual;
        CharacterIceVisual iceVisual;
        Vector3 helmetBaseScale;
        Vector3 leftCenter, rightCenter;
        float idleCountdown;
        Vector3 leftEyeVelocity, rightEyeVelocity;
        bool wasMoving;
        public bool IdleEyes { get; private set; }

        public static PrototypeCharacterAppearance Create(PrototypePlayer owner, PrototypeSettings config, int shape)
        {
            var root = new GameObject("Character Visual (replaceable)");
            root.transform.SetParent(owner.transform, false);
            var appearance = root.AddComponent<PrototypeCharacterAppearance>();
            appearance.player = owner; appearance.settings = config;
            appearance.iceVisual = root.AddComponent<CharacterIceVisual>(); appearance.iceVisual.Initialize(owner);
            if (!LoadArtwork())
            {
                Debug.LogWarning("Character artwork missing; using prototype shape.");
                var fallback = PrototypeVisuals.Shape(root.transform, shape, PrototypeVisuals.PlayerColor(owner.colorIndex));
                appearance.stunVisual = root.AddComponent<CharacterStunVisual>(); appearance.stunVisual.Initialize(owner, fallback.transform, null, null, null);
                return appearance;
            }
            int index = Mathf.Clamp(shape, 0, 2);
            var body = CreateSprite(root.transform, "Body", bodies[index], Vector2.zero, Vector2.one, 1);
            Vector2 eyeSize;
            if (index == 0) { appearance.leftCenter = new Vector3(-.145f, -.145f); appearance.rightCenter = new Vector3(.145f, -.145f); eyeSize = new Vector2(.135f, .16f); }
            else if (index == 1) { appearance.leftCenter = new Vector3(-.22f, .07f); appearance.rightCenter = new Vector3(.22f, .07f); eyeSize = new Vector2(.16f, .16f); }
            else { appearance.leftCenter = new Vector3(-.205f, .065f); appearance.rightCenter = new Vector3(.205f, .065f); eyeSize = new Vector2(.15f, .16f); }
            appearance.leftEye = CreateSprite(root.transform, "Pupil Left", pupilLeft, appearance.leftCenter, eyeSize, 2);
            appearance.rightEye = CreateSprite(root.transform, "Pupil Right", pupilRight, appearance.rightCenter, eyeSize, 2);
            appearance.helmetObject = CreateSprite(root.transform, "Helmet", helmet, new Vector2(0, .735f), new Vector2(1.12f, .68f), 3);
            appearance.helmetBaseScale = appearance.helmetObject.localScale;
            appearance.stunVisual = root.AddComponent<CharacterStunVisual>();
            appearance.stunVisual.Initialize(owner, body, appearance.helmetObject, appearance.leftEye, appearance.rightEye);
            appearance.ResetEyes(); return appearance;
        }
        public void SetHelmetLayout(Vector2 offset, float size)
        {
            if (helmetObject == null) return;
            helmetObject.localPosition = new Vector3(offset.x, .735f + offset.y, 0);
            helmetObject.localScale = new Vector3(helmetBaseScale.x * size, helmetBaseScale.y * size, helmetBaseScale.z);
        }
        static bool LoadArtwork()
        {
            if (bodies != null && helmet != null) return true;
            sheet = Resources.Load<Texture2D>("ShapeCoop/character");
            if (sheet == null) return false;
            // Source coordinates are measured from the upper left of the supplied sprite sheet.
            bodies = new[] { Crop(1031, 80, 478, 406), Crop(578, 101, 381, 383), Crop(74, 85, 401, 417) };
            helmet = Crop(44, 632, 457, 279);
            pupilLeft = Crop(733, 716, 71, 70); pupilRight = Crop(1233, 716, 72, 71);
            return true;
        }
        static Sprite Crop(float x, float y, float width, float height)
        {
            float sx = sheet.width / SourceWidth, sy = sheet.height / SourceHeight;
            var rect = new Rect(Mathf.Round(x * sx), Mathf.Round((SourceHeight - y - height) * sy), Mathf.Round(width * sx), Mathf.Round(height * sy));
            return Sprite.Create(sheet, rect, new Vector2(.5f, .5f), 100f, 0, SpriteMeshType.FullRect);
        }
        static Transform CreateSprite(Transform parent, string name, Sprite sprite, Vector2 position, Vector2 size, int order)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = position;
            var renderer = go.AddComponent<SpriteRenderer>(); renderer.sprite = sprite; renderer.sortingOrder = order;
            var bounds = sprite.bounds.size; go.transform.localScale = new Vector3(size.x / bounds.x, size.y / bounds.y, 1);
            return go.transform;
        }
        public void ResetEyes()
        {
            IdleEyes = false; wasMoving = false;
            leftEyeVelocity = rightEyeVelocity = Vector3.zero;
            idleCountdown = NextDelay();
            if (leftEye != null) leftEye.localPosition = leftCenter;
            if (rightEye != null) rightEye.localPosition = rightCenter;
        }
        public void ResetStatus() { if (stunVisual != null) stunVisual.Clear(); if (iceVisual != null) iceVisual.Clear(); }
        float NextDelay()
        {
            float minimum = Mathf.Max(.1f, settings.idleLookMinDelay);
            return Random.Range(minimum, Mathf.Max(minimum, settings.idleLookMaxDelay));
        }
        void Update()
        {
            if (player == null || settings == null || leftEye == null || rightEye == null) return;
            if (player.stunned)
            {
                leftEye.localPosition = leftCenter; rightEye.localPosition = rightCenter;
                leftEyeVelocity = rightEyeVelocity = Vector3.zero; return;
            }
            var velocity = player.AppearanceVelocity;
            bool moving = velocity.sqrMagnitude > .0144f || Mathf.Abs(player.AppearanceAngularVelocity) > 4f;
            if (player.AppearanceAuthority)
            {
                if (moving)
                {
                    if (!wasMoving || IdleEyes) idleCountdown = NextDelay();
                    IdleEyes = false;
                }
                else if (!IdleEyes)
                {
                    idleCountdown -= Time.deltaTime;
                    if (idleCountdown <= 0) IdleEyes = true;
                }
            }
            else IdleEyes = player.RemoteIdleEyes;
            wasMoving = moving;
            float distance = Mathf.Clamp(settings.eyeLookDistance, 0, .08f);
            Vector3 leftOffset = Vector3.zero, rightOffset = Vector3.zero;
            if (IdleEyes)
            {
                leftOffset = new Vector3(-distance, -distance * .65f);
                rightOffset = new Vector3(distance, distance * .65f);
            }
            else if (velocity.sqrMagnitude > .0144f)
            {
                // Mirror the pupils away from their own centers, with a directional tilt.
                // Both remain bounded inside the eyes, even when the body rotates.
                var worldLook = Vector2.ClampMagnitude(velocity / Mathf.Max(1f, settings.moveSpeed * .55f), 1f);
                var localLook = transform.InverseTransformDirection(new Vector3(worldLook.x, worldLook.y, 0));
                float spread = Mathf.Max(Mathf.Abs(localLook.x), Mathf.Abs(localLook.y) * .35f);
                float tilt = Mathf.Clamp(localLook.y * .8f + localLook.x * .45f, -1f, 1f);
                Vector2 offset = Vector2.ClampMagnitude(new Vector2(spread, tilt), 1f) * distance;
                leftOffset = new Vector3(-offset.x, -offset.y);
                rightOffset = new Vector3(offset.x, offset.y);
            }
            // Look changes are quick; settling back to the center is deliberately softer.
            float smoothTime = IdleEyes ? .035f : velocity.sqrMagnitude > .0144f ? .065f : 2f / Mathf.Max(.1f, settings.eyeReturnSpeed);
            leftEye.localPosition = Vector3.SmoothDamp(leftEye.localPosition, leftCenter + leftOffset, ref leftEyeVelocity, smoothTime);
            rightEye.localPosition = Vector3.SmoothDamp(rightEye.localPosition, rightCenter + rightOffset, ref rightEyeVelocity, smoothTime);
        }
    }
}
