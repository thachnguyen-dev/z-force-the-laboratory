using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ZombieGame.Tests
{
    /// <summary>
    /// Owner, 2026-10-08: the helicopter (landing with the players aboard, the rotor, getting back to it, take-off), the
    /// blue blood, the Tiger's charge, spectating in Co-op, and the new sounds.
    /// </summary>
    public class FeatureTests
    {
        private class Scripted : ICharacterInputSource
        {
            public CharacterInput Value;
            public CharacterInput Read(Survivor self) => Value;
        }

        private GameSession _session;
        private Survivor Player => _session.LocalSurvivor;

        private IEnumerator Start(string stageId, bool withAi = false, bool intro = false)
        {
            _session = new GameObject("Game Session").AddComponent<GameSession>();
            yield return null;
            _session.StartStage(StageLibrary.Load(stageId), _session.Tuning.characters[0], 41, withAi, intro: intro);
            yield return null;
        }

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

        private static void Teleport(Survivor s, Vector3 p)
        {
            var c = s.GetComponent<CharacterController>();
            c.enabled = false;
            s.transform.position = p;
            c.enabled = true;
        }

        private void ClearZombies()
        {
            foreach (var z in Zombie.Alive.ToArray()) if (!z.IsMainBoss) z.TakeDamage(1e6f, z.transform.position, true);
            _session.Stage.Population?.ClearDormant();
        }

        [UnityTest]
        public IEnumerator ThePlayersArriveInTheHelicopterAndStepOutWhenItLands()
        {
            yield return Start("m1_gate_a", intro: true);
            var heli = Helicopter.Current;
            Assert.IsNotNull(heli, "the helicopter");
            Assert.IsNotNull(heli.transform.Find("Rotor"), "its rotor, split off the model");
            Assert.AreEqual(Helicopter.Phase.Landing, heli.State, "coming down");
            yield return null;
            Assert.Greater(heli.transform.position.y, 5f, "from above");
            Assert.IsTrue(Player.Boarded, "the player is aboard");
            Assert.IsTrue(Player.GetComponentsInChildren<Renderer>().All(r => !r.enabled), "hidden");
            var hp = Player.Hp;
            Player.TakeHit(50f, Player.transform.position + Vector3.forward, 0f);
            Assert.AreEqual(hp, Player.Hp, "can't be hurt aboard");
            var rotor = heli.transform.Find("Rotor");
            var turn = rotor.localEulerAngles.y;
            yield return new WaitForSeconds(0.1f);
            Assert.AreNotEqual(turn, rotor.localEulerAngles.y, "the rotor spins");
            var sound = heli.GetComponent<AudioSource>();
            Assert.IsNotNull(sound, "the helicopter is heard");
            Assert.AreEqual("HelicopterLoop", sound.clip.name, "the owner's helicopter sound");
            Assert.IsTrue(sound.isPlaying && sound.loop, "looping while the rotor turns");
            yield return new WaitForSeconds(Helicopter.LandTime);
            Assert.AreEqual(Helicopter.Phase.Landed, heli.State, "landed");
            Assert.Less(heli.transform.position.y, 0.05f, "on the pad");
            yield return null;
            Assert.IsFalse(Player.Boarded, "out");
            Assert.IsTrue(Player.GetComponentsInChildren<Renderer>().Any(r => r.enabled), "and seen");
            yield return new WaitForSeconds(Helicopter.WindDown + 0.3f);
            Assert.IsFalse(sound.isPlaying, "quiet once the rotor has stopped");
        }

        [UnityTest]
        public IEnumerator AStageIsWonOnlyWhenEveryoneIsBackAtTheHelicopterAndItHasFlown()
        {
            yield return Start("m1_gate_a", withAi: true);
            ClearZombies();
            var heli = Helicopter.Current;
            var ai = _session.AiSurvivor;
            ai.InputSource = null;
            // The mission done (the Main Boss down) far from the helicopter: not won yet.
            var pad = _session.Stage.Layout.SpawnArea;
            var away = new Vector3(pad.center.x + 25f, 0f, pad.center.y + 25f);
            Teleport(Player, away);
            Teleport(ai, away + Vector3.right);
            _session.Stage.MainBoss.TakeDamage(1e7f, _session.Stage.MainBoss.transform.position, true);
            for (var i = 0; i < 5; i++) yield return null;
            Assert.AreEqual(SessionState.Playing, _session.State, "not won yet");
            Assert.AreEqual(Helicopter.Phase.Ready, heli.State, "the helicopter waits, rotor spinning up");
            // The player back, the teammate still away: it waits.
            Teleport(Player, new Vector3(pad.center.x, 0f, pad.center.y - pad.height * 0.3f));
            for (var i = 0; i < 5; i++) yield return null;
            Assert.AreEqual(Helicopter.Phase.Ready, heli.State, "waits for every living player");
            // Everyone back: it takes off, and then the stage is won.
            Teleport(ai, new Vector3(pad.center.x + 1f, 0f, pad.center.y - pad.height * 0.3f));
            for (var i = 0; i < 5; i++) yield return null;
            Assert.AreEqual(Helicopter.Phase.TakingOff, heli.State, "takes off");
            Assert.IsTrue(Player.Boarded && ai.Boarded, "with everyone aboard");
            Assert.AreEqual(SessionState.Playing, _session.State, "the stage ends after the take-off");
            yield return new WaitForSeconds(Helicopter.TakeOffTime + 0.3f);
            Assert.Greater(heli.transform.position.y, Helicopter.Height * 0.8f, "flown up");
            Assert.AreEqual(SessionState.Victory, _session.State, "stage complete");
        }

        [UnityTest]
        public IEnumerator ADeadTeammateDoesntHoldTheHelicopterUp()
        {
            yield return Start("m1_gate_a", withAi: true);
            ClearZombies();
            var ai = _session.AiSurvivor;
            ai.InputSource = null;
            ai.TakeDamage(1e6f, ai.transform.position, false);
            _session.Stage.MainBoss.TakeDamage(1e7f, _session.Stage.MainBoss.transform.position, true);
            var pad = _session.Stage.Layout.SpawnArea;
            Teleport(Player, new Vector3(pad.center.x, 0f, pad.center.y - pad.height * 0.3f));
            for (var i = 0; i < 5; i++) yield return null;
            Assert.AreEqual(Helicopter.Phase.TakingOff, Helicopter.Current.State, "the living players are aboard");
        }

        [UnityTest]
        public IEnumerator ShotZombiesSprayGreenBloodThatLeavesNoMarks()
        {
            yield return Start("test_stage");
            ClearZombies();
            yield return null;
            var z = _session.Stage.SpawnZombie(ZombieType.Basic, Player.transform.position + Vector3.forward * 6f);
            z.enabled = false;
            z.TakeDamage(5f, Player.transform.position, false);
            Assert.Greater(BloodSpray.InAir, 6, "a spray of drops");
            var drops = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r => r.name == "Drop" && r.gameObject.activeInHierarchy).ToList();
            Assert.IsTrue(drops.All(d => d.transform.position.y > 0.2f), "in the air, not on the ground");
            var colour = drops[0].sharedMaterial.GetColor("_BaseColor");
            Assert.Greater(colour.g, colour.r * 2f, "green");
            Assert.Greater(colour.g, colour.b * 2f, "green");
            // Most of it goes out the far side.
            var away = z.transform.position - Player.transform.position;
            yield return new WaitForSeconds(0.15f);
            drops = Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Where(r => r.name == "Drop" && r.gameObject.activeInHierarchy).ToList();
            Assert.Greater(drops.Count(d => Vector3.Dot(d.transform.position - z.transform.position, away) > 0f), drops.Count / 2, "most out the far side");
            yield return new WaitForSeconds(BloodSpray.MaxLife + 0.1f);
            Assert.AreEqual(0, BloodSpray.InAir, "gone, leaving no marks");
            Assert.IsFalse(Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None).Any(r => r.name == "Splat"), "no ground splats");
            z.TakeDamage(5f, z.transform.position, true);
            Assert.AreEqual(0, BloodSpray.InAir, "explosions don't spray");
        }

        [UnityTest]
        public IEnumerator TheScientistFollowsOnlyAfterTheTalk()
        {
            yield return Start("test_rescue");
            ClearZombies();
            var hostage = _session.Hostage;
            Teleport(Player, hostage.transform.position + new Vector3(1f, 0f, 0f));
            yield return new WaitForSeconds(1f);
            Assert.IsFalse(hostage.IsFreed, "standing next to them does nothing");
            Assert.IsFalse(Mission.Alerted, "and alerts nobody");
            Assert.GreaterOrEqual(HostageBrain.Dialogue.Length, 2, "there is something to say");
            Assert.IsTrue(HostageBrain.Dialogue.Any(l => l.who == "Scientist"), "the scientist speaks");
        }

        [UnityTest]
        public IEnumerator TheTigerWarnsInRedThenCharges()
        {
            yield return Start("test_stage");
            ClearZombies();
            var input = new Scripted { Value = new CharacterInput { GrenadeDistance = -1f } };
            Player.InputSource = input;
            yield return null;
            var c = _session.Tuning.charge;
            var tiger = _session.Stage.SpawnZombie(ZombieType.Tiger, Player.transform.position + Vector3.forward * 10f);
            Assert.IsNotNull(tiger, "a Tiger");
            tiger.JoinChase(Player, 30f);
            var hp = Player.Hp;
            Transform lane = null;
            var until = Time.time + 6f;
            while (lane == null && Time.time < until)
            {
                var found = GameObject.Find("Charge warning");
                if (found != null) lane = found.transform;
                yield return null;
            }
            Assert.IsNotNull(lane, "a red warning appears");
            Assert.IsTrue(tiger.Charging, "it is about to charge");
            var colour = lane.GetComponent<MeshRenderer>().sharedMaterial.GetColor("_BaseColor");
            Assert.Greater(colour.r, colour.g * 3f, "red");
            var start = tiger.transform.position;
            yield return new WaitForSeconds(c.telegraph + c.distance / c.speed + 0.3f);
            Assert.Greater(Vector3.Distance(start, tiger.transform.position), 5f, "it charged forward");
            Assert.Less(Player.Hp, hp - c.damage * 0.5f, "and hit the player in its way");
            Player.InputSource = null;
        }

        [UnityTest]
        public IEnumerator ADeadPlayerSpectatesTheirTeammate()
        {
            yield return Start("test_stage", withAi: true);
            ClearZombies();
            var mate = _session.AiSurvivor;
            // The other player online (a human teammate), standing in for the AI.
            mate.IsHuman = true;
            Player.TakeDamage(1e6f, Player.transform.position, false);
            for (var i = 0; i < 3; i++) yield return null;
            Assert.IsTrue(Player.IsDead, "dead");
            Assert.AreEqual(SessionState.Playing, _session.State, "the teammate plays on");
            Assert.AreEqual(mate, _session.Spectated, "watching the teammate");
            Assert.AreEqual(mate.Visual, Object.FindObjectsByType<CameraRig>(FindObjectsSortMode.None)[0].Target, "the camera follows them");
        }

        [Test]
        public void TheOwnersSoundsAreIn()
        {
            foreach (var id in new[] { SoundId.ZombieGroan, SoundId.ZombieDeath, SoundId.BossGroan, SoundId.BearGroan, SoundId.DogGroan, SoundId.RatGroan, SoundId.TigerGroan })
                Assert.IsNotNull(Resources.Load<AudioClip>($"Audio/{id}"), $"{id}");
            Assert.IsNotNull(Resources.Load<AudioClip>("Audio/AmbientNight"), "the night");
            Assert.IsNotNull(Resources.Load<AudioClip>("Audio/AmbientRain"), "the rain");
            Assert.IsNotNull(Resources.Load<AudioClip>("Audio/HelicopterLoop"), "the helicopter");
            Assert.AreEqual(SoundId.TigerGroan, Sfx.VoiceOf(ZombieType.Tiger));
            Assert.AreEqual(SoundId.BossGroan, Sfx.VoiceOf(ZombieType.Tank));
            Assert.AreEqual(SoundId.ZombieGroan, Sfx.VoiceOf(ZombieType.Fast));
            Assert.AreEqual(SoundId.ZombieDeath, Sfx.DeathOf(ZombieType.Basic));
            Assert.AreEqual(SoundId.DogGroan, Sfx.DeathOf(ZombieType.Dog));
        }
    }
}
