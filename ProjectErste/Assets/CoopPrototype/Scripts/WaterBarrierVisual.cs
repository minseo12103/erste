using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShapeCoop
{
    // Draws the same ribbons MapGimmick uses to sample the water's force.
    public sealed class WaterBarrierVisual : MonoBehaviour
    {
        public Sprite[] streamFrames = Array.Empty<Sprite>();
        public Sprite[] splashFrames = Array.Empty<Sprite>();
        public Sprite[] splitFrames = Array.Empty<Sprite>();
        public SpriteRenderer splash, split;
        [Min(.1f)] public float framesPerSecond = 8f;

        const string FlowShaderPath = "ShapeCoop/Effects/WaterBarrierFlow";
        static readonly int MainTex = Shader.PropertyToID("_MainTex");
        static readonly int AtlasRect = Shader.PropertyToID("_AtlasRect");
        static readonly int UseStreamArt = Shader.PropertyToID("_UseStreamArt");
        static readonly int FlowTime = Shader.PropertyToID("_FlowTime");

        Transform flowSpace;
        SpriteRenderer stream;
        Sprite materialFrame;
        Material flowMaterial;
        RibbonRenderer main, left, right;

        public void Bind(Transform water, Transform flowSpace)
        {
            this.flowSpace = flowSpace;
            if (water != null)
            {
                var art = water.Find("Stream art");
                stream = art != null ? art.GetComponent<SpriteRenderer>() : water.GetComponent<SpriteRenderer>();
            }
        }

        internal void Show(float elapsed, WaterBarrierFlow flow)
        {
            if (flowSpace == null || !isActiveAndEnabled) return;
            float fps = float.IsNaN(framesPerSecond) || float.IsInfinity(framesPerSecond) ? 8f : Mathf.Max(.1f, framesPerSecond);
            int frame = Mathf.FloorToInt(Mathf.Max(0, elapsed) * fps);
            Sprite animated = streamFrames != null && streamFrames.Length > 0 ? streamFrames[frame % streamFrames.Length] : null;
            bool needsMaterial = flow.Main.Count >= 2 || flow.Left.Count >= 2 || flow.Right.Count >= 2;
            if (needsMaterial && flowMaterial == null)
            {
                var shader = Resources.Load<Shader>(FlowShaderPath);
                if (shader != null) flowMaterial = new Material(shader) { name = "Water barrier flow (runtime)", hideFlags = HideFlags.DontSave };
            }
            if (flowMaterial != null)
            {
                if (stream != null) stream.enabled = false;
                if (materialFrame != animated)
                {
                    materialFrame = animated;
                    flowMaterial.SetFloat(UseStreamArt, animated != null ? 1 : 0);
                    if (animated != null)
                    {
                        flowMaterial.SetTexture(MainTex, animated.texture);
                        flowMaterial.SetVector(AtlasRect, GetAtlasRect(animated));
                    }
                }
                flowMaterial.SetFloat(FlowTime, elapsed);
                var junction = new Junction(flow);
                SetRibbon(ref main, "Water main flow (runtime)", flow.Main, 0, junction);
                SetRibbon(ref left, "Water left flow (runtime)", flow.Left, -1, junction);
                SetRibbon(ref right, "Water right flow (runtime)", flow.Right, 1, junction);
            }

            bool showSplash = flow.Shielded && flow.ImpactWidth > .01f;
            if (splash != null)
            {
                splash.sprite = showSplash && splashFrames != null && splashFrames.Length > 0 ? splashFrames[frame % splashFrames.Length] : null;
                if (splash.sprite != null)
                    FitInFlowSpace(splash.transform, flow.Impact, flow.ImpactWidth * .85f, Mathf.Min(.52f, .2f + flow.ImpactWidth * .3f));
            }
            if (split != null && split.sprite != null) split.sprite = null;
        }

        void FitInFlowSpace(Transform target, Vector2 center, float width, float height)
        {
            target.position = flowSpace.TransformPoint(center);
            target.rotation = flowSpace.rotation;
            var parent = target.parent;
            float x = flowSpace.TransformVector(Vector3.right * width).magnitude;
            float y = flowSpace.TransformVector(Vector3.up * height).magnitude;
            if (parent != null)
            {
                x /= Mathf.Max(.0001f, parent.TransformVector(target.localRotation * Vector3.right).magnitude);
                y /= Mathf.Max(.0001f, parent.TransformVector(target.localRotation * Vector3.up).magnitude);
            }
            target.localScale = new Vector3(x, y, 1);
        }

        static Vector4 GetAtlasRect(Sprite frame)
        {
            if (frame.packed)
            {
                var min = Vector2.one;
                var max = Vector2.zero;
                foreach (var uv in frame.uv)
                {
                    min = Vector2.Min(min, uv);
                    max = Vector2.Max(max, uv);
                }
                return new Vector4(min.x, min.y, max.x - min.x, max.y - min.y);
            }
            var rect = frame.rect;
            return new Vector4(rect.x / frame.texture.width, rect.y / frame.texture.height,
                rect.width / frame.texture.width, rect.height / frame.texture.height);
        }

        // The overlap stays inside the main flow; the force-sampling ribbons are untouched.
        readonly struct Junction
        {
            internal readonly float Left, Width, MainLength, Overlap, Transition, Bottom, Height, ImpactY, EdgeCurve;

            internal Junction(WaterBarrierFlow flow)
            {
                bool hasMain = flow.Main.Count >= 2;
                bool hasLeft = flow.Left.Count >= 2;
                bool hasRight = flow.Right.Count >= 2;
                Left = hasMain ? flow.Main.Edges[0].x : hasLeft ? flow.Left.Edges[0].x : hasRight ? flow.Right.Edges[0].x : 0;
                float right = hasMain ? flow.Main.Edges[1].x : hasRight ? flow.Right.Edges[1].x : hasLeft ? flow.Left.Edges[1].x : Left;
                Width = Mathf.Max(.0001f, right - Left);
                MainLength = hasMain ? Mathf.Max(0, flow.Main.Edges[0].y - flow.Main.Edges[2].y) : 0;
                Overlap = flow.Shielded && (hasLeft || hasRight)
                    ? Mathf.Min(.24f, Mathf.Min(Width * .12f, MainLength * .5f)) : 0;
                Transition = Mathf.Clamp(Width * .25f, .12f, .6f);
                Bottom = flow.Bounds.yMin;
                Height = Mathf.Max(.0001f, flow.Bounds.height);
                ImpactY = flow.Impact.y;
                // Rounded and flat-topped obstacles already have curved shoulders.
                EdgeCurve = flow.Blocker.Shape == 0 ? Mathf.Clamp(Width * .3f, .2f, .6f) : 0;
            }
        }

        void SetRibbon(ref RibbonRenderer renderer, string name, WaterBarrierFlow.Ribbon ribbon, int side, Junction junction)
        {
            if (ribbon.Count < 2)
            {
                if (renderer != null) renderer.Hide();
                return;
            }
            if (renderer == null) renderer = new RibbonRenderer(flowSpace, name, flowMaterial, stream, side);
            renderer.Show(ribbon, junction);
        }

        void OnDisable()
        {
            main?.Hide();
            left?.Hide();
            right?.Hide();
        }

        void OnDestroy()
        {
            main?.Release();
            left?.Release();
            right?.Release();
            if (flowMaterial != null) Destroy(flowMaterial);
        }

        sealed class RibbonRenderer
        {
            readonly Mesh mesh;
            readonly MeshRenderer renderer;
            readonly int side;
            readonly List<Vector3> positions = new List<Vector3>(48);
            readonly List<Vector4> textureCoordinates = new List<Vector4>(48);
            readonly List<Vector4> junctionCoordinates = new List<Vector4>(48);
            readonly List<int> triangles = new List<int>(144);
            Vector2[] lastEdges;
            int lastCount;
            Junction lastJunction;

            internal RibbonRenderer(Transform parent, string name, Material material, SpriteRenderer template, int side)
            {
                this.side = side;
                var part = new GameObject(name) { hideFlags = HideFlags.DontSave, layer = template != null ? template.gameObject.layer : parent.gameObject.layer };
                part.transform.SetParent(parent, false);
                mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
                mesh.MarkDynamic();
                part.AddComponent<MeshFilter>().sharedMesh = mesh;
                renderer = part.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                if (template != null)
                {
                    renderer.sortingLayerID = template.sortingLayerID;
                    renderer.sortingOrder = template.sortingOrder;
                }
            }

            internal void Show(WaterBarrierFlow.Ribbon ribbon, Junction junction)
            {
                renderer.enabled = true;
                bool hasOverlap = junction.Overlap > .0001f;
                int rowCount = ribbon.Count + (hasOverlap ? 1 : 0);
                bool topologyChanged = positions.Count != rowCount * 2;
                if (!topologyChanged && Matches(ribbon, junction)) return;
                bool branch = side != 0;
                float roundStrength = branch && junction.EdgeCurve > 0 ? OuterRoundStrength(ribbon, junction.EdgeCurve) : 0;

                positions.Clear();
                textureCoordinates.Clear();
                junctionCoordinates.Clear();
                float incomingLeft = Mathf.Clamp01((ribbon.Edges[0].x - junction.Left) / junction.Width);
                float incomingRight = Mathf.Clamp01((ribbon.Edges[1].x - junction.Left) / junction.Width);
                float distance = 0;
                if (branch && hasOverlap)
                    AddRow(ribbon.Edges[0] + Vector2.up * junction.Overlap, ribbon.Edges[1] + Vector2.up * junction.Overlap,
                        -junction.Overlap, incomingLeft, incomingRight, junction, roundStrength);
                for (int i = 0; i < ribbon.Count; i++)
                {
                    if (i > 0) distance += Vector2.Distance(Center(ribbon, i - 1), Center(ribbon, i));
                    if (!branch && hasOverlap && i == ribbon.Count - 1)
                        AddRow(ribbon.Edges[i * 2] + Vector2.up * junction.Overlap, ribbon.Edges[i * 2 + 1] + Vector2.up * junction.Overlap,
                            distance - junction.Overlap, incomingLeft, incomingRight, junction, roundStrength);
                    AddRow(ribbon.Edges[i * 2], ribbon.Edges[i * 2 + 1], distance, incomingLeft, incomingRight, junction, roundStrength);
                }

                if (topologyChanged) mesh.Clear();
                mesh.SetVertices(positions);
                mesh.SetUVs(0, textureCoordinates);
                mesh.SetUVs(1, junctionCoordinates);
                if (topologyChanged)
                {
                    triangles.Clear();
                    for (int i = 0; i < rowCount - 1; i++)
                    {
                        int j = i * 2;
                        triangles.Add(j);
                        triangles.Add(j + 1);
                        triangles.Add(j + 2);
                        triangles.Add(j + 2);
                        triangles.Add(j + 1);
                        triangles.Add(j + 3);
                    }
                    mesh.SetTriangles(triangles, 0, false);
                }
                mesh.RecalculateBounds();
                if (lastEdges == null || lastEdges.Length < ribbon.Count * 2) lastEdges = new Vector2[ribbon.Edges.Length];
                Array.Copy(ribbon.Edges, lastEdges, ribbon.Count * 2);
                lastCount = ribbon.Count;
                lastJunction = junction;
            }

            float OuterRoundStrength(WaterBarrierFlow.Ribbon ribbon, float curveLength)
            {
                int edge = side < 0 ? 0 : 1;
                var top = ribbon.Edges[edge];
                for (int i = 1; i < ribbon.Count; i++)
                {
                    var point = ribbon.Edges[i * 2 + edge];
                    float drop = top.y - point.y;
                    if (drop >= Mathf.Min(.04f, curveLength * .2f) || i == ribbon.Count - 1)
                    {
                        if (drop <= .0001f) return 0;
                        float steepness = Mathf.Abs(point.x - top.x) / drop;
                        float t = Mathf.InverseLerp(1.8f, 3f, steepness);
                        return 1 - t * t * (3 - 2 * t);
                    }
                }
                return 0;
            }

            void AddRow(Vector2 a, Vector2 b, float distance, float incomingLeft, float incomingRight, Junction junction, float roundStrength)
            {
                bool branch = side != 0;
                float edgeInset = 0;
                if (roundStrength > 0 && distance > 0 && junction.ImpactY - a.y < junction.EdgeCurve)
                {
                    // Feather the outer edge without changing the force-sampling ribbon.
                    float t = Mathf.Clamp01((junction.ImpactY - a.y) / junction.EdgeCurve);
                    float curve = t * t * (3 - 2 * t);
                    float minimumWidth = Mathf.Min((b.x - a.x) * .3f, .04f);
                    float maxInset = Mathf.Min(junction.Width * .05f, .1f);
                    float wanted;
                    if (side < 0)
                        wanted = Mathf.Max(0, (junction.Left - a.x) * (1 - curve));
                    else
                        wanted = Mathf.Max(0, (b.x - junction.Left - junction.Width) * (1 - curve));
                    float inset = Mathf.Min(maxInset * (1 - Mathf.Exp(-wanted / maxInset)), b.x - a.x - minimumWidth);
                    edgeInset = roundStrength * inset / Mathf.Max(.0001f, b.x - a.x);
                }
                // Anchor the texture to the full water height so a moving impact does not stretch it.
                float v = Mathf.Clamp01((a.y - junction.Bottom) / junction.Height);
                float progress = branch
                    ? distance < 0 ? distance / junction.Overlap : distance / junction.Transition
                    : junction.Overlap > .0001f ? -Mathf.Clamp01((junction.MainLength - distance) / junction.Overlap) : -1;
                positions.Add(a);
                positions.Add(b);
                // The shader maps the incoming texture across each branch and feathers its outer edge.
                float flowDistance = distance + (branch ? junction.MainLength : 0);
                textureCoordinates.Add(new Vector4(0, v, flowDistance, branch ? 1 : 0));
                textureCoordinates.Add(new Vector4(1, v, flowDistance, branch ? 1 : 0));
                junctionCoordinates.Add(new Vector4(incomingLeft, edgeInset, side, progress));
                junctionCoordinates.Add(new Vector4(incomingRight, edgeInset, side, progress));
            }

            bool Matches(WaterBarrierFlow.Ribbon ribbon, Junction junction)
            {
                if (lastCount != ribbon.Count || lastJunction.Left != junction.Left || lastJunction.Width != junction.Width ||
                    lastJunction.MainLength != junction.MainLength || lastJunction.Overlap != junction.Overlap ||
                    lastJunction.Transition != junction.Transition || lastJunction.Bottom != junction.Bottom ||
                    lastJunction.Height != junction.Height || lastJunction.ImpactY != junction.ImpactY ||
                    lastJunction.EdgeCurve != junction.EdgeCurve) return false;
                for (int i = 0; i < ribbon.Count * 2; i++)
                    if (!lastEdges[i].Equals(ribbon.Edges[i])) return false;
                return true;
            }

            static Vector2 Center(WaterBarrierFlow.Ribbon ribbon, int row) => (ribbon.Edges[row * 2] + ribbon.Edges[row * 2 + 1]) * .5f;

            internal void Hide() { if (renderer != null) renderer.enabled = false; }

            internal void Release()
            {
                if (renderer != null) Destroy(renderer.gameObject);
                if (mesh != null) Destroy(mesh);
            }
        }
    }
}
