using System;
using UnityEngine;
using UnityEngine.UI;

namespace ZombieGame
{
    /// <summary>
    /// Owner, 2026-10-09: the introduction, the first time the game starts (and from Settings). The main story in seven
    /// cards: each a picture from the game's own world (Resources/UI/Intro, rendered by CaptureTests.RenderTheIntroPictures)
    /// drifting slowly closer, its line of the story typed in below; tap to go on (a tap while typing shows the whole
    /// line), or it goes on by itself. The last card is the team and the name. Skip leaves at any time.
    /// </summary>
    public class IntroPlayer : MonoBehaviour
    {
        /// <summary>The story (owner, 2026-10-03), a line or two a card; null picture = the team on the cartoon backdrop.</summary>
        public static readonly (string picture, string text)[] Cards =
        {
            ("UI/Intro/1", "In a remote town, the government opened a wildlife research and conservation facility, run by scientists and the military."),
            ("UI/Intro/2", "That was the story. In truth, it was a vast biological weapons laboratory."),
            ("UI/Intro/3", "Then the scientists made a mistake: a virus that invades the brain and takes control of its host."),
            ("UI/Intro/4", "It spread fast: first the lab rats and rabbits, then the large animals of the wildlife reserve."),
            ("UI/Intro/5", "Then the scientists and the security forces, one after another, turned into zombies."),
            ("UI/Intro/6", "The government sealed the town. Inside, scientists are still alive, and with them a vaccine against the virus."),
            (null, "A special forces team is sent in: rescue the scientists, recover the vaccine."),
        };

        /// <summary>Letters typed a second. PLACEHOLDER.</summary>
        public const float TypeSpeed = 42f;
        /// <summary>Seconds a card stays once its line is typed, before the next. PLACEHOLDER.</summary>
        public const float Hold = 4.5f;
        private const float Fade = 0.45f;
        /// <summary>How far the picture drifts closer over a card. PLACEHOLDER.</summary>
        private const float Zoom = 0.07f;

        public event Action Finished;

        private RawImage _picture, _black;
        private RectTransform _team;
        private Text _text, _hint;
        private Image[] _dots;
        private int _card = -1;
        private float _t, _typed, _doneAt = -1f, _fadeIn;
        private bool _leaving;
        private float _width = 1280f;
        private float _leaveT;

        /// <summary>Builds the intro over <paramref name="screen"/> (the menus' canvas area).</summary>
        public static IntroPlayer Create(RectTransform screen, float width, Action finished)
        {
            var root = UiKit.Node(screen, "Intro");
            UiKit.Fill(root);
            var intro = root.gameObject.AddComponent<IntroPlayer>();
            intro.Finished += finished;
            intro._width = width;
            intro.Build(root);
            return intro;
        }

        private void Build(RectTransform root)
        {
            // The picture, filling the screen (cropped to keep its shape).
            var holder = UiKit.Node(root, "Picture");
            UiKit.Fill(holder);
            holder.gameObject.AddComponent<RectMask2D>();
            _picture = UiKit.Node(holder, "Image").gameObject.AddComponent<RawImage>();
            UiKit.Fill(_picture.rectTransform);
            _picture.raycastTarget = false;
            // The team (last card).
            _team = UiKit.Node(root, "Team");
            UiKit.Fill(_team);
            var team = UiKit.Node(_team, "Team picture").gameObject.AddComponent<Image>();
            team.sprite = Resources.Load<Sprite>("UI/Portraits/team");
            team.preserveAspect = true;
            team.raycastTarget = false;
            UiKit.Place(team.rectTransform, 0f, 172f, 400f, 300f);
            var title = UiKit.Title(_team, "Z-Force", 110, UiKit.Green);
            UiKit.Place(title.rectTransform, 0f, 10f, 700f, 130f);
            var sub = UiKit.Title(_team, "The Laboratory", 40, UiKit.Ink);
            UiKit.Place(sub.rectTransform, 0f, 118f, 600f, 56f);
            // The caption: a dark band low on the screen.
            var band = UiKit.Box(root, "Caption", UiKit.PanelDark);
            band.raycastTarget = false;
            UiKit.Place(band.rectTransform, 0f, UiKit.Height - 200f, Mathf.Min(1100f, _width - 60f), 150f);
            _text = UiKit.Label(band.rectTransform, "", 30, UiKit.Ink, TextAnchor.MiddleCenter);
            UiKit.Fill(_text.rectTransform, 22f);
            _hint = UiKit.Label(root, "Tap to continue", 18, UiKit.Dim, TextAnchor.MiddleCenter);
            UiKit.Place(_hint.rectTransform, 0f, UiKit.Height - 46f, 400f, 30f);
            // Where we are: a dot a card.
            _dots = new Image[Cards.Length];
            for (var i = 0; i < Cards.Length; i++)
            {
                _dots[i] = UiKit.Box(root, "Dot", UiKit.Dim);
                _dots[i].raycastTarget = false;
                UiKit.Place(_dots[i].rectTransform, (i - (Cards.Length - 1) * 0.5f) * 26f, UiKit.Height - 228f, 14f, 14f);
            }
            // Taps anywhere go on; Skip leaves.
            var tap = UiKit.Node(root, "Tap").gameObject.AddComponent<Button>();
            var catcher = tap.gameObject.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            UiKit.Fill((RectTransform)tap.transform);
            tap.transition = Selectable.Transition.None;
            tap.onClick.AddListener(Next);
            var skip = UiKit.Button(root, "Skip", Leave, UiKit.Kind.Quiet, 22);
            var srt = (RectTransform)skip.transform;
            srt.anchorMin = srt.anchorMax = srt.pivot = new Vector2(1f, 1f);
            srt.anchoredPosition = new Vector2(-24f, -20f);
            srt.sizeDelta = new Vector2(150f, 54f);
            // The fade between cards.
            _black = UiKit.Node(root, "Fade").gameObject.AddComponent<RawImage>();
            _black.color = Color.black;
            _black.raycastTarget = false;
            UiKit.Fill(_black.rectTransform);
            Show(0);
        }

