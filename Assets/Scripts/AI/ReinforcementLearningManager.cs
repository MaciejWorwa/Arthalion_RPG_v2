using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.IO;
using System;
using System.Globalization;
using UnitStates;
using TMPro;

namespace UnitStates
{
    public enum AIState
    {
        CanDoAction,
        CanMove,
        IsHeavilyWounded,
        IsTargetHeavilyWounded,
        IsStrongerThanTarget,
        IsBeyondAttackRange,
        IsInChargeRange,
        HasRangedWeapon,
        WeaponIsLoaded,
        TargetBehindObstacle,
        IsAiming,

        COUNT
    }
}

public enum TargetType
{
    None = 0,
    Closest,
    Furthest,
    MostInjured,
    LeastInjured,
    Weakest,
    Strongest,
    MostAlliesNearby
}

public enum AttackType
{
    Move = 0,
    Run,
    Null,
    Charge,
    AllOutAttack,
    Aim,
    Reload,
    FinishTurn,
    MoveAway,
    RunAway,
    Retreat,
    ChangeWeaponToRanged,
    ChangeWeaponToMelee
}

public class ActionDefinition
{
    public TargetType targetType;
    public AttackType attackType;

    public ActionDefinition(TargetType t, AttackType a)
    {
        targetType = t;
        attackType = a;
    }
}

public class ReinforcementLearningManager : MonoBehaviour
{
    public static ReinforcementLearningManager Instance { get; private set; }

    [Header("Q-learning parameters")]
    public float Alpha = 0.1f;
    public float Gamma = 0.9f;
    public float Epsilon = 0.2f;
    public float EpsilonStart = 1.0f;
    public float EpsilonEnd = 0.05f;
    public int EpsilonDecayEpochs = 1000;

    public float StepPenalty = -0.2f;

    private int currentEpoch = 0;

    [Header("Logging Parameters")]
    public int ActionsPerEpoch = 500;
    public SimpleGraph simpleGraph;
    [Tooltip("Co ile epok zapisywac automatycznie Q-tabele. 0 = brak autozapisu.")]
    public int AutoSaveEveryEpochs = 10;
    [Header("Training Debug")]
    [SerializeField] private TMP_Text _trainingDebugDisplay;
    [Min(1)] public int LearningSettleFrames = 2;
    [Min(10)] public int LearningStepTimeoutFrames = 240;
    [Min(1)] public int MaxLearningIterationsPerUnitTurn = 8;
    public bool EnableStepCsvLogging = true;

    [Header("Periodic Evaluation")]
    public bool EnablePeriodicEvaluation = true;
    [Min(1)] public int EvaluateEveryEpisodes = 50;
    [Min(1)] public int EvaluationEpisodesCount = 20;

    private int _totalEpisodeCount = 0;
    private int _trainingEpisodeCount = 0;
    private int _evaluationEpisodeCount = 0;
    private int _globalStepIndex = 0;
    private int _currentEpisodeStepCount = 0;
    private int _invalidActionCount = 0;
    private int _noOpStepCount = 0;
    private int _timeoutCount = 0;
    private float _currentEpisodeReward = 0f;

    private bool _isEvaluationMode = false;
    private int _evaluationEpisodesLeft = 0;
    private int _evaluationWins = 0;
    private int _evaluationPlayed = 0;
    private bool _stepTimeoutFlag = false;

    private readonly Queue<int> _recentEpisodeWins = new();
    private readonly Queue<float> _recentEpisodeRewards = new();
    private const int RECENT_EPISODES_WINDOW = 100;

    private string _currentRunFolder = string.Empty;
    private string _stepLogPath = string.Empty;
    private List<float> epochRewards = new List<float>();
    private float currentEpochReward = 0f;
    private int actionsThisEpoch = 0;

    private const float WIN_REWARD = 100f;
    private const float LOSS_REWARD = -30f;

    [SerializeField] private int _playerWins;
    [SerializeField] private int _enemyWins;
    [SerializeField] private TMP_Text _teamWinsDisplay;

    private const int ACTION_COUNT = 43;

    // ZMIANA: Zamiast float[,] używamy Dictionary.
    // Klucz zewnętrzny: Rasa
    // Klucz wewnętrzny: State Index (int)
    // Wartość: Tablica Q-Values dla akcji (float[])
    private Dictionary<string, Dictionary<int, float[]>> QTables = new Dictionary<string, Dictionary<int, float[]>>();

    public bool IsLearning;
    private bool _wasLearningEnabledLastFrame;
    private HashSet<string> _trainedRaces = new HashSet<string>();

    private struct LastStep
    {
        public string Race;
        public int State;
        public int Action;
        public float ImmediateReward;
        public Unit Target;
        public bool TargetExisted;
        public int PrevSelfHP;
        public int PrevTargetHP;
        public int TargetOverall;
        public int ValidActionsCount;
        public bool ForcedFallback;
        public bool HasValue;
    }

    private readonly Dictionary<Unit, LastStep> _lastStepByUnit = new();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    void Start()
    {
        _wasLearningEnabledLastFrame = IsLearning;
        if (!IsLearning) return;

        StartLearningSession(forceLoadFromDisk: true);
    }

    void Update()
    {
        if (IsLearning && !_wasLearningEnabledLastFrame)
        {
            // Gdy IsLearning jest przełączone w runtime (np. toggle w Inspectorze),
            // od razu dociągamy zapisane Q-tabele, żeby kontynuować trening.
            StartLearningSession(forceLoadFromDisk: false);
        }

        _wasLearningEnabledLastFrame = IsLearning;

        if (Input.GetKeyDown(KeyCode.R) && IsLearning) SaveQTables();
    }

    private void StartLearningSession(bool forceLoadFromDisk)
    {
        if (forceLoadFromDisk || QTables.Count == 0)
        {
            LoadQTables();
        }

        InitializeTrainingRun();
        UpdateTrainingDebugUI();
    }

    void OnApplicationQuit()
    {
        if (IsLearning) SaveQTables();
    }
    public int GetMaxLearningIterationsPerUnitTurn()
    {
        return Mathf.Max(1, MaxLearningIterationsPerUnitTurn);
    }

    public int GetLearningSettleFrames()
    {
        return Mathf.Max(1, LearningSettleFrames);
    }

    public int GetLearningStepTimeoutFrames()
    {
        return Mathf.Max(10, LearningStepTimeoutFrames);
    }

    public bool ConsumeStepTimeoutFlag()
    {
        bool value = _stepTimeoutFlag;
        _stepTimeoutFlag = false;
        return value;
    }

    private float CurrentExplorationRate()
    {
        return _isEvaluationMode ? 0f : Epsilon;
    }

    public void ReportLearningStepTimeout(Unit unit)
    {
        _stepTimeoutFlag = true;
        _timeoutCount++;
        LogStepRow("timeout", unit, null, -1, 0f, 0f, 0f);
        UpdateTrainingDebugUI();
    }

