using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
using Unity.VisualScripting;
using Unity.VisualScripting.Antlr3.Runtime.Misc;
using UnityEngine;

public class StatesManager : MonoBehaviour
{
    // Prywatne statyczne pole przechowujące instancję
    private static StatesManager instance;

    // Publiczny dostęp do instancji
    public static StatesManager Instance
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

    private GameObject _extinguishPanel;
    private bool _attemptExtinguish;

    public IEnumerator ResolveTurnStart(Unit unit)
    {
        if (unit == null) yield break;
        yield return StartCoroutine(Ablaze(unit));
        if (unit != null && Unit.SelectedUnit == unit.gameObject)
        {
            UnitsManager.Instance.UpdateUnitPanel(unit.gameObject);
            LoadUnitStates();
            RoundsManager.Instance.DisplayActionsLeft();
        }
    }

    public void ResetTurnStartState()
    {
        StopAllCoroutines();
        if (_extinguishPanel != null) _extinguishPanel.SetActive(false);
        _attemptExtinguish = false;
    }

    public static int GetPoisonLevel(Stats stats, int level)
    {
        if (stats.Undead || stats.IsUnaffectedBy("Poison")) return 0;
        level = Mathf.Max(0, level);
        return stats.HasResistance("Poison") ? Mathf.CeilToInt(level / 2f) : level;
    }

    public void UpdateUnitStates(Unit unit)
    {
        StartCoroutine(Bleeding(unit)); // Krwawienie
        StartCoroutine(Poison(unit)); // Zatrucie
    }

    private IEnumerator Ablaze(Unit unit)
    {
        if (unit == null || !unit.Ablaze) yield break;
        Stats stats = unit.GetComponent<Stats>();

        if (stats.IsUnaffectedBy("Fire"))
        {
            unit.Ablaze = false;
            Debug.Log($"<color=#FF7F50>{stats.Name} jest niewrażliwy/a na ogień – ignoruje stan Podpalenia.</color>");
            yield break;
        }

        // Obrażenia występują na początku tury, przed decyzją o gaszeniu, bez pancerza.
        int rolledDamage = UnityEngine.Random.Range(1, 7);
        int damage = stats.HasResistance("Fire") ? Mathf.CeilToInt(rolledDamage / 2f) : rolledDamage;
        stats.TempHealth -= damage;
        Debug.Log($"<color=#FF7F50>{stats.Name} traci {damage} Punkty/ów Zdrowia na początku tury w wyniku Podpalenia.</color>");
        unit.DisplayUnitHealthPoints();

        if (stats.TempHealth < 0 && GameManager.IsAutoKillMode)
        {
            UnitsManager.Instance.DestroyUnit(unit.gameObject);
            yield break;
        }
        if (unit.Unconscious || !unit.CanDoAction) yield break;
        while (DiceRollManager.Instance.IsWaitingForRoll) yield return null;
        if (unit == null) yield break;

        bool manualDecision = unit.CompareTag("PlayerUnit")
            && (!GameManager.IsAutoCombatMode || GameManager.IsStatsHidingMode)
            && (ReinforcementLearningManager.Instance == null || !ReinforcementLearningManager.Instance.IsLearning);

        _attemptExtinguish = !manualDecision;
        if (manualDecision)
        {
            if (_extinguishPanel == null)
            {
                _extinguishPanel = CombatManager.Instance.CreateExtinguishPanel(out var attemptButton, out var skipButton);
                if (_extinguishPanel == null) yield break;
                attemptButton.onClick.AddListener(() => ChooseExtinguish(true));
                skipButton.onClick.AddListener(() => ChooseExtinguish(false));
            }
            _extinguishPanel.GetComponentInChildren<TMP_Text>(true).text =
                $"{stats.Name} płonie.\nUgaszenie ognia wymaga zużycia akcji i zdania testu Atletyki (14+).";
            _extinguishPanel.transform.SetAsLastSibling();
            _extinguishPanel.SetActive(true);
            yield return new WaitUntil(() => unit == null || !_extinguishPanel.activeInHierarchy);
        }

        if (unit == null || !_attemptExtinguish || !unit.Ablaze || unit.Unconscious || !unit.CanDoAction) yield break;

        // Akcja jest zużywana również w razie niepowodzenia lub anulowania ręcznego rzutu.
        if (!RoundsManager.Instance.DoAction(unit, finishTurnIfNoActions: false)) yield break;
        int difficulty = 14;
        int[] test = null;

        if (!GameManager.IsAutoDiceRollingMode && stats.CompareTag("PlayerUnit"))
        {
            yield return StartCoroutine(DiceRollManager.Instance.WaitForRollValue(
                stats: stats,
                rollContext: "Atletykę na ugaszenie płomieni",
                attributeName: "Zw", 
                skillName: "Athletics",
                difficultyLevel: difficulty,
                callback: res => test = res
            ));
            if (test == null) yield break; // anulowano panel
        }
        else
        {
            test = DiceRollManager.Instance.TestSkill(
                stats: stats,
                rollContext: "Atletykę na ugaszenie płomieni",
                attributeName: "Zw",
                skillName: "Athletics",
                difficultyLevel: difficulty
            );
        }

        int finalScore = test[2];
        if (finalScore >= difficulty)
        {
            unit.Ablaze = false;
            Debug.Log($"<color=#FF7F50>{stats.Name} zdaje test Atletyki i gasi płomienie.</color>");
            yield break;
        }

        Debug.Log($"<color=#FF7F50>{stats.Name} nie udaje się ugasić płomieni. Podpalenie trwa dalej.</color>");
    }

