using UnityEngine;
using UnityEngine.InputSystem;

namespace ShapeCoop
{
    // Collects a press counter plus the current held state, so a short press is not lost.
    public sealed class EmoteInputReader
    {
        public int sequence, requested;
        public bool held;
        public Vector2 direction = Vector2.right;

        public void Reset() { sequence = requested = 0; held = false; direction = Vector2.right; }
        public void Read(Keyboard keyboard, Camera camera, Vector3 origin, bool enabled)
        {
            if (!enabled || keyboard == null) { held = false; return; }
            if (keyboard.digit1Key.wasPressedThisFrame) { requested = 1; sequence++; }
            if (keyboard.digit2Key.wasPressedThisFrame) { requested = 2; sequence++; }
            if (keyboard.digit3Key.wasPressedThisFrame) { requested = 3; sequence++; }
            held = requested == 1 ? keyboard.digit1Key.isPressed : requested == 2 ? keyboard.digit2Key.isPressed : requested == 3 && keyboard.digit3Key.isPressed;
            if (camera == null || Mouse.current == null) return;
            var ray = camera.ScreenPointToRay(Mouse.current.position.ReadValue());
            var plane = new Plane(Vector3.forward, origin);
            if (!plane.Raycast(ray, out float distance)) return;
            Vector2 aim = ray.GetPoint(distance) - origin;
            if (aim.sqrMagnitude > .0001f) direction = aim.normalized;
        }
    }

    // Visual only. The host owns state; clients render the state from snapshots.
    public sealed class CharacterEmote : MonoBehaviour
    {
        const float DisplayTime = 1.4f;
        static Sprite[] sprites;
        static bool missingImageLogged;
        PrototypePlayer owner;
        SpriteRenderer renderer2D;
        int active, lastSequence;
        bool held, authoritative;
        Vector2 direction = Vector2.right;
        float startedAt;

        public void Initialize(PrototypePlayer player, bool host)
        {
            owner = player; authoritative = host;
            EnsureSprites();
            var visual = new GameObject("Emote hand (visual only)");
            visual.transform.SetParent(transform, false);
            renderer2D = visual.AddComponent<SpriteRenderer>();
            renderer2D.sortingOrder = 5; renderer2D.enabled = false;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetSpriteCache() { sprites = null; missingImageLogged = false; }
        static void EnsureSprites()
        {
            if (sprites != null && sprites.Length == 3 && sprites[0] != null && sprites[1] != null && sprites[2] != null) return;
            var texture = Resources.Load<Texture2D>("ShapeCoop/EmotionHands");
            if (texture == null)
            {
                sprites = null;
                if (!missingImageLogged) { Debug.LogWarning("EmotionHands image missing."); missingImageLogged = true; }
                return;
            }
            missingImageLogged = false;
            // Positions are measured from the top left of the supplied 1254 x 1254 sheet.
            if (sprites == null || sprites.Length != 3) sprites = new Sprite[3];
            if (sprites[0] == null) sprites[0] = Crop(texture, 745, 32, 390, 580);
            if (sprites[1] == null) sprites[1] = Crop(texture, 148, 699, 380, 548);
            if (sprites[2] == null) sprites[2] = Crop(texture, 731, 655, 386, 594);
        }
        static Sprite Crop(Texture2D texture, float x, float y, float width, float height)
        {
            float sx = texture.width / 1254f, sy = texture.height / 1254f;
            return Sprite.Create(texture, new Rect(x * sx, (1254f - y - height) * sy, width * sx, height * sy), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
        }
        static bool Valid(Vector2 value) => !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && value.sqrMagnitude > .0001f;
        public void Input(int sequence, int requested, bool isHeld, Vector2 aim)
        {
            if (!authoritative) return;
            if (sequence > lastSequence)
            {
                lastSequence = sequence;
                if (requested >= 1 && requested <= 3) { active = requested; startedAt = Time.time; }
            }
            held = isHeld && active != 0 && requested == active;
            if (Valid(aim)) direction = aim.normalized;
        }
        public void ResetState()
        {
            active = lastSequence = 0; held = false; direction = Vector2.right;
            if (renderer2D != null) renderer2D.enabled = false;
        }
        public void Capture(BodyData body)
        {
            body.emoteType = active; body.emoteSequence = lastSequence;
            body.emoteX = direction.x; body.emoteY = direction.y;
            body.emoteElapsed = active == 0 ? 0 : Mathf.Max(0, Time.time - startedAt);
        }
        public void Apply(BodyData body)
        {
            if (authoritative || body == null) return;
            active = Mathf.Clamp(body.emoteType, 0, 3); lastSequence = body.emoteSequence;
            var aim = new Vector2(body.emoteX, body.emoteY);
            if (Valid(aim)) direction = aim.normalized;
        }
        void LateUpdate()
        {
            if (owner == null || renderer2D == null) return;
            if (authoritative && active != 0 && !held && Time.time - startedAt >= DisplayTime) active = 0;
            if (active == 0) { renderer2D.enabled = false; return; }
            int spriteIndex = active - 1;
            if (sprites == null || sprites.Length != 3 || sprites[spriteIndex] == null) EnsureSprites();
            var sprite = sprites != null && sprites.Length == 3 ? sprites[spriteIndex] : null;
            if (sprite == null) { renderer2D.enabled = false; return; }
            renderer2D.enabled = true;
            renderer2D.sprite = sprite;
            float playerSize = owner.CurrentCharacterLayout.size;
            renderer2D.transform.position = owner.transform.position + (Vector3)(direction * (.85f * playerSize));
            // Pointing follows the aim. Other signs are perpendicular to it and
            // mirror across the player to keep their orientation natural on either side.
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            bool mirror = active != 1 && direction.x < 0;
            if (active == 1) angle -= 90f;
            else if (mirror) angle += angle >= 0 ? -180f : 180f;
            renderer2D.flipX = mirror;
            renderer2D.transform.rotation = Quaternion.Euler(0, 0, angle);
            float spriteHeight = sprite.bounds.size.y;
            float scale = .85f / Mathf.Max(.001f, spriteHeight);
            renderer2D.transform.localScale = new Vector3(scale, scale, 1);
        }
    }
}
