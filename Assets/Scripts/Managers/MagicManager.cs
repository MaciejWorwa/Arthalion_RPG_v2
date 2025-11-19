using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using TMPro;
using Unity.Collections.LowLevel.Unsafe;
using Unity.VisualScripting;
using Unity.VisualScripting.Antlr3.Runtime.Misc;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

public class MagicManager : MonoBehaviour
{
    // Prywatne statyczne pole przechowujące instancję
    private static MagicManager instance;

    // Publiczny dostęp do instancji
    public static MagicManager Instance
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

    [SerializeField] private CustomDropdown _spellbookDropdown;
    [SerializeField] private UnityEngine.UI.Button _castSpellButton;
    public List<Spell> SpellBook = new List<Spell>();
    public static bool IsTargetSelecting;
    private float _spellDistance;

    public HashSet<GameObject> Targets = new HashSet<GameObject>();
    private List<Stats> _targetsStats; // Lista jednostek, które są wybierane jako cele zaklęcia, które pozwala wybrać więcej niż jeden cel

    [Header("Panel do manualnego zarządzania krytycznym splecieniem zaklęcia")]
    [SerializeField] private GameObject _criticalCastingPanel;
    public string CriticalCastingString;
    [SerializeField] private UnityEngine.UI.Button _extraTargetButton;
    [SerializeField] private UnityEngine.UI.Button _extraDamageButton;
    [SerializeField] private UnityEngine.UI.Button _extraRangeButton;
    [SerializeField] private UnityEngine.UI.Button _extraAreaSizeButton;
    [SerializeField] private UnityEngine.UI.Button _extraDurationButton;

    void Start()
    {
        //Wczytuje listę wszystkich zaklęć
        DataManager.Instance.LoadAndUpdateSpells();

        _targetsStats = new List<Stats>();

        _extraTargetButton.onClick.AddListener(() => CriticalButtonClick(_extraTargetButton.gameObject, "target"));
        _extraDamageButton.onClick.AddListener(() => CriticalButtonClick(_extraDamageButton.gameObject, "damage"));
        _extraRangeButton.onClick.AddListener(() => CriticalButtonClick(_extraRangeButton.gameObject, "range"));
        _extraAreaSizeButton.onClick.AddListener(() => CriticalButtonClick(_extraAreaSizeButton.gameObject, "area_size"));
        _extraDurationButton.onClick.AddListener(() => CriticalButtonClick(_extraDurationButton.gameObject, "duration"));
    }

    #region Casting
    public void CastingSpellMode()
    {
        if (Unit.SelectedUnit == null) return;

        if (Unit.SelectedUnit.GetComponent<Stats>().Spellcasting == 0)
        {
            Debug.Log("Wybrana jednostka nie może rzucać zaklęć.");
            return;
        }

        if (!Unit.SelectedUnit.GetComponent<Unit>().CanCastSpell && RoundsManager.RoundNumber != 0)
        {
            Debug.Log("Wybrana jednostka nie może w tej rundzie rzucić więcej zaklęć.");
            return;
        }

        if (_spellbookDropdown.SelectedButton == null)
        {
            Debug.Log("Musisz najpierw wybrać zaklęcie z listy.");
            return;
        }

        Targets.Clear();
        string selectedSpellName = _spellbookDropdown.SelectedButton.GetComponentInChildren<TextMeshProUGUI>().text;
        DataManager.Instance.LoadAndUpdateSpells(selectedSpellName);

        if (!Unit.SelectedUnit.GetComponent<Unit>().CanDoAction)
        {
            Debug.Log("Ta jednostka nie może w tej rundzie wykonać więcej akcji.");
            return;
        }

        RoundsManager.Instance.DoAction(Unit.SelectedUnit.GetComponent<Unit>());

        StartCoroutine(CastSpell());
    }

