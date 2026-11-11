using UnityEngine;

namespace ShapeCoop
{
    // In emitter space, each row is the pair of visible water edges at one height.
    // Rendering and force sampling both use these same rows.
    internal sealed class WaterBarrierFlow
    {
        const int ContourSteps = 16;
        const int FallSteps = 16;
        const float Epsilon = .0001f;

        internal struct Obstacle
        {
            internal int Shape;
            internal Vector2 Center;
            // World-space half extents before rotation; Angle is relative to the emitter.
            internal Vector2 HalfSize;
            internal float Angle;
        }

        internal sealed class Ribbon
        {
            internal readonly Vector2[] Edges = new Vector2[(ContourSteps + FallSteps + 1) * 2];
            internal int Count { get; private set; }

            internal void Clear() => Count = 0;

            internal void Add(float y, float left, float right)
            {
                Edges[Count * 2] = new Vector2(left, y);
                Edges[Count * 2 + 1] = new Vector2(right, y);
                Count++;
            }

            internal bool TryGetCurrent(Vector2 point, out Vector2 direction)
            {
                direction = default;
                if (Count < 2 || point.y > Edges[0].y || point.y < Edges[(Count - 1) * 2].y) return false;

                for (int i = 0; i < (Count - 1) * 2; i += 2)
                {
                    var top = Edges[i];
                    var bottom = Edges[i + 2];
                    if (point.y < bottom.y) continue;
                    float t = Mathf.InverseLerp(top.y, bottom.y, point.y);
                    float left = Mathf.Lerp(top.x, bottom.x, t);
                    float right = Mathf.Lerp(Edges[i + 1].x, Edges[i + 3].x, t);
                    if (point.x < left || point.x > right) return false;
                    direction = (bottom + Edges[i + 3] - top - Edges[i + 1]).normalized;
                    return true;
                }
                return false;
            }
        }

        readonly Vector2[] outline = new Vector2[40];
        int outlineCount;
        bool initialized;
        Rect area;

        internal readonly Ribbon Main = new Ribbon();
        internal readonly Ribbon Left = new Ribbon();
        internal readonly Ribbon Right = new Ribbon();
        internal bool Shielded { get; private set; }
        internal Vector2 Impact { get; private set; }
        internal float ImpactWidth { get; private set; }
        internal Rect Bounds { get; private set; }
        internal Obstacle Blocker { get; private set; }

        internal bool TryGetImpact(Transform space, Rect stream, Obstacle obstacle, out Vector2 impact)
        {
            impact = default;
            if (obstacle.Shape < 0 || obstacle.Shape > 2 ||
                !float.IsFinite(obstacle.Angle) || !float.IsFinite(obstacle.Center.x) || !float.IsFinite(obstacle.Center.y) ||
                !float.IsFinite(obstacle.HalfSize.x) || !float.IsFinite(obstacle.HalfSize.y) ||
                obstacle.HalfSize.x <= Epsilon || obstacle.HalfSize.y <= Epsilon)
                return false;

            BuildOutline(space, obstacle);
            float crest = float.NegativeInfinity, crestLeft = 0, crestRight = 0;
            float hit = float.NegativeInfinity, baseY = float.PositiveInfinity;
            float minX = float.PositiveInfinity, maxX = float.NegativeInfinity;
            for (int i = 0; i < outlineCount; i++)
            {
                var point = outline[i];
                var next = outline[(i + 1) % outlineCount];
                if (point.y > crest + Epsilon)
                {
                    crest = point.y;
                    crestLeft = crestRight = point.x;
                }
                else if (Mathf.Abs(point.y - crest) <= Epsilon)
                {
                    crestLeft = Mathf.Min(crestLeft, point.x);
                    crestRight = Mathf.Max(crestRight, point.x);
                }
                minX = Mathf.Min(minX, point.x);
                maxX = Mathf.Max(maxX, point.x);
                baseY = Mathf.Min(baseY, point.y);
                if (point.x >= stream.xMin && point.x <= stream.xMax) hit = Mathf.Max(hit, point.y);
                hit = Mathf.Max(hit, Mathf.Max(EdgeAtX(point, next, stream.xMin), EdgeAtX(point, next, stream.xMax)));
            }

            if (maxX <= stream.xMin + Epsilon || minX >= stream.xMax - Epsilon ||
                hit <= stream.yMin + Epsilon || baseY >= stream.yMax) return false;

            float x = Mathf.Clamp((crestLeft + crestRight) * .5f, stream.xMin, stream.xMax);
            impact = new Vector2(x, Mathf.Min(hit, stream.yMax));
            return true;
        }

        internal void Set(Transform space, Rect stream, Obstacle obstacle)
        {
            var previous = Blocker;
            if (initialized && area.Equals(stream) && previous.Shape == obstacle.Shape &&
                previous.Center.Equals(obstacle.Center) && previous.HalfSize.Equals(obstacle.HalfSize) && previous.Angle == obstacle.Angle) return;

            initialized = true;
            area = stream;
            Blocker = obstacle;
            Main.Clear();
            Left.Clear();
            Right.Clear();
            Shielded = TryGetImpact(space, stream, obstacle, out var impact);
            Impact = impact;
            ImpactWidth = 0;
            Bounds = stream;
            float end = Shielded ? impact.y : stream.yMin;
            if (stream.yMax > end + Epsilon)
            {
                Main.Add(stream.yMax, stream.xMin, stream.xMax);
                Main.Add(end, stream.xMin, stream.xMax);
            }
            if (!Shielded) return;

            float left = stream.xMax, right = stream.xMin;
            for (int i = 0; i < outlineCount; i++)
            {
                left = Mathf.Min(left, outline[i].x);
                right = Mathf.Max(right, outline[i].x);
            }
            ImpactWidth = Mathf.Min(stream.xMax, right) - Mathf.Max(stream.xMin, left);
            BuildBranch(Left, -1, impact.x - stream.xMin);
            BuildBranch(Right, 1, stream.xMax - impact.x);
        }

