using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ShapeCoop.Editor
{
    internal sealed class MapBrushStroke
    {
        const string PaintUndoName = "Paint map objects";
        const string EraseUndoName = "Erase map objects";
        const int MaxCellsPerSegment = 256;

        readonly MapSection section;
        readonly GameObject prefab;
        readonly Vector3 origin;
        readonly float spacing;
        readonly float rotation;
        readonly HashSet<Vector2Int> occupied = new HashSet<Vector2Int>();
        readonly Dictionary<Vector2Int, List<GameObject>> eraseTargets;
        Vector2Int? lastCell;
        int undoGroup = -1;

        internal GameObject LastPlaced { get; private set; }
        internal bool SkippedLongSegment { get; private set; }

        internal MapBrushStroke(MapSection section, GameObject prefab, float spacing, float rotation)
        {
            this.section = section;
            this.prefab = prefab;
            this.spacing = ValidSpacing(spacing);
            this.rotation = rotation;
            origin = section.transform.position;

            foreach (Transform child in section.transform)
            {
                var position = child.position;
                if (Mathf.Abs(position.z) > .0001f) continue;
                if (child.GetComponentInChildren<Renderer>(true) != null || child.GetComponentInChildren<Collider2D>(true) != null)
                    occupied.Add(CellAt(origin, this.spacing, position));
            }
        }

        internal MapBrushStroke(MapSection section, float spacing)
        {
            this.section = section;
            this.spacing = ValidSpacing(spacing);
            origin = section.transform.position;
            eraseTargets = new Dictionary<Vector2Int, List<GameObject>>();
            foreach (Transform child in section.transform)
            {
                var instance = child.gameObject;
                if (Mathf.Abs(child.position.z) > .0001f || !PrefabUtility.IsAnyPrefabInstanceRoot(instance)) continue;
                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance);
                if (!MapEditorWindow.IsBasicPalettePath(path)) continue;

                var cell = CellAt(origin, this.spacing, child.position);
                if (!eraseTargets.TryGetValue(cell, out var targets))
                {
                    targets = new List<GameObject>();
                    eraseTargets.Add(cell, targets);
                }
                targets.Add(instance);
            }
        }

        internal static float ValidSpacing(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? 1f : Mathf.Max(.05f, value);
        }

        static Vector2Int CellAt(Vector3 origin, float spacing, Vector3 point)
        {
            return new Vector2Int(Mathf.RoundToInt((point.x - origin.x) / spacing), Mathf.RoundToInt((point.y - origin.y) / spacing));
        }

        static Vector3 CellPosition(Vector3 origin, float spacing, Vector2Int cell)
        {
            return new Vector3(origin.x + cell.x * spacing, origin.y + cell.y * spacing, 0);
        }

        internal static Vector3 SnapToCell(Vector3 origin, float spacing, Vector3 point)
        {
            return CellPosition(origin, spacing, CellAt(origin, spacing, point));
        }

        // Fill the grid cells between mouse events, including across fast diagonal drags.
        internal static IEnumerable<Vector2Int> CellsBetween(Vector2Int from, Vector2Int to)
        {
            long dx = Math.Abs((long)to.x - from.x);
            long dy = -Math.Abs((long)to.y - from.y);
            int stepX = from.x < to.x ? 1 : -1;
            int stepY = from.y < to.y ? 1 : -1;
            long error = dx + dy;

            while (true)
            {
                yield return from;
                if (from == to) yield break;
                long nextError = 2 * error;
                if (nextError >= dy) { error += dy; from.x += stepX; }
                if (nextError <= dx) { error += dx; from.y += stepY; }
            }
        }

        internal bool AddPoint(Vector3 worldPosition)
        {
            var cell = CellAt(origin, spacing, worldPosition);
            SkippedLongSegment = false;
            if (lastCell == cell) return false;
            if (lastCell.HasValue)
            {
                long columns = Math.Abs((long)cell.x - lastCell.Value.x);
                long rows = Math.Abs((long)cell.y - lastCell.Value.y);
                if (Math.Max(columns, rows) >= MaxCellsPerSegment)
                {
                    SkippedLongSegment = true;
                    return false;
                }
            }
            bool changed = false;
            foreach (var next in CellsBetween(lastCell ?? cell, cell))
            {
                if (eraseTargets != null)
                {
                    if (!eraseTargets.TryGetValue(next, out var targets)) continue;
                    eraseTargets.Remove(next);
                    foreach (var target in targets)
                    {
                        if (target == null || target.transform.parent != section.transform) continue;
                        BeginUndo(EraseUndoName);
                        Undo.DestroyObjectImmediate(target);
                        changed = true;
                    }
                    continue;
                }
                if (occupied.Contains(next)) continue;
                BeginUndo(PaintUndoName);
                var instance = MapEditorWindow.InstantiatePlacedPrefab(section, prefab, CellPosition(origin, spacing, next), rotation, PaintUndoName);
                if (instance == null) continue;
                occupied.Add(next);
                LastPlaced = instance;
                changed = true;
            }

            lastCell = cell;
            if (changed) EditorSceneManager.MarkSceneDirty(section.gameObject.scene);
            return changed;
        }

        void BeginUndo(string name)
        {
            if (undoGroup >= 0) return;
            Undo.IncrementCurrentGroup();
            undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(name);
        }

        internal void Finish()
        {
            if (undoGroup < 0) return;
            Undo.CollapseUndoOperations(undoGroup);
            Undo.IncrementCurrentGroup();
            undoGroup = -1;
        }
    }
}
