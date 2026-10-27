using UnityEngine;

namespace Octopusdream
{
    /// <summary>
    /// Contact-driven visual body. A cached surface field and damped vertex constraints
    /// let the painted shell spread and drape without networking thousands of particles.
    /// The swimming controller remains responsible for locomotion.
    /// </summary>
    public sealed class PrototypeSurfaceBody
    {
        const int Grid = 13;
        const float Skin = .008f;
        readonly float[] heights = new float[Grid * Grid];
        Vector3[] velocities;
        Vector3 lastVelocity, inertia, inertiaVelocity, lastSamplePosition;
        Quaternion lastSampleRotation;
        float sampleAge = 10f, supportY, extent, contact, sampledSoftness = -1f;
        bool hasVelocity, hasField;
        Vector3 lookupOrigin, lookupX, lookupZ;
        readonly Ledge[] ledges = new Ledge[16];
        int ledgeCount;
        float bendWidth;
        struct Ledge { public Vector2 point, normal; }

        public float Contact => contact;

        public void Reset()
        {
            inertia = inertiaVelocity = lastVelocity = Vector3.zero;
            if (velocities != null) System.Array.Clear(velocities, 0, velocities.Length);
            hasVelocity = false;
            contact = 0f;
            sampleAge = 10f;
            hasField = false;
        }

        public void Impact(Transform frame, Vector3 normal, float strength, float softness)
        {
            inertiaVelocity += frame.InverseTransformDirection(normal).normalized * Mathf.Clamp(strength, 0f, 12f) * softness * .012f;
        }

        public void Step(Transform frame, Vector3[] rest, Vector3[] vertices, float softness, Vector3 worldVelocity, float deltaTime)
        {
            float dt = Mathf.Clamp(deltaTime, 0f, .05f);
            if (dt <= 0f) return;
            if (velocities == null || velocities.Length != rest.Length) velocities = new Vector3[rest.Length];
            float s = Mathf.Clamp01(softness * .1f);
            float radius = PrototypeAppearance.OctopusRadius;
            float spread = s <= .5f ? Mathf.Lerp(1f, 1.23f, s * 2f) : Mathf.Lerp(1.23f, 2.1f, (s - .5f) * 2f);
            float height = s <= .5f ? Mathf.Lerp(radius * 2f, .98f, s * 2f) : Mathf.Lerp(.98f, .14f, (s - .5f) * 2f);

            sampleAge += dt;
            bool moved = (frame.position - lastSamplePosition).sqrMagnitude > .012f || Quaternion.Angle(frame.rotation, lastSampleRotation) > 8f;
            if (sampleAge >= .09f || moved || Mathf.Abs(sampledSoftness - softness) > .05f)
                SampleSurface(frame, radius * spread + .16f, softness);
            Quaternion inverseSample = Quaternion.Inverse(lastSampleRotation);
            lookupOrigin = inverseSample * (frame.position - lastSamplePosition);
            lookupX = inverseSample * frame.right;
            lookupZ = inverseSample * frame.forward;

            float gap = frame.position.y - radius - supportY;
            float wantedContact = hasField ? 1f - Mathf.InverseLerp(.38f, .65f, gap) : 0f;
            if (gap < -.2f || worldVelocity.y > .8f) wantedContact = 0f;
            contact = Mathf.MoveTowards(contact, wantedContact, dt * (wantedContact > contact ? 5f : 9f));

            if (hasVelocity)
                inertiaVelocity -= Vector3.ClampMagnitude(frame.InverseTransformDirection(worldVelocity - lastVelocity), 10f) * s * .15f;
            lastVelocity = worldVelocity;
            hasVelocity = true;
            float frequency = Mathf.Lerp(25f, 10f, s);
            int steps = Mathf.Max(1, Mathf.CeilToInt(dt / .012f));
            float step = dt / steps;
            for (int iteration = 0; iteration < steps; iteration++)
            {
                inertiaVelocity += (-inertia * 45f - inertiaVelocity * 8f) * step;
                inertia += inertiaVelocity * step;
                inertia = Vector3.ClampMagnitude(inertia, .18f * s);
                for (int i = 0; i < rest.Length; i++)
                {
                    Vector3 n = rest[i] / radius;
                    Vector3 air = rest[i] + inertia * (.25f + .75f * (1f - n.y * n.y));
                    Vector3 target = air;
                    if (contact > 0f && hasField)
                    {
                        Vector3 grounded = new Vector3(rest[i].x * spread, 0f, rest[i].z * spread);
                        grounded.x += inertia.x * .3f;
                        grounded.z += inertia.z * .3f;
                        float surface = SurfaceAt(grounded.x, grounded.z, true);
                        float drape = Mathf.Lerp(0f, .8f, Mathf.Pow(s, .8f));
                        float bed = Mathf.Max(surface, supportY - drape);
                        float sphereHeight = (n.y + 1f) * .5f;
                        float domeHeight = n.y >= 0f ? .035f + .965f * n.y : .035f * (n.y + 1f);
                        float profile = Mathf.Lerp(sphereHeight, domeHeight, Mathf.Clamp01(s * 2f));
                        float domeResistance = Mathf.Lerp(.75f, 0f, Mathf.InverseLerp(.5f, 1f, s));
                        float drop = supportY - bed;
                        if (drop > 0f) bed += drop * profile * domeResistance;
                        grounded.y = bed - frame.position.y + Skin + profile * height;
                        // The unsupported rim settles downward; the thick middle retains its dome.
                        grounded.y += inertia.y * profile * .15f;
                        target = Vector3.Lerp(air, grounded, contact);
                    }
                    if (s <= .0001f)
                    {
                        vertices[i] = target;
                        velocities[i] = Vector3.zero;
                    }
                    else
                    {
                        velocities[i] += ((target - vertices[i]) * (frequency * frequency) - velocities[i] * (frequency * 1.7f)) * step;
                        vertices[i] += velocities[i] * step;
                    }
                    if (contact > .95f && hasField)
                    {
                        float minimum = SurfaceAt(vertices[i].x, vertices[i].z, false) - frame.position.y + Skin;
                        if (vertices[i].y < minimum)
                        {
                            vertices[i].y = minimum;
                            velocities[i].y = Mathf.Max(0f, velocities[i].y);
                        }
                    }
                }
            }
        }

