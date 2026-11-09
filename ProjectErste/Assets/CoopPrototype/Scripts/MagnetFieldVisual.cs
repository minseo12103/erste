using System;
using UnityEngine;

namespace ShapeCoop
{
    // Presentation only. MapGimmick controls activation and the actual force.
    [ExecuteAlways]
    public sealed class MagnetFieldVisual : MonoBehaviour
    {
        public BoxCollider2D area;
        public SpriteRenderer device, field;
        public Sprite deviceOff;
        public Sprite[] deviceOnFrames = Array.Empty<Sprite>();
        public Sprite[] fieldFrames = Array.Empty<Sprite>();
        [Min(.1f)] public float framesPerSecond = 8f;

        public void Show(bool active, float elapsed)
        {
            RefreshGeometry();
            float fps = float.IsNaN(framesPerSecond) || float.IsInfinity(framesPerSecond) ? 8f : Mathf.Max(.1f, framesPerSecond);
            int frame = Mathf.FloorToInt(Mathf.Max(0, elapsed) * fps);
            if (device != null) device.sprite = active && deviceOnFrames != null && deviceOnFrames.Length > 0 ? deviceOnFrames[frame % deviceOnFrames.Length] : deviceOff;
            if (field != null) field.sprite = active && fieldFrames != null && fieldFrames.Length > 0 ? fieldFrames[frame % fieldFrames.Length] : null;
        }
        public void RefreshGeometry()
        {
            if (area == null || field == null || fieldFrames == null || fieldFrames.Length == 0 || fieldFrames[0] == null) return;
            var bounds = fieldFrames[0].bounds.size;
            if (bounds.x <= 0 || bounds.y <= 0) return;
            field.transform.localPosition = new Vector3(area.offset.x, area.offset.y, 0);
            field.transform.localScale = new Vector3(area.size.x / bounds.x, area.size.y / bounds.y, 1);
        }
        void Update() { if (!Application.isPlaying) RefreshGeometry(); }
    }
}