    private void ChooseExtinguish(bool attempt)
    {
        _attemptExtinguish = attempt;
        _extinguishPanel.SetActive(false);
    }

    private IEnumerator Bleeding(Unit unit)
    {
        if (unit.Bleeding <= 0) yield break;
        Stats stats = unit.GetComponent<Stats>();

        if (stats.TempHealth > 0)
        {
            stats.TempHealth -= unit.Bleeding;
            Debug.Log($"<color=#FF7F50>{stats.Name} traci {unit.Bleeding} Punkty/ów Zdrowia w wyniku krwawienia.</color>");
            unit.DisplayUnitHealthPoints();
        }
        else if (!unit.Unconscious)
        {
            unit.Unconscious = true; // Utrata Przytomności
            Debug.Log($"<color=#FF7F50>{stats.Name} traci przytomność w wyniku krwawienia.</color>");
        }
        else
        {
            int difficulty = GetDifficulty(unit.Bleeding);
            int[] testResult = null;
            if (!GameManager.IsAutoDiceRollingMode && stats.CompareTag("PlayerUnit"))
            {
                // Rzut manualny
                yield return StartCoroutine(DiceRollManager.Instance.WaitForRollValue(
                    stats,
                    "śmierć w wyniku krwawienia",
                    "K",
                    "Endurance",
                    difficultyLevel: difficulty,
                    callback: result => testResult = result
                ));
                if (testResult == null) yield break;
            }
            else
            {
                // Rzut automatyczny
                testResult = DiceRollManager.Instance.TestSkill(
                    stats,
                    "śmierć w wyniku krwawienia",
                    "K",
                    "Endurance",
                    difficultyLevel: difficulty
                );
            }

            int finalScore = testResult[2];

            if (finalScore < difficulty)
            {
                Debug.Log($"<color=#FF7F50>{stats.Name} nie zdał/a testu Odporności i umiera w wyniku krwawienia.</color>");
                if (GameManager.IsAutoKillMode)
                    UnitsManager.Instance.DestroyUnit(unit.gameObject);
            }
            else
            {
                if (DiceRollManager.Instance.IsDoubleDigit(testResult[0], testResult[1]))
                {
                    Debug.Log($"<color=#FF7F50>{stats.Name} wyrzucił/a <color=green>SZCZĘŚCIE</color>! Krwawienie zostaje całkowicie usunięte.</color>");
                    unit.Bleeding = 0;
                }
                else
                {
                    Debug.Log($"<color=#FF7F50>{stats.Name} zdał/a test Odporności. Wciąż żyje, ale pozostaje nieprzytomny/a.</color>");
                }
            }
        }
    }

