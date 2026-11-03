using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZombieGame.Tests
{
    /// <summary>
    /// Owner, 2026-10-12: after pausing and watching an ad the inventory took no touch; and the inventory as a grid of
    /// pictures, without weapons.
    /// </summary>
    public class InventoryUiTests
    {
        private GameSession _session;
        private static readonly System.Reflection.BindingFlags Any = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_session == null) yield break;
            _session.GoTo(SessionState.MainMenu);
            yield return null;
            foreach (var camera in Object.FindObjectsByType<CameraRig>(FindObjectsSortMode.None)) Object.Destroy(camera.gameObject);
            Object.Destroy(_session.gameObject);
            _session = null;
            yield return null;
        }

        private IEnumerator Start()
        {
            _session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            _session.StartStage(StageLibrary.Load(StageLibrary.TestStageId), _session.Tuning.characters[0], 41);
            for (var i = 0; i < 3; i++) yield return null;
        }

        private static PlayerHud Hud => Object.FindObjectsByType<PlayerHud>(FindObjectsSortMode.None).First();

        [UnityTest]
        public IEnumerator AFingerLostToAnAdNoLongerBlocksTheInventory()
        {
            yield return Start();
            var hud = Hud;
            hud.OpenInventory();
            // As after an ad: the inventory still "holds" a finger that the screen no longer has.
            foreach (var field in new[] { "_uiPointer", "_movePointer", "_firePointer", "_aimPointer", "_quitPointer" })
                typeof(PlayerHud).GetField(field, Any).SetValue(hud, 4242);
            yield return null;
            yield return null;
            foreach (var field in new[] { "_uiPointer", "_movePointer", "_firePointer", "_aimPointer", "_quitPointer" })
                Assert.AreEqual(int.MinValue, (int)typeof(PlayerHud).GetField(field, Any).GetValue(hud), field + " let go of the lost finger");
        }

        [UnityTest]
        public IEnumerator TheGridShowsItemsNotWeapons()
        {
            yield return Start();
            var me = _session.LocalSurvivor;
            me.TryPickup(LootItem.SpawnLongGun(me.transform.position, _session.Tuning.Firearm(WeaponId.AR), 30));
            me.Inventory.AddAmmo(AmmoType.AR, 30);
            me.Inventory.AddHealthItem();
            var hud = Hud;
            hud.OpenInventory();
            yield return null;
            typeof(PlayerHud).GetMethod("BuildRows", Any).Invoke(hud, null);
            var count = (int)typeof(PlayerHud).GetField("_rowCount", Any).GetValue(hud);
            var rows = ((System.Array)typeof(PlayerHud).GetField("_rows", Any).GetValue(hud)).Cast<object>().Take(count).Select(o => o.ToString()).ToList();
            Assert.IsFalse(rows.Contains("LongGun"), "no weapons in the inventory");
            CollectionAssert.AreEquivalent(new[] { "ArAmmo", "Health" }, rows);
            // Every item has its picture.
            foreach (var r in typeof(PlayerHud).GetField("_rows", Any).GetValue(hud) as System.Array)
            {
                if (r.ToString() == "LongGun") continue;
                var icon = typeof(PlayerHud).GetMethod("ItemIcon", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).Invoke(null, new[] { r });
                Assert.IsNotNull(icon, r + " has a picture");
            }
            // A tap on the Health Item uses it (hurt first, so it can).
            me.TakeHit(40f, me.transform.position + Vector3.forward, 0f);
            var slot = (Rect)typeof(PlayerHud).GetMethod("InventorySlot", Any).Invoke(hud, new object[] { rows.IndexOf("Health") });
            typeof(PlayerHud).GetField("_uiItem", Any).SetValue(hud, rows.IndexOf("Health"));
            typeof(PlayerHud).GetField("_uiFromWeaponPanel", Any).SetValue(hud, false);
            typeof(PlayerHud).GetField("_uiStart", Any).SetValue(hud, slot.center);
            typeof(PlayerHud).GetMethod("Release", Any).Invoke(hud, new object[] { slot.center });
            Assert.IsTrue(me.Healing, "tapping the Health Item uses it");
        }
    }
}
