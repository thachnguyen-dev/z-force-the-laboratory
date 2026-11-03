using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;
using TouchPhase = UnityEngine.InputSystem.TouchPhase;

namespace ZombieGame
{
    public struct PointerState
    {
        public int Id;
        /// <summary>Virtual GUI units (see <see cref="Gui"/>), origin top-left.</summary>
        public Vector2 Position;
        public bool Began;
        public bool Ended;
    }

    /// <summary>Every finger on the screen, plus the mouse's left button as one more pointer.</summary>
    public static class PointerInput
    {
        public const int MouseId = -1;
        private static readonly List<PointerState> Pointers = new List<PointerState>();
        private static int _frame = -1;

        public static IReadOnlyList<PointerState> Current
        {
            get
            {
                if (_frame != Time.frameCount) Refresh();
                return Pointers;
            }
        }

        private static void Refresh()
        {
            _frame = Time.frameCount;
            Pointers.Clear();
            if (!EnhancedTouchSupport.enabled) EnhancedTouchSupport.Enable();
            foreach (var touch in Touch.activeTouches)
            {
                var phase = touch.phase;
                Pointers.Add(new PointerState
                {
                    Id = touch.touchId,
                    Position = Gui.FromScreen(touch.screenPosition),
                    Began = phase == TouchPhase.Began,
                    Ended = phase == TouchPhase.Ended || phase == TouchPhase.Canceled,
                });
            }
            var mouse = Mouse.current;
            if (Pointers.Count > 0 || mouse == null) return;
            var button = mouse.leftButton;
            if (!button.isPressed && !button.wasReleasedThisFrame) return;
            Pointers.Add(new PointerState
            {
                Id = MouseId,
                Position = Gui.FromScreen(mouse.position.ReadValue()),
                Began = button.wasPressedThisFrame,
                Ended = button.wasReleasedThisFrame,
            });
        }
    }

    /// <summary>
    /// IMGUI helpers for the HUD and the screens not yet in the real UI. Everything is laid out on a virtual canvas 720
    /// units tall so controls keep their size across screens. Owner, 2026-10-08 (the cartoon style): they draw in the
    /// real UI's look (UiKit): rounded outlined panels and bars, chunky buttons, outlined white text, the cartoon font.
    /// </summary>
    public static class Gui
    {
        public const float VirtualHeight = 720f;
        private static Texture2D _circle;
        private static GUIStyle _label;
        private static bool _fontWarmed;

        /// <summary>
        /// The owner's fonts (2026-10-04): one for the title and large text, one for medium and small text (both SIL OFL,
        /// licences next to them in Resources/Fonts). Text from this size up uses the title font.
        /// </summary>
        public const int TitleFontFrom = 24;
        private static Font _titleFont, _textFont;
        private static bool _fontsLoaded;

        public static Font TitleFont { get { LoadFonts(); return _titleFont; } }
        public static Font TextFont { get { LoadFonts(); return _textFont; } }

        private static void LoadFonts()
        {
            if (_fontsLoaded) return;
            _fontsLoaded = true;
            // Owner, 2026-10-08: the cartoon font everywhere (the real UI's too).
            _titleFont = Resources.Load<Font>("Fonts/Cartoon");
            _textFont = _titleFont;
        }

        public static float Scale => Mathf.Max(0.01f, Screen.height / VirtualHeight);
        public static float Width => Screen.width / Scale;
        public static float Height => VirtualHeight;

        public static Vector2 FromScreen(Vector2 screen) => new Vector2(screen.x, Screen.height - screen.y) / Scale;

        /// <summary>Development builds and the editor: the testing tools show. Never in a release (store) build.</summary>
        public static bool DevTools => DevToolsOverride ?? (Debug.isDebugBuild || Application.isEditor);

        /// <summary>Tests only: behave as a release build (false) or a development build (true).</summary>
        public static bool? DevToolsOverride;