        void SampleSurface(Transform frame, float newExtent, float softness)
        {
            sampleAge = 0f;
            sampledSoftness = softness;
            lastSamplePosition = frame.position;
            lastSampleRotation = frame.rotation;
            extent = newExtent;
            hasField = false;
            ledgeCount = 0;
            bendWidth = Mathf.Lerp(.4f, .25f, Mathf.InverseLerp(5f, 10f, softness));
            // Only a nearby floor anchors the body. Empty water does not pull it down.
            float closest = float.PositiveInfinity;
            float selected = 0f;
            Vector3 anchor = frame.position;
            for (int i = 0; i < 9; i++)
            {
                float angle = (i - 1) * Mathf.PI * .25f;
                Vector3 offset = i == 0 ? Vector3.zero : new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * .22f;
                Vector3 origin = frame.position + offset + Vector3.up * .2f;
                if (!Physics.Raycast(origin, Vector3.down, out var hit, 1.55f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) || hit.normal.y < .25f) continue;
                float gap = frame.position.y - PrototypeAppearance.OctopusRadius - hit.point.y;
                if (gap < -.2f || gap > .65f || Mathf.Abs(gap) >= closest) continue;
                closest = Mathf.Abs(gap);
                selected = hit.point.y;
                anchor = hit.point;
                hasField = true;
            }
            if (!hasField) return;
            supportY = selected;
            for (int z = 0; z < Grid; z++)
                for (int x = 0; x < Grid; x++)
                {
                    Vector3 local = new Vector3(Mathf.Lerp(-extent, extent, x / (float)(Grid - 1)), 0f, Mathf.Lerp(-extent, extent, z / (float)(Grid - 1)));
                    Vector3 point = frame.TransformPoint(local);
                    Vector3 origin = new Vector3(point.x, Mathf.Max(frame.position.y + .8f, supportY + 1.1f), point.z);
                    float floor = supportY - .85f;
                    if (Physics.Raycast(origin, Vector3.down, out var hit, origin.y - supportY + 1.1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) && hit.normal.y > .2f)
                        floor = hit.point.y;
                    heights[z * Grid + x] = floor;
                }
            // Detect actual side planes so a diagonal stair lip does not become
            // a jagged row of vertical ridges in the cached support field.
            for (int i = 0; i < 16; i++)
            {
                float angle = i * Mathf.PI / 8f;
                Vector3 local = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * extent;
                Vector3 outside = frame.TransformPoint(local);
                Vector3 above = new Vector3(outside.x, supportY + .5f, outside.z);
                float lower = supportY - .85f;
                if (Physics.Raycast(above, Vector3.down, out var floorHit, 1.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) lower = floorHit.point.y;
                if (lower >= supportY - .08f) continue;
                outside.y = supportY - .025f;
                Vector3 inside = new Vector3(anchor.x, outside.y, anchor.z);
                Vector3 delta = inside - outside;
                if (!Physics.Raycast(outside, delta.normalized, out var wall, delta.magnitude + .04f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) || Mathf.Abs(wall.normal.y) > .3f) continue;
                Vector3 localPoint = frame.InverseTransformPoint(wall.point);
                Vector3 localNormal = frame.InverseTransformDirection(wall.normal);
                var candidate = new Ledge { point = new Vector2(localPoint.x, localPoint.z), normal = new Vector2(localNormal.x, localNormal.z).normalized };
                bool duplicate = false;
                for (int j = 0; j < ledgeCount; j++)
                    if (Vector2.Dot(candidate.normal, ledges[j].normal) > .98f && Mathf.Abs(Vector2.Dot(candidate.point - ledges[j].point, candidate.normal)) < .04f) { duplicate = true; break; }
                if (!duplicate && ledgeCount < ledges.Length) ledges[ledgeCount++] = candidate;
            }
        }

        float SurfaceAt(float x, float z, bool roundedBend)
        {
            float sampleX = lookupOrigin.x + lookupX.x * x + lookupZ.x * z;
            float sampleZ = lookupOrigin.z + lookupX.z * x + lookupZ.z * z;
            x = sampleX;
            z = sampleZ;
            float gx = Mathf.Clamp((x / extent + 1f) * .5f * (Grid - 1), 0f, Grid - 1.001f);
            float gz = Mathf.Clamp((z / extent + 1f) * .5f * (Grid - 1), 0f, Grid - 1.001f);
            int ix = (int)gx, iz = (int)gz;
            float a = heights[iz * Grid + ix], b = heights[iz * Grid + ix + 1];
            float c = heights[(iz + 1) * Grid + ix], d = heights[(iz + 1) * Grid + ix + 1];
            float highest = Mathf.Max(Mathf.Max(a, b), Mathf.Max(c, d));
            float lowest = Mathf.Min(Mathf.Min(a, b), Mathf.Min(c, d));
            float floor = Mathf.Lerp(Mathf.Lerp(a, b, gx - ix), Mathf.Lerp(c, d, gx - ix), gz - iz);
            float closestDistance = float.PositiveInfinity, signedDistance = 0f;
            Vector2 point = new Vector2(x, z);
            for (int i = 0; i < ledgeCount; i++)
            {
                float distance = Vector2.Dot(point - ledges[i].point, ledges[i].normal);
                if (Mathf.Abs(distance) < closestDistance) { closestDistance = Mathf.Abs(distance); signedDistance = distance; }
            }
            if (highest - lowest > .08f)
                floor = ledgeCount > 0 && Mathf.Abs(highest - supportY) < .12f ? (signedDistance > 0f ? lowest : highest) : highest;
            if (roundedBend && ledgeCount > 0 && floor < supportY - .08f && signedDistance > 0f)
            {
                float t = Mathf.Clamp01(signedDistance / bendWidth);
                t = t * t * (3f - 2f * t);
                floor = Mathf.Lerp(supportY, floor, t);
            }
            return floor;
        }
    }
}
