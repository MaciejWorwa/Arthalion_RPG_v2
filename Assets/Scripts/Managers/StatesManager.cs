using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TMPro;
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

    public void UpdateUnitStates(Unit unit)
    {
        StartCoroutine(Ablaze(unit)); // Podpalenie
        StartCoroutine(Bleeding(unit)); // Krwawienie
        StartCoroutine(Poison(unit)); // Zatrucie
    }

    private IEnumerator Ablaze(Unit unit)
    {
        if (!unit.Ablaze) yield break; // jeśli nie płonie, nic nie robimy
        Stats stats = unit.GetComponent<Stats>();

        if (stats.Resistance != null && stats.Resistance.Contains("Fire"))
        {
            unit.Ablaze = false; // od razu gasimy, bo ogień nie działa
            Debug.Log($"<color=#FF7F50>{stats.Name} jest odporny na ogień – ignoruje stan Podpalenia.</color>");
            yield break;
        }

        // 1) Próba ugaszenia: wymagający (14+) test Atletyki (*Zwinność*)
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

        // 2) Porażka testu → obrażenia k6 ignorujące pancerz (efekt Podpalenia)
        int damage = UnityEngine.Random.Range(1, 7);
        stats.TempHealth -= damage;
        Debug.Log($"<color=#FF7F50>{stats.Name} nie udaje się ugasić ognia i traci {damage} Punkty/ów Zdrowia w wyniku podpalenia.</color>");
        unit.DisplayUnitHealthPoints();
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

        if ((stats.Resistance != null && stats.Resistance.Contains("Poison")) || stats.Undead)
        {
            unit.Poison = 0;
            Debug.Log($"<color=#FF7F50>{stats.Name} jest odporny na truciznę – ignoruje stan Zatrucia.</color>");
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

    public void Entangled(Unit unit, int value = 0)
    {
        if (value > 0)
        {
            unit.Entangled = true;
        }

        if (unit.Entangled) unit.CanMove = false;
    }


    public void Prone(Unit unit, bool value = true)
    {
        Stats stats = unit.GetComponent<Stats>();

        unit.Prone = value;
        if (unit.Unconscious)
        {
            Debug.Log($"<color=#FF7F50>{stats.Name} zostaje powalony.</color>");
        }
        else
        {
            unit.Prone = false;
            unit.CanMove = false;
            MovementManager.Instance.SetCanMoveToggle(false);
            Debug.Log($"<color=green>{unit.GetComponent<Stats>().Name} podnosi się z ziemi.</color>");
        }
    }

    public void Unconscious(Unit unit, bool value = true)
    {
        Stats stats = unit.GetComponent<Stats>();

        unit.Unconscious = value;
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

            field.SetValue(unit, value);
        }
        else if (field.FieldType == typeof(bool))
        {
            bool boolValue = textInput.GetComponent<UnityEngine.UI.Toggle>().isOn;
            field.SetValue(unit, boolValue);

            // Przy usuwaniu Unieruchomienia związanego z pochwyceniem usuwamy również stan Grappled
            if(field.Name == "Entangle" && boolValue == false) unit.Grappled = boolValue;
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

            FieldInfo field = unit.GetType().GetField(stateName);
            if (field == null)
                continue;

            if (field.FieldType == typeof(int))
            {
                int value = (int)field.GetValue(unit);

                if (inputField.GetComponent<TMPro.TMP_InputField>() != null)
                {
                    inputField.GetComponent<TMPro.TMP_InputField>().text = value.ToString();
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
        if (target == null || target.GetComponent<Stats>().Undead || target.GetComponent<Stats>().Unmeaning) yield break;

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
