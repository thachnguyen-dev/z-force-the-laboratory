using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ZombieGame
{
    /// <summary>
    /// The local player's controls and HUD (⚠ placeholder IMGUI). Runs before the characters each frame: it reads
    /// touch or keyboard/mouse, handles the weapon panel and inventory screen, and hands a <see cref="CharacterInput"/>
    /// to the local <see cref="Survivor"/>.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class PlayerHud : MonoBehaviour, ICharacterInputSource
    {
        public Survivor Player;
        public Camera Camera;

        /// <summary>Touch layout (Android) vs keyboard and mouse (PC), by platform (owner, 2026-10-08: no switching).</summary>
        public bool TouchMode;

        private CharacterInput _input;

        /// <summary>Development automation only: rewrites this frame's input (capture runs).</summary>
        public System.Func<CharacterInput, CharacterInput> DevOverride;
        private Vector2 _lastAim = new Vector2(0f, 1f);
        /// <summary>Dragging the fire stick this far (screen units) throws a grenade at its full range. PLACEHOLDER.</summary>
        public const float ThrowDragLength = 170f;

        // Pointer ownership: each finger belongs to one control from the moment it lands.
        private int _movePointer = int.MinValue;
        private int _firePointer = int.MinValue;
        private int _aimPointer = int.MinValue;
        private Vector2 _aimStart;
        private Vector2 _fireStart;
        private Vector2 _moveVector;
        private bool _fireHeld;
        private bool _fireHeldLast;

        // Panels.
        private bool _weaponPanel;
        private bool _inventory;
        private int _uiPointer = int.MinValue;
        private Vector2 _uiStart;
        private int _uiItem = -1;
        private bool _uiFromWeaponPanel;
        private AmmoType _dialogAmmo = AmmoType.None;
        private int _dialogAmount;
        private bool _sliderDrag;

        private string _toast;
        private float _toastUntil;

        private static readonly Slot[] PanelSlots = { Slot.Pistol, Slot.LongGun, Slot.Knife, Slot.Grenade };

        private void Start()
        {
            TouchMode = Application.isMobilePlatform;
            if (Player != null)
            {
                Player.Message += Toast;
                Player.ChestOpened += OpenChest;
            }
        }

        private void OnDestroy()
        {
            if (Player != null)
            {
                Player.Message -= Toast;
                Player.ChestOpened -= OpenChest;
            }
        }

        public void Toast(string text)
        {
            _toast = text;
            _toastUntil = Time.unscaledTime + 1.6f;
        }

        public CharacterInput Read(Survivor self) => _input;

        // ---------------------------------------------------------------- input

        private void Update()
        {
            _input = default;
            _input.GrenadeDistance = -1f;
            _input.ThrowOnRelease = false;
            if (Player == null || GameSession.Instance == null || !GameSession.Instance.Playing) return;
            DropLostFingers();
            var pointerOverUi = HandleQuit();
            // Paused (Solo): nothing else reads input.
            if (Paused) return;
            pointerOverUi |= HandlePanels();
            if (TouchMode) ReadTouch();
            else ReadKeyboardMouse(pointerOverUi);

            _input.FireHeld = _fireHeld;
            _input.FirePressed = _fireHeld && !_fireHeldLast;
            _input.FireReleased = !_fireHeld && _fireHeldLast;
            _fireHeldLast = _fireHeld;
            _input.Aim = _lastAim;
            if (DevOverride != null) _input = DevOverride(_input);
        }

        private SettingsData Prefs => GameSession.Instance.Settings;

        private void ReadKeyboardMouse(bool pointerOverUi)
        {
            var k = Keyboard.current;
            var mouse = Mouse.current;
            var s = Prefs;
            var move = Vector2.zero;
            // Rebindable keys (owner, 2026-09-29); the arrow keys always move as well.
            if (Controls.Held(PcAction.MoveUp, s) || (k != null && k.upArrowKey.isPressed)) move.y += 1f;
            if (Controls.Held(PcAction.MoveDown, s) || (k != null && k.downArrowKey.isPressed)) move.y -= 1f;
            if (Controls.Held(PcAction.MoveRight, s) || (k != null && k.rightArrowKey.isPressed)) move.x += 1f;
            if (Controls.Held(PcAction.MoveLeft, s) || (k != null && k.leftArrowKey.isPressed)) move.x -= 1f;
            if (Controls.Pressed(PcAction.WeaponPanel, s)) ToggleWeaponPanel();
            if (Controls.Pressed(PcAction.Inventory, s)) ToggleInventory();
            _input.Reload = Controls.Pressed(PcAction.Reload, s);
            // Bomb stages: next to the target, the Pickup key plants the C4 (owner, 2026-10-02: a Plant action there).
            var pickupPressed = Controls.Pressed(PcAction.Pickup, s);
            if (pickupPressed && CanPlant) Player.PlantC4();
            else if (pickupPressed && CanRescue) Talk();
            else _input.Pickup = pickupPressed;
            _input.Move = move.sqrMagnitude > 1f ? move.normalized : move;

            if (mouse == null || Camera == null) return;
            // Aim follows the cursor (no aim assist on PC, D-013).
            var ray = Camera.ScreenPointToRay(mouse.position.ReadValue());
            var plane = new Plane(Vector3.up, new Vector3(0f, 1f, 0f));
            if (plane.Raycast(ray, out var enter))
            {
                var point = ray.GetPoint(enter);
                var to = point - Player.transform.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.04f) _lastAim = new Vector2(to.x, to.z).normalized;
                _input.GrenadeDistance = to.magnitude;
            }
            // A mouse-button Fire is ignored while the pointer is on an open panel; a keyboard Fire isn't.
            var mouseFire = Controls.IsMouse(PcAction.Fire, s);
            _fireHeld = Controls.Held(PcAction.Fire, s) && (!mouseFire || (!pointerOverUi && _uiPointer != PointerInput.MouseId));
        }

        private List<TouchButton> _layout;
        private SettingsData _layoutFrom;
        private int _layoutVersion = -1;

        /// <summary>The saved Android layout (D-017), re-read whenever the settings change.</summary>
        private List<TouchButton> Layout
        {
            get
            {
                if (_layout == null || _layoutFrom != Prefs || _layoutVersion != GameSession.Instance.SettingsVersion)
                {
                    _layout = Controls.Layout(Prefs);
                    _layoutFrom = Prefs;
                    _layoutVersion = GameSession.Instance.SettingsVersion;
                }
                return _layout;
            }
        }

        private Vector2 _moveOrigin;

        /// <summary>Buttons moved onto the left half in the touch layout editor keep working there.</summary>
        private bool LeftSideButton(Vector2 p)
        {
            foreach (var b in Layout)
                if (b.id != Controls.Move && Vector2.Distance(p, Controls.Center(b, Gui.Width)) < Controls.Radius(b) + 4f) return true;
            return false;
        }

        private bool Hit(string id, Vector2 p, float extra = 4f)
        {
            var b = Controls.Find(Layout, id);
            return Vector2.Distance(p, Controls.Center(b, Gui.Width)) < Controls.Radius(b) + extra;
        }

        private void ReadTouch()
        {
            var move = Controls.Find(Layout, Controls.Move);
            var moveRadius = Controls.Radius(move);

            _fireHeld = false;
            _moveVector = Vector2.zero;
            foreach (var p in PointerInput.Current)
            {
                if (p.Id == _uiPointer || p.Id == _quitPointer) continue;
                if (p.Began)
                {
                    if (p.Position.x < Gui.Width * 0.5f && !LeftSideButton(p.Position))
                    {
                        // Owner, 2026-10-02: the whole left half moves. The stick appears under the thumb.
                        _movePointer = p.Id;
                        _moveOrigin = p.Position;
                    }
                    else if (Hit(Controls.Fire, p.Position))
                    {
                        _firePointer = p.Id;
                        _fireStart = p.Position;
                    }
                    else if (Hit(Controls.Reload, p.Position)) _input.Reload = true;
                    else if (CanPlant && Hit(Controls.Pickup, p.Position)) Player.PlantC4();
                    else if (CanRescue && Hit(Controls.Pickup, p.Position)) Talk();
                    else if (CanPickup && Hit(Controls.Pickup, p.Position)) _input.Pickup = true;
                    else if (Hit(Controls.Weapon, p.Position)) ToggleWeaponPanel();
                    else if (Hit(Controls.Bag, p.Position)) ToggleInventory();
                    else
                    {
                        _aimPointer = p.Id;
                        _aimStart = p.Position;
                    }
                }

                if (p.Id == _movePointer)
                {
                    var offset = p.Position - _moveOrigin;
                    // Dragging past the rim pulls the stick along, so the thumb never runs out of room.
                    if (offset.magnitude > moveRadius) _moveOrigin = p.Position - offset.normalized * moveRadius;
                    var v = (p.Position - _moveOrigin) / moveRadius;
                    _moveVector = Vector2.ClampMagnitude(new Vector2(v.x, -v.y), 1f);
                    if (p.Ended) _movePointer = int.MinValue;
                }
                else if (p.Id == _firePointer)
                {
                    _fireHeld = !p.Ended;
                    // Owner, 2026-09-30: sliding the thumb on FIRE keeps aiming while firing.
                    var drag = p.Position - _fireStart;
                    if (drag.magnitude > 14f) _lastAim = new Vector2(drag.x, -drag.y).normalized;
                    // A grenade: how far the stick is dragged is how far it goes (owner, 2026-10-07: the throw point
                    // freely controlled while holding); letting go throws.
                    if (Player != null && Player.Selected == Slot.Grenade)
                    {
                        var g = GameSession.Instance.Tuning.grenade;
                        _input.GrenadeDistance = Mathf.Lerp(g.minRange, g.maxRange, Mathf.Clamp01(drag.magnitude / ThrowDragLength));
                        _input.ThrowOnRelease = true;
                    }
                    if (p.Ended) _firePointer = int.MinValue;
                }
                else if (p.Id == _aimPointer)
                {
                    // Drag anywhere else to aim (D-009): the drag direction is the aim direction.
                    var d = p.Position - _aimStart;
                    if (d.magnitude > 14f) _lastAim = new Vector2(d.x, -d.y).normalized;
                    if (p.Ended) _aimPointer = int.MinValue;
                }
            }
            if (_moveVector.magnitude < 0.12f) _moveVector = Vector2.zero;
            _input.Move = _moveVector;
            _input.AimAssist = true;
        }


        private LootItem NearLoot => Player != null && !Player.IsDead ? Player.NearestLoot() : null;
        private Chest NearChest => Player != null && !Player.IsDead ? Player.NearestChest() : null;
        private bool CanPickup => NearLoot != null || NearChest != null;
        private bool CanPlant => Player != null && Player.CanPlantC4;
        private bool CanRescue => Player != null && Player.CanFreeHostage;

        /// <summary>The line of the scientist's dialogue showing (-1: not talking).</summary>
        private int _talk = -1;

        /// <summary>
        /// Owner, 2026-10-08: next to the scientist the button talks: the first press opens the dialogue, each press the next
        /// line, and after the last the scientist follows (the Host frees them). Walking away ends it.
        /// </summary>
        private void Talk()
        {
            _talk++;
            if (_talk < HostageBrain.Dialogue.Length) return;
            _talk = -1;
            Player.FreeHostage();
        }

        private void DrawDialogue()
        {
            if (!CanRescue) _talk = -1;
            if (_talk < 0) return;
            var (who, line) = HostageBrain.Dialogue[_talk];
            var panel = new Rect(Gui.Width * 0.5f - 420f, Gui.Height - 270f, 840f, 118f);
            Gui.Panel(panel, new Color(0f, 0f, 0f, 0.72f));
            Gui.Frame(panel, 2f, new Color(0.5f, 0.85f, 1f, 0.6f));
            Gui.Label(new Rect(panel.x + 22f, panel.y + 10f, panel.width - 44f, 30f), who, 20, TextAnchor.MiddleLeft,
                who == "You" ? new Color(1f, 0.9f, 0.5f) : new Color(0.55f, 0.9f, 1f), true);
            Gui.Label(new Rect(panel.x + 22f, panel.y + 42f, panel.width - 44f, 66f), line, 20, TextAnchor.UpperLeft, Color.white, true);
            Gui.Label(new Rect(panel.xMax - 220f, panel.yMax - 30f, 200f, 24f), $"{_talk + 1}/{HostageBrain.Dialogue.Length}", 14, TextAnchor.MiddleRight, Color.gray);
        }

        private void ToggleWeaponPanel()
        {
            _weaponPanel = !_weaponPanel;
            _chest = null;
            if (_weaponPanel) _inventory = false;
            _dialogAmmo = AmmoType.None;
        }

        /// <summary>Opens the inventory (development captures).</summary>
        public void OpenInventory()
        {
            if (!_inventory) ToggleInventory();
        }

        private void ToggleInventory()
        {
            _inventory = !_inventory;
            _chest = null;
            if (_inventory) _weaponPanel = false;
            _dialogAmmo = AmmoType.None;
        }


        // ---------------------------------------------------------------- lost fingers

        /// <summary>
        /// Owner, 2026-10-12: after pausing and watching an ad, the inventory took no touch at all. Each control owns the
        /// finger that landed on it until that finger lifts; a full-screen ad (or leaving the game) takes the screen
        /// mid-touch, so the lift never arrives and the control kept waiting for that finger, ignoring every new one.
        /// A finger that is no longer on the screen lets go of whatever held it.
        /// </summary>
        private void DropLostFingers()
        {
            var live = PointerInput.Current;
            bool On(int id)
            {
                if (id == int.MinValue) return true;
                foreach (var p in live) if (p.Id == id) return true;
                return false;
            }
            if (!On(_movePointer)) { _movePointer = int.MinValue; _moveVector = Vector2.zero; }
            if (!On(_firePointer)) { _firePointer = int.MinValue; _fireHeld = false; }
            if (!On(_aimPointer)) _aimPointer = int.MinValue;
            if (!On(_quitPointer)) _quitPointer = int.MinValue;
            if (!On(_uiPointer)) { _uiPointer = int.MinValue; _sliderDrag = false; }
        }

        /// <summary>Back from an ad or the background: every control lets go (no touch survives it).</summary>
        private void OnApplicationFocus(bool focused)
        {
            if (!focused) return;
            _movePointer = _firePointer = _aimPointer = _quitPointer = _uiPointer = int.MinValue;
            _moveVector = Vector2.zero;
            _fireHeld = false;
            _sliderDrag = false;
        }

        // ---------------------------------------------------------------- quitting (D-193) and pausing

        private bool _quitAsked;

        /// <summary>
        /// Owner, 2026-10-08: Solo pauses (the top-left button and Esc): the game and its sound stop, Resume or Leave
        /// stage; pausing shows the full-screen ad when one is due (Monetization). Online Co-op can't stop a shared game:
        /// there the button asks before leaving while the game keeps running (D-193).
        /// </summary>
        public static bool CanPause
        {
            get
            {
                var net = NetSession.Instance;
                return net == null || net.Role == NetRole.None;
            }
        }

        /// <summary>The game is paused (Solo).</summary>
        public bool Paused => _quitAsked && CanPause;

        private void SetAsked(bool asked)
        {
            if (_quitAsked == asked) return;
            _quitAsked = asked;
            if (!CanPause) return;
            Freeze(asked);
            if (asked) Monetization.TryShowPauseAd();
        }

        private static void Freeze(bool on)
        {
            Time.timeScale = on ? 0f : 1f;
            AudioListener.pause = on;
        }

        private void OnDisable()
        {
            // Never leave the game frozen (leaving the stage, the HUD going away).
            if (_quitAsked && CanPause) Freeze(false);
            _quitAsked = false;
        }
        private int _quitPointer = int.MinValue;
        private Rect QuitButton => new Rect(20f, 140f, 90f, 40f);
        private Rect QuitDialog => new Rect(Gui.Width * 0.5f - 260f, 220f, 520f, 200f);
        private Rect QuitYes => new Rect(QuitDialog.x + 30f, QuitDialog.yMax - 76f, 210f, 56f);
        private Rect QuitNo => new Rect(QuitDialog.xMax - 240f, QuitDialog.yMax - 76f, 210f, 56f);

        /// <summary>
        /// The Quit button (touch/mouse) or Esc asks first; the game keeps running meanwhile (there is no pause). Leaving
        /// loses the stage's progress (D-193).
        /// </summary>
        private bool HandleQuit()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) SetAsked(!_quitAsked);
            var busy = false;
            foreach (var p in PointerInput.Current)
            {
                var over = QuitButton.Contains(p.Position) || (_quitAsked && QuitDialog.Contains(p.Position));
                if (p.Began && over) _quitPointer = p.Id;
                if (p.Id != _quitPointer) continue;
                busy = true;
                if (!p.Ended) continue;
                _quitPointer = int.MinValue;
                if (QuitButton.Contains(p.Position)) SetAsked(!_quitAsked);
                else if (_quitAsked && QuitNo.Contains(p.Position)) SetAsked(false);
                else if (_quitAsked && QuitYes.Contains(p.Position))
                {
                    SetAsked(false);
                    GameSession.Instance.GoTo(SessionState.MainMenu);
                    return true;
                }
            }
            return busy;
        }

        private void DrawQuit()
        {
            Gui.Panel(QuitButton, new Color(0f, 0f, 0f, 0.45f));
            var pause = CanPause;
            Gui.Label(QuitButton, pause ? (TouchMode ? "Pause" : "Esc  Pause") : (TouchMode ? "Quit" : "Esc  Quit"), 16, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.8f));
            if (!_quitAsked) return;
            if (pause) Gui.Fill(new Rect(0f, 0f, Gui.Width, Gui.Height), new Color(0.02f, 0.05f, 0.1f, 0.55f));
            var r = QuitDialog;
            Gui.Panel(r, new Color(0.16f, 0.27f, 0.4f, 0.97f));
            Gui.Shadowed(new Rect(r.x, r.y + 14f, r.width, 40f), pause ? "Paused" : "Leave the stage?", pause ? 34 : 26, TextAnchor.MiddleCenter, Color.white, true);
            Gui.Label(new Rect(r.x + 20f, r.y + 58f, r.width - 40f, 50f),
                pause ? "Leaving loses everything found in this stage." : "Everything found in this stage is lost. The game keeps running.", 16,
                TextAnchor.MiddleCenter, Color.gray);
            Gui.Panel(QuitYes, new Color(0.85f, 0.3f, 0.25f, 1f));
            Gui.Shadowed(new Rect(QuitYes.x, QuitYes.y, QuitYes.width, QuitYes.height - 6f), pause ? "Leave stage" : "Leave", 22, TextAnchor.MiddleCenter, Color.white, true);
            Gui.Panel(QuitNo, pause ? new Color(1f, 0.8f, 0.14f, 1f) : new Color(0.24f, 0.6f, 0.95f, 1f));
            Gui.Shadowed(new Rect(QuitNo.x, QuitNo.y, QuitNo.width, QuitNo.height - 6f), pause ? "Resume" : "Stay", 22, TextAnchor.MiddleCenter, Color.white, true);
        }
        // ---------------------------------------------------------------- panels

        private Rect WeaponPanelRect => new Rect(Gui.Width * 0.5f - 275f, 430f, 550f, 150f);
        private Rect WeaponTile(int i) => new Rect(WeaponPanelRect.x + 10f + i * 135f, WeaponPanelRect.y + 10f, 125f, 130f);
        private Rect InventoryRect => new Rect(Gui.Width * 0.5f - 290f, 80f, 580f, 440f);
        private Rect ListRow(int i) => new Rect(InventoryRect.x + 15f, InventoryRect.y + 80f + i * 58f, InventoryRect.width - 30f, 52f);

        /// <summary>Owner, 2026-10-12: the inventory as a grid of square slots, four to a row.</summary>
        private const int SlotColumns = 4;
        private const float SlotSize = 120f, SlotGap = 14f;

        private Rect InventorySlot(int i)
        {
            var r = InventoryRect;
            var left = r.x + (r.width - (SlotColumns * SlotSize + (SlotColumns - 1) * SlotGap)) * 0.5f;
            return new Rect(left + (i % SlotColumns) * (SlotSize + SlotGap), r.y + 80f + (i / SlotColumns) * (SlotSize + SlotGap), SlotSize, SlotSize);
        }
        private Rect InventoryClose => new Rect(InventoryRect.xMax - 60f, InventoryRect.y + 10f, 50f, 44f);
        private Rect DialogRect => new Rect(Gui.Width * 0.5f - 260f, 190f, 520f, 230f);
        private Rect SliderTrack => new Rect(DialogRect.x + 40f, DialogRect.y + 100f, DialogRect.width - 80f, 30f);
        private Rect DialogDrop => new Rect(DialogRect.x + 40f, DialogRect.yMax - 70f, 200f, 52f);
        private Rect DialogCancel => new Rect(DialogRect.xMax - 240f, DialogRect.yMax - 70f, 200f, 52f);

        private enum InvRow { LongGun, ShotgunAmmo, ArAmmo, RifleAmmo, SmgAmmo, Health, Grenade, C4, Vaccine }
        private readonly InvRow[] _rows = new InvRow[9];
        private int _rowCount;

        private void BuildRows()
        {
            _rowCount = 0;
            var inv = Player.Inventory;
            // Owner, 2026-10-12: weapons aren't shown in the inventory (they're on the weapon panel).
            if (inv.Ammo(AmmoType.Shotgun) > 0) _rows[_rowCount++] = InvRow.ShotgunAmmo;
            if (inv.Ammo(AmmoType.AR) > 0) _rows[_rowCount++] = InvRow.ArAmmo;
            if (inv.Ammo(AmmoType.Rifle) > 0) _rows[_rowCount++] = InvRow.RifleAmmo;
            if (inv.Ammo(AmmoType.SMG) > 0) _rows[_rowCount++] = InvRow.SmgAmmo;
            if (inv.HealthItems > 0) _rows[_rowCount++] = InvRow.Health;
            if (inv.Grenades > 0) _rows[_rowCount++] = InvRow.Grenade;
            if (inv.C4) _rows[_rowCount++] = InvRow.C4;
            if (inv.Vaccine) _rows[_rowCount++] = InvRow.Vaccine;
        }

        private static readonly System.Collections.Generic.Dictionary<InvRow, Texture2D> Icons = new System.Collections.Generic.Dictionary<InvRow, Texture2D>();

        /// <summary>The item's picture (Resources/UI/Icons, rendered from its 3D model: CaptureTests.RenderTheItemIcons).</summary>
        private static Texture2D ItemIcon(InvRow row)
        {
            if (Icons.TryGetValue(row, out var t)) return t;
            var data = row switch
            {
                InvRow.Health => LootData.Single(LootKind.Health),
                InvRow.Grenade => LootData.Single(LootKind.Grenade),
                InvRow.C4 => LootData.Single(LootKind.C4),
                InvRow.Vaccine => LootData.Single(LootKind.Vaccine),
                _ => LootData.AmmoBox(RowAmmo(row), 1),
            };
            var (id, _, _) = LootItem.ModelFor(data);
            t = id != null ? Resources.Load<Texture2D>("UI/Icons/" + id) : null;
            Icons[row] = t;
            return t;
        }

        private static string ItemName(InvRow row) => row switch
        {
            InvRow.Health => "Health",
            InvRow.Grenade => "Grenade",
            InvRow.C4 => "C4",
            InvRow.Vaccine => "Vaccine",
            InvRow.ShotgunAmmo => "Shotgun",
            InvRow.ArAmmo => "AR",
            InvRow.RifleAmmo => "Sniper",
            InvRow.SmgAmmo => "SMG",
            _ => "",
        };

        private int ItemCount(InvRow row)
        {
            var inv = Player.Inventory;
            switch (row)
            {
                case InvRow.Health: return inv.HealthItems;
                case InvRow.Grenade: return inv.Grenades;
                case InvRow.C4:
                case InvRow.Vaccine: return 1;
                default: return inv.Ammo(RowAmmo(row));
            }
        }

        private static AmmoType RowAmmo(InvRow row) =>
            row == InvRow.ShotgunAmmo ? AmmoType.Shotgun : row == InvRow.ArAmmo ? AmmoType.AR : row == InvRow.RifleAmmo ? AmmoType.Rifle : row == InvRow.SmgAmmo ? AmmoType.SMG : AmmoType.None;

        /// <summary>
        /// Taps and drags on the open panel. Dragging an item out of the panel drops it (owner, 2026-09-29).
        /// Returns true when the mouse/finger is busy with the UI this frame.
        /// </summary>
        private bool HandlePanels()
        {
            if (Player.IsDead)
            {
                _weaponPanel = _inventory = false;
                return false;
            }
            if (_chest != null) return HandleChest();
            if (!_weaponPanel && !_inventory) return false;
            if (_inventory) BuildRows();
            var busy = false;
            foreach (var p in PointerInput.Current)
            {
                if (p.Began && _uiPointer == int.MinValue && OverOpenPanel(p.Position))
                {
                    _uiPointer = p.Id;
                    _uiStart = p.Position;
                    _uiItem = -1;
                    _sliderDrag = _dialogAmmo != AmmoType.None && SliderTrack.Contains(p.Position);
                    if (_dialogAmmo == AmmoType.None)
                    {
                        if (_weaponPanel)
                            for (var i = 0; i < PanelSlots.Length; i++)
                                if (WeaponTile(i).Contains(p.Position)) { _uiItem = i; _uiFromWeaponPanel = true; }
                        if (_inventory)
                            for (var i = 0; i < _rowCount; i++)
                                if (InventorySlot(i).Contains(p.Position)) { _uiItem = i; _uiFromWeaponPanel = false; }
                    }
                }
                if (p.Id != _uiPointer) continue;
                busy = true;
                if (_sliderDrag) SetSlider(p.Position.x);
                if (p.Ended)
                {
                    Release(p.Position);
                    _uiPointer = int.MinValue;
                }
            }
            if (!TouchMode && Mouse.current != null && OverOpenPanel(Gui.FromScreen(Mouse.current.position.ReadValue()))) busy = true;
            return busy;
        }

        private bool OverOpenPanel(Vector2 p) =>
            (_dialogAmmo != AmmoType.None && DialogRect.Contains(p)) ||
            (_weaponPanel && WeaponPanelRect.Contains(p)) ||
            (_inventory && InventoryRect.Contains(p));

        private void SetSlider(float x)
        {
            var max = Player.Inventory.Ammo(_dialogAmmo);
            var k = Mathf.Clamp01((x - SliderTrack.x) / SliderTrack.width);
            _dialogAmount = Mathf.Clamp(Mathf.RoundToInt(k * max), 1, Mathf.Max(1, max));
        }

        private void Release(Vector2 at)
        {
            if (_dialogAmmo != AmmoType.None)
            {
                if (_sliderDrag) return;
                if (DialogDrop.Contains(at) && DialogDrop.Contains(_uiStart)) Player.DropAmmo(_dialogAmmo, _dialogAmount);
                if ((DialogDrop.Contains(at) && DialogDrop.Contains(_uiStart)) || (DialogCancel.Contains(at) && DialogCancel.Contains(_uiStart)))
                    _dialogAmmo = AmmoType.None;
                return;
            }
            if (_inventory && InventoryClose.Contains(at) && InventoryClose.Contains(_uiStart))
            {
                _inventory = false;
                return;
            }
            if (_uiItem < 0) return;
            var tap = Vector2.Distance(at, _uiStart) < 20f;
            var outside = _uiFromWeaponPanel ? !WeaponPanelRect.Contains(at) : !InventoryRect.Contains(at);

            if (_uiFromWeaponPanel)
            {
                var slot = PanelSlots[_uiItem];
                if (outside)
                {
                    // The Pistol and the Knife can never be dropped.
                    if (slot == Slot.LongGun) Player.DropLongGun();
                    else if (slot == Slot.Grenade) Player.DropGrenade();
                }
                else if (tap && Player.CanSelect(slot))
                {
                    Player.Select(slot);
                    _weaponPanel = false;
                    Sfx.PlayUi();
                }
                return;
            }

            if (_uiItem >= _rowCount) return;
            var row = _rows[_uiItem];
            if (outside)
            {
                switch (row)
                {
                    case InvRow.LongGun: Player.DropLongGun(); break;
                    case InvRow.Health: Player.DropHealthItem(); break;
                    case InvRow.Grenade: Player.DropGrenade(); break;
                    case InvRow.C4: Player.DropMissionItem(LootKind.C4); break;
                    case InvRow.Vaccine: Player.DropMissionItem(LootKind.Vaccine); break;
                    default:
                        // Part of a stack: the slider picks how much.
                        _dialogAmmo = RowAmmo(row);
                        _dialogAmount = Player.Inventory.Ammo(_dialogAmmo);
                        break;
                }
            }
            else if (tap && row == InvRow.Health) Player.UseHealthItem();
            else if (tap && RowAmmo(row) != AmmoType.None)
            {
                // Owner, 2026-10-12: a tap on ammo asks how much to drop (dragging it out still works).
                _dialogAmmo = RowAmmo(row);
                _dialogAmount = Player.Inventory.Ammo(_dialogAmmo);
            }
        }

        // ---------------------------------------------------------------- equipment chest

        private Chest _chest;

        private void OpenChest(Chest chest)
        {
            _chest = chest;
            _weaponPanel = _inventory = false;
            _dialogAmmo = AmmoType.None;
        }

        /// <summary>The open chest's panel: tap an item to take it. Closes when you walk away.</summary>
        private bool HandleChest()
        {
            if (_chest == null) return false;
            var d = _chest.transform.position - Player.transform.position;
            d.y = 0f;
            if (Player.IsDead || !_chest.isActiveAndEnabled || d.magnitude > Player.Tuning.player.pickupRange + 1f)
            {
                _chest = null;
                return false;
            }
            var busy = false;
            foreach (var p in PointerInput.Current)
            {
                if (p.Began && _uiPointer == int.MinValue && InventoryRect.Contains(p.Position))
                {
                    _uiPointer = p.Id;
                    _uiStart = p.Position;
                }
                if (p.Id != _uiPointer) continue;
                busy = true;
                if (!p.Ended) continue;
                _uiPointer = int.MinValue;
                if (Vector2.Distance(p.Position, _uiStart) > 20f) continue;
                if (InventoryClose.Contains(p.Position))
                {
                    _chest = null;
                    return true;
                }
                for (var i = 0; i < _chest.Contents.Count; i++)
                    if (ListRow(i).Contains(p.Position))
                    {
                        Player.TakeFromChest(_chest, i);
                        break;
                    }
            }
            if (!TouchMode && Mouse.current != null && InventoryRect.Contains(Gui.FromScreen(Mouse.current.position.ReadValue()))) busy = true;
            return busy;
        }

        private void DrawChest()
        {
            var r = InventoryRect;
            Gui.Panel(r, new Color(0.08f, 0.1f, 0.06f, 0.8f));
            Gui.Label(new Rect(r.x + 20f, r.y + 12f, 300f, 30f), _chest.Title, 24, TextAnchor.MiddleLeft, Color.white, true);
            var inv = Player.Inventory;
            Gui.Label(new Rect(r.x + 20f, r.y + 42f, 400f, 26f), $"Your slots {inv.UsedSlots} / {inv.Capacity}", 16, TextAnchor.MiddleLeft,
                new Color(1f, 1f, 1f, 0.75f));
            Gui.Panel(InventoryClose, new Color(1f, 1f, 1f, 0.15f));
            Gui.Label(InventoryClose, "X", 22, TextAnchor.MiddleCenter, Color.white, true);
            for (var i = 0; i < _chest.Contents.Count && i < 6; i++)
            {
                var row = ListRow(i);
                Gui.Panel(row, new Color(1f, 1f, 1f, 0.1f));
                Gui.Label(new Rect(row.x + 14f, row.y, row.width - 200f, row.height), _chest.Contents[i].Label, 18, TextAnchor.MiddleLeft, Color.white);
                Gui.Label(new Rect(row.xMax - 214f, row.y, 200f, row.height), "tap to take", 14, TextAnchor.MiddleRight, new Color(1f, 1f, 1f, 0.55f));
            }
            if (_chest.Contents.Count == 0)
                Gui.Label(new Rect(r.x, r.y + 120f, r.width, 40f), "Empty", 20, TextAnchor.MiddleCenter, Color.gray);
        }

        // ---------------------------------------------------------------- drawing

        private void OnGUI()
        {
            if (Player == null || GameSession.Instance == null || !GameSession.Instance.Playing) return;
            Gui.Begin();
            DrawWorldMarkers();
            DrawDamageEffects();
            DrawStatus();
            if (GameSession.Instance.Settings.minimap) DrawMinimap();
            DrawDialogue();
            // Online Co-op (owner, 2026-10-07: "very laggy"): the round trip to the other player.
            var net = NetSession.Instance;
            if (net != null && net.InMatch)
            {
                var ping = net.PingMs;
                if (ping >= 0)
                    Gui.Label(new Rect(Gui.Width - 206f, 210f, 190f, 24f), $"Ping {ping} ms", 16, TextAnchor.MiddleRight,
                        ping < 120 ? new Color(0.6f, 1f, 0.6f) : ping < 250 ? new Color(1f, 0.9f, 0.4f) : new Color(1f, 0.45f, 0.4f), true);
            }
            DrawBossBar();
            if (TouchMode) DrawTouchControls();
            else DrawPcPrompts();
            if (_weaponPanel) DrawWeaponPanel();
            if (_inventory) DrawInventory();
            if (_chest != null) DrawChest();
            if (_dialogAmmo != AmmoType.None) DrawDialog();
            if (_toast != null && Time.unscaledTime < _toastUntil)
                Gui.Shadowed(new Rect(0f, 300f, Gui.Width, 40f), _toast, 26, TextAnchor.MiddleCenter, Color.white, true);
            if (Gui.DevTools && GameSession.Instance.Settings.statsDisplay) DrawDev();
            DrawQuit();
            if (Player.IsDead) DrawDeathScreen();
        }

        private void DrawWorldMarkers()
        {
            if (Player.IsDead) return;
            // D-235: the crosshair sits at a fixed distance on the aim line.
            var cross = Gui.WorldToGui(Camera, Player.transform.position + Vector3.up * 1.05f + Player.Facing * 3.5f);
            if (cross.HasValue)
            {
                var c = cross.Value;
                Gui.Fill(new Rect(c.x - 12f, c.y - 1.5f, 9f, 3f), Color.white);
                Gui.Fill(new Rect(c.x + 3f, c.y - 1.5f, 9f, 3f), Color.white);
                Gui.Fill(new Rect(c.x - 1.5f, c.y - 12f, 3f, 9f), Color.white);
                Gui.Fill(new Rect(c.x - 1.5f, c.y + 3f, 3f, 9f), Color.white);
            }
            if (Player.GrenadeAiming)
            {
                var m = Gui.WorldToGui(Camera, Player.GrenadeTarget);
                if (m.HasValue)
                {
                    Gui.Circle(m.Value, 22f, new Color(1f, 0.6f, 0.1f, 0.45f));
                    Gui.Circle(m.Value, 6f, new Color(1f, 0.8f, 0.2f, 0.9f));
                }
            }
            foreach (var item in LootItem.All)
            {
                var d = item.transform.position - Player.transform.position;
                if (d.sqrMagnitude > 49f || Building.Covered(item.transform.position)) continue;
                var at = Gui.WorldToGui(Camera, item.transform.position + Vector3.up * 0.6f);
                if (at.HasValue)
                    Gui.Shadowed(new Rect(at.Value.x - 100f, at.Value.y - 30f, 200f, 24f), item.Label, 16, TextAnchor.MiddleCenter, new Color(1f, 1f, 0.8f));
            }
        }

        private void DrawDamageEffects()
        {
            // D-244: hit screen effect. D-245: low-HP warning.
            var since = Time.time - Player.LastHitTime;
            if (since < 0.25f) Gui.Fill(new Rect(0f, 0f, Gui.Width, Gui.Height), new Color(0.8f, 0f, 0f, 0.18f * (1f - since / 0.25f)));
            if (!Player.IsDead && Player.Hp / Player.MaxHp < 0.3f)
            {
                var a = 0.25f + 0.2f * Mathf.Sin(Time.time * 6f);
                Gui.Frame(new Rect(0f, 0f, Gui.Width, Gui.Height), 26f, new Color(0.8f, 0f, 0f, a));
            }
        }

        private static string _infinity;

        private static string Infinity
        {
            get
            {
                if (_infinity != null) return _infinity;
                var font = GUI.skin.label.font != null ? GUI.skin.label.font : GUI.skin.font;
                _infinity = font != null && font.HasCharacter('∞') ? "∞" : "inf";
                return _infinity;
            }
        }


        /// <summary>D-234: the current objective. Rescue stages have two steps (owner, 2026-09-30).</summary>
        private static string ObjectiveText()
        {
            var game = GameSession.Instance;
            // Owner, 2026-10-08: the mission is done; everyone back to the helicopter.
            var heli = Helicopter.Current;
            if (heli != null && heli.State == Helicopter.Phase.Landing) return "Landing...";
            if (heli != null && (heli.State == Helicopter.Phase.TakingOff || heli.State == Helicopter.Phase.Gone)) return "Taking off!";
            if (heli != null && heli.State == Helicopter.Phase.Ready)
            {
                var away = game.AwayFromTheHelicopter();
                return away > 1 ? $"Mission complete! Everyone back to the helicopter ({away} to go)" : "Mission complete! Get back to the helicopter";
            }
            if (game.Stage != null && (game.Stage.Layout.Bomb || game.Stage.Layout.VaccineMission)) return Mission.ObjectiveText(game.LocalSurvivor);
            if (game.Stage != null && game.Stage.Layout.Tutorial) return "Tutorial";
            if (game.Stage != null && game.Stage.Layout.Endless) return Endless.Current != null ? Endless.Current.ObjectiveText() : "Endless";
            if (game.Stage == null || !game.Stage.Layout.Rescue) return "Objective: kill the Main Boss";
            var hostage = game.Hostage;
            if (hostage == null || !hostage.IsFreed) return "Objective: find the scientist and talk to them";
            return "Objective: bring the hostage back to the start";
        }
        private void DrawStatus()
        {
            // HP (D-232).
            var hpRect = new Rect(20f, 18f, 320f, 26f);
            Gui.Bar(hpRect, Player.Hp / Player.MaxHp,
                new Color(0.8f, 0.15f, 0.15f));
            Gui.Shadowed(hpRect, $"HP {Mathf.CeilToInt(Player.Hp)} / {Mathf.CeilToInt(Player.MaxHp)}", 16, TextAnchor.MiddleCenter, Color.white, true);

            // Current weapon, magazine + reserve (D-236).
            string weapon;
            string ammo = "";
            switch (Player.Selected)
            {
                case Slot.Knife: weapon = "Knife"; break;
                case Slot.Grenade: weapon = "Grenade"; ammo = $"×{Player.Inventory.Grenades}"; break;
                default:
                    var gun = Player.ActiveFirearm;
                    weapon = gun != null ? gun.Def.displayName : "Long gun (none)";
                    if (gun != null) ammo = $"{gun.Magazine} / {(gun.ReserveSource.Unlimited ? Infinity : gun.Reserve.ToString())}";
                    break;
            }
            var weaponRect = new Rect(20f, 50f, 320f, 60f);
            Gui.Panel(weaponRect, new Color(0f, 0f, 0f, 0.45f));
            Gui.Shadowed(new Rect(32f, 52f, 200f, 30f), weapon, 22, TextAnchor.MiddleLeft, Color.white, true);
            Gui.Shadowed(new Rect(32f, 80f, 296f, 26f), ammo, 20, TextAnchor.MiddleLeft, new Color(1f, 0.9f, 0.6f));
            var active = Player.ActiveFirearm;
            if (Player.Switching)
                Gui.Shadowed(new Rect(180f, 80f, 148f, 26f), "switching", 16, TextAnchor.MiddleRight, Color.gray);
            else if (active != null && active.Reloading)
            {
                Gui.Fill(new Rect(180f, 90f, 148f * active.ReloadProgress, 8f), new Color(1f, 0.85f, 0.3f));
                Gui.Shadowed(new Rect(180f, 64f, 148f, 24f), "reloading", 16, TextAnchor.MiddleRight, new Color(1f, 0.85f, 0.3f));
            }
            else if (active != null && active.Dry)
                Gui.Shadowed(new Rect(180f, 64f, 148f, 24f), "no ammo", 16, TextAnchor.MiddleRight, new Color(1f, 0.4f, 0.4f));

            if (Player.Healing)
            {
                var bar = new Rect(Gui.Width * 0.5f - 120f, 380f, 240f, 12f);
                Gui.Bar(bar, Player.HealProgress, new Color(0.4f, 1f, 0.5f));
                Gui.Shadowed(new Rect(bar.x, bar.y - 28f, bar.width, 26f), "healing", 18, TextAnchor.MiddleCenter, new Color(0.6f, 1f, 0.7f), true);
            }

            // D-234: the objective is always on screen.
            Gui.Shadowed(new Rect(Gui.Width * 0.5f - 300f, 14f, 600f, 30f), ObjectiveText(), 20, TextAnchor.MiddleCenter, Color.white, true);
            // The tutorial's instruction (owner, 2026-10-07), on a panel under the objective.
            var tutorial = Tutorial.Current;
            var instruction = tutorial != null ? tutorial.Instruction(TouchMode) : null;
            if (instruction != null)
            {
                var panel = new Rect(Gui.Width * 0.5f - 420f, 52f, 840f, 64f);
                Gui.Panel(panel, new Color(0f, 0f, 0f, 0.55f));
                Gui.Shadowed(new Rect(panel.x + 12f, panel.y, 160f, panel.height), $"Step {tutorial.StepNumber}/{tutorial.StepCount}", 18,
                    TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.4f), true);
                Gui.Shadowed(new Rect(panel.x + 120f, panel.y, panel.width - 132f, panel.height), instruction, 20, TextAnchor.MiddleLeft, Color.white, true);
            }
            var game = GameSession.Instance;
            if (game.MigrationNotice != null && Time.unscaledTime - game.MigrationTime < 10f)
                Gui.Shadowed(new Rect(0f, 150f, Gui.Width, 30f), game.MigrationNotice, 20, TextAnchor.MiddleCenter, new Color(1f, 0.85f, 0.4f), true);
        }

        private static Texture2D _minimap;
        private static StageMap _minimapOf;

        /// <summary>The minimap is the stage grid itself: one pixel per cell, made once per stage.</summary>
        private static Texture2D MinimapTexture(StageMap map)
        {
            if (_minimap != null && _minimapOf == map) return _minimap;
            _minimapOf = map;
            _minimap = new Texture2D(map.width, map.height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (var z = 0; z < map.height; z++)
            for (var x = 0; x < map.width; x++)
            {
                Color c;
                switch (map[x, z])
                {
                    case Tile.Wall: c = new Color(0.8f, 0.8f, 0.8f, 0.9f); break;
                    case Tile.Cliff: c = new Color(0.55f, 0.45f, 0.35f, 0.9f); break;
                    case Tile.Cover: c = new Color(0.65f, 0.6f, 0.5f, 0.9f); break;
                    case Tile.Bush: c = new Color(0.25f, 0.55f, 0.25f, 0.9f); break;
                    default:
                        // Open cells show their surface (layer 2), so areas still read on the minimap.
                        switch (map.SurfaceAt(x, z))
                        {
                            case Surface.Grass:
                            case Surface.Grass2: c = new Color(0.2f, 0.32f, 0.2f, 0.8f); break;
                            case Surface.Road:
                            case Surface.Road2: c = new Color(0.3f, 0.3f, 0.28f, 0.8f); break;
                            case Surface.Floor: c = new Color(0.35f, 0.32f, 0.28f, 0.8f); break;
                            default: c = new Color(0f, 0f, 0f, 0.55f); break;
                        }
                        break;
                }
                _minimap.SetPixel(x, z, c);
            }
            _minimap.Apply();
            return _minimap;
        }

        private void DrawMinimap()
        {
            var stage = GameSession.Instance.Stage;
            if (stage == null) return;
            const float size = 190f;
            var rect = new Rect(Gui.Width - size - 16f, 16f, size, size);
            var layout = stage.Layout;
            var k = size / Mathf.Max(layout.Size.x, layout.Size.y);
            if (layout.Map != null) GUI.DrawTexture(rect, MinimapTexture(layout.Map));
            else
            {
                Gui.Panel(rect, new Color(0f, 0f, 0f, 0.55f));
                foreach (var block in layout.Blocks)
                {
                    var r = new Rect(rect.x + (block.Center.x - block.Size.x * 0.5f) * k, rect.yMax - (block.Center.y + block.Size.y * 0.5f) * k,
                        Mathf.Max(1f, block.Size.x * k), Mathf.Max(1f, block.Size.y * k));
                    Gui.Fill(r, new Color(0.75f, 0.75f, 0.75f, 0.8f));
                }
            }
            Vector2 Dot(Vector3 w) => new Vector2(rect.x + w.x * k, rect.yMax - w.z * k);
            // Mission markers: the objective, and the Vaccine stage's rivals (red).
            if (layout.Bomb || layout.Rescue || layout.VaccineMission)
            {
                var objective = Mission.ObjectivePosition;
                if (objective.HasValue && Mathf.Repeat(Time.time, 1f) < 0.7f) Gui.Circle(Dot(objective.Value), 5f, new Color(1f, 0.85f, 0.2f));
            }
            // Rival teams (red, the second orange); Co-op teammates in green (owner, 2026-10-07), the AI teammate too.
            foreach (var s in Survivor.All)
            {
                if (s.IsDead || s == Player || s.IsHostage) continue;
                if (s.Team == Player.Team) Gui.Circle(Dot(s.transform.position), 4f, new Color(0.35f, 0.95f, 0.45f));
                else Gui.Circle(Dot(s.transform.position), 3.5f, s.Team == Survivor.RivalTeam2 ? new Color(1f, 0.6f, 0.15f) : new Color(1f, 0.25f, 0.3f));
            }
            // Owner, 2026-10-09: the helicopter (the way out) marked with an H.
            var heli = Helicopter.Current;
            if (heli != null && heli.State != Helicopter.Phase.Gone)
            {
                var h = Dot(heli.transform.position);
                Gui.Circle(h, 8f, new Color(0.05f, 0.08f, 0.16f, 0.9f));
                Gui.Circle(h, 6.5f, new Color(1f, 0.82f, 0.2f));
                Gui.Label(new Rect(h.x - 8f, h.y - 9f, 16f, 18f), "H", 13, TextAnchor.MiddleCenter, new Color(0.05f, 0.08f, 0.16f), true);
            }
            var p = Player.transform.position;
            Gui.Circle(Dot(p), 4f, new Color(0.3f, 0.7f, 1f));
            Gui.Frame(rect, 2f, new Color(1f, 1f, 1f, 0.4f));
        }

        private void DrawBossBar()
        {
            var stage = GameSession.Instance.Stage;
            if (stage == null) return;
            var p = Player.transform.position;
            Zombie boss = null;
            var label = "MAIN BOSS";
            var main = stage.MainBoss;
            if (main != null && !main.IsDead && stage.Layout.BossArea.Contains(new Vector2(p.x, p.z))) boss = main;
            else
            {
                // A secondary boss close by (owner, 2026-10-03) gets the bar too.
                var best = 16f;
                foreach (var z in Zombie.Alive)
                {
                    if (!ZombieTypes.IsBoss(z.Def.type) || z.IsMainBoss || z.IsDead) continue;
                    var d = Vector3.Distance(z.transform.position, p);
                    if (d >= best) continue;
                    best = d;
                    boss = z;
                    label = "SUB-BOSS";
                }
            }
            if (boss == null) return;
            var rect = new Rect(Gui.Width * 0.5f - 250f, 50f, 500f, 20f);
            Gui.Bar(rect, boss.Hp / boss.Def.hp,
                label == "MAIN BOSS" ? new Color(0.7f, 0.1f, 0.1f) : new Color(0.85f, 0.45f, 0.1f));
            Gui.Shadowed(new Rect(rect.x, rect.y - 2f, rect.width, rect.height + 4f), label, 14, TextAnchor.MiddleCenter, Color.white, true);
        }

        private void DrawTouchControls()
        {
            var layout = Layout;
            var move = Controls.Find(layout, Controls.Move);
            // The stick sits under the thumb while moving; at rest a faint hint shows where it usually is.
            var active = _movePointer != int.MinValue;
            var m = active ? _moveOrigin : Controls.Center(move, Gui.Width);
            var r = Controls.Radius(move);
            Gui.Circle(m, r, new Color(1f, 1f, 1f, active ? 0.14f : 0.06f));
            Gui.Circle(m + new Vector2(_moveVector.x, -_moveVector.y) * r, r * 0.38f, new Color(1f, 1f, 1f, active ? 0.4f : 0.15f));
            if (!active) Gui.Label(new Rect(m.x - 60f, m.y - 14f, 120f, 28f), "MOVE", 16, TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.3f));

            DrawButton(Controls.Find(layout, Controls.Fire), "FIRE", _fireHeld ? new Color(1f, 0.4f, 0.3f, 0.6f) : new Color(1f, 0.3f, 0.2f, 0.35f));
            DrawButton(Controls.Find(layout, Controls.Reload), "RELOAD", new Color(1f, 1f, 1f, 0.25f));
            DrawButton(Controls.Find(layout, Controls.Weapon), "WEAPON", new Color(1f, 1f, 1f, _weaponPanel ? 0.5f : 0.25f));
            DrawButton(Controls.Find(layout, Controls.Bag), "BAG", new Color(1f, 1f, 1f, _inventory ? 0.5f : 0.25f));
            // D-239: only near something that can be picked up.
            if (CanPlant)
                DrawButton(Controls.Find(layout, Controls.Pickup), "PLANT", new Color(1f, 0.35f, 0.25f, 0.6f));
            else if (CanRescue)
                DrawButton(Controls.Find(layout, Controls.Pickup), _talk < 0 ? "TALK" : "NEXT", new Color(0.4f, 0.8f, 1f, 0.6f));
            else if (CanPickup)
                DrawButton(Controls.Find(layout, Controls.Pickup), NearChest != null && NearLoot == null ? "OPEN" : "PICK UP", new Color(0.4f, 1f, 0.4f, 0.4f));
        }

        /// <summary>One round touch button (also used by the layout editor).</summary>
        public static void DrawButton(TouchButton b, string text, Color color)
        {
            var center = Controls.Center(b, Gui.Width);
            var radius = Controls.Radius(b);
            Gui.Circle(center, radius, color);
            Gui.Shadowed(new Rect(center.x - radius, center.y - 14f, radius * 2f, 28f), text, radius > 60f ? 22 : 14, TextAnchor.MiddleCenter, Color.white, true);
        }

        private string KeyLabel(PcAction action) => Controls.Label(Controls.KeyFor(action, Prefs));

        private void DrawPcPrompts()
        {
            var loot = NearLoot;
            var y = Gui.Height - 40f;
            var move = KeyLabel(PcAction.MoveUp) + KeyLabel(PcAction.MoveLeft) + KeyLabel(PcAction.MoveDown) + KeyLabel(PcAction.MoveRight);
            Gui.Shadowed(new Rect(20f, y, 1000f, 30f),
                $"{move} move · mouse aim · {KeyLabel(PcAction.Fire)} fire · {KeyLabel(PcAction.WeaponPanel)} weapons · {KeyLabel(PcAction.Reload)} reload · " +
                $"{KeyLabel(PcAction.Inventory)} inventory", 16, TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.6f));
            var pickup = KeyLabel(PcAction.Pickup);
            if (CanPlant)
                Gui.Shadowed(new Rect(0f, Gui.Height - 110f, Gui.Width, 32f), $"{pickup}  plant the C4", 24, TextAnchor.MiddleCenter, new Color(1f, 0.5f, 0.4f), true);
            else if (CanRescue)
                Gui.Shadowed(new Rect(0f, Gui.Height - 110f, Gui.Width, 32f), _talk < 0 ? $"{pickup}  talk to the scientist" : $"{pickup}  continue", 24, TextAnchor.MiddleCenter, new Color(0.5f, 0.85f, 1f), true);
            else if (loot != null)
                Gui.Shadowed(new Rect(0f, Gui.Height - 110f, Gui.Width, 32f), $"{pickup}  pick up {loot.Label}", 22, TextAnchor.MiddleCenter, new Color(0.6f, 1f, 0.6f), true);
            else if (NearChest != null && _chest == null)
                Gui.Shadowed(new Rect(0f, Gui.Height - 110f, Gui.Width, 32f), $"{pickup}  open {NearChest.Title}", 22, TextAnchor.MiddleCenter, new Color(0.6f, 1f, 0.6f), true);
        }

        private void DrawWeaponPanel()
        {
            var panel = WeaponPanelRect;
            Gui.Panel(panel, new Color(0f, 0f, 0f, 0.7f));
            for (var i = 0; i < PanelSlots.Length; i++)
            {
                var slot = PanelSlots[i];
                var tile = WeaponTile(i);
                var available = Player.CanSelect(slot);
                var selected = Player.Selected == slot;
                Gui.Panel(tile, selected ? new Color(0.3f, 0.5f, 0.8f, 0.8f) : new Color(1f, 1f, 1f, available ? 0.15f : 0.05f));
                string name, sub;
                switch (slot)
                {
                    case Slot.Pistol: name = "Pistol"; sub = $"{Player.Pistol.Magazine} / {Infinity}"; break;
                    case Slot.LongGun:
                        name = Player.LongGun != null ? Player.LongGun.Def.displayName : "—";
                        sub = Player.LongGun != null ? $"{Player.LongGun.Magazine} / {Player.LongGun.Reserve}" : "empty";
                        break;
                    case Slot.Knife: name = "Knife"; sub = ""; break;
                    default: name = "Grenade"; sub = $"×{Player.Inventory.Grenades}"; break;
                }
                var color = available ? Color.white : Color.gray;
                Gui.Label(new Rect(tile.x, tile.y + 30f, tile.width, 30f), name, 20, TextAnchor.MiddleCenter, color, true);
                Gui.Label(new Rect(tile.x, tile.y + 66f, tile.width, 26f), sub, 16, TextAnchor.MiddleCenter, color);
            }
            Gui.Label(new Rect(panel.x, panel.yMax + 2f, panel.width, 24f), "tap to select · drag out to drop", 14, TextAnchor.MiddleCenter,
                new Color(1f, 1f, 1f, 0.6f));
        }

        private void DrawInventory()
        {
            var r = InventoryRect;
            var inv = Player.Inventory;
            // Solid, so nothing of the stage shows through the items.
            Gui.Panel(r, new Color(0.09f, 0.14f, 0.21f, 0.98f));
            Gui.Label(new Rect(r.x + 20f, r.y + 12f, 300f, 30f), "Inventory", 24, TextAnchor.MiddleLeft, Color.white, true);
            Gui.Label(new Rect(r.x + 20f, r.y + 42f, 400f, 26f), $"Slots {inv.UsedSlots} / {inv.Capacity}", 16, TextAnchor.MiddleLeft,
                new Color(1f, 1f, 1f, 0.75f));
            var bar = new Rect(r.x + 220f, r.y + 50f, 280f, 10f);
            Gui.Bar(bar, inv.UsedSlots / Mathf.Max(1f, inv.Capacity), new Color(0.9f, 0.8f, 0.3f));
            Gui.Panel(InventoryClose, new Color(1f, 1f, 1f, 0.15f));
            Gui.Label(InventoryClose, "X", 22, TextAnchor.MiddleCenter, Color.white, true);

            for (var i = 0; i < _rowCount; i++)
            {
                var slot = InventorySlot(i);
                var item = _rows[i];
                Gui.Panel(slot, new Color(0.24f, 0.36f, 0.5f, 0.95f));
                // The item's picture, from its 3D model.
                var icon = ItemIcon(item);
                if (icon != null) GUI.DrawTexture(new Rect(slot.x + 12f, slot.y + 8f, slot.width - 24f, slot.height - 34f), icon, ScaleMode.ScaleToFit, true);
                Gui.Shadowed(new Rect(slot.x + 6f, slot.yMax - 30f, slot.width - 12f, 24f), ItemName(item), 15, TextAnchor.MiddleCenter, Color.white, true);
                var count = ItemCount(item);
                if (count > 1) Gui.Shadowed(new Rect(slot.x + 6f, slot.y + 4f, slot.width - 12f, 24f), $"×{count}", 18, TextAnchor.UpperRight, UiKit.Yellow, true);
            }
            if (_rowCount > 0)
                Gui.Label(new Rect(r.x + 20f, r.yMax - 46f, r.width - 40f, 30f), "Tap a Health Item to use it · tap ammo to drop some · drag an item out to drop it", 15,
                    TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.7f));
            if (_rowCount == 0)
                Gui.Label(new Rect(r.x, r.y + 120f, r.width, 40f), "Empty", 20, TextAnchor.MiddleCenter, Color.gray);
        }

        private void DrawDialog()
        {
            var r = DialogRect;
            var max = Player.Inventory.Ammo(_dialogAmmo);
            if (max <= 0)
            {
                _dialogAmmo = AmmoType.None;
                return;
            }
            _dialogAmount = Mathf.Clamp(_dialogAmount, 1, max);
            Gui.Panel(r, new Color(0.1f, 0.1f, 0.1f, 0.95f));
            Gui.Frame(r, 2f, new Color(1f, 1f, 1f, 0.3f));
            Gui.Label(new Rect(r.x, r.y + 20f, r.width, 34f), $"Drop {_dialogAmmo} ammo: {_dialogAmount} of {max}", 22, TextAnchor.MiddleCenter, Color.white, true);
            var track = SliderTrack;
            Gui.Fill(new Rect(track.x, track.center.y - 4f, track.width, 8f), new Color(1f, 1f, 1f, 0.25f));
            var knobX = track.x + track.width * (max <= 1 ? 1f : (_dialogAmount / (float)max));
            Gui.Circle(new Vector2(knobX, track.center.y), 18f, new Color(0.9f, 0.8f, 0.3f));
            Gui.Panel(DialogDrop, new Color(0.8f, 0.3f, 0.2f, 0.8f));
            Gui.Label(DialogDrop, "Drop", 22, TextAnchor.MiddleCenter, Color.white, true);
            Gui.Panel(DialogCancel, new Color(1f, 1f, 1f, 0.2f));
            Gui.Label(DialogCancel, "Cancel", 22, TextAnchor.MiddleCenter, Color.white, true);
        }


        /// <summary>
        /// Owner, 2026-10-08 (replacing D-097, no spectating): in Co-op a dead player watches their teammate; the stage goes
        /// on while a teammate lives (D-102) and a later Victory still counts for them (D-103). With nobody left to watch,
        /// the full "You died" screen.
        /// </summary>
        private void DrawDeathScreen()
        {
            var game = GameSession.Instance;
            var watching = game != null ? game.Spectated : null;
            if (watching != null)
            {
                Gui.Fill(new Rect(0f, 0f, Gui.Width, 96f), new Color(0.03f, 0.02f, 0.02f, 0.7f));
                Gui.Shadowed(new Rect(0f, 8f, Gui.Width, 44f), "YOU DIED", 34, TextAnchor.MiddleCenter, new Color(1f, 0.35f, 0.3f), true);
                Gui.Label(new Rect(0f, 52f, Gui.Width, 30f), $"Spectating {watching.DisplayName}. If your team wins, the stage counts for you too.", 18,
                    TextAnchor.MiddleCenter, Color.white, true);
                if (game.LivingTeammates().Count > 1 && Gui.Button(new Rect(Gui.Width - 220f, 110f, 200f, 56f), "Next teammate")) game.SpectateNext();
                return;
            }
            Gui.Fill(new Rect(0f, 0f, Gui.Width, Gui.Height), new Color(0.03f, 0.02f, 0.02f, 1f));
            Gui.Shadowed(new Rect(0f, 250f, Gui.Width, 60f), "YOU DIED", 48, TextAnchor.MiddleCenter, new Color(1f, 0.35f, 0.3f), true);
            Gui.Label(new Rect(0f, 330f, Gui.Width, 40f), "Your teammate fights on. If they kill the Main Boss, the stage counts for you too.", 20,
                TextAnchor.MiddleCenter, Color.gray);
        }
        private float _fpsSmoothed = 60f;

        private void DrawDev()
        {
            _fpsSmoothed = Mathf.Lerp(_fpsSmoothed, 1f / Mathf.Max(0.0001f, Time.unscaledDeltaTime), 0.05f);
            Gui.Shadowed(new Rect(20f, 116f, 400f, 22f), $"{_fpsSmoothed:0} fps · zombies {Zombie.Alive.Count} · shots {Projectiles.InFlight}", 14,
                TextAnchor.MiddleLeft, new Color(0.7f, 1f, 0.7f));
        }
    }
}
