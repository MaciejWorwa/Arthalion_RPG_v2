using System;
using System.Collections;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

public class DiceRollManager : MonoBehaviour
{
    // Prywatne statyczne pole przechowujące instancję
    private static DiceRollManager instance;

    // Publiczny dostęp do instancji
    public static DiceRollManager Instance
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

    // Zmienne do przechowywania wyniku
    public bool IsWaitingForRoll => _pendingRollCount > 0;
    private bool _hasActiveRoll;
    private int _pendingRollCount;

    [SerializeField] private TMP_InputField _roll1InputField;
    [SerializeField] private TMP_InputField _roll2InputField;
    [SerializeField] private GameObject _applyRollResultPanel;

    public int RollModifier = 0;
    private bool _isRollModifierUpdating = false;
    [SerializeField] private UnityEngine.UI.Slider _modifierSlider;
    [SerializeField] private TMP_InputField _modifierInputField;

    // --- KONTEKST OCZEKUJĄCEGO TESTU ---
    private Stats _pendingStats;
    private string _pendingAttributeName;
    private string _pendingSkillName;
    private int _pendingModifier;
    private int _pendingDifficultyLevel;
    private string _pendingRollContext;
    private bool _pendingApplyFatigue;

    // wynik gotowy do odebrania przez korutynę
    private int[] _pendingResult;
    private int _rollInputVersion;
    private int _rollQueueVersion;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) && IsWaitingForRoll)
        {
            ForceCancelPendingRollInput(cancelQueuedRolls: false);
        }
    }

    public IEnumerator WaitForRollValue(
        Stats stats,
        string rollContext,
        string attributeName,
        string skillName = null,
        int modifier = 0,
        int difficultyLevel = 0,
        Action<int[]> callback = null,
        bool applyFatigue = true)
    {
        if (!CanPerformTest(stats, attributeName, skillName))
        {
            callback?.Invoke(TestSkill(stats, rollContext, attributeName, skillName));
            yield break;
        }
        // Panel pozostaje zajęty aż do odebrania wyniku, również po jego ukryciu.
        int queueVersion = _rollQueueVersion;
        int version = 0;
        int[] result = null;
        _pendingRollCount++;
        try
        {
            while (_hasActiveRoll && queueVersion == _rollQueueVersion)
                yield return null;
            if (queueVersion != _rollQueueVersion || stats == null) yield break;
            version = ++_rollInputVersion;
            _hasActiveRoll = true;

            // Ustaw kontekst oczekującego testu
            _pendingStats = stats;
            _pendingAttributeName = attributeName;
            _pendingSkillName = skillName;
            _pendingModifier = modifier;
            _pendingDifficultyLevel = difficultyLevel;
            _pendingRollContext = rollContext;
            _pendingApplyFatigue = applyFatigue;
            _pendingResult = null;

            // Ustala interaktywność 2. kości:
            if (_roll2InputField != null)
            {
                string rc = _pendingRollContext?.ToLower() ?? "";

                bool isDamageRoll = rc.Contains("obrażenia");

                _roll2InputField.gameObject.SetActive(!isDamageRoll);

                if(isDamageRoll)
                {
                    _roll1InputField.GetComponentInChildren<TMP_Text>().text = "Suma z kości";
                    _roll1InputField.GetComponent<InputFieldFilter>().SetBool("_isDamageRoll", true);
                    _roll1InputField.GetComponent<InputFieldFilter>().SetBool("_isDiceRoll", false);
                }
                else
                {
                    _roll1InputField.GetComponentInChildren<TMP_Text>().text = "Kość 1";
                    _roll1InputField.GetComponent<InputFieldFilter>().SetBool("_isDamageRoll", false);
                    _roll1InputField.GetComponent<InputFieldFilter>().SetBool("_isDiceRoll", true);
                }
            }

            // UI
            if (_applyRollResultPanel != null)
            {
                _applyRollResultPanel.SetActive(true);
                var label = _applyRollResultPanel.GetComponentInChildren<TMP_Text>();
                if (label != null)
                    label.text = $"Wpisz wynik rzutu {stats.Name} na {rollContext}.";
            }

            if (_roll1InputField != null) _roll1InputField.text = "";
            if (_roll2InputField != null) _roll2InputField.text = "";

            // Czekaj aż OnSubmitRoll policzy TestSkill i zapisze _pendingResult
            while (_pendingResult == null && version == _rollInputVersion)
                yield return null;

            if (version != _rollInputVersion) yield break;
            result = _pendingResult;
        }
        finally
        {
            // Anulowana, starsza korutyna nie może wyczyścić kontekstu nowego rzutu.
            if (version != 0 && version == _rollInputVersion) ClearPendingRollInput();
            if (queueVersion == _rollQueueVersion) _pendingRollCount--;
        }

        callback?.Invoke(result);
    }
    public void ForceCancelPendingRollInput(bool cancelQueuedRolls = true)
    {
        _rollInputVersion++;
        if (cancelQueuedRolls)
        {
            _rollQueueVersion++;
            _pendingRollCount = 0;
        }
        ClearPendingRollInput();
    }

    private void ClearPendingRollInput()
    {
        _hasActiveRoll = false;

        if (_roll1InputField != null) _roll1InputField.text = "";
        if (_roll2InputField != null) _roll2InputField.text = "";
        if (_applyRollResultPanel != null) _applyRollResultPanel.SetActive(false);

        _pendingResult = null;
        _pendingStats = null;
        _pendingAttributeName = null;
        _pendingSkillName = null;
        _pendingRollContext = null;
    }
    public void OnSubmitRoll()
    {
        if (!_hasActiveRoll || _pendingStats == null || _pendingResult != null
            || _roll1InputField == null || _roll2InputField == null)
            return;

        string rc = _pendingRollContext?.ToLower() ?? "";

        // sprawdź czy to rzut na obrażenia
        bool isDamageRoll = rc.Contains("obrażenia");

        int r1 = 0, r2 = 0;

        if (!int.TryParse(_roll1InputField.text, out r1))
        {
            Debug.Log("<color=red>Należy wpisać wynik kości.</color>");
            return;
        }

        if (isDamageRoll)
        {
            // RZUT NA OBRAŻENIA:
            // gracz wpisał SUMĘ wszystkich kości do pierwszego pola
            _pendingResult = new int[] { r1 };

            _roll1InputField.text = "";
            _roll2InputField.text = "";
            _applyRollResultPanel.SetActive(false);
            return;
        }

        // TESTY CECH/UMIEJĘTNOŚCI – jak wcześniej: dwa pola
        if (!int.TryParse(_roll2InputField.text, out r2))
        {
            Debug.Log("<color=red>Należy wpisać wynik obu kości.</color>");
            return;
        }

        _pendingResult = TestSkill(
            stats: _pendingStats,
            rollContext: _pendingRollContext,
            attributeName: _pendingAttributeName,
            skillName: _pendingSkillName,
            modifier: _pendingModifier,
            roll1: r1,
            roll2: r2,
            difficultyLevel: _pendingDifficultyLevel,
            applyFatigue: _pendingApplyFatigue
        );

        _roll1InputField.text = "";
        _roll2InputField.text = "";
        _applyRollResultPanel.SetActive(false);
    }

    // Funkcja sprawdzająca, czy liczba ma dwie identyczne cyfry
    public bool IsDoubleDigit(int number1, int number2)
    {
        if (number1 >= 1 && number1 <= 10 && number1 == number2) return true;
        else return false;
    }

    public void SetRollModifier(GameObject gameObject)
    {
        if (_isRollModifierUpdating) return;
        _isRollModifierUpdating = true;

        if (gameObject.GetComponent<UnityEngine.UI.Slider>() != null)
        {
            _modifierInputField.text = _modifierSlider.value.ToString();
            RollModifier = (int)_modifierSlider.value;
        }
        else
        {
            if (int.TryParse(_modifierInputField.text, out int value))
            {
                value = Mathf.Clamp(value, -10, 10);
                _modifierSlider.SetValueWithoutNotify(Mathf.RoundToInt(value)); // Dopasowanie wartości slidera bez wywołania eventu
                RollModifier = value;
            }
            else
            {
                _modifierInputField.text = "0";
                RollModifier = 0;
            }
        }

        _isRollModifierUpdating = false;
    }


    public void ResetRollModifier()
    {
        _modifierSlider.value = 0;
        _modifierInputField.text = "0";
        RollModifier = 0;
    }

    #region Attributes and skills tests
    public static bool CanPerformTest(Stats stats, string attributeName, string skillName = null)
    {
        if (stats == null) return false;
        if (!stats.Unmeaning) return true;
        return attributeName != "SW" && attributeName != "Ch" && attributeName != "Int"
            && skillName != "Cool" && skillName != "Spellcasting" && skillName != "Pray"
            && skillName != "Channeling" && skillName != "MagicLanguage";
    }

    public int[] TestSkill(
    Stats stats,
    string rollContext,              // kontekst do logów
    string attributeName,            // może być null
    string skillName = null,
    int modifier = 0,
    int difficultyLevel = 0,
    int roll1 = 0,
    int roll2 = 0,
    bool applyFatigue = true)
    {
        if (!CanPerformTest(stats, attributeName, skillName))
        {
            Debug.Log($"<color=red>{stats.Name} posiada cechę Bezrozumny – nie może wykonywać testów Charyzmy, Inteligencji, Siły Woli ani opartych na nich umiejętności.</color>");
            ResetRollModifier();
            return new int[] { 0, 0, 0 }; // Nie wykonano testu; zerowe kości nie wywołują Szczęścia ani Pecha.
        }
        // Pobieranie wartości umiejętności
        int skillValue = 0;
        if (!string.IsNullOrEmpty(skillName))
        {
            var field = typeof(Stats).GetField(skillName);
            if (field != null)
                skillValue = (int)field.GetValue(stats);
        }

        // Losowanie kości tylko jeśli nie podano gotowych wyników
        if (roll1 == 0)
        {
            roll1 = UnityEngine.Random.Range(1, 11);
            roll2 = UnityEngine.Random.Range(1, 11);
        }

        // Modyfikator z panelu jednostki
        if (RollModifier != 0)
            modifier += RollModifier;

        // Modyfikator za Strach
        Unit unit = stats.GetComponent<Unit>();
        if (unit != null && unit.Scared && !unit.Blinded) modifier -= 2;
        if (applyFatigue && unit != null)
        {
            int fatigue = Mathf.Clamp(unit.Fatiqued, 0, 5);
            modifier -= fatigue;
            if (fatigue > 0) Debug.Log($"{stats.Name}: Zmęczenie obniża wynik testu o {fatigue}.");
        }

        // W przypadku testu Refleksu wybieramy, która cecha jest wyższa
        if (skillName == "Reflex")
        {
            // Podstawa: wyższa z P lub Zw
            int baseAttr = Mathf.Max(stats.P, stats.Zw);
            attributeName = (baseAttr == stats.Zw) ? "Zw" : "P";
        }

        // Pobieranie wartości CECHY — bezpiecznie, gdy attributeName == null
        int attributeValue = 0;
        if (!string.IsNullOrEmpty(attributeName))
        {
            var attributeField = typeof(Stats).GetField(attributeName);
            if (attributeField != null)
                attributeValue = (int)attributeField.GetValue(stats);
        }

        // ===== Użycie talentu SPECJALISTA: warunkowy przerzut niższej kości =====
        if (stats.HasSpecialist(skillName) && (GameManager.IsAutoDiceRollingMode || stats.CompareTag("EnemyUnit")))
        {
            int oldValue = roll1 < roll2 ? roll1 : roll2;

            bool shouldReroll = oldValue <= 5; // przerzut jeśli niższa kość wynosi 5 lub mniej

            if (shouldReroll)
            {
                // przerzut
                int newValue = UnityEngine.Random.Range(1, 11);

                if (roll1 < roll2)
                    roll1 = newValue;
                else
                    roll2 = newValue;

                // logi
                if (string.IsNullOrEmpty(rollContext))
                    rollContext = !string.IsNullOrEmpty(skillName) ? skillName :
                                  !string.IsNullOrEmpty(attributeName) ? attributeName : "";

                string oldStr = oldValue > 0 ? $" z <color=#FF7F50>{oldValue}</color>" : "";
                Debug.Log($"{stats.Name} korzysta z talentu Specjalista i przerzuca niższą kość{oldStr} na <color=#FF7F50>{newValue}</color>.");

            }
        }

        // Dublety sprawdzamy na rzeczywistych kościach, po przerzucie Specjalisty,
        // ale przed liczbową premią Twardziela.
        int rawRoll1 = roll1;
        int rawRoll2 = roll2;

        // ===== Talent TWARDZIEL (Hardy): podwajamy niższą kość przy rzucie na Kondycję =====
        if (stats.Hardy && attributeName == "K")
        {
            int oldValue = 0;
            if (roll1 <= roll2)
            {
                oldValue = roll1;
                roll1 = Mathf.Min(roll1 * 2, 10);
            }
            else
            {
                oldValue = roll2;
                roll2 = Mathf.Min(roll2 * 2, 10);
            }
            Debug.Log($"{stats.Name} korzysta z talentu Twardziel – niższa kość zostaje zwiększona z <color=#4dd2ff>{oldValue}</color> na <color=#4dd2ff>{Mathf.Min(oldValue * 2, 10)}</color>.");
        }

        int finalScore = Mathf.Max(0, roll1 + roll2 + Math.Min(skillValue + attributeValue, 10) + modifier);

        if (string.IsNullOrEmpty(rollContext))
        {
            rollContext = !string.IsNullOrEmpty(skillName) ? skillName :
                          !string.IsNullOrEmpty(attributeName) ? attributeName : "";
        }

        string attrString = attributeValue != 0 ? $" Modyfikator z cechy: {attributeValue}." : "";
        string skillString = skillValue != 0 ? $" Modyfikator z umiejętności: {skillValue}." : "";

        bool hasBaseMods = (attributeValue != 0 || skillValue != 0);
        string modifierLabel = hasBaseMods ? " Inne modyfikatory" : " Modyfikatory";
        string modifierString = modifier != 0 ? $"{modifierLabel}: {modifier}." : "";

        string difficultyLevelString = difficultyLevel != 0 ? $"/{difficultyLevel}" : "";

        string color = (difficultyLevel == 0 || finalScore >= difficultyLevel) ? "green" : "red";

        string roll1Str = $"<color=#4dd2ff>{roll1}</color>";
        string roll2Str = roll2 != 0 ? $" + <color=#4dd2ff>{roll2}</color>" : "";

        // suma rzutów do wyświetlenia: jeśli jest roll2, pokazujemy "= suma"
        string totalStr = roll2 != 0 ? $" = <color=#4dd2ff>{roll1 + roll2}</color>" : "";

        if (!string.IsNullOrEmpty(stats.Name))
        {
            Debug.Log(
                $"{stats.Name} rzuca na {rollContext}: {roll1Str}{roll2Str}{totalStr}."
                + $"{attrString}{skillString}{modifierString} Łączny wynik: <color={color}>{finalScore}{difficultyLevelString}</color>."
            );

            if (difficultyLevel != 0 && IsDoubleDigit(rawRoll1, rawRoll2))
            {
                if (finalScore >= difficultyLevel)
                {
                    Debug.Log($"{stats.Name} wyrzucił <color=green>SZCZĘŚCIE</color>!");
                    stats.FortunateEvents++;
                }
                else
                {
                    Debug.Log($"{stats.Name} wyrzucił <color=red>PECHA</color>!");
                    stats.UnfortunateEvents++;
                }
            }
        }

        ResetRollModifier();
        return new int[] { rawRoll1, rawRoll2, finalScore };
    }
    #endregion
}
