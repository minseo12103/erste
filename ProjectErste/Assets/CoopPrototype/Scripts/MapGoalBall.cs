using UnityEngine;

namespace ShapeCoop
{
    // Identifies the actual goal ball and restores its normal material after leaving oil.
    [RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
    public sealed class MapGoalBall : MonoBehaviour
    {
        Collider2D shape;
        PhysicsMaterial2D original;
        float oilUntil;
        void Awake() { shape = GetComponent<Collider2D>(); original = shape.sharedMaterial; }
        public void SetNormalMaterial(PhysicsMaterial2D material) { original = material; if (shape != null) shape.sharedMaterial = material; }
        public void TouchOil(PhysicsMaterial2D material) { oilUntil = Time.fixedTime + Time.fixedDeltaTime * 2.5f; if (shape != null) shape.sharedMaterial = material; }
        public void ResetEffects() { oilUntil = 0; if (shape != null) shape.sharedMaterial = original; }
        void FixedUpdate() { if (oilUntil > 0 && Time.fixedTime > oilUntil) ResetEffects(); }
    }
}