    private IEnumerator Poison(Unit unit)
    {
        if (unit.Poison <= 0) yield break;

        Stats stats = unit.GetComponent<Stats>();

        if (stats.IsUnaffectedBy("Poison") || stats.Undead)
        {
            unit.Poison = 0;
            Debug.Log($"<color=#FF7F50>{stats.Name} jest niewrażliwy/a na truciznę – ignoruje stan Zatrucia.</color>");
            yield break;
        }

        // Rzut na Odporność zależny od poziomu zatrucia
        int[] test = null;
        int difficulty = GetDifficulty(unit.Poison);

        if (!GameManager.IsAutoDiceRollingMode && stats.CompareTag("PlayerUnit"))
        {
            yield return StartCoroutine(DiceRollManager.Instance.WaitForRollValue(
                stats,
                "Odporność na zatrucie",
                attributeName: "K",
                skillName: "Endurance",
                difficultyLevel: difficulty,
                callback: res => test = res
            ));
            if (test == null) yield break;
        }
        else
        {
            // Auto – TestSkill sam wylosuje kości
            test = DiceRollManager.Instance.TestSkill(
                stats: stats,
                rollContext: "Odporność na zatrucie",
                attributeName: "K",
                skillName: "Endurance",
                difficultyLevel: difficulty
            );
        }

        int roll1 = test[0];
        int roll2 = test[1];
        int finalScore = test[2];

        // SZCZĘŚCIE i PECH
        if (DiceRollManager.Instance.IsDoubleDigit(roll1, roll2))
        {
            if (finalScore >= difficulty)
            {
                Debug.Log($"<color=#FF7F50>{stats.Name} wyrzucił/a <color=green>SZCZĘŚCIE</color>! Zatrucie zostaje całkowicie usunięte.</color>");
                unit.Poison = 0;
            }
            else
            {
                Debug.Log($"<color=#FF7F50>{stats.Name} wyrzucił/a <color=red>PECHA</color>! Poziom zatrucia wzrasta o 1.</color>");
                unit.Poison++;
            }

            yield break;
        }

        // Jeśli postać jest NIEPRZYTOMNA – nieudany test = śmierć
        if (unit.Unconscious)
        {
            if (finalScore < difficulty)
            {
                Debug.Log($"<color=#FF7F50>{stats.Name} nie zdaje testu Odporności i <color=red>umiera</color> z powodu zatrucia.</color>");
                if (GameManager.IsAutoKillMode)
                    UnitsManager.Instance.DestroyUnit(unit.gameObject);
            }
            else
            {
                Debug.Log($"<color=#FF7F50>{stats.Name} zdaje test Odporności. Wciąż żyje, ale pozostaje nieprzytomny/a.</color>");
            }
            yield break;
        }

        // Postać PRZYTOMNA – porażka = -1 HP
        if (finalScore < difficulty)
        {
            // Jeśli spadło do 0 lub poniżej → Utrata przytomności
            if (stats.TempHealth <= 0 && !unit.Unconscious)
            {
                unit.Unconscious = true;
                Debug.Log($"<color=#FF7F50>{stats.Name} traci przytomność w wyniku zatrucia.</color>");
            }
            else
            {
                // Utrata 1 HP (nie cała wartość Poison!)
                stats.TempHealth -= 1;
                Debug.Log($"<color=#FF7F50>{stats.Name} nie zdaje testu Odporności i traci 1 Punkt Zdrowia z powodu zatrucia.</color>");
                unit.DisplayUnitHealthPoints();
            }
        }
        else
        {
            Debug.Log($"<color=#FF7F50>{stats.Name} zdaje test Odporności i nie traci Punktu Zdrowia w tej turze.</color>");
        }
    }