    public void NotifyEpisodeEnd(bool didAIWin)
    {
        _totalEpisodeCount++;

        if (_isEvaluationMode)
        {
            _evaluationEpisodeCount++;
            _evaluationPlayed++;
            if (didAIWin) _evaluationWins++;
            _evaluationEpisodesLeft--;

            if (_evaluationEpisodesLeft <= 0)
            {
                float evalWinRate = _evaluationPlayed > 0 ? (float)_evaluationWins / _evaluationPlayed : 0f;
                Debug.Log($"[RL] Evaluation finished: wins={_evaluationWins}/{_evaluationPlayed} ({evalWinRate:P1}).");
                _isEvaluationMode = false;
                _evaluationPlayed = 0;
                _evaluationWins = 0;
            }
        }
        else
        {
            _trainingEpisodeCount++;
            PushWindow(_recentEpisodeWins, didAIWin ? 1 : 0, RECENT_EPISODES_WINDOW);
            PushWindow(_recentEpisodeRewards, _currentEpisodeReward, RECENT_EPISODES_WINDOW);

            if (EnablePeriodicEvaluation && EvaluateEveryEpisodes > 0 && _trainingEpisodeCount % EvaluateEveryEpisodes == 0)
            {
                _isEvaluationMode = true;
                _evaluationEpisodesLeft = Mathf.Max(1, EvaluationEpisodesCount);
                _evaluationPlayed = 0;
                _evaluationWins = 0;
                Debug.Log($"[RL] Starting evaluation block: {_evaluationEpisodesLeft} episodes (epsilon=0, learning OFF).");
            }
        }

        _currentEpisodeReward = 0f;
        _currentEpisodeStepCount = 0;
        UpdateTrainingDebugUI();
    }

    private static void PushWindow<T>(Queue<T> queue, T value, int window)
    {
        queue.Enqueue(value);
        while (queue.Count > window) queue.Dequeue();
    }

    private float GetRecentWinRate()
    {
        if (_recentEpisodeWins.Count == 0) return 0f;
        return (float)_recentEpisodeWins.Average();
    }

    private float GetRecentEpisodeReward()
    {
        if (_recentEpisodeRewards.Count == 0) return 0f;
        return _recentEpisodeRewards.Average();
    }

    private void UpdateTrainingDebugUI()
    {
        if (_trainingDebugDisplay == null) return;

        string mode = _isEvaluationMode ? "EVAL" : "TRAIN";
        float winRate = GetRecentWinRate() * 100f;
        float avgReward = GetRecentEpisodeReward();
        float invalidRate = _globalStepIndex > 0 ? (100f * _invalidActionCount / _globalStepIndex) : 0f;
        float timeoutRate = _globalStepIndex > 0 ? (100f * _timeoutCount / _globalStepIndex) : 0f;
        float noOpRate = _globalStepIndex > 0 ? (100f * _noOpStepCount / _globalStepIndex) : 0f;

        _trainingDebugDisplay.text =
                    $"Episodes T/E/All: {_trainingEpisodeCount}/{_evaluationEpisodeCount}/{_totalEpisodeCount}\n" +
                    $"Epoch: {currentEpoch}\n" +
                    $"WinRate(100): {winRate:F1}%  AvgReward(100): {avgReward:F2}\n" +
                    $"Fallback%: {invalidRate:F1}  Timeout%: {timeoutRate:F1}  NoOp%: {noOpRate:F1}";
    }

    private void InitializeTrainingRun()
    {
        if (!EnableStepCsvLogging) return;

        string root = Path.Combine(Application.persistentDataPath, "training_runs");
        Directory.CreateDirectory(root);

        _currentRunFolder = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd_HHmmss"));
        Directory.CreateDirectory(_currentRunFolder);

        _stepLogPath = Path.Combine(_currentRunFolder, "steps.csv");
        File.WriteAllText(_stepLogPath,
            "event;mode;episode;round;global_step;episode_step;unit_id;race;state;action;next_state;target_id;valid_actions;fallback;reward;old_q;new_q;epsilon;self_hp;target_hp\n");

        Debug.Log($"[RL] Step log file: {_stepLogPath}");
    }

    private void LogStepRow(string eventType, Unit unit, LastStep? prevNullable, int nextState, float reward, float oldQ, float newQ)
    {
        if (!EnableStepCsvLogging || string.IsNullOrEmpty(_stepLogPath)) return;

        LastStep prev = prevNullable ?? default;
        int unitId = unit != null ? unit.UnitId : -1;
        string race = (unit != null && unit.Stats != null && !string.IsNullOrEmpty(unit.Stats.Race))
            ? unit.Stats.Race
            : (!string.IsNullOrEmpty(prev.Race) ? prev.Race : "unknown");
        int state = prev.State;
        int action = prev.Action;
        int targetId = prev.Target != null ? prev.Target.UnitId : -1;
        int validActions = prev.ValidActionsCount;
        int fallback = prev.ForcedFallback ? 1 : 0;
        int selfHp = unit != null && unit.Stats != null ? unit.Stats.TempHealth : -1;
        int targetHp = prev.Target != null && prev.Target.Stats != null ? prev.Target.Stats.TempHealth : -1;
        string mode = _isEvaluationMode ? "EVAL" : "TRAIN";

        string line = string.Join(";", new string[]
        {
            eventType,
            mode,
            _totalEpisodeCount.ToString(),
            RoundsManager.RoundNumber.ToString(),
            _globalStepIndex.ToString(),
            _currentEpisodeStepCount.ToString(),
            unitId.ToString(),
            race,
            state.ToString(),
            action.ToString(),
            nextState.ToString(),
            targetId.ToString(),
            validActions.ToString(),
            fallback.ToString(),
            F(reward),
            F(oldQ),
            F(newQ),
            F(CurrentExplorationRate()),
            selfHp.ToString(),
            targetHp.ToString(),
        });

        File.AppendAllText(_stepLogPath, line + Environment.NewLine);
    }

    private static string F(float value)
    {
        return value.ToString("F4", CultureInfo.InvariantCulture);
    }

    // ======================================================================
    //             DOSTĘP DO TABLICY Q (SPARSE MATRIX)
    // ======================================================================

    public void RegisterRace(string raceName)
    {
        if (string.IsNullOrEmpty(raceName)) return;
        if (!QTables.ContainsKey(raceName))
        {
            QTables[raceName] = new Dictionary<int, float[]>();
        }
    }

    // Pobiera tablicę wartości Q dla danego stanu. Jeśli stan nie istnieje, tworzy go.
    private float[] GetStateQValues(string raceName, int stateIndex)
    {
        RegisterRace(raceName);
        var raceTable = QTables[raceName];

        if (!raceTable.ContainsKey(stateIndex))
        {
            // Leniwa inicjalizacja - tworzymy wpis tylko gdy jest potrzebny
            raceTable[stateIndex] = new float[ACTION_COUNT];
        }

        return raceTable[stateIndex];
    }

    // ======================================================================
    //                     PODSTAWOWE METODY Q-LEARNING
    // ======================================================================

