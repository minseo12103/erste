using UnityEngine;

namespace ShapeCoop
{
    // References the supplied welding sheet; no separate artwork is generated.
    public sealed class CharacterStunArt : ScriptableObject
    {
        [HideInInspector] public Sprite[] arcs;
        public Sprite[] impacts;
        public Color tint = new Color(.55f, .86f, 1f, 1f);
        [Range(0f, 1f)] public float tintStrength = .3f;
        [HideInInspector] public float framesPerSecond = 12f;
        [Min(.1f)] public float outlinePulseSpeed = 8f;
        [Min(.02f)] public float impactDuration = .3f;
    }
}
