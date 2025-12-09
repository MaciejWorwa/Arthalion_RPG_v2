using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.IO;
using System;
using UnitStates;
using TMPro;

namespace UnitStates
{
    public enum AIState
    {
        IsInMelee,
        IsHeavilyWounded,
        HasRangedWeapon,
        IsBeyondAttackRange,
        IsInChargeRange,
        TargetBehindObstacle,
        WeaponIsLoaded,
        
        // Dodaj tu kolejne stany
        COUNT // Tyle mamy stanów
    }
}

public enum TargetType
{
    None = 0,    // dla akcji bez celu (DefensiveStance, Reload, itp.)
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
    Null,         // Zwykły atak
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

// Definiujemy parę (target, attack)
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
    [Tooltip("Współczynnik uczenia (learning rate)")]
    public float Alpha = 0.1f;

    [Tooltip("Współczynnik dyskontowania (discount factor)")]
    public float Gamma = 0.9f;

    [Tooltip("Szansa na losową akcję (eksploracja)")]
    public float Epsilon = 0.2f;

    public float EpsilonStart = 1f;
    public float EpsilonEnd = 0.05f;
    public int EpsilonDecayEpochs = 500;
    private int currentEpoch = 0;

    [Header("Logging Parameters")]
    [Tooltip("Number of actions per epoch before calculating average reward.")]
    public int ActionsPerEpoch = 1000;

    [Header("Graphing")]
    public SimpleGraph simpleGraph;

    // Logowanie nagród
    private List<float> epochRewards = new List<float>();
    private float currentEpochReward = 0f;
    private int actionsThisEpoch = 0;

    private const int HISTORY_LIMIT = 10;
    private Dictionary<Unit, Queue<(int state, int action, string race)>> unitActionHistory = new Dictionary<Unit, Queue<(int, int, string)>>();
    private const float WIN_REWARD = 100f;
    private const float LOSS_REWARD = -20f;

    [SerializeField] private int _playerWins;
    [SerializeField] private int _enemyWins;
    [SerializeField] private TMP_Text _teamWinsDisplay;

    // Liczba dostępnych akcji
    private const int ACTION_COUNT = 43;

    // Liczba kombinacji stanów (2^(AIState.COUNT))
    private int totalStateCombinations;

    // klucz: nazwa rasy (np. "Human", "Orc", "Elf"), wartość: tablica Q
    private Dictionary<string, float[,]> QTables = new Dictionary<string, float[,]>();

    // Jak często dana akcja została użyta
    private Dictionary<string, int[,]> ActionUsageCount = new Dictionary<string, int[,]>();

    public bool IsLearning;

    // Lista lub zbiór wytrenowanych ras
    private HashSet<string> _trainedRaces = new HashSet<string>();

    private PrioritizedReplayBuffer replayBuffer;
    private Dictionary<string, float[,]> eligibilityTrace;
    public float Lambda = 0.8f;  // śledzenie śladu
    public int ReplayBatchSize = 64;
    public int ReplayStartSize = 1000;  // zacznij replay dopiero po tylu przejściach