    public int EncodeState(bool[] states)
    {
        int stateIndex = 0;
        for (int i = 0; i < states.Length; i++)
        {
            if (states[i]) stateIndex |= (1 << i);
        }
        return stateIndex;
    }

    private bool[] DecodeState(int stateIndex)
    {
        int n = (int)AIState.COUNT;
        bool[] s = new bool[n];
        for (int i = 0; i < n; i++) s[i] = (stateIndex & (1 << i)) != 0;
        return s;
    }

    public class ActionChoice
    {
        public int ActionId;
        public Unit ChosenTarget;
        public bool[] ChosenStates;
        public int ValidActionCount;
        public bool ForcedFallback;
    }

    // --- WYBÓR AKCJI ---
    private ActionChoice ChooseValidActionEpsilonGreedy(ActionContext context, Unit unit)
    {
        float explorationRate = CurrentExplorationRate();

        // 1. Eksploracja
        if (UnityEngine.Random.value < explorationRate)
        {
            List<int> potentialActions = new List<int>();
            for (int i = 0; i < ACTION_COUNT; i++)
            {
                Unit t = ResolveTargetFromAction(null, i, context.Info);
                if (t != null || AllActions[i].targetType == TargetType.None)
                {
                    bool[] tempStates = DetermineStates(unit, t);
                    if (IsActionValidForUnitAndTarget(unit, t, i, tempStates))
                    {
                        potentialActions.Add(i);
                    }
                }
            }

            if (potentialActions.Count > 0)
            {
                int rndIdx = potentialActions[UnityEngine.Random.Range(0, potentialActions.Count)];
                Unit rndTarget = ResolveTargetFromAction(null, rndIdx, context.Info);
                return new ActionChoice
                {
                    ActionId = rndIdx,
                    ChosenTarget = rndTarget,
                    ChosenStates = DetermineStates(unit, rndTarget),
                    ValidActionCount = potentialActions.Count,
                    ForcedFallback = false
                };
            }

            return new ActionChoice
            {
                ActionId = (int)AttackType.FinishTurn,
                ChosenTarget = null,
                ChosenStates = DetermineStates(unit, null),
                ValidActionCount = 0,
                ForcedFallback = true
            };
        }

        // 2. Eksploatacja
        float maxQ = float.MinValue;
        List<ActionChoice> bestChoices = new List<ActionChoice>();
        int validActionsCount = 0;

        for (int actId = 0; actId < AllActions.Length; actId++)
        {
            Unit potentialTarget = ResolveTargetFromAction(null, actId, context.Info);

            if (AllActions[actId].targetType != TargetType.None && potentialTarget == null)
                continue;

            bool[] currentStates = DetermineStates(unit, potentialTarget);

            if (!IsActionValidForUnitAndTarget(unit, potentialTarget, actId, currentStates))
                continue;

            validActionsCount++;
            int stateIdx = EncodeState(currentStates);

            float[] stateQValues = GetStateQValues(context.RaceName, stateIdx);
            float q = stateQValues[actId];

            if (q > maxQ)
            {
                maxQ = q;
                bestChoices.Clear();
                bestChoices.Add(new ActionChoice
                {
                    ActionId = actId,
                    ChosenTarget = potentialTarget,
                    ChosenStates = currentStates,
                    ValidActionCount = validActionsCount,
                    ForcedFallback = false
                });
            }
            else if (Mathf.Abs(q - maxQ) < 0.001f)
            {
                bestChoices.Add(new ActionChoice
                {
                    ActionId = actId,
                    ChosenTarget = potentialTarget,
                    ChosenStates = currentStates,
                    ValidActionCount = validActionsCount,
                    ForcedFallback = false
                });
            }
        }

        if (bestChoices.Count > 0)
        {
            ActionChoice selected = bestChoices[UnityEngine.Random.Range(0, bestChoices.Count)];
            selected.ValidActionCount = validActionsCount;
            return selected;
        }

        return new ActionChoice
        {
            ActionId = (int)AttackType.FinishTurn,
            ChosenTarget = null,
            ChosenStates = DetermineStates(unit, null),
            ValidActionCount = 0,
            ForcedFallback = true
        };
    }

    private bool IsActionValidForUnitAndTarget(Unit unit, Unit target, int actionId, bool[] states)
    {
        var def = AllActions[actionId];
        var aType = def.attackType;

        bool canMove = states[(int)AIState.CanMove];
        bool canDoAction = states[(int)AIState.CanDoAction];
        bool hasRanged = states[(int)AIState.HasRangedWeapon];
        bool inCharge = states[(int)AIState.IsInChargeRange];
        bool beyondAttack = states[(int)AIState.IsBeyondAttackRange];
        bool isLoaded = states[(int)AIState.WeaponIsLoaded];
        bool targetBehindObstacle = states[(int)AIState.TargetBehindObstacle];
        bool isAiming = states[(int)AIState.IsAiming];

        if (aType == AttackType.FinishTurn) return true;

        switch (aType)
        {
            case AttackType.Move:
            case AttackType.MoveAway:
                return canMove;
            case AttackType.Run:
            case AttackType.RunAway:
            case AttackType.Retreat:
                return canMove && canDoAction;
            case AttackType.Reload:
                return canDoAction && hasRanged && !isLoaded;
            case AttackType.ChangeWeaponToMelee:
                return canDoAction && hasRanged;
            case AttackType.ChangeWeaponToRanged:
                return canDoAction && !hasRanged;
            case AttackType.Aim:
                return canDoAction && !isAiming;
            case AttackType.Charge:
                return canMove && canDoAction && inCharge && !hasRanged;
            case AttackType.AllOutAttack:
                return canDoAction && !hasRanged && !beyondAttack;
            case AttackType.Null:
                if (!canDoAction || beyondAttack) return false;
                if (hasRanged) return isLoaded && !targetBehindObstacle;
                return true;
            default:
                return canDoAction;
        }
    }

    private bool IsActionValidForUnitAndTarget_Approx(bool[] states, int actionId)
    {
        return IsActionValidForUnitAndTarget(null, null, actionId, states);
    }

    private Unit ResolveTargetFromAction(Unit suggestedTarget, int actionId, TargetsInfo info)
    {
        var def = AllActions[actionId];
        if (def.targetType == TargetType.None) return null;
        if (suggestedTarget != null) return suggestedTarget;
        return GetTargetByType(info, def.targetType);
    }

    private void UpdateQ(string raceName, int oldState, int action, float reward, int newState, bool isTerminal = false)
    {
        // Pobieramy tablice Q dla obu stanow
        float[] qOldStateVals = GetStateQValues(raceName, oldState);

        float oldQ = qOldStateVals[action];
        float maxQnext = isTerminal ? 0f : GetMaxQNextMasked(raceName, newState);

        float newQ = oldQ + Alpha * (reward + Gamma * maxQnext - oldQ);

        // Zapisujemy nowa wartosc
        qOldStateVals[action] = newQ;
    }

