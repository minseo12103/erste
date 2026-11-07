using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShapeCoop.Editor
{
    // Renders the actual character visual layers in an isolated editor preview scene.
    public sealed class CharacterComparisonWindow : EditorWindow
    {
        static readonly string[] Names = { "삼각형", "사각형", "원형" };
        public const string LayoutPath = "Assets/Resources/ShapeCoop/CharacterLayout.asset";
        Scene previewScene;
        GameObject settingsObject;
        readonly PrototypePlayer[] players = new PrototypePlayer[3];
        Vector2 scroll;
        float pixelsPerUnit = 155;
        bool showColliders = true, showHats = true;
        [SerializeField] CharacterShapeLayout[] draft;

        [MenuItem("Tools/Shape Coop/Character Size Comparison")]
        public static void Open()
        {
            var window = GetWindow<CharacterComparisonWindow>("캐릭터 크기 비교");
            window.minSize = new Vector2(500, 460);
            if (window.position.width < 760 || window.position.height < 720)
            { var rect = window.position; rect.width = Mathf.Max(rect.width, 760); rect.height = Mathf.Max(rect.height, 720); window.position = rect; }
            window.Show();
        }
        void OnEnable() { if (draft == null || draft.Length != 3) LoadApplied(); for (int i = 0; i < draft.Length; i++) draft[i] = draft[i].Safe(); CreatePreview(); }
        void OnDisable() { DisposePreview(); }
        void CreatePreview()
        {
            DisposePreview();
            previewScene = EditorSceneManager.NewPreviewScene();
            settingsObject = new GameObject("Comparison Settings") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(settingsObject, previewScene);
            var settings = settingsObject.AddComponent<PrototypeSettings>();
            for (int i = 0; i < players.Length; i++)
            {
                var go = new GameObject("Comparison " + Names[i]) { hideFlags = HideFlags.HideAndDontSave };
                SceneManager.MoveGameObjectToScene(go, previewScene);
                var player = go.AddComponent<PrototypePlayer>();
                player.Initialize(settings, new PlayerData { id = "comparison" + i, name = Names[i], shape = i, color = i }, Vector2.zero, true);
                player.enabled = false;
                var appearance = player.GetComponentInChildren<PrototypeCharacterAppearance>();
                if (appearance != null) appearance.enabled = false;
                player.body.simulated = false; players[i] = player; player.SetCharacterLayout(draft[i]);
            }
        }
        public void LoadApplied()
        {
            var layout = AssetDatabase.LoadAssetAtPath<CharacterLayout>(LayoutPath);
            draft = new CharacterShapeLayout[3];
            for (int i = 0; i < draft.Length; i++) draft[i] = layout == null ? CharacterShapeLayout.Default : layout.ForShape(i);
            UpdatePreview();
        }
        void UpdatePreview()
        {
            if (draft == null || draft.Length != 3) return;
            for (int i = 0; i < players.Length; i++) if (players[i] != null) players[i].SetCharacterLayout(draft[i]);
            Repaint();
        }
        public static bool SaveLayout(string path, CharacterShapeLayout[] values)
        {
            if (values == null || values.Length != 3 || string.IsNullOrEmpty(path)) return false;
            var layout = AssetDatabase.LoadAssetAtPath<CharacterLayout>(path);
            if (layout == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null) return false;
                layout = CreateInstance<CharacterLayout>(); AssetDatabase.CreateAsset(layout, path);
            }
            else Undo.RecordObject(layout, "Apply character sizes");
            for (int i = 0; i < 3; i++) layout.SetShape(i, values[i]);
            EditorUtility.SetDirty(layout); AssetDatabase.SaveAssetIfDirty(layout); return true;
        }
        void DisposePreview()
        {
            for (int i = 0; i < players.Length; i++) { if (players[i] != null) DestroyImmediate(players[i].gameObject); players[i] = null; }
            if (settingsObject != null) DestroyImmediate(settingsObject); settingsObject = null;
            if (previewScene.IsValid()) EditorSceneManager.ClosePreviewScene(previewScene);
            previewScene = default;
        }
        void OnGUI()
        {
            if (players[0] == null) CreatePreview();
            EditorGUILayout.LabelField("캐릭터 크기 비교", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("크기와 모자 위치를 조절한 뒤 게임에 적용할 수 있습니다. 점선은 몸체 충돌체이며 모자는 충돌하지 않습니다.", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                {
                    if (GUILayout.Button("게임에 적용", GUILayout.Height(26)))
                    {
                        if (SaveLayout(LayoutPath, draft)) ShowNotification(new GUIContent("저장 완료: 다음 게임 시작부터 적용됩니다"));
                        else EditorUtility.DisplayDialog("적용 실패", "캐릭터 설정 파일을 저장할 수 없습니다.", "확인");
                    }
                }
                if (GUILayout.Button("적용된 값 불러오기", GUILayout.Height(26))) LoadApplied();
                if (GUILayout.Button("기본값 미리보기", GUILayout.Height(26))) { for (int i = 0; i < draft.Length; i++) draft[i] = CharacterShapeLayout.Default; UpdatePreview(); }
            }
            EditorGUILayout.LabelField("수정 내용은 게임에 적용을 누르기 전까지 미리보기입니다.", EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                showColliders = EditorGUILayout.ToggleLeft("충돌체 표시", showColliders, GUILayout.Width(100));
                showHats = EditorGUILayout.ToggleLeft("모자 표시", showHats, GUILayout.Width(85));
                pixelsPerUnit = EditorGUILayout.Slider("확대", pixelsPerUnit, 80, 230);
            }
            scroll = EditorGUILayout.BeginScrollView(scroll, false, false);
            float maxHalfWidth = .5f, maxHeight = 0;
            for (int i = 0; i < draft.Length; i++)
            {
                var data = draft[i].Safe();
                float hatHalfWidth = showHats ? Mathf.Abs(data.helmetOffset.x) + .56f * data.helmetSize : .5f;
                maxHalfWidth = Mathf.Max(maxHalfWidth, Mathf.Max(.5f, hatHalfWidth) * data.size);
                maxHeight = Mathf.Max(maxHeight, (Mathf.Max(.5f, showHats ? .735f + .34f * data.helmetSize + data.helmetOffset.y : .5f) + .5f) * data.size);
            }
            float width = Mathf.Max(position.width - 30, pixelsPerUnit * (6f * maxHalfWidth + 1.1f));
            float height = Mathf.Max(260, pixelsPerUnit * maxHeight + 95);
            Rect canvas = GUILayoutUtility.GetRect(width, height, GUILayout.Width(width), GUILayout.Height(height));
            EditorGUI.DrawRect(canvas, new Color(.15f, .17f, .20f));
            float baseline = canvas.y + 20 + pixelsPerUnit * maxHeight;
            EditorGUI.DrawRect(new Rect(canvas.x + 15, baseline, canvas.width - 30, 1), new Color(.7f,.72f,.75f));
            for (int i = 0; i < players.Length; i++)
            {
                float x = canvas.x + canvas.width * (i + .5f) / 3f;
                DrawCharacter(players[i], x, baseline, pixelsPerUnit, showHats);
                if (showColliders) DrawCollider(players[i].shapeCollider, x, baseline, pixelsPerUnit);
                var label = new GUIStyle(EditorStyles.centeredGreyMiniLabel) { normal = { textColor = Color.white }, fontSize = 12 };
                GUI.Label(new Rect(x - 100, baseline + 12, 200, 22), Names[i] + "   " + ColliderSize(players[i].shapeCollider, draft[i].size), label);
            }
            EditorGUILayout.Space();
            float previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 65;
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int i = 0; i < draft.Length; i++)
                {
                    using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                    {
                        EditorGUILayout.LabelField(Names[i], EditorStyles.boldLabel);
                        EditorGUI.BeginChangeCheck();
                        float size = EditorGUILayout.Slider("크기", draft[i].size, .35f, 2f);
                        float hatSize = EditorGUILayout.Slider("모자 크기", draft[i].helmetSize, .35f, 2f);
                        float x = EditorGUILayout.Slider("모자 좌우", draft[i].helmetOffset.x, -.6f, .6f);
                        float y = EditorGUILayout.Slider("모자 높이", draft[i].helmetOffset.y, -.6f, .6f);
                        if (EditorGUI.EndChangeCheck()) { draft[i] = new CharacterShapeLayout { size = size, helmetOffset = new Vector2(x, y), helmetSize = hatSize }.Safe(); UpdatePreview(); }
                    }
                }
            }
            EditorGUIUtility.labelWidth = previousLabelWidth;
            EditorGUILayout.HelpBox("크기는 몸체 그림과 충돌체에 함께 적용됩니다. 모자 크기와 위치는 해당 몸체 크기 기준이며, 모자에는 충돌이 없습니다.", MessageType.None);
            EditorGUILayout.EndScrollView();
        }
        static void DrawCharacter(PrototypePlayer player, float x, float baseline, float scale, bool hats)
        {
            if (player == null) return;
            var renderers = player.GetComponentsInChildren<SpriteRenderer>(true);
            Array.Sort(renderers, (a,b) => a.sortingOrder.CompareTo(b.sortingOrder));
            foreach (var renderer in renderers)
            {
                if (renderer.sprite == null || (!hats && renderer.gameObject.name == "Helmet")) continue;
                var local = renderer.transform.position - player.transform.position;
                var size = Vector3.Scale(renderer.sprite.bounds.size, renderer.transform.lossyScale);
                var rect = new Rect(x + (local.x - size.x * .5f) * scale, baseline - (local.y + player.CurrentCharacterLayout.size * .5f + size.y * .5f) * scale, size.x * scale, size.y * scale);
                var source = renderer.sprite.rect; var texture = renderer.sprite.texture;
                var uv = new Rect(source.x / texture.width, source.y / texture.height, source.width / texture.width, source.height / texture.height);
                var color = GUI.color; GUI.color = renderer.color; GUI.DrawTextureWithTexCoords(rect, texture, uv, true); GUI.color = color;
            }
        }
        static string ColliderSize(Collider2D collider, float sizeFactor)
        {
            if (collider is CircleCollider2D circle) return "지름 " + (circle.radius * 2 * sizeFactor).ToString("0.##") + "u";
            if (collider is BoxCollider2D box) return (box.size.x * sizeFactor).ToString("0.##") + " × " + (box.size.y * sizeFactor).ToString("0.##") + "u";
            if (collider is PolygonCollider2D polygon)
            {
                var points = polygon.points; if (points.Length == 0) return "";
                var min = points[0]; var max = points[0];
                foreach (var point in points) { min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
                var size = (max - min) * sizeFactor; return size.x.ToString("0.##") + " × " + size.y.ToString("0.##") + "u";
            }
            return "";
        }
        static void DrawCollider(Collider2D collider, float x, float baseline, float scale)
        {
            float sizeFactor = collider.transform.lossyScale.x;
            var points = Array.Empty<Vector2>();
            if (collider is BoxCollider2D box)
            {
                var a = box.offset - box.size * .5f; var b = box.offset + box.size * .5f;
                points = new[] { a, new Vector2(b.x,a.y), b, new Vector2(a.x,b.y), a };
            }
            else if (collider is PolygonCollider2D polygon)
            {
                var original = polygon.points; if (original.Length == 0) return;
                points = new Vector2[original.Length + 1]; for (int i = 0; i < original.Length; i++) points[i] = original[i] + polygon.offset; points[original.Length] = points[0];
            }
            else if (collider is CircleCollider2D circle)
            {
                points = new Vector2[49];
                for (int i = 0; i < points.Length; i++) { float a = i * Mathf.PI * 2 / (points.Length - 1); points[i] = circle.offset + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * circle.radius; }
            }
            if (points.Length == 0) return;
            var previous = Handles.color; Handles.color = new Color(1f,1f,1f,.85f);
            for (int i = 0; i + 1 < points.Length; i++)
            {
                var a = new Vector3(x + points[i].x * sizeFactor * scale, baseline - (points[i].y + .5f) * sizeFactor * scale);
                var b = new Vector3(x + points[i+1].x * sizeFactor * scale, baseline - (points[i+1].y + .5f) * sizeFactor * scale);
                Handles.DrawDottedLine(a,b,3f);
            }
            Handles.color = previous;
        }
    }
}
