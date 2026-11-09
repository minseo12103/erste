using UnityEngine;

namespace ShapeCoop
{
    // Presentation follows the replicated stun flag; it never changes the player's physics.
    public sealed class CharacterStunVisual : MonoBehaviour
    {
        PrototypePlayer player;
        CharacterStunArt art;
        SpriteRenderer body, helmet, leftPupil, rightPupil, outerOutline, innerOutline, flash;
        Color bodyColor, helmetColor;
        bool leftPupilWasEnabled, rightPupilWasEnabled, showing;
        float started;

        public void Initialize(PrototypePlayer owner, Transform bodyPart, Transform helmetPart, Transform leftEye, Transform rightEye)
        {
            player = owner; art = Resources.Load<CharacterStunArt>("ShapeCoop/CharacterStunArt");
            body = bodyPart != null ? bodyPart.GetComponent<SpriteRenderer>() : null;
            helmet = helmetPart != null ? helmetPart.GetComponent<SpriteRenderer>() : null;
            leftPupil = leftEye != null ? leftEye.GetComponent<SpriteRenderer>() : null;
            rightPupil = rightEye != null ? rightEye.GetComponent<SpriteRenderer>() : null;
            if (art == null) return;
            var outlineMaterial = Resources.Load<Material>("ShapeCoop/CharacterStunOutline");
            if (body != null && outlineMaterial != null)
            {
                outerOutline = MakeRenderer("Stun outline glow", body.sortingOrder - 2);
                innerOutline = MakeRenderer("Stun outline core", body.sortingOrder - 1);
                outerOutline.sharedMaterial = innerOutline.sharedMaterial = outlineMaterial;
                outerOutline.enabled = innerOutline.enabled = false;
            }
            flash = MakeRenderer("Electric impact", 5); flash.enabled = false;
        }
        SpriteRenderer MakeRenderer(string name, int sortingOrder)
        {
            var go = new GameObject(name); go.transform.SetParent(transform, false);
            var result = go.AddComponent<SpriteRenderer>(); result.sortingOrder = sortingOrder; return result;
        }
        static void SetSize(SpriteRenderer renderer, Vector2 size)
        {
            if (renderer == null || renderer.sprite == null) return;
            Vector3 bounds = renderer.sprite.bounds.size;
            renderer.transform.localScale = new Vector3(size.x / Mathf.Max(.001f, bounds.x), size.y / Mathf.Max(.001f, bounds.y), 1);
        }
        void LateUpdate()
        {
            if (player == null || !player.stunned) { Clear(); return; }
            if (!showing)
            {
                showing = true; started = Time.time;
                if (body != null) bodyColor = body.color;
                if (helmet != null) helmetColor = helmet.color;
                if (leftPupil != null) { leftPupilWasEnabled = leftPupil.enabled; leftPupil.enabled = false; }
                if (rightPupil != null) { rightPupilWasEnabled = rightPupil.enabled; rightPupil.enabled = false; }
            }
            if (art == null) return;
            float age = Mathf.Max(0, Time.time - started);
            float pulse = .75f + .25f * Mathf.Sin(age * Mathf.Max(.1f, art.outlinePulseSpeed));
            Color tint = Color.Lerp(Color.white, art.tint, Mathf.Clamp01(art.tintStrength * pulse));
            if (body != null) body.color = bodyColor * tint;
            if (helmet != null) helmet.color = helmetColor * tint;
            if (outerOutline != null && innerOutline != null && body != null && body.sprite != null)
            {
                UpdateOutline(outerOutline, 1.14f, new Color(.1f, .64f, 1f, .3f * pulse));
                UpdateOutline(innerOutline, 1.07f, new Color(.42f, .94f, 1f, .9f * pulse));
            }
            bool showImpact = age < Mathf.Max(.02f, art.impactDuration) && art.impacts != null && art.impacts.Length > 0;
            flash.enabled = showImpact;
            if (showImpact)
            {
                int impactFrame = Mathf.Min(art.impacts.Length - 1, Mathf.FloorToInt(age / Mathf.Max(.02f, art.impactDuration) * art.impacts.Length));
                flash.sprite = art.impacts[impactFrame];
                float top = helmet != null && helmet.sprite != null ? helmet.transform.localPosition.y + helmet.sprite.bounds.size.y * Mathf.Abs(helmet.transform.localScale.y) * .5f : .55f;
                flash.transform.localPosition = new Vector3(helmet != null ? helmet.transform.localPosition.x : 0, top, 0);
                SetSize(flash, new Vector2(.72f, .49f));
            }
        }
        void UpdateOutline(SpriteRenderer outline, float scale, Color color)
        {
            outline.sprite = body.sprite; outline.flipX = body.flipX; outline.flipY = body.flipY;
            outline.transform.localPosition = body.transform.localPosition;
            outline.transform.localRotation = body.transform.localRotation;
            outline.transform.localScale = body.transform.localScale * scale;
            outline.color = color; outline.enabled = body.enabled;
        }
        public void Clear()
        {
            if (!showing) return;
            showing = false;
            if (body != null) body.color = bodyColor;
            if (helmet != null) helmet.color = helmetColor;
            if (leftPupil != null) leftPupil.enabled = leftPupilWasEnabled;
            if (rightPupil != null) rightPupil.enabled = rightPupilWasEnabled;
            if (outerOutline != null) outerOutline.enabled = false;
            if (innerOutline != null) innerOutline.enabled = false;
            if (flash != null) flash.enabled = false;
        }
        void OnDisable() { Clear(); }
    }
}
