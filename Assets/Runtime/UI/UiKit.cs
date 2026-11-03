using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace ZombieGame
{
    /// <summary>
    /// The real UI's parts (owner, 2026-10-08: "UI next", no UI art yet, so Claude's placeholder style; then "more
    /// cartoon-like rather than overly horror-themed"): a canvas scaled to the screen (720 units tall, like the old
    /// code-drawn UI) inside the phone's safe area; rounded panels with heavy outlines, chunky 3D buttons (a darker ledge
    /// below, a glossy top), white text with a thick dark outline in a rounded display font (Lilita One, SIL OFL). Bright
    /// colours: yellow for the main action, blue for the rest, slate for going back, the infection's green for selection.
    /// </summary>
    public static class UiKit
    {
        public const float Height = 720f;

        public static readonly Color Ink = Color.white;
        public static readonly Color Dim = new Color(0.78f, 0.86f, 0.9f);
        public static readonly Color Green = new Color(0.5f, 0.95f, 0.3f);
        public static readonly Color GreenDark = new Color(0.2f, 0.5f, 0.15f);
        public static readonly Color Amber = new Color(1f, 0.82f, 0.28f);
        public static readonly Color Red = new Color(1f, 0.36f, 0.3f);
        public static readonly Color Yellow = new Color(1f, 0.8f, 0.14f);
        public static readonly Color Blue = new Color(0.24f, 0.6f, 0.95f);
        public static readonly Color Slate = new Color(0.42f, 0.5f, 0.6f);
        public static readonly Color Panel = new Color(0.16f, 0.27f, 0.4f, 0.95f);
        public static readonly Color PanelDark = new Color(0.08f, 0.14f, 0.22f, 0.95f);
        public static readonly Color Metal = Blue;
        /// <summary>The dark of every outline (text and shapes).</summary>
        public static readonly Color Line = new Color(0.05f, 0.08f, 0.16f, 1f);

        public enum Kind { Normal, Primary, Danger, Quiet }

        private static Font _title, _text;
        private static Sprite _panel, _button, _frame, _bar;

        public static Font TitleFont => _title != null ? _title : _title = Resources.Load<Font>("Fonts/Cartoon");
        public static Font TextFont => _text != null ? _text : _text = Resources.Load<Font>("Fonts/Cartoon");

        // ------------------------------------------------------------------ the canvas

        /// <summary>A screen-space canvas (720 units tall), its content kept inside the safe area. Returns the safe area.</summary>
        public static RectTransform Canvas(string name, int order)
        {
            EnsureEventSystem();
            var go = new GameObject(name, typeof(RectTransform));
            Object.DontDestroyOnLoad(go);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = order;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, Height);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;
            go.AddComponent<GraphicRaycaster>();
            var safe = new GameObject("Safe area", typeof(RectTransform)).GetComponent<RectTransform>();
            safe.SetParent(go.transform, false);
            safe.gameObject.AddComponent<SafeArea>();
            return safe;
        }

        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("Event system", typeof(EventSystem), typeof(InputSystemUIInputModule));
            Object.DontDestroyOnLoad(go);
        }

        /// <summary>Keeps its rect inside the screen's safe area (notches, rounded corners).</summary>
        public class SafeArea : MonoBehaviour
        {
            private Rect _applied;

            private void Update()
            {
                var area = Screen.safeArea;
                if (area == _applied || Screen.width <= 0 || Screen.height <= 0) return;
                _applied = area;
                var rt = (RectTransform)transform;
                rt.anchorMin = new Vector2(area.xMin / Screen.width, area.yMin / Screen.height);
                rt.anchorMax = new Vector2(area.xMax / Screen.width, area.yMax / Screen.height);
                rt.offsetMin = rt.offsetMax = Vector2.zero;
            }
        }

        // ------------------------------------------------------------------ placing

        /// <summary>A child rect placed from the top centre: x is its centre's offset from the middle, y its top.</summary>
        public static RectTransform Place(RectTransform rt, float x, float top, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(x, -top);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        /// <summary>A child rect filling its parent, inset by <paramref name="inset"/>.</summary>
        public static RectTransform Fill(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        public static RectTransform Node(Transform parent, string name)
        {
            var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rt.SetParent(parent, false);
            return rt;
        }

        // ------------------------------------------------------------------ parts

        public static Image Box(Transform parent, string name, Color tint, Sprite sprite = null)
        {
            var image = Node(parent, name).gameObject.AddComponent<Image>();
            image.sprite = sprite != null ? sprite : PanelSprite;
            image.type = Image.Type.Sliced;
            image.color = tint;
            return image;
        }

        public static Text Label(Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleLeft, bool title = false)
        {
            var t = Node(parent, "Label").gameObject.AddComponent<Text>();
            t.font = title ? TitleFont : TextFont;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            // Cartoon text: a dark outline all round, and a drop below.
            var outline = t.gameObject.AddComponent<Outline>();
            outline.effectColor = Line;
            var o = Mathf.Clamp(size * 0.06f, 1.2f, 3.2f);
            outline.effectDistance = new Vector2(o, -o);
            var shadow = t.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.55f);
            shadow.effectDistance = new Vector2(0f, -Mathf.Clamp(size * 0.09f, 1.5f, 6f));
            return t;
        }

        /// <summary>A heading in the owner's title font, outlined so the drips read on any background.</summary>
        public static Text Title(Transform parent, string text, int size, Color color, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var t = Label(parent, text, size, color, align, true);
            // Headings: a second, wider outline for weight.
            var outline = t.gameObject.AddComponent<Outline>();
            outline.effectColor = Line;
            // (Capped: wide outline copies turn into a block behind big letters.)
            var o = Mathf.Clamp(size * 0.06f, 1.5f, 3.5f);
            outline.effectDistance = new Vector2(o, o);
            return t;
        }

        /// <summary>Shrinks the text (down to <paramref name="min"/>) to fit its box on one line.</summary>
        public static void Fit(Text t, int min)
        {
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = min;
            t.resizeTextMaxSize = t.fontSize;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
        }

        public static Button Button(Transform parent, string text, System.Action onClick, Kind kind = Kind.Normal, int size = 24)
        {
            var image = Box(parent, "Button " + text, Color.white, ButtonSprite);
            var button = image.gameObject.AddComponent<Button>();
            var c = kind == Kind.Primary ? Yellow : kind == Kind.Danger ? Red : kind == Kind.Quiet ? Slate : Blue;
            var colors = button.colors;
            colors.normalColor = c;
            colors.highlightedColor = Color.Lerp(c, Color.white, 0.18f);
            colors.selectedColor = c;
            colors.pressedColor = Color.Lerp(c, Color.black, 0.25f);
            colors.disabledColor = new Color(0.3f, 0.32f, 0.3f, 0.55f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            var label = Label(image.transform, text, size, Ink, TextAnchor.MiddleCenter);
            Fill(label.rectTransform, 6f);
            // The label sits on the button's face, above its ledge.
            label.rectTransform.offsetMin = new Vector2(6f, 10f);
            button.onClick.AddListener(() =>
            {
                Sfx.PlayUi();
                onClick?.Invoke();
            });
            return button;
        }

        /// <summary>A thin frame (selected cards, the minimap): only its edges.</summary>
        public static Image Frame(Transform parent, Color color)
        {
            var image = Box(parent, "Frame", color, FrameSprite);
            image.raycastTarget = false;
            Fill(image.rectTransform, -3f);
            return image;
        }

        /// <summary>A bar filled to <paramref name="k"/> (0-1), for stats and health.</summary>
        public static RectTransform Bar(Transform parent, float k, Color fill)
        {
            var back = Box(parent, "Bar", new Color(0.05f, 0.06f, 0.05f, 0.85f), BarSprite);
            var front = Box(back.transform, "Fill", fill, BarSprite);
            var rt = front.rectTransform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = new Vector2(Mathf.Clamp01(k), 1f);
            rt.offsetMin = new Vector2(2f, 2f);
            rt.offsetMax = new Vector2(-2f, -2f);
            return back.rectTransform;
        }

        // ------------------------------------------------------------------ sprites (made once)

        public static Sprite PanelSprite => _panel != null ? _panel : _panel = Rounded(64, 14, 3, 0f, 0.92f, 1f);
        public static Sprite ButtonSprite => _button != null ? _button : _button = Rounded(64, 16, 3, 7f, 0.9f, 1f);
        public static Sprite BarSprite => _bar != null ? _bar : _bar = Rounded(24, 6, 2, 0f, 0.95f, 1f);
        public static Sprite FrameSprite => _frame != null ? _frame : _frame = RoundFrame(64, 16, 5);

        /// <summary>
        /// A rounded cartoon shape, light grey so the image's colour tints it: a heavy dark outline, a lighter top half with a
        /// gloss band, and (for buttons) a darker ledge along the bottom that makes it stand up. Sliced, so it stretches
        /// cleanly.
        /// </summary>
        private static Sprite Rounded(int size, int radius, int outline, float ledge, float low, float high)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "UI shape" };
            var px = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                // Distance inside the rounded square (negative outside).
                var cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                var cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                var inside = radius - new Vector2(x + 0.5f - cx, y + 0.5f - cy).magnitude;
                var edge = Mathf.Min(inside, Mathf.Min(Mathf.Min(x + 0.5f, size - x - 0.5f), Mathf.Min(y + 0.5f, size - y - 0.5f)));
                var a = Mathf.Clamp01(inside + 0.5f);
                if (a <= 0f)
                {
                    px[y * size + x] = new Color32(0, 0, 0, 0);
                    continue;
                }
                float v;
                if (edge < outline) v = 0.12f;                                            // the outline
                else if (y < ledge + outline) v = Mathf.Lerp(low, high, 0.2f) * 0.62f;    // the ledge (darker)
                else
                {
                    var t = (y - ledge - outline) / Mathf.Max(1f, size - ledge - outline * 2f);
                    v = Mathf.Lerp(low * 0.82f, high, t);
                    if (t > 0.6f && t < 0.86f) v = Mathf.Min(1f, v + 0.08f);              // the gloss band
                }
                var g = (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);
                px[y * size + x] = new Color32(g, g, g, (byte)Mathf.RoundToInt(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply();
            var b = radius + outline + 1;
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b + ledge, b, b));
        }

        private static Sprite RoundFrame(int size, int radius, int width)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "UI frame" };
            var px = new Color32[size * size];
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                var cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                var inside = radius - new Vector2(x + 0.5f - cx, y + 0.5f - cy).magnitude;
                var edge = Mathf.Min(inside, Mathf.Min(Mathf.Min(x + 0.5f, size - x - 0.5f), Mathf.Min(y + 0.5f, size - y - 0.5f)));
                var a = Mathf.Clamp01(inside + 0.5f) * Mathf.Clamp01(width - edge + 0.5f);
                px[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
            }
            tex.SetPixels32(px);
            tex.Apply();
            var b = radius + width + 1;
            return Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
        }

        /// <summary>
        /// The menus' backdrop (cartoon): a teal-green sky, lighter at the top, darker below, with broad soft diagonal stripes.
        /// </summary>
        public static Texture2D Backdrop()
        {
            const int w = 256, h = 144;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Menu backdrop" };
            var px = new Color32[w * h];
            var top = new Color(0.2f, 0.62f, 0.55f);
            var bottom = new Color(0.07f, 0.27f, 0.3f);
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var t = y / (float)(h - 1);
                var c = Color.Lerp(bottom, top, t);
                // Stripes across, gently.
                var stripe = Mathf.Repeat((x + y) / 22f, 1f) < 0.5f ? 1.05f : 0.96f;
                var u = x / (float)(w - 1) - 0.5f;
                var v = t - 0.55f;
                var vignette = 1f - Mathf.Clamp01(u * u * 0.9f + v * v * 1.1f) * 0.45f;
                px[y * w + x] = new Color(c.r * stripe * vignette, c.g * stripe * vignette, c.b * stripe * vignette, 1f);
            }
            tex.SetPixels32(px);
            tex.Apply();
            return tex;
        }
    }
}
