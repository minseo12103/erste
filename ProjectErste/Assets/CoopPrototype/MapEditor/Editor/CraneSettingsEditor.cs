using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ShapeCoop.Editor
{
    public static class CraneSettingsGUI
    {
        public static void Draw(RotatingCranePlatforms crane)
        {
            if (crane == null) return;
            var gimmick = crane.GetComponent<MapGimmick>(); if (gimmick == null) return;
            EditorGUILayout.LabelField("크레인 발판 설정", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            float speed = EditorGUILayout.FloatField(new GUIContent("회전 속도 (도/초)", "음수는 시계 방향, 양수는 반시계 방향입니다."), gimmick.rotationSpeed);
            float radius = EditorGUILayout.FloatField(new GUIContent("회전 반경", "중심에서 발판 연결점까지의 거리입니다."), crane.Radius);
            Vector2 size = EditorGUILayout.Vector2Field(new GUIContent("발판 크기 (너비, 두께)", "여섯 발판의 그림과 실제 충돌체에 함께 적용됩니다."), crane.PlatformSize);
            if (EditorGUI.EndChangeCheck()) Apply(crane, speed, radius, size);
            EditorGUILayout.LabelField("음수: 시계 방향 · 양수: 반시계 방향 · 크기는 로컬 단위", EditorStyles.wordWrappedMiniLabel);
            if (GUILayout.Button("크레인 기본값 복원")) Apply(crane, -45f, 2.15f, new Vector2(1.12f, .19f));
        }
        public static void Apply(RotatingCranePlatforms crane, float speed, float radius, Vector2 size)
        {
            if (crane == null) return;
            var gimmick = crane.GetComponent<MapGimmick>(); if (gimmick == null) return;
            var objects = new List<Object> { crane, gimmick };
            if (gimmick.rotatingBeam != null)
            {
                foreach (string name in new[] { "Visual", "Spoke 60", "Spoke 120" })
                {
                    var t = gimmick.rotatingBeam.transform.Find(name); var renderer = t != null ? t.GetComponent<SpriteRenderer>() : null;
                    if (renderer != null) objects.Add(renderer);
                    var collision = t != null ? t.Find("Spoke collision") : null;
                    var box = collision != null ? collision.GetComponent<BoxCollider2D>() : null;
                    if (box != null) objects.Add(box);
                }
            }
            if (crane.platforms != null) foreach (var platform in crane.platforms)
            {
                if (platform == null) continue;
                objects.Add(platform.transform);
                var collider = platform.GetComponent<BoxCollider2D>(); if (collider != null) objects.Add(collider);
                var art = platform.transform.Find("Platform art"); if (art != null) objects.Add(art);
            }
            Undo.RecordObjects(objects.ToArray(), "Adjust crane platforms");
            gimmick.rotationSpeed = float.IsNaN(speed) || float.IsInfinity(speed) ? -45f : speed;
            crane.SetDimensions(radius, size); crane.RefreshPreview();
            foreach (var obj in objects)
            {
                if (obj == null) continue;
                EditorUtility.SetDirty(obj);
                if (PrefabUtility.IsPartOfPrefabInstance(obj)) PrefabUtility.RecordPrefabInstancePropertyModifications(obj);
            }
            if (crane.gameObject.scene.IsValid()) EditorSceneManager.MarkSceneDirty(crane.gameObject.scene);
            SceneView.RepaintAll();
        }
    }
    [CustomEditor(typeof(RotatingCranePlatforms))]
    public sealed class CraneSettingsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode)) CraneSettingsGUI.Draw((RotatingCranePlatforms)target);
        }
    }
}