    private float GetMaxQNextMasked(string race, int nextStateIndex)
    {
        // Sprawdzamy czy stan w ogóle istnieje w pamięci, jeśli nie -> maxQ = 0 (domyślnie)
        if (!QTables.ContainsKey(race) || !QTables[race].ContainsKey(nextStateIndex))
        {
            // Nowy stan, wszystkie Q=0, więc max też 0.
            // Sprawdzamy tylko poprawność akcji, ale przy Q=0 wynik to 0.
            return 0f;
        }

        float[] qNextStateVals = QTables[race][nextStateIndex];
        bool[] nextStates = DecodeState(nextStateIndex);

        float best = float.MinValue;
        bool found = false;

        for (int a = 0; a < AllActions.Length; a++)
        {
            if (!IsActionValidForUnitAndTarget_Approx(nextStates, a)) continue;

            float v = qNextStateVals[a];
            if (!found || v > best) { best = v; found = true; }
        }
        return found ? best : 0f;
    }

    // ======================================================================
    //         PATHFINDING HELPERS (BFS + Manhattan)
    // ======================================================================

    private int GetManhattanDistance(Vector2 a, Vector2 b)
    {
        return Mathf.Abs((int)(a.x - b.x)) + Mathf.Abs((int)(a.y - b.y));
    }

    private int CalculateQuickPathLength(Vector2 startPos, Vector2 targetPos, int maxSearchDepth)
    {
        if (GetManhattanDistance(startPos, targetPos) > maxSearchDepth) return -1;
        if (GetManhattanDistance(startPos, targetPos) == 1) return 1;

        Queue<Vector2> queue = new Queue<Vector2>();
        HashSet<Vector2> visited = new HashSet<Vector2>();
        Dictionary<Vector2, int> depth = new Dictionary<Vector2, int>();

        queue.Enqueue(startPos);
        visited.Add(startPos);
        depth[startPos] = 0;

        Vector2[] directions = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };

