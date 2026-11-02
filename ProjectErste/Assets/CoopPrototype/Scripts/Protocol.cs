using System;

namespace ShapeCoop
{
    [Serializable] public sealed class Packet
    {
        public string kind;
        public int epoch, sequence;
        public float move;
        public int jump, dash, freeze;
        public double elapsed;
        public bool completed;
        public PlayerData[] players;
        public BodyData ball;
    }
    [Serializable] public sealed class PlayerData
    {
        public string id, name;
        public int shape, color;
        public BodyData body;
    }
    [Serializable] public sealed class BodyData
    {
        public float x, y, angle;
        public bool frozen;
    }
}
