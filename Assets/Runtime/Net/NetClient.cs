using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace ZombieGame
{
    /// <summary>Who's in the match, as the Host announced it.</summary>
    public struct NetSurvivorInfo
    {
        public int Id;
        public string Name;
        public Color Color;
        public bool IsHuman;
        public bool IsHostage;
        public int CharacterIndex;
        public byte Team;
        public Vector3 Position;
    }

    /// <summary>
    /// Client side of an online match: builds the replica world from the Host's messages and sends this player's controls
    /// (their own movement is local — client-authoritative — everything else the Host decides).
    /// </summary>
    public class NetClient : MonoBehaviour
    {
        /// <summary>Owner, 2026-10-08 (lowering the lag): the friend's input 60 times a second (was 30).</summary>
        private const float InputInterval = 1f / 60f;

        private readonly Dictionary<int, Survivor> _survivors = new Dictionary<int, Survivor>();
        private readonly Dictionary<int, Zombie> _zombies = new Dictionary<int, Zombie>();
        private readonly Dictionary<int, Incubator> _incubators = new Dictionary<int, Incubator>();
        private readonly Dictionary<int, LootItem> _loot = new Dictionary<int, LootItem>();
        private readonly Dictionary<int, Chest> _chests = new Dictionary<int, Chest>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly List<int> _gone = new List<int>();

        private int _presses;
        private int _releases;
        private bool _wasHeld;
        private float _sendLeft;

        /// <summary>From the start message: what a migrating client needs to host the same match.</summary>
        public string HostPlayerId { get; private set; }
        public int Seed { get; private set; }
        public string StageName { get; private set; }

        /// <summary>Snapshots received so far (dev logging).</summary>
        public int Snapshots { get; private set; }

        private NetSession Session => NetSession.Instance;
        private static GameSession Game => GameSession.Instance;

        public void SendCommand(NetCommand command, int a, int b)
        {
            using var w = new FastBufferWriter(16, Allocator.Temp);
            w.WriteValueSafe((byte)command);
            w.WriteValueSafe(a);
            w.WriteValueSafe(b);
            Session.SendToHost(Wire.Command, w, NetworkDelivery.ReliableSequenced);
        }

        private void LateUpdate()
        {
            var session = Session;
            if (session == null || session.Role != NetRole.Client || !session.InMatch || Game == null) return;
            var me = Game.LocalSurvivor;
            if (me == null || me.InputSource == null) return;
            var input = me.InputSource.Read(me);
            if (input.FireHeld && !_wasHeld) _presses++;
            if (!input.FireHeld && _wasHeld) _releases++;
            _wasHeld = input.FireHeld;
            _sendLeft -= Time.unscaledDeltaTime;
            if (_sendLeft > 0f && !input.FirePressed && !input.FireReleased) return;
            _sendLeft = InputInterval;

            var p = me.transform.position;
            var aim = me.transform.forward;
            var net = new NetInput
            {
                X = p.x, Z = p.z, AimX = aim.x, AimY = aim.z, MoveX = input.Move.x, MoveY = input.Move.y,
                GrenadeDistance = input.GrenadeDistance, FireHeld = input.FireHeld, AimAssist = input.AimAssist, ThrowOnRelease = input.ThrowOnRelease,
                Presses = _presses, Releases = _releases, T = Time.unscaledTime,
            };
            using var w = new FastBufferWriter(64, Allocator.Temp);
            net.Write(w);
            session.SendToHost(Wire.Input, w, NetworkDelivery.UnreliableSequenced);
        }

        // ------------------------------------------------------------------ incoming

        public void OnStart(FastBufferReader r)
        {
            r.ReadValueSafe(out int seed);
            r.ReadValueSafe(out string stage);
            r.ReadValueSafe(out int localId);
            r.ReadValueSafe(out string hostPlayerId);
            HostPlayerId = hostPlayerId;
            Seed = seed;
            StageName = stage;
            r.ReadValueSafe(out int count);
            var infos = new List<NetSurvivorInfo>();
            for (var i = 0; i < count; i++)
            {
                var info = new NetSurvivorInfo();
                r.ReadValueSafe(out info.Id);
                r.ReadValueSafe(out info.Name);
                var c = Wire.ReadVector(r);
                info.Color = new Color(c.x, c.y, c.z);
                r.ReadValueSafe(out info.IsHuman);
                r.ReadValueSafe(out info.IsHostage);
                r.ReadValueSafe(out info.Team);
                r.ReadValueSafe(out info.CharacterIndex);
                info.Position = Wire.ReadVector(r);
                infos.Add(info);
            }
            Clear();
            Session.InMatch = true;
            foreach (var s in Game.StartReplicaStage(stage, seed, infos, localId)) _survivors[s.NetId] = s;
        }

        private void Clear()
        {
            _survivors.Clear();
            _zombies.Clear();
            _incubators.Clear();
            _loot.Clear();
            _chests.Clear();
            _presses = _releases = 0;
            _wasHeld = false;
            Snapshots = 0;
        }

        private bool _snapshotFailed;

        public void OnSnapshot(FastBufferReader r)
        {
            try
            {
                ReadSnapshot(r);
            }
            catch (System.Exception e)
            {
                if (_snapshotFailed) return;
                _snapshotFailed = true;
                Debug.LogWarning($"[ZOMBIE-NET] an update from the Host couldn't be read (a different build?): {e.Message}");
            }
        }

        private void ReadSnapshot(FastBufferReader r)
        {
            var stage = Game != null ? Game.Stage : null;
            if (stage == null || !Session.InMatch) return;
            Snapshots++;

            // The Host's clock when sent: the other characters are played back smoothly from it (owner, 2026-10-07).
            r.ReadValueSafe(out float sent);
            r.ReadValueSafe(out int survivors);
            for (var i = 0; i < survivors; i++)
            {
                r.ReadValueSafe(out int id);
                var state = SurvivorState.Read(r);
                if (_survivors.TryGetValue(id, out var s) && s != null) s.ApplyState(state, sent);
            }

            r.ReadValueSafe(out int incubators);
            _seen.Clear();
            for (var i = 0; i < incubators; i++)
            {
                r.ReadValueSafe(out int id);
                var position = Wire.ReadVector(r);
                r.ReadValueSafe(out float hp);
                _seen.Add(id);
                if (_incubators.TryGetValue(id, out var incubator) && incubator != null) incubator.ApplyReplica(hp);
                else
                {
                    var spawned = Incubator.SpawnReplica(stage, id, position, Game.Tuning.incubator);
                    if (stage.Layout.Bomb && (position - stage.Layout.BombTargetPosition).sqrMagnitude < 0.5f) spawned.MarkTarget();
                    _incubators[id] = spawned;
                }
                NetIds.EnsureAbove(id);
            }
            Retire(_incubators, incubator => Destroy(incubator.gameObject));
            r.ReadValueSafe(out bool planted);
            r.ReadValueSafe(out float fuse);
            Mission.ApplyReplica(planted, fuse, Wire.ReadVector(r));
            r.ReadValueSafe(out float rivalHold);
            Mission.ApplyRivalReplica(rivalHold);
            r.ReadValueSafe(out float endlessClock);
            r.ReadValueSafe(out int endlessKills);
            r.ReadValueSafe(out int endlessWave);
            if (endlessClock >= 0f) Endless.Current?.ApplyReplica(endlessClock, endlessKills, endlessWave);
            r.ReadValueSafe(out byte heliPhase);
            r.ReadValueSafe(out float heliProgress);
            if (heliPhase != 255) Helicopter.Current?.ApplyReplica((Helicopter.Phase)heliPhase, heliProgress);

            // Zombies that just died on the Host (repeated for a second, snapshots can be lost).
            r.ReadValueSafe(out byte deaths);
            for (var i = 0; i < deaths; i++)
            {
                r.ReadValueSafe(out int id);
                if (_zombies.TryGetValue(id, out var dead))
                {
                    if (dead != null) Kill(dead);
                    _zombies.Remove(id);
                }
            }
            // The zombies near this player (the rest keep their last place until they come near or the roster drops them).
            r.ReadValueSafe(out ushort zombies);
            for (var i = 0; i < zombies; i++)
            {
                Wire.ReadZombie(r, out var id, out var type, out var position, out var yaw, out var hp, out var slam);
                if (_zombies.TryGetValue(id, out var zombie) && zombie != null) zombie.ApplyReplica(position, yaw, hp, false, slam);
                else _zombies[id] = stage.SpawnZombieReplica((ZombieType)type, id, position, yaw);
                NetIds.EnsureAbove(id);
            }
        }

        public void OnLoot(FastBufferReader r)
        {
            if (Game == null || Game.Stage == null) return;
            r.ReadValueSafe(out int count);
            _seen.Clear();
            for (var i = 0; i < count; i++)
            {
                r.ReadValueSafe(out int id);
                var data = Wire.ReadLoot(r);
                var position = Wire.ReadVector(r);
                r.ReadValueSafe(out float yaw);
                r.ReadValueSafe(out bool respawns);
                _seen.Add(id);
                NetIds.EnsureAbove(id);
                if (_loot.TryGetValue(id, out var item) && item != null)
                {
                    item.Data = data;
                    item.Respawns = respawns;
                    continue;
                }
                item = LootItem.Spawn(position, data);
                item.NetId = id;
                item.Respawns = respawns;
                item.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                _loot[id] = item;
            }
            Retire(_loot, item => item.Consume());
        }

        public void OnChests(FastBufferReader r)
        {
            if (Game == null || Game.Stage == null) return;
            r.ReadValueSafe(out int count);
            _seen.Clear();
            for (var i = 0; i < count; i++)
            {
                r.ReadValueSafe(out int id);
                r.ReadValueSafe(out int owner);
                r.ReadValueSafe(out string title);
                var position = Wire.ReadVector(r);
                r.ReadValueSafe(out float yaw);
                r.ReadValueSafe(out float lifetime);
                r.ReadValueSafe(out int entries);
                _seen.Add(id);
                NetIds.EnsureAbove(id);
                if (!_chests.TryGetValue(id, out var chest) || chest == null)
                {
                    if (owner >= 0 && _survivors.TryGetValue(owner, out var dead) && dead != null)
                        chest = Chest.Corpse(Game.StageRoot, dead, lifetime);
                    else chest = Chest.Spawn(Game.StageRoot, position, yaw);
                    chest.NetId = id;
                    chest.OwnerId = owner;
                    chest.Title = title;
                    _chests[id] = chest;
                }
                chest.Lifetime = lifetime;
                chest.Contents.Clear();
                for (var e = 0; e < entries; e++) chest.Contents.Add(Wire.ReadLoot(r));
            }
            // A container that's gone took its corpse with it.
            Retire(_chests, chest =>
            {
                if (chest.RemoveWith != null) Destroy(chest.RemoveWith);
                Destroy(chest.gameObject);
            });
        }

        public void OnEvents(FastBufferReader r)
        {
            if (Game == null || Game.Stage == null) return;
            r.ReadValueSafe(out int count);
            for (var i = 0; i < count; i++)
            {
                r.ReadValueSafe(out byte type);
                r.ReadValueSafe(out int a);
                r.ReadValueSafe(out float f);
                var v1 = Wire.ReadVector(r);
                var v2 = Wire.ReadVector(r);
                r.ReadValueSafe(out string text);
                Play((NetEventType)type, a, f, v1, v2, text);
            }
        }

        private void Play(NetEventType type, int a, float f, Vector3 v1, Vector3 v2, string text)
        {
            switch (type)
            {
                case NetEventType.Sound: Sfx.PlayLocal((SoundId)a, v1, f); break;
                case NetEventType.Shot:
                    // Their own shots are drawn already, the moment they fired (Survivor.PredictFire).
                    if (a >= 0 && Game.LocalSurvivor != null && a == Game.LocalSurvivor.NetId) break;
                    var speed = v2.magnitude;
                    if (speed > 0.01f) Projectiles.Fire(v1, v2 / speed, speed, f, 0f, false);
                    break;
                case NetEventType.Explosion: ExplosionFx.Spawn(v1, f); break;
                case NetEventType.Knife:
                    if (_survivors.TryGetValue(a, out var s) && s != null) KnifeFx.Spawn(s.transform, Game.Tuning.knife.reach, Game.Tuning.knife.arcDeg);
                    break;
                case NetEventType.Grenade: Grenade.ThrowReplica(v1, v2, Game.Tuning.grenade); break;
                case NetEventType.BarrelGone:
                    foreach (var barrel in Barrel.All.ToArray())
                        if (barrel.NetId == a) barrel.RemoveReplica();
                    break;
                case NetEventType.Toast: Game.Hud?.Toast(text); break;
                case NetEventType.Knockback: Game.LocalSurvivor?.AddKnockback(v1); break;
            }
        }

        public void OnEnd(FastBufferReader r)
        {
            r.ReadValueSafe(out byte outcome);
            Game?.EndOnlineMatch((MatchOutcome)outcome);
        }

        private static void Kill(Zombie zombie) => zombie.ApplyReplica(zombie.transform.position, zombie.transform.eulerAngles.y, 0f, true, 0f);

        /// <summary>Every zombie alive on the Host: any replica not on it has died there.</summary>
        public void OnRoster(FastBufferReader r)
        {
            if (Game == null || Game.Stage == null || !Session.InMatch) return;
            r.ReadValueSafe(out int count);
            _seen.Clear();
            for (var i = 0; i < count; i++)
            {
                r.ReadValueSafe(out int id);
                _seen.Add(id);
            }
            Retire(_zombies, Kill);
        }

        private void Retire<T>(Dictionary<int, T> map, System.Action<T> remove) where T : Object
        {
            _gone.Clear();
            foreach (var pair in map)
                if (!_seen.Contains(pair.Key)) _gone.Add(pair.Key);
            foreach (var id in _gone)
            {
                if (map[id] != null) remove(map[id]);
                map.Remove(id);
            }
        }
    }
}
