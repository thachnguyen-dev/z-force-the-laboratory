using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZombieGame.Tests
{
    /// <summary>
    /// Every gun hits every zombie and the barrel (owner, 2026-10-07: "fix every issue related to height ... must not
    /// occur again"; the Rat, the fuel barrel and then the sniper against the Rat had all been missed because a shot
    /// only hit what reached its flight height). Each gun against each target, standing still near and far, and the
    /// small fast ones charging in.
    /// </summary>
    public class HitTests
    {
        private class Scripted : ICharacterInputSource
        {
            public CharacterInput Value;
            public CharacterInput Read(Survivor self) => Value;
        }

        private GameSession _session;
        /// <summary>A direction from the player with nothing in the way for 32 m (the targets go along it).</summary>
        private Vector3 _lane = Vector3.forward;

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
            _session.StartStage(StageLibrary.Load(StageLibrary.TestStageId), _session.Tuning.characters[0], 31);
            yield return null;
            // Everything but the Main Boss (killing it would end the stage).
            foreach (var z in Zombie.Alive.ToArray()) if (!z.IsMainBoss) Object.Destroy(z.gameObject);
            _session.Stage.Population?.ClearDormant();
            // A clear firing lane: the first of 16 directions with no wall or cover within 32 m at shot height or below.
            var from = _session.LocalSurvivor.transform.position;
            for (var i = 0; i < 16; i++)
            {
                var d = Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward;
                var clear = true;
                foreach (var h in new[] { 0.3f, Survivor.FlightHeight, 1.8f })
                    if (Physics.SphereCast(from + Vector3.up * h, 0.5f, d, out _, 32f, 1 << Layers.Obstacle)) clear = false;
                if (!clear) continue;
                _lane = d;
                break;
            }
            // The Main Boss held still behind the player, out of the line of fire.
            var boss = _session.Stage.MainBoss;
            if (boss != null)
            {
                boss.enabled = false;
                boss.transform.position = from - _lane * 30f;
            }
            yield return null;
        }

        /// <summary>Puts the gun in the player's hands (null: the pistol).</summary>
        private IEnumerator Arm(Survivor player, WeaponId? gun)
        {
            var input = new Scripted { Value = new CharacterInput { GrenadeDistance = -1f } };
            player.InputSource = input;
            if (gun.HasValue)
            {
                var def = _session.Tuning.Firearm(gun.Value);
                // The gun first, then plenty of spare ammo (an empty long gun switches to the pistol); one at a time,
                // so the pickup takes each.
                LootItem.SpawnLongGun(player.transform.position, def, def.magazine);
                yield return Pick(input);
                LootItem.SpawnAmmo(player.transform.position, def.ammo, 999, false);
                yield return Pick(input);
                player.Select(Slot.LongGun);
            }
            else player.Select(Slot.Pistol);
            yield return Wait(1f);
            Assert.AreEqual(gun.HasValue ? Slot.LongGun : Slot.Pistol, player.Selected, "the gun is in hand");
            if (gun.HasValue) Assert.AreEqual(gun.Value, player.ActiveFirearm.Def.id, "the gun is in hand");
        }

        /// <summary>
        /// Before each target: the long gun's ammo topped up and the gun back in hand (an empty long gun switches to the
        /// pistol, and the inventory holds only so much spare ammo).
        /// </summary>
        private IEnumerator Refill(Survivor player, WeaponId? gun)
        {
            if (!gun.HasValue) yield break;
            var input = (Scripted)player.InputSource;
            LootItem.SpawnAmmo(player.transform.position, _session.Tuning.Firearm(gun.Value).ammo, 999, false);
            yield return Pick(input);
            if (player.Selected != Slot.LongGun)
            {
                player.Select(Slot.LongGun);
                yield return Wait(0.8f);
            }
            Assert.AreEqual(Slot.LongGun, player.Selected, "the long gun is back in hand");
            Assert.IsFalse(player.IsDead, "the player is alive");
        }

        /// <summary>A zombie out of the way: killed (zombies are pooled; a destroyed one would be handed out again).</summary>
        private static void Remove(Zombie z)
        {
            if (z == null || z.IsDead) return;
            z.enabled = true;
            z.TakeDamage(1e6f, z.transform.position, false);
        }

        /// <summary>Waits, keeping the stage's own zombies away (only the test's target may be near the player).</summary>
        private static IEnumerator Wait(float seconds, Zombie keep = null)
        {
            var until = Time.time + seconds;
            do
            {
                foreach (var other in Zombie.Alive.ToArray()) if (other != keep && !other.IsMainBoss) Remove(other);
                yield return null;
            } while (Time.time < until);
        }

        private static IEnumerator Pick(Scripted input)
        {
            // One press (held, it would take every frame: the gun swapped out, then back).
            input.Value.Pickup = true;
            yield return null;
            input.Value.Pickup = false;
            yield return Wait(0.05f);
            // Whatever is left (the gun that was swapped out) goes, so the next pickup takes the next item.
            foreach (var item in LootItem.All.ToArray()) Object.Destroy(item.gameObject);
            yield return null;
        }

        /// <summary>Fires at the target (keeping the aim on it) until it is hurt or the time runs out; true if hurt.</summary>
        private static IEnumerator FireAt(Survivor player, Zombie target, float seconds, bool[] hurt)
        {
            var input = (Scripted)player.InputSource;
            // Each target starts with the gun ready (not reloading).
            var gun = player.ActiveFirearm;
            var ready = Time.time + 4f;
            while (gun != null && gun.Reloading && Time.time < ready) yield return null;
            var until = Time.time + seconds;
            while (Time.time < until && target != null && !target.IsDead && target.Hp >= target.Def.hp)
            {
                // Only the target: the stage's own spawning would bring others that hit the player.
                foreach (var other in Zombie.Alive.ToArray()) if (other != target && !other.IsMainBoss) Remove(other);
                var to = target.transform.position - player.transform.position;
                input.Value.Aim = new Vector2(to.x, to.z).normalized;
                input.Value.FireHeld = true;
                yield return null;
            }
            input.Value.FireHeld = false;
            hurt[0] = target == null || target.IsDead || target.Hp < target.Def.hp;
        }

        private static readonly WeaponId?[] Guns = { null, WeaponId.AR, WeaponId.Shotgun, WeaponId.Rifle, WeaponId.SMG };

        [UnityTest]
        public IEnumerator EveryGunHitsEveryZombieStandingStill()
        {
            yield return Start();
            var player = _session.LocalSurvivor;
            var missed = new System.Collections.Generic.List<string>();
            foreach (var gun in Guns)
            {
                yield return Arm(player, gun);
                // Near, middling, and as far as the gun reaches (80 % of its range).
                var range = _session.Tuning.Firearm(gun ?? WeaponId.Pistol).range;
                foreach (ZombieType type in System.Enum.GetValues(typeof(ZombieType)))
                    foreach (var distance in new[] { 1.6f, Mathf.Min(5f, range * 0.5f), range * 0.8f })
                    {
                        var z = _session.Stage.SpawnZombie(type, player.transform.position + _lane * (distance + (ZombieTypes.IsBoss(type) ? 1.5f : 0f)));
                        yield return Refill(player, gun);
                        // Held still where it stands, so only the hit is tested.
                        z.enabled = false;
                        yield return new WaitForFixedUpdate();
                        var hurt = new bool[1];
                        yield return FireAt(player, z, 5f, hurt);
                        if (!hurt[0])
                        {
                            missed.Add($"{(gun.HasValue ? gun.Value.ToString() : "Pistol")} vs {type} at {distance} m");
                            // What lies along the line, for the record.
                            var from = player.transform.position + Vector3.up * Survivor.FlightHeight;
                            var along = string.Join(", ", Physics.SphereCastAll(from, 0.08f, _lane, distance + 2f, ~0, QueryTriggerInteraction.Ignore)
                                .OrderBy(h => h.distance).Select(h => $"{h.collider.name} (layer {h.collider.gameObject.layer}) at {h.distance:0.0}"));
                            Debug.Log($"[ZOMBIE-MISS] {missed[missed.Count - 1]}: player {player.transform.position}, target {(z != null ? z.transform.position.ToString() : "gone")}, alive {(z != null && z.gameObject.activeSelf)}, gun {player.Selected} {player.ActiveFirearm?.Def.id}, forward {player.transform.forward}, lane {_lane}; along: {along}");
                        }
                        Remove(z);
                        yield return null;
                    }
            }
            player.InputSource = null;
            Assert.IsEmpty(missed, "missed: " + string.Join("; ", missed));
        }

        [UnityTest]
        public IEnumerator EveryGunHitsTheSmallFastOnesChargingIn()
        {
            yield return Start();
            var player = _session.LocalSurvivor;
            var missed = new System.Collections.Generic.List<string>();
            foreach (var gun in Guns)
            {
                yield return Arm(player, gun);
                foreach (var type in new[] { ZombieType.Rat, ZombieType.Dog, ZombieType.Fast })
                {
                    yield return Refill(player, gun);
                    var z = _session.Stage.SpawnZombie(type, player.transform.position + _lane * 9f);
                    z.JoinChase(player, 30f);
                    var hurt = new bool[1];
                    yield return FireAt(player, z, 4f, hurt);
                    if (!hurt[0]) missed.Add($"{(gun.HasValue ? gun.Value.ToString() : "Pistol")} vs {type} charging");
                    if (z != null) Remove(z);
                    yield return Wait(0.3f);
                }
            }
            player.InputSource = null;
            Assert.IsEmpty(missed, "missed: " + string.Join("; ", missed));
        }

        [UnityTest]
        public IEnumerator EveryGunHitsTheBarrel()
        {
            yield return Start();
            var player = _session.LocalSurvivor;
            var missed = new System.Collections.Generic.List<string>();
            foreach (var gun in Guns)
            {
                yield return Arm(player, gun);
                // Outside the barrel's blast (it would hurt the player otherwise).
                foreach (var distance in new[] { 6f, 10f })
                {
                    yield return Refill(player, gun);
                    var ready = Time.time + 4f;
                    while (player.ActiveFirearm != null && player.ActiveFirearm.Reloading && Time.time < ready) yield return null;
                    var barrel = Barrel.Spawn(_session.StageRoot, player.transform.position + _lane * distance, _session.Tuning.barrel);
                    yield return new WaitForFixedUpdate();
                    var input = (Scripted)player.InputSource;
                    input.Value.Aim = new Vector2(_lane.x, _lane.z);
                    input.Value.FireHeld = true;
                    var until = Time.time + 5f;
                    while (Time.time < until && barrel != null && !barrel.IsDead)
                    {
                        foreach (var other in Zombie.Alive.ToArray()) if (!other.IsMainBoss) Remove(other);
                        yield return null;
                    }
                    input.Value.FireHeld = false;
                    if (barrel != null && !barrel.IsDead) missed.Add($"{(gun.HasValue ? gun.Value.ToString() : "Pistol")} vs the barrel at {distance} m");
                    if (barrel != null) Object.Destroy(barrel.gameObject);
                    yield return Wait(0.3f);
                }
            }
            player.InputSource = null;
            Assert.IsEmpty(missed, "missed: " + string.Join("; ", missed));
        }
    }
}
