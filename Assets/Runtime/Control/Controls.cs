using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace ZombieGame
{
    /// <summary>The PC actions a player can rebind (owner, 2026-09-29: all PC controls can be rebound).</summary>
    public enum PcAction { MoveUp, MoveDown, MoveLeft, MoveRight, Fire, WeaponPanel, Reload, Pickup, Inventory }

    /// <summary>
    /// Saved control settings: the Android button layout and the PC key bindings, with the owner's defaults
    /// (D-009, PC keys 2026-09-29). Arrow keys always move too, whatever the bindings.
    /// </summary>
    public static class Controls
    {
        // ---- Android layout ----

        public const string Move = "Move";
        public const string Fire = "Fire";
        public const string Reload = "Reload";
        public const string Pickup = "Pickup";
        public const string Weapon = "Weapon";
        public const string Bag = "Bag";

        /// <summary>Base radius of each button before its size setting.</summary>
        public static float BaseRadius(string id)
        {
            switch (id)
            {
                case Move: return 110f;
                case Fire: return 85f;
                case Pickup: return 46f;
                case Bag: return 40f;
                default: return 42f;
            }
        }

        public static List<TouchButton> DefaultLayout() => new List<TouchButton>
        {
            new TouchButton { id = Move, right = false, x = 170f, y = 560f },
            new TouchButton { id = Fire, right = true, x = 160f, y = 580f },
            new TouchButton { id = Reload, right = true, x = 300f, y = 655f },
            new TouchButton { id = Pickup, right = true, x = 310f, y = 520f },
            new TouchButton { id = Weapon, right = true, x = 160f, y = 430f },
            new TouchButton { id = Bag, right = true, x = 60f, y = 330f },
        };

        /// <summary>The saved layout, filled in with defaults for anything missing.</summary>
        public static List<TouchButton> Layout(SettingsData settings)
        {
            var result = DefaultLayout();
            if (settings?.touch == null) return result;
            for (var i = 0; i < result.Count; i++)
                foreach (var saved in settings.touch)
                    if (saved.id == result[i].id) result[i] = saved.Copy();
            return result;
        }

        public static TouchButton Find(List<TouchButton> layout, string id)
        {
            foreach (var b in layout) if (b.id == id) return b;
            return null;
        }

        public static Vector2 Center(TouchButton b, float screenWidth) => new Vector2(b.right ? screenWidth - b.x : b.x, b.y);

        public static float Radius(TouchButton b) => BaseRadius(b.id) * Mathf.Clamp(b.size, 0.6f, 1.8f);

        // ---- PC keys ----

        public static Dictionary<PcAction, string> DefaultKeys() => new Dictionary<PcAction, string>
        {
            [PcAction.MoveUp] = nameof(Key.W), [PcAction.MoveDown] = nameof(Key.S), [PcAction.MoveLeft] = nameof(Key.A),
            [PcAction.MoveRight] = nameof(Key.D), [PcAction.Fire] = "Mouse0", [PcAction.WeaponPanel] = nameof(Key.Q),
            [PcAction.Reload] = nameof(Key.R), [PcAction.Pickup] = nameof(Key.E), [PcAction.Inventory] = nameof(Key.F),
        };

        private static Dictionary<PcAction, string> _keys;
        private static SettingsData _keysFrom;

        public static string KeyFor(PcAction action, SettingsData settings)
        {
            if (_keys == null || _keysFrom != settings) ReloadBindings(settings);
            return _keys[action];
        }

        /// <summary>Re-reads the bindings (after the rebinding screen saved them).</summary>
        public static void ReloadBindings(SettingsData settings)
        {
            _keysFrom = settings;
            _keys = DefaultKeys();
            if (settings?.keys == null) return;
            foreach (var b in settings.keys)
                if (Enum.TryParse<PcAction>(b.action, out var action) && IsValidKey(b.key)) _keys[action] = b.key;
        }

        public static bool IsValidKey(string key) => key == "Mouse0" || key == "Mouse1" || key == "Mouse2" || Enum.TryParse<Key>(key, out _);

        private static ButtonControl Control(string key)
        {
            var mouse = Mouse.current;
            switch (key)
            {
                case "Mouse0": return mouse?.leftButton;
                case "Mouse1": return mouse?.rightButton;
                case "Mouse2": return mouse?.middleButton;
            }
            var keyboard = Keyboard.current;
            return keyboard != null && Enum.TryParse<Key>(key, out var k) && k != Key.None ? keyboard[k] : null;
        }

        public static bool Held(PcAction action, SettingsData settings) => Control(KeyFor(action, settings))?.isPressed ?? false;

        public static bool Pressed(PcAction action, SettingsData settings) => Control(KeyFor(action, settings))?.wasPressedThisFrame ?? false;

        public static bool IsMouse(PcAction action, SettingsData settings) => KeyFor(action, settings).StartsWith("Mouse");

        /// <summary>For prompts and the rebinding screen.</summary>
        public static string Label(string key)
        {
            switch (key)
            {
                case "Mouse0": return "LMB";
                case "Mouse1": return "RMB";
                case "Mouse2": return "MMB";
                default: return key;
            }
        }

        /// <summary>The first key or mouse button pressed this frame (rebinding), or null.</summary>
        public static string AnyPressed()
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.leftButton.wasPressedThisFrame) return "Mouse0";
                if (mouse.rightButton.wasPressedThisFrame) return "Mouse1";
                if (mouse.middleButton.wasPressedThisFrame) return "Mouse2";
            }
            var keyboard = Keyboard.current;
            if (keyboard == null) return null;
            foreach (var key in keyboard.allKeys)
                if (key.wasPressedThisFrame && key.keyCode != Key.Escape) return key.keyCode.ToString();
            return null;
        }
    }
}