    public void Entangled(Unit unit, bool value)
    {
        unit.Entangled = value;
        unit.CanMove = !unit.Entangled;

        if(Unit.SelectedUnit == unit.gameObject)
        {
            MovementManager.Instance.SetCanMoveToggle(unit.CanMove);
            GridManager.Instance.HighlightTilesInMovementRange(unit.GetComponent<Stats>());
        }
    }

    public void Prone(Unit unit, bool value = true)
    {
        Stats stats = unit.GetComponent<Stats>();

        unit.Prone = value;
        if (unit.Prone)
        {
            StartCoroutine(MovementManager.Instance.UpdateMovementRange(1, null, false));
            Debug.Log($"<color=#FF7F50>{stats.Name} zostaje powalony.</color>");
        }
        else
        {
            unit.Prone = false;
            unit.CanMove = false;
            stats.TempSz = stats.Sz;
            MovementManager.Instance.SetCanMoveToggle(false);
            Debug.Log($"<color=green>{unit.GetComponent<Stats>().Name} podnosi się z ziemi.</color>");
        }
    }

    public void Unconscious(Unit unit, bool value = true)
    {
        Stats stats = unit.GetComponent<Stats>();

        unit.Unconscious = value;
        unit.CanMove = !value;
        unit.CanDoAction = !value;

        if(Unit.SelectedUnit == unit.gameObject)
        {
            MovementManager.Instance.SetCanMoveToggle(unit.CanMove);
            RoundsManager.Instance.SetCanDoActionToggle(unit.CanDoAction);
        }

        if (unit.Unconscious)
        {
            Debug.Log($"<color=#FF7F50>{stats.Name} traci przytomność.</color>");
        }
        else
        {
            Debug.Log($"<color=#FF7F50>{stats.Name} odzyskuje przytomność.</color>");
        }
    }

    public IEnumerator Recover(Unit unit)
    {
        Stats stats = unit.GetComponent<Stats>();

        // 1) Próba odzyskania przytomności: wymagający (14+) test Kondycji
        int difficulty = 14;
        int[] test = null;

        if (!GameManager.IsAutoDiceRollingMode && stats.CompareTag("PlayerUnit"))
        {
            yield return StartCoroutine(DiceRollManager.Instance.WaitForRollValue(
                stats: stats,
                rollContext: "Kondycję na odzyskanie przytomności",
                attributeName: "K",
                difficultyLevel: difficulty,
                callback: res => test = res
            ));
            if (test == null) yield break; // anulowano panel
        }
        else
        {
            test = DiceRollManager.Instance.TestSkill(
                stats: stats,
                rollContext: "Kondycję na odzyskanie przytomności",
                attributeName: "K",
                difficultyLevel: difficulty
            );
        }

        int finalScore = test[2];
        if (finalScore >= difficulty)
        {
            unit.Unconscious = false;
            Debug.Log($"<color=#FF7F50>{stats.Name} odzyskuje przytomność.</color>");
            yield break;
        }
        else
        {
            unit.CanDoAction = false;
            unit.CanMove = false;
        }
    }

    private int GetDifficulty(int stateLevel)
    {
        return stateLevel switch
        {
            1 => 12, // Przeciętny
            2 => 14, // Wymagający
            3 => 16, // Trudny
            4 => 18, // Bardzo trudny
            5 => 20, // Ekstremalny
            _ => 20
        };
    }

