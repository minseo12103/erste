using System;
using UnityEngine;

namespace ShapeCoop
{
    // The collider, activation and freeze duration are controlled by MapGimmick.
    [ExecuteAlways]
    public sealed class NitrogenGeyserVisual : MonoBehaviour
    {
        public BoxCollider2D area;
        public SpriteRenderer gas, activeArea;
        public Sprite[] frames = Array.Empty<Sprite>();

        public void Show(bool active, float elapsed, float activeSeconds)
        {
            RefreshGeometry();
            int count = frames != null ? frames.Length : 0;
            float duration = Mathf.Max(.02f, activeSeconds);
            int frame = count > 0 ? Mathf.Min(count - 1, Mathf.FloorToInt(Mathf.Clamp01(elapsed / duration) * count)) : 0;
            if (gas != null) gas.sprite = active && count > 0 ? frames[frame] : null;
            // Keep the whole active area visible even during the small start/end puffs.
            if (activeArea != null) activeArea.sprite = active && count > 2 ? frames[2] : null;
        }
        public void RefreshGeometry()
        {
            if (area == null || frames == null || frames.Length == 0 || frames[0] == null) return;
            var size = frames[0].bounds.size;
            if (size.x <= 0 || size.y <= 0) return;
            SetGeometry(gas, size); SetGeometry(activeArea, size);
        }
        void SetGeometry(SpriteRenderer renderer, Vector3 size)
        {
            if (renderer == null) return;
            renderer.transform.localPosition = new Vector3(area.offset.x, area.offset.y, 0);
            renderer.transform.localScale = new Vector3(area.size.x / size.x, area.size.y / size.y, 1);
        }
        void Update() { if (!Application.isPlaying) RefreshGeometry(); }
    }
}
