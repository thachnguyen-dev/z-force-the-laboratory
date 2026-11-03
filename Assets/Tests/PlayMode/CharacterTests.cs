using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using ZombieGame.Art;

namespace ZombieGame.Tests
{
    /// <summary>
    /// The owner's character models (2026-10-07: connected to the game): baked, skinned to their own skeletons, worn by
    /// their character or zombie type, animated in code.
    /// </summary>
    public class CharacterTests
    {
        private class Scripted : ICharacterInputSource
        {
            public CharacterInput Value;
            public CharacterInput Read(Survivor self) => Value;
        }

        private GameSession _session;

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

        [Test]
        public void EveryCharacterModelIsBakedAndSkinned()
        {
            var lib = CharacterModelLibrary.Instance;
            Assert.IsNotNull(lib, "baked (Zombie > Developer (Claude) > Bake Characters For The Game)");
            foreach (var m in lib.models)
            {
                var mesh = CharacterRig.LoadMesh(m.id);
                Assert.IsNotNull(mesh, m.id);
                Assert.AreEqual(m.bones.Length, mesh.bindposes.Length, m.id);
                Assert.AreEqual(m.bones.Length, m.parents.Length, m.id);
                Assert.AreEqual(m.bones.Length, m.joints.Length, m.id);
                // The rig's bones first, in its order (the animation finds them by position); fingers after.
                var names = System.Enum.GetNames(m.quadruped ? typeof(CharacterRig.Quad) : typeof(CharacterRig.Bone));
                for (var i = 0; i < names.Length; i++) Assert.AreEqual(CharacterRig.Delivered(names[i]), m.bones[i], $"{m.id}: bone {i}");
                Assert.AreEqual(1, mesh.subMeshCount, $"{m.id}: one material, colours in the mesh");
                Assert.Greater(mesh.colors.Length, 0, $"{m.id}: coloured");
                Assert.AreEqual(m.height, mesh.bounds.max.y, 0.05f, $"{m.id}: height");
                Assert.AreEqual(0f, mesh.bounds.min.y, 0.15f, $"{m.id}: feet on the ground");
                foreach (var w in mesh.boneWeights)
                {
                    Assert.AreEqual(1f, w.weight0 + w.weight1 + w.weight2 + w.weight3, 0.01f, m.id);
                    Assert.Less(Mathf.Max(w.boneIndex0, w.boneIndex1, w.boneIndex2, w.boneIndex3), m.bones.Length, m.id);
                }
                // Built in the game, every bone hangs from its parent, at its joint.
                var holder = new GameObject("holder").transform;
                var bones = CharacterRig.Build(m, holder, out _);
                for (var i = 0; i < bones.Length; i++)
                {
                    var p = m.parents[i];
                    Assert.AreEqual(p < 0 ? holder.GetChild(0) : bones[p], bones[i].parent, $"{m.id}: {m.bones[i]}'s parent");
                    Assert.Less((bones[i].position - m.joints[i]).magnitude, 0.001f, $"{m.id}: {m.bones[i]} at its joint");
                }
                Object.DestroyImmediate(holder.gameObject);
            }
            // Every playable character wears a model, with a firing pose for every gun.
            foreach (var c in new GameTuning().characters)
            {
                var m = CharacterRig.ForCharacter(c.id);
                Assert.IsNotNull(m, $"{c.id} wears a model");
                foreach (var gun in new[] { "ar_gun", "shotgun", "sniper_gun", "smg_gun", "pistol" })
                {
                    var hold = m.holds.FirstOrDefault(h => h.weapon == gun);
                    Assert.IsNotNull(hold, $"{c.id}: a firing pose for the {gun}");
                    Assert.AreEqual(m.bones.Length, hold.rotations.Length, $"{c.id} {gun}");
                }
            }
            // Every zombie type wears a model.
            foreach (ZombieType t in System.Enum.GetValues(typeof(ZombieType)))
                foreach (var pick in new[] { 0, 1 })
                    Assert.IsNotNull(CharacterModelLibrary.Find(Zombie.ModelFor(t, pick)), $"{t} ({pick}) has a model");
        }

