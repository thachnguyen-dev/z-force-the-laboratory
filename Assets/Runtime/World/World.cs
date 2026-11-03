namespace ZombieGame
{
    /// <summary>
    /// Who runs the simulation on this device. Solo, Co-op with AI and the online Host simulate everything
    /// (host-authoritative, Handoff 4 §7). An online client only shows what the Host sends: its objects are replicas
    /// that never run game logic — except its own character's movement, which the client owns.
    /// </summary>
    public static class World
    {
        public static bool Simulating = true;
    }

    /// <summary>Network ids for everything that is replicated. Assigned by whoever simulates.</summary>
    public static class NetIds
    {
        private static int _next = 1;

        public static int Next() => _next++;

        public static void Reset() => _next = 1;

        /// <summary>After host migration the new Host continues numbering above everything it already knows.</summary>
        public static void EnsureAbove(int id)
        {
            if (_next <= id) _next = id + 1;
        }
    }
}
