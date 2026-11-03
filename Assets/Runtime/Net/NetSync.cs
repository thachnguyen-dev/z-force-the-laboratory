using UnityEngine;

namespace ZombieGame
{
    /// <summary>
    /// The small surface gameplay code uses to talk to the network: the Host queues events for the client, and a client's
    /// own character sends its commands to the Host. Everything is a no-op outside an online match.
    /// </summary>
    public static class NetSync
    {
        private static NetHost Host
        {
            get
            {
                var s = NetSession.Instance;
                return s != null && s.Role == NetRole.Host && s.InMatch && s.PeerConnected ? s.HostSide : null;
            }
        }

        public static void Sound(SoundId id, Vector3 position, float volume) =>
            Host?.Queue(new NetEvent { Type = NetEventType.Sound, A = (int)id, V1 = position, F = volume });

        /// <param name="shooter">The survivor who fired (-1: none): the online friend draws their own shots themselves.</param>
        public static void Shot(Vector3 origin, Vector3 direction, float speed, float range, int shooter = -1) =>
            Host?.Queue(new NetEvent { Type = NetEventType.Shot, V1 = origin, V2 = direction * speed, F = range, A = shooter });

        public static void Explosion(Vector3 center, float radius) =>
            Host?.Queue(new NetEvent { Type = NetEventType.Explosion, V1 = center, F = radius });

        public static void Knife(Transform owner)
        {
            var host = Host;
            if (host == null) return;
            var survivor = owner.GetComponent<Survivor>();
            if (survivor != null) host.Queue(new NetEvent { Type = NetEventType.Knife, A = survivor.NetId });
        }

        public static void GrenadeThrown(Vector3 from, Vector3 to) =>
            Host?.Queue(new NetEvent { Type = NetEventType.Grenade, V1 = from, V2 = to });

        public static void BarrelGone(int id) => Host?.Queue(new NetEvent { Type = NetEventType.BarrelGone, A = id });

        /// <summary>A message for the online teammate's HUD ("Inventory full").</summary>
        public static void Toast(Survivor s, string text)
        {
            if (s.RemoteDriven) Host?.Queue(new NetEvent { Type = NetEventType.Toast, Text = text });
        }

        /// <summary>A message for the online teammate whoever it concerns.</summary>
        public static void ToastAll(string text) => Host?.Queue(new NetEvent { Type = NetEventType.Toast, Text = text });

        /// <summary>The online teammate's device moves them, so it applies the knockback.</summary>
        public static void Knockback(Survivor s, Vector3 push) => Host?.Queue(new NetEvent { Type = NetEventType.Knockback, V1 = push });

        /// <summary>Online client: an action for its own character, performed by the Host.</summary>
        public static void Command(Survivor s, NetCommand command, int a = 0, int b = 0)
        {
            var session = NetSession.Instance;
            if (session == null || session.Role != NetRole.Client || s.InputSource == null) return;
            session.ClientSide.SendCommand(command, a, b);
        }
    }
}
