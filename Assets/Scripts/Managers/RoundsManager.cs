using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

public class RoundsManager : MonoBehaviour
{
    // Prywatne statyczne pole przechowujące instancję
    private static RoundsManager instance;

    // Publiczny dostęp do instancji
    public static RoundsManager Instance
    {
        get { return instance; }
    }

    void Awake()
    {
        if (instance == null)
        {
            instance = this;
            //DontDestroyOnLoad(gameObject);
        }
        else if (instance != this)
        {
            // Jeśli instancja już istnieje, a próbujemy utworzyć kolejną, niszczymy nadmiarową
            Destroy(gameObject);
        }
    }
    public static int RoundNumber;
    [SerializeField] private TMP_Text _roundNumberDisplay;
    [SerializeField] private TMP_Text _playersRoundNumberDisplay;
    public UnityEngine.UI.Button NextRoundButton;
    [SerializeField] private UnityEngine.UI.Toggle _canDoActionToggle;
    [SerializeField] private GameObject _useFortunePointsButton;
    private bool _isFortunePointSpent; //informacja o tym, że punkt szczęścia został zużyty, aby nie można było ponownie go użyć do wczytania tego samego autozapisu
    private Coroutine _autoCombatCoroutine;
    private bool _isRoundAdvanceScheduled;
    private bool _isRoundGenerationInProgress;
    private Coroutine _turnStartCoroutine;
    public Unit TurnStartUnit { get; private set; }
    public bool IsResolvingTurnStart { get; private set; }

    public bool CanUnitAct(Unit unit)
    {
        if (unit == null) return false;
        if (RoundNumber <= 0) return true;
        if (unit.IsTurnFinished) return false;
        BeginTurn(unit);
        return unit != null && !IsResolvingTurnStart && unit.TurnStartedInRound == RoundNumber;
    }

    public void BeginTurn(Unit unit)
    {
        if (unit == null || RoundNumber <= 0 || unit.IsTurnFinished || unit.TurnStartedInRound == RoundNumber) return;
        if (_isRoundGenerationInProgress || IsResolvingTurnStart) return;
        if (CombatManager.Instance != null && CombatManager.Instance.IsAttackSequenceRunning) return;
        if (MovementManager.Instance != null && MovementManager.Instance.IsMoving) return;
        if (SaveAndLoadManager.Instance != null && SaveAndLoadManager.Instance.IsLoading) return;
        unit.TurnStartedInRound = RoundNumber;
        if (!unit.Ablaze) return;
        TurnStartUnit = unit;
        IsResolvingTurnStart = true;
        GameManager.Instance?.RefreshPauseState();
        _turnStartCoroutine = StartCoroutine(ResolveUnitTurnStart(unit));
    }

    private IEnumerator ResolveUnitTurnStart(Unit unit)
    {
        yield return StatesManager.Instance.ResolveTurnStart(unit);
        TurnStartUnit = null;
        IsResolvingTurnStart = false;
        GameManager.Instance?.RefreshPauseState();
        _turnStartCoroutine = null;
        if (unit != null && !unit.CanDoAction && !unit.CanMove && !unit.IsTurnFinished)
            FinishTurn(unit);
        InitiativeQueueManager.Instance.UpdateInitiativeQueue();
    }

    private void Start()
    {
        RoundNumber = 0;
        ResetRoundsToStartUi();
        _isRoundAdvanceScheduled = false;
        _isRoundGenerationInProgress = false;
        _autoCombatCoroutine = null;

        _useFortunePointsButton.SetActive(false);
    }

