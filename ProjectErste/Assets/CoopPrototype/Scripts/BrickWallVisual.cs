using UnityEngine;

namespace ShapeCoop
{
    // Presentation only; impact and collision decisions remain with MapGimmick.
    public sealed class BrickWallVisual : MonoBehaviour
    {
        public SpriteRenderer wall, rubble, burst;
        public Sprite intact, cracked, rubbleSprite;
        public Sprite[] burstFrames;
        [Min(.02f)] public float frameSeconds = .1f;

        public float Duration => Mathf.Max(.02f, frameSeconds) * (1 + (burstFrames != null ? burstFrames.Length : 0));

        public void Show(bool broken, float elapsed)
        {
            float step = Mathf.Max(.02f, frameSeconds);
            bool cracking = broken && elapsed < step;
            if (wall != null) wall.sprite = !broken ? intact : cracking ? cracked : null;
            if (rubble != null) rubble.sprite = broken && !cracking ? rubbleSprite : null;
            int frame = Mathf.FloorToInt(Mathf.Max(0, elapsed - step) / step);
            if (burst != null) burst.sprite = broken && !cracking && elapsed + .0001f < Duration && burstFrames != null && frame < burstFrames.Length ? burstFrames[frame] : null;
        }
    }
}
