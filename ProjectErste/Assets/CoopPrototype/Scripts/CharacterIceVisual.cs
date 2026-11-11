using System;
using UnityEngine;

namespace ShapeCoop
{
    // A visual-only indicator for nitrogen's authoritative forced-freeze state.
    public sealed class CharacterIceVisual : MonoBehaviour
    {
        const string IceSprite = "ShapeCoop/Effects/CharacterIceBlock";
        const string FallbackSheet = "ShapeCoop/Effects/NitrogenGeyser_Sheet";
        // 1.35 local units at the current 1.2-unit triangle size.
        const float IceWorldSize = 1.62f;
        static Sprite ice;
        PrototypePlayer player;
        SpriteRenderer image;

        public void Initialize(PrototypePlayer owner)
        {
            player = owner;
            if (ice == null) ice = Array.Find(Resources.LoadAll<Sprite>(IceSprite), sprite => sprite.name == "CharacterIceBlock");
            if (ice == null) ice = Array.Find(Resources.LoadAll<Sprite>(FallbackSheet), sprite => sprite.name == "Nitrogen_Ice");
            if (ice == null) return;
            var obj = new GameObject("Nitrogen ice (visual only)"); obj.transform.SetParent(transform, false);
            image = obj.AddComponent<SpriteRenderer>(); image.sprite = ice; image.sortingOrder = 0; image.color = Color.white; image.enabled = false;
            RefreshSize();
        }
        public void RefreshSize()
        {
            if (image == null || image.sprite == null) return;
            var size = image.sprite.bounds.size; var parentScale = image.transform.parent.lossyScale;
            if (size.x <= 0 || size.y <= 0) return;
            image.transform.localScale = new Vector3(IceWorldSize / (size.x * Mathf.Max(.0001f, Mathf.Abs(parentScale.x))), IceWorldSize / (size.y * Mathf.Max(.0001f, Mathf.Abs(parentScale.y))), 1);
        }
        public void Clear() { if (image != null) image.enabled = false; }
        void LateUpdate() { if (image != null) { RefreshSize(); image.enabled = player != null && player.forcedFrozen; } }
    }
}