    public void NextRound()
    {
        if (_isRoundGenerationInProgress || IsResolvingTurnStart) return;
        _isRoundGenerationInProgress = true;
        _isRoundAdvanceScheduled = false;

        RoundNumber++;
        _roundNumberDisplay.text = "Runda: " + RoundNumber;
        _playersRoundNumberDisplay.text = "Runda: " + RoundNumber;

        if (RoundNumber > 0)
        {
            NextRoundButton.transform.GetChild(0).GetComponent<TMP_Text>().text = "Następna runda";
        }

        Debug.Log($"<color=#4dd2ff>------------------------------------------------------------------------------------ RUNDA {RoundNumber} ------------------------------------------------------------------------------------</color>");

        // Sprawdzenie istnieje jakakolwiek jednostka ze Smrodem
        bool stinkUnitExist = UnitsManager.Instance.AllUnits.Any(u =>
        {
            var s = u.GetComponent<Stats>();
            return s != null && s.Stink;
        });

        //Resetuje ilość dostępnych akcji dla wszystkich jednostek
        foreach (Unit unit in UnitsManager.Instance.AllUnits)
        {
            if (unit == null) continue;

            Stats stats = unit.GetComponent<Stats>();

            unit.IsTurnFinished = false;
            unit.HasUsedOpportunityAttack = false;
            unit.CanDoAction = true;
            SetCanDoActionToggle(true);

            if (unit.Entangled || unit.Grappled || stats.Sz == 0)
            {
                unit.CanMove = false;
            }
            else
            {
                unit.CanMove = true;
            }

            if (unit.Unconscious && !unit.Petrified)
            {
                StartCoroutine(StatesManager.Instance.Recover(unit));
            }

            if (stats.Spellcasting > 0)
            {
                unit.CanCastSpell = true;
            }

            if (stinkUnitExist) StartCoroutine(StatesManager.Instance.HandleStink(unit));

            if (stats.ActiveSpellEffects != null && stats.ActiveSpellEffects.Count != 0)
            {
                stats.UpdateSpellEffects();
            }

            if (unit.EntangledUnitId != 0)
            {
                bool entangledUnitExist = false;

                foreach (var u in UnitsManager.Instance.AllUnits)
                {
                    if (u.UnitId == unit.EntangledUnitId && u.Entangled)
                    {
                        entangledUnitExist = true;
                    }
                }

                if (!entangledUnitExist)
                {
                    unit.EntangledUnitId = 0;
                }
            }

            if (unit.GrappledUnitId != 0)
            {
                bool grappledUnitExist = false;

                foreach (var u in UnitsManager.Instance.AllUnits)
                {
                    if (u.UnitId == unit.GrappledUnitId && u.Grappled)
                    {
                        grappledUnitExist = true;
                    }
                }

                if (!grappledUnitExist)
                {
                    unit.GrappledUnitId = 0;
                }
            }

            //Aktualizuje osiągnięcia
            stats.RoundsPlayed++;
        }

        // Wykonuje testy grozy i strachu, jeśli na polu bitwy są jednostki straszne
        if (GameManager.IsFearIncluded)
        {
            var queue = InitiativeQueueManager.Instance.InitiativeQueue;

            // Oblicza maxima dla obu stron
            int maxScaryEnemies = queue
                .Where(p => p.Key != null && p.Key.CompareTag("EnemyUnit"))
                .Select(p => p.Key.GetComponent<Stats>()?.Scary ?? 0)
                .DefaultIfEmpty(0).Max();

            int maxScaryPlayers = queue
                .Where(p => p.Key != null && p.Key.CompareTag("PlayerUnit"))
                .Select(p => p.Key.GetComponent<Stats>()?.Scary ?? 0)
                .DefaultIfEmpty(0).Max();

            // jeśli nikt nie jest straszny - pomija dalszy kod
            if (maxScaryEnemies > 0 || maxScaryPlayers > 0)
            {
                foreach (var pair in queue)
                {
                    var unit = pair.Key;
                    if (unit == null) continue;

                    // wybierz odpowiedni max poziom Strachu przeciwnika
                    int requiredLevel = unit.CompareTag("PlayerUnit") ? maxScaryEnemies : maxScaryPlayers;
                    if (requiredLevel <= unit.FearTestedLevel) continue;           // test już się odbył na tym lub wyższym poziomie Strachu

                    unit.FearTestedLevel = requiredLevel;
                    StartCoroutine(StatesManager.Instance.FearTest(unit));         // korutyna sama zrobi test na aktualne warunki
                }
            }
        }

        InitiativeQueueManager.Instance.UpdateInitiativeQueue();

        //Odświeża panel jednostki, aby zaktualizować ewentualną informację o długości trwania stanu (np. ogłuszenia) wybranej jednostki
        if (Unit.SelectedUnit != null)
        {
            UnitsManager.Instance.UpdateUnitPanel(Unit.SelectedUnit);
        }

        //Wybiera jednostkę zgodnie z kolejką inicjatywy, jeśli ten tryb jest włączony
        if (GameManager.IsAutoSelectUnitMode && InitiativeQueueManager.Instance.ActiveUnit != null)
        {
            InitiativeQueueManager.Instance.SelectUnitByQueue();
        }

        _isRoundGenerationInProgress = false;
        BeginTurn(InitiativeQueueManager.Instance.ActiveUnit);

        //Wykonuje automatyczną akcję za każdą jednostkę
        if (GameManager.IsAutoCombatMode)
        {
            if (_autoCombatCoroutine != null)
            {
                StopCoroutine(_autoCombatCoroutine);
            }

            _autoCombatCoroutine = StartCoroutine(AutoCombat());
        }
        else if (_autoCombatCoroutine != null)
        {
            StopCoroutine(_autoCombatCoroutine);
            _autoCombatCoroutine = null;
        }

    }

