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
    public bool IsWaitingForRoll;

    [SerializeField] private TMP_InputField _roll1InputField;
    [SerializeField] private TMP_InputField _roll2InputField;
    [SerializeField] private TMP_InputField _skillRollInputField;
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
    private Action<int[]> _pendingCallback;
    private string _pendingRollContext;

    // wynik gotowy do odebrania przez korutynę
    private int[] _pendingResult;


    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) && IsWaitingForRoll)
        {
            IsWaitingForRoll = false; // Przerywamy oczekiwanie
        }
    }

    public IEnumerator WaitForRollValue(
        Stats stats,
        string rollContext,
        string attributeName,
        string skillName = null,
        int modifier = 0,
        int difficultyLevel = 0,
        Action<int[]> callback = null)
    {
        // Jeśli inny panel jest aktywny — poczekaj
        while (_applyRollResultPanel != null && _applyRollResultPanel.activeSelf)
            yield return null;

        // Ustaw kontekst oczekującego testu
        _pendingStats = stats;
        _pendingAttributeName = attributeName;
        _pendingSkillName = skillName;
        _pendingModifier = modifier;
        _pendingDifficultyLevel = difficultyLevel;
        _pendingCallback = callback;
        _pendingRollContext = rollContext;
        _pendingResult = null;
        IsWaitingForRoll = true;

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
        if (_skillRollInputField != null) _skillRollInputField.text = "";

        // zablokuj imput na Kość Umiejętności, jeśli jednostka nie posiada rozwiniętej umiejętności
        if (_skillRollInputField != null)
        {
            HasSkillDie(_pendingStats, _pendingSkillName, out var skillVal);
            _skillRollInputField.interactable = skillVal > 0;
        }

        // Czekaj aż OnSubmitRoll policzy TestSkill i zapisze _pendingResult
        while (_pendingResult == null)
            yield return null;

        // Schowaj panel i zwróć wynik
        if (_applyRollResultPanel != null)
            _applyRollResultPanel.SetActive(false);

        IsWaitingForRoll = false;

        _pendingCallback?.Invoke(_pendingResult);

        // Sprzątanie kontekstu
        _pendingStats = null;
        _pendingAttributeName = null;
        _pendingSkillName = null;
        _pendingCallback = null;
        _pendingRollContext = null;
    }

    public void OnSubmitRoll()
    {
        if (_roll1InputField == null || _roll2InputField == null || _skillRollInputField == null)
            return;

        if (!int.TryParse(_roll1InputField.text, out int r1) ||
                !int.TryParse(_roll2InputField.text, out int r2))
        {
            Debug.Log($"<color=red>Należy wpisać wynik obu kości k10.</color>");
            return;
        }

        // skillRoll jest opcjonalny — puste lub nieparsowalne = 0
        int r3 = 0;
        if (!string.IsNullOrWhiteSpace(_skillRollInputField.text))
            int.TryParse(_skillRollInputField.text, out r3);

        // Uruchom właściwy test z podanymi rzutami
        _pendingResult = TestSkill(
            stats: _pendingStats,
            rollContext: _pendingRollContext,
            attributeName: _pendingAttributeName,      // może być null
            skillName: _pendingSkillName,
            modifier: _pendingModifier,
            roll1: r1,
            roll2: r2,
            skillRoll: r3,                  // może być 0 — wtedy po prostu się doliczy 0
            difficultyLevel: _pendingDifficultyLevel
        );

        // Czyść inputy
        _roll1InputField.text = "";
        _roll2InputField.text = "";
        _skillRollInputField.text = "";

        _applyRollResultPanel.SetActive(false);
    }


    // Funkcja sprawdzająca, czy liczba ma dwie identyczne cyfry
    public bool IsDoubleDigit(int number1, int number2)
    {
        if (number1 == number2) return true;
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
    public int[] TestSkill(
    Stats stats,
    string rollContext,              // kontekst do logów
    string attributeName,            // może być null
    string skillName = null,
    int modifier = 0,
    int difficultyLevel = 0,
    int roll1 = 0,
    int roll2 = 0,
    int skillRoll = 0)
    {
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

            switch (skillValue)
            {
                case 1: skillRoll = UnityEngine.Random.Range(1, 5); break;  // k4
                case 2: skillRoll = UnityEngine.Random.Range(1, 7); break;  // k6
                case 3: skillRoll = UnityEngine.Random.Range(1, 9); break;  // k8
                default:
                    // brak kostki umiejętności
                    break;
            }
        }

        // --- ZAPAMIĘTAJ PIERWOTNE WYNIKI K10 (przed Twardzielem)
        int rawRoll1 = roll1;
        int rawRoll2 = roll2;

        // Modyfikator z panelu jednostki
        if (RollModifier != 0)
            modifier += RollModifier;

        // Modyfikator za Strach
        if (stats.GetComponent<Unit>().Scared && !stats.GetComponent<Unit>().Blinded) modifier -= 2;

        // Pobieranie wartości CECHY — bezpiecznie, gdy attributeName == null
        int attributeValue2 = 0;
        if (!string.IsNullOrEmpty(attributeName))
        {
            var attributeField = typeof(Stats).GetField(attributeName);
            if (attributeField != null)
                attributeValue2 = (int)attributeField.GetValue(stats);
        }

        int finalScore = roll1 + roll2 + skillRoll + attributeValue2 + modifier;
        if (finalScore < 0) finalScore = 0;

        // ===== Cecha Bezrozumny =====
        if (stats.Unmeaning && (attributeName == "SW" || attributeName == "Ch" || attributeName == "Int"))
        {
            // Jeśli jest poziom trudności – osiągnij go dokładnie; jeśli brak, daj „bezpieczny” wysoki wynik
            finalScore = (difficultyLevel > 0) ? difficultyLevel : 20;

            // Ustal etykietę kontekstu jak w Twojej logice
            if (string.IsNullOrEmpty(rollContext))
                rollContext = !string.IsNullOrEmpty(skillName) ? skillName :
                              !string.IsNullOrEmpty(attributeName) ? attributeName : "";

            Debug.Log($"<color=red>{stats.Name} posiada cechę Bezrozumny – nie może wykonywać testów opartych na Inteligencji, Charyźmie ani Sile Woli.</color>");

            ResetRollModifier();

            // Zwracamy „puste” kości (żeby nie odpalić sekcji SZCZĘŚCIE/PECH) i finalny wynik
            return new int[] { 0, 0, 0, finalScore };
        }

        // ===== Użycie talentu SPECJALISTA: warunkowy przerzut Kości Umiejętności =====
        if (stats.HasSpecialist(skillName) && skillValue >= 1 && skillValue <= 3 && (GameManager.IsAutoDiceRollingMode || stats.CompareTag("EnemyUnit")))
        {
            int oldSkillRoll = skillRoll;

            // średnie wartości dla k4/k6/k8
            float avg = skillValue switch
            {
                1 => 2.5f, // k4
                2 => 3.5f, // k6
                3 => 4.5f, // k8
                _ => 0f
            };

            bool shouldReroll = false;

            if (difficultyLevel > 0)
            {
                if (finalScore < difficultyLevel)
                    shouldReroll = true;      // zawsze przerzut – nie spełniasz trudności
                else
                    shouldReroll = false;     // spełniasz trudność – nie przerzucasz
            }
            else // difficultyLevel == 0
            {
                if (oldSkillRoll < avg)
                    shouldReroll = true;      // brak poziomu trudności – przerzut tylko przy wcześniejszym rzucie poniżej średniej
            }

            if (shouldReroll)
            {
                // przerzut
                switch (skillValue)
                {
                    case 1: skillRoll = UnityEngine.Random.Range(1, 5); break;  // k4
                    case 2: skillRoll = UnityEngine.Random.Range(1, 7); break;  // k6
                    case 3: skillRoll = UnityEngine.Random.Range(1, 9); break;  // k8
                }

                // logi
                if (string.IsNullOrEmpty(rollContext))
                    rollContext = !string.IsNullOrEmpty(skillName) ? skillName :
                                  !string.IsNullOrEmpty(attributeName) ? attributeName : "";

                string oldStr = oldSkillRoll > 0 ? $" z <color=#FF7F50>{oldSkillRoll}</color>" : "";
                Debug.Log($"{stats.Name} korzysta z talentu Specjalista i przerzuca Kość Umiejętności{oldStr} na <color=#FF7F50>{skillRoll}</color>.");

                finalScore = roll1 + roll2 + skillRoll + attributeValue2 + modifier;
            }
        }

        // ===== Talent TWARDZIEL (Hardy): podwajamy niższą kość przy rzucie na Kondycję =====
        if (stats.Hardy && attributeName == "K")
        {
            int oldValue = 0;
            if (roll1 <= roll2)
            {
                oldValue = roll1;
                roll1 *= 2;
            }
            else
            {
                oldValue = roll2;
                roll2 *= 2;
            }
            Debug.Log($"{stats.Name} korzysta z talentu Twardziel – niższa kość zostaje podwojona z <color=#4dd2ff>{oldValue}</color> na <color=#4dd2ff>{oldValue * 2}</color>.");
        }

        if (string.IsNullOrEmpty(rollContext))
        {
            rollContext = !string.IsNullOrEmpty(skillName) ? skillName :
                          !string.IsNullOrEmpty(attributeName) ? attributeName : "";
        }

        string attrString = attributeValue2 != 0 ? $" Modyfikator z cechy: {attributeValue2}." : "";

        // Jeśli nie ma modyfikatora z cechy, użyj etykiety "Modyfikatory", inaczej "Inne modyfikatory"
        string modifierLabel = string.IsNullOrEmpty(attrString) ? " Modyfikatory" : " Inne modyfikatory";
        string modifierString = modifier != 0 ? $"{modifierLabel}: {modifier}." : "";

        string difficultyLevelString = difficultyLevel != 0 ? $"/{difficultyLevel}" : "";

        string color = (difficultyLevel == 0 || finalScore >= difficultyLevel) ? "green" : "red";

        string roll1Str = $"<color=#4dd2ff>{roll1}</color>";
        string roll2Str = $"<color=#4dd2ff>{roll2}</color>";
        string skillRollStr = skillRoll != 0 ? $" + <color=#FF7F50>{skillRoll}</color>" : "";

        if (stats.Name != null && stats.Name.Length > 0)
        {
            Debug.Log($"{stats.Name} rzuca na {rollContext}: {roll1Str} + {roll2Str}{skillRollStr} = <color=#4dd2ff>{roll1 + roll2 + skillRoll}</color>." + $"{attrString}{modifierString} Łączny wynik: <color={color}>{finalScore}{difficultyLevelString}</color>.");

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
        return new int[] { roll1, roll2, skillRoll, finalScore };
    }
    #endregion

    private bool HasSkillDie(Stats stats, string skillName, out int skillValue)
    {
        skillValue = 0;
        if (stats == null || string.IsNullOrEmpty(skillName)) return false;

        var field = typeof(Stats).GetField(skillName);
        if (field == null) return false;

        skillValue = (int)field.GetValue(stats); // 0..3
        return skillValue > 0;
    }
}
