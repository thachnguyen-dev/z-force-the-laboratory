using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace ZombieGame
{
    /// <summary>
    /// The online teammate's controls on the Host. D-179: while they're disconnected it gives nothing, so their character
    /// stands still and can be killed.
    /// </summary>
    public class RemoteInput : ICharacterInputSource
    {
        public NetInput Latest;
        public bool Live;
        private int _presses;
        private int _releases;

        public CharacterInput Read(Survivor self)
        {
            if (!Live) return new CharacterInput { GrenadeDistance = -1f };
            var i = new CharacterInput
            {
                Move = new Vector2(Latest.MoveX, Latest.MoveY),
                Aim = new Vector2(Latest.AimX, Latest.AimY),
                FireHeld = Latest.FireHeld,
                FirePressed = Latest.Presses != _presses,
                FireReleased = Latest.Releases != _releases,
                GrenadeDistance = Latest.GrenadeDistance,
                ThrowOnRelease = Latest.ThrowOnRelease,
                AimAssist = Latest.AimAssist,
            };
            _presses = Latest.Presses;
            _releases = Latest.Releases;
            return i;
        }
    }

    /// <summary>
    /// Host side of an online match: sends the client the world (10 snapshots a second, loot and containers when they
    /// change, one-off events every frame) and applies the client's input and commands to their character.
    /// </summary>
    public class NetHost : MonoBehaviour
    {
        /// <summary>Owner, 2026-10-07: 20 updates a second (the teammate's movement is played back from them).</summary>
        private const float SnapshotInterval = 1f / 20f;
        private const float RosterInterval = 2f;
        /// <summary>Zombies further than this from the friend's character aren't in their snapshot (well off their screen).</summary>
        private const float SnapshotRange = 45f;
        /// <summary>A zombie's death is repeated in the snapshots this long (they can be lost).</summary>
        private const float DeathRepeat = 1f;
        private float _rosterLeft;
        private readonly List<(int id, float time)> _deaths = new List<(int id, float time)>();
        private readonly List<Zombie> _near = new List<Zombie>();

        private void OnEnable() => Zombie.Killed += OnZombieKilled;
        private void OnDisable() => Zombie.Killed -= OnZombieKilled;
        private void OnZombieKilled(Zombie z) => _deaths.Add((z.NetId, Time.unscaledTime));

        private readonly List<NetEvent> _events = new List<NetEvent>();
        private Survivor _remote;
        private RemoteInput _remoteInput;
        private int _seed;
        private string _stage;
        private float _snapshotLeft;
        private int _lootSent = int.MinValue;
        private int _chestsSent = int.MinValue;

        private NetSession Session => NetSession.Instance;

        public void BeginMatch(Survivor remote, RemoteInput input, int seed, string stageName)
        {
            _remote = remote;
            _remoteInput = input;
            _seed = seed;
            _stage = stageName;
            _events.Clear();
            remote.Message += text => NetSync.Toast(remote, text);
            Session.InMatch = true;
            SendStart();
        }

        public void Queue(NetEvent e)
        {
            if (_events.Count < 1024) _events.Add(e);
        }

        public bool CanRejoin() => _remote != null && !_remote.IsDead;

        /// <summary>D-179: the character stays and can be killed; it takes no input until they're back.</summary>
        public void OnPeerLost()
        {
            if (_remoteInput != null) _remoteInput.Live = false;
            _events.Clear();
        }

        /// <summary>D-180: back into the same character — resend everything.</summary>
        public void OnPeerRejoined()
        {
            _lootSent = _chestsSent = int.MinValue;
            SendStart();
        }

        public void EndMatch(MatchOutcome outcome)
        {
            FlushEvents();
            using var w = new FastBufferWriter(16, Allocator.Temp);
            w.WriteValueSafe((byte)outcome);
            Session.SendToPeer(Wire.End, w, NetworkDelivery.ReliableSequenced);
        }

        /// <summary>The Host left the result screen for the lobby; the client waits for the next stage.</summary>
        public void ReturnToLobby()
        {
            Session.InMatch = false;
            _remote = null;
            _remoteInput = null;
            using var w = new FastBufferWriter(8, Allocator.Temp);
            w.WriteValueSafe((byte)0);
            Session.SendToPeer(Wire.Lobby, w, NetworkDelivery.ReliableSequenced);
        }

        private void LateUpdate()
        {
            var session = Session;
            if (session == null || session.Role != NetRole.Host || !session.InMatch) return;
            if (!session.PeerConnected)
            {
                _events.Clear();
                return;
            }
            FlushEvents();
            _snapshotLeft -= Time.unscaledDeltaTime;
            if (_snapshotLeft > 0f) return;
            _snapshotLeft = SnapshotInterval;
            SendSnapshot();
            _rosterLeft -= SnapshotInterval;
            if (_rosterLeft <= 0f)
            {
                _rosterLeft = RosterInterval;
                SendRoster();
            }
            if (_lootSent != LootItem.Version) SendLoot();
            if (_chestsSent != Chest.Version) SendChests();
        }

        // ------------------------------------------------------------------ outgoing

        private void SendStart()
        {
            using var w = new FastBufferWriter(512, Allocator.Temp, 8192);
            w.WriteValueSafe(_seed);
            w.WriteValueSafe(_stage);
            w.WriteValueSafe(_remote.NetId);
            w.WriteValueSafe(Session.LocalPlayerId);
            w.WriteValueSafe(Survivor.All.Count);
            foreach (var s in Survivor.All)
            {
                w.WriteValueSafe(s.NetId);
                w.WriteValueSafe(s.DisplayName);
                Wire.Write(w, new Vector3(s.BodyColor.r, s.BodyColor.g, s.BodyColor.b));
                w.WriteValueSafe(s.IsHuman);
                w.WriteValueSafe(s.IsHostage);
                w.WriteValueSafe((byte)s.Team);
                w.WriteValueSafe(System.Array.IndexOf(s.Tuning.characters, s.Character));
                Wire.Write(w, s.transform.position);
            }
            Session.SendToPeer(Wire.Start, w, NetworkDelivery.ReliableFragmentedSequenced);
            _lootSent = _chestsSent = int.MinValue;
        }

        /// <summary>
        /// Owner, 2026-10-07 (laggy over a join code): 15 a second, unreliable (a lost one is replaced by the next instead of
        /// holding the rest back), packed into one message: the survivors, the mission state, the zombies that just died
        /// (repeated for a second), then as many of the zombies near the friend's character as fit, nearest first.
        /// </summary>
        private void SendSnapshot()
        {
            using var w = new FastBufferWriter(Wire.SnapshotLimit, Allocator.Temp, 1 << 16);
            WriteSnapshot(w, _remote != null ? _remote.transform.position : Vector3.zero);
            // Too big for one unreliable message only if the survivors and incubators alone overflow it: then reliably.
            Session.SendToPeer(Wire.Snapshot, w, w.Length <= Wire.SnapshotLimit ? NetworkDelivery.UnreliableSequenced : NetworkDelivery.ReliableFragmentedSequenced);
        }

        /// <summary>Every zombie alive (reliable, every 2 s): the client removes replicas the Host no longer has.</summary>
        private void SendRoster()
        {
            using var w = new FastBufferWriter(1024, Allocator.Temp, 1 << 16);
            w.WriteValueSafe(Zombie.Alive.Count);
            foreach (var z in Zombie.Alive) w.WriteValueSafe(z.NetId);
            Session.SendToPeer(Wire.Roster, w, NetworkDelivery.ReliableFragmentedSequenced);
        }

        /// <summary>The snapshot as the friend at <paramref name="friend"/> gets it (public for the size test).</summary>
        public void WriteSnapshot(FastBufferWriter w, Vector3 friend)
        {
            w.WriteValueSafe(Time.unscaledTime);
            w.WriteValueSafe(Survivor.All.Count);
            foreach (var s in Survivor.All)
            {
                w.WriteValueSafe(s.NetId);
                s.Capture().Write(w);
            }
            w.WriteValueSafe(Incubator.All.Count);
            foreach (var i in Incubator.All)
            {
                w.WriteValueSafe(i.NetId);
                Wire.Write(w, i.transform.position);
                w.WriteValueSafe(i.Hp);
            }
            // Bomb stages (owner, 2026-10-02): the planted C4 and its fuse.
            w.WriteValueSafe(Mission.Planted);
            w.WriteValueSafe(Mission.FuseLeft);
            Wire.Write(w, Mission.BombPosition);
            w.WriteValueSafe(Mission.RivalHoldLeft);
            // Endless mode: the run's clock (the countdown and the weather follow it), kills and wave.
            var endless = Endless.Current;
            w.WriteValueSafe(endless != null ? endless.Clock : -1f);
            w.WriteValueSafe(endless != null ? endless.Kills : 0);
            w.WriteValueSafe(endless != null ? endless.Wave : 0);
            // The helicopter (owner, 2026-10-08): the friend's follows the Host's take-off.
            var heli = Helicopter.Current;
            w.WriteValueSafe(heli != null ? (byte)heli.State : (byte)255);
            w.WriteValueSafe(heli != null ? heli.Progress : 0f);
            // Deaths in the last second.
            var now = Time.unscaledTime;
            _deaths.RemoveAll(d => now - d.time > DeathRepeat);
            var deaths = Mathf.Min(_deaths.Count, 40);
            w.WriteValueSafe((byte)deaths);
            for (var i = 0; i < deaths; i++) w.WriteValueSafe(_deaths[_deaths.Count - 1 - i].id);
            // The zombies near the friend, nearest first, as many as fit.
            _near.Clear();
            foreach (var z in Zombie.Alive)
            {
                var d = z.transform.position - friend;
                if (d.x * d.x + d.z * d.z < SnapshotRange * SnapshotRange) _near.Add(z);
            }
            _near.Sort((a, b) => Flat(a.transform.position - friend).CompareTo(Flat(b.transform.position - friend)));
            var room = Mathf.Max(0, (Wire.SnapshotLimit - w.Length - 2) / Wire.ZombieEntry);
            var count = Mathf.Min(_near.Count, room, ushort.MaxValue);
            w.WriteValueSafe((ushort)count);
            for (var i = 0; i < count; i++)
            {
                var z = _near[i];
                Wire.WriteZombie(w, z.NetId, (byte)z.Def.type, z.transform.position, z.transform.eulerAngles.y, z.Hp, z.SlamProgress);
            }
        }

        private static float Flat(Vector3 d) => d.x * d.x + d.z * d.z;

        private void SendLoot()
        {
            _lootSent = LootItem.Version;
            using var w = new FastBufferWriter(2048, Allocator.Temp, 1 << 17);
            w.WriteValueSafe(LootItem.All.Count);
            foreach (var item in LootItem.All)
            {
                w.WriteValueSafe(item.NetId);
                Wire.Write(w, item.Data);
                Wire.Write(w, item.transform.position);
                w.WriteValueSafe(item.transform.eulerAngles.y);
                w.WriteValueSafe(item.Respawns);
            }
            Session.SendToPeer(Wire.Loot, w, NetworkDelivery.ReliableFragmentedSequenced);
        }

        private void SendChests()
        {
            _chestsSent = Chest.Version;
            using var w = new FastBufferWriter(1024, Allocator.Temp, 1 << 17);
            w.WriteValueSafe(Chest.All.Count);
            foreach (var chest in Chest.All)
            {
                w.WriteValueSafe(chest.NetId);
                w.WriteValueSafe(chest.OwnerId);
                w.WriteValueSafe(chest.Title);
                Wire.Write(w, chest.transform.position);
                w.WriteValueSafe(chest.transform.eulerAngles.y);
                w.WriteValueSafe(chest.Lifetime);
                w.WriteValueSafe(chest.Contents.Count);
                foreach (var entry in chest.Contents) Wire.Write(w, entry);
            }
            Session.SendToPeer(Wire.Chests, w, NetworkDelivery.ReliableFragmentedSequenced);
        }

        private void FlushEvents()
        {
            if (_events.Count == 0) return;
            using var w = new FastBufferWriter(1024, Allocator.Temp, 1 << 17);
            w.WriteValueSafe(_events.Count);
            foreach (var e in _events)
            {
                w.WriteValueSafe((byte)e.Type);
                w.WriteValueSafe(e.A);
                w.WriteValueSafe(e.F);
                Wire.Write(w, e.V1);
                Wire.Write(w, e.V2);
                w.WriteValueSafe(e.Text ?? "");
            }
            _events.Clear();
            Session.SendToPeer(Wire.Events, w, NetworkDelivery.ReliableFragmentedSequenced);
        }

        // ------------------------------------------------------------------ incoming

        public void OnInput(FastBufferReader reader)
        {
            var input = NetInput.Read(reader);
            if (_remote == null || _remoteInput == null) return;
            _remoteInput.Latest = input;
            _remoteInput.Live = true;
            _remote.RemoteTarget = new Vector3(input.X, 0f, input.Z);
            // What the Host sees of the friend is played back smoothly; the character itself stays where they really are.
            _remote.AddRemoteSample(input.T, _remote.RemoteTarget, Mathf.Atan2(input.AimX, input.AimY) * Mathf.Rad2Deg);
        }

        public void OnCommand(FastBufferReader reader)
        {
            reader.ReadValueSafe(out byte command);
            reader.ReadValueSafe(out int a);
            reader.ReadValueSafe(out int b);
            var s = _remote;
            if (s == null || s.IsDead) return;
            switch ((NetCommand)command)
            {
                case NetCommand.Select: s.Select((Slot)a); break;
                case NetCommand.UseHealth: s.UseHealthItem(); break;
                case NetCommand.DropLongGun: s.DropLongGun(); break;
                case NetCommand.DropAmmo: s.DropAmmo((AmmoType)a, b); break;
                case NetCommand.DropHealth: s.DropHealthItem(); break;
                case NetCommand.DropGrenade: s.DropGrenade(); break;
                case NetCommand.Reload: s.ActiveFirearm?.RequestReload(); break;
                case NetCommand.DropMission: s.DropMissionItem((LootKind)a); break;
                case NetCommand.PlantC4: s.PlantC4(); break;
                case NetCommand.FreeHostage: s.FreeHostage(); break;
                case NetCommand.Pickup:
                    foreach (var item in LootItem.All)
                    {
                        if (item.NetId != a) continue;
                        var d = item.transform.position - s.transform.position;
                        d.y = 0f;
                        // A little extra reach: positions arrive a moment late.
                        if (d.magnitude <= s.Tuning.player.pickupRange + 1f) s.TryPickup(item);
                        break;
                    }
                    break;
                case NetCommand.TakeFromChest:
                    foreach (var chest in Chest.All)
                        if (chest.NetId == a)
                        {
                            s.TakeFromChest(chest, b);
                            break;
                        }
                    break;
            }
        }
    }
}