    IEnumerator AutoCombat()
    {
        bool shouldStartNextRound = false;
        NextRoundButton.gameObject.SetActive(false);
        _useFortunePointsButton.SetActive(false);

        while (true)
        {
            Unit unit = GetNextUnitForAutoCombat();
            if (unit == null) break;

            // Wybór jednostki bez asynchronicznego wyścigu z SelectUnitByQueue.
            if (Unit.SelectedUnit != unit.gameObject)
            {
                unit.SelectUnit();
            }

            if (!ReinforcementLearningManager.Instance.IsLearning)
            {
                yield return new WaitForSeconds(0.05f);
            }

            BeginTurn(unit);
            yield return new WaitUntil(() => !IsResolvingTurnStart);
            if (unit == null || unit.IsTurnFinished) continue;

            // Jeśli jednostka to PlayerUnit i gramy w trybie ukrywania statystyk wrogów
            if (unit.CompareTag("PlayerUnit") && GameManager.IsStatsHidingMode)
            {
                // Czeka aż jednostka zakończy swoją turę
                yield return new WaitUntil(() => (unit.CanDoAction == false && unit.CanMove == false) || unit.IsTurnFinished);
                if (!ReinforcementLearningManager.Instance.IsLearning)
                {
                    yield return new WaitForSeconds(0.6f);
                }
            }
            else // Jednostki wrogów lub wszystkie jednostki, jeśli nie ukrywamy ich statystyk
            {
                if (ReinforcementLearningManager.Instance.IsLearning)
                {
                    if (unit.CompareTag("PlayerUnit"))
                    {
                        AutoCombatManager.Instance.Act(unit);
                        yield return StartCoroutine(WaitForLearningStepResolution(unit));

                        if (ReinforcementLearningManager.Instance.ConsumeStepTimeoutFlag() && !unit.IsTurnFinished)
                        {
                            FinishTurn(unit, false);
                        }
                    }
                    else
                    {
                        int iterationCount = 0;
                        int maxIterations = ReinforcementLearningManager.Instance.GetMaxLearningIterationsPerUnitTurn();

                        while ((unit.CanDoAction || unit.CanMove) && !unit.IsTurnFinished && iterationCount < maxIterations)
                        {
                            ReinforcementLearningManager.Instance.SimulateUnit(unit);
                            yield return StartCoroutine(WaitForLearningStepResolution(unit));

                            if (ReinforcementLearningManager.Instance.ConsumeStepTimeoutFlag())
                            {
                                if (!unit.IsTurnFinished)
                                {
                                    FinishTurn(unit, false);
                                }
                                break;
                            }

                            iterationCount++;
                        }

                        if (iterationCount >= maxIterations && !unit.IsTurnFinished)
                        {
                            FinishTurn(unit, false);
                        }
                    }
                }
                else
                {
                    // NORMALNA ROZGRYWKA
                    AutoCombatManager.Instance.Act(unit);
                }

                // Czeka, aż jednostka zakończy ruch
                yield return new WaitUntil(() => MovementManager.Instance.IsMoving == false && (CombatManager.Instance == null || !CombatManager.Instance.IsAttackSequenceRunning));
                if (!ReinforcementLearningManager.Instance.IsLearning)
                {
                    yield return new WaitForSeconds(0.6f);
                }

                if (!unit.IsTurnFinished && (unit.CanDoAction || unit.CanMove))
                {
                    FinishTurn(unit, false);
                }
            }
        }

        NextRoundButton.gameObject.SetActive(true);
        _useFortunePointsButton.SetActive(true);

        //DO SZKOLENIA AI
        if (ReinforcementLearningManager.Instance.IsLearning)
        {
            // Sprawdź, czy któraś z drużyn już nie istnieje lub przekroczono limit tur
            bool battleEnded = !ReinforcementLearningManager.Instance.BothTeamsExist() || RoundNumber > 50;
            if (battleEnded)
            {
                // Wylicz zwycięzcę: true jeśli gracz wciąż ma jednostki, a enemy nie
                bool playerUnitsExist = UnitsManager.Instance != null
                    && UnitsManager.Instance.AllUnits != null
                    && UnitsManager.Instance.AllUnits.Any(u =>
                    {
                        if (u == null || !u.CompareTag("PlayerUnit")) return false;
                        Stats s = u.GetComponent<Stats>();
                        return s != null && s.TempHealth > 0;
                    });
                bool enemyUnitsExist = UnitsManager.Instance != null
                    && UnitsManager.Instance.AllUnits != null
                    && UnitsManager.Instance.AllUnits.Any(u =>
                    {
                        if (u == null || !u.CompareTag("EnemyUnit")) return false;
                        Stats s = u.GetComponent<Stats>();
                        return s != null && s.TempHealth > 0;
                    });
                bool didAIWin = !playerUnitsExist && enemyUnitsExist;

                // Terminalna nagroda dla wszystkich zapisanych akcji
                ReinforcementLearningManager.Instance.GiveTerminalRewardToAll(didAIWin);

                // Zaktualizuj licznik zwycięstw w UI i metryki epizodów
                ReinforcementLearningManager.Instance.UpdateTeamWins();
                ReinforcementLearningManager.Instance.NotifyEpisodeEnd(didAIWin);

                // Wczytaj ponownie scenę/stan rozpoczynający kolejne epizody
                if (!SaveAndLoadManager.Instance.IsLoading)
                {
                    SaveAndLoadManager.Instance.SetLoadingType("units");
                    SaveAndLoadManager.Instance.LoadGame("AIlearning");
                }
            }

            // Czekaj na zakończenie ładowania, potem leci dalej
            yield return new WaitUntil(() => SaveAndLoadManager.Instance.IsLoading == false);

            GridManager.Instance.CheckTileOccupancy();
            shouldStartNextRound = true;
        }

        _autoCombatCoroutine = null;

        if (shouldStartNextRound)
        {
            NextRound();
        }
    }

