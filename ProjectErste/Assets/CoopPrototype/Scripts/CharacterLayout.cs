using System;
using UnityEngine;

namespace ShapeCoop
{
    [Serializable]
    public struct CharacterShapeLayout
    {
        [Range(.35f, 2f)] public float size;
        public Vector2 helmetOffset;
        [Range(.35f, 2f)] public float helmetSize;
        public static CharacterShapeLayout Default => new CharacterShapeLayout { size = 1f, helmetOffset = Vector2.zero, helmetSize = 1f };
        public CharacterShapeLayout Safe()
        {
            var result = this;
            if (float.IsNaN(result.size) || float.IsInfinity(result.size) || result.size <= 0) result.size = 1;
            result.size = Mathf.Clamp(result.size, .35f, 2f);
            // Old assets and packets do not contain this field; preserve their original appearance.
            if (float.IsNaN(result.helmetSize) || float.IsInfinity(result.helmetSize) || result.helmetSize <= 0) result.helmetSize = 1f;
            result.helmetSize = Mathf.Clamp(result.helmetSize, .35f, 2f);
            if (float.IsNaN(result.helmetOffset.x) || float.IsInfinity(result.helmetOffset.x)) result.helmetOffset.x = 0;
            if (float.IsNaN(result.helmetOffset.y) || float.IsInfinity(result.helmetOffset.y)) result.helmetOffset.y = 0;
            result.helmetOffset.x = Mathf.Clamp(result.helmetOffset.x, -.6f, .6f);
            result.helmetOffset.y = Mathf.Clamp(result.helmetOffset.y, -.6f, .6f);
            return result;
        }
    }
    public sealed class CharacterLayout : ScriptableObject
    {
        public CharacterShapeLayout triangle = CharacterShapeLayout.Default;
        public CharacterShapeLayout square = CharacterShapeLayout.Default;
        public CharacterShapeLayout circle = CharacterShapeLayout.Default;
        public CharacterShapeLayout ForShape(int shape) => (shape == 0 ? triangle : shape == 1 ? square : circle).Safe();
        public void SetShape(int shape, CharacterShapeLayout value)
        {
            if (shape == 0) triangle = value.Safe(); else if (shape == 1) square = value.Safe(); else circle = value.Safe();
        }
        public static CharacterShapeLayout Resolve(PlayerData data)
        {
            if (data != null && data.hasCharacterLayout)
                return new CharacterShapeLayout { size = data.characterSize, helmetOffset = new Vector2(data.helmetOffsetX, data.helmetOffsetY), helmetSize = data.helmetSize }.Safe();
            var layout = Resources.Load<CharacterLayout>("ShapeCoop/CharacterLayout");
            return layout != null && data != null ? layout.ForShape(data.shape) : CharacterShapeLayout.Default;
        }
    }
}