        while (queue.Count > 0)
        {
            Vector2 current = queue.Dequeue();
            int currentDepth = depth[current];

            if (currentDepth >= maxSearchDepth) continue;

            foreach (Vector2 dir in directions)
            {
                Vector2 neighbor = current + dir;
                if (neighbor == targetPos) return currentDepth + 1;
                if (visited.Contains(neighbor)) continue;

                Collider2D col = Physics2D.OverlapPoint(neighbor);
                bool isBlocked = false;

                if (col != null)
                {
                    if (col.CompareTag("Tile"))
                    {
                        var tileComp = col.GetComponent<Tile>();
                        if (tileComp != null && tileComp.IsOccupied) isBlocked = true;
                    }
                    else if (col.GetComponent<Unit>() != null || col.GetComponent<MapElement>() != null)
                    {
                        isBlocked = true;
                    }
                }
                else isBlocked = true;

                if (!isBlocked)
                {
                    visited.Add(neighbor);
                    depth[neighbor] = currentDepth + 1;
                    queue.Enqueue(neighbor);
                }
            }
        }
        return -1;
    }

    // ======================================================================
    //         LOGIKA STANÓW
    // ======================================================================

    public bool[] DetermineStates(Unit unit, Unit target)
    {
        bool[] s = new bool[(int)AIState.COUNT];

        Stats stats = unit.GetComponent<Stats>();
        Weapon weapon = InventoryManager.Instance.ChooseWeaponToAttack(unit.gameObject);
        bool hasRanged = weapon != null && weapon.Type.Contains("ranged");

        s[(int)AIState.CanDoAction] = unit.CanDoAction;
        s[(int)AIState.CanMove] = unit.CanMove;
        s[(int)AIState.HasRangedWeapon] = hasRanged;
        s[(int)AIState.IsHeavilyWounded] = stats.TempHealth <= (stats.MaxHealth * 0.3f);
        s[(int)AIState.WeaponIsLoaded] = (weapon != null) && weapon.ReloadLeft == 0;
        s[(int)AIState.IsAiming] = unit.AimingBonus > 0;

        if (target != null)
        {
            int manhattanDist = GetManhattanDistance(unit.transform.position, target.transform.position);
            float distance = CombatManager.Instance.CalculateDistance(unit.gameObject, target.gameObject);
            float attackRange = weapon != null ? weapon.AttackRange : 1.5f;
            int attackRangeInt = Mathf.CeilToInt(attackRange);
            if (!hasRanged) attackRangeInt = 1;

            // Jeśli !HasRanged i !IsBeyondAttackRange -> jesteśmy w melee.

            if (hasRanged)
                s[(int)AIState.IsBeyondAttackRange] = distance > attackRange || distance <= 1.5f;
            else
                s[(int)AIState.IsBeyondAttackRange] = distance > attackRange;

            // Charge Logic
            int movement = stats.TempSz;
            float minCharge = movement / 2f;
            int maxCharge = movement * 2;

            if (manhattanDist >= minCharge && manhattanDist <= maxCharge)
            {
                int realPathLength = CalculateQuickPathLength(unit.transform.position, target.transform.position, maxCharge);
                s[(int)AIState.IsInChargeRange] = (realPathLength != -1 && realPathLength >= minCharge);
            }
            else
            {
                s[(int)AIState.IsInChargeRange] = false;
            }

            if (hasRanged)
            {
                float distFloat = Vector2.Distance(unit.transform.position, target.transform.position);
                s[(int)AIState.TargetBehindObstacle] = IsTargetBehindObstacle(unit.gameObject, target.gameObject, distFloat);
            }

            Stats tStats = target.GetComponent<Stats>();
            if (tStats != null)
            {
                s[(int)AIState.IsTargetHeavilyWounded] = tStats.TempHealth <= (tStats.MaxHealth * 0.3f);
                s[(int)AIState.IsStrongerThanTarget] = stats.TempHealth * stats.Overall > tStats.TempHealth * tStats.Overall;
            }
        }

        return s;
    }

    public void SimulateUnit(Unit unit)
    {
        if (unit == null) return;
        Stats stats = unit.GetComponent<Stats>();
        if (stats == null || stats.TempHealth <= 0) return;

        // Rozlicz poprzedni krok
        if (_lastStepByUnit.TryGetValue(unit, out var prev) && prev.HasValue)
        {
            float delayedReward = ComputeDelayedReward(unit, prev);

            Unit targetForNextState = prev.TargetExisted && prev.Target != null ? prev.Target : null;
            if (targetForNextState == null)
            {
                var infoTemp = GatherTargetsInfo(unit);
                targetForNextState = infoTemp.Closest;
            }

            bool[] statesNow = DetermineStates(unit, targetForNextState);
            int stateNowIndex = EncodeState(statesNow);

            float oldQ = GetStateQValues(prev.Race, prev.State)[prev.Action];

            if (!_isEvaluationMode)
            {
                UpdateQ(prev.Race, prev.State, prev.Action, delayedReward, stateNowIndex);
                currentEpochReward += delayedReward;
                actionsThisEpoch++;
            }

            float newQ = GetStateQValues(prev.Race, prev.State)[prev.Action];

            _currentEpisodeReward += delayedReward;
            _currentEpisodeStepCount++;
            _globalStepIndex++;

            if (Mathf.Abs(delayedReward) < 0.001f)
            {
                _noOpStepCount++;
            }

            LogStepRow("transition", unit, prev, stateNowIndex, delayedReward, oldQ, newQ);
            _lastStepByUnit[unit] = new LastStep { HasValue = false };
        }

        TargetsInfo info = GatherTargetsInfo(unit);
        ActionContext ctx = new ActionContext { Unit = unit, RaceName = stats.Race, Info = info };

        ActionChoice choice = ChooseValidActionEpsilonGreedy(ctx, unit);
        if (choice.ForcedFallback && (unit.CanDoAction || unit.CanMove))
        {
            _invalidActionCount++;

            LastStep fallbackStep = new LastStep
            {
                Race = stats.Race,
                State = EncodeState(choice.ChosenStates),
                Action = choice.ActionId,
                ValidActionsCount = choice.ValidActionCount,
                ForcedFallback = true,
                HasValue = false
            };

            LogStepRow("fallback", unit, fallbackStep, fallbackStep.State, 0f, 0f, 0f);
        }

        int prevSelfHP = stats.TempHealth;
        int prevTargetHP = 0;
        int targetOverall = 0;
        bool targetExisted = (choice.ChosenTarget != null);

        if (targetExisted)
        {
            var ts = choice.ChosenTarget.GetComponent<Stats>();
            if (ts != null) { prevTargetHP = ts.TempHealth; targetOverall = ts.Overall; }
        }

        float immediateReward = PerformParameterAction(choice.ActionId, unit, choice.ChosenTarget, info, prevSelfHP);
        immediateReward += StepPenalty;

        _lastStepByUnit[unit] = new LastStep
        {
            Race = stats.Race,
            State = EncodeState(choice.ChosenStates),
            Action = choice.ActionId,
            ImmediateReward = immediateReward,
            Target = choice.ChosenTarget,
            TargetExisted = targetExisted,
            PrevSelfHP = prevSelfHP,
            PrevTargetHP = prevTargetHP,
            TargetOverall = targetOverall,
            ValidActionsCount = choice.ValidActionCount,
            ForcedFallback = choice.ForcedFallback,
            HasValue = true
        };

        if (!_isEvaluationMode && actionsThisEpoch >= ActionsPerEpoch)
        {
            AdvanceEpoch();
        }

        UpdateTrainingDebugUI();
    }

    private void AdvanceEpoch()
    {
        float avgReward = currentEpochReward / Mathf.Max(1, actionsThisEpoch);
        epochRewards.Add(avgReward);
        Debug.Log($"Epoch {currentEpoch} ended. Avg Reward: {avgReward:F2} | Epsilon: {Epsilon:F3}");

        currentEpochReward = 0f;
        actionsThisEpoch = 0;
        currentEpoch++;

        if (currentEpoch < EpsilonDecayEpochs)
        {
            float progress = (float)currentEpoch / EpsilonDecayEpochs;
            Epsilon = Mathf.Lerp(EpsilonStart, EpsilonEnd, progress);
        }
        else Epsilon = EpsilonEnd;

        SaveAverageReward(avgReward);
        if (AutoSaveEveryEpochs > 0 && (currentEpoch % AutoSaveEveryEpochs == 0))
        {
            SaveQTables();
        }

        UpdateTrainingDebugUI();
    }

    private float ComputeDelayedReward(Unit unit, LastStep ls)
    {
        float reward = ls.ImmediateReward;
        var selfStats = unit.GetComponent<Stats>();
        if (selfStats == null) return reward;

        int hpDiff = selfStats.TempHealth - ls.PrevSelfHP;
        if (hpDiff < 0) reward += hpDiff * 2.0f;
        if (selfStats.TempHealth <= 0) reward += LOSS_REWARD;

        if (ls.TargetExisted)
        {
            bool targetDead = false;
            int currentTargetHP = 0;

            if (ls.Target == null || ls.Target.GetComponent<Stats>() == null) targetDead = true;
            else
            {
                var tStats = ls.Target.GetComponent<Stats>();
                currentTargetHP = tStats.TempHealth;
                if (currentTargetHP <= 0) targetDead = true;
            }

            if (targetDead) reward += 20f + (ls.TargetOverall / 2f);
            else
            {
                int dmgDealt = ls.PrevTargetHP - currentTargetHP;
                if (dmgDealt > 0) reward += dmgDealt * 1.5f;
            }
        }
        return reward;
    }

    private float PerformParameterAction(int actionID, Unit unit, Unit chosenTarget, TargetsInfo info, int oldHP)
    {
        float reward = 0f;
        if (actionID < 0 || actionID >= AllActions.Length)
        {
            RoundsManager.Instance.FinishTurn();
            return reward;
        }

        ActionDefinition def = AllActions[actionID];
        AttackType aType = def.attackType;
        TargetType tType = def.targetType;

        Unit target = chosenTarget != null ? chosenTarget : GetTargetByType(info, tType);

        switch (aType)
        {
            case AttackType.Move:
                if (target != null) MoveTowards(unit, target.gameObject);
                break;
            case AttackType.Run:
                if (target != null) MoveTowards(unit, target.gameObject, 3);
                break;
            case AttackType.MoveAway:
                if (target != null)
                {
                    GameObject tile = GetTileFarthestFromTarget(unit.gameObject, target.gameObject);
                    if (tile != null) MoveTowards(unit, tile, 1, true);
                }
                break;
            case AttackType.RunAway:
                if (target != null)
                {
                    GameObject tile = GetTileFarthestFromTarget(unit.gameObject, target.gameObject);
                    if (tile != null) MoveTowards(unit, tile, 3, true);
                }
                break;
            case AttackType.Retreat:
                if (target != null)
                {
                    GameObject tile = GetTileFarthestFromTarget(unit.gameObject, target.gameObject);
                    if (tile != null)
                    {
                        MovementManager.Instance.Retreat(true);
                        MoveTowards(unit, tile, 1, true);
                    }
                }
                break;
            case AttackType.Aim:
                CombatManager.Instance.SetAim();
                return reward;
            case AttackType.Reload:
                CombatManager.Instance.Reload();
                reward += 1f;
                return reward;
            case AttackType.ChangeWeaponToMelee:
                ChangeWeapon(unit, "melee");
                return reward;
            case AttackType.ChangeWeaponToRanged:
                ChangeWeapon(unit, "ranged");
                return reward;
            case AttackType.FinishTurn:
                RoundsManager.Instance.FinishTurn();
                return reward;
        }

        string attackName = null;
        if (aType == AttackType.Charge) attackName = "Charge";
        else if (aType == AttackType.AllOutAttack) attackName = "AllOutAttack";

        if (aType == AttackType.Null || aType == AttackType.Charge || aType == AttackType.AllOutAttack)
        {
            PerformAttack(unit.gameObject, target != null ? target.gameObject : null, attackName);
        }

        reward += CalculateRewardBasedOnUnitHealth(unit.GetComponent<Stats>(), oldHP);
        return reward;
    }

    // --- HELPERS ---
    private void MoveTowards(Unit unit, GameObject targetObject, int modifier = 1, bool retreat = false)
    {
        if (modifier != 1) StartCoroutine(MovementManager.Instance.UpdateMovementRange(modifier));
        GameObject tile = retreat ? targetObject : CombatManager.Instance.GetTileAdjacentToTarget(unit.gameObject, targetObject);
        if (tile != null)
        {
            MovementManager.Instance.MoveSelectedUnit(tile, unit.gameObject);
            Physics2D.SyncTransforms();
        }
    }

    private void PerformAttack(GameObject attacker, GameObject target, string attackType)
    {
        if (attacker == null || target == null) return;
        var aUnit = attacker.GetComponent<Unit>();
        var tUnit = target.GetComponent<Unit>();
        if (attackType != null) CombatManager.Instance.ChangeAttackType(attackType);
        CombatManager.Instance.Attack(aUnit, tUnit, false);
    }

    private void ChangeWeapon(Unit unit, string desiredType)
    {
        if (unit == null) return;
        var inv = unit.GetComponent<Inventory>();
        var unitWeapon = unit.GetComponent<Weapon>();
        if (inv == null || unitWeapon == null) return;

        Weapon current = InventoryManager.Instance.ChooseWeaponToAttack(unit.gameObject);
        if (current != null && !current.Broken && current.Type != null && current.Type.Contains(desiredType)) return;

        Weapon candidate = inv.AllWeapons.FirstOrDefault(w => w != null && w.Armor == 0 && !w.Broken && w.Type != null && w.Type.Contains(desiredType));
        if (candidate == null) return;

        if (inv.EquippedWeapons == null || inv.EquippedWeapons.Length < 2) return;
        inv.EquippedWeapons[0] = candidate;
        inv.EquippedWeapons[1] = candidate.TwoHanded ? candidate : inv.EquippedWeapons[1];

        var fields = typeof(Weapon).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        foreach (var f in fields) f.SetValue(unitWeapon, f.GetValue(candidate));
        Debug.Log($"{unit.name} dobył/a {candidate.Name}.");
    }

    private int CalculateRewardBasedOnUnitHealth(Stats stats, int oldAttackerHP)
    {
        int reward = 0;
        int lostHP = oldAttackerHP - stats.TempHealth;
        if (lostHP > 0) reward -= lostHP;
        if (stats.TempHealth < 0) reward -= 15;
        return reward;
    }

    public void GiveTerminalRewardToAll(bool didAIWin)
    {
        float terminal = didAIWin ? WIN_REWARD : LOSS_REWARD;

        foreach (var kv in _lastStepByUnit.ToList())
        {
            Unit u = kv.Key;
            LastStep ls = kv.Value;

            if (!ls.HasValue)
                continue;

            int nextState = ls.State;
            float terminalReward;

            if (u == null)
            {
                terminalReward = ls.ImmediateReward + terminal;
            }
            else
            {
                TargetsInfo infoNow = GatherTargetsInfo(u);
                Unit defaultTargetNow = infoNow != null ? infoNow.Closest : null;
                bool[] statesNow = DetermineStates(u, defaultTargetNow);
                nextState = EncodeState(statesNow);
                terminalReward = ComputeDelayedReward(u, ls) + terminal;
            }

            float oldQ = GetStateQValues(ls.Race, ls.State)[ls.Action];

            if (!_isEvaluationMode)
            {
                UpdateQ(ls.Race, ls.State, ls.Action, terminalReward, nextState, true);
                currentEpochReward += terminalReward;
                actionsThisEpoch++;
            }

            float newQ = GetStateQValues(ls.Race, ls.State)[ls.Action];

            _currentEpisodeReward += terminalReward;
            _currentEpisodeStepCount++;
            _globalStepIndex++;

            LogStepRow("terminal", u, ls, nextState, terminalReward, oldQ, newQ);
        }

        _lastStepByUnit.Clear();
        UpdateTrainingDebugUI();
    }

    private Unit GetTargetByType(TargetsInfo info, TargetType t)
    {
        switch (t)
        {
            case TargetType.Closest: return info.Closest;
            case TargetType.Furthest: return info.Furthest;
            case TargetType.MostInjured: return info.MostInjured;
            case TargetType.LeastInjured: return info.LeastInjured;
            case TargetType.Weakest: return info.Weakest;
            case TargetType.Strongest: return info.Strongest;
            case TargetType.MostAlliesNearby: return info.WithMostAllies;
            default: return null;
        }
    }

    public TargetsInfo GatherTargetsInfo(Unit currentUnit)
    {
        TargetsInfo info = new TargetsInfo();
        foreach (Unit other in UnitsManager.Instance.AllUnits)
        {
            if (other == null) continue;
            var oStats = other.GetComponent<Stats>();
            if (oStats == null || oStats.TempHealth <= 0) continue;
            if (!IsValidTarget(currentUnit, other)) continue;

            float dist = Vector2.Distance(currentUnit.transform.position, other.transform.position);
            info.Distances[other] = dist;

            if (dist < info.ClosestDistance) { info.ClosestDistance = dist; info.Closest = other; }
            if (dist > info.FurthestDistance) { info.FurthestDistance = dist; info.Furthest = other; }

            float hp = oStats.TempHealth;
            if (hp < info.MostInjuredHP) { info.MostInjuredHP = hp; info.MostInjured = other; }
            if (hp > info.LeastInjuredHP) { info.LeastInjuredHP = hp; info.LeastInjured = other; }

            int ov = oStats.Overall;
            if (ov < info.WeakestOverall) { info.WeakestOverall = ov; info.Weakest = other; }
            if (ov > info.StrongestOverall) { info.StrongestOverall = ov; info.Strongest = other; }

            int allies = 0, enemies = 0;
            CountAdjacentUnits(other.transform.position, currentUnit.tag, other.tag, ref allies, ref enemies);
            int adv = allies - enemies;
            if (adv > info.WithMostAlliesScore) { info.WithMostAlliesScore = adv; info.WithMostAllies = other; }
        }
        return info;
    }

    private bool IsValidTarget(Unit currentUnit, Unit other)
    {
        if (other == null || currentUnit == null) return false;
        if (other == currentUnit) return false;
        if (other.CompareTag(currentUnit.tag)) return false;
        return true;
    }

    private void CountAdjacentUnits(Vector2 center, string allyTag, string opponentTag, ref int allies, ref int opponents)
    {
        Vector2[] offsets = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
        foreach (var off in offsets)
        {
            Collider2D col = Physics2D.OverlapPoint(center + off);
            if (col == null) continue;
            if (col.CompareTag(allyTag)) allies++;
            else if (col.CompareTag(opponentTag)) opponents++;
        }
    }

    private bool IsTargetBehindObstacle(GameObject attacker, GameObject target, float dist)
    {
        var hits = Physics2D.RaycastAll(attacker.transform.position, target.transform.position - attacker.transform.position, dist);
        foreach (var h in hits)
        {
            if (h.collider.gameObject == attacker || h.collider.gameObject == target) continue;
            if (h.collider.GetComponent<MapElement>() != null || h.collider.GetComponent<Unit>() != null) return true;
        }
        return false;
    }

    public GameObject GetTileFarthestFromTarget(GameObject attacker, GameObject target)
    {
        if (target == null) return null;
        Vector2 tPos = target.transform.position;
        Tile bestTile = null;
        float maxDist = -1f;
        int moveRange = attacker.GetComponent<Stats>().TempSz;
        Vector2 aPos = attacker.transform.position;

        foreach (Tile tile in GridManager.Instance.Tiles)
        {
            if (tile.IsOccupied) continue;
            float dToSelf = GetManhattanDistance(aPos, tile.transform.position); // Optymalizacja
            if (dToSelf > moveRange) continue;

            float dToTarget = Vector2.Distance(tile.transform.position, tPos);
            if (dToTarget > maxDist)
            {
                maxDist = dToTarget;
                bestTile = tile;
            }
        }
        return bestTile != null ? bestTile.gameObject : null;
    }

    public bool BothTeamsExist()
    {
        if (UnitsManager.Instance == null || UnitsManager.Instance.AllUnits == null)
        {
            return false;
        }

        bool p = false, e = false;
        foreach (var u in UnitsManager.Instance.AllUnits)
        {
            if (u == null) continue;

            Stats stats = u.Stats != null ? u.Stats : u.GetComponent<Stats>();
            if (stats == null) continue;

            if (u.CompareTag("PlayerUnit") && stats.TempHealth > 0) p = true;
            if (u.CompareTag("EnemyUnit") && stats.TempHealth > 0) e = true;
            if (p && e) return true;
        }

        if (p && !e) _playerWins++;
        if (!p && e) _enemyWins++;
        return false;
    }

    public void ToggleLogs()
    {
        var logger = Debug.unityLogger;

        if (logger.filterLogType == LogType.Error)
        {
            // Przywróć pełne logi
            logger.filterLogType = LogType.Log;
        }
        else
        {
            // Pokazuj tylko błędy i wyjątki
            logger.filterLogType = LogType.Error;
        }

        //Debug.unityLogger.logEnabled = !Debug.unityLogger.logEnabled; // To wyłącza wszystkie logi, razem z errorami
    }

    // ======================================================================
    // ZAPIS / ODCZYT JSON (SPARSE FORMAT)
    // ======================================================================

    [System.Serializable]
    public class QTableStateEntry
    {
        public int state;       // Index stanu
        public float[] values;  // Tablica wartości Q (o długości ACTION_COUNT)
    }

    [System.Serializable]
    public class QTableData
    {
        public string raceName;
        public List<QTableStateEntry> entries = new List<QTableStateEntry>();
    }

    [System.Serializable]
    public class QTablesContainer
    {
        public List<QTableData> tables = new List<QTableData>();
    }

    public void SaveQTables()
    {
        QTablesContainer container = new QTablesContainer();

        foreach (var kvp in QTables)
        {
            QTableData data = new QTableData { raceName = kvp.Key };

            // Iterujemy tylko po istniejących stanach w słowniku
            foreach (var stateKvp in kvp.Value)
            {
                data.entries.Add(new QTableStateEntry
                {
                    state = stateKvp.Key,
                    values = stateKvp.Value
                });
            }
            container.tables.Add(data);
        }

        string json = JsonUtility.ToJson(container, true);
        File.WriteAllText(Path.Combine(Application.persistentDataPath, "q_tables.json"), json);
        Debug.Log("QTables saved (Sparse Format).");
    }

    public void LoadQTables()
    {
        string path = Path.Combine(Application.persistentDataPath, "q_tables.json");
        if (!File.Exists(path)) return;

        try
        {
            var container = JsonUtility.FromJson<QTablesContainer>(File.ReadAllText(path));

            foreach (var data in container.tables)
            {
                // Tworzymy słownik dla danej rasy
                Dictionary<int, float[]> raceDict = new Dictionary<int, float[]>();

                foreach (var entry in data.entries)
                {
                    raceDict[entry.state] = entry.values;
                }

                QTables[data.raceName] = raceDict;
                _trainedRaces.Add(data.raceName);
            }
            Debug.Log("QTables loaded (Sparse Format).");
        }
        catch (Exception e)
        {
            Debug.LogError("Failed to load QTables: " + e.Message);
        }
    }

    // ======================================================================
    //                            EKSPORT WYNIKÓW
    // ======================================================================

    public void ExportAllData()
    {
        string folderPath = Application.persistentDataPath;
        ExportAllQToCSV(folderPath);
    }

    public void ExportAllQToCSV(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        foreach (string raceName in QTables.Keys)
        {
            string sanitizedRace = raceName.Replace(" ", "_");
            string fileName = $"Q_{sanitizedRace}.csv";
            string filePath = Path.Combine(folderPath, fileName);

            ExportQToCSV(raceName, filePath);
        }
    }

    public void ExportQToCSV(string raceName, string filePath)
    {
        if (!QTables.ContainsKey(raceName)) return;

        var table = QTables[raceName];
        int numActions = ACTION_COUNT;
        int numberOfStates = (int)AIState.COUNT;

        using (StreamWriter sw = new StreamWriter(filePath))
        {
            List<string> headers = new List<string> { "StateIndex" };
            for (int i = 0; i < numberOfStates; i++)
            {
                headers.Add(((AIState)i).ToString());
            }
            headers.Add("ActionIndex");
            headers.Add("QValue");
            sw.WriteLine(string.Join(";", headers));

            // Iterujemy tylko po zapisanych stanach
            foreach (var kvp in table)
            {
                int s = kvp.Key;
                float[] values = kvp.Value;

                List<string> rowValues = new List<string> { s.ToString() };
                for (int i = 0; i < numberOfStates; i++)
                {
                    bool isSet = (s & (1 << i)) != 0;
                    rowValues.Add(isSet.ToString().ToLower());
                }

                for (int a = 0; a < numActions; a++)
                {
                    // Eksportujemy tylko niezerowe, żeby oszczędzić miejsce w CSV,
                    // albo wszystkie - tutaj wszystkie dla czytelności wykresów
                    List<string> actionValues = new List<string>(rowValues)
                    {
                        a.ToString(),
                        values[a].ToString("F2")
                    };
                    sw.WriteLine(string.Join(";", actionValues));
                }
            }
        }
        Debug.Log($"Q-values for race '{raceName}' exported to: {filePath}");
    }

    // ======================================================================
    //                        EKSPORT LOGÓW
    // ======================================================================

    private void SaveAverageReward(float averageReward)
    {
        string filePath = Path.Combine(Application.persistentDataPath, "average_rewards.csv");
        bool fileExists = File.Exists(filePath);

        using (StreamWriter sw = new StreamWriter(filePath, append: true))
        {
            if (!fileExists)
            {
                sw.WriteLine("Epoch;AverageReward");
            }
            sw.WriteLine($"{epochRewards.Count};{averageReward}");
        }

        if (simpleGraph != null)
        {
            simpleGraph.AddValue(averageReward);
        }
    }

    public void UpdateTeamWins()
    {
        if (_teamWinsDisplay != null) _teamWinsDisplay.text = $"Player: {_playerWins} Enemy: {_enemyWins}";
    }

    private static readonly ActionDefinition[] AllActions = new ActionDefinition[]
    {
        // --- MOVE (RUCH) ---
        new ActionDefinition(TargetType.Closest, AttackType.Move),          // 0: Ruch do najbliższego wroga
        new ActionDefinition(TargetType.Furthest, AttackType.Move),         // 1: Ruch do najdalszego wroga
        new ActionDefinition(TargetType.MostInjured, AttackType.Move),      // 2: Ruch do najbardziej rannego wroga
        new ActionDefinition(TargetType.LeastInjured, AttackType.Move),     // 3: Ruch do najmniej rannego wroga
        new ActionDefinition(TargetType.Weakest, AttackType.Move),          // 4: Ruch do najsłabszego wroga (statystyki)
        new ActionDefinition(TargetType.Strongest, AttackType.Move),        // 5: Ruch do najsilniejszego wroga (statystyki)
        new ActionDefinition(TargetType.MostAlliesNearby, AttackType.Move), // 6: Ruch do wroga, przy którym jest najwięcej sojuszników

        // --- RUN (BIEG) ---
        new ActionDefinition(TargetType.Closest, AttackType.Run),           // 7: Bieg do najbliższego wroga
        new ActionDefinition(TargetType.Furthest, AttackType.Run),          // 8: Bieg do najdalszego wroga
        new ActionDefinition(TargetType.MostInjured, AttackType.Run),       // 9: Bieg do najbardziej rannego wroga
        new ActionDefinition(TargetType.LeastInjured, AttackType.Run),      // 10: Bieg do najmniej rannego wroga
        new ActionDefinition(TargetType.Weakest, AttackType.Run),           // 11: Bieg do najsłabszego wroga
        new ActionDefinition(TargetType.Strongest, AttackType.Run),         // 12: Bieg do najsilniejszego wroga
        new ActionDefinition(TargetType.MostAlliesNearby, AttackType.Run),  // 13: Bieg do wroga, przy którym jest najwięcej sojuszników

        // --- STANDARD ATTACK (ZWYKŁY ATAK) ---
        new ActionDefinition(TargetType.Closest, AttackType.Null),          // 14: Zwykły atak na najbliższego wroga
        new ActionDefinition(TargetType.Furthest, AttackType.Null),         // 15: Zwykły atak na najdalszego wroga
        new ActionDefinition(TargetType.MostInjured, AttackType.Null),      // 16: Zwykły atak na najbardziej rannego wroga
        new ActionDefinition(TargetType.LeastInjured, AttackType.Null),     // 17: Zwykły atak na najmniej rannego wroga
        new ActionDefinition(TargetType.Weakest, AttackType.Null),          // 18: Zwykły atak na najsłabszego wroga
        new ActionDefinition(TargetType.Strongest, AttackType.Null),        // 19: Zwykły atak na najsilniejszego wroga
        new ActionDefinition(TargetType.MostAlliesNearby, AttackType.Null), // 20: Zwykły atak na wroga z przewagą liczebną sojuszników

        // --- CHARGE (SZARŻA) ---
        new ActionDefinition(TargetType.Closest, AttackType.Charge),          // 21: Szarża na najbliższego wroga
        new ActionDefinition(TargetType.Furthest, AttackType.Charge),         // 22: Szarża na najdalszego wroga
        new ActionDefinition(TargetType.MostInjured, AttackType.Charge),      // 23: Szarża na najbardziej rannego wroga
        new ActionDefinition(TargetType.LeastInjured, AttackType.Charge),     // 24: Szarża na najmniej rannego wroga
        new ActionDefinition(TargetType.Weakest, AttackType.Charge),          // 25: Szarża na najsłabszego wroga
        new ActionDefinition(TargetType.Strongest, AttackType.Charge),        // 26: Szarża na najsilniejszego wroga
        new ActionDefinition(TargetType.MostAlliesNearby, AttackType.Charge), // 27: Szarża na wroga z przewagą liczebną sojuszników

        // --- ALL OUT ATTACK (SZALEŃCZY ATAK) ---
        new ActionDefinition(TargetType.Closest, AttackType.AllOutAttack),          // 28: Szaleńczy atak na najbliższego wroga
        new ActionDefinition(TargetType.Furthest, AttackType.AllOutAttack),         // 29: Szaleńczy atak na najdalszego wroga
        new ActionDefinition(TargetType.MostInjured, AttackType.AllOutAttack),      // 30: Szaleńczy atak na najbardziej rannego wroga
        new ActionDefinition(TargetType.LeastInjured, AttackType.AllOutAttack),     // 31: Szaleńczy atak na najmniej rannego wroga
        new ActionDefinition(TargetType.Weakest, AttackType.AllOutAttack),          // 32: Szaleńczy atak na najsłabszego wroga
        new ActionDefinition(TargetType.Strongest, AttackType.AllOutAttack),        // 33: Szaleńczy atak na najsilniejszego wroga
        new ActionDefinition(TargetType.MostAlliesNearby, AttackType.AllOutAttack), // 34: Szaleńczy atak na wroga z przewagą liczebną sojuszników

        // --- SPECIAL / UTILITY (SPECJALNE / UŻYTKOWE) ---
        new ActionDefinition(TargetType.None, AttackType.Aim),                  // 35: Przycelowanie (bonus do trafienia)
        new ActionDefinition(TargetType.None, AttackType.Reload),               // 36: Przeładowanie broni
        new ActionDefinition(TargetType.Closest, AttackType.MoveAway),          // 37: Odejście od najbliższego wroga (zwykły ruch)
        new ActionDefinition(TargetType.Closest, AttackType.RunAway),           // 38: Ucieczka biegiem od najbliższego wroga
        new ActionDefinition(TargetType.Closest, AttackType.Retreat),           // 39: Bezpieczny odwrót od najbliższego wroga
        new ActionDefinition(TargetType.None, AttackType.ChangeWeaponToMelee),  // 40: Zmiana broni na białą (melee)
        new ActionDefinition(TargetType.None, AttackType.ChangeWeaponToRanged), // 41: Zmiana broni na dystansową (ranged)
        new ActionDefinition(TargetType.None, AttackType.FinishTurn),           // 42: Zakończenie tury (czekanie)
    };
}