    private IEnumerator WaitForLearningStepResolution(Unit unit)
    {
        int timeoutFrames = ReinforcementLearningManager.Instance.GetLearningStepTimeoutFrames();
        int settleFrames = ReinforcementLearningManager.Instance.GetLearningSettleFrames();
        int waited = 0;

        while (MovementManager.Instance.IsMoving)
        {
            if (waited >= timeoutFrames)
            {
                ReinforcementLearningManager.Instance.ReportLearningStepTimeout(unit);
                yield break;
            }

            waited++;
            yield return null;
        }

        for (int i = 0; i < settleFrames; i++)
        {
            if (waited >= timeoutFrames)
            {
                ReinforcementLearningManager.Instance.ReportLearningStepTimeout(unit);
                yield break;
            }

            waited++;
            yield return null;
        }
    }    
    
    #region Units actions
    public bool DoAction(Unit unit, bool finishTurnIfNoActions = true)
    {
        if (unit == null || unit.IsTurnFinished || (IsResolvingTurnStart && finishTurnIfNoActions)) return false;
        //Zapobiega zużywaniu akcji przed rozpoczęciem bitwy
        if (RoundNumber == 0) return true;
        if (finishTurnIfNoActions && !CanUnitAct(unit)) return false;
        if (IsResolvingTurnStart && TurnStartUnit != unit) return false;

        if (unit.CanDoAction)
        {
            // Automatyczny zapis, aby możliwe było użycie punktów szczęścia lub zepsucia
            if (!GameManager.IsAutoCombatMode)
            {
                SaveAndLoadManager.Instance.SaveUnits(UnitsManager.Instance.AllUnits, "autosave");
                _isFortunePointSpent = false;
            }

            unit.CanDoAction = false;
            DisplayActionsLeft();

            Debug.Log($"<color=green>{unit.GetComponent<Stats>().Name} wykonał/a akcje. </color>");

            //Zresetowanie szarży lub biegu, jeśli były aktywne (po zużyciu jednej akcji szarża i bieg nie mogą być możliwe)
            //MovementManager.Instance.UpdateMovementRange(1);

            //W przypadku ręcznego zadawania obrażeń, czekamy na wpisanie wartości obrażeń przed zmianą jednostki (jednostka jest wtedy zmieniana w funkcji ExecuteAttack w CombatManager)
            if (finishTurnIfNoActions && !GameManager.IsAutoCombatMode && !CombatManager.Instance.IsManualPlayerAttack && !unit.CanMove && !unit.CanDoAction)
            {
                FinishTurn(unit);
            }

            return true;
        }
        else
        {
            Debug.Log("Ta jednostka nie może w tej rundzie wykonać więcej akcji.");
            return false;
        }
    }

