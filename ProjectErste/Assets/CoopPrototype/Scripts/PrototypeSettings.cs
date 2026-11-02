using UnityEngine;

namespace ShapeCoop
{
    public sealed class PrototypeSettings : MonoBehaviour
    {
        [Header("Player physics")]
        public float moveSpeed = 5f;
        public float moveAcceleration = 35f;
        public float jumpImpulse = 7f;
        public float dashImpulse = 6f;
        public float dashCooldown = 0.65f;
        public float playerMass = 1f;
        public float ballMass = 1.2f;
        public float gravityScale = 2f;
        [Header("Network")]
        [Range(5, 30)] public int snapshotsPerSecond = 20;
        public float peerTimeout = 12f;
        public float interpolationSpeed = 15f;
    }
}
