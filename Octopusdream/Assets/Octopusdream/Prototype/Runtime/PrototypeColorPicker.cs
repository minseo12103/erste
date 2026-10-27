using System;
using UnityEngine;

namespace Octopusdream
{
    /// <summary>A compact, classic hue/saturation picker with lightness and exact RGB entry.</summary>
    public sealed class PrototypeColorPicker : IDisposable
    {
        const string FieldPrefix = "OctopusPaintRgb";
        static readonly string[] ChannelNames = { "빨강 (R)", "초록 (G)", "파랑 (B)" };
        readonly string[] channelText = new string[3];
        Texture2D hueSaturationTexture, lightnessTexture;
        GUIStyle numberStyle, stepStyle;
        Color color;
        float hue, saturation, lightness;
        float gradientHue = -1f, gradientSaturation = -1f;
        bool initialized, clearFocusRequested;

        public bool HasTextFocus { get; private set; }

        public Color Draw(Rect rect, Color selected, GUIStyle labelStyle)
        {
            EnsureTextures();
            EnsureStyles();
            if (!initialized || !SameRgb(selected, color)) SetRgb(selected);
            if (clearFocusRequested)
            {
                GUI.FocusControl(null);
                clearFocusRequested = false;
            }

            Rect field = new Rect(rect.x, rect.y + 21f, 211f, 139f);
            Rect brightness = new Rect(rect.x + 226f, field.y, 18f, field.height);
            Rect[] inputs = {
                new Rect(rect.x + 66f, rect.y + 191f, 76f, 23f),
                new Rect(rect.x + 66f, rect.y + 216f, 76f, 23f),
                new Rect(rect.x + 66f, rect.y + 241f, 76f, 23f)
            };
            Event current = Event.current;
            bool finishEntry = HasTextFocus && current.type == EventType.KeyDown &&
                (current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter || current.keyCode == KeyCode.Escape);
            if (current.type == EventType.MouseDown && current.button == 0 &&
                !inputs[0].Contains(current.mousePosition) && !inputs[1].Contains(current.mousePosition) && !inputs[2].Contains(current.mousePosition))
            {
                ReleaseTextFocus();
                GUI.FocusControl(null);
                clearFocusRequested = false;
            }

            GUI.Label(new Rect(rect.x, rect.y, 210f, 19f), "색상 / 채도", labelStyle);
            GUI.Label(new Rect(rect.x + 224f, rect.y, 36f, 19f), "밝기", labelStyle);
            DrawBorder(field);
            DrawBorder(brightness);
            GUI.DrawTexture(field, hueSaturationTexture, ScaleMode.StretchToFill, false);

            Vector2 hs = new Vector2(hue, 1f - saturation);
            if (DragArea(field, "OctopusPaintHS".GetHashCode(), ref hs))
            {
                hue = hs.x;
                saturation = 1f - hs.y;
                SetHsl();
            }
            Vector2 level = new Vector2(0f, 1f - lightness);
            if (DragArea(brightness, "OctopusPaintLightness".GetHashCode(), ref level))
            {
                lightness = 1f - level.y;
                SetHsl();
            }
            UpdateLightnessTexture();
            GUI.DrawTexture(brightness, lightnessTexture, ScaleMode.StretchToFill, false);
            DrawCrosshair(new Vector2(field.x + hue * field.width, field.y + (1f - saturation) * field.height));
            float brightnessY = brightness.y + (1f - lightness) * brightness.height;
            Fill(new Rect(brightness.x - 2f, brightnessY - 2f, brightness.width + 4f, 4f), Color.black);
            Fill(new Rect(brightness.x - 2f, brightnessY - 1f, brightness.width + 4f, 2f), Color.white);
            GUI.Label(new Rect(brightness.xMax + 2f, brightnessY - 10f, 13f, 22f), "◀", labelStyle);

            GUI.Label(new Rect(rect.x, rect.y + 167f, 195f, 20f), "색 모델: RGB   (0–255)", labelStyle);
            Rect preview = new Rect(rect.x + 216f, rect.y + 191f, 44f, 73f);
            DrawBorder(preview);
            Fill(preview, color);
            GUI.Label(new Rect(rect.x + 213f, rect.y + 167f, 48f, 20f), "선택 색", labelStyle);
            for (int channel = 0; channel < 3; channel++) DrawChannel(rect.x, inputs[channel], channel, labelStyle);

            string focused = GUI.GetNameOfFocusedControl();
            HasTextFocus = focused != null && focused.StartsWith(FieldPrefix, StringComparison.Ordinal);
            if (finishEntry)
            {
                ReleaseTextFocus();
                GUI.FocusControl(null);
                clearFocusRequested = false;
                if (current.type != EventType.Used) current.Use();
            }
            return color;
        }

