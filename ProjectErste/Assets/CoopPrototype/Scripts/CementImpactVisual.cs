using UnityEngine;

namespace ShapeCoop
{
    // Artwork only: host collision decisions are made by MapGimmick.
    public sealed class CementImpactVisual : MonoBehaviour
    {
        public SpriteRenderer pile, dust;
        [Tooltip("Two pile frames followed by six impact dust frames.")]
        public Sprite[] frames;
        [Min(.02f)] public float frameSeconds = .09f;
        int sequence;
        float started;
        bool hasImpact;
        Vector2 contact;

        public void ResetAnimation()
        {
            sequence++; hasImpact = false; contact = Vector2.zero; RenderFrame();
        }
        public void Impact(Vector2 point)
        {
            sequence++; contact = point; started = Time.time; hasImpact = true; RenderFrame();
        }
        public void WriteState(GimmickData state)
        {
            state.cementSequence = sequence;
            state.cementAge = hasImpact ? Mathf.Max(0, Time.time - started) : -1;
            state.cementX = contact.x; state.cementY = contact.y;
        }
        public void ApplyState(GimmickData state)
        {
            if (state.cementSequence <= 0 || state.cementAge < 0)
            {
                sequence = state.cementSequence; hasImpact = false;
            }
            else if (sequence != state.cementSequence)
            {
                sequence = state.cementSequence; contact = new Vector2(state.cementX, state.cementY);
                started = Time.time - state.cementAge; hasImpact = true;
            }
            RenderFrame();
        }
        void Update() { RenderFrame(); }
        void RenderFrame()
        {
            if (pile == null || dust == null || frames == null || frames.Length < 8) return;
            float step = Mathf.Max(.02f, frameSeconds);
            float age = hasImpact ? Mathf.Max(0, Time.time - started) : float.PositiveInfinity;
            int dustFrame = age < step * 6 ? Mathf.FloorToInt(age / step) + 2 : -1;
            pile.sprite = age < step * 1.5f ? frames[1] : frames[0];
            dust.sprite = dustFrame >= 0 ? frames[dustFrame] : null;
            // Dust frames end 59 pixels below their center; align that edge with contact.
            dust.transform.localPosition = new Vector3(contact.x, contact.y + 59f / 80f * .615f, 0);
        }
    }
}
