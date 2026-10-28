using UnityEngine;
using UnityEngine.SceneManagement;

namespace Arthalion.AI
{
    public static class CombatAIBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void OpenBattleScene()
        {
            if (ReinforcementLearningManager.CommandLine("--ai-mode") != null &&
                SceneManager.GetActiveScene().name != "BattleScene") SceneManager.LoadScene("BattleScene");
        }
    }
}
