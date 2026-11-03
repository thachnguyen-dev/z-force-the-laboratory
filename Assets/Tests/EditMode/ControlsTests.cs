using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ZombieGame.Tests
{
    public class ControlsTests
    {
        [Test]
        public void D017_SavedButtonsOverrideTheDefaultsOthersStay()
        {
            var settings = new SettingsData
            {
                touch = new List<TouchButton> { new TouchButton { id = Controls.Fire, right = false, x = 300f, y = 400f, size = 1.4f } },
            };
            var layout = Controls.Layout(settings);
            Assert.AreEqual(6, layout.Count);
            var fire = Controls.Find(layout, Controls.Fire);
            Assert.IsFalse(fire.right);
            Assert.AreEqual(new Vector2(300f, 400f), Controls.Center(fire, 1280f));
            Assert.AreEqual(Controls.BaseRadius(Controls.Fire) * 1.4f, Controls.Radius(fire), 0.001f);
            var move = Controls.Find(layout, Controls.Move);
            Assert.AreEqual(new Vector2(170f, 560f), Controls.Center(move, 1280f), "untouched button keeps its default");
            var reload = Controls.Find(layout, Controls.Reload);
            Assert.AreEqual(1280f - 300f, Controls.Center(reload, 1280f).x, "right-anchored buttons follow the right edge");
        }

        [Test]
        public void PcKeysDefaultToTheOwnersAndCanBeRebound()
        {
            var settings = new SettingsData();
            Controls.ReloadBindings(settings);
            Assert.AreEqual("Q", Controls.KeyFor(PcAction.WeaponPanel, settings));
            Assert.AreEqual("Mouse0", Controls.KeyFor(PcAction.Fire, settings));
            settings.keys.Add(new KeyBinding { action = nameof(PcAction.Reload), key = "T" });
            settings.keys.Add(new KeyBinding { action = nameof(PcAction.Pickup), key = "NotAKey" });
            Controls.ReloadBindings(settings);
            Assert.AreEqual("T", Controls.KeyFor(PcAction.Reload, settings));
            Assert.AreEqual("E", Controls.KeyFor(PcAction.Pickup, settings), "an invalid key falls back to the default");
        }

        [Test]
        public void SettingsKeepLayoutAndKeysThroughJson()
        {
            var settings = new SettingsData();
            settings.touch.Add(new TouchButton { id = Controls.Move, x = 200f, y = 500f, size = 0.8f });
            settings.keys.Add(new KeyBinding { action = nameof(PcAction.Fire), key = "Space" });
            var back = JsonUtility.FromJson<SettingsData>(JsonUtility.ToJson(settings));
            Assert.AreEqual(200f, back.touch[0].x);
            Assert.AreEqual(0.8f, back.touch[0].size);
            Assert.AreEqual("Space", back.keys[0].key);
        }

        [Test]
        public void OldSettingsFilesStillLoad()
        {
            var back = JsonUtility.FromJson<SettingsData>("{\"version\":1,\"minimap\":false,\"volume\":0.5}");
            Assert.IsFalse(back.minimap);
            Assert.AreEqual(6, Controls.Layout(back).Count);
            Controls.ReloadBindings(back);
            Assert.AreEqual("W", Controls.KeyFor(PcAction.MoveUp, back));
        }
    }
}
