using UnityEngine;

namespace ShapeCoop
{
    public enum MapConnectorKind { Entrance, Exit }
    public sealed class MapConnector : MonoBehaviour
    {
        public MapConnectorKind kind;
#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            Gizmos.color = kind == MapConnectorKind.Entrance ? Color.cyan : Color.magenta;
            Gizmos.DrawWireCube(transform.position, new Vector3(.6f, .6f, .01f));
            UnityEditor.Handles.Label(transform.position + Vector3.up * .4f, kind == MapConnectorKind.Entrance ? "구간 입구" : "구간 출구");
        }
#endif
    }
}