        public static void Begin()
        {
            GUI.matrix = Matrix4x4.Scale(new Vector3(Scale, Scale, 1f));
            if (TextFont != null && GUI.skin.label.font != TextFont)
            {
                GUI.skin.font = TextFont;
                GUI.skin.label.font = TextFont;
                GUI.skin.button.font = TextFont;
                GUI.skin.textField.font = TextFont;
                GUI.skin.toggle.font = TextFont;
                _label = null;
            }
            if (GUI.skin.button.fontSize != 22)
            {
                GUI.skin.button.fontSize = 22;
                GUI.skin.label.fontSize = 18;
            }
            if (!_fontWarmed) WarmFont();
        }

        /// <summary>Renders every size and character up front: a new glyph mid-fight stalls a phone frame.</summary>
        private static void WarmFont()
        {
            _fontWarmed = true;
            var chars = new System.Text.StringBuilder(128);
            for (var c = ' '; c <= '~'; c++) chars.Append(c);
            chars.Append("×–—·∞");
            foreach (var font in new[] { TextFont != null ? TextFont : GUI.skin.font, TitleFont })
            {
                if (font == null) continue;
                foreach (var size in new[] { 14, 16, 18, 20, 22, 24, 26, 32, 48 })
                {
                    font.RequestCharactersInTexture(chars.ToString(), size, FontStyle.Normal);
                    font.RequestCharactersInTexture(chars.ToString(), size, FontStyle.Bold);
                }
            }
        }

        public static Vector2? WorldToGui(Camera camera, Vector3 world)
        {
            if (camera == null) return null;
            var s = camera.WorldToScreenPoint(world);
            if (s.z < 0f) return null;
            return FromScreen(s);
        }

        public static void Circle(Vector2 center, float radius, Color color)
        {
            if (_circle == null) _circle = MakeCircle(128);
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(new Rect(center.x - radius, center.y - radius, radius * 2f, radius * 2f), _circle);
            GUI.color = previous;
        }

        public static void Fill(Rect rect, Color color)
        {
            var previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static GUIStyle _panel, _button, _bar;

        private static GUIStyle Sliced(Sprite sprite)
        {
            var b = sprite.border;
            return new GUIStyle { normal = { background = sprite.texture }, border = new RectOffset((int)b.x, (int)b.z, (int)b.w, (int)b.y) };
        }

        /// <summary>A rounded panel with a heavy outline (the real UI's), tinted <paramref name="color"/>.</summary>
        public static void Panel(Rect rect, Color color)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (_panel == null) _panel = Sliced(UiKit.PanelSprite);
            var previous = GUI.color;
            GUI.color = color;
            _panel.Draw(rect, false, false, false, false);
            GUI.color = previous;
        }

        /// <summary>A rounded bar: its dark track, filled to <paramref name="k"/> (0-1) with <paramref name="fill"/>.</summary>
        public static void Bar(Rect rect, float k, Color fill, Color? back = null)
        {
            if (Event.current.type != EventType.Repaint) return;
            if (_bar == null) _bar = Sliced(UiKit.BarSprite);
            var previous = GUI.color;
            GUI.color = back ?? new Color(0.05f, 0.08f, 0.14f, 0.85f);
            _bar.Draw(rect, false, false, false, false);
            k = Mathf.Clamp01(k);
            if (k > 0.001f)
            {
                GUI.color = fill;
                var inner = new Rect(rect.x + 2f, rect.y + 2f, Mathf.Max(rect.height - 4f, (rect.width - 4f) * k), rect.height - 4f);
                _bar.Draw(inner, false, false, false, false);
            }
            GUI.color = previous;
        }

        public static void Frame(Rect r, float thickness, Color color)
        {
            Fill(new Rect(r.x, r.y, r.width, thickness), color);
            Fill(new Rect(r.x, r.yMax - thickness, r.width, thickness), color);
            Fill(new Rect(r.x, r.y, thickness, r.height), color);
            Fill(new Rect(r.xMax - thickness, r.y, thickness, r.height), color);
        }

