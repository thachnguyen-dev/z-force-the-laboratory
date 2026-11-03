using Unity.Netcode;
using UnityEngine;

namespace ZombieGame
{
    /// <summary>Actions the online client asks the Host to perform for its character.</summary>
    public enum NetCommand : byte { Select, UseHealth, DropLongGun, DropAmmo, DropHealth, DropGrenade, Pickup, TakeFromChest, Reload, DropMission, PlantC4, FreeHostage }

    public enum NetEventType : byte { Sound, Shot, Explosion, Knife, Grenade, BarrelGone, Toast, Knockback }

    /// <summary>A one-off thing that happened on the Host, replayed on the client.</summary>
    public struct NetEvent
    {
        public NetEventType Type;
        public int A;
        public float F;
        public Vector3 V1;
        public Vector3 V2;
        public string Text;
    }

    /// <summary>One character as the Host sees it.</summary>
    public struct SurvivorState
    {
        public float X, Z, Yaw, Hp;
        public bool Dead, Moving, Freed;
        public byte Selected, SwitchTarget;
        public float Switch;
        public byte PistolMag;
        public float PistolReload;
        public sbyte LongGun;
        public byte LongMag;
        public float LongReload;
        public short Shotgun, Ar, Rifle, Smg;
        /// <summary>Mission items carried: 1 = C4, 2 = Vaccine.</summary>
        public byte Mission;
        public byte Health, Grenades;
        public float Heal;
        public bool GrenadeAiming;
        public float GrenadeX, GrenadeZ;

        public void Write(FastBufferWriter w)
        {
            w.WriteValueSafe(X); w.WriteValueSafe(Z); w.WriteValueSafe(Yaw); w.WriteValueSafe(Hp);
            w.WriteValueSafe(Dead); w.WriteValueSafe(Moving); w.WriteValueSafe(Selected); w.WriteValueSafe(SwitchTarget);
            w.WriteValueSafe(Switch); w.WriteValueSafe(PistolMag); w.WriteValueSafe(PistolReload); w.WriteValueSafe(LongGun);
            w.WriteValueSafe(LongMag); w.WriteValueSafe(LongReload); w.WriteValueSafe(Shotgun); w.WriteValueSafe(Ar);
            w.WriteValueSafe(Rifle); w.WriteValueSafe(Smg); w.WriteValueSafe(Mission); w.WriteValueSafe(Health); w.WriteValueSafe(Grenades); w.WriteValueSafe(Heal);
            w.WriteValueSafe(GrenadeAiming); w.WriteValueSafe(GrenadeX); w.WriteValueSafe(GrenadeZ); w.WriteValueSafe(Freed);
        }

        public static SurvivorState Read(FastBufferReader r)
        {
            var s = new SurvivorState();
            r.ReadValueSafe(out s.X); r.ReadValueSafe(out s.Z); r.ReadValueSafe(out s.Yaw); r.ReadValueSafe(out s.Hp);
            r.ReadValueSafe(out s.Dead); r.ReadValueSafe(out s.Moving); r.ReadValueSafe(out s.Selected); r.ReadValueSafe(out s.SwitchTarget);
            r.ReadValueSafe(out s.Switch); r.ReadValueSafe(out s.PistolMag); r.ReadValueSafe(out s.PistolReload); r.ReadValueSafe(out s.LongGun);
            r.ReadValueSafe(out s.LongMag); r.ReadValueSafe(out s.LongReload); r.ReadValueSafe(out s.Shotgun); r.ReadValueSafe(out s.Ar);
            r.ReadValueSafe(out s.Rifle); r.ReadValueSafe(out s.Smg); r.ReadValueSafe(out s.Mission); r.ReadValueSafe(out s.Health); r.ReadValueSafe(out s.Grenades); r.ReadValueSafe(out s.Heal);
            r.ReadValueSafe(out s.GrenadeAiming); r.ReadValueSafe(out s.GrenadeX); r.ReadValueSafe(out s.GrenadeZ); r.ReadValueSafe(out s.Freed);
            return s;
        }
    }

    /// <summary>The client's controls for one frame. Fire edges are counters so a lost packet can't swallow a tap.</summary>
    public struct NetInput
    {
        public float X, Z, AimX, AimY, MoveX, MoveY, GrenadeDistance;
        public bool FireHeld, AimAssist, ThrowOnRelease;
        public int Presses, Releases;
        /// <summary>The client's clock when sent (the host plays the client's movement back smoothly from it).</summary>
        public float T;

        public void Write(FastBufferWriter w)
        {
            w.WriteValueSafe(X); w.WriteValueSafe(Z); w.WriteValueSafe(AimX); w.WriteValueSafe(AimY);
            w.WriteValueSafe(MoveX); w.WriteValueSafe(MoveY); w.WriteValueSafe(GrenadeDistance);
            w.WriteValueSafe(FireHeld); w.WriteValueSafe(AimAssist); w.WriteValueSafe(Presses); w.WriteValueSafe(Releases);
            w.WriteValueSafe(ThrowOnRelease);
            w.WriteValueSafe(T);
        }