        internal bool TryGetCurrent(Vector2 localPoint, out Vector2 localDirection) =>
            Main.TryGetCurrent(localPoint, out localDirection) ||
            Left.TryGetCurrent(localPoint, out localDirection) ||
            Right.TryGetCurrent(localPoint, out localDirection);

        void BuildOutline(Transform space, Obstacle obstacle)
        {
            var orientation = Quaternion.Euler(0, 0, space.eulerAngles.z + obstacle.Angle);
            Vector2 x = space.InverseTransformVector(orientation * Vector3.right * obstacle.HalfSize.x);
            Vector2 y = space.InverseTransformVector(orientation * Vector3.up * obstacle.HalfSize.y);
            var center = obstacle.Center;
            if (obstacle.Shape == 2)
            {
                outlineCount = outline.Length;
                for (int i = 0; i < outlineCount; i++)
                {
                    float angle = i * (2 * Mathf.PI / outlineCount);
                    outline[i] = center + Mathf.Cos(angle) * x + Mathf.Sin(angle) * y;
                }
                return;
            }

            outline[0] = center - x - y;
            outline[1] = center + x - y;
            outlineCount = 3;
            if (obstacle.Shape == 0) outline[2] = center + y;
            else
            {
                outline[2] = center + x + y;
                outline[3] = center - x + y;
                outlineCount = 4;
            }
        }

        static float EdgeAtX(Vector2 a, Vector2 b, float x)
        {
            if (x <= Mathf.Min(a.x, b.x) || x >= Mathf.Max(a.x, b.x)) return float.NegativeInfinity;
            return Mathf.Lerp(a.y, b.y, (x - a.x) / (b.x - a.x));
        }

        void BuildBranch(Ribbon ribbon, int side, float width)
        {
            if (width <= Epsilon) return;
            var outerPoint = outline[0];
            for (int i = 1; i < outlineCount; i++)
            {
                var point = outline[i];
                float distance = (point.x - outerPoint.x) * side;
                if (distance > Epsilon || (Mathf.Abs(distance) <= Epsilon && point.y > outerPoint.y)) outerPoint = point;
            }

            float x = Impact.x;
            float y = Impact.y;
            AddBranchRow(ribbon, side, x, y, width);

            // Rounded shapes release at the widest point. A flat shoulder turns over a short fall.
            bool shoulder = outerPoint.y >= Impact.y - Epsilon;
            float turn = Mathf.Min(.18f, (Impact.y - area.yMin) * .25f);
            float contourEnd = Mathf.Max(area.yMin, Mathf.Min(Impact.y - Epsilon, shoulder ? Impact.y - turn : outerPoint.y));
            float tangent = 0;
            if (y > contourEnd + Epsilon)
            {
                for (int i = 1; i <= ContourSteps; i++)
                {
                    float t = i / (float)ContourSteps;
                    float nextY = Mathf.Lerp(Impact.y, contourEnd, t * t);
                    float edge = shoulder ? Mathf.Lerp(Impact.x, outerPoint.x, Mathf.SmoothStep(0, 1, t)) : EdgeAtY(nextY, side, x);
                    float nextX = side < 0 ? Mathf.Min(x, edge) : Mathf.Max(x, edge);
                    tangent = (nextX - x) * side / Mathf.Max(Epsilon, y - nextY);
                    x = nextX;
                    y = nextY;
                    AddBranchRow(ribbon, side, x, y, width);
                }
            }

            float remaining = y - area.yMin;
            if (remaining <= Epsilon) return;
            float spread = Mathf.Clamp(tangent, .08f, 1.5f);
            for (int i = 1; i <= FallSteps; i++)
            {
                float t = i / (float)FallSteps;
                float fall = remaining * t;
                float drift = spread * fall / (1 + fall / Mathf.Max(.25f, area.width * .6f));
                float taper = Mathf.Min(width * .2f * t, drift);
                AddBranchRow(ribbon, side, x + side * drift, y - fall, width - taper);
            }
        }

        float EdgeAtY(float y, int side, float fallback)
        {
            float edge = fallback * side;
            for (int i = 0; i < outlineCount; i++)
            {
                var a = outline[i];
                var b = outline[(i + 1) % outlineCount];
                if (y < Mathf.Min(a.y, b.y) || y > Mathf.Max(a.y, b.y)) continue;
                float x = Mathf.Abs(a.y - b.y) <= Epsilon
                    ? Mathf.Max(a.x * side, b.x * side)
                    : Mathf.Lerp(a.x, b.x, Mathf.InverseLerp(a.y, b.y, y)) * side;
                edge = Mathf.Max(edge, x);
            }
            return edge * side;
        }

        void AddBranchRow(Ribbon ribbon, int side, float inner, float y, float width)
        {
            float left = side < 0 ? inner - width : inner;
            float right = side < 0 ? inner : inner + width;
            ribbon.Add(y, left, right);
            var bounds = Bounds;
            bounds.xMin = Mathf.Min(bounds.xMin, left);
            bounds.xMax = Mathf.Max(bounds.xMax, right);
            Bounds = bounds;
        }
    }
}