        private void Show(int card)
        {
            _card = card;
            _t = 0f;
            _typed = 0f;
            _doneAt = -1f;
            _fadeIn = 0f;
            var (picture, _) = Cards[card];
            var texture = picture != null ? Resources.Load<Texture2D>(picture) : null;
            _picture.texture = texture != null ? texture : UiKit.Backdrop();
            _picture.gameObject.SetActive(true);
            _team.gameObject.SetActive(picture == null);
            _text.text = "";
            for (var i = 0; i < _dots.Length; i++) _dots[i].color = i == card ? UiKit.Yellow : new Color(1f, 1f, 1f, 0.35f);
        }

        private void Next()
        {
            if (_leaving) return;
            Sfx.PlayUi();
            var line = Cards[_card].text;
            // Still typing: the whole line at once.
            if (_typed < line.Length)
            {
                _typed = line.Length;
                return;
            }
            if (_card + 1 < Cards.Length) Show(_card + 1);
            else Leave();
        }

        private void Leave()
        {
            if (_leaving) return;
            _leaving = true;
            _leaveT = 0f;
        }

        private void Update()
        {
            var dt = Time.unscaledDeltaTime;
            if (_leaving)
            {
                _leaveT += dt;
                _black.color = new Color(0f, 0f, 0f, Mathf.Clamp01(_leaveT / Fade));
                if (_leaveT >= Fade)
                {
                    enabled = false;
                    Finished?.Invoke();
                }
                return;
            }
            _t += dt;
            _fadeIn += dt;
            _black.color = new Color(0f, 0f, 0f, 1f - Mathf.Clamp01(_fadeIn / Fade));
            // The picture drifts slowly closer, keeping its shape on any screen.
            FitPicture(1f + Zoom * Mathf.SmoothStep(0f, 1f, _t / 9f));
            var line = Cards[_card].text;
            if (_fadeIn > Fade * 0.6f) _typed = Mathf.Min(line.Length, _typed + TypeSpeed * dt);
            _text.text = line.Substring(0, Mathf.FloorToInt(_typed));
            var done = _typed >= line.Length;
            if (done && _doneAt < 0f) _doneAt = _t;
            _hint.text = _card + 1 < Cards.Length ? "Tap to continue" : "Tap to begin";
            _hint.color = new Color(UiKit.Dim.r, UiKit.Dim.g, UiKit.Dim.b, done ? 0.55f + 0.45f * Mathf.Sin(_t * 4f) : 0f);
            // On by itself after a while (the last card waits for a tap).
            if (done && _card + 1 < Cards.Length && _t - _doneAt > Hold) Show(_card + 1);
        }

        /// <summary>The picture covers the screen at this zoom (cropping the long side), centred.</summary>
        private void FitPicture(float zoom)
        {
            var tex = _picture.texture;
            if (tex == null) return;
            var screen = ((RectTransform)transform).rect.size;
            if (screen.x <= 0f || screen.y <= 0f) return;
            var pictureAspect = (float)tex.width / tex.height;
            var screenAspect = screen.x / screen.y;
            var w = 1f / zoom;
            var h = 1f / zoom;
            if (screenAspect > pictureAspect) h *= pictureAspect / screenAspect;
            else w *= screenAspect / pictureAspect;
            _picture.uvRect = new Rect((1f - w) * 0.5f, (1f - h) * 0.5f, w, h);
        }
    }
}
