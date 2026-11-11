using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Tilemaps;
using Object = UnityEngine.Object;

namespace ShapeCoop.Editor
{
    internal static class MapTerrainBake
    {
        const string BasicFolder = MapEditorInstaller.PalettePath + "/기본 오브젝트/";
        const string TileFolder = MapEditorInstaller.BasePath + "/GeneratedTerrainTiles";
        const float Tolerance = .0001f;
        static readonly Vector2[] Square =
        {
            new Vector2(-.5f, -.5f), new Vector2(.5f, -.5f),
            new Vector2(.5f, .5f), new Vector2(-.5f, .5f)
        };

        sealed class Entry
        {
            public Transform source;
            public int sibling;
            public Vector2Int halfCell;
            public int quarterTurns;
            public SpriteRenderer renderer;
            public Collider2D collider;
            public Vector2[] polygon;
            public Vector3Int Cell => new Vector3Int(
                (halfCell.x - (halfCell.x & 1)) / 2, (halfCell.y - (halfCell.y & 1)) / 2, 0);
        }

        internal static bool HasCandidates(MapSection source)
        {
            if (!CanBakeSection(source)) return false;
            int count = 0;
            foreach (Transform child in source.transform)
                if (ReadEntry(child) != null && ++count >= 2) return true;
            return false;
        }

        // Read scene prefab origins and references from source. Only replace objects in the staged prefab contents.
        internal static int Bake(MapSection source, MapSection exportCopy)
        {
            if (!CanBakeSection(source) || exportCopy == null || source.gameObject.scene == exportCopy.gameObject.scene ||
                source.transform.childCount != exportCopy.transform.childCount) return 0;

            var entries = new List<Entry>();
            var byPosition = new Dictionary<Vector2Int, int>();
            var candidates = new Dictionary<GameObject, Entry>();
            foreach (Transform child in source.transform)
            {
                var entry = ReadEntry(child);
                if (entry == null) continue;
                var copy = exportCopy.transform.GetChild(entry.sibling);
                if (copy.name != child.name || copy.localPosition != child.localPosition ||
                    !TryReadQuarterTurns(copy.localRotation, out int copiedTurns) || copiedTurns != entry.quarterTurns ||
                    copy.GetComponent<SpriteRenderer>()?.sprite != entry.renderer.sprite) return 0;

                entries.Add(entry);
                candidates.Add(child.gameObject, entry);
                byPosition.TryGetValue(entry.halfCell, out int count);
                byPosition[entry.halfCell] = count + 1;
            }
            if (entries.Count < 2) return 0;

            var preserve = FindReferencedEntries(source, candidates);
            foreach (var entry in entries)
                if (OverlapsCandidate(entry.halfCell, byPosition)) preserve.Add(entry);
            PreserveAmbiguousRendering(source, entries, candidates, preserve);

            var groups = new Dictionary<(int x, int y, int layer, int sortingLayer, int order, uint renderLayer,
                Material material, PhysicsMaterial2D physicsMaterial), List<Entry>>();
            foreach (var entry in entries)
            {
                if (preserve.Contains(entry)) continue;
                var key = (entry.halfCell.x & 1, entry.halfCell.y & 1, entry.source.gameObject.layer,
                    entry.renderer.sortingLayerID, entry.renderer.sortingOrder, entry.renderer.renderingLayerMask,
                    entry.renderer.sharedMaterial, entry.collider.sharedMaterial);
                if (!groups.TryGetValue(key, out var group)) groups.Add(key, group = new List<Entry>());
                group.Add(entry);
            }

            var removed = new List<GameObject>();
            var tiles = new Dictionary<Sprite, Tile>();
            foreach (var group in groups.Values)
            {
                if (group.Count < 2 || !BakeGroup(exportCopy, group, tiles)) continue;
                foreach (var entry in group) removed.Add(exportCopy.transform.GetChild(entry.sibling).gameObject);
            }
            foreach (var instance in removed) Object.DestroyImmediate(instance);
            return removed.Count;
        }

        static bool CanBakeSection(MapSection section) =>
            section != null && section.transform.parent == null && section.gameObject.activeInHierarchy &&
            section.transform.localScale.Equals(Vector3.one) && section.transform.localRotation.Equals(Quaternion.identity) &&
            section.GetComponent<Animator>() == null && section.GetComponent<Animation>() == null &&
            section.GetComponent<SortingGroup>() == null;

