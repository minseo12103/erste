using UnityEngine;

namespace ShapeCoop
{
    // The host moves six kinematic platforms; clients derive their positions from the shared wheel angle.
    public sealed class RotatingCranePlatforms : MonoBehaviour
    {
        public Rigidbody2D[] platforms;
        [SerializeField] float radius = 2.15f;
        [SerializeField] Vector2 platformSize = new Vector2(1.12f, .19f);
        bool authority;
        Vector3 center;
        Quaternion initialRotation;
        Vector2[] velocities;
        public float Radius => radius;
        public Vector2 PlatformSize => platformSize;

        static float Safe(float value, float minimum, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Max(minimum, value);
        public void SetDimensions(float newRadius, Vector2 newSize)
        {
            radius = Safe(newRadius, .3f, 2.15f);
            platformSize = new Vector2(Safe(newSize.x, .1f, 1.12f), Safe(newSize.y, .05f, .19f));
            RefreshGeometry();
        }
        // Adjust visual spokes and every physical platform from the same dimensions.
        public void RefreshGeometry()
        {
            var g = GetComponent<MapGimmick>();
            var hub = g != null && g.rotatingBeam != null ? g.rotatingBeam.transform : null;
            if (hub != null)
            {
                foreach (string name in new[] { "Visual", "Spoke 60", "Spoke 120" })
                {
                    var part = hub.Find(name); var renderer = part != null ? part.GetComponent<SpriteRenderer>() : null;
                    if (renderer != null) { var size = renderer.size; size.x = 2f * radius + .88f; renderer.size = size; }
                    var collision = part != null ? part.Find("Spoke collision") : null;
                    var box = collision != null ? collision.GetComponent<BoxCollider2D>() : null;
                    if (box != null) box.size = new Vector2(2f * radius + .22f, .23f);
                }
            }
            if (platforms == null) return;
            float sx = platformSize.x / 1.12f, sy = platformSize.y / .19f;
            foreach (var platform in platforms)
            {
                if (platform == null) continue;
                var collider = platform.GetComponent<BoxCollider2D>();
                if (collider != null) { collider.size = platformSize; collider.offset = new Vector2(0, .395f * sy); }
                var art = platform.transform.Find("Platform art");
                if (art != null) { art.localPosition = new Vector3(0, .49f * sy, 0); art.localScale = new Vector3(sx, sy, 1); }
            }
        }
        // Used by the authoring UI, without starting physics or advancing the game clock.
        public void RefreshPreview()
        {
            RefreshGeometry();
            var g = GetComponent<MapGimmick>(); var hub = g != null ? g.rotatingBeam : null;
            Vector3 origin = hub != null ? hub.transform.localPosition : Vector3.zero;
            Quaternion rotation = hub != null ? hub.transform.localRotation : Quaternion.identity;
            if (platforms == null || platforms.Length == 0) return;
            for (int i = 0; i < platforms.Length; i++)
            {
                if (platforms[i] == null) continue;
                float a = 360f * i / platforms.Length * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0) * radius;
                platforms[i].transform.position = transform.TransformPoint(origin + rotation * offset);
                platforms[i].transform.rotation = Quaternion.identity;
            }
        }

        public void Initialize(bool host, Rigidbody2D hub)
        {
            authority = host;
            SetDimensions(radius, platformSize);
            center = hub != null ? hub.transform.localPosition : Vector3.zero;
            initialRotation = hub != null ? hub.transform.localRotation : Quaternion.identity;
            velocities = platforms != null ? new Vector2[platforms.Length] : null;
            if (!host && platforms != null) foreach (var platform in platforms) if (platform != null) platform.simulated = false;
        }
        public void Apply(float angle, bool snap)
        {
            if (platforms == null || platforms.Length == 0) return;
            for (int i = 0; i < platforms.Length; i++)
            {
                var platform = platforms[i]; if (platform == null) continue;
                float radians = (angle + 360f * i / platforms.Length) * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0) * radius;
                Vector3 target = transform.TransformPoint(center + initialRotation * offset);
                if (authority)
                {
                    if (snap)
                    {
                        if (velocities != null) velocities[i] = Vector2.zero;
                        platform.position = target; platform.rotation = 0;
                        platform.linearVelocity = Vector2.zero; platform.angularVelocity = 0;
                    }
                    else
                    {
                        if (velocities != null) velocities[i] = ((Vector2)target - platform.position) / Mathf.Max(.0001f, Time.fixedDeltaTime);
                        platform.MovePosition(target); platform.MoveRotation(0);
                    }
                }
                else { platform.transform.position = target; platform.transform.rotation = Quaternion.identity; }
            }
        }
        public bool TryGetVelocity(Rigidbody2D platform, out Vector2 velocity)
        {
            velocity = Vector2.zero;
            if (!authority || platforms == null || velocities == null) return false;
            for (int i = 0; i < platforms.Length; i++) if (platforms[i] == platform) { velocity = velocities[i]; return true; }
            return false;
        }
    }
}