    public void DisplayActionsLeft()
    {
        if (Unit.SelectedUnit == null)
        {
            _useFortunePointsButton.SetActive(false);
        }
        else
        {
            Unit unit = Unit.SelectedUnit.GetComponent<Unit>();

            SetCanDoActionToggle(unit.CanDoAction);
            MovementManager.Instance.SetCanMoveToggle(unit.CanMove);

            if (_isFortunePointSpent != true && !unit.CanDoAction && !GameManager.IsAutoCombatMode)
            {
                _useFortunePointsButton.SetActive(true);
            }
        }
    }

    public void UseFortunePoint()
    {
        if (Unit.SelectedUnit == null) return;

        Unit unit = Unit.SelectedUnit.GetComponent<Unit>();
        Stats stats = Unit.SelectedUnit.GetComponent<Stats>();

        if (unit.CanDoAction)
        {
            if (Unit.LastSelectedUnit == null) return;
            stats = Unit.LastSelectedUnit.GetComponent<Stats>();
        }

        Debug.Log($"{stats.Name} zużywa Punkt Losu. Wykonaj akcję ponownie.");
        stats.TempPL--;

        _isFortunePointSpent = true;

        SaveAndLoadManager.Instance.SaveFortunePoints("autosave", stats, stats.TempPL);
        SaveAndLoadManager.Instance.LoadGame("autosave");

        _useFortunePointsButton.SetActive(false);
    }

        //Zakończenie tury danej jednostki mimo tego, że ma jeszcze dostępne akcje
    public void FinishTurn()
    {
        if (Unit.SelectedUnit == null) return;
        FinishTurn(Unit.SelectedUnit.GetComponent<Unit>());
        TryAdvanceRoundAfterManualFinish();
    }

    public void FinishTurn(Unit unit, bool selectNextUnit = true)
    {
        if (unit == null || IsResolvingTurnStart) return;

        if (unit.IsTurnFinished)
        {
            if (selectNextUnit)
            {
                InitiativeQueueManager.Instance.SelectUnitByQueue();
            }

            TryAdvanceRoundIfComplete();
            return;
        }

        if (!CanUnitAct(unit)) return;
        bool hadUnusedActions = unit.CanMove || unit.CanDoAction;
        unit.IsTurnFinished = true;
        unit.CanDoAction = false;
        unit.CanMove = false;
        if (Unit.SelectedUnit == unit.gameObject) DisplayActionsLeft();

        // Bierze pod uwagę efekty ewentualnych stanów postaci
        StatesManager.Instance.UpdateUnitStates(unit);

        if (hadUnusedActions)
        {
            Debug.Log($"<color=green>{unit.Stats.Name} konczy swoja ture.</color>");
        }

        if (selectNextUnit)
        {
            InitiativeQueueManager.Instance.SelectUnitByQueue();
        }

        TryAdvanceRoundIfComplete();
    }
    #endregion

    public void ResetRoundsToStartUi()
    {
        RoundNumber = 0;
        _roundNumberDisplay.text = "Zaczynamy?";
        _playersRoundNumberDisplay.text = "";

        if (NextRoundButton != null && NextRoundButton.transform.childCount > 0)
        {
            NextRoundButton.transform.GetChild(0).GetComponent<TMP_Text>().text = "Start";
        }
    }

