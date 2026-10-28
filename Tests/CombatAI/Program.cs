using System;
using Arthalion.AI;

class Program
{
    static int checks;
    static void Check(bool condition, string name) { checks++; if (!condition) throw new Exception(name); }
    static void Main()
    {
        foreach (bool player in new[] { false, true })
        {
            Check(CombatLearningContract.OutcomeReward(BattleResult.Draw, player) == 0, "Draw is neutral");
            Check(CombatLearningContract.OutcomeReward(BattleResult.TechnicalAbort, player) == 0, "Technical failure is not a defeat");
            Check(CombatLearningContract.OutcomeReward(BattleResult.PlayerWin, player) == (player ? 1 : -1), "Reward belongs to correct team");
            Check(CombatLearningContract.OutcomeReward(BattleResult.EnemyWin, player) == (player ? -1 : 1), "Reward is zero-sum");
        }
        for (int episode = 0; episode < 100; episode += 2)
        {
            Check(CombatLearningContract.EpisodeSeed(27, episode, true) == CombatLearningContract.EpisodeSeed(27, episode + 1, true), "Paired trials share a seed");
            Check(CombatLearningContract.EpisodeSeed(27, episode, true) != CombatLearningContract.EpisodeSeed(27, episode + 2, true), "Different trial pairs have different seeds");
            foreach (LearningSide side in Enum.GetValues<LearningSide>())
                Check(CombatLearningContract.ControlledPlayerSide(side, episode, true) != CombatLearningContract.ControlledPlayerSide(side, episode + 1, true), "Paired trials exchange policies between sides");
        }
        CombatLearningContract.WilsonInterval(200, 400, out double lower, out double upper);
        Check(Math.Abs(lower - 0.45123) < 0.0001 && Math.Abs(upper - 0.54877) < 0.0001, "Wilson interval for 400 trials");
        CombatLearningContract.WilsonInterval(0, 0, out lower, out upper);
        Check(lower == 0 && upper == 1, "No-data interval is uncertainty, not zero skill");
        Check(CombatLearningContract.ActionOptions > CombatLearningContract.BoardWidth * CombatLearningContract.BoardHeight, "Spell target completion has its own index");
        Console.WriteLine($"PASS: {checks} combat AI contract checks. Full scene/physics integration is tested with CombatAIEditor.RunSmokeTest.");
    }
}
