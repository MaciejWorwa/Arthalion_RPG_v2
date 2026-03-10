using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

public class RoundsManager : MonoBehaviour
{
    // Prywatne statyczne pole przechowuj?ce instancj?
    private static RoundsManager instance;

    // Publiczny dost?p do instancji
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
            // Je?li instancja ju? istnieje, a pr?bujemy utworzy? kolejn?, niszczymy nadmiarow?
            Destroy(gameObject);
        }
    }
    public static int RoundNumber;
    [SerializeField] private TMP_Text _roundNumberDisplay;
    [SerializeField] private TMP_Text _playersRoundNumberDisplay;
    public UnityEngine.UI.Button NextRoundButton;
    [SerializeField] private UnityEngine.UI.Toggle _canDoActionToggle;
    [SerializeField] private GameObject _useFortunePointsButton;
    private bool _isFortunePointSpent; //informacja o tym, ?e punkt szcz?cia zosta? zu?yty, aby nie mo?na by?o ponownie go u?y? do wczytania tego samego autozapisu

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
            NextRoundButton.transform.GetChild(0).GetComponent<TMP_Text>().text = "Nastepna runda";
        }

        Debug.Log($"<color=#4dd2ff>------------------------------------------------------------------------------------ RUNDA {RoundNumber} ------------------------------------------------------------------------------------</color>");

        // Sprawdzenie istnieje jakakolwiek jednostka ze Smrodem
        bool stinkUnitExist = UnitsManager.Instance.AllUnits.Any(u =>
        {
            var s = u.GetComponent<Stats>();
            return s != null && s.Stink;
        });

        //Resetuje ilo?? dost?pnych akcji dla wszystkich jednostek
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

            //Aktualizuje osi?gni?cia
            stats.RoundsPlayed++;
        }

        // Wykonuje testy grozy i strachu, je?li na polu bitwy s? jednostki straszne
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

            // je?li nikt nie jest straszny ? pomija dalszy kod
            if (maxScaryEnemies > 0 || maxScaryPlayers > 0)
            {
                foreach (var pair in queue)
                {
                    var unit = pair.Key;
                    if (unit == null) continue;

                    // wybierz odpowiedni max poziom Strachu przeciwnika
                    int requiredLevel = unit.CompareTag("PlayerUnit") ? maxScaryEnemies : maxScaryPlayers;
                    if (requiredLevel <= unit.FearTestedLevel) continue;           // test ju? si? odby? na tym lub wy?szym poziomie Starchu

                    unit.FearTestedLevel = requiredLevel;
                    StartCoroutine(StatesManager.Instance.FearTest(unit));         // korutyna sama zrobi test na aktualne warunki
                }
            }
        }

        InitiativeQueueManager.Instance.UpdateInitiativeQueue();

        //Od?wie?a panel jednostki, aby zaktualizowac ewentualn? informacj? o d?ugo?ci trwania stanu (np. og?uszenia) wybranej jednostki
        if (Unit.SelectedUnit != null)
        {
            UnitsManager.Instance.UpdateUnitPanel(Unit.SelectedUnit);
        }

        //Wybiera jednostk? zgodnie z kolejk? inicjatywy, je?li ten tryb jest w??czony
        if (GameManager.IsAutoSelectUnitMode && InitiativeQueueManager.Instance.ActiveUnit != null)
        {
            InitiativeQueueManager.Instance.SelectUnitByQueue();
        }

        //Wykonuje automatyczn? akcj? za ka?d? jednostk?
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

            // Wyb?r jednostki bez asynchronicznego wy?cigu z SelectUnitByQueue.
            if (Unit.SelectedUnit != unit.gameObject)
            {
                unit.SelectUnit();
            }

            if (!ReinforcementLearningManager.Instance.IsLearning)
            {
                yield return new WaitForSeconds(0.05f);
            }

            // Je?eli jednostka to PlayerUnit i gramy w trybie ukrywania statystyk wrog?w
            if (unit.CompareTag("PlayerUnit") && GameManager.IsStatsHidingMode)
            {
                // Czeka a? jednostka zako?czy swoj? tur?
                yield return new WaitUntil(() => (unit.CanDoAction == false && unit.CanMove == false) || unit.IsTurnFinished);
                if (!ReinforcementLearningManager.Instance.IsLearning)
                {
                    yield return new WaitForSeconds(0.6f);
                }
            }
            else // Jednostki wrog?w lub wszystkie jednostki, je?li nie ukrywamy ich statystyk
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

                // Czeka, a? jednostka zako?czy ruch
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
            // Sprawd?, czy kt?ra? z dru?yn ju? nie istnieje lub przekroczono limit tur
            bool battleEnded = !ReinforcementLearningManager.Instance.BothTeamsExist() || RoundNumber > 50;
            if (battleEnded)
            {
                // Wylicz zwyci?zc?: true je?li gracz wci?? ma jednostki, a enemy nie
                bool playerUnitsExist = UnitsManager.Instance.AllUnits.Any(u =>
                    u != null && u.CompareTag("PlayerUnit") && u.GetComponent<Stats>().TempHealth > 0);
                bool enemyUnitsExist = UnitsManager.Instance.AllUnits.Any(u =>
                    u != null && u.CompareTag("EnemyUnit") && u.GetComponent<Stats>().TempHealth > 0);
                bool didAIWin = !playerUnitsExist && enemyUnitsExist;

                // Terminalna nagroda dla wszystkich zapisanych akcji
                ReinforcementLearningManager.Instance.GiveTerminalRewardToAll(didAIWin);

                // Zaktualizuj licznik zwyci?stw w UI i metryki epizod?w
                ReinforcementLearningManager.Instance.UpdateTeamWins();
                ReinforcementLearningManager.Instance.NotifyEpisodeEnd(didAIWin);

                // Wczytaj ponownie scen?/stan rozpoczynaj?cy kolejne epizody
                SaveAndLoadManager.Instance.SetLoadingType("units");
                SaveAndLoadManager.Instance.LoadGame("AIlearning");

                for (int i = 0; i < UnitsManager.Instance.AllUnits.Count; i++)
                {
                    if (UnitsManager.Instance.AllUnits[i] == null || !InitiativeQueueManager.Instance.InitiativeQueue.ContainsKey(UnitsManager.Instance.AllUnits[i])) continue;
                    UnitsManager.Instance.AllUnits[i].GetComponent<Stats>().Overall = UnitsManager.Instance.AllUnits[i].GetComponent<Stats>().CalculateOverall();
                }
            }

            // Czekaj na zako?czenie ?adowania, potem leci dalej
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
        //Zapobiega zu?ywaniu akcji przed rozpocz?ciem bitwy
        if (RoundNumber == 0) return;

        if (unit.CanDoAction)
        {
            // Automatyczny zapis, aby mo?liwe by?o u?ycie punkt?w szcz?cia lub zepsucia
            if (!GameManager.IsAutoCombatMode)
            {
                SaveAndLoadManager.Instance.SaveUnits(UnitsManager.Instance.AllUnits, "autosave");
                _isFortunePointSpent = false;
            }

            unit.CanDoAction = false;
            DisplayActionsLeft();

            Debug.Log($"<color=green>{unit.GetComponent<Stats>().Name} wykonal/a akcje. </color>");

            //Zresetowanie szar?y lub biegu, je?li by?y aktywne (po zu?yciu jednej akcji szar?a i bieg nie mog? by? mo?liwe)
            //MovementManager.Instance.UpdateMovementRange(1);

            //W przypadku r?cznego zadawania obra?e?, czekamy na wpisanie warto?ci obra?e? przed zmian? jednostki (jednostka jest wtedy zmieniana w funkcji ExecuteAttack w CombatManager)
            if (!CombatManager.Instance.IsManualPlayerAttack && !unit.CanMove && !unit.CanDoAction)
            {
                FinishTurn();
            }

            return;
        }
        else
        {
            Debug.Log("Ta jednostka nie mo?e w tej rundzie wykona? wi?cej akcji.");
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

        Debug.Log($"{stats.Name} zuzywa Punkt Losu. Wykonaj akcje ponownie.");
        stats.TempPL--;

        _isFortunePointSpent = true;

        SaveAndLoadManager.Instance.SaveFortunePoints("autosave", stats, stats.TempPL);
        SaveAndLoadManager.Instance.LoadGame("autosave");

        _useFortunePointsButton.SetActive(false);
    }

        //Zako?czenie tury danej jednostki mimo tego, ?e ma jeszcze dost?pne akcje
    public void FinishTurn()
    {
        if (Unit.SelectedUnit == null) return;
        FinishTurn(Unit.SelectedUnit.GetComponent<Unit>());
    }

    public void FinishTurn(Unit unit, bool selectNextUnit = true)
    {
        if (unit == null) return;

        unit.IsTurnFinished = true;

        // Bierze pod uwag? efekty ewentualnych stan?w postaci
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
            NextRoundButton.transform.GetChild(0).GetComponent<TMP_Text>().text = "Nastepna runda";
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