        public static void Label(Rect rect, string text, int size, TextAnchor anchor, Color color, bool bold = false)
        {
            if (_label == null) _label = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = false };
            _label.fontSize = size;
            _label.font = size >= TitleFontFrom && TitleFont != null ? TitleFont : TextFont;
            _label.alignment = anchor;
            _label.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            // The old grey hints were made for a dark background; on the cartoon backdrop they need a light tone.
            if (color == Color.gray) color = UiKit.Dim;
            _label.normal.textColor = color;
            GUI.Label(rect, text, _label);
        }

        /// <summary>Text with a dark outline all round and a drop below (the cartoon style).</summary>
        public static void Shadowed(Rect rect, string text, int size, TextAnchor anchor, Color color, bool bold = false)
        {
            var o = Mathf.Clamp(size * 0.07f, 1.2f, 2.6f);
            var line = new Color(UiKit.Line.r, UiKit.Line.g, UiKit.Line.b, color.a);
            Label(new Rect(rect.x, rect.y + o * 1.6f, rect.width, rect.height), text, size, anchor, new Color(0f, 0f, 0f, 0.5f * color.a), bold);
            Label(new Rect(rect.x - o, rect.y - o, rect.width, rect.height), text, size, anchor, line, bold);
            Label(new Rect(rect.x + o, rect.y - o, rect.width, rect.height), text, size, anchor, line, bold);
            Label(new Rect(rect.x - o, rect.y + o, rect.width, rect.height), text, size, anchor, line, bold);
            Label(new Rect(rect.x + o, rect.y + o, rect.width, rect.height), text, size, anchor, line, bold);
            Label(rect, text, size, anchor, color, bold);
        }

        /// <summary>A chunky cartoon button (blue; <paramref name="tint"/> for another colour). True on the frame it's pressed.</summary>
        public static bool Button(Rect rect, string text, Color? tint = null)
        {
            if (_button == null) _button = Sliced(UiKit.ButtonSprite);
            var enabled = GUI.enabled;
            var hover = enabled && rect.Contains(Event.current.mousePosition);
            if (Event.current.type == EventType.Repaint)
            {
                var previous = GUI.color;
                var c = tint ?? UiKit.Blue;
                GUI.color = !enabled ? new Color(0.35f, 0.4f, 0.45f, 0.6f) : hover ? Color.Lerp(c, Color.white, 0.12f) : c;
                _button.Draw(rect, false, false, false, false);
                GUI.color = previous;
            }
            var size = Mathf.Clamp(Mathf.RoundToInt(rect.height * 0.38f), 14, 30);
            Shadowed(new Rect(rect.x + 4f, rect.y, rect.width - 8f, rect.height - 6f), text, size, TextAnchor.MiddleCenter, enabled ? Color.white : new Color(1f, 1f, 1f, 0.5f));
            var pressed = GUI.Button(rect, GUIContent.none, GUIStyle.none);
            if (pressed) Sfx.PlayUi();
            return pressed;
        }

        /// <summary>
        /// A round touch button (cartoon): a dark outline ring, the face lighter at the top with a gloss, tinted by the colour
        /// it's drawn with (its alpha still sets how see-through it is).
        /// </summary>
        private static Texture2D MakeCircle(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var r = size * 0.5f;
            var ring = size * 0.05f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                var a = Mathf.Clamp01(r - d);
                float v;
                if (d > r - ring) v = 0.15f;
                else
                {
                    var t = y / (float)size;
                    v = Mathf.Lerp(0.78f, 1f, t);
                    if (t > 0.62f && t < 0.84f && d < r * 0.78f) v = 1f;
                }
                texture.SetPixel(x, y, new Color(v, v, v, a));
            }
            texture.Apply();
            return texture;
        }
    }
}
