using System;
using UnityEngine;

namespace ShapeCoop
{
    public static class MapSectionAssembler
    {
        public static bool Build(MapSectionPlacement[] placements, Transform parent, Action<Collider2D> onGoal, out Vector2? playerStart, out Vector2? ballStart)
        {
            playerStart = null; ballStart = null; bool any = false;
            if (placements == null) return false;
            foreach (var placement in placements)
            {
                if (placement == null || placement.prefab == null) continue;
                any = true;
                var section = UnityEngine.Object.Instantiate(placement.prefab, parent);
                section.transform.localPosition = placement.position;
                foreach (var marker in section.GetComponentsInChildren<MapMarker>(true))
                {
                    if (!playerStart.HasValue && marker.kind == MapMarkerKind.PlayerStart) playerStart = marker.transform.position;
                    if (!ballStart.HasValue && marker.kind == MapMarkerKind.BallStart) ballStart = marker.transform.position;
                }
                if (onGoal != null) foreach (var goal in section.GetComponentsInChildren<MapGoalArea>(true)) goal.Entered += onGoal;
            }
            return any;
        }
    }
}
