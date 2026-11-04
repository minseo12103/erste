using UnityEngine;

namespace ShapeCoop
{
    // One independently authored piece of the level. Children are the exported content.
    public sealed class MapSection : MonoBehaviour
    {
        public string sectionId;
        public string displayName;
        [HideInInspector] public string prefabPath;
    }

    [System.Serializable]
    public sealed class MapSectionPlacement
    {
        public MapSection prefab;
        public Vector2 position;
    }
}