        static Entry ReadEntry(Transform child)
        {
            var instance = child.gameObject;
            if (!instance.activeSelf || child.childCount != 0 || !child.localScale.Equals(Vector3.one) ||
                !TryReadQuarterTurns(child.localRotation, out int quarterTurns) || !instance.CompareTag("Untagged") ||
                GameObjectUtility.GetStaticEditorFlags(instance) != 0) return null;

            var position = child.localPosition;
            if (!float.IsFinite(position.x) || !float.IsFinite(position.y) ||
                Mathf.Abs(position.x) > 100000f || Mathf.Abs(position.y) > 100000f || position.z != 0) return null;
            var halfCell = new Vector2Int(Mathf.RoundToInt(position.x * 2), Mathf.RoundToInt(position.y * 2));
            if (Mathf.Abs(position.x - halfCell.x * .5f) > Tolerance ||
                Mathf.Abs(position.y - halfCell.y * .5f) > Tolerance) return null;

            if (!PrefabUtility.IsAnyPrefabInstanceRoot(instance)) return null;
            string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance);
            if (string.IsNullOrEmpty(path) || !path.StartsWith(BasicFolder, StringComparison.Ordinal) ||
                PrefabUtility.HasPrefabInstanceAnyOverrides(instance, false)) return null;

            var renderer = instance.GetComponent<SpriteRenderer>();
            var collider = instance.GetComponent<Collider2D>();
            if (instance.GetComponents<Component>().Length != 3 || renderer == null || collider == null ||
                !renderer.enabled || renderer.forceRenderingOff || renderer.sprite == null ||
                renderer.drawMode != SpriteDrawMode.Simple || renderer.flipX || renderer.flipY ||
                renderer.color != Color.white || renderer.maskInteraction != SpriteMaskInteraction.None ||
                renderer.sharedMaterial == null || renderer.HasPropertyBlock() ||
                renderer.shadowCastingMode != ShadowCastingMode.Off || renderer.receiveShadows) return null;

            var sprite = renderer.sprite;
            if ((sprite.rect.size / sprite.pixelsPerUnit - Vector2.one).sqrMagnitude > Tolerance * Tolerance ||
                (sprite.pivot - sprite.rect.size * .5f).sqrMagnitude > Tolerance * Tolerance) return null;

            if (!collider.enabled || collider.isTrigger || collider.usedByEffector || collider.attachedRigidbody != null ||
                collider.offset != Vector2.zero || collider.compositeOperation != Collider2D.CompositeOperation.None ||
                collider.includeLayers.value != 0 || collider.excludeLayers.value != 0 ||
                collider.forceSendLayers.value != -1 || collider.forceReceiveLayers.value != -1 ||
                collider.contactCaptureLayers.value != -1 || collider.callbackLayers.value != -1 ||
                collider.layerOverridePriority != 0) return null;

            Vector2[] points;
            if (collider is BoxCollider2D box && box.size == Vector2.one && box.edgeRadius == 0 && !box.autoTiling)
                points = Square;
            else if (collider is PolygonCollider2D polygon && polygon.pathCount == 1 && !polygon.autoTiling)
            {
                points = polygon.GetPath(0);
                if (points.Length != 3 || Array.Exists(points, point => Array.IndexOf(Square, point) < 0)) return null;
            }
            else return null;

            return new Entry { source = child, sibling = child.GetSiblingIndex(), halfCell = halfCell, quarterTurns = quarterTurns, renderer = renderer,
                collider = collider, polygon = points };
        }

        static bool TryReadQuarterTurns(Quaternion rotation, out int quarterTurns)
        {
            quarterTurns = 0;
            if (rotation.Equals(Quaternion.identity)) return true;

            var right = rotation * Vector3.right;
            var up = rotation * Vector3.up;
            quarterTurns = Mathf.RoundToInt(Mathf.Atan2(right.y, right.x) / (Mathf.PI * .5f)) & 3;
            // Test both axes to exclude out-of-plane rotations and non-grid-aligned edges.
            return (right - (Vector3)RotateQuarterTurn(Vector2.right, quarterTurns)).sqrMagnitude <= Tolerance * Tolerance &&
                (up - (Vector3)RotateQuarterTurn(Vector2.up, quarterTurns)).sqrMagnitude <= Tolerance * Tolerance;
        }

        static Vector2 RotateQuarterTurn(Vector2 point, int quarterTurns) => quarterTurns switch
        {
            1 => new Vector2(-point.y, point.x),
            2 => -point,
            3 => new Vector2(point.y, -point.x),
            _ => point
        };

        static Matrix4x4 TileTransform(int quarterTurns)
        {
            var right = RotateQuarterTurn(Vector2.right, quarterTurns);
            var up = RotateQuarterTurn(Vector2.up, quarterTurns);
            return new Matrix4x4(new Vector4(right.x, right.y, 0, 0), new Vector4(up.x, up.y, 0, 0),
                new Vector4(0, 0, 1, 0), new Vector4(0, 0, 0, 1));
        }

