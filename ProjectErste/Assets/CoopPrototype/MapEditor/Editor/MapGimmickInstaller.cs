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
        public const string SeesawSheet = MapEditorInstaller.BasePath + "/Textures/seesaw_parts_512.png";
        public const string PulleySheet = MapEditorInstaller.BasePath + "/Textures/pulley_linked_platform_parts_384x160.png";
        public const string OilSheet = MapEditorInstaller.BasePath + "/Textures/Gimmicks/Oil/OilSurface_Tiles.png";
        public const string CementSheet = MapEditorInstaller.BasePath + "/Textures/Gimmicks/Cement/CementPile_Impact.png";
        public const string WeldingSheet = MapEditorInstaller.BasePath + "/Textures/Gimmicks/Welding/WeldingArc_Sheet.png";
        public const string CraneSheet = MapEditorInstaller.BasePath + "/Textures/Gimmicks/Crane/CraneParts_Sheet.png";
        public const string WaterSheet = MapEditorInstaller.BasePath + "/Textures/Gimmicks/WaterBarrier/WaterBarrier_Sheet.png";
        public const string BrickSheet = MapEditorInstaller.BasePath + "/Textures/Gimmicks/BrickWall/BrickWall_Break.png";
        public const string MagnetSheet = MapEditorInstaller.BasePath + "/Textures/Gimmicks/Magnet/Magnet_Field.png";
        public const string NitrogenSheet = "Assets/Resources/ShapeCoop/Effects/NitrogenGeyser_Sheet.png";
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
            Create(MapGimmickKind.Seesaw, "철골 시소 발판");
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
        // Reuses the original physics objects and their file IDs so existing section instances keep their references.
        public static bool ApplyPulleyArtwork(GameObject root)
        {
            var g = root.GetComponent<MapGimmick>();
            var sprites = AssetDatabase.LoadAllAssetsAtPath(PulleySheet).OfType<Sprite>().ToArray();
            var plate = sprites.FirstOrDefault(s => s.name == "Pulley_Platform");
            var wheel = sprites.FirstOrDefault(s => s.name == "Pulley_Wheel");
            var vertical = sprites.FirstOrDefault(s => s.name == "Pulley_RopeVertical");
            var horizontal = sprites.FirstOrDefault(s => s.name == "Pulley_RopeHorizontal");
            if (g == null || g.platformA == null || g.platformB == null || g.visual == null || g.ropeA == null || g.ropeB == null || plate == null || wheel == null || vertical == null || horizontal == null) return false;
            foreach (var body in new[] { g.platformA, g.platformB })
            {
                var art = body.transform.Find("Visual"); if (art == null) return false;
                art.localPosition = new Vector3(0, .14f, 0); art.localScale = Vector3.one;
                var renderer = art.GetComponent<SpriteRenderer>(); renderer.sprite = plate; renderer.color = Color.white; renderer.sortingOrder = 1;
            }
            foreach (var rope in new[] { g.ropeA, g.ropeB })
            {
                rope.localScale = Vector3.one;
                var renderer = rope.GetComponent<SpriteRenderer>(); renderer.sprite = vertical; renderer.color = Color.white; renderer.drawMode = SpriteDrawMode.Tiled; renderer.size = new Vector2(.08f, 2.08f); renderer.sortingOrder = 0;
                // Unity can change scale while switching the SpriteRenderer to tiled mode.
                rope.localScale = Vector3.one;
                rope.localPosition = new Vector3(rope == g.ropeA ? -1.5f : 1.5f, 1.46f, 0);
            }
            var left = g.visual.Find("Top"); if (left == null) return false;
            var right = g.visual.Find("Right wheel"); if (right == null) right = new GameObject("Right wheel").transform;
            right.SetParent(g.visual, false);
            foreach (var pulley in new[] { left, right })
            {
                pulley.localPosition = new Vector3(pulley == left ? -1.147f : 1.147f, 2.982f, 0); pulley.localRotation = Quaternion.identity; pulley.localScale = Vector3.one;
                var renderer = pulley.GetComponent<SpriteRenderer>(); if (renderer == null) renderer = pulley.gameObject.AddComponent<SpriteRenderer>();
                renderer.sprite = wheel; renderer.color = Color.white; renderer.flipX = pulley == right; renderer.sortingOrder = 1;
            }
            var bridge = g.visual.Find("Connecting rope"); if (bridge == null) bridge = new GameObject("Connecting rope").transform;
            bridge.SetParent(g.visual, false); bridge.localPosition = new Vector3(0, 3.17f, 0); bridge.localRotation = Quaternion.identity; bridge.localScale = Vector3.one;
            var bridgeRenderer = bridge.GetComponent<SpriteRenderer>(); if (bridgeRenderer == null) bridgeRenderer = bridge.gameObject.AddComponent<SpriteRenderer>();
            bridgeRenderer.sprite = horizontal; bridgeRenderer.color = Color.white; bridgeRenderer.drawMode = SpriteDrawMode.Tiled; bridgeRenderer.size = new Vector2(2.1f, .1f); bridgeRenderer.sortingOrder = 0;
            return true;
        }
        public static bool ApplyOilArtwork(GameObject root)
        {
            var g = root.GetComponent<MapGimmick>(); var area = root.GetComponent<BoxCollider2D>();
            var sprites = AssetDatabase.LoadAllAssetsAtPath(OilSheet).OfType<Sprite>().ToArray();
            var start = sprites.FirstOrDefault(s => s.name == "OilSurface_Left");
            var fill = sprites.FirstOrDefault(s => s.name == "OilSurface_Middle");
            var end = sprites.FirstOrDefault(s => s.name == "OilSurface_Right");
            var small = sprites.FirstOrDefault(s => s.name == "OilSurface_Small");
            if (g == null || area == null || g.visual == null || start == null || fill == null || end == null || small == null) return false;
            var center = g.visual.Find("Oil"); if (center == null || center.GetComponent<SpriteRenderer>() == null) return false;
            var visual = root.GetComponent<OilSurfaceVisual>(); if (visual == null) visual = root.AddComponent<OilSurfaceVisual>();
            visual.area = area; visual.middle = center.GetComponent<SpriteRenderer>();
            visual.left = OilPart(g.visual, "Oil left", start); visual.right = OilPart(g.visual, "Oil right", end); visual.isolated = OilPart(g.visual, "Oil small", small);
            visual.middle.sprite = fill; visual.middle.color = Color.white; visual.middle.sortingOrder = 2; visual.middle.drawMode = SpriteDrawMode.Tiled;
            center.localScale = Vector3.one; visual.Refresh();
            return true;
        }
        static SpriteRenderer OilPart(Transform parent, string name, Sprite sprite)
        {
            var part = parent.Find(name); if (part == null) { part = new GameObject(name).transform; part.SetParent(parent, false); }
            part.localRotation = Quaternion.identity; part.localScale = Vector3.one;
            var renderer = part.GetComponent<SpriteRenderer>(); if (renderer == null) renderer = part.gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite; renderer.drawMode = SpriteDrawMode.Simple; renderer.color = Color.white; renderer.sortingOrder = 2;
            part.localScale = Vector3.one; return renderer;
        }
        public static bool ApplyCementArtwork(GameObject root)
        {
            var g = root.GetComponent<MapGimmick>();
            var frames = AssetDatabase.LoadAllAssetsAtPath(CementSheet).OfType<Sprite>().Where(s => s.name.StartsWith("CementPile_", System.StringComparison.Ordinal)).OrderBy(s => s.name, System.StringComparer.Ordinal).ToArray();
            if (g == null || g.visual == null || frames.Length != 8) return false;
            var pile = g.visual.Find("Cement"); if (pile == null || pile.GetComponent<SpriteRenderer>() == null) return false;
            var pileRenderer = pile.GetComponent<SpriteRenderer>(); pileRenderer.sprite = frames[0]; pileRenderer.color = Color.white; pileRenderer.sortingOrder = 0;
            // Fit the visible pile to the existing 2 x 1 collider without changing gameplay geometry.
            pile.localPosition = new Vector3(0, .1f, 0); pile.localScale = new Vector3(1, .615f, 1);
            var dust = g.visual.Find("Impact dust"); if (dust == null) { dust = new GameObject("Impact dust").transform; dust.SetParent(g.visual, false); }
            dust.localPosition = Vector3.zero; dust.localScale = new Vector3(1, .615f, 1);
            var dustRenderer = dust.GetComponent<SpriteRenderer>(); if (dustRenderer == null) dustRenderer = dust.gameObject.AddComponent<SpriteRenderer>();
            dustRenderer.sprite = null; dustRenderer.color = Color.white; dustRenderer.sortingOrder = 2;
            var animation = g.visual.GetComponent<CementImpactVisual>(); if (animation == null) animation = g.visual.gameObject.AddComponent<CementImpactVisual>();
            animation.pile = pileRenderer; animation.dust = dustRenderer; animation.frames = frames;
            return true;
        }
        public static bool ApplyBrickArtwork(GameObject root)
        {
            var g = root.GetComponent<MapGimmick>();
            var sprites = AssetDatabase.LoadAllAssetsAtPath(BrickSheet).OfType<Sprite>().ToArray();
            var intact = sprites.FirstOrDefault(s => s.name == "BrickWall_Intact");
            var cracked = sprites.FirstOrDefault(s => s.name == "BrickWall_Cracked");
            var rubble = sprites.FirstOrDefault(s => s.name == "BrickWall_Rubble");
            var frames = sprites.Where(s => s.name.StartsWith("BrickWall_Burst_", System.StringComparison.Ordinal)).OrderBy(s => s.name, System.StringComparer.Ordinal).ToArray();
            if (g == null || g.visual == null || intact == null || cracked == null || rubble == null || frames.Length != 6) return false;
            var wall = g.visual.Find("Wall"); if (wall == null || wall.GetComponent<SpriteRenderer>() == null) return false;
            var wallRenderer = wall.GetComponent<SpriteRenderer>(); wallRenderer.sprite = intact; wallRenderer.color = Color.white; wallRenderer.sortingOrder = 0;
            // Keep the original .8 x 3 collider and align the visible bricks to it.
            wall.localPosition = Vector3.zero; wall.localScale = new Vector3(1.164f, 1.699f, 1);
            var debris = g.visual.Find("Rubble"); if (debris == null) { debris = new GameObject("Rubble").transform; debris.SetParent(g.visual, false); }
            debris.localPosition = new Vector3(0, -1.5f, 0); debris.localScale = new Vector3(1.164f, 1.699f, 1);
            var rubbleRenderer = debris.GetComponent<SpriteRenderer>(); if (rubbleRenderer == null) rubbleRenderer = debris.gameObject.AddComponent<SpriteRenderer>();
            rubbleRenderer.sprite = null; rubbleRenderer.color = Color.white; rubbleRenderer.sortingOrder = 0;
            var effect = g.visual.Find("Breaking debris"); if (effect == null) { effect = new GameObject("Breaking debris").transform; effect.SetParent(g.visual, false); }
            effect.localPosition = new Vector3(0, -.55f, 0); effect.localScale = new Vector3(2, 2, 1);
            var burst = effect.GetComponent<SpriteRenderer>(); if (burst == null) burst = effect.gameObject.AddComponent<SpriteRenderer>();
            burst.sprite = null; burst.color = Color.white; burst.sortingOrder = 2;
            var animation = g.visual.GetComponent<BrickWallVisual>(); if (animation == null) animation = g.visual.gameObject.AddComponent<BrickWallVisual>();
            animation.wall = wallRenderer; animation.rubble = rubbleRenderer; animation.burst = burst;
            animation.intact = intact; animation.cracked = cracked; animation.rubbleSprite = rubble; animation.burstFrames = frames;
            return true;
        }
        public static bool ApplyMagnetArtwork(GameObject root)
        {
            var g = root.GetComponent<MapGimmick>(); var area = root.GetComponent<BoxCollider2D>();
            var sprites = AssetDatabase.LoadAllAssetsAtPath(MagnetSheet).OfType<Sprite>().ToArray();
            var off = sprites.FirstOrDefault(s => s.name == "Magnet_Device_Off");
            var on = sprites.Where(s => s.name.StartsWith("Magnet_Device_On_", System.StringComparison.Ordinal)).OrderBy(s => s.name, System.StringComparer.Ordinal).ToArray();
            var frames = sprites.Where(s => s.name.StartsWith("Magnet_Field_", System.StringComparison.Ordinal)).OrderBy(s => s.name, System.StringComparer.Ordinal).ToArray();
            if (g == null || area == null || g.visual == null || off == null || on.Length != 2 || frames.Length != 4) return false;
            var body = root.transform.Find("Magnet body"); var field = g.visual.Find("Magnetic field");
            if (body == null || field == null || body.GetComponent<SpriteRenderer>() == null || field.GetComponent<SpriteRenderer>() == null) return false;
            var device = body.GetComponent<SpriteRenderer>(); device.sprite = off; device.color = Color.white; device.sortingOrder = 1;
            body.localPosition = new Vector3(0, -.25f, 0); body.localScale = new Vector3(2.55f, 2f, 1);
            var fieldRenderer = field.GetComponent<SpriteRenderer>(); fieldRenderer.sprite = frames[0]; fieldRenderer.color = Color.white; fieldRenderer.sortingOrder = -1;
            var animation = g.visual.GetComponent<MagnetFieldVisual>(); if (animation == null) animation = g.visual.gameObject.AddComponent<MagnetFieldVisual>();
            animation.area = area; animation.device = device; animation.field = fieldRenderer; animation.deviceOff = off; animation.deviceOnFrames = on; animation.fieldFrames = frames;
            animation.RefreshGeometry(); return true;
        }
        public static bool ApplyNitrogenArtwork(GameObject root)
        {
            var g = root.GetComponent<MapGimmick>(); var area = root.GetComponent<BoxCollider2D>();
            var sprites = AssetDatabase.LoadAllAssetsAtPath(NitrogenSheet).OfType<Sprite>().ToArray();
            var pipe = sprites.FirstOrDefault(s => s.name == "Nitrogen_Pipe");
            var ice = sprites.FirstOrDefault(s => s.name == "Nitrogen_Ice");
            var frames = sprites.Where(s => s.name.StartsWith("Nitrogen_Gas_", System.StringComparison.Ordinal)).OrderBy(s => s.name, System.StringComparer.Ordinal).ToArray();
            if (g == null || area == null || g.visual == null || pipe == null || ice == null || frames.Length != 6) return false;
            var pipeObject = root.transform.Find("Pipe"); var gasObject = g.visual.Find("Gas");
            if (pipeObject == null || gasObject == null || pipeObject.GetComponent<SpriteRenderer>() == null || gasObject.GetComponent<SpriteRenderer>() == null) return false;
            var pipeRenderer = pipeObject.GetComponent<SpriteRenderer>(); pipeRenderer.sprite = pipe; pipeRenderer.color = Color.white; pipeRenderer.sortingOrder = 1;
            pipeObject.localPosition = new Vector3(0, .25f, 0); pipeObject.localScale = new Vector3(1.4f, 1.4f, 1);
            var gas = gasObject.GetComponent<SpriteRenderer>(); gas.sprite = frames[2]; gas.color = Color.white; gas.sortingOrder = 0;
            var background = g.visual.Find("Active area"); if (background == null) { background = new GameObject("Active area").transform; background.SetParent(g.visual, false); }
            var indicator = background.GetComponent<SpriteRenderer>(); if (indicator == null) indicator = background.gameObject.AddComponent<SpriteRenderer>();
            indicator.sprite = frames[2]; indicator.color = new Color(1, 1, 1, .25f); indicator.sortingOrder = -1;
            var animation = g.visual.GetComponent<NitrogenGeyserVisual>(); if (animation == null) animation = g.visual.gameObject.AddComponent<NitrogenGeyserVisual>();
            animation.area = area; animation.gas = gas; animation.activeArea = indicator; animation.frames = frames; animation.RefreshGeometry(); return true;
        }
        public static bool ApplyWeldingArtwork(GameObject root)
        {
            var g = root.GetComponent<MapGimmick>();
            var sprites = AssetDatabase.LoadAllAssetsAtPath(WeldingSheet).OfType<Sprite>().ToArray();
            var off = sprites.FirstOrDefault(s => s.name == "Welding_Device_Off"); var on = sprites.FirstOrDefault(s => s.name == "Welding_Device_On");
            var arcs = sprites.Where(s => s.name.StartsWith("Welding_Arc_", System.StringComparison.Ordinal)).OrderBy(s => s.name, System.StringComparer.Ordinal).ToArray();
            var impacts = sprites.Where(s => s.name.StartsWith("Welding_Impact_", System.StringComparison.Ordinal)).OrderBy(s => s.name, System.StringComparer.Ordinal).ToArray();
            if (g == null || g.visual == null || g.beamVisual == null || off == null || on == null || arcs.Length != 6 || impacts.Length != 4) return false;
            var welder = g.visual.Find("Welder"); if (welder == null || welder.GetComponent<SpriteRenderer>() == null) return false;
            // Preview the active beam in the editor; Initialize shows the real initial off state in play.
            var device = welder.GetComponent<SpriteRenderer>(); device.sprite = on; device.color = Color.white; device.sortingOrder = 1;
            welder.localPosition = Vector3.zero; welder.localScale = Vector3.one;
            var original = g.beamVisual.GetComponent<SpriteRenderer>(); if (original != null) { original.sprite = null; original.color = Color.white; }
            g.beamVisual.localScale = new Vector3(1, g.beamRange, 1);
            var art = g.beamVisual.Find("Arc art"); if (art == null) { art = new GameObject("Arc art").transform; art.SetParent(g.beamVisual, false); }
            art.localPosition = Vector3.zero; art.localScale = new Vector3(.6f / arcs[0].bounds.size.x, 1f / arcs[0].bounds.size.y, 1);
            var arc = art.GetComponent<SpriteRenderer>(); if (arc == null) arc = art.gameObject.AddComponent<SpriteRenderer>();
            arc.sprite = arcs[0]; arc.color = Color.white; arc.sortingOrder = 2;
            var hit = g.visual.Find("Contact sparks"); if (hit == null) { hit = new GameObject("Contact sparks").transform; hit.SetParent(g.visual, false); }
            hit.localPosition = new Vector3(0, -g.beamRange, 0); hit.localScale = Vector3.one;
            var impact = hit.GetComponent<SpriteRenderer>(); if (impact == null) impact = hit.gameObject.AddComponent<SpriteRenderer>();
            impact.sprite = null; impact.color = Color.white; impact.sortingOrder = 3;
            var animation = g.visual.GetComponent<WeldingSparkVisual>(); if (animation == null) animation = g.visual.gameObject.AddComponent<WeldingSparkVisual>();
            animation.device = device; animation.arc = arc; animation.impact = impact; animation.deviceOff = off; animation.deviceOn = on; animation.arcFrames = arcs; animation.impactFrames = impacts;
            return true;
        }
        public static bool ApplyCraneArtwork(GameObject root)
        {
            var g = root.GetComponent<MapGimmick>();
            var sprites = AssetDatabase.LoadAllAssetsAtPath(CraneSheet).OfType<Sprite>().ToArray();
            var tower = sprites.FirstOrDefault(s => s.name == "Crane_Tower");
            var gear = sprites.FirstOrDefault(s => s.name == "Crane_Gear");
            var plate = sprites.FirstOrDefault(s => s.name == "Crane_Plate");
            var beam = sprites.FirstOrDefault(s => s.name == "Crane_Beam");
            if (g == null || g.visual == null || g.rotatingBeam == null || tower == null || gear == null || plate == null || beam == null) return false;
            var towerObject = g.visual.Find("Pivot"); var beamObject = g.rotatingBeam.transform.Find("Visual");
            if (towerObject == null || beamObject == null || towerObject.GetComponent<SpriteRenderer>() == null || beamObject.GetComponent<SpriteRenderer>() == null) return false;
            var support = towerObject.GetComponent<SpriteRenderer>(); support.sprite = tower; support.color = Color.white; support.sortingOrder = -1;
            towerObject.localPosition = Vector3.zero; towerObject.localScale = Vector3.one;
            var beamRenderer = beamObject.GetComponent<SpriteRenderer>(); beamRenderer.sprite = beam; beamRenderer.color = Color.white; beamRenderer.sortingOrder = 1;
            beamRenderer.drawMode = SpriteDrawMode.Sliced; beamRenderer.size = new Vector2(5.18f, 1.28f);
            beamObject.localPosition = Vector3.zero; beamObject.localScale = Vector3.one;
            for (int i = 1; i < 3; i++)
            {
                string spokeName = "Spoke " + (i * 60);
                var spoke = g.rotatingBeam.transform.Find(spokeName);
                if (spoke == null) { spoke = new GameObject(spokeName).transform; spoke.SetParent(g.rotatingBeam.transform, false); }
                spoke.localPosition = Vector3.zero; spoke.localRotation = Quaternion.Euler(0, 0, i * 60);
                var spokeRenderer = spoke.GetComponent<SpriteRenderer>(); if (spokeRenderer == null) spokeRenderer = spoke.gameObject.AddComponent<SpriteRenderer>();
                spokeRenderer.sprite = beam; spokeRenderer.color = Color.white; spokeRenderer.sortingOrder = 1; spokeRenderer.drawMode = SpriteDrawMode.Sliced; spokeRenderer.size = new Vector2(5.18f, 1.28f); spoke.localScale = Vector3.one;
            }
            var wheel = g.rotatingBeam.transform.Find("Center gear");
            if (wheel == null) { wheel = new GameObject("Center gear").transform; wheel.SetParent(g.rotatingBeam.transform, false); }
            wheel.localPosition = Vector3.zero; wheel.localScale = Vector3.one;
            var wheelRenderer = wheel.GetComponent<SpriteRenderer>(); if (wheelRenderer == null) wheelRenderer = wheel.gameObject.AddComponent<SpriteRenderer>();
            wheelRenderer.sprite = gear; wheelRenderer.color = Color.white; wheelRenderer.sortingOrder = 3;
            // Keep the original component for prefab compatibility, but only the six horizontal plates are solid.
            var oldCollider = g.rotatingBeam.GetComponent<Collider2D>(); if (oldCollider != null) oldCollider.enabled = false;
            var oldLeft = g.rotatingBeam.transform.Find("Platform detail");
            var oldRight = g.rotatingBeam.transform.Find("Platform detail right");
            var controller = root.GetComponent<RotatingCranePlatforms>(); if (controller == null) controller = root.AddComponent<RotatingCranePlatforms>();
            var platforms = new Rigidbody2D[6];
            for (int i = 0; i < platforms.Length; i++)
            {
                string name = "Moving platform " + (i + 1).ToString("00");
                var platform = root.transform.Find(name);
                if (platform == null) { platform = new GameObject(name).transform; platform.SetParent(root.transform, false); }
                float radians = i * 60f * Mathf.Deg2Rad;
                platform.localPosition = new Vector3(Mathf.Cos(radians) * 2.15f, Mathf.Sin(radians) * 2.15f, 0);
                platform.localRotation = Quaternion.identity; platform.localScale = Vector3.one;
                var body = platform.GetComponent<Rigidbody2D>(); if (body == null) body = platform.gameObject.AddComponent<Rigidbody2D>();
                body.bodyType = RigidbodyType2D.Kinematic; body.interpolation = RigidbodyInterpolation2D.Interpolate; body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                var hitbox = platform.GetComponent<BoxCollider2D>(); if (hitbox == null) hitbox = platform.gameObject.AddComponent<BoxCollider2D>();
                hitbox.size = new Vector2(1.12f, .19f); hitbox.offset = new Vector2(0, .395f);
                var part = platform.Find("Platform art");
                if (part == null) part = i == 0 ? oldRight : i == 3 ? oldLeft : null;
                if (part == null) part = new GameObject("Platform art").transform;
                part.SetParent(platform, false); part.name = "Platform art";
                part.localPosition = new Vector3(0, .49f, 0); part.localRotation = Quaternion.identity; part.localScale = Vector3.one;
                var partRenderer = part.GetComponent<SpriteRenderer>(); if (partRenderer == null) partRenderer = part.gameObject.AddComponent<SpriteRenderer>();
                partRenderer.sprite = plate; partRenderer.color = Color.white; partRenderer.sortingOrder = 2;
                platforms[i] = body;
            }
            controller.platforms = platforms;
            controller.SetDimensions(controller.Radius, controller.PlatformSize);
            controller.RefreshPreview();
            return true;
        }
        public static bool ApplyWaterArtwork(GameObject root)
        {
            var g = root.GetComponent<MapGimmick>();
            var sprites = AssetDatabase.LoadAllAssetsAtPath(WaterSheet).OfType<Sprite>().ToArray();
            var pipeSprite = sprites.FirstOrDefault(s => s.name == "WaterBarrier_Pipe");
            var streams = sprites.Where(s => s.name.StartsWith("WaterBarrier_Stream_", System.StringComparison.Ordinal)).OrderBy(s => s.name, System.StringComparer.Ordinal).ToArray();
            var splashes = sprites.Where(s => s.name.StartsWith("WaterBarrier_Splash_", System.StringComparison.Ordinal)).OrderBy(s => s.name, System.StringComparer.Ordinal).ToArray();
            var splits = sprites.Where(s => s.name.StartsWith("WaterBarrier_Split_", System.StringComparison.Ordinal)).OrderBy(s => s.name, System.StringComparer.Ordinal).ToArray();
            if (g == null || g.visual == null || pipeSprite == null || streams.Length != 4 || splashes.Length != 3 || splits.Length != 4) return false;
            var pipe = g.visual.Find("Pipe"); var water = g.visual.Find("Water");
            if (pipe == null || water == null || pipe.GetComponent<SpriteRenderer>() == null || water.GetComponent<SpriteRenderer>() == null) return false;
            var pipeRenderer = pipe.GetComponent<SpriteRenderer>(); pipeRenderer.sprite = pipeSprite; pipeRenderer.color = Color.white; pipeRenderer.sortingOrder = 1;
            pipe.localPosition = Vector3.zero; pipe.localScale = new Vector3(2.2f, 2.2f, 1);
            var original = water.GetComponent<SpriteRenderer>(); original.sprite = null; original.color = Color.white;
            var stream = water.Find("Stream art"); if (stream == null) { stream = new GameObject("Stream art").transform; stream.SetParent(water, false); }
            stream.localPosition = Vector3.zero; stream.localScale = new Vector3(128f / 70f, 128f / 238f, 1);
            var streamRenderer = stream.GetComponent<SpriteRenderer>(); if (streamRenderer == null) streamRenderer = stream.gameObject.AddComponent<SpriteRenderer>();
            streamRenderer.sprite = streams[0]; streamRenderer.color = Color.white; streamRenderer.sortingOrder = 0;
            var splash = g.visual.Find("Shield splash"); if (splash == null) { splash = new GameObject("Shield splash").transform; splash.SetParent(g.visual, false); }
            var splashRenderer = splash.GetComponent<SpriteRenderer>(); if (splashRenderer == null) splashRenderer = splash.gameObject.AddComponent<SpriteRenderer>();
            splashRenderer.sprite = null; splashRenderer.color = Color.white; splashRenderer.sortingOrder = 2;
            var split = g.visual.Find("Shield split"); if (split == null) { split = new GameObject("Shield split").transform; split.SetParent(g.visual, false); }
            var splitRenderer = split.GetComponent<SpriteRenderer>(); if (splitRenderer == null) splitRenderer = split.gameObject.AddComponent<SpriteRenderer>();
            splitRenderer.sprite = null; splitRenderer.color = Color.white; splitRenderer.sortingOrder = 2;
            var visual = g.visual.GetComponent<WaterBarrierVisual>(); if (visual == null) visual = g.visual.gameObject.AddComponent<WaterBarrierVisual>();
            visual.streamFrames = streams; visual.splashFrames = splashes; visual.splitFrames = splits; visual.splash = splashRenderer; visual.split = splitRenderer;
            return true;
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
                    gimmick.ropeB = Visual(visuals.transform, "Right rope", new Vector2(1.5f,1.25f), new Vector2(.08f,2.5f), Color.white).transform;
                    ApplyPulleyArtwork(root); break;
                case MapGimmickKind.Oil:
                    var oil = root.AddComponent<BoxCollider2D>(); oil.isTrigger = true; oil.size = new Vector2(4, .65f); oil.offset = new Vector2(0,.3f);
                    Visual(visuals.transform, "Oil", new Vector2(0,.03f), new Vector2(4,.12f), new Color(.12f,.12f,.18f,.85f), 2);
                    ApplyOilArtwork(root); break;
                case MapGimmickKind.Cement:
                    root.AddComponent<BoxCollider2D>().size = new Vector2(2,1);
                    Visual(visuals.transform, "Cement", Vector2.zero, new Vector2(2,1), new Color(.58f,.58f,.52f));
                    ApplyCementArtwork(root); break;
                case MapGimmickKind.Sparks:
                    gimmick.beamRange = 5; gimmick.effectSeconds = 1.5f;
                    Visual(visuals.transform, "Welder", Vector2.zero, new Vector2(1,.4f), Metal);
                    gimmick.beamVisual = Visual(visuals.transform, "Spark", new Vector2(0,-2.5f), new Vector2(.22f,5), new Color(1,.65f,.08f,.85f), 2).transform;
                    ApplyWeldingArtwork(root); break;
                case MapGimmickKind.Crane:
                    gimmick.rotatingBeam = Platform(root.transform, "Rotating beam", Vector2.zero, new Vector2(5,.35f));
                    Visual(visuals.transform, "Pivot", Vector2.zero, new Vector2(.6f,.6f), new Color(1,.75f,.1f), 2);
                    ApplyCraneArtwork(root); break;
                case MapGimmickKind.Sprinkler:
                    var water = root.AddComponent<BoxCollider2D>(); water.isTrigger = true; water.size = new Vector2(2,5); water.offset = new Vector2(0,-2.5f); gimmick.force = 24;
                    Visual(visuals.transform, "Pipe", Vector2.zero, new Vector2(2,.4f), Metal);
                    Visual(visuals.transform, "Water", new Vector2(0,-2.5f), new Vector2(2,5), new Color(.2f,.6f,1f,.35f));
                    ApplyWaterArtwork(root); break;
                case MapGimmickKind.BrickWall:
                    root.AddComponent<BoxCollider2D>().size = new Vector2(.8f,3); gimmick.impactSpeed = 8;
                    Visual(visuals.transform, "Wall", Vector2.zero, new Vector2(.8f,3), new Color(.65f,.25f,.2f)); ApplyBrickArtwork(root); break;
                case MapGimmickKind.Magnet:
                    var magnet = root.AddComponent<BoxCollider2D>(); magnet.isTrigger = true; magnet.size = new Vector2(4,5); magnet.offset = new Vector2(0,-2.5f); gimmick.force = 28;
                    Visual(root.transform, "Magnet body", Vector2.zero, new Vector2(2,.5f), new Color(.8f,.15f,.16f), 1);
                    Visual(visuals.transform, "Magnetic field", new Vector2(0,-2.5f), new Vector2(4,5), new Color(.85f,.2f,.4f,.22f)); ApplyMagnetArtwork(root); break;
                case MapGimmickKind.Nitrogen:
                    var gas = root.AddComponent<BoxCollider2D>(); gas.isTrigger = true; gas.size = new Vector2(3,4); gas.offset = new Vector2(0,2); gimmick.inactiveSeconds = 2.4f; gimmick.activeSeconds = .6f; gimmick.effectSeconds = 2;
                    Visual(root.transform, "Pipe", Vector2.zero, new Vector2(1,.4f), Metal, 1);
                    Visual(visuals.transform, "Gas", new Vector2(0,2), new Vector2(3,4), new Color(.55f,.95f,1f,.35f)); ApplyNitrogenArtwork(root); break;
                case MapGimmickKind.Seesaw:
                    var pivot = root.AddComponent<Rigidbody2D>(); pivot.bodyType = RigidbodyType2D.Static;
                    var postCollider = root.AddComponent<BoxCollider2D>(); postCollider.size = new Vector2(.65f, 2f); postCollider.offset = new Vector2(0, -1.1f);
                    var footCollider = root.AddComponent<BoxCollider2D>(); footCollider.size = new Vector2(1.3f, .2f); footCollider.offset = new Vector2(0, -2.04f);
                    var sprites = AssetDatabase.LoadAllAssetsAtPath(SeesawSheet).OfType<Sprite>().ToArray();
                    var beamSprite = sprites.FirstOrDefault(s => s.name == "Seesaw_Beam");
                    var supportSprite = sprites.FirstOrDefault(s => s.name == "Seesaw_Support");
                    var support = Visual(visuals.transform, "Support", new Vector2(0, -1), new Vector2(.65f, 2f), Metal, 2);
                    if (supportSprite != null) { support.GetComponent<SpriteRenderer>().sprite = supportSprite; support.GetComponent<SpriteRenderer>().color = Color.white; support.transform.localPosition = Vector3.zero; support.transform.localScale = Vector3.one; }
                    var beamObject = new GameObject("Seesaw beam (physics)"); beamObject.transform.SetParent(root.transform, false);
                    var beam = beamObject.AddComponent<Rigidbody2D>(); beam.bodyType = RigidbodyType2D.Dynamic; beam.mass = 2; beam.gravityScale = 1; beam.angularDamping = 1.2f; beam.interpolation = RigidbodyInterpolation2D.Interpolate; beam.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
                    beamObject.AddComponent<BoxCollider2D>().size = new Vector2(7.94f, .62f);
                    var hinge = beamObject.AddComponent<HingeJoint2D>(); hinge.connectedBody = pivot; hinge.autoConfigureConnectedAnchor = false; hinge.anchor = Vector2.zero; hinge.connectedAnchor = Vector2.zero; hinge.enableCollision = false; hinge.useLimits = true; hinge.limits = new JointAngleLimits2D { min = -25, max = 25 };
                    var beamArt = Visual(beamObject.transform, "Beam art", Vector2.zero, new Vector2(8, .65f), Metal, 1);
                    if (beamSprite != null) { beamArt.GetComponent<SpriteRenderer>().sprite = beamSprite; beamArt.GetComponent<SpriteRenderer>().color = Color.white; beamArt.transform.localScale = Vector3.one; }
                    gimmick.rotatingBeam = beam;
                    break;
            }
            PrefabUtility.SaveAsPrefabAsset(root, path); Object.DestroyImmediate(root);
        }
    }
}
