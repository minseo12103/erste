using UnityEngine;

namespace ShapeCoop
{
    // The gameplay raycast and timing are owned by MapGimmick; these sprites only visualize its state.
    public sealed class WeldingSparkVisual : MonoBehaviour
    {
        public SpriteRenderer device, arc, impact;
        public Sprite deviceOff, deviceOn;
        public Sprite[] arcFrames, impactFrames;
        [Min(.1f)] public float framesPerSecond = 12f;

        public void Show(bool active, bool hit, Vector3 contactWorld, float phase)
        {
            if (device != null) device.sprite = active ? deviceOn : deviceOff;
            int frame = Mathf.FloorToInt(Mathf.Max(0, phase) * Mathf.Max(.1f, framesPerSecond));
            if (arc != null) arc.sprite = active && arcFrames != null && arcFrames.Length > 0 ? arcFrames[frame % arcFrames.Length] : null;
            if (impact != null)
            {
                impact.sprite = active && hit && impactFrames != null && impactFrames.Length > 0 ? impactFrames[frame % impactFrames.Length] : null;
                impact.transform.position = contactWorld;
                impact.transform.rotation = Quaternion.identity;
            }
        }
    }
}