    public void SetUnitState(GameObject textInput)
    {
        if (Unit.SelectedUnit == null) return;

        Unit unit = Unit.SelectedUnit.GetComponent<Unit>();

        // Pobiera nazwę cechy z nazwy obiektu InputField (bez "_input")
        string stateName = textInput.name.Replace("_input", "");
        if (stateName == "Fatigued") stateName = "Fatiqued";

        // Szukamy pola
        FieldInfo field = unit.GetType().GetField(stateName);

        // Jeżeli pole nie istnieje, kończymy metodę
        if (field == null)
        {
            Debug.Log($"Nie znaleziono pola '{stateName}'.");
            return;
        }

        // Zależnie od typu pola...
        if (field.FieldType == typeof(int) && textInput.GetComponent<UnityEngine.UI.Slider>() == null)
        {
            // int przez InputField
            int value = int.TryParse(textInput.GetComponent<TMP_InputField>().text, out int inputValue) ? inputValue : 0;
            if (field.Name == "Fatiqued") value = Mathf.Clamp(value, 0, 5);
            if (field.Name == "Poison") value = GetPoisonLevel(unit.GetComponent<Stats>(), value);
            field.SetValue(unit, value);
            textInput.GetComponent<TMP_InputField>().SetTextWithoutNotify(value.ToString());
        }
        else if (field.FieldType == typeof(bool))
        {
            bool boolValue = textInput.GetComponent<UnityEngine.UI.Toggle>().isOn;

            if (field.Name == "Unconscious")
            {
                Unconscious(unit, boolValue);
            }
            else if (field.Name == "Prone")
            {
                Prone(unit, boolValue);
            }
            else if (field.Name == "Entangled")
            {
                Entangled(unit, boolValue);

                // Przy usuwaniu Unieruchomienia związanego z pochwyceniem usuwamy również stan Grappled
                if (boolValue == false) unit.Grappled = boolValue;
            }
            else
            {
                field.SetValue(unit, boolValue);
            }
        }
        else
        {
            Debug.Log($"Nie udało się zmienić wartości stanu '{stateName}'.");
        }
    }

    public void LoadUnitStates()
    {
        if (Unit.SelectedUnit == null) return;
        Unit unit = Unit.SelectedUnit.GetComponent<Unit>();

        GameObject[] statesInputFields = GameObject.FindGameObjectsWithTag("State");

        foreach (var inputField in statesInputFields)
        {
            // Wyciągamy nazwę stanu z nazwy obiektu InputField
            string stateName = inputField.name.Replace("_input", "");
            if (stateName == "Fatigued") stateName = "Fatiqued";

            FieldInfo field = unit.GetType().GetField(stateName);
            if (field == null)
                continue;

            if (field.FieldType == typeof(int))
            {
                int value = (int)field.GetValue(unit);

                if (inputField.GetComponent<TMPro.TMP_InputField>() != null)
                {
                    inputField.GetComponent<TMPro.TMP_InputField>().SetTextWithoutNotify(value.ToString());
                }
            }
            else if (field.FieldType == typeof(bool))
            {
                bool value = (bool)field.GetValue(unit);
                inputField.GetComponent<UnityEngine.UI.Toggle>().isOn = value;
            }
        }
    }

