using System;

namespace ShapeCoop
{
    [Serializable] public sealed class Packet
    {
        public string kind;
        public int epoch, sequence;
        public float move;
        public int jump, dash, freeze;
        public bool jumpHeld;
        public int emoteSequence, emoteRequested;
        public bool emoteHeld;
        public float emoteX, emoteY;
        public double elapsed;
        public bool completed;
        public PlayerData[] players;
        public BodyData ball;
        public GimmickData[] gimmicks;
    }
    [Serializable] public sealed class PlayerData
    {
        public string id, name;
        public int shape, color;
        public bool hasCharacterLayout;
        public float characterSize, helmetOffsetX, helmetOffsetY, helmetSize;
        public BodyData body;
    }
    [Serializable] public sealed class BodyData
    {
        public float x, y, angle;
        public bool frozen;
        public bool stunned, forcedFrozen;
        public float velocityX, velocityY, angularVelocity;
        public bool idleEyes;
        public int emoteType, emoteSequence;
        public float emoteX, emoteY, emoteElapsed;
    }
    [Serializable] public sealed class GimmickData
    {
        public int index;
        public bool active, broken, shielded, sparkHit;
        public float progress, offset, angle, beamLength, shieldX, shieldY, shieldHalfWidth, shieldHeight;
        public float nitrogenActiveSeconds;
        public int cementSequence;
        public float cementAge, cementX, cementY;
        public bool hasCraneDimensions;
        public float craneRadius, craneWidth, craneHeight, craneSpeed;
    }
}