        static HashSet<Entry> FindReferencedEntries(MapSection source, Dictionary<GameObject, Entry> candidates)
        {
            var referenced = new HashSet<Entry>();
            foreach (var component in source.GetComponentsInChildren<Component>(true))
            {
                if (component == null || component is Transform || candidates.ContainsKey(component.gameObject)) continue;
                using var serialized = new SerializedObject(component);
                var property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference) continue;
                    var value = property.objectReferenceValue;
                    var target = value is Component targetComponent ? targetComponent.gameObject : value as GameObject;
                    if (target != null && candidates.TryGetValue(target, out var entry)) referenced.Add(entry);
                }
            }
            return referenced;
        }

        static bool OverlapsCandidate(Vector2Int position, Dictionary<Vector2Int, int> byPosition)
        {
            if (byPosition[position] > 1) return true;
            for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                    if ((x != 0 || y != 0) && byPosition.ContainsKey(position + new Vector2Int(x, y))) return true;
            return false;
        }

        static void PreserveAmbiguousRendering(MapSection source, List<Entry> entries,
            Dictionary<GameObject, Entry> candidates, HashSet<Entry> preserve)
        {
            var obstacles = new Dictionary<(int, int), List<Bounds>>();
            var unboundedSorts = new HashSet<(int, int)>();
            foreach (var renderer in source.GetComponentsInChildren<Renderer>(true))
            {
                if (candidates.ContainsKey(renderer.gameObject)) continue;
                var sortingGroup = renderer.GetComponentInParent<SortingGroup>(true);
                var key = sortingGroup != null ? (sortingGroup.sortingLayerID, sortingGroup.sortingOrder) :
                    (renderer.sortingLayerID, renderer.sortingOrder);
                var gimmick = renderer.GetComponentInParent<MapGimmick>(true);
                var body = renderer.GetComponentInParent<Rigidbody2D>(true);
                if (gimmick == null && body != null && body.bodyType != RigidbodyType2D.Static)
                {
                    unboundedSorts.Add(key);
                    continue;
                }
                var bounds = renderer.bounds;
                if (gimmick != null)
                {
                    // Include the space occupied by moving parts and alternate frames at the same sort order.
                    float margin = 1f;
                    switch (gimmick.kind)
                    {
                        case MapGimmickKind.Pulley: margin += Mathf.Abs(gimmick.travel); break;
                        case MapGimmickKind.Crane:
                            var platforms = gimmick.GetComponent<RotatingCranePlatforms>();
                            if (platforms != null) margin += 2 * platforms.Radius;
                            break;
                        case MapGimmickKind.Seesaw:
                            if (gimmick.rotatingBeam != null)
                                margin += 2 * (bounds.center - gimmick.rotatingBeam.transform.position).magnitude + bounds.size.magnitude;
                            break;
                        case MapGimmickKind.Sparks: margin += Mathf.Abs(gimmick.beamRange); break;
                    }
                    var scale = gimmick.transform.lossyScale;
                    if (bounds.size == Vector3.zero) bounds.center = renderer.transform.position;
                    bounds.Expand(2 * margin * Mathf.Max(1, Mathf.Abs(scale.x), Mathf.Abs(scale.y)));
                }
                if (bounds.size == Vector3.zero) continue;
                if (!obstacles.TryGetValue(key, out var list)) obstacles.Add(key, list = new List<Bounds>());
                list.Add(bounds);
            }

            foreach (var entry in entries)
            {
                var key = (entry.renderer.sortingLayerID, entry.renderer.sortingOrder);
                if (unboundedSorts.Contains(key))
                {
                    preserve.Add(entry);
                    continue;
                }
                if (!obstacles.TryGetValue(key, out var list)) continue;
                var center = entry.source.position;
                foreach (var obstacle in list)
                {
                    // Compare in XY even at different depths, since the camera can draw them over one another.
                    if (center.x + .5f <= obstacle.min.x + Tolerance || center.x - .5f >= obstacle.max.x - Tolerance ||
                        center.y + .5f <= obstacle.min.y + Tolerance || center.y - .5f >= obstacle.max.y - Tolerance) continue;
                    preserve.Add(entry);
                    break;
                }
            }
        }

        static bool BakeGroup(MapSection exportCopy, List<Entry> entries, Dictionary<Sprite, Tile> tileCache)
        {
            var first = entries[0];
            var tileChanges = new TileChangeData[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var sprite = entry.renderer.sprite;
                if (!tileCache.TryGetValue(sprite, out var tile)) tileCache.Add(sprite, tile = LoadOrCreateTile(sprite));
                if (tile == null) return false;
                tileChanges[i] = new TileChangeData(entry.Cell, tile, Color.white, TileTransform(entry.quarterTurns));
            }

            var scene = exportCopy.gameObject.scene;
            var grid = ObjectFactory.CreateGameObject(scene, HideFlags.None, "Baked Terrain Grid", typeof(Grid));
            // Keep each generated grid distinct when a section needs several rendering groups.
            grid.name += " " + exportCopy.transform.childCount;
            grid.transform.SetParent(exportCopy.transform, false);
            grid.transform.localPosition = new Vector3((first.halfCell.x & 1) * .5f - .5f,
                (first.halfCell.y & 1) * .5f - .5f, 0);
            var terrain = ObjectFactory.CreateGameObject(scene, HideFlags.None, "Terrain Tiles",
                typeof(Tilemap), typeof(TilemapRenderer), typeof(Rigidbody2D), typeof(PolygonCollider2D), typeof(CompositeCollider2D));
            terrain.layer = first.source.gameObject.layer;
            terrain.transform.SetParent(grid.transform, false);

            var tilemap = terrain.GetComponent<Tilemap>();
            tilemap.tileAnchor = new Vector3(.5f, .5f, 0);
            tilemap.SetTiles(tileChanges, ignoreLockFlags: true);
            var renderer = terrain.GetComponent<TilemapRenderer>();
            renderer.mode = TilemapRenderer.Mode.Chunk;
            renderer.sharedMaterial = first.renderer.sharedMaterial;
            renderer.sortingLayerID = first.renderer.sortingLayerID;
            renderer.sortingOrder = first.renderer.sortingOrder;
            renderer.renderingLayerMask = first.renderer.renderingLayerMask;

            var body = terrain.GetComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Static;
            var composite = terrain.GetComponent<CompositeCollider2D>();
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
            composite.generationType = CompositeCollider2D.GenerationType.Manual;
            composite.sharedMaterial = first.collider.sharedMaterial;
            composite.offsetDistance = 0;
            var source = terrain.GetComponent<PolygonCollider2D>();
            source.compositeOperation = Collider2D.CompositeOperation.Merge;

            var paths = BuildCollisionPaths(entries);
            source.pathCount = paths.Count;
            for (int i = 0; i < paths.Count; i++) source.SetPath(i, paths[i]);
            composite.GenerateGeometry();
            if (composite.pathCount != 0) return true;

            Object.DestroyImmediate(grid);
            return false;
        }

        static List<Vector2[]> BuildCollisionPaths(List<Entry> entries)
        {
            // Coalesce contiguous squares in a row before asking the composite to merge shared edges.
            entries.Sort((left, right) => left.Cell.y == right.Cell.y ? left.Cell.x.CompareTo(right.Cell.x) : left.Cell.y.CompareTo(right.Cell.y));
            var paths = new List<Vector2[]>();
            for (int i = 0; i < entries.Count; i++)
            {
                var entry = entries[i];
                var cell = entry.Cell;
                if (entry.polygon == Square)
                {
                    int end = cell.x + 1;
                    while (i + 1 < entries.Count && entries[i + 1].polygon == Square &&
                        entries[i + 1].Cell == new Vector3Int(end, cell.y, 0))
                    {
                        i++;
                        end++;
                    }
                    paths.Add(new[] { new Vector2(cell.x, cell.y), new Vector2(end, cell.y),
                        new Vector2(end, cell.y + 1), new Vector2(cell.x, cell.y + 1) });
                }
                else
                {
                    var center = new Vector2(cell.x + .5f, cell.y + .5f);
                    var path = new Vector2[entry.polygon.Length];
                    for (int point = 0; point < path.Length; point++)
                        path[point] = RotateQuarterTurn(entry.polygon[point], entry.quarterTurns) + center;
                    paths.Add(path);
                }
            }
            return paths;
        }

        static Tile LoadOrCreateTile(Sprite sprite)
        {
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(sprite, out string guid, out long localId)) return null;
            if (!AssetDatabase.IsValidFolder(TileFolder) &&
                string.IsNullOrEmpty(AssetDatabase.CreateFolder(MapEditorInstaller.BasePath, "GeneratedTerrainTiles"))) return null;

            string path = $"{TileFolder}/Terrain-{guid}-{localId}.asset";
            var asset = AssetDatabase.LoadMainAssetAtPath(path);
            if (asset != null)
            {
                return asset is Tile existing && existing.sprite == sprite && existing.color == Color.white &&
                    existing.transform == Matrix4x4.identity && existing.gameObject == null &&
                    existing.colliderType == Tile.ColliderType.None ? existing : null;
            }
            if (File.Exists(path)) return null;

            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            tile.color = Color.white;
            tile.transform = Matrix4x4.identity;
            tile.colliderType = Tile.ColliderType.None;
            AssetDatabase.CreateAsset(tile, path);
            return tile;
        }
    }
}
