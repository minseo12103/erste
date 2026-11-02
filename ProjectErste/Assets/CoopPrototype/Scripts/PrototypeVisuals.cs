using UnityEngine;

namespace ShapeCoop
{
    public static class PrototypeVisuals
    {
        static Sprite square, triangle, circle;
        public static Color PlayerColor(int index) => new[] { new Color(.2f,.7f,1f), new Color(1f,.37f,.25f), new Color(.7f,.4f,1f) }[index % 3];
        static Sprite SpriteFor(int shape)
        {
            Sprite existing = shape == 0 ? triangle : shape == 1 ? square : circle;
            if (existing != null) return existing;
            const int n = 128; var tex = new Texture2D(n, n, TextureFormat.RGBA32, false); tex.filterMode = FilterMode.Bilinear;
            var pixels = new Color32[n*n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                float px = (x + .5f) / n - .5f, py = (y + .5f) / n - .5f;
                bool visible = shape == 1 || (shape == 2 ? px*px + py*py <= .25f : py >= -.5f && py <= .5f && Mathf.Abs(px) <= (.5f-py)*.5f);
                pixels[y*n+x] = new Color32(255,255,255, visible ? (byte)255 : (byte)0);
            }
            tex.SetPixels32(pixels); tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0,0,n,n), new Vector2(.5f,.5f), n);
            if (shape == 0) triangle = sprite; else if (shape == 1) square = sprite; else circle = sprite;
            return sprite;
        }
        public static GameObject Shape(Transform parent, int shape, Color color, Vector2? scale = null, int order = 1)
        {
            var go = new GameObject("Visual (replaceable)"); go.transform.SetParent(parent, false);
            if (scale.HasValue) go.transform.localScale = scale.Value;
            var renderer = go.AddComponent<SpriteRenderer>(); renderer.sprite = SpriteFor(shape); renderer.color = color; renderer.sortingOrder = order;
            return go;
        }
        public static void Box(Transform parent, string name, Vector2 pos, Vector2 size, Color color)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.position = pos;
            var collider = go.AddComponent<BoxCollider2D>(); collider.size = size;
            Shape(go.transform, 1, color, size, 0);
        }
    }
}
