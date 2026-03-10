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

    private void Start()
    {
        RoundNumber = 0;
        _roundNumberDisplay.text = "Zaczynamy?";
        _playersRoundNumberDisplay.text = "";

        NextRoundButton.transform.GetChild(0).GetComponent<TMP_Text>().text = "Start";

        _useFortunePointsButton.SetActive(false);
    }

    public void NextRound()
    {
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

        //Wykonuje automatyczną akcję za każdą jednostkę
        if (GameManager.IsAutoCombatMode)
        {
            StartCoroutine(AutoCombat());
        }
    }

    IEnumerator AutoCombat()
    {
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
                yield return new WaitUntil(() => MovementManager.Instance.IsMoving == false);
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
                bool playerUnitsExist = UnitsManager.Instance.AllUnits.Any(u =>
                    u != null && u.CompareTag("PlayerUnit") && u.GetComponent<Stats>().TempHealth > 0);
                bool enemyUnitsExist = UnitsManager.Instance.AllUnits.Any(u =>
                    u != null && u.CompareTag("EnemyUnit") && u.GetComponent<Stats>().TempHealth > 0);
                bool didAIWin = !playerUnitsExist && enemyUnitsExist;

                // Terminalna nagroda dla wszystkich zapisanych akcji
                ReinforcementLearningManager.Instance.GiveTerminalRewardToAll(didAIWin);

                // Zaktualizuj licznik zwycięstw w UI i metryki epizodów
                ReinforcementLearningManager.Instance.UpdateTeamWins();
                ReinforcementLearningManager.Instance.NotifyEpisodeEnd(didAIWin);

                // Wczytaj ponownie scenę/stan rozpoczynający kolejne epizody
                SaveAndLoadManager.Instance.SetLoadingType("units");
                SaveAndLoadManager.Instance.LoadGame("AIlearning");

                for (int i = 0; i < UnitsManager.Instance.AllUnits.Count; i++)
                {
                    if (UnitsManager.Instance.AllUnits[i] == null || !InitiativeQueueManager.Instance.InitiativeQueue.ContainsKey(UnitsManager.Instance.AllUnits[i])) continue;
                    UnitsManager.Instance.AllUnits[i].GetComponent<Stats>().Overall = UnitsManager.Instance.AllUnits[i].GetComponent<Stats>().CalculateOverall();
                }
            }

            // Czekaj na zakończenie ładowania, potem leci dalej
            yield return new WaitUntil(() => SaveAndLoadManager.Instance.IsLoading == false);

            GridManager.Instance.CheckTileOccupancy();
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
    public void DoAction(Unit unit)
    {
        //Zapobiega zużywaniu akcji przed rozpoczęciem bitwy
        if (RoundNumber == 0) return;

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
            if (!CombatManager.Instance.IsManualPlayerAttack && !unit.CanMove && !unit.CanDoAction)
            {
                FinishTurn();
            }

            return;
        }
        else
        {
            Debug.Log("Ta jednostka nie może w tej rundzie wykonać więcej akcji.");
            return;
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
    }

    public void FinishTurn(Unit unit, bool selectNextUnit = true)
    {
        if (unit == null) return;

        unit.IsTurnFinished = true;

        // Bierze pod uwagę efekty ewentualnych stanów postaci
        StatesManager.Instance.UpdateUnitStates(unit);

        if (unit.CanMove || unit.CanDoAction)
        {
            Debug.Log($"<color=green>{unit.Stats.Name} konczy swoja ture.</color>");
        }

        if (selectNextUnit)
        {
            InitiativeQueueManager.Instance.SelectUnitByQueue();
        }
    }
    #endregion

    public void LoadRoundsManagerData(RoundsManagerData data)
    {
        RoundNumber = data.RoundNumber;
        if (RoundNumber > 0)
        {
            _roundNumberDisplay.text = "Runda: " + RoundNumber;
            NextRoundButton.transform.GetChild(0).GetComponent<TMP_Text>().text = "Następna runda";
        }
        else
        {
            _roundNumberDisplay.text = "Zaczynamy?";
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

