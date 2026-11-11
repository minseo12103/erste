using System.Collections.Generic;
using UnityEngine;

namespace ShapeCoop
{
    // Scene-authored dynamic bodies that do not already have a gimmick-specific network state.
    public sealed class MapDynamicBodies
    {
        public const int ChunkSize = 8;
        sealed class Entry
        {
            public Rigidbody2D body;
            public Vector2 initialPosition, targetPosition;
            public float initialAngle, targetAngle;
            public int lastSequence = -1;
        }
        readonly Entry[] entries;
        readonly bool authority;
        public int Count => entries.Length;

        public MapDynamicBodies(Transform root, MapGimmick[] gimmicks, bool host)
        {
            authority = host;
            // Section placement changes transforms immediately; synchronize physics before recording starts.
            Physics2D.SyncTransforms();
            var managed = new HashSet<Rigidbody2D>();
            if (gimmicks != null) foreach (var g in gimmicks)
            {
                if (g == null) continue;
                if (g.platformA != null) managed.Add(g.platformA);
                if (g.platformB != null) managed.Add(g.platformB);
                if (g.rotatingBeam != null) managed.Add(g.rotatingBeam);
                var crane = g.GetComponent<RotatingCranePlatforms>();
                if (crane != null && crane.platforms != null) foreach (var platform in crane.platforms) if (platform != null) managed.Add(platform);
            }
            var list = new List<Entry>();
            if (root != null) foreach (var body in root.GetComponentsInChildren<Rigidbody2D>(true))
            {
                if (body == null || body.bodyType != RigidbodyType2D.Dynamic || managed.Contains(body)) continue;
                var e = new Entry { body = body, initialPosition = body.transform.position, initialAngle = body.transform.eulerAngles.z };
                e.targetPosition = e.initialPosition; e.targetAngle = e.initialAngle; list.Add(e);
                if (!host) body.simulated = false;
            }
            entries = list.ToArray();
        }
        public void Reset()
        {
            foreach (var e in entries)
            {
                e.targetPosition = e.initialPosition; e.targetAngle = e.initialAngle; e.lastSequence = -1;
                if (e.body == null) continue;
                if (authority)
                {
                    e.body.position = e.initialPosition; e.body.rotation = e.initialAngle;
                    e.body.linearVelocity = Vector2.zero; e.body.angularVelocity = 0;
                }
                else e.body.transform.SetPositionAndRotation(e.initialPosition, Quaternion.Euler(0, 0, e.initialAngle));
            }
        }
        public MapBodyState[] Capture(int offset)
        {
            int count = Mathf.Clamp(entries.Length - offset, 0, ChunkSize); var result = new MapBodyState[count];
            for (int i = 0; i < count; i++)
            {
                var body = entries[offset + i].body;
                if (body != null) result[i] = new MapBodyState { present = true, x = body.position.x, y = body.position.y, angle = body.rotation };
            }
            return result;
        }
        public void Apply(int offset, MapBodyState[] states, int sequence, bool snap)
        {
            if (authority || states == null || offset < 0 || offset >= entries.Length) return;
            for (int i = 0; i < states.Length && offset + i < entries.Length; i++)
            {
                var e = entries[offset + i]; var state = states[i];
                if (!state.present || e.body == null || sequence <= e.lastSequence) continue;
                e.lastSequence = sequence; e.targetPosition = new Vector2(state.x, state.y); e.targetAngle = state.angle;
                if (snap) e.body.transform.SetPositionAndRotation(e.targetPosition, Quaternion.Euler(0, 0, e.targetAngle));
            }
        }
        public void Interpolate(float factor)
        {
            if (authority) return;
            foreach (var e in entries)
            {
                if (e.body == null) continue;
                var t = e.body.transform; t.position = Vector3.Lerp(t.position, e.targetPosition, factor);
                t.rotation = Quaternion.Lerp(t.rotation, Quaternion.Euler(0, 0, e.targetAngle), factor);
            }
        }
    }
}