    public void ResetRoundFlowState(bool resetRoundCounter = false)
    {
        if (_turnStartCoroutine != null) StopCoroutine(_turnStartCoroutine);
        _turnStartCoroutine = null;
        TurnStartUnit = null;
        IsResolvingTurnStart = false;
        GameManager.Instance?.RefreshPauseState();
        StatesManager.Instance?.ResetTurnStartState();
        if (_autoCombatCoroutine != null)
        {
            StopCoroutine(_autoCombatCoroutine);
            _autoCombatCoroutine = null;
        }

        _isRoundAdvanceScheduled = false;
        _isRoundGenerationInProgress = false;

        if (MovementManager.Instance != null)
        {
            MovementManager.Instance.IsMoving = false;
        }

        if (CombatManager.Instance != null)
        {
            CombatManager.Instance.ResetAttackSequenceTracking();
        }

        if (DiceRollManager.Instance != null)
        {
            DiceRollManager.Instance.ForceCancelPendingRollInput();
        }

        if (InitiativeQueueManager.Instance != null)
        {
            InitiativeQueueManager.Instance.CancelPendingSelectUnitByQueue();
        }

        if (NextRoundButton != null)
        {
            NextRoundButton.gameObject.SetActive(true);
            NextRoundButton.interactable = true;
        }

        if (resetRoundCounter)
        {
            ResetRoundsToStartUi();
        }
    }

    public void ResetForNewDungeonCrawlerFloor()
    {
        ResetRoundFlowState(true);

        if (UnitsManager.Instance == null) return;

        for (int i = 0; i < UnitsManager.Instance.AllUnits.Count; i++)
        {
            Unit unit = UnitsManager.Instance.AllUnits[i];
            if (unit == null) continue;

            unit.IsTurnFinished = false;
            unit.TurnStartedInRound = 0;
            unit.HasUsedOpportunityAttack = false;
            unit.CanDoAction = true;

            Stats stats = unit.GetComponent<Stats>();
            unit.CanMove = !(unit.Entangled || unit.Grappled || (stats != null && stats.Sz == 0));
        }

        ClearSelectedUnitVisualState();

        InitiativeQueueManager.Instance?.UpdateInitiativeQueue();
    }


    private void ClearSelectedUnitVisualState()
    {
        if (Unit.SelectedUnit != null)
        {
            Unit selectedUnitComponent = Unit.SelectedUnit.GetComponent<Unit>();
            if (selectedUnitComponent != null)
            {
                selectedUnitComponent.IsSelected = false;
                selectedUnitComponent.ChangeUnitColor(selectedUnitComponent.gameObject);
                GridManager.Instance.ResetColorOfTilesInMovementRange();
            }
        }

        Unit.SelectedUnit = null;
        Unit.LastSelectedUnit = null;
    }
    private bool AreAllTurnsFinished()
    {
        if (InitiativeQueueManager.Instance == null) return false;

        foreach (var pair in InitiativeQueueManager.Instance.InitiativeQueue)
        {
            Unit unit = pair.Key;
            if (unit == null) continue;

            if (!unit.IsTurnFinished && (unit.CanDoAction || unit.CanMove))
            {
                return false;
            }
        }

        return true;
    }

    private bool IsBattleStillOngoing()
    {
        if (UnitsManager.Instance == null) return false;

        bool hasLivingPlayers = false;
        bool hasLivingEnemies = false;

        for (int i = 0; i < UnitsManager.Instance.AllUnits.Count; i++)
        {
            Unit unit = UnitsManager.Instance.AllUnits[i];
            if (unit == null || !unit.gameObject.activeInHierarchy) continue;

            Stats stats = unit.GetComponent<Stats>();
            if (stats == null || stats.TempHealth <= 0) continue;

            if (unit.CompareTag("PlayerUnit")) hasLivingPlayers = true;
            else if (unit.CompareTag("EnemyUnit")) hasLivingEnemies = true;

            if (hasLivingPlayers && hasLivingEnemies) return true;
        }

        return false;
    }

