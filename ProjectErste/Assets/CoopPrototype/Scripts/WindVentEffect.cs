using System;
using UnityEngine;

namespace ShapeCoop
{
    // Presentation only: the vent's trigger and forces remain on MapGimmick.
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class WindVentEffect : MonoBehaviour
    {
        public Sprite[] frames = Array.Empty<Sprite>();
        [Min(.1f)] public float framesPerSecond = 8f;
        SpriteRenderer spriteRenderer;
        float elapsed;

        void OnEnable() { spriteRenderer = GetComponent<SpriteRenderer>(); ResetAnimation(); }
        public void ResetAnimation()
        {
            elapsed = 0;
            if (spriteRenderer == null) spriteRenderer = GetComponent<SpriteRenderer>();
            if (spriteRenderer != null && frames != null && frames.Length > 0 && frames[0] != null) spriteRenderer.sprite = frames[0];
        }
        void Update() { Advance(Time.deltaTime); }
        void Advance(float deltaTime)
        {
            if (spriteRenderer == null || frames == null || frames.Length == 0) return;
            float fps = float.IsNaN(framesPerSecond) || float.IsInfinity(framesPerSecond) ? 8f : Mathf.Max(.1f, framesPerSecond);
            elapsed = Mathf.Repeat(elapsed + Mathf.Max(0, deltaTime), frames.Length / fps);
            int index = Mathf.Min(frames.Length - 1, Mathf.FloorToInt(elapsed * fps));
            if (frames[index] != null) spriteRenderer.sprite = frames[index];
        }
    }
}