        [UnityTest]
        public IEnumerator BodyFacesTheWalkTurnsToTheAimWhenFiring()
        {
            _session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            _session.StartStage(StageLibrary.Load(StageLibrary.TestStageId), _session.Tuning.characters[0], 31);
            yield return null;
            var player = _session.LocalSurvivor;
            foreach (var z in Zombie.Alive.ToArray()) if (!z.IsMainBoss) Object.Destroy(z.gameObject);
            Assert.IsTrue(player.HasModel, "the character wears its model");
            // Facing (owner, 2026-10-07): the body follows the aim, walking or not.
            var input = new Scripted { Value = new CharacterInput { Move = new Vector2(0f, -1f), Aim = new Vector2(1f, 0f), GrenadeDistance = -1f } };
            player.InputSource = input;
            for (var i = 0; i < 30; i++) yield return null;
            Assert.Less(Vector3.Angle(player.transform.forward, Vector3.right), 15f, "faces the aim while walking another way");
            input.Value.FireHeld = true;
            for (var i = 0; i < 10; i++) yield return null;
            Assert.Less(Vector3.Angle(player.transform.forward, Vector3.right), 15f, "and while firing");
            // The gun points where the character aims (the firing pose turns the whole body so the gun points ahead).
            var gun = player.HeldTransform;
            Assert.IsNotNull(gun);
            var muzzleDir = gun.TransformDirection(Vector3.right);
            muzzleDir.y = 0f;
            Assert.Less(Vector3.Angle(muzzleDir, player.transform.forward), 20f, "the gun points along the aim");
            input.Value.FireHeld = false;
            input.Value.Aim = new Vector2(0f, 1f);
            yield return new WaitForSeconds(0.6f);
            Assert.Less(Vector3.Angle(player.transform.forward, Vector3.forward), 15f, "a new aim turns it, firing or not");
            player.InputSource = null;
        }

        [UnityTest]
        public IEnumerator TheRatCanBeShot()
        {
            // Owner, 2026-10-07: the Rat (0.45 m) couldn't be hit: bullets fly at chest height over it.
            _session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            _session.StartStage(StageLibrary.Load(StageLibrary.TestStageId), _session.Tuning.characters[0], 31);
            yield return null;
            var player = _session.LocalSurvivor;
            foreach (var z in Zombie.Alive.ToArray()) if (!z.IsMainBoss) Object.Destroy(z.gameObject);
            yield return null;
            var rat = _session.Stage.SpawnZombie(ZombieType.Rat, player.transform.position + new Vector3(0f, 0f, 4f));            var input = new Scripted { Value = new CharacterInput { Aim = new Vector2(0f, 1f), FireHeld = true, GrenadeDistance = -1f } };
            player.InputSource = input;
            var until = Time.time + 3f;
            while (Time.time < until && rat != null && !rat.IsDead && rat.Hp >= rat.Def.hp)
            {
                // Keep aiming at it as it runs in.
                if (rat != null)
                {
                    var to = rat.transform.position - player.transform.position;
                    input.Value.Aim = new Vector2(to.x, to.z).normalized;
                }
                yield return null;
            }
            Assert.IsTrue(rat == null || rat.IsDead || rat.Hp < rat.Def.hp, "the Rat is hit");
            player.InputSource = null;
        }

        [UnityTest]
        public IEnumerator TheFuelBarrelCanBeShot()
        {
            // Owner, 2026-10-07: the fuel barrel (1 m tall) couldn't be hit: bullets fly at chest height over it.
            _session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            _session.StartStage(StageLibrary.Load(StageLibrary.TestStageId), _session.Tuning.characters[0], 31);
            yield return null;
            var player = _session.LocalSurvivor;
            foreach (var z in Zombie.Alive.ToArray()) if (!z.IsMainBoss) Object.Destroy(z.gameObject);
            var barrel = Barrel.Spawn(_session.StageRoot, player.transform.position + new Vector3(0f, 0f, 5f), _session.Tuning.barrel);
            yield return new WaitForFixedUpdate();
            var input = new Scripted { Value = new CharacterInput { Aim = new Vector2(0f, 1f), FireHeld = true, GrenadeDistance = -1f } };
            player.InputSource = input;
            var until = Time.time + 3f;
            while (Time.time < until && barrel != null && !barrel.IsDead) yield return null;
            Assert.IsTrue(barrel == null || barrel.IsDead, "the shot barrel blows up");
            player.InputSource = null;
        }

