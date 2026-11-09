using System;
using UnityEngine;

namespace ShapeCoop
{
    // A visual-only indicator for nitrogen's authoritative forced-freeze state.
    public sealed class CharacterIceVisual : MonoBehaviour
    {
        const string Sheet = "ShapeCoop/Effects/NitrogenGeyser_Sheet";
        static Sprite ice;
        PrototypePlayer player;
        SpriteRenderer image;

        public void Initialize(PrototypePlayer owner)
        {
            player = owner;
            if (ice == null) ice = Array.Find(Resources.LoadAll<Sprite>(Sheet), sprite => sprite.name == "Nitrogen_Ice");
            if (ice == null) return;
            var obj = new GameObject("Nitrogen ice (visual only)"); obj.transform.SetParent(transform, false);
            image = obj.AddComponent<SpriteRenderer>(); image.sprite = ice; image.sortingOrder = 0; image.color = new Color(1, 1, 1, .85f); image.enabled = false;
            var size = ice.bounds.size; obj.transform.localScale = new Vector3(1.35f / size.x, 1.35f / size.y, 1);
        }
        public void Clear() { if (image != null) image.enabled = false; }
        void LateUpdate() { if (image != null) image.enabled = player != null && player.forcedFrozen; }
    }
}