    private float ComputePotential(bool[] states)
    {
        // przykładowa: karanie odległości od najbliższego wroga
        return -(states[(int)AIState.IsBeyondAttackRange] ? 1f : 0f);
    }

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }

    void Start()
    {
        if(IsLearning)
        {
            LoadQTables();
        }

        // Obliczamy 2^(int)AIState.COUNT
        totalStateCombinations = 1 << ((int)AIState.COUNT);

        replayBuffer = new PrioritizedReplayBuffer(10000);
        eligibilityTrace = new Dictionary<string, float[,]>();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R) && IsLearning)
        {
            SaveQTables();
        }
    }

    // Metoda do sprawdzania, czy rasa jest wytrenowana
    public bool IsRaceTrained(string race)
    {
        return _trainedRaces.Contains(race);
    }

    public void ToggleLogs()
    {
        Debug.unityLogger.logEnabled = !Debug.unityLogger.logEnabled;
    }
    private void TrainFromReplay()
    {
        var batch = replayBuffer.Sample(ReplayBatchSize);
        foreach (var t in batch)
        {
            var q = GetQTable(t.RaceName);
            // inicjalizacja śladu jeśli brak
            if (!eligibilityTrace.ContainsKey(t.RaceName))
                eligibilityTrace[t.RaceName] = new float[q.GetLength(0), q.GetLength(1)];
            var e = eligibilityTrace[t.RaceName];

            // wylicz error TD
            float maxQNext = 0f;
            for (int a = 0; a < ACTION_COUNT; a++) maxQNext = Mathf.Max(maxQNext, q[t.NextState, a]);
            float target = t.Reward + Gamma * maxQNext;
            float error = target - q[t.State, t.Action];

            // update śladu i Q
            e[t.State, t.Action] += 1f;
            for (int s = 0; s < q.GetLength(0); s++)
            {
                for (int a = 0; a < q.GetLength(1); a++)
                {
                    q[s, a] += Alpha * error * e[s, a];
                    e[s, a] *= Gamma * Lambda;
                }
            }

            // zaktualizuj priorytet
            int idx = replayBuffer.buffer.FindIndex(x => x.State == t.State && x.Action == t.Action);
            if (idx >= 0) replayBuffer.priorities[idx] = Math.Abs(error) + 0.01f;
        }
    }
    // ======================================================================
    //             REJESTRACJA / POBIERANIE TABLICY Q DLA RASY
    // ======================================================================

    // Inicjalizuje (jeśli trzeba) Q-tabelę dla danej rasy.
    public void RegisterRace(string raceName)
    {
        if (string.IsNullOrEmpty(raceName)) return;

        // 1) Zarejestruj w QTables
        if (!QTables.ContainsKey(raceName))
        {
            float[,] table = new float[totalStateCombinations, ACTION_COUNT];
            QTables[raceName] = table;
            Debug.Log($"[RegisterRace] Dodano QTables dla '{raceName}'");
        }

        // 2) Zarejestruj w ActionUsageCount
        if (!ActionUsageCount.ContainsKey(raceName))
        {
            int[,] usage = new int[totalStateCombinations, ACTION_COUNT];
            ActionUsageCount[raceName] = usage;
            Debug.Log($"[RegisterRace] Dodano ActionUsageCount dla '{raceName}'");
        }
    }

    // Zwraca tablicę Q dla podanej rasy. Jeśli brak, tworzy nową.
    private float[,] GetQTable(string raceName)
    {
        RegisterRace(raceName);
        
        return QTables[raceName];
    }

    // ======================================================================
    //                     PODSTAWOWE METODY Q-LEARNING
    // ======================================================================

    public int EncodeState(bool[] states)
    {
        int stateIndex = 0;
        for (int i = 0; i < states.Length; i++)
        {
            if (states[i])
            {
                stateIndex |= (1 << i);
            }
        }
        return stateIndex;
    }

    // Wybiera akcję epsilon-greedy na podstawie rasy i stanu.
    private ActionChoice ChooseValidActionEpsilonGreedy(ActionContext context)
    {
        float[,] qTable = GetQTable(context.RaceName);
        Unit unit = context.Unit;

        Dictionary<Unit, bool[]> statesCache = new Dictionary<Unit, bool[]>();

        // Cache na stany "bez celu"
        Unit defaultTarget = GetTargetByType(context.Info, TargetType.Closest);
        bool[] nonTargetStates = defaultTarget != null ? DetermineStates(unit, defaultTarget) : DetermineStates(unit, null);

        // Sprawdzenie, czy jednostka ma jeszcze dostępne akcje
        if (InitiativeQueueManager.Instance.InitiativeQueue.ContainsKey(unit) && !unit.CanDoAction && !unit.CanMove)
        {
            return new ActionChoice
            {
                ActionId = 42,
                ChosenTarget = null,
                ChosenStates = new bool[(int)AIState.COUNT]
            };
        }

        List<int> validActions = new List<int>();
        Unit fallbackTarget = null;
        bool[] fallbackStates = new bool[(int)AIState.COUNT];

        for (int i = 0; i < AllActions.Length; i++)
        {
            ActionDefinition def = AllActions[i];
            TargetType tType = def.targetType;
            AttackType aType = def.attackType;

            bool requiresTarget = (tType != TargetType.None);
            Unit potentialTarget = null;
            bool[] states = null;

            if (requiresTarget)
            {
                // jeśli potrzebujemy celu, pobierz go i policz stany dla tego celu (z cachingiem)
                potentialTarget = GetTargetByType(context.Info, def.targetType);
                if (potentialTarget == null)
                    continue;

                if (!statesCache.TryGetValue(potentialTarget, out states))
                {
                    states = DetermineStates(unit, potentialTarget);
                    statesCache[potentialTarget] = states;
                }
            }
            else
            {
                states = nonTargetStates;
            }

            // fallback – pierwszy potencjalny target
            if (fallbackTarget == null && potentialTarget != null)
            {
                fallbackTarget = potentialTarget;
                fallbackStates = (bool[])states.Clone();
            }

            bool isInMelee = states[(int)AIState.IsInMelee];
            bool hasRangedWeapon = states[(int)AIState.HasRangedWeapon];
            bool isBeyondAttackRange = states[(int)AIState.IsBeyondAttackRange];
            bool isInChargeRange = states[(int)AIState.IsInChargeRange];

            bool isMove = (aType == AttackType.Move || aType == AttackType.MoveAway);
            bool isRun = (aType == AttackType.Run || aType == AttackType.RunAway);
            bool isRetreat = aType == AttackType.Retreat;
            bool isAttack = (aType != AttackType.Move
                            && aType != AttackType.Aim
                            && aType != AttackType.Reload
                            && aType != AttackType.FinishTurn
                            && aType != AttackType.MoveAway
                            && aType != AttackType.RunAway
                            && aType != AttackType.Retreat
                            && aType != AttackType.ChangeWeaponToMelee
                            && aType != AttackType.ChangeWeaponToRanged);

            if (isMove)
            {
                if (!unit.CanMove)
                    continue;
            }
            if (isRun || isRetreat)
            {
                if (!unit.CanMove || !unit.CanDoAction)
                    continue;
            }
            else // każda inna akcja wymaga CanDoAction
            {
                if (!unit.CanDoAction)
                    continue;
            }

            // ---- WARUNKI BLOKADY DLA ATAKÓW ----
            if (isAttack)
            {
                if (!context.OpponentExist) continue;
                if (context.CurrentWeapon == null) continue;
                if (context.CurrentWeapon.ReloadLeft > 0) continue;
                if (potentialTarget == null) continue;
                if (!context.Info.Distances.ContainsKey(potentialTarget)) continue;

                if (aType == AttackType.Charge)
                {
                    if (!isInChargeRange || !unit.CanMove) continue;
                    if (!(isBeyondAttackRange && !hasRangedWeapon)) continue;
                }
                else
                {
                    if (hasRangedWeapon)
                    {
                        states[(int)AIState.IsBeyondAttackRange] =
                            !CombatManager.Instance.ValidateRangedAttack(unit, potentialTarget, context.CurrentWeapon, 
                                                                        context.Info.Distances[potentialTarget]);
                    }
                    else
                    {
                        states[(int)AIState.IsBeyondAttackRange] =
                            context.Info.Distances[potentialTarget] > context.CurrentWeapon.AttackRange;
                    }

                    if (isBeyondAttackRange) continue;
                }

                if (hasRangedWeapon && (aType == AttackType.Charge)) continue;
            }

            // ---- WARUNKI BLOKADY DLA RUCHU ----
            if (isMove || isRun || isRetreat)
            {
                if (tType == TargetType.Closest && !context.OpponentExist) continue;
                if (tType == TargetType.Furthest && !context.FurthestUnitExist) continue;
                if (tType == TargetType.MostInjured && !context.MostInjuredUnitExist) continue;
                if (tType == TargetType.LeastInjured && !context.LeastInjuredUnitExist) continue;
                if (tType == TargetType.Weakest && !context.WeakestUnitExist) continue;
                if (tType == TargetType.Strongest && !context.StrongestUnitExist) continue;
                if (tType == TargetType.MostAlliesNearby && !context.TargetWithMostAlliesExist) continue;
            }

            if(aType == AttackType.ChangeWeaponToRanged || aType == AttackType.ChangeWeaponToMelee)
            {
                string chosenWeaponType = aType == AttackType.ChangeWeaponToRanged ? "ranged" : "melee";
                bool chosenTypeExist = false;

                if(hasRangedWeapon && aType == AttackType.ChangeWeaponToRanged) continue;
                if(!hasRangedWeapon && aType == AttackType.ChangeWeaponToMelee) continue;

                // Sprawdzenie, czy jednostka posiada więcej niż jedną broń
                if (unit.GetComponent<Inventory>().AllWeapons.Count > 1)
                {
                    //  Zmienia bronie dopóki nie znajdzie takiej, która posuje do wybranego typu
                    foreach (Weapon w in  unit.GetComponent<Inventory>().AllWeapons)
                    {
                        if (w.Type.Contains(chosenWeaponType))
                        {
                            chosenTypeExist = true;
                            break;
                        }
                    }
                    
                    if (chosenTypeExist == false) continue;
                }
                else continue;
            }

            if (aType == AttackType.Retreat && isInMelee == false) continue;

            // ---- AKCJE SPECJALNE ----
            if (aType == AttackType.Reload)
            {
                if (context.CurrentWeapon != null && context.WeaponIsLoaded) continue;
            }

            validActions.Add(i);
        }

        // Jeśli brak dozwolonych akcji => FinishTurn
        if (validActions.Count == 0)
        {
            return new ActionChoice
            {
                ActionId = 42,
                ChosenTarget = fallbackTarget,
                ChosenStates = fallbackStates ?? new bool[(int)AIState.COUNT]
            };
        }

        // --- EPSILON-GREEDY ---
        int chosenActionId;
        if (UnityEngine.Random.value < Epsilon)
        {
            int randomIndex = UnityEngine.Random.Range(0, validActions.Count);
            chosenActionId = validActions[randomIndex];
        }
        else
        {
            float bestVal = float.NegativeInfinity;
            List<int> bestActions = new List<int>();

            foreach (int actID in validActions)
            {
                float val = qTable[context.StateIndex, actID];
                if (val > bestVal)
                {
                    bestVal = val;
                    bestActions.Clear();
                    bestActions.Add(actID);
                }
                else if (Mathf.Approximately(val, bestVal))
                {
                    bestActions.Add(actID);
                }
            }

            if (bestActions.Count > 0)
            {
                int randomBestIndex = UnityEngine.Random.Range(0, bestActions.Count);
                chosenActionId = bestActions[randomBestIndex];
            }
            else
            {
                int randomIndex = UnityEngine.Random.Range(0, validActions.Count);
                chosenActionId = validActions[randomIndex];
            }
        }

        ActionDefinition chosenDef = AllActions[chosenActionId];
        Unit chosenTarget = chosenDef.targetType != TargetType.None? GetTargetByType(context.Info, chosenDef.targetType) : null;
        bool[] chosenStates = null;

        if (chosenDef.targetType != TargetType.None)
        {
            if (!statesCache.TryGetValue(chosenTarget, out chosenStates))
            {
                chosenStates = DetermineStates(unit, chosenTarget);
            }
        }
        else
        {
            chosenStates = nonTargetStates;
        }

        return new ActionChoice
        {
            ActionId = chosenActionId,
            ChosenTarget = chosenTarget,
            ChosenStates = chosenStates
        };
    }

    public class ActionChoice
    {
        public int ActionId;         // numer akcji w AllActions
        public Unit ChosenTarget;    // docelowy target
        public bool[] ChosenStates;  // stany dla (unit, target)
    }

    /// Aktualizuje Q wg formuły Q-learningu dla danej rasy.
    private void UpdateQ(string raceName, int oldState, int action, float reward, int newState)
    {
        float[,] qTable = GetQTable(raceName);

        float oldQ = qTable[oldState, action];
        float maxQnext = float.NegativeInfinity;
        for (int a = 0; a < ACTION_COUNT; a++)
        {
            if (qTable[newState, a] > maxQnext)
            {
                maxQnext = qTable[newState, a];
            }
        }

        // Zabezpieczenie przed float.NegativeInfinity
        if (maxQnext == float.NegativeInfinity)
        {
            maxQnext = 0f;
        }

        float newQ = oldQ + Alpha * (reward + Gamma * maxQnext - oldQ);
        qTable[oldState, action] = newQ;
    }

    // ======================================================================
    //         LOGIKA STANÓW ORAZ GŁÓWNA METODA SimulateUnit
    // ======================================================================

    public bool[] DetermineStates(Unit unit, Unit target)
    {
        bool[] states = new bool[(int)AIState.COUNT];

        // Pobranie komponentów
        Stats stats = unit.GetComponent<Stats>();
        Weapon weapon = InventoryManager.Instance.ChooseWeaponToAttack(unit.gameObject);

        // Sprawdzenie, czy jednostka ma broń zasięgową
        bool hasRanged = weapon != null && weapon.Type.Contains("ranged");
        states[(int)AIState.HasRangedWeapon] = hasRanged;

        if (target != null)
        {
            float distance = CombatManager.Instance.CalculateDistance(unit.gameObject, target.gameObject);

            // Ustawienie stanu IsInMelee
            states[(int)AIState.IsInMelee] = distance <= 1.5f;

            // Pobranie zasięgu ataku z broni
            float attackRange = weapon != null ? weapon.AttackRange : 0f;

            // Ustawienie stanu IsBeyondAttackRange
            if (hasRanged)
            {
                // Dla broni zasięgowej
                states[(int)AIState.IsBeyondAttackRange] = !CombatManager.Instance.ValidateRangedAttack(unit, target, weapon, distance);
            }
            else
            {
                // Dla broni bezzasięgowej, IsBeyondAttackRange jest prawdziwe, gdy odległość > attackRange
                states[(int)AIState.IsBeyondAttackRange] = distance > attackRange;
            }

            // Ustawienie stanu IsInChargeRange
            float chargeRange = stats.TempSz * 2;
            //Ścieżka ruchu szarżującego
            GameObject targetTile = CombatManager.Instance.GetTileAdjacentToTarget(unit.gameObject, target.gameObject);
            
            if(targetTile == null)
            {
                states[(int)AIState.IsInChargeRange] = false;
            }
            else
            {
                Vector2 targetTilePosition = targetTile.transform.position;
                List<Vector2> path = MovementManager.Instance.FindPath(unit.transform.position, targetTilePosition);
                if(path == null || path.Count == 0)
                {
                    states[(int)AIState.IsInChargeRange] = false;
                }
                else
                {
                    states[(int)AIState.IsInChargeRange] = path.Count <= chargeRange && path.Count >= 2;
                }
            }

            // Ustalmy, że to ma sens tylko dla broni dystansowych:
            if (hasRanged)
            {
                states[(int)AIState.TargetBehindObstacle] = IsTargetBehindObstacle(unit.gameObject, target.gameObject, distance);
            }
            else
            {
                states[(int)AIState.TargetBehindObstacle] = false;
            }
        }

        // Ustawienie stanu IsHeavilyWounded
        if (stats != null)
        {
            states[(int)AIState.IsHeavilyWounded] = stats.TempHealth <= stats.MaxHealth / 3f;
        }

        // Ustawienie stanu WeaponIsLoaded
        states[(int)AIState.WeaponIsLoaded] = weapon.ReloadLeft == 0 ? true : false;

        return states;
    }

    // Główna metoda sterująca akcjami jednostki w turze – Q-learning.
    public void SimulateUnit(Unit unit)
    {
        if (unit == null) return;
        Stats stats = unit.GetComponent<Stats>();
        if (stats == null || stats.TempHealth < 0) return; // martwy

        // (A) Zbierz info o celach i wyznacz stan bazowy
        TargetsInfo info = GatherTargetsInfo(unit);
        Unit defaultTarget = info.Closest;
        bool[] baseStates = DetermineStates(unit, defaultTarget);
        int baseStateIndex = EncodeState(baseStates);

        // (B) Przygotuj kontekst decyzji
        ActionContext ctx = new ActionContext
        {
            Unit = unit,
            RaceName = stats.Race,
            StateIndex = baseStateIndex,

            HasRanged = baseStates[(int)AIState.HasRangedWeapon],
            IsInMelee = baseStates[(int)AIState.IsInMelee],
            IsBeyondAttackRange = baseStates[(int)AIState.IsBeyondAttackRange],
            IsInChargeRange = baseStates[(int)AIState.IsInChargeRange],
            TargetBehindObstacle = baseStates[(int)AIState.TargetBehindObstacle],
            IsHeavilyWounded = baseStates[(int)AIState.IsHeavilyWounded],
            WeaponIsLoaded = baseStates[(int)AIState.WeaponIsLoaded],

            OpponentExist = (info.Closest != null),
            FurthestUnitExist = (info.Furthest != null),
            MostInjuredUnitExist = (info.MostInjured != null),
            LeastInjuredUnitExist = (info.LeastInjured != null),
            WeakestUnitExist = (info.Weakest != null),
            StrongestUnitExist = (info.Strongest != null),
            TargetWithMostAlliesExist = (info.WithMostAllies != null),

            CurrentWeapon = InventoryManager.Instance.ChooseWeaponToAttack(unit.gameObject),
            Info = info
        };

        // (C) Wybór i wykonanie akcji
        ActionChoice choice = ChooseValidActionEpsilonGreedy(ctx);
        int actionId = choice.ActionId;
        Unit target = choice.ChosenTarget;
        bool[] oldStates = choice.ChosenStates;
        int oldState = EncodeState(oldStates);

        // Historia dla terminal reward
        if (!unitActionHistory.ContainsKey(unit))
            unitActionHistory[unit] = new Queue<(int, int, string)>();
        var history = unitActionHistory[unit];
        history.Enqueue((oldState, actionId, ctx.RaceName));
        if (history.Count > HISTORY_LIMIT)
            history.Dequeue();

        // (D) Wykonaj akcję i policz reward
        int prevHP = stats.TempHealth;
        float reward = PerformParameterAction(actionId, unit, info, prevHP);

        // Potential-based shaping
        float phiOld = ComputePotential(oldStates);
        bool[] newStates = DetermineStates(unit, target);
        float phiNew = ComputePotential(newStates);
        reward += Gamma * phiNew - phiOld;

        // Logowanie nagród
        currentEpochReward += reward;
        actionsThisEpoch++;

        // (E) Dodaj transition do Replay z priorytetem
        int nextState = EncodeState(newStates);
        float initPrio = replayBuffer.Count > 0 ? replayBuffer.priorities.Max() : 1f;
        var trans = new Transition
        {
            RaceName = ctx.RaceName,
            State = oldState,
            Action = actionId,
            Reward = reward,
            NextState = nextState,
            Priority = initPrio
        };
        replayBuffer.Add(trans);

        // (F) Aktualizacja Q: on-line lub batch z priorytetami i λ-traces
        if (replayBuffer.Count >= ReplayStartSize)
        {
            TrainFromReplay();
        }
        else
        {
            UpdateQ(ctx.RaceName, oldState, actionId, reward, nextState);
        }

        // (G) Koniec epoki? Logi, decay ε i reset nagród
        if (actionsThisEpoch >= ActionsPerEpoch)
        {
            float avgReward = currentEpochReward / actionsThisEpoch;
            epochRewards.Add(avgReward);
            Debug.Log($"Epoch completed. Avg Reward: {avgReward:F2}");

            currentEpochReward = 0f;
            actionsThisEpoch = 0;

            currentEpoch++;
            float t = Mathf.Min(1f, (float)currentEpoch / EpsilonDecayEpochs);
            Epsilon = Mathf.Lerp(EpsilonStart, EpsilonEnd, t);

            SaveAverageReward(avgReward);
            SaveQTables();
        }
    }

    private float PerformParameterAction(int actionID, Unit unit, TargetsInfo info, int oldHP)
    {
        float reward = 0f;
        // Sprawdzamy definicję:
        if (actionID < 0 || actionID >= AllActions.Length)
        {
            // out of range => FinishTurn
            RoundsManager.Instance.FinishTurn();
            return reward;
        }

        ActionDefinition def = AllActions[actionID];
        TargetType tType = def.targetType;
        AttackType aType = def.attackType;

        // Znajdujemy Unit target (jeśli w ogóle)
        Unit target = GetTargetByType(info, tType);

        // 1) Move
        if (aType == AttackType.Move)
        {
            if (target != null)
                MoveTowards(unit, target.gameObject);
            reward += CalculateRewardBasedOnUnitHealth(unit.GetComponent<Stats>(), oldHP);
            return reward;
        }

        // 2) Run
        if (aType == AttackType.Run)
        {
            if (target != null)
                MoveTowards(unit, target.gameObject, 3);
            reward += CalculateRewardBasedOnUnitHealth(unit.GetComponent<Stats>(), oldHP);
            return reward;
        }

        // 3) Specjalne akcje:
        if (aType == AttackType.Aim)
        {
            CombatManager.Instance.SetAim();
            return reward;
        }
        if (aType == AttackType.Reload)
        {
            CombatManager.Instance.Reload();
            reward++; // drobna nagroda
            return reward;
        }
        if (aType == AttackType.FinishTurn)
        {
            RoundsManager.Instance.FinishTurn();
            return reward;
        }
        if (aType == AttackType.MoveAway)
        {
            GameObject retreatTile = GetTileFarthestFromTarget(unit.gameObject, target.gameObject);
            if (retreatTile != null && target != null)
                MoveTowards(unit, retreatTile, 1, true);
            reward += CalculateRewardBasedOnUnitHealth(unit.GetComponent<Stats>(), oldHP);
            return reward;
        }
        if (aType == AttackType.RunAway)
        {
            GameObject retreatTile = GetTileFarthestFromTarget(unit.gameObject, target.gameObject);
            if (retreatTile != null && target != null)
                MoveTowards(unit, retreatTile, 3, true);
            reward += CalculateRewardBasedOnUnitHealth(unit.GetComponent<Stats>(), oldHP);
            return reward;
        }
        if (aType == AttackType.Retreat)
        {
            GameObject retreatTile = GetTileFarthestFromTarget(unit.gameObject, target.gameObject);
            if (retreatTile != null && target != null)
            {
                MovementManager.Instance.Retreat(true);
                MoveTowards(unit, retreatTile, 1, true);
            }
            reward += CalculateRewardBasedOnUnitHealth(unit.GetComponent<Stats>(), oldHP);
            return reward;
        }
        if(aType == AttackType.ChangeWeaponToRanged)
        {
            ChangeWeapon(unit, "ranged");
            return reward;
        }
        if(aType == AttackType.ChangeWeaponToMelee)
        {
            ChangeWeapon(unit, "melee");
            return reward;
        }

        //Będziemy sprawdzać, czy zdołaliśmy zabić wroga i dać nagrodę, a jeśli my umarliśmy – karę]
        // Najpierw zapisujemy HP wroga (jeśli jest)
        int enemyOverall = 0;
        if (target != null)
        {
            enemyOverall = target.GetComponent<Stats>().Overall; 
        }

        // 4) Różne typy ataku (zwykły, Charge, Feint, Swift, Guarded, AllOut)
        // 'Null' = zwykły atak
        string attackName = null;
        switch (aType)
        {
            case AttackType.Null:    attackName = null; break;
            case AttackType.Charge:  attackName = "Charge"; break;
            case AttackType.AllOutAttack: attackName = "AllOutAttack"; break;
        }

        reward += PerformAttack(unit.gameObject, target? target.gameObject:null, attackName);

        //Sprawdźmy, czy przeciwnik został zabity po ataku]
        if (target == null ||  target.GetComponent<Stats>().TempHealth < 0)
        {
            // Zakładam, że jeśli target jest null, został usunięty z gry (zabity)
            // Nagroda proporcjonalna do overall wroga
            reward += enemyOverall / 5;
        }

        reward += RoundsManager.RoundNumber / 3f; //Nagroda za przeżycie jak najdłużej. Im dłużej przeżyje tym większą nagrodę dostaje za każdą rundę

        return reward;
    }

    private Unit GetTargetByType(TargetsInfo info, TargetType t)
    {
        switch (t)
        {
            case TargetType.Closest:     return info.Closest;
            case TargetType.Furthest:    return info.Furthest;
            case TargetType.MostInjured: return info.MostInjured;
            case TargetType.LeastInjured:return info.LeastInjured;
            case TargetType.Weakest:     return info.Weakest;
            case TargetType.Strongest:   return info.Strongest;
            case TargetType.MostAlliesNearby: return info.WithMostAllies;
            default: return null; // None
        }
    }

    private static readonly ActionDefinition[] AllActions = new ActionDefinition[]
    {
        // --- MOVE ---
        new ActionDefinition(TargetType.Closest,          AttackType.Move),         // 0: Ruch - Najbliższy
        new ActionDefinition(TargetType.Furthest,         AttackType.Move),         // 1: Ruch - Najdalszy
        new ActionDefinition(TargetType.MostInjured,      AttackType.Move),         // 2: Ruch - Najbardziej ranny
        new ActionDefinition(TargetType.LeastInjured,     AttackType.Move),         // 3: Ruch - Najmniej ranny
        new ActionDefinition(TargetType.Weakest,          AttackType.Move),         // 4: Ruch - Najsłabszy
        new ActionDefinition(TargetType.Strongest,        AttackType.Move),         // 5: Ruch - Najsilniejszy
        new ActionDefinition(TargetType.MostAlliesNearby, AttackType.Move),         // 6: Ruch - Najwięcej sojuszników

        // --- RUN ---
        new ActionDefinition(TargetType.Closest,          AttackType.Run),          // 7: Bieg - Najbliższy
        new ActionDefinition(TargetType.Furthest,         AttackType.Run),          // 8: Bieg - Najdalszy
        new ActionDefinition(TargetType.MostInjured,      AttackType.Run),          // 9: Bieg - Najbardziej ranny
        new ActionDefinition(TargetType.LeastInjured,     AttackType.Run),          // 10: Bieg - Najmniej ranny
        new ActionDefinition(TargetType.Weakest,          AttackType.Run),          // 11: Bieg - Najsłabszy
        new ActionDefinition(TargetType.Strongest,        AttackType.Run),          // 12: Bieg - Najsilniejszy
        new ActionDefinition(TargetType.MostAlliesNearby, AttackType.Run),          // 13: Bieg - Najwięcej sojuszników

        // --- NORMAL ATTACK ---
        new ActionDefinition(TargetType.Closest,          AttackType.Null),         // 14: Zwykły atak - Najbliższy
        new ActionDefinition(TargetType.Furthest,         AttackType.Null),         // 15: Zwykły atak - Najdalszy
        new ActionDefinition(TargetType.MostInjured,      AttackType.Null),         // 16: Zwykły atak - Najbardziej ranny
        new ActionDefinition(TargetType.LeastInjured,     AttackType.Null),         // 17: Zwykły atak - Najmniej ranny
        new ActionDefinition(TargetType.Weakest,          AttackType.Null),         // 18: Zwykły atak - Najsłabszy
        new ActionDefinition(TargetType.Strongest,        AttackType.Null),         // 19: Zwykły atak - Najsilniejszy
        new ActionDefinition(TargetType.MostAlliesNearby, AttackType.Null),         // 20: Zwykły atak - Najwięcej sojuszników

        // --- CHARGE ---
        new ActionDefinition(TargetType.Closest,          AttackType.Charge),       // 21: Szarża - Najbliższy
        new ActionDefinition(TargetType.Furthest,         AttackType.Charge),       // 22: Szarża - Najdalszy
        new ActionDefinition(TargetType.MostInjured,      AttackType.Charge),       // 23: Szarża - Najbardziej ranny
        new ActionDefinition(TargetType.LeastInjured,     AttackType.Charge),       // 24: Szarża - Najmniej ranny
        new ActionDefinition(TargetType.Weakest,          AttackType.Charge),       // 25: Szarża - Najsłabszy
        new ActionDefinition(TargetType.Strongest,        AttackType.Charge),       // 26: Szarża - Najsilniejszy
        new ActionDefinition(TargetType.MostAlliesNearby, AttackType.Charge),       // 27: Szarża - Najwięcej sojuszników

        // --- ALL OUT ATTACK ---
        new ActionDefinition(TargetType.Closest,          AttackType.AllOutAttack), // 28: Szaleńczy atak - Najbliższy
        new ActionDefinition(TargetType.Furthest,         AttackType.AllOutAttack), // 29: Szaleńczy atak - Najdalszy
        new ActionDefinition(TargetType.MostInjured,      AttackType.AllOutAttack), // 30: Szaleńczy atak - Najbardziej ranny
        new ActionDefinition(TargetType.LeastInjured,     AttackType.AllOutAttack), // 31: Szaleńczy atak - Najmniej ranny
        new ActionDefinition(TargetType.Weakest,          AttackType.AllOutAttack), // 32: Szaleńczy atak - Najsłabszy
        new ActionDefinition(TargetType.Strongest,        AttackType.AllOutAttack), // 33: Szaleńczy atak - Najsilniejszy
        new ActionDefinition(TargetType.MostAlliesNearby, AttackType.AllOutAttack), // 34: Szaleńczy atak - Najwięcej sojuszników

        // --- AIM / RELOAD ---
        new ActionDefinition(TargetType.None,             AttackType.Aim),          // 35: Przycelowanie
        new ActionDefinition(TargetType.None,             AttackType.Reload),       // 36: Przeładowanie

        // --- MOVE AWAY / RUN AWAY / RETREAT ---
        new ActionDefinition(TargetType.Closest,          AttackType.MoveAway),     // 37: Odejście od najbliższego
        new ActionDefinition(TargetType.Closest,          AttackType.RunAway),      // 38: Bieg jak najdalej od najbliższego
        new ActionDefinition(TargetType.Closest,          AttackType.Retreat),      // 39: Bezpieczny odwrót

        // --- WEAPON CHANGE ---
        new ActionDefinition(TargetType.None,             AttackType.ChangeWeaponToMelee),   // 40: Zmiana broni na melee
        new ActionDefinition(TargetType.None,             AttackType.ChangeWeaponToRanged),  // 41: Zmiana broni na ranged

        // --- FINISH TURN ---
        new ActionDefinition(TargetType.None,             AttackType.FinishTurn),            // 42: Zakończenie tury
    };


    // ======================================================================
    //                      POMOCNICZE METODY
    // ======================================================================

    private void MoveTowards(Unit unit, GameObject targetObject, int modifier = 1, bool retreat = false)
    {
        //Bieg
        if(modifier != 1)
        {
            MovementManager.Instance.UpdateMovementRange(modifier);
        }

        GameObject tile;
        if(retreat)
        {
            tile = targetObject;
        }
        else
        {
            tile = CombatManager.Instance.GetTileAdjacentToTarget(unit.gameObject, targetObject);
        }

        if (tile == null) return;
        MovementManager.Instance.MoveSelectedUnit(tile, unit.gameObject);
        Physics2D.SyncTransforms();
    }

    private int PerformAttack(GameObject attacker, GameObject target, string attackType)
    {
        if (attackType != null)
        {
            CombatManager.Instance.ChangeAttackType(attackType);
        }

        int oldHP = target.GetComponent<Stats>().TempHealth;
       
        CombatManager.Instance.Attack(attacker.GetComponent<Unit>(), target.GetComponent<Unit>(), false);

        int newHP = target.GetComponent<Stats>().TempHealth;

        int damageReward = oldHP - newHP;

        return damageReward; // Nagroda równa różnicy w HP przeciwnika
    }

    private void ChangeWeapon(Unit unit, string chosenWeaponType)
    {
        Weapon weapon = InventoryManager.Instance.ChooseWeaponToAttack(unit.gameObject);

        if (weapon.Type.Contains(chosenWeaponType)) return;

        // Sprawdzenie, czy jednostka posiada więcej niż jedną broń
        if (InventoryManager.Instance.InventoryScrollViewContent.GetComponent<CustomDropdown>().Buttons.Count > 1)
        {
            int selectedIndex = 1;

            //  Zmienia bronie dopóki nie znajdzie takiej, która posuje do wybranego typu
            for (int i = 0; i < InventoryManager.Instance.InventoryScrollViewContent.GetComponent<CustomDropdown>().Buttons.Count; i++)
            {
                if (weapon.Type.Contains(chosenWeaponType)) break;

                InventoryManager.Instance.InventoryScrollViewContent.GetComponent<CustomDropdown>().SetSelectedIndex(selectedIndex);
                InventoryManager.Instance.InventoryScrollViewContent.GetComponent<CustomDropdown>().SelectedButton = InventoryManager.Instance.InventoryScrollViewContent.GetComponent<CustomDropdown>().Buttons[selectedIndex - 1];

                InventoryManager.Instance.GrabWeapon();
                weapon = InventoryManager.Instance.ChooseWeaponToAttack(unit.gameObject);

                selectedIndex++;
            }
        }
    }

    private int CalculateRewardBasedOnUnitHealth(Stats stats, int oldAttackerHP)
    {
        int reward = 0;

        // Obliczamy utratę HP atakującego
        int newAttackerHP = stats.TempHealth;
        int lostHP = oldAttackerHP - newAttackerHP;

        // Kara za utracone HP
        if (lostHP > 0)
        {
            reward -= lostHP; // np. -1 za każdy utracony punkt
        }

        //Kara za śmierć
        if (stats == null || stats.TempHealth < 0)
        {
            reward -= 15;
        }

        return reward;
    }

    public void GiveTerminalRewardToAll(bool didAIWin)
    {
        float shaped = didAIWin ? WIN_REWARD : LOSS_REWARD;
        foreach (var queue in unitActionHistory.Values)
        {
            foreach (var (state, action, race) in queue)
            {
                var q = GetQTable(race);
                q[state, action] += shaped;
            }
        }
        SaveQTables();
        unitActionHistory.Clear();
    }

    public TargetsInfo GatherTargetsInfo(Unit currentUnit)
    {
        TargetsInfo info = new TargetsInfo();

        foreach (Unit other in UnitsManager.Instance.AllUnits)
        {
            if (!IsValidTarget(currentUnit, other)) 
                continue;

            // Oblicz distance
            float dist = Vector2.Distance(currentUnit.transform.position, other.transform.position);
            info.Distances[other] = dist;

            // 1. Najbliższy
            if (dist < info.ClosestDistance)
            {
                info.ClosestDistance = dist;
                info.Closest = other;
            }

            // 2. Najdalszy
            if (dist > info.FurthestDistance)
            {
                info.FurthestDistance = dist;
                info.Furthest = other;
            }

            // 3. Najbardziej ranny
            float hp = other.GetComponent<Stats>().TempHealth;
            if (hp < info.MostInjuredHP)
            {
                info.MostInjuredHP = hp;
                info.MostInjured = other;
            }

            // 4. Najmniej ranny
            if (hp > info.LeastInjuredHP)
            {
                info.LeastInjuredHP = hp;
                info.LeastInjured = other;
            }

            // 5. Najniższy Overall
            int ov = other.GetComponent<Stats>().Overall;
            if (ov < info.WeakestOverall)
            {
                info.WeakestOverall = ov;
                info.Weakest = other;
            }

            // 6. Najwyższy Overall
            if (ov > info.StrongestOverall)
            {
                info.StrongestOverall = ov;
                info.Strongest = other;
            }

            // 7. targetWithMostAllies:
            // Znajdujemy jednostkę z największą przewagą liczebną sojuszników
            int adjacentAllies = 0;
            int adjacentOpponents = 0;
            CountAdjacentUnits(other.transform.position, currentUnit.tag, other.tag, ref adjacentAllies, ref adjacentOpponents);
            int advantage = adjacentAllies - adjacentOpponents;
            if (advantage > info.WithMostAlliesScore)
            {
                info.WithMostAlliesScore = advantage;
                info.WithMostAllies = other;
            }
        }

        return info;
    }

    private bool IsTargetBehindObstacle(GameObject attacker, GameObject target, float attackDistance)
    {
        if (attacker == null || target == null) return false;

        Vector2 origin = attacker.transform.position;
        Vector2 direction = target.transform.position - attacker.transform.position;
        
        // RaycastAll w stronę celu
        RaycastHit2D[] raycastHits = Physics2D.RaycastAll(origin, direction, attackDistance);

        foreach (var hit in raycastHits)
        {
            if (hit.collider == null) continue;

            // Pomińmy samego siebie i cel:
            if (hit.collider.gameObject == attacker || hit.collider.gameObject == target)
                continue;

            // Sprawdzamy przeszkodę
            var mapElement = hit.collider.GetComponent<MapElement>();
            var otherUnit  = hit.collider.GetComponent<Unit>();

            if ((mapElement != null && mapElement.IsLowObstacle) || otherUnit != null)
            {
                // Zwracamy true - bo to znaczy, że jest co najmniej jedna przeszkoda
                return true;
            }
        }

        return false;
    }

    public GameObject GetTileFarthestFromTarget(GameObject attacker, GameObject target)
    {
        if (target == null) return null;

        Vector2 attackerPos = attacker.transform.position;
        Vector2 targetPos = target.transform.position;

        // Pobierz zasięg ruchu jednostki
        int movementRange = attacker.GetComponent<Stats>().TempSz;

        Tile farthestTile = null;
        float maxDistance = -1f;
        int shortestPathLengthForFarthest = int.MaxValue;

        foreach (Tile tile in GridManager.Instance.Tiles)
        {
            if (tile.IsOccupied) continue; // Pomijamy zajęte pola

            Vector2 tilePos = tile.transform.position;

            // Oblicz długość ścieżki do danego pola
            List<Vector2> path = MovementManager.Instance.FindPath(attackerPos, tilePos);
            if (path.Count == 0) continue; // Brak ścieżki
            if (path.Count > movementRange) continue; // Poza zasięgiem ruchu

            // Oblicz odległość od przeciwnika
            float distanceToTarget = Vector2.Distance(tilePos, targetPos);

            // Aktualizacja najdalszego pola
            if (distanceToTarget > maxDistance)
            {
                maxDistance = distanceToTarget;
                farthestTile = tile;
                shortestPathLengthForFarthest = path.Count;
            }
        }

        if (farthestTile != null)
        {
            return farthestTile.gameObject;
        }
        else
        {
            return null;
        }
    }

    private void CountAdjacentUnits(Vector2 center, string allyTag, string opponentTag, ref int allies, ref int opponents)
    {
        HashSet<Collider2D> countedOpponents = new HashSet<Collider2D>();
        Vector2[] positions =
        {
            center + Vector2.right, center + Vector2.left,
            center + Vector2.up, center + Vector2.down,
            center + new Vector2(1,1), center + new Vector2(-1,-1),
            center + new Vector2(-1,1), center + new Vector2(1,-1)
        };

        foreach (var pos in positions)
        {
            Collider2D col = Physics2D.OverlapPoint(pos);
            if (col == null) continue;

            if (col.CompareTag(allyTag))
            {
                allies++;
            }
            else if (col.CompareTag(opponentTag) && !countedOpponents.Contains(col))
            {
                opponents++;
                countedOpponents.Add(col);
            }
        }
    }

    private bool IsValidTarget(Unit currentUnit, Unit other)
    {
        if (other == null) return false;
        if (other == currentUnit) return false;
        if (other.CompareTag(currentUnit.tag)) return false;
        if (other.GetComponent<Stats>().TempHealth < 0) return false;

        return true;
    }

    public bool BothTeamsExist()
    {
        bool enemyUnitExists = false;
        bool playerUnitExists = false;

        foreach (Unit unit in UnitsManager.Instance.AllUnits)
        {
            if (unit == null) continue;

            if (unit.CompareTag("PlayerUnit")) playerUnitExists = true;
            else if (unit.CompareTag("EnemyUnit")) enemyUnitExists = true;

            // Jeśli obie drużyny istnieją, zwróć true natychmiast
            if (playerUnitExists && enemyUnitExists) return true;
        }

        if(playerUnitExists == false && enemyUnitExists == true)
        {    
            _enemyWins ++;
        }
        else if(enemyUnitExists == false && playerUnitExists == true)
        {
            _playerWins ++;
        }

        // Jeśli pętla się zakończy, sprawdź, czy którakolwiek drużyna nie istnieje
        return playerUnitExists && enemyUnitExists;
    }

    // ======================================================================
    // ZAPIS / ODCZYT tablic Q
    // ======================================================================
    [System.Serializable]
    public class QTableData
    {
        public string raceName;
        public int rows;
        public int cols;
        public List<float> values = new List<float>();
    }

    [System.Serializable]
    public class QTablesContainer
    {
        public List<QTableData> tables = new List<QTableData>();
    }

    public void SaveQTables()
    {
        QTablesContainer container = new QTablesContainer();

        foreach (var kvp in QTables) // kvp.Key -> raceName, kvp.Value -> float[,]
        {
            QTableData data = new QTableData();
            data.raceName = kvp.Key;

            int rows = kvp.Value.GetLength(0);
            int cols = kvp.Value.GetLength(1);
            data.rows = rows;
            data.cols = cols;

            for (int r=0; r<rows; r++)
            {
                for (int c=0; c<cols; c++)
                {
                    data.values.Add( kvp.Value[r,c] );
                }
            }

            container.tables.Add(data);
        }

        string json = JsonUtility.ToJson(container, true);

        string filePath = Path.Combine(Application.persistentDataPath, "q_tables.json");
        File.WriteAllText(filePath, json);

        Debug.Log($"QTables saved to {filePath}");
    }

    public void LoadQTables()
    {
        string filePath = Path.Combine(Application.persistentDataPath, "q_tables.json");
        if (!File.Exists(filePath))
        {
            Debug.LogWarning("No QTables file found to load.");
            return;
        }

        string json = File.ReadAllText(filePath);
        QTablesContainer container = JsonUtility.FromJson<QTablesContainer>(json);

        foreach (var data in container.tables)
        {
            float[,] table = new float[data.rows, data.cols];
            int idx = 0;

            for (int r=0; r<data.rows; r++)
            {
                for (int c=0; c<data.cols; c++)
                {
                    table[r,c] = data.values[idx];
                    idx++;
                }
            }

            QTables[data.raceName] = table;

            _trainedRaces.Add(data.raceName);
        }

        Debug.Log($"QTables loaded from {filePath}");
    }

    
    // ======================================================================
    //                        DEBUGOWANIE WYNIKÓW
    // ======================================================================
    public void UpdateTeamWins()
    {
        _teamWinsDisplay.text = $"Player wins: {_playerWins} Enemy wins: {_enemyWins}";
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
        // Sprawdzamy, czy folder istnieje
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }

        foreach (string raceName in QTables.Keys)
        {
            // Tworzymy ścieżkę pliku np. "folderPath/Q_raceName.csv"
            string sanitizedRace = raceName.Replace(" ", "_"); 
            string fileName = $"Q_{sanitizedRace}.csv";
            string filePath = Path.Combine(folderPath, fileName);

            ExportQToCSV(raceName, filePath);
        }
    }

    public void ExportQToCSV(string raceName, string filePath)
    {
        if (!QTables.ContainsKey(raceName)) return;

        float[,] table = QTables[raceName];
        int numStates = table.GetLength(0);
        int numActions = table.GetLength(1);
        int numberOfStates = (int)AIState.COUNT;

        using (StreamWriter sw = new StreamWriter(filePath))
        {
            // Nagłówek: State;IsInMelee;IsHeavilyWounded;...
            List<string> headers = new List<string> { "State" };
            for (int i = 0; i < numberOfStates; i++)
            {
                headers.Add(((AIState)i).ToString());
            }
            headers.Add("Action");
            headers.Add("QValue");
            sw.WriteLine(string.Join(";", headers));

            for (int s = 0; s < numStates; s++)
            {
                // Tworzenie wartości dla kolumn stanów
                List<string> rowValues = new List<string> { s.ToString() };
                for (int i = 0; i < numberOfStates; i++)
                {
                    bool isSet = (s & (1 << i)) != 0;
                    rowValues.Add(isSet.ToString().ToLower()); // true/false w małych literach
                }

                for (int a = 0; a < numActions; a++)
                {
                    float val = table[s, a];
                    List<string> actionValues = new List<string>(rowValues)
                    {
                        a.ToString(),
                        val.ToString("F2") // Formatowanie Q-value do 2 miejsc po przecinku
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
                // Nagłówki z separatorem ;
                sw.WriteLine("Epoch;AverageReward");
            }

            int currentEpoch = epochRewards.Count;
            // Dane z separatorem ;
            sw.WriteLine($"{currentEpoch};{averageReward}");
        }

        Debug.Log($"Average reward for epoch {epochRewards.Count} saved to {filePath}");

        // Aktualizacja wykresu
        if (simpleGraph != null)
        {
            simpleGraph.AddValue(averageReward);
        }
    }
}