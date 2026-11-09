using System;
using UnityEngine;

namespace ShapeCoop
{
    // Presentation only: MapGimmick owns the water force and shielding decisions.
    public sealed class WaterBarrierVisual : MonoBehaviour
    {
        public Sprite[] streamFrames = Array.Empty<Sprite>();
        public Sprite[] splashFrames = Array.Empty<Sprite>();
        public Sprite[] splitFrames = Array.Empty<Sprite>();
        public SpriteRenderer splash, split;
        [Min(.1f)] public float framesPerSecond = 8f;
        readonly SpriteRenderer[] streams = new SpriteRenderer[3];

        public void Bind(Transform main, Transform left, Transform right)
        {
            var parts = new[] { main, left, right };
            for (int i = 0; i < parts.Length; i++)
            {
                var art = parts[i] != null ? parts[i].Find("Stream art") : null;
                streams[i] = art != null ? art.GetComponent<SpriteRenderer>() : null;
            }
        }
        public void Show(float elapsed, bool shielded, float shieldX, float shieldY, float shieldWidth, float shieldHeight)
        {
            float fps = float.IsNaN(framesPerSecond) || float.IsInfinity(framesPerSecond) ? 8f : Mathf.Max(.1f, framesPerSecond);
            int frame = Mathf.FloorToInt(Mathf.Max(0, elapsed) * fps);
            if (streamFrames != null && streamFrames.Length > 0)
                foreach (var renderer in streams) if (renderer != null) renderer.sprite = streamFrames[frame % streamFrames.Length];
            bool show = shielded && shieldWidth > .01f && shieldHeight > .01f;
            if (splash != null)
            {
                splash.sprite = show && splashFrames != null && splashFrames.Length > 0 ? splashFrames[frame % splashFrames.Length] : null;
                splash.transform.localPosition = new Vector3(shieldX, shieldY, 0);
                splash.transform.localScale = new Vector3(shieldWidth * 1.2f, .6f, 1);
            }
            if (split != null)
            {
                split.sprite = show && splitFrames != null && splitFrames.Length > 0 ? splitFrames[frame % splitFrames.Length] : null;
                split.transform.localPosition = new Vector3(shieldX, shieldY, 0);
                split.transform.localScale = new Vector3(shieldWidth / (120f / 128f), shieldHeight / (108f / 128f), 1);
            }
        }
    }
}