        void DrawChannel(float x, Rect input, int channel, GUIStyle labelStyle)
        {
            GUI.Label(new Rect(x, input.y + 1f, 65f, 23f), ChannelNames[channel], labelStyle);
            string controlName = FieldPrefix + channel;
            GUI.SetNextControlName(controlName);
            string entered = GUI.TextField(input, channelText[channel], 3, numberStyle);
            if (entered != channelText[channel])
            {
                // Empty text is permitted while replacing a value; blur restores its numeric value.
                if (entered.Length == 0) channelText[channel] = "";
                else if (int.TryParse(entered, out int number))
                {
                    number = Mathf.Clamp(number, 0, 255);
                    Color next = color;
                    next[channel] = number / 255f;
                    SetRgb(next);
                }
            }
            if (GUI.Button(new Rect(x + 147f, input.y, 23f, 23f), "−", stepStyle)) ChangeChannel(channel, -1);
            if (GUI.Button(new Rect(x + 174f, input.y, 23f, 23f), "+", stepStyle)) ChangeChannel(channel, 1);
            if (GUI.GetNameOfFocusedControl() != controlName && channelText[channel].Length == 0)
                channelText[channel] = Mathf.RoundToInt(color[channel] * 255f).ToString();
        }

        void ChangeChannel(int channel, int delta)
        {
            Color next = color;
            next[channel] = Mathf.Clamp(Mathf.RoundToInt(color[channel] * 255f) + delta, 0, 255) / 255f;
            SetRgb(next);
        }

        void SetRgb(Color selected)
        {
            selected = new Color(Mathf.Clamp01(selected.r), Mathf.Clamp01(selected.g), Mathf.Clamp01(selected.b), 1f);
            Vector3 hsl = ToHsl(selected);
            // Keep the chosen hue when entering a gray; increasing saturation should restore it.
            if (!initialized || hsl.y > .00001f) hue = hsl.x;
            saturation = hsl.y;
            lightness = hsl.z;
            color = selected;
            initialized = true;
            SynchronizeText();
        }

        void SetHsl()
        {
            color = FromHsl(hue, saturation, lightness);
            SynchronizeText();
        }

        void SynchronizeText()
        {
            for (int i = 0; i < 3; i++) channelText[i] = Mathf.RoundToInt(color[i] * 255f).ToString();
        }

        static bool SameRgb(Color a, Color b) => Mathf.Abs(a.r - b.r) < .000001f && Mathf.Abs(a.g - b.g) < .000001f && Mathf.Abs(a.b - b.b) < .000001f;

        static bool DragArea(Rect bounds, int hint, ref Vector2 normalized)
        {
            int control = GUIUtility.GetControlID(hint, FocusType.Passive, bounds);
            Event current = Event.current;
            EventType type = current.GetTypeForControl(control);
            bool changed = false;
            if (type == EventType.MouseDown && current.button == 0 && bounds.Contains(current.mousePosition))
            {
                GUIUtility.hotControl = control;
                GUI.FocusControl(null);
                changed = true;
            }
            else if (type == EventType.MouseDrag && GUIUtility.hotControl == control) changed = true;
            else if (type == EventType.MouseUp && GUIUtility.hotControl == control)
            {
                GUIUtility.hotControl = 0;
                current.Use();
            }
            if (changed)
            {
                normalized = new Vector2(Mathf.Clamp01((current.mousePosition.x - bounds.x) / bounds.width),
                    Mathf.Clamp01((current.mousePosition.y - bounds.y) / bounds.height));
                current.Use();
                GUI.changed = true;
            }
            return changed;
        }

        void EnsureTextures()
        {
            if (hueSaturationTexture != null) return;
            hueSaturationTexture = CreateTexture(256, 128, "Paint hue and saturation");
            var pixels = new Color32[256 * 128];
            for (int y = 0; y < 128; y++)
                for (int x = 0; x < 256; x++)
                    pixels[y * 256 + x] = FromHsl(x / 255f, y / 127f, .5f);
            hueSaturationTexture.SetPixels32(pixels);
            hueSaturationTexture.Apply(false, true);
            lightnessTexture = CreateTexture(1, 128, "Paint brightness");
        }

