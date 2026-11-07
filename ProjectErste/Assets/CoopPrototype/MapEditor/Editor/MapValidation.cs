using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ShapeCoop.Editor
{
    public sealed class MapValidationResult
    {
        public readonly List<string> errors = new List<string>();
        public readonly List<string> warnings = new List<string>();
        public string Message()
        {
            var lines = new List<string>();
            foreach (var error in errors) lines.Add("오류: " + error);
            foreach (var warning in warnings) lines.Add("확인: " + warning);
            return string.Join("\n", lines);
        }
    }
    public static class MapValidation
    {
        public static MapValidationResult Section(MapSection section)
        {
            var result = new MapValidationResult();
            if (section == null) { result.errors.Add("구간이 없습니다."); return result; }
            foreach (var t in section.GetComponentsInChildren<Transform>(true))
            {
                var go = t.gameObject;
                if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go) > 0) result.errors.Add(go.name + ": 누락된 스크립트가 있습니다.");
                if (PrefabUtility.IsPrefabAssetMissing(go)) result.errors.Add(go.name + ": 프리팹 원본을 찾을 수 없습니다.");
                foreach (var component in go.GetComponents<Component>())
                {
                    if (component == null) continue;
                    var serialized = new SerializedObject(component);
                    var property = serialized.GetIterator();
                    while (property.Next(true))
                        if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue == null && property.objectReferenceInstanceIDValue != 0)
                            result.errors.Add(go.name + " / " + component.GetType().Name + ": 참조가 누락되었습니다 (" + property.displayName + ").");
                }
                if (go.GetComponent<Renderer>() != null && go.GetComponentInParent<MapMarker>() == null && go.GetComponentInParent<MapConnector>() == null && go.GetComponentInChildren<Collider2D>(true) == null)
                    result.warnings.Add(go.name + ": 2D 충돌체가 없습니다. 배경 등 장식이라면 괜찮습니다.");
            }
            return result;
        }
        public static MapValidationResult Assembly(MapAssemblyDraft draft)
        {
            var result = new MapValidationResult();
            int count = 0, players = 0, balls = 0, goals = 0;
            if (draft == null) { result.errors.Add("맵 조합이 없습니다."); return result; }
            if (draft.transform.position.sqrMagnitude > .0001f || Quaternion.Angle(draft.transform.rotation, Quaternion.identity) > .01f || (draft.transform.lossyScale - Vector3.one).sqrMagnitude > .0001f)
                result.errors.Add("맵 조합 루트의 위치, 회전, 크기를 기본값으로 되돌리세요.");
            foreach (Transform child in draft.transform)
            {
                var section = child.GetComponent<MapSection>();
                var source = section == null ? null : PrefabUtility.GetCorrespondingObjectFromSource(section);
                if (source == null) { result.errors.Add(child.name + ": 구간 프리팹 인스턴스가 아닙니다."); continue; }
                count++;
                if (PrefabUtility.HasPrefabInstanceAnyOverrides(child.gameObject, false))
                    result.errors.Add(child.name + ": 구간 내부 변경 사항은 게임에 적용되지 않습니다. 원본 구간 작업 씬에서 수정하고 프리팹을 다시 저장하세요.");
                if (Quaternion.Angle(child.localRotation, Quaternion.identity) > .01f || (child.localScale - Vector3.one).sqrMagnitude > .0001f || Mathf.Abs(child.localPosition.z) > .001f)
                    result.errors.Add(child.name + ": 게임은 XY 위치만 지원합니다. 회전, 크기, Z 위치를 기본값으로 되돌리세요.");
                var check = Section(section); result.errors.AddRange(check.errors); result.warnings.AddRange(check.warnings);
                foreach (var marker in section.GetComponentsInChildren<MapMarker>(true))
                { if (marker.kind == MapMarkerKind.PlayerStart) players++; if (marker.kind == MapMarkerKind.BallStart) balls++; }
                goals += section.GetComponentsInChildren<MapGoalArea>(true).Length;
            }
            if (count == 0) result.errors.Add("구간 프리팹을 하나 이상 추가하세요.");
            if (players == 0) result.warnings.Add("플레이어 시작점이 없습니다."); else if (players > 1) result.warnings.Add("플레이어 시작점이 여러 개입니다. 첫 번째만 사용합니다.");
            if (balls == 0) result.warnings.Add("공 시작점이 없습니다."); else if (balls > 1) result.warnings.Add("공 시작점이 여러 개입니다. 첫 번째만 사용합니다.");
            if (goals == 0) result.warnings.Add("목표 구역이 없습니다.");
            return result;
        }
        public static bool Confirm(MapValidationResult result, string action, bool showDialog)
        {
            if (result.errors.Count > 0)
            {
                if (showDialog) EditorUtility.DisplayDialog("문제 검사: " + action, result.Message(), "확인");
                return false;
            }
            return result.warnings.Count == 0 || !showDialog || EditorUtility.DisplayDialog("문제 검사: " + action, result.Message() + "\n\n계속하시겠습니까?", "계속", "취소");
        }
    }
}
