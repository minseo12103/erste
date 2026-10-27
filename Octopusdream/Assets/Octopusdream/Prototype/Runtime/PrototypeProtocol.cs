using System;
using UnityEngine;

namespace Octopusdream
{
    public enum RoundPhase { Lobby, Hiding, Hunting, Finished }

    [Serializable]
    public sealed class PrototypePlayerState
    {
        public ulong id;
        public string name;
        public Vector3 position;
        public float yaw, pitch;
        public bool diver, volunteer, caught, editing, bot;
        public float softness = 5f;
        public float hiddenScore, revealed, blinded, scanCooldown, shotCooldown;
        public bool inkUsed;
    }

    [Serializable]
    public sealed class PrototypeWorldState
    {
        public int round, phase, hunters, sequence;
        public float remaining, hideDuration, huntDuration;
        public string result;
        public PrototypePlayerState[] players;
    }

    [Serializable]
    public sealed class PrototypeMessage
    {
        public int protocol = 1;
        public string kind;
        public int round;
        public int sequence;
        public ulong id;
        public Vector3 vector, vector2;
        public Color color;
        public float value, yaw, pitch;
        public bool flag;
        public string text;
        public PrototypeWorldState world;
    }

    public sealed class PrototypePlayer : MonoBehaviour
    {
        public PrototypePlayerState State;
        public CharacterController Controller;
        public PrototypeAppearance Appearance;
        public Vector3 InputMove, TargetPosition;
        public float InputAt, CommandAt, PaintAt, SoftnessAt;
        public float MessageWindowStart;
        public int MessagesInWindow;
        public int LastInputSequence = -1;
        public Vector3 LastVisualPosition;
        public bool Initialized;
        public Vector3 Eye => State.diver ? transform.position + Vector3.up * 1.4f :
            Appearance != null ? Appearance.FocusPoint : transform.position + Vector3.up * .8f;
        public Vector3 Forward => Quaternion.Euler(State.pitch, State.yaw, 0f) * Vector3.forward;

        public void Initialize(PrototypePlayerState state)
        {
            State = state;
            gameObject.name = "Player " + state.name;
            gameObject.layer = 2; // Explicit combat queries include this layer; environment queries exclude it.
            Controller = gameObject.AddComponent<CharacterController>();
            Controller.height = 1.6f;
            Controller.radius = .5f;
            Controller.center = Vector3.up * .8f;
            Controller.stepOffset = .25f;
            Controller.skinWidth = .045f;
            var visual = new GameObject("Replaceable character visual");
            visual.transform.SetParent(transform, false);
            visual.transform.localPosition = Vector3.up * .8f;
            Appearance = visual.AddComponent<PrototypeAppearance>();
            Appearance.Initialize(state.diver);
            Appearance.SetSoftness(state.softness);
            SetLayer(visual.transform);
            Teleport(state.position);
            LastVisualPosition = state.position;
            Initialized = true;
        }

        static void SetLayer(Transform target)
        {
            target.gameObject.layer = 2;
            foreach (Transform child in target) SetLayer(child);
        }

        public void Apply(PrototypePlayerState state, bool instant)
        {
            bool changeRole = State.diver != state.diver;
            State = state;
            TargetPosition = state.position;
            if (changeRole) Appearance.SetRole(state.diver);
            Appearance.SetSoftness(state.softness);
            SetLayer(Appearance.transform);
            Controller.enabled = !state.caught;
            if (instant) Teleport(state.position);
        }

        public void Teleport(Vector3 position)
        {
            bool wasEnabled = Controller.enabled;
            Controller.enabled = false;
            transform.position = position;
            TargetPosition = position;
            Controller.enabled = wasEnabled;
        }

        public void Move(Vector3 velocity, float dt)
        {
            if (Controller.enabled) Controller.Move(velocity * dt);
            State.position = transform.position;
        }

        void OnControllerColliderHit(ControllerColliderHit hit)
        {
            if (Appearance != null) Appearance.Impact(hit.normal, Mathf.Min(hit.moveLength / Mathf.Max(.001f, Time.deltaTime), 12f));
        }
    }
}
