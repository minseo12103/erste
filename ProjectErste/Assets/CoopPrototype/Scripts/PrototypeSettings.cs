using UnityEngine;
using UnityEngine.Serialization;

namespace ShapeCoop
{
    public sealed class PrototypeSettings : MonoBehaviour
    {
        [Header("Player physics")]
        public float moveSpeed = 5f;
        public float moveAcceleration = 35f;
        public float jumpImpulse = 7f;
        [Range(1f, 1.5f)] public float maxJumpMultiplier = 1.5f;
        [Min(0.02f)] public float jumpHoldTime = .3f;
        [FormerlySerializedAs("dashImpulse"), Min(0f)] public float dashSpeed = 16f;
        [Min(0f)]
        public float dashDuration = .105f;
        [Range(0f, 1f)] public float dashStopSpeedRatio = .15f;
        public float dashCooldown = 0.65f;
        public float playerMass = 1f;
        public float ballMass = 1.2f;
        [Range(0f, 1f)] public float ballBounciness = .75f;
        public float gravityScale = 2f;
        [Header("Network")]
        [Range(5, 30)] public int snapshotsPerSecond = 20;
        public float peerTimeout = 12f;
        public float interpolationSpeed = 15f;
    }
}