    #region Fear
    public IEnumerator FearTest(Unit target)
    {
        if (target == null) yield break;
        if (target.GetComponent<Stats>().Undead || target.GetComponent<Stats>().Unmeaning)
        {
            target.Scared = false;
            yield break;
        }

        // znajdź najstraszniejszego przeciwnika (inny tag, aktywny, Scary > 0)
        string otherTag = target.CompareTag("PlayerUnit") ? "EnemyUnit" : "PlayerUnit";

        int difficulty = 0;
        Stats targetStats = target.GetComponent<Stats>();
        Stats mostScaryEnemy = null;

        // Szukamy najstraszniejszego przeciwnika w kolejce inicjatywy
        foreach (var pair in InitiativeQueueManager.Instance.InitiativeQueue)
        {
            Unit unit = pair.Key;
            if (unit == null) continue;

            if (unit.CompareTag(otherTag))
            {
                Stats enemyStats = unit.GetComponent<Stats>();
                if (enemyStats != null && enemyStats.Scary > difficulty)
                {
                    difficulty = enemyStats.Scary;
                    mostScaryEnemy = enemyStats;
                }
            }
        }

        if (mostScaryEnemy == null)
        {
            // Brak źródeł strachu → zdejmij Scared jeśli był
            if (target.Scared)
            {
                target.Scared = false;
                Debug.Log($"<color=#FF7F50>{targetStats.Name} przestaje się bać (brak źródeł strachu).</color>");
            }
            yield break;
        }

        int[] test = null;

        if (!GameManager.IsAutoDiceRollingMode && target.CompareTag("PlayerUnit"))
        {
            yield return StartCoroutine(DiceRollManager.Instance.WaitForRollValue(
                stats: targetStats,
                rollContext: $"Opanowanie strachu przed {mostScaryEnemy.Name}",
                attributeName: "SW",
                skillName: "Cool",
                difficultyLevel: difficulty,
                callback: res => test = res
            ));
            if (test == null) yield break; // anulowano panel
        }
        else
        {
            test = DiceRollManager.Instance.TestSkill(
                stats: targetStats,
                rollContext: $"Opanowanie strachu przed {mostScaryEnemy.Name}",
                attributeName: "SW",
                skillName: "Cool",
                difficultyLevel: difficulty
            );
        }

        int finalScore = test[2];

        if (finalScore < difficulty)
        {
            // porażka → nadaj/utrzymaj Scared z informacją o sile źródła
            target.Scared = true;
            Debug.Log($"<color=#FF7F50>{targetStats.Name} nie udało się opanować strachu przed {mostScaryEnemy.Name}.</color>");
        }
        else
        {
            target.Scared = false;
            Debug.Log($"<color=#FF7F50>{targetStats.Name} opanowuje strach przed {mostScaryEnemy.Name}.</color>");
        }
    }
    #endregion

    #region Stink trait
    public IEnumerator HandleStink(Unit unit)
    {
        if (unit == null) yield break;

        Stats stats = unit.GetComponent<Stats>();
        if (stats == null || stats.Stink) yield break;
        Stats stinkSource = null;

        Vector2 center = unit.transform.position;

        Vector2[] positions = {
            center,
            center + Vector2.right,
            center + Vector2.left,
            center + Vector2.up,
            center + Vector2.down,
            center + new Vector2(1, 1),
            center + new Vector2(-1, -1),
            center + new Vector2(-1, 1),
            center + new Vector2(1, -1)
        };

        foreach (var pos in positions)
        {
            Collider2D collider = Physics2D.OverlapPoint(pos);
            if (collider == null || collider.GetComponent<Stats>() == null) continue;

            if (!collider.CompareTag(unit.tag) && collider.GetComponent<Stats>().Stink)
            {
                stinkSource = collider.GetComponent<Stats>();
                break;
            }
        }

        if (stinkSource == null) yield break; // nic nie śmierdzi w zasięgu

        // Test Odporności (10+)
        int difficulty = 10;
        int[] test = null;

        if (!GameManager.IsAutoDiceRollingMode && stats.CompareTag("PlayerUnit"))
        {
            yield return StartCoroutine(DiceRollManager.Instance.WaitForRollValue(
                stats: stats,
                rollContext: "Odporność na smród",
                attributeName: "K",
                skillName: "Endurance",
                difficultyLevel: difficulty,
                callback: res => test = res
            ));
            if (test == null) yield break; // anulowano panel
        }
        else
        {
            test = DiceRollManager.Instance.TestSkill(
                stats: stats,
                rollContext: "Odporność na smród",
                attributeName: "K",
                skillName: "Endurance",
                difficultyLevel: difficulty
            );
        }

        int finalScore = test[2];
        if (finalScore < difficulty)
        {
            unit.CanDoAction = false;
            RoundsManager.Instance.SetCanDoActionToggle(false);
            Debug.Log($"<color=#FF7F50>{stats.Name} nie zdaje testu Odporności i traci swoją akcję w związku ze smrodem {stinkSource.Name}.</color>");
        }
        else
        {
            Debug.Log($"<color=#FF7F50>{stats.Name} zdaje test Odporności na smród {stinkSource.Name}.</color>");
        }
    }
    #endregion

}
