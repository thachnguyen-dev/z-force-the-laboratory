using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ZombieGame
{
    public partial class GameSession
    {
        private Menus _menus;

        /// <summary>
        /// The front-end in the real UI (owner, 2026-10-08: "UI next"): the main menu, Co-op, joining, the lobby, the mission
        /// list with its briefing, and the character choice. Each screen does what the old code-drawn one did; it is built
        /// when something it shows changes (the lobby's status line follows every frame). Settings and the result
        /// screens are still the old ones for now.
        /// </summary>
        private sealed class Menus
        {
            private readonly GameSession _g;
            private readonly RectTransform _safe;
            private readonly RectTransform _screen;
            private readonly RawImage _back;
            private readonly Image _dim;
            private string _shownKey;
            private Text _status;
            private Text _copy;
            private InputField _code;
            /// <summary>The "Remove ads + Co-op" offer is open over the main menu.</summary>
            private bool _offer;

            /// <summary>Opens the "Remove Ads + Co-op" offer (the store screenshots).</summary>
            public void ShowOffer() => _offer = true;

            public Menus(GameSession g)
            {
                _g = g;
                _safe = UiKit.Canvas("Menus", 10);
                var canvas = _safe.parent;
                // The backdrop fills the whole screen (behind the safe area).
                _back = UiKit.Node(canvas, "Backdrop").gameObject.AddComponent<RawImage>();
                _back.texture = UiKit.Backdrop();
                _back.raycastTarget = false;
                UiKit.Fill(_back.rectTransform);
                _back.rectTransform.SetAsFirstSibling();
                // Over a finished stage: the stage stays in view, dimmed.
                _dim = UiKit.Node(canvas, "Dim").gameObject.AddComponent<Image>();
                _dim.color = new Color(0.02f, 0.05f, 0.1f, 0.6f);
                UiKit.Fill(_dim.rectTransform);
                _dim.rectTransform.SetSiblingIndex(1);
                _screen = UiKit.Fill(UiKit.Node(_safe, "Screen"));
            }

            public void Destroy()
            {
                if (_safe != null) Object.Destroy(_safe.parent.gameObject);
            }

            public static bool Handles(SessionState s) =>
                s == SessionState.MainMenu || s == SessionState.CoopSelect || s == SessionState.Join ||
                s == SessionState.OnlineLobby || s == SessionState.StageSelect || s == SessionState.CharacterSelect ||
                s == SessionState.Victory || s == SessionState.Defeat || s == SessionState.Intro;

            private static bool Result(SessionState s) => s == SessionState.Victory || s == SessionState.Defeat;

            private float W => _safe.rect.width > 1f ? _safe.rect.width : 1280f;

            public void Update()
            {
                var on = Handles(_g.State);
                var canvas = _safe.parent.gameObject;
                if (canvas.activeSelf != on) canvas.SetActive(on);
                if (!on)
                {
                    _shownKey = null;
                    return;
                }
                var result = Result(_g.State);
                if (_back.enabled == result) _back.enabled = !result;
                if (_dim.enabled != result) _dim.enabled = result;
                if (_g.State == SessionState.OnlineLobby && LobbyGone()) return;
                var key = Key();
                if (key != _shownKey)
                {
                    _shownKey = key;
                    Build();
                }
                if (_status != null && NetSession.Instance != null) _status.text = NetSession.Instance.Status;
                if (_copy != null) _copy.text = Time.unscaledTime < _g._copiedUntil ? "Copied!" : "Copy";
            }

            /// <summary>Everything a screen shows that can change; a different key rebuilds it.</summary>
            private string Key()
            {
                var net = NetSession.Instance;
                var s = _g.Settings;
                return string.Join("|", _g.State, Mission.FailReason, _g._storyIndex, _g._allStages, _g._character, _g.WithAi, OnlineHost, s.unlockAllForTesting,
                    s.completedMissions.Count, s.completedCoop.Count, net != null ? $"{net.Role}{net.PeerConnected}{net.JoinCode}{net.LocalCharacter}{net.Busy}" : "-",
                    _g._menuMessage, Mathf.RoundToInt(W), _offer, Monetization.Owned, Monetization.CoopUnlocked, Monetization.Busy,
                    Monetization.Message, Monetization.Price, AdConsent.Current, AdConsent.MustAsk);
            }

            private void Build()
            {
                for (var i = _screen.childCount - 1; i >= 0; i--) Object.Destroy(_screen.GetChild(i).gameObject);
                _status = null;
                _copy = null;
                _code = null;
                switch (_g.State)
                {
                    case SessionState.MainMenu: Main(); break;
                    case SessionState.CoopSelect: Coop(); break;
                    case SessionState.Join: Join(); break;
                    case SessionState.OnlineLobby: Lobby(); break;
                    case SessionState.StageSelect: Missions(); break;
                    case SessionState.CharacterSelect: Characters(); break;
                    case SessionState.Victory:
                    case SessionState.Defeat: Finished(); break;
                    case SessionState.Intro: IntroPlayer.Create(_screen, W, _g.IntroFinished); break;
                }
            }

            // ------------------------------------------------------------------ shared pieces

            private Text Heading(string text, float top = 60f, int size = 40)
            {
                var t = UiKit.Title(_screen, text, size, UiKit.Ink);
                UiKit.Place(t.rectTransform, 0f, top, W - 80f, size * 1.6f);
                return t;
            }

            private Button Btn(string text, float x, float top, float w, float h, System.Action onClick, UiKit.Kind kind = UiKit.Kind.Normal, int size = 24, bool enabled = true)
            {
                var b = UiKit.Button(_screen, text, onClick, kind, size);
                UiKit.Place((RectTransform)b.transform, x, top, w, h);
                b.interactable = enabled;
                return b;
            }

            private Text Line(string text, float x, float top, float w, float h, int size, Color color, TextAnchor align = TextAnchor.MiddleCenter, Transform parent = null)
            {
                var t = UiKit.Label(parent != null ? parent : _screen, text, size, color, align);
                UiKit.Place(t.rectTransform, x, top, w, h);
                return t;
            }

            private RectTransform PanelAt(float x, float top, float w, float h, Color? tint = null)
            {
                var p = UiKit.Box(_screen, "Panel", tint ?? UiKit.Panel);
                return UiKit.Place(p.rectTransform, x, top, w, h);
            }

            private void Back(SessionState to, float top = 640f) => Btn("Back", 0f, top, 300f, 54f, () => _g.GoTo(to), UiKit.Kind.Quiet, 22);

            private void Message(float top)
            {
                if (string.IsNullOrEmpty(_g._menuMessage)) return;
                Line(_g._menuMessage, 0f, top, Mathf.Min(900f, W - 80f), 56f, 19, UiKit.Amber);
            }

            // ------------------------------------------------------------------ the screens

            /// <summary>
            /// The end of a stage, over the stage dimmed: STAGE CLEARED, DEFEAT, or RUN OVER (Endless) with its result line,
            /// then on to the stage list or the main menu (an online friend waits for the Host instead), as before.
            /// </summary>
            private void Finished()
            {
                var victory = _g.State == SessionState.Victory;
                var endless = _g.Stage != null && _g.Stage.Layout.Endless;
                var panel = PanelAt(0f, 120f, Mathf.Min(760f, W - 60f), 440f);
                var title = UiKit.Title(panel, victory ? "Stage cleared!" : endless ? "Run over" : "Defeat", 64,
                    victory ? UiKit.Yellow : endless ? UiKit.Amber : UiKit.Red);
                UiKit.Place(title.rectTransform, 0f, 30f, Mathf.Min(720f, W - 80f), 96f);
                if (!victory && !string.IsNullOrEmpty(Mission.FailReason))
                    Line(Mission.FailReason, 0f, 136f, Mathf.Min(700f, W - 100f), 60f, 24, UiKit.Ink, TextAnchor.MiddleCenter, panel);
                if (OnlineClient)
                {
                    // The Host picks the next stage; the friend waits in the lobby or leaves.
                    Btn("Wait for the next stage", 0f, 340f, 400f, 70f, () => _g.GoTo(SessionState.OnlineLobby), UiKit.Kind.Primary, 26);
                    Btn("Leave", 0f, 424f, 300f, 60f, () => _g.GoTo(SessionState.MainMenu), UiKit.Kind.Quiet, 22);
                    return;
                }
                Btn("Stage selection", 0f, 340f, 400f, 70f, () => _g.GoTo(SessionState.StageSelect), UiKit.Kind.Primary, 28);
                Btn("Main menu", 0f, 424f, 300f, 60f, () => _g.GoTo(SessionState.MainMenu), UiKit.Kind.Quiet, 22);
            }

            /// <summary>
            /// The main menu (owner, 2026-10-08: with the owner's team picture): the logo and the buttons on the left, the
            /// team on the right, lit from behind and standing on a shadow.
            /// </summary>
            private void Main()
            {
                var wide = W > 1000f;
                var leftX = wide ? -W * 0.2f : 0f;
                if (wide)
                {
                    var teamH = Mathf.Min(600f, UiKit.Height - 60f);
                    var teamX = W * 0.2f;
                    var glow = UiKit.Node(_screen, "Glow").gameObject.AddComponent<RawImage>();
                    glow.texture = Glow();
                    glow.color = new Color(1f, 1f, 0.85f, 0.45f);
                    glow.raycastTarget = false;
                    UiKit.Place(glow.rectTransform, teamX, 40f, teamH * 1.15f, teamH * 1.05f);
                    var shadow = UiKit.Node(_screen, "Ground shadow").gameObject.AddComponent<RawImage>();
                    shadow.texture = Glow();
                    shadow.color = new Color(0f, 0.05f, 0.05f, 0.55f);
                    shadow.raycastTarget = false;
                    UiKit.Place(shadow.rectTransform, teamX + 10f, 60f + teamH * 0.86f, teamH * 0.85f, teamH * 0.16f);
                    var sprite = Resources.Load<Sprite>("UI/Portraits/team");
                    if (sprite != null)
                    {
                        var team = UiKit.Node(_screen, "Team").gameObject.AddComponent<Image>();
                        team.sprite = sprite;
                        team.preserveAspect = true;
                        team.raycastTarget = false;
                        UiKit.Place(team.rectTransform, teamX, 60f, teamH, teamH);
                    }
                }
                // Owner, 2026-10-08: the official name, "Z-Force" the larger.
                var title = UiKit.Title(_screen, "Z-Force", 124, UiKit.Green);
                UiKit.Place(title.rectTransform, leftX, 50f, 660f, 160f);
                var sub = UiKit.Title(_screen, "The Laboratory", 46, UiKit.Ink);
                UiKit.Place(sub.rectTransform, leftX, 196f, 620f, 64f);
                Btn("Solo", leftX, 290f, 380f, 74f, () =>
                {
                    _g.WithAi = false;
                    _g.GoTo(SessionState.StageSelect);
                }, UiKit.Kind.Primary, 32);
                // Owner, 2026-10-08: Co-op opens with the "Remove ads + Co-op" purchase.
                if (Monetization.CoopUnlocked) Btn("Co-op", leftX, 380f, 380f, 66f, () => _g.GoTo(SessionState.CoopSelect), UiKit.Kind.Normal, 27);
                else Btn("Co-op  (locked)", leftX, 380f, 380f, 66f, () => _offer = true, UiKit.Kind.Quiet, 27);
                Btn("Settings", leftX, 462f, 380f, 66f, () => _g.GoTo(SessionState.Settings), UiKit.Kind.Normal, 27);
                if (_offer) Offer();
                // Owner, 2026-10-10: in the consent countries the player chooses before any ad.
                else if (AdConsent.MustAsk) ConsentPrompt();
            }

            /// <summary>
            /// The ad consent (owner, 2026-10-10): what the ads use and the two choices, side by side and alike (neither is
            /// pushed). Over the main menu, everything behind it blocked, until the player chooses.
            /// </summary>
            private void ConsentPrompt()
            {
                var block = UiKit.Node(_screen, "Consent block").gameObject.AddComponent<Image>();
                block.color = new Color(0.02f, 0.05f, 0.1f, 0.75f);
                UiKit.Fill(block.rectTransform);
                var w = Mathf.Min(760f, W - 40f);
                var link = !string.IsNullOrEmpty(AdConsent.PrivacyPolicyUrl);
                var panel = PanelAt(0f, 80f, w, link ? 500f : 432f, new Color(UiKit.Panel.r, UiKit.Panel.g, UiKit.Panel.b, 1f));
                var title = UiKit.Title(panel, "Ads and your privacy", 42, UiKit.Yellow);
                UiKit.Place(title.rectTransform, 0f, 24f, w - 40f, 64f);
                Line("Z-Force is free: it shows an ad when you pause a Solo game. Our ad partners (Unity LevelPlay and the ad " +
                     "networks it works with) can use your device's advertising ID and information about your device to show " +
                     "ads that suit you better, or just ads that don't use that information.", 0f, 96f, w - 70f, 150f, 21, UiKit.Ink,
                    TextAnchor.UpperLeft, panel);
                Line("You can change this any time in Settings. The Remove Ads + Co-op purchase turns ads off.", 0f, 250f, w - 70f,
                    56f, 18, UiKit.Dim, TextAnchor.UpperLeft, panel);
                var bw = (w - 90f) * 0.5f;
                Btn("Personalised ads", -bw * 0.5f - 10f, 80f + 330f, bw, 70f, () => AdConsent.Choose(true), UiKit.Kind.Normal, 24);
                Btn("Non-personalised ads", bw * 0.5f + 10f, 80f + 330f, bw, 70f, () => AdConsent.Choose(false), UiKit.Kind.Normal, 24);
                if (link)
                    Btn("Privacy policy", 0f, 80f + 422f, 260f, 50f, () => Application.OpenURL(AdConsent.PrivacyPolicyUrl), UiKit.Kind.Quiet, 20);
            }

            /// <summary>
            /// The offer (owner, 2026-10-08): one purchase removes the ads and opens Co-op. Over the main menu, everything
            /// behind it blocked.
            /// </summary>
            private void Offer()
            {
                var block = UiKit.Node(_screen, "Offer block").gameObject.AddComponent<Image>();
                block.color = new Color(0.02f, 0.05f, 0.1f, 0.7f);
                UiKit.Fill(block.rectTransform);
                var w = Mathf.Min(660f, W - 40f);
                var panel = PanelAt(0f, 70f, w, 560f, new Color(UiKit.Panel.r, UiKit.Panel.g, UiKit.Panel.b, 1f));
                var title = UiKit.Title(panel, "Remove Ads + Co-op", 48, UiKit.Yellow);
                UiKit.Place(title.rectTransform, 0f, 26f, w - 40f, 70f);
                Line("•  No more ads", 0f, 112f, w - 120f, 40f, 28, UiKit.Ink, TextAnchor.MiddleLeft, panel);
                Line("•  Play Co-op online with a friend", 0f, 156f, w - 120f, 40f, 28, UiKit.Ink, TextAnchor.MiddleLeft, panel);
                Line("One-time purchase. Your friend needs it too to play Co-op.", 0f, 206f, w - 60f, 34f, 18, UiKit.Dim, TextAnchor.MiddleCenter, panel);
                var top = 70f;
                if (Monetization.Owned)
                {
                    Line("Unlocked. Thank you!", 0f, top + 262f, w - 60f, 44f, 28, UiKit.Green);
                }
                else
                {
                    Btn(Monetization.Busy ? "Please wait…" : $"Buy  {Monetization.Price}", 0f, top + 252f, 380f, 76f, Monetization.Buy, UiKit.Kind.Primary, 32,
                        !Monetization.Busy);
                }
                // iPhone only (Apple requires it). Owner, 2026-10-14: shown after the purchase too, so a reviewer whose test
                // account already owns it still finds it; only Buy goes away.
                if (Monetization.RestoreShown)
                    Btn("Restore purchase", 0f, top + 340f, 300f, 54f, Monetization.Restore, UiKit.Kind.Normal, 22, !Monetization.Busy);
                if (!string.IsNullOrEmpty(Monetization.Message))
                    Line(Monetization.Message, 0f, top + 404f, w - 60f, 40f, 20, UiKit.Amber);
                Btn("Close", 0f, top + 460f, 240f, 54f, () => _offer = false, UiKit.Kind.Quiet, 22);
            }

            private static Texture2D _glow;

            /// <summary>A soft round light (white at the middle, nothing at the rim), tinted where used.</summary>
            private static Texture2D Glow()
            {
                if (_glow != null) return _glow;
                const int size = 128;
                _glow = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "Glow" };
                var px = new Color32[size * size];
                for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var d = new Vector2((x + 0.5f) / size - 0.5f, (y + 0.5f) / size - 0.5f).magnitude * 2f;
                    var a = Mathf.Clamp01(1f - d);
                    px[y * size + x] = new Color(1f, 1f, 1f, a * a);
                }
                _glow.SetPixels32(px);
                _glow.Apply();
                return _glow;
            }

            private void Coop()
            {
                Heading("Co-op");
                Line("Two players online, or you and an AI teammate. Co-op is Hard: more zombies, more bosses.", 0f, 132f, Mathf.Min(900f, W - 80f), 30f, 19, UiKit.Dim);
                Btn("With an AI teammate", 0f, 190f, 460f, 66f, () =>
                {
                    _g.WithAi = true;
                    _g.GoTo(SessionState.StageSelect);
                }, UiKit.Kind.Primary, 26);
                var online = NetSession.OnlineAvailable;
                Btn(online ? "Host online (join code)" : "Host online — not set up yet", 0f, 272f, 460f, 66f, () =>
                {
                    _g.WithAi = false;
                    NetSession.Ensure().HostOnline();
                    _g.State = SessionState.OnlineLobby;
                }, UiKit.Kind.Normal, 24, online);
                Btn("Join a friend", 0f, 354f, 460f, 66f, () => _g.State = SessionState.Join, UiKit.Kind.Normal, 24);
                Back(SessionState.MainMenu, 470f);
                Message(546f);
            }

            private void Join()
            {
                Heading("Join a friend");
                Line("The join code your friend sent", 0f, 140f, 600f, 30f, 19, UiKit.Dim);
                var frame = PanelAt(-70f, 186f, 440f, 72f, UiKit.PanelDark);
                _code = UiKit.Node(frame, "Code").gameObject.AddComponent<InputField>();
                var text = UiKit.Label(_code.transform, "", 34, UiKit.Ink, TextAnchor.MiddleCenter);
                UiKit.Fill(text.rectTransform, 6f);
                var hint = UiKit.Label(_code.transform, "ABC123", 34, new Color(1f, 1f, 1f, 0.18f), TextAnchor.MiddleCenter);
                UiKit.Fill(hint.rectTransform, 6f);
                _code.textComponent = text;
                _code.placeholder = hint;
                _code.characterLimit = 12;
                _code.text = _g._joinText ?? "";
                _code.onValueChanged.AddListener(v => _g._joinText = v.ToUpperInvariant());
                UiKit.Fill((RectTransform)_code.transform);
                Btn("Paste", 230f, 194f, 150f, 58f, () =>
                {
                    _g._joinText = NetSession.CleanAddress(GUIUtility.systemCopyBuffer ?? "").ToUpperInvariant();
                    _code.text = _g._joinText;
                }, UiKit.Kind.Normal, 22);
                Btn("Join", 0f, 290f, 440f, 68f, () =>
                {
                    var t = (_g._joinText ?? "").Trim();
                    if (t.Length == 0) return;
                    NetSession.Ensure().JoinOnline(t);
                    _g.State = SessionState.OnlineLobby;
                }, UiKit.Kind.Primary, 28);
                Btn("Back", 0f, 400f, 300f, 54f, () => _g.State = SessionState.CoopSelect, UiKit.Kind.Quiet, 22);
            }

            /// <summary>The connection ended (or never started): back to Co-op with its message, as before.</summary>
            private bool LobbyGone()
            {
                var net = NetSession.Instance;
                if (net != null && (net.Role != NetRole.None || net.Busy)) return false;
                _g._menuMessage = net != null ? net.Status : "";
                _g.State = SessionState.CoopSelect;
                return true;
            }

            private void Lobby()
            {
                var net = NetSession.Instance;
                var hosting = net.Role == NetRole.Host || (net.Role == NetRole.None && net.Busy);
                Heading(hosting ? "Hosting a co-op game" : "Joining a co-op game");
                if (!string.IsNullOrEmpty(net.JoinCode))
                {
                    Line("Join code", 0f, 140f, 400f, 28f, 18, UiKit.Dim);
                    var plate = PanelAt(-60f, 170f, 360f, 76f, UiKit.PanelDark);
                    var code = UiKit.Title(plate, net.JoinCode, 44, UiKit.Amber);
                    UiKit.Fill(code.rectTransform);
                    var copy = Btn("Copy", 200f, 180f, 130f, 56f, () =>
                    {
                        GUIUtility.systemCopyBuffer = net.JoinCode;
                        _g._copiedUntil = Time.unscaledTime + 1.5f;
                    }, UiKit.Kind.Normal, 22);
                    _copy = copy.GetComponentInChildren<Text>();
                }
                _status = Line(net.Status, 0f, 262f, Mathf.Min(900f, W - 80f), 40f, 22, UiKit.Ink);
                if (net.Role == NetRole.Client)
                {
                    // The friend picks their own character here (the Host picks theirs before starting).
                    var list = _g.Tuning.characters;
                    var pick = Mathf.Clamp(net.LocalCharacter, 0, list.Length - 1);
                    var card = PanelAt(0f, 312f, 440f, 110f);
                    Portrait(card, list[pick].id, -150f, 6f, 110f, 98f);
                    Line(list[pick].displayName, 50f, 18f, 300f, 34f, 22, UiKit.Ink, TextAnchor.MiddleLeft, card);
                    Line(list[pick].description, 50f, 56f, 300f, 46f, 16, UiKit.Amber, TextAnchor.UpperLeft, card);
                    Btn("<", -280f, 336f, 64f, 64f, () => net.ChooseCharacter((pick + list.Length - 1) % list.Length));
                    Btn(">", 280f, 336f, 64f, 64f, () => net.ChooseCharacter((pick + 1) % list.Length));
                }
                if (net.Role == NetRole.Host)
                    Btn("Choose the stage", 0f, 330f, 380f, 66f, () => _g.GoTo(SessionState.StageSelect), UiKit.Kind.Primary, 26, net.PeerConnected);
                Btn("Leave", 0f, 450f, 300f, 54f, () =>
                {
                    net.Leave();
                    _g.GoTo(SessionState.CoopSelect);
                }, UiKit.Kind.Quiet, 22);
            }

            private void Missions()
            {
                var coop = _g.WithAi || OnlineHost;
                var settings = _g.Settings;
                var record = coop ? settings.completedCoop : settings.completedMissions;
                Heading(coop ? "Co-op · Hard" : "Solo · Normal", 34f, 34);
                Line(coop ? "Every mission is open" : "Finish a mission to open the next", 0f, 84f, 600f, 26f, 17, UiKit.Dim);
                // The full list (test and custom maps): development builds only.
                var allOpen = Gui.DevTools;
                if (!allOpen) _g._allStages = false;
                var listW = Mathf.Min(470f, W * 0.38f);
                var panelW = Mathf.Min(640f, W - listW - 90f);
                var left = -(listW + panelW + 20f) * 0.5f;
                var listX = left + listW * 0.5f;
                var panelX = left + listW + 20f + panelW * 0.5f;
                if (Gui.DevTools)
                    Btn(settings.unlockAllForTesting ? "Unlock all (test): ON" : "Unlock all (test): OFF", panelX + panelW * 0.5f - 120f, 118f, 240f, 42f, () =>
                    {
                        settings.unlockAllForTesting = !settings.unlockAllForTesting;
                        _g.SaveSettings();
                    }, UiKit.Kind.Quiet, 16);
                if (allOpen)
                {
                    Btn("Story", listX - listW * 0.25f, 118f, listW * 0.5f - 6f, 42f, () => _g._allStages = false, _g._allStages ? UiKit.Kind.Quiet : UiKit.Kind.Normal, 18);
                    Btn("All stages", listX + listW * 0.25f, 118f, listW * 0.5f - 6f, 42f, () => _g._allStages = true, _g._allStages ? UiKit.Kind.Normal : UiKit.Kind.Quiet, 18);
                }
                var missions = Campaign.Missions;
                if (_g._allStages || missions.Count == 0)
                {
                    AllStages();
                    Back(OnlineHost ? SessionState.OnlineLobby : _g.WithAi ? SessionState.CoopSelect : SessionState.MainMenu);
                    return;
                }
                // Owner, 2026-10-07: Co-op has no tutorial. Each row is a mission index (-1: Endless).
                var shown = Campaign.Listed(coop);
                var endlessRow = StageLibrary.Exists(Endless.StageId) ? shown.Count : -1;
                var rows = shown.Count + (endlessRow >= 0 ? 1 : 0);
                _g._storyIndex = Mathf.Clamp(_g._storyIndex, 0, rows - 1);
                var step = Mathf.Min(66f, 452f / rows);
                for (var r = 0; r < rows; r++)
                {
                    var isEndless = r == endlessRow;
                    var i = isEndless ? -1 : shown[r];
                    var selected = r == _g._storyIndex;
                    var locked = !isEndless && !Campaign.Unlocked(i, settings, coop);
                    var done = !isEndless && record.Contains(missions[i].id);
                    var row = r;
                    var b = UiKit.Button(_screen, "", () => _g._storyIndex = row, selected ? UiKit.Kind.Normal : UiKit.Kind.Quiet);
                    UiKit.Place((RectTransform)b.transform, listX, 172f + r * step, listW, step - 6f);
                    if (selected) UiKit.Frame(b.transform, UiKit.Green);
                    var title = isEndless ? "∞ Endless" : missions[i].title;
                    var tag = isEndless ? "Survive the waves · " + (coop ? "Co-op" : "Solo") : missions[i].tag;
                    var name = UiKit.Label(b.transform, title, step < 56f ? 18 : 21, locked ? UiKit.Dim : UiKit.Ink, TextAnchor.UpperLeft);
                    UiKit.Fill(name.rectTransform);
                    name.rectTransform.offsetMin = new Vector2(16f, (step - 6f) * 0.42f);
                    name.rectTransform.offsetMax = new Vector2(-70f, -4f);
                    var sub = UiKit.Label(b.transform, tag, step < 56f ? 13 : 15, locked ? UiKit.Dim : UiKit.Amber, TextAnchor.LowerLeft);
                    UiKit.Fill(sub.rectTransform);
                    sub.rectTransform.offsetMin = new Vector2(16f, 4f);
                    sub.rectTransform.offsetMax = new Vector2(-70f, -(step - 6f) * 0.5f);
                    if (locked || done)
                    {
                        var badge = UiKit.Label(b.transform, locked ? "LOCKED" : "✓", locked ? 13 : 26, locked ? UiKit.Dim : UiKit.Green, TextAnchor.MiddleRight);
                        UiKit.Fill(badge.rectTransform);
                        badge.rectTransform.offsetMax = new Vector2(-14f, 0f);
                    }
                }
                var panel = PanelAt(panelX, 172f, panelW, 446f);
                if (_g._storyIndex == endlessRow) EndlessBriefing(panel, panelW, coop);
                else MissionBriefing(panel, panelW, missions, shown[_g._storyIndex], record, coop);
                Back(OnlineHost ? SessionState.OnlineLobby : _g.WithAi ? SessionState.CoopSelect : SessionState.MainMenu);
            }

            private void MissionBriefing(RectTransform panel, float w, IReadOnlyList<CampaignMission> missions, int index, List<string> record, bool coop)
            {
                var mission = missions[index];
                var title = UiKit.Title(panel, mission.title, 30, UiKit.Ink, TextAnchor.MiddleLeft);
                UiKit.Fit(title, 18);
                UiKit.Place(title.rectTransform, 0f, 16f, w - 48f, 46f);
                Line(mission.tag, 0f, 64f, w - 48f, 26f, 16, UiKit.Amber, TextAnchor.MiddleLeft, panel);
                Line(mission.story, 0f, 100f, w - 48f, 200f, 19, new Color(0.88f, 0.9f, 0.85f), TextAnchor.UpperLeft, panel);
                var locked = !Campaign.Unlocked(index, _g.Settings, coop);
                if (locked)
                {
                    Line($"Finish \"{missions[index - 1].title}\" in Solo to open this mission.", 0f, 330f, w - 48f, 40f, 18, UiKit.Amber, TextAnchor.MiddleCenter, panel);
                    return;
                }
                var next = Campaign.NextStage(mission, _g.Settings);
                if (next == null) return;
                Line($"Map: {StageLibrary.DisplayName(next)}", 0f, 312f, w - 48f, 26f, 16, new Color(0.7f, 0.85f, 0.72f), TextAnchor.MiddleLeft, panel);
                var play = UiKit.Button(panel, record.Contains(mission.id) ? "Play again" : "Play", () =>
                {
                    _g.Settings.SetLastPlayed(mission.id, next);
                    SettingsStore.Save(_g.Settings);
                    _g._stageId = next;
                    _g.GoTo(SessionState.CharacterSelect);
                }, UiKit.Kind.Primary, 30);
                UiKit.Place((RectTransform)play.transform, 0f, 352f, 320f, 72f);
            }

            private void EndlessBriefing(RectTransform panel, float w, bool coop)
            {
                var t = _g.Tuning.endless;
                var title = UiKit.Title(panel, "∞ Endless", 30, UiKit.Ink, TextAnchor.MiddleLeft);
                UiKit.Place(title.rectTransform, 0f, 16f, w - 48f, 46f);
                var rules = $"The largest map, fenced in. You have {Mathf.RoundToInt(t.prepSeconds / 60f)} minutes to collect equipment, then the zombies " +
                            "pour in through the gaps in the fence: more of them, and tougher ones, the longer you last. Day, rain, night and " +
                            $"stormy night rain come in turn. Your backpack holds ×{t.backpackMultiplier:0.#} and every ammo pickup gives {Mathf.RoundToInt((t.ammoMultiplier - 1f) * 100f)} % more. " +
                            "How long can you survive?";
                Line(rules, 0f, 72f, w - 48f, 220f, 19, new Color(0.88f, 0.9f, 0.85f), TextAnchor.UpperLeft, panel);
                var best = coop ? _g.Settings.endlessBestCoop : _g.Settings.endlessBestSolo;
                Line(best > 0f ? $"Best ({(coop ? "Co-op" : "Solo")}): {Endless.Minutes(best)}" : "No run yet", 0f, 312f, w - 48f, 26f, 17,
                    new Color(0.7f, 0.85f, 0.72f), TextAnchor.MiddleLeft, panel);
                var play = UiKit.Button(panel, "Play", () =>
                {
                    _g._stageId = Endless.StageId;
                    _g.GoTo(SessionState.CharacterSelect);
                }, UiKit.Kind.Primary, 30);
                UiKit.Place((RectTransform)play.transform, 0f, 352f, 320f, 72f);
            }

            private void AllStages()
            {
                var coop = _g.WithAi || OnlineHost;
                var ids = new List<string>();
                foreach (var id in StageLibrary.Ids)
                    if (StageLibrary.Exists(id) && !(coop && Campaign.MissionOf(id)?.id == Campaign.TutorialId)) ids.Add(id);
                var cols = W > 1100f ? 3 : 2;
                var cw = Mathf.Min(340f, (W - 80f) / cols - 10f);
                for (var i = 0; i < ids.Count; i++)
                {
                    var id = ids[i];
                    var col = i % cols;
                    var line = i / cols;
                    Btn(StageLibrary.DisplayName(id), (col - (cols - 1) * 0.5f) * (cw + 10f), 176f + line * 58f, cw, 50f, () =>
                    {
                        _g._stageId = id;
                        _g.GoTo(SessionState.CharacterSelect);
                    }, UiKit.Kind.Normal, 17);
                }
            }

            private void Characters()
            {
                var list = _g.Tuning.characters;
                _g._character = Mathf.Clamp(_g._character, 0, list.Length - 1);
                Heading("Choose your character", 26f, 38);
                if (_g.WithAi) Line("The AI teammate takes a different one", 0f, 88f, 600f, 24f, 17, UiKit.Dim);
                // Owner, 2026-10-08: each character's panel has their picture on the left and their attributes on the right;
                // four panels, two by two.
                const float gap = 16f;
                var cardW = Mathf.Min(560f, (W - 70f - gap) * 0.5f);
                const float cardH = 196f;
                float maxHp = 0f, maxAtk = 0f, maxSpd = 0f;
                foreach (var c in list)
                {
                    maxHp = Mathf.Max(maxHp, _g.Tuning.player.maxHp * (1f + c.hpBonus));
                    maxAtk = Mathf.Max(maxAtk, 1f + c.attackBonus);
                    maxSpd = Mathf.Max(maxSpd, _g.Tuning.player.moveSpeed * (1f + c.speedBonus));
                }
                for (var i = 0; i < list.Length; i++)
                {
                    var c = list[i];
                    var index = i;
                    var selected = i == _g._character;
                    var col = i % 2;
                    var line = i / 2;
                    var card = UiKit.Button(_screen, "", () => _g._character = index, selected ? UiKit.Kind.Normal : UiKit.Kind.Quiet);
                    var rt = UiKit.Place((RectTransform)card.transform, (col - 0.5f) * (cardW + gap), 120f + line * (cardH + gap), cardW, cardH);
                    if (selected) UiKit.Frame(card.transform, UiKit.Green);
                    // The picture, on a darker plate, on the left.
                    var plateW = Mathf.Min(176f, cardW * 0.34f);
                    var plate = UiKit.Box(rt, "Picture plate", UiKit.PanelDark);
                    UiKit.Place(plate.rectTransform, -cardW * 0.5f + 12f + plateW * 0.5f, 12f, plateW, cardH - 30f);
                    plate.raycastTarget = false;
                    Portrait(plate.rectTransform, c.id, 0f, 4f, plateW - 8f, cardH - 38f);
                    // The attributes, on the right.
                    var infoW = cardW - plateW - 44f;
                    var infoX = -cardW * 0.5f + plateW + 28f + infoW * 0.5f;
                    var name = UiKit.Label(rt, c.displayName, 25, UiKit.Ink, TextAnchor.MiddleLeft);
                    UiKit.Place(name.rectTransform, infoX, 12f, infoW, 32f);
                    StatRow(rt, infoX, infoW, 0, "HP", _g.Tuning.player.maxHp * (1f + c.hpBonus) / maxHp, $"{_g.Tuning.player.maxHp * (1f + c.hpBonus):0}", c.hpBonus > 0f);
                    StatRow(rt, infoX, infoW, 1, "Attack", (1f + c.attackBonus) / maxAtk, $"{100f * (1f + c.attackBonus):0} %", c.attackBonus > 0f);
                    StatRow(rt, infoX, infoW, 2, "Speed", _g.Tuning.player.moveSpeed * (1f + c.speedBonus) / maxSpd, $"{_g.Tuning.player.moveSpeed * (1f + c.speedBonus):0.0} m/s", c.speedBonus > 0f);
                    var perk = UiKit.Label(rt, c.description, 16, UiKit.Amber, TextAnchor.UpperLeft);
                    UiKit.Place(perk.rectTransform, infoX, 146f, infoW, 40f);
                }
                Btn("Start", 110f, 548f, 320f, 72f, () => _g.StartStage(StageLibrary.Load(_g._stageId), list[_g._character], System.Environment.TickCount, _g.WithAi, online: OnlineHost, intro: true),
                    UiKit.Kind.Primary, 32);
                Btn("Back", -200f, 556f, 220f, 60f, () => _g.GoTo(SessionState.StageSelect), UiKit.Kind.Quiet, 22);
            }

            /// <summary>The owner's picture of a character (Resources/UI/Portraits), fitted inside a box.</summary>
            private static void Portrait(Transform parent, string characterId, float x, float top, float w, float h)
            {
                var sprite = Resources.Load<Sprite>("UI/Portraits/" + PortraitOf(characterId));
                if (sprite == null) return;
                var image = UiKit.Node(parent, "Portrait").gameObject.AddComponent<Image>();
                image.sprite = sprite;
                image.preserveAspect = true;
                image.raycastTarget = false;
                UiKit.Place(image.rectTransform, x, top, w, h);
            }

            /// <summary>Which of the owner's pictures is which character (char01-04, the four slots in order).</summary>
            public static string PortraitOf(string characterId)
            {
                switch (characterId)
                {
                    case "character_01": return "char01";
                    case "character_02": return "char02";
                    case "protagonist": return "char03";
                    case "character_04": return "char04";
                    default: return "char03";
                }
            }

            /// <summary>One stat on a card: its name, a bar against the best of the four, and the value (green when boosted).</summary>
            private static void StatRow(RectTransform card, float x, float w, int row, string name, float k, string value, bool boosted)
            {
                var top = 50f + row * 32f;
                var n = UiKit.Label(card, name, 16, UiKit.Dim, TextAnchor.MiddleLeft);
                UiKit.Place(n.rectTransform, x - w * 0.5f + 45f, top, 90f, 26f);
                var bar = UiKit.Bar(card, k, boosted ? UiKit.Green : new Color(0.7f, 0.82f, 0.95f));
                UiKit.Place(bar, x + 22f, top + 6f, w - 190f, 14f);
                var v = UiKit.Label(card, value, 16, boosted ? UiKit.Green : UiKit.Ink, TextAnchor.MiddleRight);
                UiKit.Place(v.rectTransform, x + w * 0.5f - 45f, top, 90f, 26f);
            }
        }
    }
}
