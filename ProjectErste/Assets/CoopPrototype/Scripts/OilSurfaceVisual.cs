using UnityEngine;

namespace ShapeCoop
{
    // The trigger remains the source of gameplay dimensions; only the artwork is resized here.
    [ExecuteAlways]
    public sealed class OilSurfaceVisual : MonoBehaviour
    {
        public BoxCollider2D area;
        public SpriteRenderer left, middle, right, isolated;
        Vector2 previousSize = new Vector2(-1, -1), previousOffset = new Vector2(float.NaN, float.NaN);

        void OnEnable() { Refresh(); }
        void OnValidate() { previousSize = new Vector2(-1, -1); }
        void LateUpdate()
        {
            if (area != null && (area.size != previousSize || area.offset != previousOffset)) Refresh();
        }
        public void Refresh()
        {
            if (area == null || left == null || middle == null || right == null || isolated == null || left.sprite == null || middle.sprite == null || right.sprite == null || isolated.sprite == null) return;
            previousSize = area.size; previousOffset = area.offset;
            float width = Mathf.Max(0, area.size.x), capWidth = left.sprite.bounds.size.x;
            float center = area.offset.x, floor = area.offset.y - area.size.y * .5f;
            bool shortPuddle = width < capWidth * 2f;
            isolated.enabled = shortPuddle && width > 0;
            left.enabled = right.enabled = !shortPuddle;
            middle.enabled = !shortPuddle && width > capWidth * 2f;
            isolated.transform.localPosition = new Vector3(center, floor + 14f / 128f, 0);
            isolated.transform.localScale = new Vector3(width / isolated.sprite.bounds.size.x, 1, 1);
            left.transform.localPosition = new Vector3(center - (width - capWidth) * .5f, floor + 19f / 128f, 0);
            right.transform.localPosition = new Vector3(center + (width - capWidth) * .5f, floor + 19f / 128f, 0);
            middle.transform.localPosition = new Vector3(center, floor + 19f / 128f, 0);
            middle.size = new Vector2(Mathf.Max(.001f, width - capWidth * 2f), middle.sprite.bounds.size.y);
        }
    }
}
