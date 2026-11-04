using UnityEngine;

namespace ShapeCoop
{
    public enum MapMarkerKind { TestStart, PlayerStart, BallStart }

    public sealed class MapMarker : MonoBehaviour
    {
        public MapMarkerKind kind;
#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            Gizmos.color = kind == MapMarkerKind.TestStart ? Color.cyan : kind == MapMarkerKind.PlayerStart ? new Color(.3f,.8f,1f) : new Color(1f,.8f,.1f);
            Gizmos.DrawWireSphere(transform.position, .5f);
            Gizmos.DrawLine(transform.position + Vector3.left * .7f, transform.position + Vector3.right * .7f);
            Gizmos.DrawLine(transform.position + Vector3.up * .7f, transform.position + Vector3.down * .7f);
            UnityEditor.Handles.Label(transform.position + Vector3.up * .75f, kind.ToString());
        }
#endif
    }
}
