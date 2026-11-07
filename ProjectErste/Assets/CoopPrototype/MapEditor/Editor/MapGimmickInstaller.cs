using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ShapeCoop.Editor
{
    public static class MapGimmickInstaller
    {
        public const string Folder = MapEditorInstaller.PalettePath + "/기능 오브젝트";
        public const string SandSheet = MapEditorInstaller.BasePath + "/Textures/SandPouch.png";
        public const string WindSheet = MapEditorInstaller.BasePath + "/Textures/Wind.png";
        public const string WindFloorSheet = MapEditorInstaller.BasePath + "/Textures/WindFloor.png";
        static Sprite square;
        static readonly Color Metal = new Color(.4f, .5f, .55f), Sand = new Color(.75f, .55f, .29f);
        [MenuItem("Tools/Shape Coop/Map Editor/Install missing gimmicks")]
        public static void EnsureAssets()
        {
            Directory.CreateDirectory(Folder); AssetDatabase.Refresh();
            square = AssetDatabase.LoadAssetAtPath<Sprite>(MapEditorInstaller.BasePath + "/Textures/Square.png");
            if (square == null) { Debug.LogError("Install the map editor palette first."); return; }
            Create(MapGimmickKind.Sand, "무너지는 모래 상자");
            Create(MapGimmickKind.Vent, "공기 환풍구");
            Create(MapGimmickKind.Pulley, "도르래 연동 발판");
            Create(MapGimmickKind.Oil, "기름 웅덩이");
            Create(MapGimmickKind.Cement, "무거운 시멘트 포대");
            Create(MapGimmickKind.Sparks, "용접 스파크 구역");
            Create(MapGimmickKind.Crane, "움직이는 타워크레인 빔");
            Create(MapGimmickKind.Sprinkler, "수압 장벽");
            Create(MapGimmickKind.BrickWall, "부실공사 벽돌 벽");
            Create(MapGimmickKind.Magnet, "산업용 대형 자석");
            Create(MapGimmickKind.Nitrogen, "액체 질소 가이저");
            AssetDatabase.SaveAssets();
        }
        static GameObject Visual(Transform parent, string name, Vector2 position, Vector2 size, Color color, int order = 0)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = position; go.transform.localScale = size;
            var renderer = go.AddComponent<SpriteRenderer>(); renderer.sprite = square; renderer.color = color; renderer.sortingOrder = order; return go;
        }
        static Rigidbody2D Platform(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = position;
            var body = go.AddComponent<Rigidbody2D>(); body.bodyType = RigidbodyType2D.Kinematic; body.interpolation = RigidbodyInterpolation2D.Interpolate;
            go.AddComponent<BoxCollider2D>().size = size; Visual(go.transform, "Visual", Vector2.zero, size, Metal, 1); return body;
        }
        static void Create(MapGimmickKind kind, string name)
        {
            string path = Folder + "/" + name + ".prefab"; if (File.Exists(path)) return;
            var root = new GameObject(name); var gimmick = root.AddComponent<MapGimmick>(); gimmick.kind = kind;
            var visuals = new GameObject("Visuals (replaceable)"); visuals.transform.SetParent(root.transform, false); gimmick.visual = visuals.transform;
            switch (kind)
            {
                case MapGimmickKind.Sand:
                    var sandCollider = root.AddComponent<BoxCollider2D>(); sandCollider.size = new Vector2(2, .64f); sandCollider.offset = new Vector2(0, .18f); gimmick.activationDelay = 1.5f;
                    gimmick.sandFrames = AssetDatabase.LoadAllAssetsAtPath(SandSheet).OfType<Sprite>().Where(s => s.name.StartsWith("Sand_", System.StringComparison.Ordinal)).OrderBy(s => s.name, System.StringComparer.Ordinal).ToArray();
                    var sandVisual = Visual(visuals.transform, "Sand", new Vector2(0, .434f), new Vector2(2.48f, 2.48f), Color.white);
                    if (gimmick.sandFrames.Length >= 8) sandVisual.GetComponent<SpriteRenderer>().sprite = gimmick.sandFrames[0];
                    else { sandCollider.size = new Vector2(2, 1); sandCollider.offset = Vector2.zero; sandVisual.transform.localPosition = Vector3.zero; sandVisual.transform.localScale = new Vector3(2, 1, 1); sandVisual.GetComponent<SpriteRenderer>().color = Sand; }
                    break;
                case MapGimmickKind.Vent:
                    var vent = root.AddComponent<BoxCollider2D>(); vent.isTrigger = true; vent.size = new Vector2(2, 5); vent.offset = new Vector2(0, 2.5f); gimmick.force = 32;
                    var ventBody = Visual(visuals.transform, "Vent", Vector2.zero, new Vector2(2, .3f), Metal);
                    var air = Visual(visuals.transform, "Air", new Vector2(0, 2.5f), new Vector2(2, 5), new Color(.3f,.8f,1f,.2f));
                    var floorSprite = AssetDatabase.LoadAllAssetsAtPath(WindFloorSheet).OfType<Sprite>().FirstOrDefault(s => s.name == "Vent_Floor");
                    var windFrames = AssetDatabase.LoadAllAssetsAtPath(WindSheet).OfType<Sprite>().Where(s => s.name.StartsWith("Wind_", System.StringComparison.Ordinal)).OrderBy(s => s.name, System.StringComparer.Ordinal).ToArray();
                    if (floorSprite != null && windFrames.Length == 8)
                    {
                        ventBody.GetComponent<SpriteRenderer>().sprite = floorSprite; ventBody.GetComponent<SpriteRenderer>().color = Color.white;
                        ventBody.transform.localScale = Vector3.one; ventBody.transform.localPosition = new Vector3(0, -floorSprite.bounds.size.y * .5f, 0);
                        var airRenderer = air.GetComponent<SpriteRenderer>(); airRenderer.sprite = windFrames[0]; airRenderer.color = Color.white; airRenderer.sortingOrder = -1;
                        air.transform.localPosition = new Vector3(0, 2.25f, 0);
                        air.transform.localScale = new Vector3(2f / windFrames[0].bounds.size.x, 5f / windFrames[0].bounds.size.y, 1);
                        air.AddComponent<WindVentEffect>().frames = windFrames;
                    }
                    break;
                case MapGimmickKind.Pulley:
                    gimmick.platformA = Platform(root.transform, "Left platform", new Vector2(-1.5f, 0), new Vector2(2, .35f));
                    gimmick.platformB = Platform(root.transform, "Right platform", new Vector2(1.5f, 0), new Vector2(2, .35f));
                    Visual(visuals.transform, "Top", new Vector2(0, 2.5f), new Vector2(3.4f,.2f), Metal);
                    gimmick.ropeA = Visual(visuals.transform, "Left rope", new Vector2(-1.5f,1.25f), new Vector2(.08f,2.5f), Color.white).transform;
                    gimmick.ropeB = Visual(visuals.transform, "Right rope", new Vector2(1.5f,1.25f), new Vector2(.08f,2.5f), Color.white).transform; break;
                case MapGimmickKind.Oil:
                    var oil = root.AddComponent<BoxCollider2D>(); oil.isTrigger = true; oil.size = new Vector2(4, .65f); oil.offset = new Vector2(0,.3f);
                    Visual(visuals.transform, "Oil", new Vector2(0,.03f), new Vector2(4,.12f), new Color(.12f,.12f,.18f,.85f), 2); break;
                case MapGimmickKind.Cement:
                    root.AddComponent<BoxCollider2D>().size = new Vector2(2,1);
                    Visual(visuals.transform, "Cement", Vector2.zero, new Vector2(2,1), new Color(.58f,.58f,.52f)); break;
                case MapGimmickKind.Sparks:
                    gimmick.beamRange = 5; gimmick.effectSeconds = 1.5f;
                    Visual(visuals.transform, "Welder", Vector2.zero, new Vector2(1,.4f), Metal);
                    gimmick.beamVisual = Visual(visuals.transform, "Spark", new Vector2(0,-2.5f), new Vector2(.22f,5), new Color(1,.65f,.08f,.85f), 2).transform; break;
                case MapGimmickKind.Crane:
                    gimmick.rotatingBeam = Platform(root.transform, "Rotating beam", Vector2.zero, new Vector2(5,.35f));
                    Visual(visuals.transform, "Pivot", Vector2.zero, new Vector2(.6f,.6f), new Color(1,.75f,.1f), 2); break;
                case MapGimmickKind.Sprinkler:
                    var water = root.AddComponent<BoxCollider2D>(); water.isTrigger = true; water.size = new Vector2(2,5); water.offset = new Vector2(0,-2.5f); gimmick.force = 24;
                    Visual(visuals.transform, "Pipe", Vector2.zero, new Vector2(2,.4f), Metal);
                    Visual(visuals.transform, "Water", new Vector2(0,-2.5f), new Vector2(2,5), new Color(.2f,.6f,1f,.35f)); break;
                case MapGimmickKind.BrickWall:
                    root.AddComponent<BoxCollider2D>().size = new Vector2(.8f,3); gimmick.impactSpeed = 8;
                    Visual(visuals.transform, "Wall", Vector2.zero, new Vector2(.8f,3), new Color(.65f,.25f,.2f)); break;
                case MapGimmickKind.Magnet:
                    var magnet = root.AddComponent<BoxCollider2D>(); magnet.isTrigger = true; magnet.size = new Vector2(4,5); magnet.offset = new Vector2(0,-2.5f); gimmick.force = 28;
                    Visual(root.transform, "Magnet body", Vector2.zero, new Vector2(2,.5f), new Color(.8f,.15f,.16f), 1);
                    Visual(visuals.transform, "Magnetic field", new Vector2(0,-2.5f), new Vector2(4,5), new Color(.85f,.2f,.4f,.22f)); break;
                case MapGimmickKind.Nitrogen:
                    var gas = root.AddComponent<BoxCollider2D>(); gas.isTrigger = true; gas.size = new Vector2(3,4); gas.offset = new Vector2(0,2); gimmick.inactiveSeconds = 2.4f; gimmick.activeSeconds = .6f; gimmick.effectSeconds = 2;
                    Visual(root.transform, "Pipe", Vector2.zero, new Vector2(1,.4f), Metal, 1);
                    Visual(visuals.transform, "Gas", new Vector2(0,2), new Vector2(3,4), new Color(.55f,.95f,1f,.35f)); break;
            }
            PrefabUtility.SaveAsPrefabAsset(root, path); Object.DestroyImmediate(root);
        }
    }
}
