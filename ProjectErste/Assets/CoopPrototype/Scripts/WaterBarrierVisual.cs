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
                SetRibbon(ref main, "Water main flow (runtime)", flow.Main);
                SetRibbon(ref left, "Water left flow (runtime)", flow.Left);
                SetRibbon(ref right, "Water right flow (runtime)", flow.Right);
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

        void SetRibbon(ref RibbonRenderer renderer, string name, WaterBarrierFlow.Ribbon ribbon)
        {
            if (ribbon.Count < 2)
            {
                if (renderer != null) renderer.Hide();
                return;
            }
            if (renderer == null) renderer = new RibbonRenderer(flowSpace, name, flowMaterial, stream);
            renderer.Show(ribbon);
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
            readonly List<Vector3> positions = new List<Vector3>(48);
            readonly List<Vector3> textureCoordinates = new List<Vector3>(48);
            readonly List<int> triangles = new List<int>(144);

            internal RibbonRenderer(Transform parent, string name, Material material, SpriteRenderer template)
            {
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

            internal void Show(WaterBarrierFlow.Ribbon ribbon)
            {
                renderer.enabled = true;
                bool topologyChanged = positions.Count != ribbon.Count * 2;
                if (!topologyChanged && Matches(ribbon)) return;

                float totalLength = 0;
                for (int i = 1; i < ribbon.Count; i++) totalLength += Vector2.Distance(Center(ribbon, i - 1), Center(ribbon, i));
                totalLength = Mathf.Max(.0001f, totalLength);

                positions.Clear();
                textureCoordinates.Clear();
                float distance = 0;
                for (int i = 0; i < ribbon.Count; i++)
                {
                    if (i > 0) distance += Vector2.Distance(Center(ribbon, i - 1), Center(ribbon, i));
                    float v = 1 - distance / totalLength;
                    positions.Add(ribbon.Edges[i * 2]);
                    positions.Add(ribbon.Edges[i * 2 + 1]);
                    textureCoordinates.Add(new Vector3(0, v, distance));
                    textureCoordinates.Add(new Vector3(1, v, distance));
                }

                if (topologyChanged) mesh.Clear();
                mesh.SetVertices(positions);
                mesh.SetUVs(0, textureCoordinates);
                if (topologyChanged)
                {
                    triangles.Clear();
                    for (int i = 0; i < ribbon.Count - 1; i++)
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
            }

            bool Matches(WaterBarrierFlow.Ribbon ribbon)
            {
                for (int i = 0; i < positions.Count; i++)
                    if (!((Vector2)positions[i]).Equals(ribbon.Edges[i])) return false;
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
