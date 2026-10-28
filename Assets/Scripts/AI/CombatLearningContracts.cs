using System;

namespace Arthalion.AI
{
    public enum LearningMode { Disabled, Training, Evaluation, Inference }
    public enum LearningOpponent { AutoCombat, SelfPlay }
    public enum LearningSide { Enemy, Player }
    public enum BattleResult { Ongoing, PlayerWin, EnemyWin, Draw, TechnicalAbort }

    // The action vocabulary is versioned together with observations and trainer checkpoints.
    public enum CombatActionKind
    {
        FinishTurn, Move, Run, Retreat, Fly, StandardAttack, AllOutAttack,
        Charge, Grapple, Aim, Reload, StandUp, EquipMainHand, EquipOffHand,
        SelectMainHand, SelectOffHand, CastSpell, GrappleStrike, ReleaseGrapple,
        EscapeGrapple, EscapeEntanglement
    }

    public static class CombatLearningContract
    {
        public const int Version = 1;
        public const string BehaviorName = "ArthalionCombatV1";
        public const int BoardWidth = 32;
        public const int BoardHeight = 32;
        public const int ActionOptions = BoardWidth * BoardHeight + 1;
        public const int MaxUnits = 32;
        public const int MaxWeapons = 64;
        public const int MaxEffects = 32;
        public const int MaxSpells = 32;

        public static float OutcomeReward(BattleResult result, bool playerSide)
        {
            if (result == BattleResult.Draw || result == BattleResult.TechnicalAbort) return 0f;
            if (result == BattleResult.Ongoing) throw new ArgumentException("Battle has not finished.");
            return (result == BattleResult.PlayerWin) == playerSide ? 1f : -1f;
        }

        public static int EpisodeSeed(int baseSeed, int episode, bool paired)
        {
            unchecked { return baseSeed + (paired ? episode / 2 : episode) * 104729; }
        }

        public static bool ControlledPlayerSide(LearningSide side, int episode, bool swapSides)
        {
            return (side == LearningSide.Player) ^ (swapSides && (episode & 1) != 0);
        }

        public static void WilsonInterval(int wins, int games, out double lower, out double upper)
        {
            if (games <= 0) { lower = 0; upper = 1; return; }
            const double z = 1.959963984540054;
            double p = (double)wins / games, z2 = z * z;
            double center = (p + z2 / (2 * games)) / (1 + z2 / games);
            double radius = z * Math.Sqrt(p * (1 - p) / games + z2 / (4.0 * games * games)) / (1 + z2 / games);
            lower = Math.Max(0, center - radius); upper = Math.Min(1, center + radius);
        }
    }
}