    public IEnumerator CastSpell()
    {
        if (Unit.SelectedUnit == null) yield break;

        Stats stats = Unit.SelectedUnit.GetComponent<Stats>();
        Unit unit = Unit.SelectedUnit.GetComponent<Unit>();
        Spell spell = Unit.SelectedUnit.GetComponent<Spell>();

        unit.CanCastSpell = false;
        CriticalCastingString = "";

        int rollResult = 0;
        int[] castingTest = null;
        if (!GameManager.IsAutoDiceRollingMode && stats.CompareTag("PlayerUnit"))
        {
            yield return StartCoroutine(DiceRollManager.Instance.WaitForRollValue(stats, "Rzucanie Zaklęć", "SW", "Spellcasting", difficultyLevel: spell.CastingNumber, callback: result => castingTest = result));
            if (castingTest == null) yield break;
        }
        else
        {
            castingTest = DiceRollManager.Instance.TestSkill(stats, "Rzucanie Zaklęć", "SW", "Spellcasting");
        }
        rollResult = castingTest[2];

        bool spellFailed = spell.CastingNumber - rollResult > 0;
        Debug.Log(spellFailed ? $"Rzucanie zaklęcia {spell.Name} nie powiodło się." : $"Zaklęcie {spell.Name} zostało rzucone pomyślnie.");

        // Zresetowanie zaklęcia
        ResetSpellCasting();

        // Krytyczne rzucenie zaklęcia
        if (DiceRollManager.Instance.IsDoubleDigit(castingTest[0], castingTest[1]))
        {
            StartCoroutine(GodsWrath(stats, spell.CastingNumber));

            if(!spellFailed)
            {
                CriticalCasting(spell);
            }
        }

        while (_criticalCastingPanel.activeSelf)
        {
            yield return null;
        }

        // Zaklęcie nie zostało w pełni splecione - przerywamy funkcję
        if (spellFailed) yield break;

        GridManager.Instance.ResetColorOfTilesInMovementRange();

        IsTargetSelecting = true;
        _targetsStats.Clear();

        //Zmienia kolor przycisku na aktywny
        _castSpellButton.GetComponent<UnityEngine.UI.Image>().color = UnityEngine.Color.green;

        Debug.Log("Kliknij prawym przyciskiem myszy na jednostkę, która ma być celem zaklęcia.");

        if (CriticalCastingString == "target") spell.Targets *= 2;

        while (Targets.Count < spell.Targets)
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) break;
            yield return null;
        }

        foreach (GameObject target in Targets)
        {
            Stats targetStats = target.GetComponent<Stats>();
            Unit targetUnit = target.GetComponent<Unit>();

            //Sprawdza dystans
            _spellDistance = CombatManager.Instance.CalculateDistance(Unit.SelectedUnit, target);

            if (CriticalCastingString == "range") spell.Range *= 2;

            Debug.Log($"Dystans do celu: {_spellDistance}. Zasięg zaklęcia: {spell.Range}");

            if (_spellDistance > spell.Range)
            {
                Debug.Log($"{targetStats.Name} znajduje się poza zasięgiem zaklęcia.");
                continue;
            }

            if (CriticalCastingString == "area_size") spell.AreaSize *= 2;

            // Pobiera wszystkie collidery w obszarze działania zaklęcia
            List<Collider2D> allTargets = Physics2D.OverlapCircleAll(target.transform.position, spell.AreaSize).ToList();

            // Filtruje wśród colliderów jednostki, na których można użyć tego zaklęcia
            allTargets.RemoveAll(collider =>
                collider.GetComponent<Unit>() == null ||
                (collider.gameObject == Unit.SelectedUnit && spell.Type.Contains("offensive")) ||
                (collider.gameObject != Unit.SelectedUnit && spell.Type.Contains("self-only"))
            );

            if (allTargets.Count == 0)
            {
                Debug.Log("W obszarze działania zaklęcia musi znaleźć się odpowiedni cel.");
                continue;
            }

            // Wywołanie efektu zaklęcia
            foreach (var collider in allTargets)
            {
                StartCoroutine(HandleSpellEffect(stats, collider.GetComponent<Stats>(), spell, rollResult, castingTest));
            }
        }  

        ResetSpellCasting();
    }

    public void ResetSpellCasting()
    {
        IsTargetSelecting = false;
        _castSpellButton.GetComponent<UnityEngine.UI.Image>().color = UnityEngine.Color.white;

        if (Unit.SelectedUnit != null)
        {
            GridManager.Instance.HighlightTilesInMovementRange(Unit.SelectedUnit.GetComponent<Stats>());
        }
    }
    #endregion

    #region Handle spell effect
    private IEnumerator HandleSpellEffect(Stats spellcasterStats, Stats targetStats, Spell spell, int rollResult, int[] castingTest)
    {
        Unit targetUnit = targetStats.GetComponent<Unit>();
        int successLevel = 0;

        if (CriticalCastingString == "duration") spell.Duration *= 2;

        //Uwzględnienie czasu trwania zaklęcia, które wpływa na statystyki postaci
        if (spell.Duration != 0)
        {
            // Przygotowanie słownika modyfikacji – iterujemy po liście atrybutów, które zaklęcie ma zmieniać
            Dictionary<string, int> modifications = new Dictionary<string, int>();

            // Przykładowa logika: dla każdego atrybutu pobieramy klucz i wartość
            // Jeśli chcemy skalować pierwszy atrybut przez SW, można to zrobić warunkowo
            for (int i = 0; i < spell.Attributes.Count; i++)
            {
                string attributeName = spell.Attributes[i].Key;
                int baseModifier = spell.Attributes[i].Value;
                modifications[attributeName] = baseModifier;
            }

            // Jeśli efekt dotyczy jednostki, która już ma jakiś efekt tego samego zaklęcia, możesz zdecydować czy mają się kumulować,
            // czy nadpisywać – poniżej przykład, gdzie zawsze nadpisujemy poprzedni efekt z danego spellName.
            var existingEffect = targetStats.ActiveSpellEffects.FirstOrDefault(e => e.SpellName == spell.Name);
            if (existingEffect != null)
            {
                existingEffect.RemainingRounds = spell.Duration;
                Debug.Log($"Nadpisujemy poprzedni efekt zaklęcia {spell.Name} u {targetStats.Name}.");
                yield break;
            }

            // Tworzymy nowy efekt
            SpellEffect newEffect = new SpellEffect(spell.Name, spell.Duration, modifications);
            targetStats.ActiveSpellEffects.Add(newEffect);
        }

        //Próba obrony przed zaklęciem
        if (!string.IsNullOrEmpty(spell.SaveAttribute) || !string.IsNullOrEmpty(spell.SaveSkill))
        {
            // Pobiera pierwszy atrybut jako ten, który służy do testu obronnego
            // string attributeName = spell.Attributes.First().Key;  // Zmieniono dostęp do atrybutu, teraz używamy First()

            int saveRollResult = 0;

            string saveName = !string.IsNullOrEmpty(spell.SaveSkill) ? spell.SaveSkill : spell.SaveAttribute;

            // ustalamy difficultyLevel: albo rollResult, albo SaveDifficulty
            int saveDifficulty = spell.SaveDifficulty > 0 ? spell.SaveDifficulty : rollResult;

            int[] saveTest = null;
            if (!GameManager.IsAutoDiceRollingMode && targetUnit.CompareTag("PlayerUnit"))
            {
                yield return StartCoroutine(DiceRollManager.Instance.WaitForRollValue(targetStats, $"{saveName} (rzut obronny przed zaklęciem)", spell.SaveAttribute, spell.SaveSkill, difficultyLevel: saveDifficulty, callback: result => saveTest = result));
                if (saveTest == null) yield break;
            }
            else
            {
                saveTest = DiceRollManager.Instance.TestSkill(targetStats, $"{saveName} (rzut obronny przed zaklęciem)", spell.SaveAttribute, spell.SaveSkill, difficultyLevel: saveDifficulty);
            }
            saveRollResult = saveTest[2];

            if (saveRollResult < saveDifficulty || (spell.SaveDifficulty == 0 && saveRollResult == saveDifficulty))
            {
                Debug.Log($"{targetStats.Name} nie udało się przeciwstawić zaklęciu.");
                successLevel = rollResult - saveRollResult;
            }
            else
            {
                Debug.Log($"{targetStats.Name} udało się przeciwstawić zaklęciu.");
                yield break;
            }
        }

        if (spell.Attributes != null && spell.Attributes.Count > 0)
        {
            // Konwersja listy Attributes do listy kluczy
            var keys = spell.Attributes.Select(a => a.Key).ToList();

            for (int i = 0; i < keys.Count; i++)
            {
                string attributeName = keys[i];
                int baseValue = (spell.Strength != null && spell.Strength.Length > 0) ? UnityEngine.Random.Range(1, Mathf.Max(1, spell.Strength[0]) + 1) : spell.Attributes.First(a => a.Key == attributeName).Value;

                Stats affectedStats = spell.Type.Contains("self-attribute") ? spellcasterStats : targetStats;
                Unit affectedUnit = affectedStats.GetComponent<Unit>();

                // Szukamy pola najpierw w Stats
                FieldInfo field = affectedStats.GetType().GetField(attributeName);

                object targetObject = affectedStats;

                // Jeśli nie znaleziono w Stats, próbujemy znaleźć w Unit
                if (field == null && affectedUnit != null)
                {
                    field = affectedUnit.GetType().GetField(attributeName);
                    targetObject = affectedUnit;
                }

                if (field == null) continue;

                int value = baseValue;

                if (field.FieldType == typeof(bool))
                {
                    bool boolValue = value != 0;
                    field.SetValue(targetObject, boolValue);
                    Debug.Log($"{affectedStats.Name} zmienia cechę {attributeName} na {(boolValue ? "aktywną" : "nieaktywną")}.");

                    //if (field.Name == "Entangled")
                    //{
                    //    spellcasterStats.GetComponent<Unit>().EntangledUnitId = targetUnit.UnitId;
                    //}
                }
                else if (field.FieldType == typeof(int))
                {
                    if (attributeName == "TempHealth" && targetObject is Stats)
                    {
                        int newValue = (int)field.GetValue(affectedStats) + value;
                        if (newValue <= affectedStats.MaxHealth)
                        {
                            field.SetValue(affectedStats, newValue);
                            affectedUnit.DisplayUnitHealthPoints();
                            UnitsManager.Instance.UpdateUnitPanel(Unit.SelectedUnit);
                            Debug.Log($"{affectedStats.Name} odzyskuje {value} punktów Żywotności.");
                        }
                    }
                    else
                    {
                        int current = (int)field.GetValue(targetObject);
                        int newValue = current + value;

                        // jeśli pole to Bleeding albo Poison -> zabezpieczenie przed < 0
                        if (field.Name == "Bleeding" || field.Name == "Poison")
                        {
                            newValue = Mathf.Max(0, newValue);
                        }

                        field.SetValue(targetObject, newValue);
                        Debug.Log($"Zaklęcie {spell.Name} zmienia u {affectedStats.Name} cechę {attributeName} o {value}.");
                    }

                    if (attributeName == "NaturalArmor")
                    {
                        InventoryManager.Instance.CheckForEquippedWeapons();
                    }
                }
            }

            UnitsManager.Instance.UpdateUnitPanel(Unit.SelectedUnit);
        }

        //Zaklęcia zadające obrażenia
        if (!spell.Type.Contains("no-damage") && spell.Type.Contains("offensive"))
        {
            StartCoroutine(DealMagicDamage(spellcasterStats, targetStats, spell, successLevel, castingTest));
        }
    }

    public IEnumerator DealMagicDamage(
        Stats spellcasterStats,
        Stats targetStats,
        Spell spell,
        int successLevel,
        int[] castingTest)
    {
        if (spellcasterStats == null || targetStats == null || spell == null)
            yield break;

        // === helper do ustalenia obrażeń constant-strength (1–2 kości + opcjonalny modyfikator) ===
        IEnumerator ComputeConstantStrengthDamage(
            Stats caster,
            int[] strengthSpec,
            Action<int> onComputed)
        {
            if (strengthSpec == null || strengthSpec.Length == 0)
            {
                onComputed?.Invoke(0);
                yield break;
            }

            // Kości: max 2 pierwsze wartości; 3. to modyfikator
            int dice1 = Mathf.Max(1, strengthSpec[0]);
            int dice2 = (strengthSpec.Length >= 2) ? Mathf.Max(1, strengthSpec[1]) : 0;
            int modifier = (strengthSpec.Length >= 3) ? strengthSpec[2] : 0;

            string diceCtx = FormatDiceContext(strengthSpec);

            int die1, die2;
            if (!GameManager.IsAutoDiceRollingMode && caster.CompareTag("PlayerUnit"))
            {
                int[] damageRoll = null;
                yield return StartCoroutine(DiceRollManager.Instance.WaitForRollValue(
                        caster,
                        $"obrażenia zaklęcia {diceCtx}",   // np. (k6+k10) albo (k8)
                        null,
                        callback: result => damageRoll = result
                    )
                );
                if (damageRoll == null || damageRoll.Length == 0)
                {
                    onComputed?.Invoke(0);
                    yield break;
                }

                // Gracz wpisał SUMĘ wszystkich kości do pierwszego pola
                int totalInput = damageRoll[0];

                // Jeśli chcesz, możesz tu dodać sanity check (zakres min/max),
                // ale nie jest to konieczne.
                int total = totalInput + modifier;
                onComputed?.Invoke(total);
            }
            else
            {
                // Tryb automatyczny – zostaw tak jak masz
                die1 = UnityEngine.Random.Range(1, dice1 + 1);
                die2 = (dice2 > 0) ? UnityEngine.Random.Range(1, dice2 + 1) : 0;

                int total = die1 + die2 + modifier;
                Debug.Log($"Wynik rzutu na obrażenia zaklęcia: <color=#4dd2ff>{die1}</color>"
                    + (dice2 > 0 ? $"+ <color=#4dd2ff>{die2}</color>" : "")
                    + (modifier != 0 ? $" +<color=#FF7F50>{modifier}</color>" : "")
                    + $". Suma: <color=#4dd2ff>{total}</color>.");
                onComputed?.Invoke(total);
            }
        }

        // Jeśli typ zawiera "constant-strength", obrażenia są wynikiem rzutów wg spell.Strength[]
        if (spell.Strength != null && spell.Strength.Length > 0)
        {
            int rolled = 0;
            yield return ComputeConstantStrengthDamage(spellcasterStats, spell.Strength, total => rolled = total);
            successLevel = rolled;
        }

        // Siła obrażeń = successLevel (w tym dla constant-strength)
        int damage = successLevel;

        //Debug.Log($"Poziom sukcesu {spellcasterStats.Name}: {successLevel}. Siła zaklęcia: {spell.Strength}");

        // --- Ustalamy miejsce trafienia ---
        int roll1 = castingTest[0];
        int roll2 = castingTest[1];
        int chosenValue;

        // domyślnie bierzemy niższą z kości
        chosenValue = roll1 > roll2 ? roll2 : roll1;

        string unnormalizedHitLocation = !string.IsNullOrEmpty(CombatManager.Instance.HitLocation) ? CombatManager.Instance.HitLocation : null;

        if (string.IsNullOrEmpty(unnormalizedHitLocation))
        {
            // uruchom korutynę wyboru lokacji; wynik przyjdzie w callbacku
            yield return StartCoroutine(CombatManager.Instance.DetermineHitLocationCoroutine(
                chosenValue,
                targetStats,
                location => unnormalizedHitLocation = location
            ));
        }

        string hitLocation = CombatManager.Instance.NormalizeHitLocation(unnormalizedHitLocation);

        if (!String.IsNullOrEmpty(hitLocation))
        {
            Debug.Log($"Atak jest skierowany w {CombatManager.Instance.TranslateHitLocation(unnormalizedHitLocation)}.");
        }

        if (CriticalCastingString == "damage") damage *= 2;

        Debug.Log($"Łączne obrażenia zadane przez {spellcasterStats.Name}: <color=#4dd2ff>{damage}</color>");

        // === PANCERZ CELU (bezpiecznie) ===
        int metalArmorValue = 0;
        int armor = CombatManager.Instance.CalculateArmor(targetStats, unnormalizedHitLocation, null, out metalArmorValue);

        var inventory = targetStats.GetComponent<Inventory>();
        var dict = inventory != null ? inventory.ArmorByLocation : null;

        List<Weapon> armorByLocation =
            (dict != null && dict.ContainsKey(hitLocation) && dict[hitLocation] != null)
                ? dict[hitLocation]
                : new List<Weapon>();


        // IGNOROWANIE PANCERZA
        if (spell.ArmourIgnoring) armor = 0;

        if (spell.DamageType == "Electric" && metalArmorValue > 0)
        {
            int extraDamage = UnityEngine.Random.Range(1, 7);
            damage += extraDamage;
            Debug.Log($"{targetStats.Name} otrzymuje +{extraDamage} obrażeń za noszenie metalowego pancerza.");
        }

        //if (spell.MetalArmourIgnoring && metalArmorValue != 0)
        //{
        //    armor -= metalArmorValue; // może zejść poniżej zera – dalej i tak zclampujesz w ApplyDamageToTarget

        //    if (spell.DamageType == "Electric" && metalArmorValue > 0)
        //    {
        //        int extraDamage = UnityEngine.Random.Range(1, 7);
        //        damage += extraDamage;
        //        Debug.Log($"{targetStats.Name} otrzymuje +{extraDamage} obrażeń za noszenie metalowego pancerza.");
        //    }
        //}

        // === ZADANIE OBRAŻEŃ ===
        CombatManager.Instance.ApplyDamageToTarget(
            damage,
            armor,
            spellcasterStats,
            targetStats,
            targetStats.GetComponent<Unit>(),
            damageType: spell.DamageType
        );

        // === KRYTYK / ŚMIERĆ ===
        if (targetStats.TempHealth < 0)
        {
            if (GameManager.IsAutoKillMode)
            {
                CombatManager.Instance.HandleDeath(targetStats, targetStats.gameObject, null);
            }
            else
            {
                yield return StartCoroutine(CombatManager.Instance.CriticalWoundRoll(spellcasterStats, targetStats, unnormalizedHitLocation));
            }
        }
    }

    // === helper: zapis kości do kontekstu UI/logów ===
    string FormatDiceContext(int[] strengthSpec)
    {
        if (strengthSpec == null || strengthSpec.Length == 0) return "";

        int dice1 = Mathf.Max(1, strengthSpec[0]);
        int dice2 = (strengthSpec.Length >= 2) ? Mathf.Max(1, strengthSpec[1]) : 0;
        int modifier = (strengthSpec.Length >= 3) ? strengthSpec[2] : 0;

        string part;
        if (dice2 > 0)
        {
            if (dice1 == dice2) part = $"2k{dice1}";
            else part = $"k{dice1}+k{dice2}";
        }
        else
        {
            part = $"k{dice1}";
        }

        if (modifier != 0) part += (modifier > 0 ? $"+{modifier}" : $"{modifier}");
        return $"({part})";
    }
    #endregion

    #region Critical Casting
    public void CriticalCasting(Spell spell)
    {
        if (_criticalCastingPanel == null) return;

        _extraTargetButton.interactable = !spell.Type.Contains("self-only") && spell.AreaSize == 0;
        _extraDamageButton.interactable = spell.Type.Contains("offensive") && !spell.Type.Contains("no-damage");
        _extraRangeButton.interactable = spell.Range != 1.5f;
        _extraAreaSizeButton.interactable = spell.AreaSize != 0;
        _extraDurationButton.interactable = spell.Duration != 0;

        _criticalCastingPanel.SetActive(true);
    }

    private void CriticalButtonClick(GameObject button, string action)
    {
        if (Unit.SelectedUnit == null || Unit.SelectedUnit.GetComponent<Spell>() == null) return;

        string arcane = Unit.SelectedUnit.GetComponent<Spell>().Arcane;

        CriticalCastingString = action;
    }
    #endregion

    #region Gods Wrath
    public IEnumerator GodsWrath(Stats stats, int spellLevel)
    {
        // Jeżeli jesteśmy w trybie manualnych rzutów kośćmi i wybrana jednostka to sojusznik to czekamy na wynik rzutu
        int[] test = null;
        if (!GameManager.IsAutoDiceRollingMode && stats.CompareTag("PlayerUnit"))
        {
            yield return StartCoroutine(DiceRollManager.Instance.WaitForRollValue(stats, "Gniew Boży", null, "Religious", spellLevel, callback: result => test = result));
            if (test == null) yield break;
        }
        else
        {
            test = DiceRollManager.Instance.TestSkill(stats, "Gniew Boży", null, "Religious", spellLevel);
        }

        int score = test[2];

        // Notka dołączana do każdego loga
        const string manualNote = " <color=orange>Efekt uwzględnij ręcznie</color>.";

        // Helper do 6 rund
        const string oneMinute = "6 rund";

        // Efekty wg progu wyniku
        if (score >= 4 && score <= 13)
        {
            Debug.Log($"{stats.Name}: Migotanie – zaklęcie zadziała na początku następnej tury rzucającego.{manualNote}");
        }
        else if (score >= 14 && score <= 15)
        {
            Debug.Log($"{stats.Name}: Rozbłysk – impuls magiczny oślepia rzucającego na 1 turę (stan Oślepienie).{manualNote}");
        }
        else if (score == 16)
        {
            Debug.Log($"{stats.Name}: Zwarcie magiczne – skóra pokrywa się iskrami; rzucający traci k4 Punktów Zdrowia.{manualNote}");
        }
        else if (score == 17)
        {
            Debug.Log($"{stats.Name}: Zanik mocy – brak możliwości rzucania zaklęć w następnej turze.{manualNote}");
        }
        else if (score == 18)
        {
            Debug.Log($"{stats.Name}: Przeciążenie umysłu – do końca starcia -2 do testów opartych na Inteligencji i Percepcji.{manualNote}");
        }
        else if (score == 19)
        {
            Debug.Log($"{stats.Name}: Echo – każdy dźwięk powtarza się szeptem przez minutę; testy Słuchu -5 do końca starcia.{manualNote}");
        }
        else if (score == 20)
        {
            Debug.Log($"{stats.Name}: Ślepe Oko Bogów – zaklęcie działa, ale trafia losowy cel w zasięgu (wszyscy rzucają 2k10, najwyższy wynik = cel).{manualNote}");
        }
        else if (score == 21)
        {
            Debug.Log($"{stats.Name}: Zwrot energii – zaklęcie działa, ale uderza w rzucającego; jeśli już był celem, dodatkowo traci k6 Punktów Zdrowia.{manualNote}");
        }
        else if (score == 22)
        {
            Debug.Log($"{stats.Name}: Wstrząs magiczny – k10 obrażeń ignorujących pancerz.{manualNote}");
        }
        else if (score == 23)
        {
            Debug.Log($"{stats.Name}: Paraliż – ciało sztywnieje na k4 tury; brak ruchu i akcji; traktowany jak w Utracie Przytomności.{manualNote}");
        }
        else if (score == 24)
        {
            int senseRoll = UnityEngine.Random.Range(1, 7); // k6 tylko do opisu
            string sense = senseRoll <= 2 ? "wzroku" : (senseRoll <= 4 ? "słuchu" : "dotyku");
            Debug.Log($"{stats.Name}: Zanik zmysłu – utrata {sense} na minutę ({oneMinute}).{manualNote}");
        }
        else if (score == 25)
        {
            Debug.Log($"{stats.Name}: Wypalenie magiczne – do końca starcia brak możliwości rzucania jakichkolwiek zaklęć.{manualNote}");
        }
        else if (score == 26)
        {
            Debug.Log($"{stats.Name}: Spaczenie Krwi – do najbliższego długiego odpoczynku każde rzucenie zaklęcia wywołuje Krwawienie (poziom 1).{manualNote}");
        }
        else if (score == 27)
        {
            Debug.Log($"{stats.Name}: Furia Żywiołu – użyty zostaje efekt innego potężnego zaklęcia z tej ścieżki (decyduje MG).{manualNote}");
        }
        else if (score == 28)
        {
            Debug.Log($"{stats.Name}: Drgawki – przez 3 tury: -2 do Zwinności i Zręczności.{manualNote}");
        }
        else if (score == 29)
        {
            Debug.Log($"{stats.Name}: Wyładowanie – fala o promieniu 10 m wokół rzucającego; wszystkie istoty test Odporności (16+) albo k10 obrażeń.{manualNote}");
        }
        else if (score == 30)
        {
            Debug.Log($"{stats.Name}: Zanik głosu – przez minutę ({oneMinute}) brak możliwości mówienia i rzucania zaklęć.{manualNote}");
        }
        else if (score == 31)
        {
            Debug.Log($"{stats.Name}: Rozproszenie – wszystkie aktywne efekty magiczne w promieniu 100 m natychmiast ustają.{manualNote}");
        }
        else if (score == 32)
        {
            Debug.Log($"{stats.Name}: Skoki energii – przez minutę ({oneMinute}) każdy test Rzucania Zaklęć dostaje dodatkowy rzut k10 (większa szansa na sukces i na dublet).{manualNote}");
        }
        else if (score == 33)
        {
            Debug.Log($"{stats.Name}: Blokada Mocy – do najbliższego długiego odpoczynku wszystkie testy Rzucania Zaklęć otrzymują karę -5.{manualNote}");
        }
        else if (score == 34)
        {
            Debug.Log($"{stats.Name}: Przekleństwo Mocy – do najbliższego długiego odpoczynku każde rzucenie zaklęcia ma 50% szansy na automatyczny Gniew Boży (gdy wynik rzutu jest nieparzysty).{manualNote}");
        }
        else if (score == 35)
        {
            Debug.Log($"{stats.Name}: Odmagicznienie – najbliższy magiczny przedmiot traci swoją moc (czasowo lub trwale – decyduje MG).{manualNote}");
        }
        else if (score == 36)
        {
            int deformRoll = UnityEngine.Random.Range(1, 7); // k6 do opisu
            string deform = deformRoll switch
            {
                1 => "dodatkowe oko (+1 poziom Spostrzegawczości, jeśli < 3)",
                2 => "dodatkowe ucho (+1 poziom Słuchu, jeśli < 3)",
                3 => "k4 dodatkowych palców (+1 do Zręczności)",
                4 => "dodatkowy język (głównie efekt fabularny)",
                5 => "k4 dodatkowych zębów (głównie efekt fabularny)",
                _ => "dodatkowe usta (mogą nauczyć się mówić po wydaniu 200 PD)"
            };
            Debug.Log($"{stats.Name}: Zniekształcenie – na ciele wyrasta {deform}. Miejsce wybiera MG.{manualNote}");
        }
        else if (score == 37)
        {
            int gateRoll = UnityEngine.Random.Range(1, 9); // k8 do opisu
            string entity = gateRoll switch
            {
                1 => "Nienazwany",
                2 => "Duch",
                3 => "Krwiożerczy Ogar",
                4 => "Pomiot Kainera",
                5 => "Upiór",
                6 => "Równy Żywiołom",
                7 => "Licz",
                _ => "Demon"
            };
            Debug.Log($"{stats.Name}: Wrota Wymiarów – otwiera się szczelina; pojawia się: {entity}.{manualNote}");
        }
        else if (score == 38)
        {
            Debug.Log($"{stats.Name}: Wessanie – zostaje wessany do innego wymiaru na k4 tury; po powrocie test SW (18+); porażka: traci bazowe k4 SW.{manualNote}");
        }
        else if (score == 39)
        {
            Debug.Log($"{stats.Name}: Psychoza – trwały uraz psychiczny (np. paranoja, amnezja – wybór MG).{manualNote}");
        }
        else if (score >= 40)
        {
            Debug.Log($"{stats.Name}: Katastrofa Magiczna – fala niszczy wszystko w promieniu 100 m; istoty unicestwione, teren skażony, świat się zmienia.{manualNote}");
        }
    }
    #endregion
}