    public void TryAdvanceRoundIfComplete()
    {
        if (ReinforcementLearningManager.Instance == null || !ReinforcementLearningManager.Instance.IsLearning) return;
        if (_isRoundAdvanceScheduled || _isRoundGenerationInProgress) return;
        if (RoundNumber <= 0) return;
        if (SaveAndLoadManager.Instance != null && SaveAndLoadManager.Instance.IsLoading) return;
        if (!AreAllTurnsFinished()) return;
        if (!IsBattleStillOngoing()) return;

        _isRoundAdvanceScheduled = true;
        StartCoroutine(AdvanceRoundWhenReady());
    }


    private void TryAdvanceRoundAfterManualFinish()
    {
        if (_isRoundAdvanceScheduled || _isRoundGenerationInProgress) return;
        if (RoundNumber <= 0) return;
        if (SaveAndLoadManager.Instance != null && SaveAndLoadManager.Instance.IsLoading) return;
        if (ReinforcementLearningManager.Instance != null && ReinforcementLearningManager.Instance.IsLearning) return;
        if (!AreAllTurnsFinished()) return;
        if (!IsBattleStillOngoing()) return;

        _isRoundAdvanceScheduled = true;
        StartCoroutine(AdvanceRoundAfterManualFinishWhenReady());
    }
    private IEnumerator AdvanceRoundWhenReady()
    {
        while ((MovementManager.Instance != null && MovementManager.Instance.IsMoving)
            || IsResolvingTurnStart
            || (DiceRollManager.Instance != null && DiceRollManager.Instance.IsWaitingForRoll)
            || (CombatManager.Instance != null && CombatManager.Instance.IsAttackSequenceRunning)
            || (SaveAndLoadManager.Instance != null && SaveAndLoadManager.Instance.IsLoading))
        {
            yield return null;
        }

        _isRoundAdvanceScheduled = false;

        if ((ReinforcementLearningManager.Instance == null || !ReinforcementLearningManager.Instance.IsLearning) && GameManager.IsAutoCombatMode)
        {
            yield break;
        }

        if (!AreAllTurnsFinished() || !IsBattleStillOngoing())
        {
            yield break;
        }

        NextRound();
    }

    private IEnumerator AdvanceRoundAfterManualFinishWhenReady()
    {
        while ((MovementManager.Instance != null && MovementManager.Instance.IsMoving)
            || IsResolvingTurnStart
            || (DiceRollManager.Instance != null && DiceRollManager.Instance.IsWaitingForRoll)
            || (CombatManager.Instance != null && CombatManager.Instance.IsAttackSequenceRunning)
            || (SaveAndLoadManager.Instance != null && SaveAndLoadManager.Instance.IsLoading))
        {
            yield return null;
        }

        _isRoundAdvanceScheduled = false;

        if (ReinforcementLearningManager.Instance != null && ReinforcementLearningManager.Instance.IsLearning)
        {
            yield break;
        }

        if (!AreAllTurnsFinished() || !IsBattleStillOngoing())
        {
            yield break;
        }

        NextRound();
    }
    public void LoadRoundsManagerData(RoundsManagerData data)
    {
        RoundNumber = data.RoundNumber;
        if (RoundNumber > 0)
        {
            _roundNumberDisplay.text = "Runda: " + RoundNumber;
            _playersRoundNumberDisplay.text = "Runda: " + RoundNumber;
            NextRoundButton.transform.GetChild(0).GetComponent<TMP_Text>().text = "Następna runda";
        }
        else
        {
            _roundNumberDisplay.text = "Zaczynamy?";
            _playersRoundNumberDisplay.text = "";
            NextRoundButton.transform.GetChild(0).GetComponent<TMP_Text>().text = "Start";
        }
    }

    public void SetCanDoActionToggle(bool canDoAction)
    {
        _canDoActionToggle.isOn = canDoAction;
    }
    public void SetCanDoActionByToggle()
    {
        if (Unit.SelectedUnit == null) return;
        Unit.SelectedUnit.GetComponent<Unit>().CanDoAction = _canDoActionToggle.isOn;
    }

    private Unit GetNextUnitForAutoCombat()
    {
        foreach (var pair in InitiativeQueueManager.Instance.InitiativeQueue)
        {
            Unit candidate = pair.Key;
            if (candidate == null) continue;
            if (candidate.IsTurnFinished) continue;
            if (!candidate.CanDoAction && !candidate.CanMove) continue;

            return candidate;
        }

        return null;
    }
}
