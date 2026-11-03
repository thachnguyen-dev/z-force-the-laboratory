namespace ZombieGame
{
    public enum MatchOutcome { Ongoing, Victory, Defeat }

    public static class MatchRules
    {
        /// <summary>
        /// D-067: the Main Boss dying ends the stage in Victory at once. D-098/D-101: no human alive is Defeat,
        /// whatever the AI teammate is doing.
        /// </summary>
        public static MatchOutcome Evaluate(bool mainBossDead, int humansAlive)
        {
            if (mainBossDead) return MatchOutcome.Victory;
            return humansAlive <= 0 ? MatchOutcome.Defeat : MatchOutcome.Ongoing;
        }

        /// <summary>
        /// Rescue stages (owner, 2026-09-30): Victory when the hostage is back at the start with a player. PLACEHOLDER:
        /// the hostage dying is a Defeat, and the Main Boss is not the objective there.
        /// </summary>
        public static MatchOutcome EvaluateRescue(bool hostageBackAtStart, bool hostageDead, int humansAlive)
        {
            if (humansAlive <= 0 || hostageDead) return MatchOutcome.Defeat;
            return hostageBackAtStart ? MatchOutcome.Victory : MatchOutcome.Ongoing;
        }
    }
}