        static Texture2D CreateTexture(int width, int height, string name) => new Texture2D(width, height, TextureFormat.RGBA32, false, false)
        {
            name = name,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            hideFlags = HideFlags.HideAndDontSave
        };

        void UpdateLightnessTexture()
        {
            if (Mathf.Abs(hue - gradientHue) < .0001f && Mathf.Abs(saturation - gradientSaturation) < .0001f) return;
            var pixels = new Color32[128];
            for (int y = 0; y < pixels.Length; y++) pixels[y] = FromHsl(hue, saturation, y / 127f);
            lightnessTexture.SetPixels32(pixels);
            lightnessTexture.Apply(false, false);
            gradientHue = hue;
            gradientSaturation = saturation;
        }

        void EnsureStyles()
        {
            if (numberStyle != null) return;
            numberStyle = new GUIStyle(GUI.skin.textField) { fontSize = 14, alignment = TextAnchor.MiddleCenter };
            stepStyle = new GUIStyle(GUI.skin.button) { fontSize = 16, alignment = TextAnchor.MiddleCenter, padding = new RectOffset(0, 0, 0, 0) };
        }

        static void DrawCrosshair(Vector2 position)
        {
            Fill(new Rect(position.x - 7f, position.y - 2f, 14f, 4f), Color.black);
            Fill(new Rect(position.x - 2f, position.y - 7f, 4f, 14f), Color.black);
            Fill(new Rect(position.x - 6f, position.y - 1f, 12f, 2f), Color.white);
            Fill(new Rect(position.x - 1f, position.y - 6f, 2f, 12f), Color.white);
        }

        static void DrawBorder(Rect rect) => Fill(new Rect(rect.x - 1f, rect.y - 1f, rect.width + 2f, rect.height + 2f), new Color(.5f, .65f, .68f));
        static void Fill(Rect rect, Color tint)
        {
            Color previous = GUI.color;
            GUI.color = tint;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        public void ReleaseTextFocus()
        {
            // Can be called by Update/ToggleEdit; GUI calls belong to the next OnGUI.
            clearFocusRequested = true;
            HasTextFocus = false;
            if (initialized) SynchronizeText();
        }

        public void Dispose()
        {
            if (hueSaturationTexture != null) UnityEngine.Object.Destroy(hueSaturationTexture);
            if (lightnessTexture != null) UnityEngine.Object.Destroy(lightnessTexture);
            hueSaturationTexture = lightnessTexture = null;
            HasTextFocus = false;
        }

        public static Color FromHsl(float h, float s, float l)
        {
            h = Mathf.Repeat(h, 1f);
            s = Mathf.Clamp01(s);
            l = Mathf.Clamp01(l);
            float chroma = (1f - Mathf.Abs(2f * l - 1f)) * s;
            float segment = h * 6f;
            float secondary = chroma * (1f - Mathf.Abs(segment % 2f - 1f));
            Color rgb = segment < 1f ? new Color(chroma, secondary, 0f) :
                segment < 2f ? new Color(secondary, chroma, 0f) :
                segment < 3f ? new Color(0f, chroma, secondary) :
                segment < 4f ? new Color(0f, secondary, chroma) :
                segment < 5f ? new Color(secondary, 0f, chroma) : new Color(chroma, 0f, secondary);
            float offset = l - chroma * .5f;
            return new Color(rgb.r + offset, rgb.g + offset, rgb.b + offset, 1f);
        }

        public static Vector3 ToHsl(Color rgb)
        {
            float max = Mathf.Max(rgb.r, Mathf.Max(rgb.g, rgb.b));
            float min = Mathf.Min(rgb.r, Mathf.Min(rgb.g, rgb.b));
            float difference = max - min;
            float l = (max + min) * .5f;
            if (difference < .000001f) return new Vector3(0f, 0f, l);
            float s = difference / Mathf.Max(.000001f, 1f - Mathf.Abs(2f * l - 1f));
            float h = max == rgb.r ? (rgb.g - rgb.b) / difference : max == rgb.g ? (rgb.b - rgb.r) / difference + 2f : (rgb.r - rgb.g) / difference + 4f;
            return new Vector3(Mathf.Repeat(h / 6f, 1f), s, l);
        }
    }
}
