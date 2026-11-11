using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ShapeCoop.Editor
{
    // Edits instance overrides only; effect colliders remain the gameplay source of truth.
    public static class GimmickSettingsGUI
    {
        const float MinimumAreaSize = .05f;
        static SceneView rangeHandleView;
        static GameObject rangeHandleSelection;
        static int nearestRangeHandle;
        static SettingsSnapshot copiedSettings;
        static readonly GUIContent CopySettingsButton = new GUIContent("설정 복사", "배율, 효과 범위와 작동 수치를 복사합니다.");
        static readonly GUIContent PasteSettingsButton = new GUIContent("설정 붙여넣기", "같은 종류의 배치된 오브젝트에 설정을 적용합니다.");

        static readonly string[] TuningProperties =
        {
            nameof(MapGimmick.activationDelay), nameof(MapGimmick.force),
            nameof(MapGimmick.activeSeconds), nameof(MapGimmick.inactiveSeconds),
            nameof(MapGimmick.effectSeconds), nameof(MapGimmick.impactSpeed),
            nameof(MapGimmick.pulleySpeed), nameof(MapGimmick.rotationSpeed)
        };

        sealed class SettingsSnapshot
        {
            public readonly string sourceName;
            public readonly MapGimmickKind kind;
            readonly Vector2 scale;
            readonly (Vector2 size, Vector2 offset)? area;
            readonly (float radius, Vector2 size)? craneDimensions;
            readonly float beamRange;
            readonly float travel;
            readonly float[] tuning;

            public SettingsSnapshot(MapGimmick source)
            {
                sourceName = source.name;
                kind = source.kind;
                scale = source.transform.localScale;
                beamRange = source.beamRange;
                travel = source.travel;
                var box = EffectArea(source);
                if (box != null) area = (box.size, box.offset);
                var crane = source.GetComponent<RotatingCranePlatforms>();
                if (crane != null) craneDimensions = (crane.Radius, crane.PlatformSize);

                using var serialized = new SerializedObject(source);
                tuning = new float[TuningProperties.Length];
                for (int i = 0; i < tuning.Length; i++)
                    tuning[i] = serialized.FindProperty(TuningProperties[i]).floatValue;
            }

            public void Apply(MapGimmick target)
            {
                ApplyScale(target, scale);
                if (area.HasValue) ApplyArea(target, area.Value.size, area.Value.offset);
                ApplySparkLength(target, beamRange);
                ApplyPulleyTravel(target, travel);

                using var serialized = new SerializedObject(target);
                for (int i = 0; i < tuning.Length; i++)
                    if (float.IsFinite(tuning[i])) serialized.FindProperty(TuningProperties[i]).floatValue = tuning[i];
                if (serialized.ApplyModifiedProperties()) MarkChanged(target, new Object[] { target });

                var crane = target.GetComponent<RotatingCranePlatforms>();
                if (crane != null && craneDimensions.HasValue)
                {
                    var (radius, size) = craneDimensions.Value;
                    if (!Mathf.Approximately(crane.Radius, radius) || crane.PlatformSize != size)
                        CraneSettingsGUI.Apply(crane, target.rotationSpeed, radius, size);
                }
            }
        }

        internal static void CopySettings(MapGimmick source)
        {
            if (source != null && !EditorApplication.isPlayingOrWillChangePlaymode)
                copiedSettings = new SettingsSnapshot(source);
        }

        static bool CanPasteSettings(MapGimmick target)
        {
            return copiedSettings != null && target != null && copiedSettings.kind == target.kind &&
                !EditorApplication.isPlayingOrWillChangePlaymode &&
                target.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(target);
        }

        internal static bool PasteSettings(MapGimmick target)
        {
            if (!CanPasteSettings(target)) return false;
            const string undoName = "Paste gimmick settings";
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            try
            {
                Undo.SetCurrentGroupName(undoName);
                copiedSettings.Apply(target);
            }
            finally
            {
                Undo.FlushUndoRecordObjects();
                Undo.SetCurrentGroupName(undoName);
                Undo.CollapseUndoOperations(group);
            }
            return true;
        }

        static void DrawSettingsClipboard(MapGimmick gimmick)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(CopySettingsButton)) CopySettings(gimmick);
                using (new EditorGUI.DisabledScope(!CanPasteSettings(gimmick)))
                    if (GUILayout.Button(PasteSettingsButton)) PasteSettings(gimmick);
            }
            if (copiedSettings != null)
                EditorGUILayout.LabelField("복사한 설정  " + copiedSettings.sourceName, EditorStyles.wordWrappedMiniLabel);
        }

        static float Positive(float value, float fallback, float minimum = MinimumAreaSize)
            => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Max(minimum, value);

        static float ScaleComponent(float value, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return fallback;
            return Mathf.Abs(value) < .01f ? (value < 0 ? -.01f : .01f) : value;
        }

        static void MarkChanged(MapGimmick gimmick, IEnumerable<Object> changed)
        {
            foreach (var item in changed)
            {
                if (item == null) continue;
                EditorUtility.SetDirty(item);
                if (PrefabUtility.IsPartOfPrefabInstance(item)) PrefabUtility.RecordPrefabInstancePropertyModifications(item);
            }
            if (gimmick.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(gimmick.gameObject.scene);
            SceneView.RepaintAll();
        }

        public static void ApplyScale(MapGimmick gimmick, Vector2 value)
        {
            if (gimmick == null) return;
            var t = gimmick.transform; var current = t.localScale;
            var next = new Vector3(ScaleComponent(value.x, current.x), ScaleComponent(value.y, current.y), current.z);
            if (next == current) return;
            Undo.RecordObject(t, "Resize map object"); t.localScale = next;
            MarkChanged(gimmick, new Object[] { t });
        }

        public static void ApplySparkLength(MapGimmick gimmick, float value)
        {
            if (gimmick == null || gimmick.kind != MapGimmickKind.Sparks) return;
            float next = Positive(value, gimmick.beamRange, .1f);
            if (Mathf.Approximately(next, gimmick.beamRange)) return;
            Undo.RecordObject(gimmick, "Change spark range"); gimmick.beamRange = next;
            MarkChanged(gimmick, new Object[] { gimmick });
        }

        static void ApplyPulleyTravel(MapGimmick gimmick, float value)
        {
            if (gimmick.kind != MapGimmickKind.Pulley) return;
            float next = Positive(value, gimmick.travel, .1f);
            if (Mathf.Approximately(next, gimmick.travel)) return;
            Undo.RecordObject(gimmick, "Change pulley travel");
            gimmick.travel = next;
            MarkChanged(gimmick, new Object[] { gimmick });
        }

        static BoxCollider2D EffectArea(MapGimmick gimmick)
        {
            if (gimmick == null) return null;
            switch (gimmick.kind)
            {
                case MapGimmickKind.Vent:
                case MapGimmickKind.Oil:
                case MapGimmickKind.Sprinkler:
                case MapGimmickKind.Magnet:
                case MapGimmickKind.Nitrogen:
                    var box = gimmick.GetComponent<BoxCollider2D>();
                    return box != null && box.isTrigger ? box : null;
                default: return null;
            }
        }

        static bool AreaExtendsDownward(MapGimmickKind kind)
            => kind == MapGimmickKind.Magnet || kind == MapGimmickKind.Sprinkler;

        static void AddRenderer(List<Object> objects, SpriteRenderer renderer)
        {
            if (renderer == null) return;
            objects.Add(renderer); objects.Add(renderer.transform);
        }

        public static void ApplyArea(MapGimmick gimmick, Vector2 size, Vector2 offset)
        {
            var area = EffectArea(gimmick);
            if (area == null) return;
            var oldSize = area.size; var oldOffset = area.offset;
            size = new Vector2(Positive(size.x, oldSize.x), Positive(size.y, oldSize.y));
            if (float.IsNaN(offset.x) || float.IsInfinity(offset.x)) offset.x = oldOffset.x;
            if (float.IsNaN(offset.y) || float.IsInfinity(offset.y)) offset.y = oldOffset.y;
            if (size == oldSize && offset == oldOffset) return;

            var changed = new List<Object> { area };
            var oil = gimmick.GetComponent<OilSurfaceVisual>();
            if (oil != null) { AddRenderer(changed, oil.left); AddRenderer(changed, oil.middle); AddRenderer(changed, oil.right); AddRenderer(changed, oil.isolated); }
            var magnet = gimmick.visual != null ? gimmick.visual.GetComponentInChildren<MagnetFieldVisual>(true) : null;
            if (magnet != null) AddRenderer(changed, magnet.field);
            var nitrogen = gimmick.visual != null ? gimmick.visual.GetComponentInChildren<NitrogenGeyserVisual>(true) : null;
            if (nitrogen != null) { AddRenderer(changed, nitrogen.gas); AddRenderer(changed, nitrogen.activeArea); }
            Transform animatedArea = null;
            if (gimmick.visual != null && gimmick.kind == MapGimmickKind.Vent) animatedArea = gimmick.visual.Find("Air");
            if (gimmick.visual != null && gimmick.kind == MapGimmickKind.Sprinkler) animatedArea = gimmick.visual.Find("Water");
            if (animatedArea != null) changed.Add(animatedArea);

            Undo.RecordObjects(changed.ToArray(), "Change gimmick effect area");
            area.size = size; area.offset = offset;
            if (oil != null) oil.Refresh();
            if (magnet != null) magnet.RefreshGeometry();
            if (nitrogen != null) nitrogen.RefreshGeometry();
            if (animatedArea != null)
            {
                var p = animatedArea.localPosition; p.x += offset.x - oldOffset.x; p.y += offset.y - oldOffset.y; animatedArea.localPosition = p;
                var scale = animatedArea.localScale;
                if (Mathf.Abs(oldSize.x) > .0001f) scale.x *= size.x / oldSize.x;
                if (Mathf.Abs(oldSize.y) > .0001f) scale.y *= size.y / oldSize.y;
                animatedArea.localScale = scale;
            }
            MarkChanged(gimmick, changed);
        }

        public static void ApplyOilAngle(MapGimmick gimmick, float degrees)
        {
            if (gimmick == null || gimmick.kind != MapGimmickKind.Oil || float.IsNaN(degrees) || float.IsInfinity(degrees)) return;
            var t = gimmick.transform; var euler = t.localEulerAngles;
            if (Mathf.Approximately(Mathf.DeltaAngle(euler.z, degrees), 0)) return;
            Undo.RecordObject(t, "Rotate oil surface"); t.localRotation = Quaternion.Euler(euler.x, euler.y, degrees);
            MarkChanged(gimmick, new Object[] { t });
        }

        // Rotate the puddle and its trigger as one object, placing the trigger's lower edge on the hit surface.
        public static bool AlignOilToGround(MapGimmick gimmick)
        {
            var area = EffectArea(gimmick);
            if (gimmick == null || gimmick.kind != MapGimmickKind.Oil || area == null || !gimmick.gameObject.scene.IsValid()) return false;
            var section = gimmick.GetComponentInParent<MapSection>();
            Physics2D.SyncTransforms();
            var origin = (Vector2)gimmick.transform.position + Vector2.up * .5f;
            foreach (var hit in Physics2D.RaycastAll(origin, Vector2.down, 5.5f))
            {
                var ground = hit.collider;
                if (ground == null || ground.isTrigger || ground.gameObject.scene != gimmick.gameObject.scene || ground.transform.IsChildOf(gimmick.transform) ||
                    (section != null && !ground.transform.IsChildOf(section.transform)) || hit.normal.y < .15f) continue;
                var t = gimmick.transform; float depth = t.position.z;
                Undo.RecordObject(t, "Align oil with ground");
                t.rotation = Quaternion.Euler(0, 0, Vector2.SignedAngle(Vector2.up, hit.normal));
                Vector3 floorOffset = t.TransformVector(new Vector3(area.offset.x, area.offset.y - area.size.y * .5f, 0));
                var position = (Vector3)hit.point - floorOffset; position.z = depth; t.position = position;
                MarkChanged(gimmick, new Object[] { t });
                return true;
            }
            return false;
        }

        public static void Draw(MapGimmick gimmick)
        {
            if (gimmick == null) return;
            EditorGUILayout.LabelField("기능 오브젝트 크기와 범위", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
            {
                DrawSettingsClipboard(gimmick);
                var scale = gimmick.transform.localScale;
                EditorGUI.BeginChangeCheck();
                Vector2 nextScale = EditorGUILayout.Vector2Field(new GUIContent("오브젝트 배율 (X, Y)", "그림과 충돌체를 함께 확대하거나 축소합니다."), new Vector2(scale.x, scale.y));
                if (EditorGUI.EndChangeCheck()) ApplyScale(gimmick, nextScale);
                if (gimmick.kind == MapGimmickKind.Sparks)
                {
                    EditorGUI.BeginChangeCheck();
                    float length = EditorGUILayout.FloatField(new GUIContent("스파크 최대 길이", "월드 단위입니다. 스파크는 항상 아래로 향합니다."), gimmick.beamRange);
                    if (EditorGUI.EndChangeCheck()) ApplySparkLength(gimmick, length);
                    EditorGUILayout.LabelField("스파크 길이는 오브젝트 배율과 별도로 설정합니다.", EditorStyles.wordWrappedMiniLabel);
                }
                var area = EffectArea(gimmick);
                if (area != null)
                {
                    string label = gimmick.kind == MapGimmickKind.Oil ? "기름 범위 (너비, 높이)" : "효과 범위 (너비, 높이)";
                    EditorGUI.BeginChangeCheck(); Vector2 size = EditorGUILayout.Vector2Field(label, area.size);
                    if (EditorGUI.EndChangeCheck())
                    {
                        size = new Vector2(Positive(size.x, area.size.x), Positive(size.y, area.size.y));
                        var offset = area.offset;
                        float direction = AreaExtendsDownward(gimmick.kind) ? -1f : 1f;
                        offset.y += (size.y - area.size.y) * .5f * direction;
                        ApplyArea(gimmick, size, offset);
                    }
                    EditorGUI.BeginChangeCheck(); Vector2 center = EditorGUILayout.Vector2Field("효과 중심 (X, Y)", area.offset);
                    if (EditorGUI.EndChangeCheck()) ApplyArea(gimmick, area.size, center);
                    EditorGUILayout.LabelField("범위와 중심은 오브젝트의 로컬 단위이며 배율과 회전이 함께 적용됩니다.", EditorStyles.wordWrappedMiniLabel);
                }
                if (area != null || gimmick.kind == MapGimmickKind.Sparks)
                    EditorGUILayout.LabelField("Scene의 청록색 손잡이를 끌어 효과 범위를 조절할 수 있습니다.", EditorStyles.wordWrappedMiniLabel);
                if (gimmick.kind == MapGimmickKind.Oil)
                {
                    EditorGUI.BeginChangeCheck();
                    float angle = EditorGUILayout.FloatField(new GUIContent("기름 각도 (도)", "바닥의 경사에 맞춰 기름 그림과 판정 영역을 함께 회전합니다."), Mathf.DeltaAngle(0, gimmick.transform.localEulerAngles.z));
                    if (EditorGUI.EndChangeCheck()) ApplyOilAngle(gimmick, angle);
                    if (GUILayout.Button("아래 바닥 경사에 맞추기") && !AlignOilToGround(gimmick))
                        EditorUtility.DisplayDialog("바닥을 찾을 수 없음", "기름 웅덩이를 바닥 가까이에 놓고 다시 시도하세요.", "확인");
                }
                if (gimmick.kind == MapGimmickKind.Pulley)
                {
                    EditorGUI.BeginChangeCheck(); float travel = EditorGUILayout.FloatField("발판 이동 거리", gimmick.travel);
                    if (EditorGUI.EndChangeCheck()) ApplyPulleyTravel(gimmick, travel);
                }
                var crane = gimmick.GetComponent<RotatingCranePlatforms>();
                if (crane != null) { EditorGUILayout.Space(); CraneSettingsGUI.Draw(crane); }
            }
        }

        internal static bool HasNearestRangeHandle(SceneView view)
        {
            return nearestRangeHandle != 0 && view == rangeHandleView &&
                Selection.activeGameObject == rangeHandleSelection && HandleUtility.nearestControl == nearestRangeHandle;
        }

        internal static void ResizeAreaEdge(MapGimmick gimmick, BoxCollider2D area, int axis, bool positive, float localEdge)
        {
            if (!float.IsFinite(localEdge)) return;
            var size = area.size;
            var center = area.offset;
            float direction = positive ? 1f : -1f;
            float fixedEdge = center[axis] - direction * size[axis] * .5f;
            float nextSize = Mathf.Max(MinimumAreaSize, direction * (localEdge - fixedEdge));
            if (Mathf.Approximately(size[axis], nextSize)) return;
            size[axis] = nextSize;
            center[axis] = fixedEdge + direction * nextSize * .5f;
            ApplyArea(gimmick, size, center);
        }

        static Vector3 RangeHandle(Vector3 position)
        {
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            if (GUIUtility.hotControl != id && (e.alt || e.shift || e.control || e.command || Tools.viewToolActive))
            {
                if (e.type == EventType.Repaint)
                    Handles.DotHandleCap(id, position, Quaternion.identity, HandleUtility.GetHandleSize(position) * .065f, e.type);
                return position;
            }

            // Project the free drag onto the range axis so the map's movement grid doesn't snap effect sizes.
            var moved = Handles.FreeMoveHandle(id, position, HandleUtility.GetHandleSize(position) * .065f, Vector3.zero, Handles.DotHandleCap);
            if (e.type == EventType.Layout && HandleUtility.nearestControl == id)
                nearestRangeHandle = id;
            return moved;
        }

        static void DrawAreaEdge(MapGimmick gimmick, BoxCollider2D area, int axis, bool positive)
        {
            var local = area.offset;
            local[axis] += (positive ? .5f : -.5f) * area.size[axis];
            var position = area.transform.TransformPoint(local);
            var moved = RangeHandle(position);
            if (moved != position)
            {
                var delta = area.transform.InverseTransformVector(moved - position);
                ResizeAreaEdge(gimmick, area, axis, positive, local[axis] + delta[axis]);
            }
        }

        public static void DrawSceneRange(MapGimmick gimmick)
        {
            if (gimmick == null) return;
            bool activeSelection = Selection.activeGameObject == gimmick.gameObject;
            if (activeSelection && Event.current.type == EventType.Layout)
            {
                rangeHandleView = SceneView.currentDrawingSceneView;
                rangeHandleSelection = gimmick.gameObject;
                nearestRangeHandle = 0;
            }
            bool editable = activeSelection && !EditorApplication.isPlayingOrWillChangePlaymode;

            using (new Handles.DrawingScope(new Color(.1f, .9f, 1f, .95f), Matrix4x4.identity))
            {
                var area = EffectArea(gimmick);
                if (area != null)
                {
                    using (new Handles.DrawingScope(area.transform.localToWorldMatrix))
                        Handles.DrawWireCube(area.offset, area.size);
                    if (editable)
                    {
                        DrawAreaEdge(gimmick, area, 0, false);
                        DrawAreaEdge(gimmick, area, 0, true);
                        // Resize away from the emitter or the surface supporting the puddle.
                        DrawAreaEdge(gimmick, area, 1, !AreaExtendsDownward(gimmick.kind));
                    }
                }
                if (gimmick.kind == MapGimmickKind.Sparks)
                {
                    var start = gimmick.transform.position;
                    var end = start + Vector3.down * Mathf.Max(.1f, gimmick.beamRange);
                    Handles.DrawLine(start, end);
                    if (editable)
                    {
                        var moved = RangeHandle(end);
                        if (moved != end) ApplySparkLength(gimmick, start.y - moved.y);
                    }
                }
            }
        }
    }

    [CustomEditor(typeof(MapGimmick))]
    public sealed class GimmickSettingsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            GimmickSettingsGUI.Draw((MapGimmick)target);
            EditorGUILayout.Space();
            DrawDefaultInspector();
        }
        void OnSceneGUI() { GimmickSettingsGUI.DrawSceneRange((MapGimmick)target); }
    }
}
