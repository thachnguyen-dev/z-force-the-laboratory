using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.AI;

namespace ZombieGame
{
    public partial class GameSession
    {
        /// <summary>
        /// Owner, 2026-10-09 (publishing on Google Play): the store's screenshots. <c>-zombie-store-shots &lt;folder&gt;</c>
        /// (a PC command line; release builds too, so no "Development Build" mark) plays a set of moments with the phone's
        /// touch controls showing: the main menu, the characters, and fights in the main missions; each saved as a PNG.
        /// </summary>
        private IEnumerator StoreShots(string folder)
        {
            System.IO.Directory.CreateDirectory(folder);
            yield return new WaitForSeconds(1.5f);
            GoTo(SessionState.MainMenu);
            yield return new WaitForSeconds(1f);
            yield return StoreShot(folder, "01-menu.png");
            // The character choice (it follows the stage choice).
            _storyIndex = 0;
            GoTo(SessionState.CharacterSelect);
            yield return new WaitForSeconds(1f);
            yield return StoreShot(folder, "02-characters.png");

            var scenes = new[]
            {
                // stage, character, AI teammate, the zombies sent at the player, file
                ("m1_gate_a", 0, false, new[] { ZombieType.Basic, ZombieType.Basic, ZombieType.Fast, ZombieType.Basic, ZombieType.Tank }, "03-checkpoint.png"),
                ("m2_scientist_a", 1, true, new[] { ZombieType.Basic, ZombieType.Fast, ZombieType.Basic, ZombieType.Basic, ZombieType.Dog }, "04-town.png"),
                ("m4_reserve_a", 2, false, new[] { ZombieType.Dog, ZombieType.Dog, ZombieType.Tiger, ZombieType.Rat, ZombieType.Rat, ZombieType.Rat }, "05-reserve-rain.png"),
                ("m5_wing_a", 3, true, new[] { ZombieType.Basic, ZombieType.Rat, ZombieType.Rat, ZombieType.Fast, ZombieType.Basic }, "06-lab-night.png"),
                ("m3_breeding_a", 0, false, new[] { ZombieType.Basic, ZombieType.Fast, ZombieType.Basic, ZombieType.SubBoss, ZombieType.Basic }, "07-lab-compound.png"),
                (Endless.StageId, 1, true, new[] { ZombieType.Basic, ZombieType.Basic, ZombieType.Fast, ZombieType.Fast, ZombieType.Bear, ZombieType.Basic, ZombieType.Basic }, "08-endless.png"),
            };
            foreach (var (stage, character, ai, zombies, file) in scenes)
            {
                GoTo(SessionState.MainMenu);
                yield return null;
                StartStage(StageLibrary.Load(stage), Tuning.characters[character], 1234, withAi: ai);
                // The helicopter brings the team in first.
                for (var t = 0f; t < 20f && (LocalSurvivor == null || LocalSurvivor.Boarded); t += 0.25f) yield return new WaitForSeconds(0.25f);
                yield return new WaitForSeconds(0.5f);
                var me = LocalSurvivor;
                if (me == null) continue;
                if (_hud != null) _hud.TouchMode = true;
                // An open spot away from the landing pad (out of the picture): nothing solid within a few metres.
                var at = OpenSpot(Stage.Layout.PlayerSpawn);
                var cc = me.GetComponent<CharacterController>();
                cc.enabled = false;
                me.transform.position = at;
                cc.enabled = true;
                foreach (var z in Zombie.Alive.ToArray()) if (!z.IsMainBoss && Vector3.Distance(z.transform.position, at) < 30f) Destroy(z.gameObject);
                me.TryPickup(LootItem.SpawnLongGun(at, Tuning.Firearm(WeaponId.AR), 30));
                me.Inventory.AddAmmo(AmmoType.AR, 90);
                me.Select(Slot.LongGun);
                // Endless: past the preparation time, the waves under way.
                if (Endless.Current != null)
                    typeof(Endless).GetProperty("Clock").SetValue(Endless.Current, Tuning.endless.prepSeconds + 95f);
                var ally = AiSurvivor;
                if (ally != null)
                {
                    var acc = ally.GetComponent<CharacterController>();
                    acc.enabled = false;
                    ally.transform.position = at + new Vector3(-1.6f, 0f, -0.8f);
                    acc.enabled = true;
                    ally.TryPickup(LootItem.SpawnLongGun(ally.transform.position, Tuning.Firearm(WeaponId.Shotgun), 6));
                }
                _camera.Snap();
                // The zombies come from ahead, spread across the screen.
                for (var i = 0; i < zombies.Length; i++)
                {
                    var a = Mathf.Lerp(-55f, 55f, zombies.Length == 1 ? 0.5f : i / (zombies.Length - 1f)) * Mathf.Deg2Rad;
                    var d = 6.5f + (i % 2) * 2f;
                    Stage.SpawnZombie(zombies[i], at + new Vector3(Mathf.Sin(a) * d * 1.4f, 0f, Mathf.Cos(a) * d));
                }
                // Fire at the nearest.
                _hud.DevOverride = input =>
                {
                    Zombie target = null;
                    var best = 16f;
                    foreach (var z in Zombie.Alive)
                    {
                        var dz = Vector3.Distance(z.transform.position, me.transform.position);
                        if (dz < best) { best = dz; target = z; }
                    }
                    if (target == null) return input;
                    var to = target.transform.position - me.transform.position;
                    input.Aim = new Vector2(to.x, to.z).normalized;
                    input.FireHeld = true;
                    return input;
                };
                yield return new WaitForSeconds(1.3f);
                yield return StoreShot(folder, file);
                _hud.DevOverride = null;
            }
            // Owner, 2026-10-12: the purchase as the App Store reviewer sees it (the offer with Buy and Restore).
            GoTo(SessionState.MainMenu);
            yield return new WaitForSeconds(0.5f);
            Monetization.RestoreShownOverride = true;
            _menus.ShowOffer();
            yield return new WaitForSeconds(1f);
            yield return StoreShot(folder, "09-purchase-offer.png");
            Monetization.RestoreShownOverride = null;
            Application.Quit();
        }

        private static Vector3 OpenSpot(Vector3 start)
        {
            const int walls = (1 << Layers.Obstacle) | (1 << Layers.ShootThrough) | (1 << Layers.Barrel);
            for (var d = 22f; d <= 46f; d += 6f)
                for (var k = 0; k < 12; k++)
                {
                    var a = (k * 30f + 15f) * Mathf.Deg2Rad;
                    var p = start + new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a)) * d;
                    if (!NavMesh.SamplePosition(p, out var hit, 1f, NavAreas.WalkMask) || hit.position.y > 0.2f) continue;
                    // Room round the player and ahead of them for the zombies.
                    if (Physics.CheckSphere(hit.position + Vector3.up, 3f, walls, QueryTriggerInteraction.Ignore)) continue;
                    if (Physics.CheckSphere(hit.position + new Vector3(0f, 1f, 6f), 3.5f, walls, QueryTriggerInteraction.Ignore)) continue;
                    return hit.position;
                }
            return start + new Vector3(0f, 0f, 20f);
        }

        private static IEnumerator StoreShot(string folder, string file)
        {
            yield return new WaitForEndOfFrame();
            // -zombie-store-scale n: rendered n times the window's size (the App Store's iPhone sizes are bigger than the monitor).
            var scale = int.TryParse(NetSession.CommandLineValue("-zombie-store-scale"), out var n) && n > 1 ? n : 1;
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(folder, file), scale);
            yield return new WaitForSeconds(0.4f);
        }
    }
}