        [UnityTest]
        public IEnumerator TheTutorialTeachesStepByStep()
        {
            // Owner, 2026-10-07: the tutorial stage, instructions one at a time; only the zombies each step brings.
            _session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            _session.StartStage(StageLibrary.Load("m0_tutorial"), _session.Tuning.characters[0], 31);
            yield return null;
            var tutorial = Tutorial.Current;
            Assert.IsNotNull(tutorial);
            Assert.AreEqual(1, tutorial.StepNumber);
            StringAssert.Contains("Move", tutorial.Instruction(true));
            Assert.IsNotNull(_session.Stage.MainBoss, "Boss 1 waits at the end");
            Assert.AreEqual(ZombieType.Boss, _session.Stage.MainBoss.Def.type);
            Assert.AreEqual(1, Zombie.Alive.Count, "no other zombies until a step brings them");
            var player = _session.LocalSurvivor;
            var input = new Scripted { Value = new CharacterInput { Move = new Vector2(1f, 0f), Aim = new Vector2(1f, 0f), GrenadeDistance = -1f } };
            player.InputSource = input;
            yield return new WaitForSeconds(2.5f);
            Assert.AreEqual(2, tutorial.StepNumber, "walking on finishes the first step");
            Assert.AreEqual(3, Zombie.Alive.Count, "the shooting step brings two zombies");
            input.Value.Move = Vector2.zero;
            foreach (var z in Zombie.Alive.ToArray()) if (!z.IsMainBoss) z.TakeDamage(1000f, player.transform.position, false);
            yield return new WaitForSeconds(2f);
            Assert.AreEqual(3, tutorial.StepNumber, "killing them moves on to reloading");
            player.InputSource = null;
            yield return new WaitForSeconds(3f);
            Assert.AreEqual(1, Zombie.Alive.Count, "still nothing else comes");
        }

        [UnityTest]
        public IEnumerator TheGrenadeGoesWhereTheThumbPointsWhileHeld()
        {
            // Owner, 2026-10-07 (Android): the throw point freely controlled while holding Fire; letting go throws.
            _session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            _session.StartStage(StageLibrary.Load(StageLibrary.TestStageId), _session.Tuning.characters[0], 31);
            yield return null;
            var player = _session.LocalSurvivor;
            foreach (var z in Zombie.Alive.ToArray()) if (!z.IsMainBoss) Object.Destroy(z.gameObject);
            LootItem.Spawn(player.transform.position, LootKind.Grenade);
            var input = new Scripted { Value = new CharacterInput { Pickup = true, GrenadeDistance = -1f } };
            player.InputSource = input;
            yield return null;
            input.Value.Pickup = false;
            yield return null;
            Assert.Greater(player.Inventory.Grenades, 0);
            player.Select(Slot.Grenade);
            yield return new WaitForSeconds(0.8f);
            var g = _session.Tuning.grenade;
            float Out() { var d = player.GrenadeTarget - player.transform.position; d.y = 0f; return d.magnitude; }
            // Held: near, then far, then near again, in two directions.
            input.Value = new CharacterInput { Aim = new Vector2(1f, 0f), FireHeld = true, ThrowOnRelease = true, GrenadeDistance = g.minRange + 1f };
            for (var i = 0; i < 5; i++) yield return null;
            Assert.IsTrue(player.GrenadeAiming);
            var near = Out();
            input.Value.GrenadeDistance = g.maxRange;
            input.Value.Aim = new Vector2(0f, 1f);
            for (var i = 0; i < 5; i++) yield return null;
            var far = Out();
            input.Value.GrenadeDistance = g.minRange + 1f;
            for (var i = 0; i < 5; i++) yield return null;
            var back = Out();
            Assert.Greater(far, near + 1f, "the marker goes out as the thumb drags out");
            Assert.Less(back, far - 1f, "and comes back in: not stuck at the full range");
            var count = player.Inventory.Grenades;
            input.Value.FireHeld = false;
            for (var i = 0; i < 3; i++) yield return null;
            Assert.AreEqual(count - 1, player.Inventory.Grenades, "letting go throws");
            Assert.IsFalse(player.GrenadeAiming);
            player.InputSource = null;
        }

        [UnityTest]
        public IEnumerator ZombiesWearTheirModelsAndWalk()
        {
            _session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            _session.StartStage(StageLibrary.Load(StageLibrary.TestStageId), _session.Tuning.characters[0], 31);
            for (var i = 0; i < 5; i++) yield return null;
            var zombies = Zombie.Alive.ToArray();
            Assert.Greater(zombies.Length, 0);
            foreach (var z in zombies)
            {
                Assert.IsNotNull(z.GetComponent<ZombiePuppet>(), $"{z.Def.type} is animated");
                Assert.IsNotNull(z.GetComponentInChildren<SkinnedMeshRenderer>(), $"{z.Def.type} wears its model");
            }
        }
    }
}