        public static NetInput Read(FastBufferReader r)
        {
            var i = new NetInput();
            r.ReadValueSafe(out i.X); r.ReadValueSafe(out i.Z); r.ReadValueSafe(out i.AimX); r.ReadValueSafe(out i.AimY);
            r.ReadValueSafe(out i.MoveX); r.ReadValueSafe(out i.MoveY); r.ReadValueSafe(out i.GrenadeDistance);
            r.ReadValueSafe(out i.FireHeld); r.ReadValueSafe(out i.AimAssist); r.ReadValueSafe(out i.Presses); r.ReadValueSafe(out i.Releases);
            r.ReadValueSafe(out i.ThrowOnRelease);
            r.ReadValueSafe(out i.T);
            return i;
        }
    }

    public static class Wire
    {
        public const string Hello = "z.hello";
        public const string Start = "z.start";
        public const string Snapshot = "z.snap";
        public const string Loot = "z.loot";
        public const string Chests = "z.chests";
        public const string Events = "z.events";
        public const string End = "z.end";
        public const string Lobby = "z.lobby";
        public const string Input = "z.input";
        public const string Command = "z.cmd";
        public const string Character = "z.char";
        /// <summary>Every zombie alive, every 2 s, reliably: the client removes any replica the Host no longer has.</summary>
        public const string Roster = "z.roster";

        /// <summary>
        /// Owner, 2026-10-07 (Co-op over a join code was very laggy): the snapshot goes unreliably, so a lost one is just
        /// replaced by the next instead of holding everything behind it; it must fit one unreliable message.
        /// </summary>
        public const int SnapshotLimit = 1250;
        /// <summary>One zombie in a snapshot: id, type, position (cm, from -50 m), turn, health (half float), slam.</summary>
        public const int ZombieEntry = 13;

        public static void WriteZombie(FastBufferWriter w, int id, byte type, Vector3 p, float yaw, float hp, float slam)
        {
            w.WriteValueSafe(id);
            w.WriteValueSafe(type);
            w.WriteValueSafe(PackMetres(p.x));
            w.WriteValueSafe(PackMetres(p.z));
            w.WriteValueSafe((byte)Mathf.RoundToInt(Mathf.Repeat(yaw, 360f) / 360f * 255f));
            w.WriteValueSafe(Mathf.FloatToHalf(hp));
            w.WriteValueSafe((byte)Mathf.RoundToInt(Mathf.Clamp01(slam) * 255f));
        }

        public static void ReadZombie(FastBufferReader r, out int id, out byte type, out Vector3 p, out float yaw, out float hp, out float slam)
        {
            r.ReadValueSafe(out id);
            r.ReadValueSafe(out type);
            r.ReadValueSafe(out ushort x);
            r.ReadValueSafe(out ushort z);
            r.ReadValueSafe(out byte turn);
            r.ReadValueSafe(out ushort health);
            r.ReadValueSafe(out byte s);
            p = new Vector3(UnpackMetres(x), 0f, UnpackMetres(z));
            yaw = turn / 255f * 360f;
            hp = Mathf.HalfToFloat(health);
            slam = s / 255f;
        }

        public static ushort PackMetres(float m) => (ushort)Mathf.Clamp(Mathf.RoundToInt((m + 50f) * 100f), 0, 65535);
        public static float UnpackMetres(ushort v) => v / 100f - 50f;

        public static void Write(FastBufferWriter w, Vector3 v)
        {
            w.WriteValueSafe(v.x); w.WriteValueSafe(v.y); w.WriteValueSafe(v.z);
        }

        public static Vector3 ReadVector(FastBufferReader r)
        {
            r.ReadValueSafe(out float x); r.ReadValueSafe(out float y); r.ReadValueSafe(out float z);
            return new Vector3(x, y, z);
        }

        public static void Write(FastBufferWriter w, in LootData d)
        {
            w.WriteValueSafe((byte)d.Kind); w.WriteValueSafe((byte)d.Weapon); w.WriteValueSafe((byte)d.Ammo);
            w.WriteValueSafe(d.Amount); w.WriteValueSafe((short)d.Magazine);
        }

        public static LootData ReadLoot(FastBufferReader r)
        {
            r.ReadValueSafe(out byte kind); r.ReadValueSafe(out byte weapon); r.ReadValueSafe(out byte ammo);
            r.ReadValueSafe(out int amount); r.ReadValueSafe(out short magazine);
            return new LootData { Kind = (LootKind)kind, Weapon = (WeaponId)weapon, Ammo = (AmmoType)ammo, Amount = amount, Magazine = magazine };
        }
    }
}
