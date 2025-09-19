using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Mathematics;
using Unity.VisualScripting;
using Unity.VisualScripting.Antlr3.Runtime.Misc;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;
using static UnityEngine.Rendering.DebugUI;
using static UnityEngine.UI.CanvasScaler;

public class CombatManager : MonoBehaviour
{
    // Prywatne statyczne pole przechowujące instancję
    private static CombatManager instance;

    // Publiczny dostęp do instancji
    public static CombatManager Instance
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

    [Header("Przyciski wszystkich typów ataku")]
    [SerializeField] private UnityEngine.UI.Button _standardAttackButton;
    [SerializeField] private UnityEngine.UI.Button _chargeButton;
    [SerializeField] private UnityEngine.UI.Button _mountAttackButton;
    [SerializeField] private UnityEngine.UI.Button _grapplingButton;
    [SerializeField] private UnityEngine.UI.Button _disarmButton;
    public Dictionary<string, bool> AttackTypes = new Dictionary<string, bool>();

    [SerializeField] private UnityEngine.UI.Button _aimButton;
    [SerializeField] private UnityEngine.UI.Button _reloadButton;

    [Header("Panel do manualnego zarządzania sposobem obrony")]
    [SerializeField] private GameObject _parryAndDodgePanel;
    [SerializeField] private UnityEngine.UI.Button _dodgeButton;
    [SerializeField] private UnityEngine.UI.Button _parryButton;
    private string _parryOrDodge;
    public int[] DefenceResults = new int[3]; // Wynik rzutu obronnego

    private string _grapplingActionChoice = "";    // Zmienna do przechowywania wyboru akcji przy grapplingu
    [SerializeField] private GameObject _grapplingActionPanel;
    [SerializeField] private UnityEngine.UI.Button _grapplingAttackButton;
    [SerializeField] private UnityEngine.UI.Button _releaseGrappleButton;
    [SerializeField] private UnityEngine.UI.Button _escapeGrappleButton;

    public string HitLocation = null;    // Zmienna do przechowywania wyboru lokacji
    [SerializeField] private GameObject _selectHitLocationPanel;
    [SerializeField] private GameObject _riderOrMountPanel;
    private string _riderOrMount;
    [SerializeField] private UnityEngine.UI.Button _riderButton;
    [SerializeField] private UnityEngine.UI.Button _mountButton;


    [SerializeField] private GameObject _survivalInstinctPanel;
    [SerializeField] private TMP_InputField _survivalInstinctInput;
    [SerializeField] private UnityEngine.UI.Button _survivalInstinctButton;

    [SerializeField] private GameObject _entaglingPanel;
    [SerializeField] private UnityEngine.UI.Button _escapeYesButton;
    [SerializeField] private UnityEngine.UI.Button _escapeNoButton;

    public bool IsManualPlayerAttack;

    private Unit[] _groupOfTargets;
    private bool _groupOfTargetsPenalty;
    private int _groupTargetModifier;
    [SerializeField] private GameObject _groupOfTargetsPanel;
    private Unit _newTargetUnit; //Jeżeli strzał trafi w inną jednostkę z grupy, to zmieniany jest cel ataku.

    // Metoda inicjalizująca słownik ataków
    void Start()
    {
        InitializeAttackTypes();
        UpdateAttackTypeButtonsColor();

        _dodgeButton.onClick.AddListener(() => ParryOrDodgeButtonClick("dodge"));
        _parryButton.onClick.AddListener(() => ParryOrDodgeButtonClick("parry"));

        _riderButton.onClick.AddListener(() => RiderOrMountButtonClick("rider"));
        _mountButton.onClick.AddListener(() => RiderOrMountButtonClick("mount"));

        _grapplingAttackButton.onClick.AddListener(() => GrapplingActionButtonClick("attack"));
        _releaseGrappleButton.onClick.AddListener(() => GrapplingActionButtonClick("release"));
        _escapeGrappleButton.onClick.AddListener(() => GrapplingActionButtonClick("escape"));
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) && _parryAndDodgePanel.activeSelf)
        {
            ParryOrDodgeButtonClick("");
        }

        if (Input.GetKeyDown(KeyCode.Escape) && _riderOrMountPanel.activeSelf)
        {
            RiderOrMountButtonClick("");
        }

        if (Input.GetKeyDown(KeyCode.Escape) && _grapplingActionPanel.activeSelf)
        {
            GrapplingActionButtonClick("");
        }
    }

    #region Attack types
    private void InitializeAttackTypes()
    {
        // Dodajemy typy ataków do słownika
        AttackTypes.Add("StandardAttack", true);
        AttackTypes.Add("Charge", false);
        AttackTypes.Add("MountAttack", false);  // Atak wierzchowca
        AttackTypes.Add("Grappling", false);  // Zapasy
        AttackTypes.Add("Disarm", false);  // Rozbrajanie
    }

    // Metoda ustawiająca dany typ ataku
    public void ChangeAttackType(string attackTypeName = null)
    {
        if (Unit.SelectedUnit == null) return;

        Unit unit = Unit.SelectedUnit.GetComponent<Unit>();
        Stats stats = Unit.SelectedUnit.GetComponent<Stats>();

        if (attackTypeName == null && !unit.Entangled && unit.EntangledUnitId == 0)
        {
            attackTypeName = "StandardAttack";
        }
        else if (attackTypeName == null)
        {
            attackTypeName = "Grappling";
        }

        //Resetuje szarżę lub bieg, jeśli były aktywne
        if (attackTypeName != "Charge" && unit.IsCharging)
        {
            StartCoroutine(MovementManager.Instance.UpdateMovementRange(1));
        }

        if (attackTypeName == "Charge" && (!unit.CanDoAction || !unit.CanMove))
        {
            Debug.Log("Ta jednostka nie może wykonać szarży w obecnej rundzie.");
            return;
        }

        // Sprawdzamy, czy słownik zawiera podany typ ataku
        if (AttackTypes.ContainsKey(attackTypeName))
        {
            // Ustawiamy wszystkie typy ataków na false
            List<string> keysToReset = new List<string>();

            foreach (var key in AttackTypes.Keys)
            {
                if (key != attackTypeName)
                {
                    keysToReset.Add(key);
                }
            }

            foreach (var key in keysToReset)
            {
                AttackTypes[key] = false;
            }

            AttackTypes[attackTypeName] = true;

            //Ograniczenie finty, ogłuszania i rozbrajania do ataków w zwarciu
            if ((AttackTypes["Disarm"] || AttackTypes["Charge"]) == true && unit.GetComponent<Inventory>().EquippedWeapons[0] != null && unit.GetComponent<Inventory>().EquippedWeapons[0].Type.Contains("ranged"))
            {
                AttackTypes[attackTypeName] = false;
                AttackTypes["StandardAttack"] = true;
                Debug.Log("Jednostka walcząca bronią dystansową nie może wykonać tej akcji.");
            }

            // Podczas pochwycenia lub pochwytywania kogoś możemy tylko wykonywac atak typu Zapasy
            if (attackTypeName != "Grappling" && (unit.Grappled || unit.GrappledUnitId != 0))
            {
                AttackTypes[attackTypeName] = false;
                AttackTypes["Grappling"] = true;

                //Debug.Log("Ta jednostka w obecnej rundzie nie może wykonywać innej akcji ataku niż zapasy.");
            }

            if (AttackTypes["Charge"] == true && !unit.IsCharging)
            {
                bool isEngagedInCombat = AdjacentOpponents(unit.transform.position, unit.tag).Any(opponent => opponent.Prone == false && opponent.Unconscious == false);

                if (isEngagedInCombat == true)
                {
                    Debug.Log("Ta jednostka nie może wykonać szarży, bo jest związana walką.");
                    return;
                }

                if (!unit.CanDoAction || !unit.CanMove)
                {
                    Debug.Log("Ta jednostka nie może wykonać szarży w obecnej rundzie.");
                    return;
                }

                StartCoroutine(MovementManager.Instance.UpdateMovementRange(2, null, true));
                MovementManager.Instance.Retreat(false); // Zresetowanie bezpiecznego odwrotu
            }
        }

        UpdateAttackTypeButtonsColor();
    }

    public void UpdateAttackTypeButtonsColor()
    {
        _standardAttackButton.GetComponent<UnityEngine.UI.Image>().color = AttackTypes["StandardAttack"] ? new UnityEngine.Color(0.15f, 1f, 0.45f) : UnityEngine.Color.white;
        _chargeButton.GetComponent<UnityEngine.UI.Image>().color = AttackTypes["Charge"] ? new UnityEngine.Color(0.15f, 1f, 0.45f) : UnityEngine.Color.white;
        _mountAttackButton.GetComponent<UnityEngine.UI.Image>().color = AttackTypes["MountAttack"] ? new UnityEngine.Color(0.15f, 1f, 0.45f) : UnityEngine.Color.white;
        _grapplingButton.GetComponent<UnityEngine.UI.Image>().color = AttackTypes["Grappling"] ? new UnityEngine.Color(0.15f, 1f, 0.45f) : UnityEngine.Color.white;
        _disarmButton.GetComponent<UnityEngine.UI.Image>().color = AttackTypes["Disarm"] ? new UnityEngine.Color(0.15f, 1f, 0.45f) : UnityEngine.Color.white;
        
        SetActionsButtonsInteractable();
    }

    public void SetActionsButtonsInteractable()
    {
        if (Unit.SelectedUnit == null) return;
        _reloadButton.interactable = Unit.SelectedUnit.GetComponent<Inventory>().EquippedWeapons.Any(weapon => weapon != null && weapon.ReloadLeft > 0);
        _grapplingButton.gameObject.SetActive(!Unit.SelectedUnit.GetComponent<Unit>().IsMounted);
        _mountAttackButton.gameObject.SetActive(Unit.SelectedUnit.GetComponent<Unit>().IsMounted);
    }
    #endregion

    #region Attack functions
    public void Attack(Unit attacker, Unit target, bool opportunityAttack = false)
    {
        // Sprawdź, czy gra jest wstrzymana
        if (GameManager.IsGamePaused)
        {
            Debug.Log("Gra została wstrzymana. Aby ją wznowić musisz wyłączyć okno znajdujące się na polu gry.");
            return;
        }

        if (AttackTypes["MountAttack"]) // Atak wierzchowca
        {
            attacker = attacker.Mount;
        }

        // Sprawdź, czy jednostka może wykonać atak
        if (!attacker.CanDoAction && !opportunityAttack)
        {
            Debug.Log("Wybrana jednostka nie może wykonać ataku w tej rundzie.");
            return;
        }

        if (opportunityAttack) ChangeAttackType();

        StartCoroutine(AttackCoroutine(attacker, target, opportunityAttack));
    }
    private IEnumerator AttackCoroutine(Unit attacker, Unit target, bool opportunityAttack = false)
    {
        // Czekaj aż użytkownik wybierze lokację trafienia (jeśli panel wyboru lokacji jest otwarty)
        while (_selectHitLocationPanel.activeSelf)
        {
            yield return null; // Czekaj na następną ramkę
        }

        // 1) Oblicz dystans między walczącymi
        float attackDistance = CalculateDistance(attacker.gameObject, target.gameObject);

        // 2) Pobierz statystyki i broń
        Stats attackerStats = attacker.Stats;
        Stats targetStats = target.Stats;

        Weapon attackerWeapon = null;
        Weapon targetWeapon = null;
        if (AttackTypes["Grappling"]) // Zapasy
        {
            attackerStats.GetComponent<Weapon>().ResetWeapon();
            attackerWeapon = attackerStats.GetComponent<Weapon>();

            targetStats.GetComponent<Weapon>().ResetWeapon();
            targetWeapon = targetStats.GetComponent<Weapon>();

            if (attacker.GrappledUnitId != 0 && attacker.GrappledUnitId != target.UnitId)
            {
                Debug.Log("Celem ataku musi być jednostka, z którą toczą się zapasy.");
                yield break;
            }
        }
        else // Zwykły atak bronią
        {
            attackerWeapon = InventoryManager.Instance.ChooseWeaponToAttack(attacker.gameObject);
            targetWeapon = InventoryManager.Instance.ChooseWeaponToAttack(target.gameObject);

            // Uwzględniamy typ amunicji
            if (attackerWeapon.Type.Contains("ranged") && !attackerWeapon.Type.Contains("throwing"))
            {
                if (string.IsNullOrEmpty(attackerWeapon.AmmoType) || attackerWeapon.AmmoType == "Brak")
                {
                    Debug.Log("Do ataku bronią dystansową niezbędne jest wybranie typu amunicji. Możesz to zrobić w panelu ekwipunku.");
                    yield break;
                }
            }
        }

        bool isMeleeAttack = attackerWeapon.Type.Contains("melee");
        bool isRangedAttack = !isMeleeAttack && attackerWeapon.Type.Contains("ranged");

        if(isRangedAttack && attacker.Blinded)
        {
            Debug.Log("Jednostki będące w stanie Oślepienia nie mogą wykonywać ataków dystansowych.");
            yield break;
        }

        // 3) Sprawdź zasięg i ewentualnie wykonaj szarżę
        bool isOutOfRange = attackDistance > attackerWeapon.AttackRange;

        if (isOutOfRange)
        {
            // Poza zasięgiem
            if (attacker.IsCharging)
            {
                // Jeżeli to miała być szarża, próbujemy ją wykonać
                Charge(attacker.gameObject, target.gameObject);
            }
            else
            {
                Debug.Log("Cel jest poza zasięgiem ataku.");
            }
            yield break;
        }

        // 4) Sprawdzenie dodatkowych warunków dla ataku dystansowego (np. przeszkody, czy broń jest naładowana, itp.)
        if (isRangedAttack)
        {
            bool validRanged = ValidateRangedAttack(attacker, target, attackerWeapon, attackDistance);
            if (!validRanged) yield break;

            // ==================================================================
            //DODAĆ MODYFIKATORY ZA PRZESZKODY NA LINII STRZAŁU
            // ==================================================================
        }

        // 5) Określamy, czy atak jest manualny czy automatyczny
        IsManualPlayerAttack = attacker.CompareTag("PlayerUnit") && GameManager.IsAutoDiceRollingMode == false;

        // 6) Jeśli to nie atak okazyjny – zużywamy akcję
        if (!opportunityAttack)
        {
            if (attacker.CanDoAction)
            {
                RoundsManager.Instance.DoAction(attacker);
            }
            else
            {
                Debug.Log("Wybrana jednostka nie może wykonać kolejnego ataku w tej rundzie.");
                yield break;
            }
        }

        // Rozbrojenie
        if (AttackTypes["Disarm"])
        {
            StartCoroutine(Disarm(attackerStats, targetStats, attackerWeapon, targetWeapon));
            yield break;
        }

        // ==================================================================
        // 7) *** RZUT ATAKU *** (manualny lub automatyczny)
        // ==================================================================

        // Sprawdzenie, czy strzelamy do pojedynczego wroga, czy do grupy
        if (isRangedAttack)
        {
            _groupOfTargets = GetAdjacentUnits(targetStats.transform.position);

            if (_groupOfTargets.Length > 1 && !GameManager.IsAutoCombatMode)
            {
                _groupOfTargetsPanel.SetActive(true);

                // Najpierw czekamy, aż gracz kliknie którykolwiek przycisk
                yield return new WaitUntil(() => !_groupOfTargetsPanel.activeSelf);
            }
        }

        int attackModifier = CalculateAttackModifier(attacker, attackerWeapon, target, attackDistance);
        Debug.Log($"{attackerStats.Name} atakuje przy użyciu {attackerWeapon.Name}.");

        // Jeżeli w wyniku ataku dystansowego w grupę została trafiona przypadkowo inna jednostka, aktualizujemy cel
        if(_newTargetUnit != null)
        {
            target = _newTargetUnit;
            targetStats = target.Stats;
            targetWeapon = InventoryManager.Instance.ChooseWeaponToAttack(target.gameObject);
            _newTargetUnit = null;
        }

        // POMYŚLEĆ, CZY TO WPROWADZIĆ
        //// Modyfikator do trafienia, za wybór konkretnej lokacji
        //if (HitLocation != null && HitLocation.Length > 0 && !((attackerWeapon.Pummel || attackerWeapon.Id == 4) && attackerStats.StrikeToStun > 0))
        //{
        //    attackModifier -= 20;
        //    Debug.Log("Modyfikator -20 do trafienia za wybór konkretnej lokalizacji");
        //}

        //Zresetowanie celowania, jeżeli było aktywne
        if (attacker.AimingBonus != 0)
        {
            attacker.AimingBonus = 0;
            UpdateAimButtonColor();
        }

        int attackRollResult = 0;
        string skillName = isRangedAttack ? "RangedCombat" : "MeleeCombat";

        int[] attackTest = null;
        if (IsManualPlayerAttack)
        {
            // Ręczne wpisanie 2–3 kości i natychmiastowy TestSkill po submit
            yield return StartCoroutine(DiceRollManager.Instance.WaitForRollValue(attackerStats, "trafienie", "Zr", skillName, attackModifier, callback: res => attackTest = res));
            if (attackTest == null) yield break;
        }
        else
        {
            // Auto – TestSkill sam wylosuje kości
            attackTest = DiceRollManager.Instance.TestSkill(attackerStats, "trafienie", "Zr", skillName, attackModifier);
        }
        attackRollResult = attackTest[3];


        // ============== TALENTY ZABÓJCA, WOJOWNIK I STRZELEC WYBOROWY =================
        //
        // Podwojenie niższej k10: wybierz ŹRÓDŁO (Slayer > CombatMaster/Scharpshooter)
        bool hasSharpshooterOrCombatMaster =
            (!isRangedAttack && attackerStats.CombatMaster) ||
            (isRangedAttack && attackerStats.Sharpshooter);

        // Sprawdzenie Slayera — dopasowanie typu celu (case-insensitive), z tłumaczeniem PL
        bool hasSlayerMatch = false;
        string matchedTypePl = null;

        if (attackerStats.Slayer != null && targetStats != null && !string.IsNullOrEmpty(targetStats.Type))
        {
            foreach (var t in attackerStats.Slayer)
            {
                if (string.IsNullOrEmpty(t)) continue;
                if (string.Equals(t, targetStats.Type, System.StringComparison.OrdinalIgnoreCase))
                {
                    hasSlayerMatch = true;
                    matchedTypePl = UnitsManager.SlayerEnToPl.TryGetValue(t, out var pl) ? pl : t;
                    break;
                }
            }
        }

        // Priorytet: Slayer > Wojownik/Strzelec Wyborowy (nie sumują się)
        bool shouldDoubleLowerDice = hasSlayerMatch || (!hasSlayerMatch && hasSharpshooterOrCombatMaster);

        if (shouldDoubleLowerDice)
        {
            int lowerIndex = attackTest[0] <= attackTest[1] ? 0 : 1;
            attackRollResult += attackTest[lowerIndex]; // dodaj niższą kość drugi raz = podwojenie

            if (hasSlayerMatch)
            {
                Debug.Log($"{attackerStats.Name} korzysta z talentu Zabójca ({matchedTypePl}). " +
                          $"Wartość niższej kości k10 zostaje podwojona z <color=#4dd2ff>{attackTest[lowerIndex]}</color> " +
                          $"na <color=#4dd2ff>{attackTest[lowerIndex] * 2}</color>. Nowy łączny wynik: <color=green>{attackRollResult}</color>.");
            }
            else
            {
                string talentName = isRangedAttack ? "Strzelec Wyborowy" : "Wojownik";
                Debug.Log($"{attackerStats.Name} korzysta z talentu {talentName}. " +
                          $"Wartość niższej kości k10 zostaje podwojona z <color=#4dd2ff>{attackTest[lowerIndex]}</color> " +
                          $"na <color=#4dd2ff>{attackTest[lowerIndex] * 2}</color>. Nowy łączny wynik: <color=green>{attackRollResult}</color>.");
            }
        }

        // --- Ustalamy miejsce trafienia ---
        int roll1 = attackTest[0];
        int roll2 = attackTest[1];
        int chosenValue;

        // domyślnie bierzemy niższą z kości
        chosenValue = roll1 > roll2 ? roll2 : roll1;

        if ((isRangedAttack && attackerStats.AccurateShot) ||      // Celny Strzał
            (!isRangedAttack && attackerStats.Fencing))             // Szermierka
        {
            chosenValue = roll1 > roll2 ? roll1 : roll2;
            Debug.Log($"{attackerStats.Name} używa talentu {(isRangedAttack ? "Celny Strzał" : "Szermierka")} i wybiera wyższą kość ({chosenValue}) do ustalenia lokacji trafienia.");
        }

        string hitLocation = !string.IsNullOrEmpty(HitLocation) ? HitLocation : null;

        if (string.IsNullOrEmpty(hitLocation))
        {
            // uruchom korutynę wyboru lokacji; wynik przyjdzie w callbacku
            yield return StartCoroutine(DetermineHitLocationCoroutine(
                chosenValue,
                targetStats,
                location => hitLocation = location
            ));
        }

        if (!String.IsNullOrEmpty(HitLocation))
        {
            Debug.Log($"Atak jest skierowany w {TranslateHitLocation(hitLocation)}.");
            HitLocation = null;
        }

        // Jeśli to była broń dystansowa – resetujemy ładowanie
        if (isRangedAttack)
        {
            ResetWeaponLoad(attackerWeapon, attackerStats);
        }

        // ==================================================================
        // 9) *** OBRONA ***
        // ==================================================================
        int parryValue = 0;
        int dodgeValue = 0;
        bool canParry = false;

        //if (AttackTypes["Grappling"]) // Zapasy
        //{
        //    // Jeżeli jesteśmy w trybie manualnych rzutów kośćmi
        //    if (!GameManager.IsAutoDiceRollingMode && target.CompareTag("PlayerUnit"))
        //    {
        //        yield return StartCoroutine(DiceRollManager.Instance.WaitForRollValue(targetStats, "siłę", result => defenceRollResult = result));
        //        if (defenceRollResult == 0) yield break;
        //    }
        //    else
        //    {
        //        defenceRollResult = UnityEngine.Random.Range(1, 101);
        //    }

        //    //int modifier = target.Entangled > 0 ? 10 : 0;
        //    defenceRollResult = DiceRollManager.Instance.TestSkill("S", targetStats, null, defenceRollResult);
        //}
        //else if
        
        // Zwykły atak bronią

        // Sprawdzenie, czy jednostka może próbować parować lub unikać ataku
        Inventory inventory = target.GetComponent<Inventory>();
        bool hasMeleeWeapon = inventory.EquippedWeapons.Any(weapon => weapon != null && weapon.Type.Contains("melee"));
        bool hasShield = inventory.EquippedWeapons.Any(weapon => weapon != null && weapon.Type.Contains("shield"));
        bool bothUnarmed = attackerWeapon.Id == 0 && targetWeapon.Id == 0;

        canParry = (isMeleeAttack && hasMeleeWeapon) || hasShield;

        Weapon weaponUsedForParry = null;
        int parryModifier = 0;
        int dodgeModifier = CalculateDodgeModifier(target, attacker);

        if (canParry)
        {
            weaponUsedForParry = GetBestParryWeapon(targetStats, targetWeapon);
            parryModifier = CalculateParryModifier(target, targetStats, attackerStats, weaponUsedForParry);
        }

        string parryModifierString = parryModifier != 0 ? $" Modyfikator: {parryModifier}," : "";
        string dodgeModifierString = dodgeModifier != 0 ? $" Modyfikator: {dodgeModifier}," : "";

        // Obliczamy sumaryczną wartość parowania i uniku
        parryValue = targetStats.Zr + parryModifier;
        dodgeValue = targetStats.Zw + dodgeModifier;

        // Funkcja obrony
        yield return StartCoroutine(Defense(target, targetStats, weaponUsedForParry, parryValue, dodgeValue, parryModifier, dodgeModifier, canParry));


        //W przypadku manualnego ataku sprawdzamy, czy postać powinna zakończyć turę
        if (IsManualPlayerAttack && !attacker.CanMove && !attacker.CanDoAction)
        {
            RoundsManager.Instance.FinishTurn();
        }

        // 10) Rozstrzygnięcie trafienia

        bool attackSucceeded = attackRollResult >= DefenceResults[3];

        // Sprawdzamy Szczęście i Pecha

        if (DiceRollManager.Instance.IsDoubleDigit(attackTest[0], attackTest[1]))
        {
            if (attackSucceeded)
            {
                Debug.Log($"{attackerStats.Name} wyrzucił <color=green>SZCZĘŚCIE</color>!");
                attackerStats.FortunateEvents++;

                StartCoroutine(CriticalWoundRoll(attackerStats, targetStats, hitLocation));
            }
            else
            {
                Debug.Log($"{attackerStats.Name} wyrzucił <color=red>PECHA</color>!");
                attackerStats.UnfortunateEvents++;
            }
        }

        if (DiceRollManager.Instance.IsDoubleDigit(DefenceResults[0], DefenceResults[1]))
        {
            if (!attackSucceeded)
            {
                Debug.Log($"{targetStats.Name} wyrzucił <color=green>SZCZĘŚCIE</color>!");
                targetStats.FortunateEvents++;

                // domyślnie bierzemy niższą z kości
                int parryLowerValue = DefenceResults[0] > DefenceResults[1] ? DefenceResults[1] : DefenceResults[0];

                yield return StartCoroutine(DetermineHitLocationCoroutine(parryLowerValue, attackerStats, location => hitLocation = location));

                StartCoroutine(CriticalWoundRoll(targetStats, attackerStats, hitLocation));
            }
            else
            {
                Debug.Log($"{targetStats.Name} wyrzucił <color=red>PECHA</color>!");
                targetStats.UnfortunateEvents++;
            }
        }

        //Resetujemy czas przeładowania broni celu ataku, bo ładowanie zostało zakłócone przez atak, przed którym musi się bronić. W przypadku chybienia z broni dystansowej, nie przeszkadza to w ładowaniu
        if (targetWeapon.ReloadLeft != 0 && (attackSucceeded || isMeleeAttack))
        {
            ResetWeaponLoad(targetWeapon, targetStats);
        }


        // Atak chybił, przerywamy funkcję
        if (attackSucceeded == false)
        {
            Debug.Log($"Atak {attackerStats.Name} chybił.");
            StartCoroutine(AnimationManager.Instance.PlayAnimation("miss", null, target.gameObject));
            ChangeAttackType(); // Resetuje typ ataku

            yield break;
        }

        //// Sprawdzenie, czy atak ogłuszy przeciwnika
        //if (hitLocation == "head" && (attackerWeapon.Pummel || (attackerWeapon.Id == 4 && attackerStats.StrikeToStun > 0)))
        //{
        //    StartCoroutine(Stun(attackerStats, targetStats));
        //}

        // ========= UNIERUCHOMIENIE ==========

        bool isThrowing = attackerWeapon.Type != null && attackerWeapon.Type.Contains("throwing");
        if (attackerWeapon.Entangle && attacker.EntangledUnitId != target.UnitId && !target.Entangled)
        {
            target.Entangled = true;
            target.CanMove = false;

            Debug.Log($"{attackerStats.Name} unieruchomił/a {targetStats.Name} przy użyciu {attackerWeapon.Name}.");

            if (!isThrowing)
            {
                attacker.CanMove = false;
                attacker.EntangledUnitId = target.UnitId;
                MovementManager.Instance.SetCanMoveToggle(false);
            }
        }

        if (attackerWeapon.Type.Contains("no-damage")) yield break; //Jeśli broń nie powoduje obrażeń, np. sieć, to pomijamy dalszą część kodu

        // 11) OBLICZENIE OBRAŻEŃ

        int armor = CalculateArmor(targetStats, hitLocation, attackerWeapon);
        int damage = CalculateDamage(attackRollResult - DefenceResults[3], attackerStats, attackerWeapon);
        //.Log($"{attackerStats.Name} zadaje {damage} obrażeń.");

        // 12) ZADANIE OBRAŻEŃ

        // Lista jednostek, które otrzymają obrażenia
        HashSet<Unit> affectedUnits = new HashSet<Unit> { target }; // Dodajemy target od razu

        ApplyDamageToTarget(damage, armor, attackerStats, targetStats, target, attackerWeapon);

        // 13) ANIMACJA ATAKU I OBSŁUGA ŚMIERCI

        StartCoroutine(AnimationManager.Instance.PlayAnimation("attack", attacker.gameObject, target.gameObject));

        if (targetStats.TempHealth < 0)
        {
            if (GameManager.IsAutoKillMode)
            {
                HandleDeath(targetStats, target.gameObject, attackerStats);
            }
            else
            {
                StartCoroutine(CriticalWoundRoll(attackerStats, targetStats, hitLocation));
            }
        }

        ChangeAttackType(); // Resetuje typ ataku
    }

    public void ApplyDamageToTarget(int damage, int armor, Stats attackerStats, Stats targetStats, Unit target, Weapon attackerWeapon = null, string damageType = "Physical")
    {
        int finalDamage = 0;

        if (damage > armor)
        {
            finalDamage = damage - armor;
        }
        else
        {
            // Jeśli atak nie przebił pancerza, ale broń NIE JEST tępa, to broń zadaje 1 obrażeń
            if (attackerWeapon != null)
            {
                finalDamage = 1;
            }
        }

        if (finalDamage > 0)
        {
            // --- ODPORNOŚCI (Resistance) — pełna odporność: 0 dmg ---
            // Zasada: jeśli to atak bronią i broń NIE jest magiczna → Physical.
            // Jeśli broń jest Magical → NIE traktujemy jako Physical (brak typu).
            if (targetStats.Resistance != null && targetStats.Resistance.Length > 0)
            {
                string effectiveDamageType = !string.IsNullOrEmpty(damageType) ? damageType : null;

                if (attackerWeapon != null && attackerWeapon.Magical)
                {
                    effectiveDamageType = null;
                }

                // Jeśli nie mamy typu (np. magiczna broń bez typu) – odporności nie mają do czego się odnieść
                if (!string.IsNullOrEmpty(effectiveDamageType))
                {
                    bool immune = false;
                    foreach (var r in targetStats.Resistance)
                    {
                        if (string.IsNullOrWhiteSpace(r)) continue;
                        var rEn = UnitsManager.ResistancePlToEn.TryGetValue(r, out var map) ? map : r; // znormalizuj do EN
                        if (string.Equals(rEn, damageType, StringComparison.OrdinalIgnoreCase)) { immune = true; break; }
                    }

                    if (immune)
                    {
                        var dmgPl = UnitsManager.ResistanceEnToPl.TryGetValue(damageType, out var pl) ? pl : damageType;
                        Debug.Log($"{targetStats.Name} jest odporny/a na {dmgPl}. Obrażenia zostały zignorowane.");

                        StartCoroutine(AnimationManager.Instance.PlayAnimation("parry", null, target.gameObject));
                        return;
                    }
                }
            }

            targetStats.TempHealth -= finalDamage;

            if (armor != 0)
            {
                Debug.Log($"{targetStats.Name} znegował {armor} obrażeń.");
            }

            //Informacja o punktach żywotności po zadaniu obrażeń
            if (!GameManager.IsHealthPointsHidingMode || target.CompareTag("PlayerUnit"))
            {
                if (targetStats.TempHealth < 0)
                {
                    Debug.Log($"Punkty żywotności {targetStats.Name}: <color=red>{targetStats.TempHealth}</color><color=#4dd2ff>/{targetStats.MaxHealth}</color>");
                    target.Prone = true; // Powalenie
                }
                else
                {
                    Debug.Log($"Punkty żywotności {targetStats.Name}: <color=#4dd2ff>{targetStats.TempHealth}/{targetStats.MaxHealth}</color>");
                }
            }
            else if (targetStats.TempHealth >= 0)
            {
                Debug.Log($"{targetStats.Name} został/a zraniony/a.");
            }

            target.DisplayUnitHealthPoints();

            if ((targetStats.TempHealth < 0 && GameManager.IsHealthPointsHidingMode) || (targetStats.TempHealth < 0 && targetStats.gameObject.CompareTag("EnemyUnit") && GameManager.IsStatsHidingMode))
            {
                Debug.Log($"Żywotność {targetStats.Name} spadła poniżej zera i wynosi <color=red>{targetStats.TempHealth}</color>.");
                target.Prone = true; // Powalenie
            }

            target.LastAttackerStats = attackerStats;

            // Aktualizuje żywotność w panelu jednostki, jeśli dostała obrażenia w wyniku ataku okazyjnego
            if (Unit.SelectedUnit == target.gameObject)
            {
                UnitsManager.Instance.UpdateUnitPanel(target.gameObject);
            }

            StartCoroutine(AnimationManager.Instance.PlayAnimation("damage", null, target.gameObject, finalDamage));

            // Uwzględnienie cechy broni "Zatruta"
            if (attackerWeapon != null && attackerWeapon.Poisonous > 0)
            {
                // sprawdzamy odporności celu
                bool immuneToPoison = (targetStats.Resistance != null && targetStats.Resistance.Contains("Poison")) || targetStats.Undead;

                if (!immuneToPoison && target.Poison < attackerWeapon.Poisonous)
                {
                    target.Poison = attackerWeapon.Poisonous;
                    Debug.Log($"<color=#FF7F50>{targetStats.Name} dostaje stan Zatrucia (poziom {target.Poison}).</color>");
                }
                else if (immuneToPoison)
                {
                    Debug.Log($"<color=#FF7F50>{targetStats.Name} jest odporny na zatrucie.</color>");
                }
            }
        }
        else
        {
            Debug.Log($"Atak nie przebił pancerza {targetStats.Name} i nie zadał obrażeń.");
            StartCoroutine(AnimationManager.Instance.PlayAnimation("parry", null, target.gameObject));
        }

        // Zaktualizowanie osiągnięć
        attackerStats.TotalDamageDealt += finalDamage;
        if (attackerStats.HighestDamageDealt < finalDamage) attackerStats.HighestDamageDealt = finalDamage;
        targetStats.TotalDamageTaken += finalDamage;
        if (targetStats.HighestDamageTaken < finalDamage) targetStats.HighestDamageTaken = finalDamage;
    }

    public void HandleDeath(Stats targetStats, GameObject target, Stats attackerStats = null)
    {
        // Zapobiega usunięciu postaci graczy, gdy statystyki przeciwników są ukryte
        if (GameManager.IsStatsHidingMode && targetStats.gameObject.CompareTag("PlayerUnit"))
        {
            return;
        }

        // Usuwanie jednostki
        UnitsManager.Instance.DestroyUnit(target);

        if (attackerStats == null && Unit.SelectedUnit != null)
        {
            attackerStats = Unit.SelectedUnit.GetComponent<Stats>();
        }
        else if(attackerStats == null) return;

        StartCoroutine(AnimationManager.Instance.PlayAnimation("kill", attackerStats.gameObject, target));

        // Aktualizacja podświetlenia pól w zasięgu ruchu atakującego
        GridManager.Instance.HighlightTilesInMovementRange(attackerStats);
    }
    #endregion

    #region Defense functions
    public IEnumerator Defense(Unit target, Stats targetStats, Weapon targetWeapon, int parryValue, int dodgeValue, int parryModifier, int dodgeModifier, bool canParry)
    {
        // Jak cel jest nieprzytomny to wynik obrony wynosi 0.
        if(target.Unconscious)
        {
            Debug.Log($"{targetStats.Name} jest nieprzytomny i nie może się bronić.");
            DefenceResults = new int[] { 0, 1, 0, 0 };
            yield break;
        }

        if (!GameManager.IsAutoDefenseMode)
        {
            yield return StartCoroutine(ManualDefense(target, targetStats, targetWeapon, parryValue, dodgeValue, parryModifier, dodgeModifier, canParry));
        }
        else
        {
            yield return StartCoroutine(AutoDefense(target, targetStats, targetWeapon, parryValue, dodgeValue, parryModifier, dodgeModifier, canParry));
        }

        yield return null;
    }
    public Weapon GetBestParryWeapon(Stats targetStats, Weapon defaultWeapon)
    {
        return targetStats.GetComponent<Inventory>().EquippedWeapons
            .Where(w => w != null)
            .OrderByDescending(w => w.Defensive)
            .FirstOrDefault() ?? defaultWeapon;
    }

    public int CalculateParryModifier(Unit target, Stats targetStats, Stats attackerStats, Weapon weaponUsedForParry)
    {
        int modifier = 0;
        if (weaponUsedForParry.Defensive > 0) modifier += weaponUsedForParry.Defensive;

        if (attackerStats.Size > targetStats.Size) modifier -= (attackerStats.Size - targetStats.Size) * 2; // Kara do parowania za rozmiar
        if (target.Blinded) modifier -= 5;
        if (target.Prone) modifier -= 5;
        if (target.Entangled && attackerStats.GetComponent<Unit>().EntangledUnitId != target.UnitId) modifier -= 5;

        return modifier;
    }

    public int CalculateDodgeModifier(Unit target, Unit attacker)
    {
        int modifier = 0;

        if(target.Blinded) modifier -= 5;
        if(target.Prone) modifier -= 5;
        if(target.Entangled && attacker.EntangledUnitId != target.UnitId) modifier -= 5;

        return modifier;
    }

    private IEnumerator ManualDefense(Unit target, Stats targetStats, Weapon targetWeapon, int parryValue, int dodgeValue, int parryModifier, int dodgeModifier, bool canParry)
    {
        _parryAndDodgePanel.SetActive(true);
        _parryAndDodgePanel.GetComponentInChildren<TMP_Text>().text = "Wybierz reakcję atakowanej postaci.";
        _parryOrDodge = ""; // Resetujemy wybór reakcji obronnej
        _parryButton.gameObject.SetActive(canParry);
        _dodgeButton.gameObject.SetActive(true);

        // Najpierw czekamy, aż gracz kliknie którykolwiek przycisk
        yield return new WaitUntil(() => !_parryAndDodgePanel.activeSelf);

        if (_parryOrDodge == "parry")
        {
            yield return StartCoroutine(Parry(target, targetStats, targetWeapon, parryValue, parryModifier));
        }
        else if (_parryOrDodge == "dodge")
        {
            yield return StartCoroutine(Dodge(target, targetStats, dodgeValue, dodgeModifier));
        }
    }

    private IEnumerator AutoDefense(Unit target, Stats targetStats, Weapon targetWeapon, int parryValue, int dodgeValue, int parryModifier, int dodgeModifier, bool canParry)
    {
        if (parryValue + parryModifier >= dodgeValue + dodgeModifier && canParry)
        {
            yield return StartCoroutine(Parry(target, targetStats, targetWeapon, parryValue, parryModifier));
        }
        else
        {
            yield return StartCoroutine(Dodge(target, targetStats, dodgeValue, dodgeModifier));
        }
    }

    private IEnumerator Parry(Unit target, Stats targetStats, Weapon targetWeapon, int parryValue, int parryModifier)
    {
        Debug.Log($"{targetStats.Name} próbuje parować przy użyciu {targetWeapon.Name}.");

        // Jeżeli jesteśmy w trybie manualnych rzutów kośćmi i wybrana jednostka to sojusznik to czekamy na wynik rzutu
        int[] defenceTest = null;
        if (!GameManager.IsAutoDiceRollingMode && target.CompareTag("PlayerUnit"))
        {
            yield return StartCoroutine(DiceRollManager.Instance.WaitForRollValue(targetStats, "parowanie", "Zr", "MeleeCombat", targetWeapon.Defensive + parryModifier, callback: result => defenceTest = result));
            if (defenceTest == null) yield break;
        }
        else
        {
            defenceTest = DiceRollManager.Instance.TestSkill(targetStats, "parowanie", "Zr", "MeleeCombat", targetWeapon.Defensive + parryModifier);
        }

        DefenceResults = defenceTest;
    }

    public IEnumerator Dodge(Unit target, Stats targetStats, int dodgeValue, int dodgeModifier)
    {
        // Jeżeli jesteśmy w trybie manualnych rzutów kośćmi i wybrana jednostka to sojusznik to czekamy na wynik rzutu
        int[] defenceTest = null;
        if (!GameManager.IsAutoDiceRollingMode && target.CompareTag("PlayerUnit"))
        {
            yield return StartCoroutine(DiceRollManager.Instance.WaitForRollValue(targetStats, "unik", "Zw", "Dodge", dodgeModifier, callback: result => defenceTest = result));
            if (defenceTest == null) yield break;
        }
        else
        {
            defenceTest = DiceRollManager.Instance.TestSkill(targetStats, "unik", "Zw", "Dodge", dodgeModifier);
        }

        DefenceResults = defenceTest;
    }
    public void ParryOrDodgeButtonClick(string parryOrDodge)
    {
        _parryOrDodge = parryOrDodge;
    }
    #endregion

    #region Calculating distance and validating distance attack
    public float CalculateDistance(GameObject attacker, GameObject target)
    {
        if (attacker != null && target != null)
        {
            return Vector2.Distance(attacker.transform.position, target.transform.position);
        }
        else
        {
            Debug.LogError("Nie udało się ustalić odległości pomiędzy jednostkami.");
            return 0;
        }
    }

    public bool ValidateRangedAttack(Unit attacker, Unit target, Weapon attackerWeapon, float attackDistance)
    {
        // Sprawdza, czy broń jest naładowana
        if (attackerWeapon.ReloadLeft != 0)
        {
            Debug.Log($"Broń {attacker.GetComponent<Stats>().Name} wymaga przeładowania.");
            return false;
        }

        // Sprawdza, czy cel nie znajduje się zbyt blisko
        if (attackDistance <= 1.5f)
        {
            Debug.Log($"{attacker.GetComponent<Stats>().Name} stoi zbyt blisko celu, aby wykonać atak dystansowy.");
            return false;
        }

        // Sprawdza, czy na linii strzału znajduje się przeszkoda
        RaycastHit2D[] raycastHits = Physics2D.RaycastAll(attacker.transform.position, target.transform.position - attacker.transform.position, attackDistance);

        foreach (var raycastHit in raycastHits)
        {
            if (raycastHit.collider == null) continue;

            var mapElement = raycastHit.collider.GetComponent<MapElement>();

            if (mapElement != null)
            {
                if (mapElement.IsHighObstacle)
                {
                    Debug.Log($"Na linii strzału {attacker.GetComponent<Stats>().Name} znajduje się przeszkoda, przez którą strzał jest niemożliwy.");
                    return false;
                }
            }
        }

        return true;
    }

    #endregion

    #region Calculate attack modifier
    //Oblicza modyfikator do trafienia
    public int CalculateAttackModifier(Unit attackerUnit, Weapon attackerWeapon, Unit targetUnit, float attackDistance = 0)
    {
        if(attackerUnit == null) return 0;
        int attackModifier = 0;
        //int attackModifier = DiceRollManager.Instance.RollModifier;
        //DiceRollManager.Instance.ResetRollModifier();
        Stats attackerStats = attackerUnit.GetComponent<Stats>();
        Stats targetStats = targetUnit.GetComponent<Stats>();

        // Modyfikator za celowanie
        attackModifier += attackerUnit.AimingBonus;
        if (attackerUnit.AimingBonus > 0) Debug.Log($"Uwzględniono modyfikator +{attackerUnit.AimingBonus} za celowanie. Łączny modyfikator: " + attackModifier);
        else if (attackerUnit.AimingBonus < 0) Debug.Log($"Uwzględniono modyfikator {attackerUnit.AimingBonus} za celowanie. Łączny modyfikator: " + attackModifier);

        // Modyfikator za szarżę
        if (attackerUnit.IsCharging && attackerStats.S > 0)
        {
            attackModifier += attackerStats.S;
            Debug.Log($"Uwzględniono modyfikator +{attackerStats.S} za szarżę. Łączny modyfikator: " + attackModifier);
        }
     
        // Utrudnienie za atak słabszą ręką
        if (attackerUnit.GetComponent<Inventory>().EquippedWeapons[0] == null || attackerWeapon.Name != attackerUnit.GetComponent<Inventory>().EquippedWeapons[0].Name)
        {
            if (attackerWeapon.Id != 0)
            {
                attackModifier -= 3;
                Debug.Log($"Uwzględniono modyfikator -3 za atak słabszą ręką. Łączny modyfikator: " + attackModifier);
            }
        }

        //// Modyfikatory za jakość broni
        //if (attackerWeapon.Quality == "Kiepska") attackModifier -= 5;
        //else if (attackerWeapon.Quality == "Najlepsza" || attackerWeapon.Quality == "Magiczna") attackModifier += 5;

        //Debug.Log($"Uwzględniono modyfikator za jakość broni. Łączny modyfikator: " + attackModifier);


        //MODYFIKATORY ZA STANY
        if (attackerUnit.Prone)
        {
            attackModifier -= 5;
            Debug.Log($"Uwzględniono modyfikator -5 za Powalenie. Łączny modyfikator: " + attackModifier);
        }
        if (attackerUnit.Blinded)
        {
            attackModifier -= 5;
            Debug.Log($"Uwzględniono modyfikator -5 za Oślepienie. Łączny modyfikator: " + attackModifier);
        }

        // Przewaga liczebna
        int adjacentEnemies;
        int outNumber = CountOutnumber(attackerUnit, targetUnit, out adjacentEnemies);

        // Tylko w Walce Wręcz
        if (attackerWeapon.Type.Contains("melee"))
        {
            // Przewaga liczebna
            attackModifier += outNumber;

            if(outNumber != 0)
            {
                Debug.Log($"Uwzględniono modyfikator +{outNumber} za przewagę liczebną. Łączny modyfikator: {attackModifier}");
            }
        }

        // Tylko w Walce Dystansowej
        if (attackerWeapon.Type.Contains("ranged"))
        {
            // Modyfikator za różnicę rozmiarów
            if (attackerStats.Size < targetStats.Size)
            {
                attackModifier += (targetStats.Size - attackerStats.Size) * 2;
                Debug.Log($"Uwzględniono modyfikator +{(targetStats.Size - attackerStats.Size) * 2} za różnicę rozmiarów. Łączny modyfikator: " + attackModifier);
            }

            // Sprawdza, czy na linii strzału znajduje się przeszkoda
            RaycastHit2D[] raycastHits = Physics2D.RaycastAll(attackerUnit.transform.position, targetUnit.transform.position - attackerUnit.transform.position, attackDistance);

            foreach (var raycastHit in raycastHits)
            {
                if (raycastHit.collider == null) continue;

                var mapElement = raycastHit.collider.GetComponent<MapElement>();
                var unit = raycastHit.collider.GetComponent<Unit>();

                if (mapElement != null && mapElement.IsLowObstacle)
                {
                    attackModifier -= 5;
                    Debug.Log($"Strzał jest wykonywany w jednostkę znajdującą się za przeszkodą. Zastosowano modyfikator -5 do trafienia. Łączny modyfikator: " + attackModifier);
                    break; // Żeby modyfikator nie kumulował się za każdą przeszkodę
                }

                if (unit != null && unit != targetUnit && unit != attackerUnit && !_groupOfTargets.Contains(unit))
                {
                    attackModifier -= 5;
                    Debug.Log("Na linii strzału znajduje się inna jednostka. Zastosowano modyfikator -5 do trafienia. Łączny modyfikator: " + attackModifier);
                    break; // Żeby modyfikator nie kumulował się za każdą postać
                }
            }
        }

        return attackModifier;
    }

    //Modyfikator za przewagę liczebną
    private int CountOutnumber(Unit attacker, Unit target, out int adjacentAllies)
    {
        if (attacker.CompareTag(target.tag))
        {
            adjacentAllies = 0;
            return 0; // Jeśli atakujemy sojusznika to pomijamy przewagę liczebną
        }

        int adjacentOpponents = 0; // Przeciwnicy atakującego stojący obok celu ataku
        adjacentAllies = 0;    // Sojusznicy atakującego stojący obok celu ataku
        int adjacentOpponentsNearAttacker = 0; // Przeciwnicy atakującego stojący obok atakującego
        int modifier = 0;

        // Zbiór do przechowywania już policzonych przeciwników
        HashSet<Collider2D> countedOpponents = new HashSet<Collider2D>();

        List<Unit> alliesUnits = new List<Unit>();
        List<Unit> opponentsUnits = new List<Unit>();

        // Funkcja pomocnicza do zliczania jednostek w sąsiedztwie danej pozycji
        void CountAdjacentUnits(Vector2 center, string allyTag, string opponentTag, ref int allies, ref int opponents)
        {
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
                if (collider == null) continue;

                if (collider.CompareTag(allyTag) && (InventoryManager.Instance.ChooseWeaponToAttack(collider.gameObject).Type.Contains("melee") || pos == center))
                {
                    allies++;
                    alliesUnits.Add(collider.GetComponent<Unit>());
                }
                else if (collider.CompareTag(opponentTag) && !opponentsUnits.Contains(collider.GetComponent<Unit>()) && (InventoryManager.Instance.ChooseWeaponToAttack(collider.gameObject).Type.Contains("melee") || pos == center))
                {
                    opponents++;
                    opponentsUnits.Add(collider.GetComponent<Unit>());
                }
            }
        }

        // Zlicza sojuszników i przeciwników atakującego w sąsiedztwie celu ataku
        CountAdjacentUnits(target.transform.position, attacker.tag, target.tag, ref adjacentAllies, ref adjacentOpponents);

        // Zlicza przeciwników atakujacego w sąsiedztwie atakującego (bez liczenia jego sojuszników, bo oni nie mają wpływu na przewagę)
        int ignoredAllies = 0; // Tymczasowy licznik, ignorowany
        CountAdjacentUnits(attacker.transform.position, attacker.tag, target.tag, ref ignoredAllies /* ignorujemy sojuszników */, ref adjacentOpponentsNearAttacker);

        // Dodaje przeciwników w sąsiedztwie atakującego do całkowitej liczby jego przeciwników
        adjacentOpponents += adjacentOpponentsNearAttacker;


        // Wylicza modyfikator na podstawie stosunku przeciwników do sojuszników atakującego
        if (adjacentAllies >= adjacentOpponents * 4)
        {
            modifier = 6;
        }
        else if (adjacentAllies >= adjacentOpponents * 3)
        {
            modifier = 4;
        }
        else if (adjacentAllies >= adjacentOpponents * 2)
        {
            modifier = 2;
        }

        return modifier;
    }
    #endregion

    #region Calculating damage
    int CalculateDamage(int successLevel, Stats attackerStats, Weapon attackerWeapon)
    {
        int damage;
        int strengthModifier = 0;

        //Modyfikator za rozmiar
        if (attackerStats.Size > SizeCategory.Average && attackerWeapon.Type.Contains("melee") && attackerStats.S > 0)
        {
            strengthModifier = attackerStats.S;
        }

        //Modyfikator za broń typu strength-based
        if(attackerWeapon.Type.Contains("strength-based"))
        {
            attackerWeapon.Damage = attackerStats.S;
        }

        damage = successLevel + strengthModifier + attackerWeapon.Damage;

        string strenghtBonusString = strengthModifier != 0 ? $" Modyfikator z Siły: {strengthModifier}." : "";
        Debug.Log($"Różnica w rzutach: {successLevel}.{strenghtBonusString} Siła broni: {attackerWeapon.Damage}. Łączne obrażenia zadane przez {attackerStats.Name}: <color=#4dd2ff>{damage}</color>");

        if (damage < 0) damage = 0;

        return damage;
    }
    #endregion

    #region Critical wounds
    public IEnumerator CriticalWoundRoll(Stats attackerStats, Stats targetStats, string hitLocation)
    {
        int rollResult = 0;
        int[] criticalWoundTest = null;
        if (!GameManager.IsAutoDiceRollingMode && attackerStats.CompareTag("PlayerUnit"))
        {
            yield return StartCoroutine(DiceRollManager.Instance.WaitForRollValue(attackerStats, "trafienie krytyczne", null, "Pitiless", callback: result => criticalWoundTest = result));
            if (criticalWoundTest == null) yield break;
        }
        else
        {
            criticalWoundTest = DiceRollManager.Instance.TestSkill(attackerStats, "trafienie krytyczne", null, "Pitiless");
        }
        rollResult = criticalWoundTest[3];


        int modifier = targetStats.TempHealth < 0 ? Math.Abs(targetStats.TempHealth) : 0;

        // --- Talent: Instynkt Przetrwania (redukcja k4/k6/k8)
        int survivalDieSize = 0;
        switch (Mathf.Clamp(targetStats.SurvivalInstinct, 0, 3))
        {
            case 1: survivalDieSize = 4; break;
            case 2: survivalDieSize = 6; break;
            case 3: survivalDieSize = 8; break;
            default: survivalDieSize = 0; break;
        }

        if (survivalDieSize > 0)
        {
            if (!GameManager.IsAutoDiceRollingMode && targetStats.CompareTag("PlayerUnit"))
            {
                yield return StartCoroutine(WaitForRoll());

                IEnumerator WaitForRoll()
                {
                    int rollResult = 0;
                    _survivalInstinctInput.text = "";
                    _survivalInstinctPanel.SetActive(true);

                    // podpinamy listener pod button
                    _survivalInstinctButton.onClick.RemoveAllListeners();
                    _survivalInstinctButton.onClick.AddListener(() =>
                    {
                        if (int.TryParse(_survivalInstinctInput.text, out int value))
                        {
                            rollResult = value;
                        }
                        else
                        {
                            Debug.Log("<color=red>Musisz wpisać liczbę!</color>");
                        }
                    });

                    // czekamy aż gracz zatwierdzi
                    while (rollResult == 0)
                        yield return null;

                    Debug.Log($"{targetStats.Name} korzysta z talentu Instynkt Przetrwania i obniża wartość trafienia krytycznego o {rollResult}.");
                    modifier -= rollResult;
                    _survivalInstinctPanel.SetActive(false);
                }
            }
            else
            {
                int roll = UnityEngine.Random.Range(1, survivalDieSize + 1);
                Debug.Log($"{targetStats.Name} korzysta z talentu Instynkt Przetrwania i obniża wartość trafienia krytycznego o {roll}.");
                modifier -= roll;
            }
        }

        string modifierString = modifier != 0 ? $" Modyfikator: {modifier}." : "";
        Debug.Log($"Wynik rzutu na trafienie krytyczne: {rollResult}.{modifierString} {targetStats.Name} otrzymuje trafienie krytyczne w {TranslateHitLocation(hitLocation)} o wartości <color=red>{rollResult + modifier}</color>");

        int value = rollResult + modifier;
        Unit unit = targetStats.GetComponent<Unit>();
        string loc = (hitLocation ?? "").ToLowerInvariant();

        // Mały lokalny pomocnik do logów (krótko i jasno)
        void Log(string text) =>
            Debug.Log($"{text}");

        // Uwzględniamy tylko krwawienie; CAŁĄ resztę MG/Gracz dopisuje ręcznie.
        // GŁOWA
        // GŁOWA
        if (loc.Contains("głowa") || loc.Contains("glowa") || loc.Contains("head"))
        {
            if (value <= 7) Log($"Zamroczenie – {targetStats.Name} traci następną turę. <color=orange>Efekt uwzględnij ręcznie</color>.");
            else if (value <= 9) { unit.Bleeding = Mathf.Max(unit.Bleeding, 1); Log("Rozcięcie skroni – Krwawienie (1)."); }
            else if (value == 10) { unit.Bleeding = Mathf.Max(unit.Bleeding, 1); Log($"Rozcięcie łuku brwiowego – krew zalewa oczy (Percepcja -1 na 6 rund) + Krwawienie (1). <color=orange>Efekty poza Krwawieniem uwzględnij ręcznie</color>. ."); }
            else if (value == 11) Log("Oszołomienie – -2 do wszystkich testów na 6 rund. <color=orange>Efekt uwzględnij ręcznie</color>.");
            else if (value == 12) { unit.Bleeding = Mathf.Max(unit.Bleeding, 2); Log("Ostry krwotok z nosa – Krwawienie (2)."); }
            else if (value == 13) Log("Utrata 1k4 zębów. Charyzma -1 (na stałe). <color=orange>Efekt uwzględnij ręcznie</color>.");
            else if (value == 14) Log("Uszkodzenie nerwu wzrokowego – ślepota na 1 oko, Spostrzegawczość -3 (na stałe). <color=orange>Efekt uwzględnij ręcznie</color>.");
            else if (value == 15) Log("Uszkodzenie mózgu – Inteligencja -1k4 (na stałe). <color=orange>Efekt uwzględnij ręcznie</color>.");
            else if (value <= 17) { unit.Bleeding = Mathf.Max(unit.Bleeding, 1); Log("Utrata ucha – Słuch -3 (na stałe) + Krwawienie (1). <color=orange>Efekty poza Krwawieniem uwzględnij ręcznie</color>."); }
            else if (value <= 19) { unit.Bleeding = Mathf.Max(unit.Bleeding, 2); Log("Pęknięcie czaszki – Krwawienie (2). Brak pomocy chirurgiczej w ciągu 6 rund = śmierć. <color=orange>Efekty poza Krwawieniem uwzględnij ręcznie</color>."); }
            else Log("<color=red>Dekapitacja / Zmiażdżenie mózgu – natychmiastowa śmierć.</color>");
        }

        // RĘCE
        if (loc.Contains("arm"))
        {
            if (value <= 7) Log("Stłuczenie nadgarstka – -1 do testów tą ręką do końca walki. <color=orange>Efekt uwzględnij ręcznie</color>.");
            else if (value <= 9) { unit.Bleeding = Mathf.Max(unit.Bleeding, 1); Log("Rozcięcie przedramienia – Krwawienie (1)."); }
            else if (value == 10) Log("Porażenie nerwu – -2 do testów tą ręką do końca walki. <color=orange>Efekt uwzględnij ręcznie</color>.");
            else if (value == 11) { unit.Bleeding = Mathf.Max(unit.Bleeding, 2); Log("Otwarta rana – Krwawienie (2)."); }
            else if (value == 12) Log("Zwichnięcie nadgarstka – testy z użyciej tej ręki z ujemnym modyfikatorem k8 (do wyzdrowienia). <color=orange>Efekt uwzględnij ręcznie</color>.");
            else if (value == 13) Log("Złamanie palca – Zręczność -1 i testy z użyciej tej ręki z ujemnym modyfikatorem k8 (do wyzdrowienia). <color=orange>Efekt uwzględnij ręcznie</color>.");
            else if (value == 14) Log("Uszkodzenie ścięgna – Siła -1 i Zręczność -1 (do wyzdrowienia). <color=orange>Efekt uwzględnij ręcznie</color>.");
            else if (value == 15) Log("Złamanie ręki – Zręczność -3 i testy z użyciej tej ręki z ujemnym modyfikatorem k8 (do wyzdrowienia). <color=orange>Efekt uwzględnij ręcznie</color>.");
            else if (value <= 17) { unit.Bleeding = Mathf.Max(unit.Bleeding, 3); Log("Odcięcie dłoni – Amputacja + Krwawienie (3). <color=orange>Efekty poza Krwawieniem uwzględnij ręcznie</color>."); }
            else if (value <= 19) { unit.Bleeding = Mathf.Max(unit.Bleeding, 3); Log("Odcięcie/zmiażdżenie przedramienia – Amputacja + Krwawienie (3). Brak pomocy chirurgiczej w ciągu 6 rund = śmierć. <color=orange>Efekty poza Krwawieniem uwzględnij ręcznie</color>."); }
            else Log("<color=red>Odcięcie/zmiażdżenie ramienia – natychmiastowa śmierć.</color>");
        }

        // TUŁÓW
        if (loc.Contains("torso"))
        {
            if (value <= 7) Log("Stłuczone żebra – Siła -1 do końca walki. <color=orange>Efekt uwzględnij ręcznie</color>.");
            else if (value <= 9) { unit.Bleeding = Mathf.Max(unit.Bleeding, 1); Log("Płytkie rozcięcie brzucha – Krwawienie (1)."); }
            else if (value == 10) Log("Wgniecenie przepony – traci następną turę. <color=orange>Efekt uwzględnij ręcznie</color>.");
            else if (value == 11) { unit.Bleeding = Mathf.Max(unit.Bleeding, 2); Log("Głębokie rozcięcie brzucha – Krwawienie (2)."); }
            else if (value == 12) Log("Złamanie żebra – Siła -2 (do wyzdrowienia). <color=orange>Efekt uwzględnij ręcznie</color>.");
            else if (value == 13) Log("Uszkodzenie nerek – testy Odporności utrudnione o 3 (do wyzdrowienia). <color=orange>Efekt uwzględnij ręcznie</color>.");
            else if (value == 14) Log("Uszkodzenie płuca – Kondycja -3 (do wyzdrowienia). <color=orange>Efekt uwzględnij ręcznie</color>.");
            else if (value == 15) Log("Uszkodzenie kręgosłupa – Siła -2 i Zwinność -2 (do wyzdrowienia). <color=orange>Efekt uwzględnij ręcznie</color>.");
            else if (value <= 17) { unit.Bleeding = Mathf.Max(unit.Bleeding, 3); Log("Krwotok wewnętrzny – Krwawienie (3)."); }
            else if (value <= 19) Log("Przebicie płuca – Brak pomocy chirurgiczej w ciągu 6 rund = śmierć. <color=orange>Efekt uwzględnij ręcznie</color>.");
            else Log("<color=red>Przebicie serca / zmiażdżenie rdzenia – natychmiastowa śmierć.</color>");
        }

        // NOGI
        if (loc.Contains("leg"))
        {
            if (value <= 7) Log("Stłuczenie kolana – zasięg ruchu zmniejszony dwukrotnie do końca walki. <color=orange>Efekt uwzględnij ręcznie</color>.");
            else if (value <= 9) { unit.Bleeding = Mathf.Max(unit.Bleeding, 1); Log("Rozcięcie łydki – Krwawienie (1)."); }
            else if (value == 10) Log("Naderwane ścięgno – Zwinność -1 przez tydzień. <color=orange>Efekt uwzględnij ręcznie</color>.");
            else if (value == 11) Log("Zwichnięcie stawu skokowego – przez tydzień Szybkość zmniejszona dwukrotnie i Zwinność -1. <color=orange>Efekt uwzględnij ręcznie</color>.");
            else if (value == 12) { unit.Bleeding = Mathf.Max(unit.Bleeding, 1); Log("Głębokie cięcie uda – Krwawienie (1)."); }
            else if (value == 13) Log("Złamane palce – Szybkość zmniejszona dwukrotnie i Zwinność -2 (do wyzdrowienia). <color=orange>Efekt uwzględnij ręcznie</color>.");
            else if (value == 14) Log("Uszkodzenie więzadła w kolanie – Zwinność -2 oraz brak możliwości biegu i skoku (do wyzdrowienia). <color=orange>Efekt uwzględnij ręcznie</color>.");
            else if (value == 15) Log("Złamany piszczel – Szybkość zmniejszona dwukrotnie i Zwinność -3, potrzebny podpór aby się poruszać (do wyzdrowienia). <color=orange>Efekt uwzględnij ręcznie</color>.");
            else if (value <= 17) { unit.Bleeding = Mathf.Max(unit.Bleeding, 3); Log("Odcięcie/zmiażdżenie stopy – Amputacja, potrzebny podpór aby się poruszać + Krwawienie (3). <color=orange>Efekty poza Krwawieniem uwzględnij ręcznie</color>."); }
            else if (value <= 19) { unit.Bleeding = Mathf.Max(unit.Bleeding, 3); Log("Zmiażdżenie tętnicy udowej – Krwawienie (3). Brak pomocy chirurgiczej w ciągu 6 rund = śmierć. <color=orange>Efekty poza Krwawieniem uwzględnij ręcznie</color>."); }
            else Log("<color=red>Oderwanie nogi – natychmiastowa śmierć.</color>");
        }

        targetStats.CriticalWounds++;

        // Obsługa śmierci
        if (targetStats.CriticalWounds > 2 || value >= 20)
        {
            if(targetStats.CriticalWounds > 2) Debug.Log($"<color=red>Ilość ran krytycznych {targetStats.Name} przekroczyła limit. Jednostka umiera.</color>");

            if (GameManager.IsAutoKillMode)
            {
                HandleDeath(targetStats, targetStats.gameObject, attackerStats);
            }
        }
        else if (targetStats != null && targetStats.gameObject != null && targetStats.TempHealth < 0)
        {
            // Jeśli jednostka nie umarła, ale jej żywotność spadła poniżej 0 – ustaw na 0.
            targetStats.TempHealth = 0;
        }

        if(targetStats != null)
        {
            targetStats.GetComponent<Unit>().DisplayUnitHealthPoints();
        }
    }
    #endregion

    #region Check for attack localization and return armor value
    public IEnumerator OpenHitLocationPanel()
    {
        float holdTime = 0.3f;
        float elapsedTime = 0f;

        while (Input.GetMouseButton(1))
        {
            elapsedTime += Time.deltaTime;
            if (elapsedTime >= holdTime)
            {
                _selectHitLocationPanel.SetActive(true);
                break;
            }
            yield return null; // Poczekaj do następnej klatki
        }
    }

    //Celowanie w wybraną lokalizację
    public void SelectHitLocation(string hitLocation)
    {
        HitLocation = hitLocation;

        // Jeśli postać celowała wcześniej to nie dostaje kary do trafienia, ale też nie dostaje bonusu z Percepcji
        if (Unit.SelectedUnit.GetComponent<Unit>().AimingBonus > 0)
        {
            Unit.SelectedUnit.GetComponent<Unit>().AimingBonus = 0; 
        }
        else
        {
            Unit.SelectedUnit.GetComponent<Unit>().AimingBonus = -3;
        }
    }

    // Metoda określająca miejsce trafienia
    public IEnumerator DetermineHitLocationCoroutine(
    int rollResult,
    Stats targetStats = null,
    System.Action<string> onResolved = null)
    {
        string hitLocation = rollResult switch
        {
            1 or 2 => "torso",
            3 => "rightArm",
            4 => "leftArm",
            5 => "rightLeg",
            6 => "leftLeg",
            7 or 8 => "head",
            9 or 10 => null, // wybór gracza / auto
            _ => ""
        };

        // 9–10: wybór
        if (rollResult is 9 or 10)
        {
            // AUTOMATYCZNIE → wybór najsłabszej lokacji
            if (!IsManualPlayerAttack)
            {
                hitLocation = ChooseBestHitLocation(targetStats);
                HitLocation = hitLocation;
                onResolved?.Invoke(hitLocation);
                yield break;
            }

            // RĘCZNIE → panel i czekanie
            HitLocation = null; // zresetuj przed otwarciem panelu
            _selectHitLocationPanel.SetActive(true);
            Debug.Log("Wybierz lokalizację ataku.");

            yield return new WaitUntil(() => !string.IsNullOrEmpty(HitLocation));

            // gracz wybrał w SelectHitLocation(...)
            _selectHitLocationPanel.SetActive(false);
            hitLocation = HitLocation;
            onResolved?.Invoke(hitLocation);
            yield break;
        }

        // 1–8: bez wyboru
        HitLocation = hitLocation;
        onResolved?.Invoke(hitLocation);
        yield break;
    }

    public string NormalizeHitLocation(string hitLocation)
    {
        // Normalizujemy lokalizację trafienia
        string normalizedHitLocation = hitLocation switch
        {
            "rightArm" or "leftArm" => "arms",
            "rightLeg" or "leftLeg" => "legs",
            _ => hitLocation
        };

        return normalizedHitLocation;
    }

    // Metoda do logowania trafienia po polsku
    private string TranslateHitLocation(string hitLocation)
    {
        string message = hitLocation switch
        {
            "head" => "głowę",
            "leftArm" => "lewą rękę",
            "rightArm" => "prawą rękę",
            "torso" => "korpus",
            "leftLeg" => "lewą nogę",
            "rightLeg" => "prawą nogę",
            _ => "nieznaną lokalizację trafienia"
        };

        return message;
    }

    private string ChooseBestHitLocation(Stats targetStats, Weapon attackerWeapon = null)
    {
        if (targetStats == null) return "head";

        // Kolejność – HEAD wygrywa tylko przy remisie
        var locs = new[] { "head", "torso", "rightArm", "leftArm", "rightLeg", "leftLeg" };

        int bestArmor = int.MaxValue;
        string best = "head";

        foreach (var loc in locs)
        {
            int armor = CalculateArmor(targetStats, loc, attackerWeapon);
            if (armor < bestArmor)
            {
                bestArmor = armor;
                best = loc;
            }
            // przy remisie zostaje wcześniejszy wybór (HEAD jest pierwsza, więc wygrywa TYLKO gdy wartości równe)
        }

        return best;
    }


    // prostsza wersja – bez metalu
    public int CalculateArmor(Stats targetStats, string hitLocation, Weapon attackerWeapon = null)
    {
        return CalculateArmor(targetStats, hitLocation, attackerWeapon, out _);
    }

    // pełna wersja – z metalem
    public int CalculateArmor(Stats targetStats, string hitLocation, Weapon attackerWeapon, out int metalArmorValue)
    {
        string normalizedHitLocation = NormalizeHitLocation(hitLocation);

        int armor = normalizedHitLocation switch
        {
            "head" => targetStats.Armor_head,
            "arms" => targetStats.Armor_arms,
            "torso" => targetStats.Armor_torso,
            "legs" => targetStats.Armor_legs,
            _ => 0
        };

        Inventory inventory = targetStats.GetComponent<Inventory>();
        List<Weapon> armorByLocation =
            (inventory.ArmorByLocation != null && inventory.ArmorByLocation.ContainsKey(normalizedHitLocation))
            ? inventory.ArmorByLocation[normalizedHitLocation]
            : new List<Weapon>();

        metalArmorValue = armorByLocation
            .Where(w => w != null && w.Type != null &&
                        w.Type.Any(t => !string.IsNullOrEmpty(t) &&
                                        (t.Equals("chain", StringComparison.OrdinalIgnoreCase) ||
                                         t.Equals("plate", StringComparison.OrdinalIgnoreCase))))
            .Sum(w => Mathf.Max(0, w.Armor - w.Damage));

        if (attackerWeapon != null && attackerWeapon.Penetrating && armor > 0)
            armor--;

        return armor;
    }


    public IEnumerator SelectRiderOrMount(Unit unit)
    {
        _riderOrMountPanel.SetActive(true);

        // Najpierw czekamy, aż gracz kliknie którykolwiek przycisk
        yield return new WaitUntil(() => !_riderOrMountPanel.activeSelf);

        if (Unit.SelectedUnit == null) yield break;

        if (_riderOrMount == "rider")
        {
            Attack(Unit.SelectedUnit.GetComponent<Unit>(), unit, false);
        }
        else if (_riderOrMount == "mount")
        {
            Attack(Unit.SelectedUnit.GetComponent<Unit>(), unit.Mount, false);
        }
    }

    public void RiderOrMountButtonClick(string riderOrmount)
    {
        _riderOrMount = riderOrmount;
    }
    #endregion

    #region Charge
    public void Charge(GameObject attacker, GameObject target)
    {
        //Sprawdza pole, w którym atakujący zatrzyma się po wykonaniu szarży
        GameObject targetTile = GetTileAdjacentToTarget(attacker, target);

        Stats attackerStats = attacker.GetComponent<Stats>();
        Stats targetStats = target.GetComponent<Stats>();

        Vector2 targetTilePosition;

        if (targetTile != null)
        {
            targetTilePosition = new Vector2(targetTile.transform.position.x, targetTile.transform.position.y);
        }
        else
        {
            Debug.Log($"Cel ataku stoi poza zasięgiem szarży.");
            return;
        }

        if (attacker.GetComponent<Unit>().Prone)
        {
            Debug.Log("Jednostka w stanie utraty przytomności nie może wykonywać szarży.");
            return;
        }

        //Ścieżka ruchu szarżującego
        List<Vector2> path = MovementManager.Instance.FindPath(attacker.transform.position, targetTilePosition);

        //Sprawdza, czy postać jest wystarczająco daleko do wykonania szarży
        if (path.Count >= attackerStats.Sz / 2f && path.Count <= attackerStats.TempSz)
        {
            //Zapisuje grę przed wykonaniem ruchu, aby użycie punktu szczęścia wczytywało pozycję przed wykonaniem szarży i można było wykonać ją ponownie
            SaveAndLoadManager.Instance.SaveUnits(UnitsManager.Instance.AllUnits, "autosave");

            MovementManager.Instance.MoveSelectedUnit(targetTile, attacker);

            // Wywołanie funkcji z wyczekaniem na koniec animacji ruchu postaci
            float delay = 0.25f;
            StartCoroutine(DelayedAttack(attacker, target, path.Count * delay));

            IEnumerator DelayedAttack(GameObject attacker, GameObject target, float delay)
            {
                yield return new WaitForSeconds(delay);

                if (attacker == null || target == null) yield break;

                yield return StartCoroutine(AttackCoroutine(attacker.GetComponent<Unit>(), target.GetComponent<Unit>()));
            }
        }
        else
        {
            ChangeAttackType(); // Resetuje szarżę

            Debug.Log("Zbyt mała odległość na wykonanie szarży.");
        }
    }

    // Szuka wolnej pozycji obok celu szarży, do której droga postaci jest najkrótsza
    public GameObject GetTileAdjacentToTarget(GameObject attacker, GameObject target)
    {
        if (target == null) return null;

        Vector2 targetPos = target.transform.position;

        //Wszystkie przylegające pozycje do atakowanego
        Vector2[] positions = { targetPos + Vector2.right,
            targetPos + Vector2.left,
            targetPos + Vector2.up,
            targetPos + Vector2.down,
            targetPos + new Vector2(1, 1),
            targetPos + new Vector2(-1, -1),
            targetPos + new Vector2(-1, 1),
            targetPos + new Vector2(1, -1)
        };

        GameObject targetTile = null;

        //Długość najkrótszej ścieżki do pola docelowego
        int shortestPathLength = int.MaxValue;

        //Lista przechowująca ścieżkę ruchu szarżującego
        List<Vector2> path = new List<Vector2>();

        foreach (Vector2 pos in positions)
        {
            GameObject tile = GameObject.Find($"Tile {pos.x - GridManager.Instance.transform.position.x} {pos.y - GridManager.Instance.transform.position.y}");

            //Jeżeli pole jest zajęte to szukamy innego
            if (tile == null || tile.GetComponent<Tile>().IsOccupied) continue;

            path = MovementManager.Instance.FindPath(attacker.transform.position, pos);

            if (path.Count == 0) continue;

            // Aktualizuje najkrótszą drogę
            if (path.Count < shortestPathLength)
            {
                shortestPathLength = path.Count;
                targetTile = tile;
            }
        }

        if (shortestPathLength > attacker.GetComponent<Stats>().TempSz && !GameManager.IsAutoCombatMode)
        {
            return null;
        }
        else
        {
            return targetTile;
        }
    }
    #endregion

    #region Grappling
    public void Grappling(Unit attacker, Unit target)
    {
        // Sprawdzamy, czy atakujący może wykonać akcję
        if (!attacker.CanDoAction)
        {
            Debug.Log("Ta jednostka nie może w tej rundzie wykonać więcej akcji.");
            return;
        }

        StartCoroutine(GrapplingActionCoroutine(attacker, target));
    }

    private IEnumerator GrapplingActionCoroutine(Unit attacker, Unit target)
    {
        //bool isGrappler = (attacker.EntangledUnitId == target.UnitId);
        //bool isGrappled = (attacker.UnitId == target.EntangledUnitId);

        //// Jeśli żadna z relacji nie występuje – wychodzimy i wykonujemy atak
        //if (!isGrappler && !isGrappled)
        //{
        //    Attack(attacker, target);
        //    yield break;
        //}

        // Pobieramy statystyki obu jednostek
        Stats attackerStats = attacker.Stats;
        Stats targetStats = target.Stats;



        //// Ustawiamy widoczność przycisków w panelu wyboru akcji:
        //if (isGrappler)
        //{
        //    _escapeGrappleButton.gameObject.SetActive(false);
        //    _releaseGrappleButton.gameObject.SetActive(true);
        //}
        //else if (isGrappled)
        //{
        //    _escapeGrappleButton.gameObject.SetActive(true);
        //    _releaseGrappleButton.gameObject.SetActive(false);
        //}


        // Walka w pochwyceniu
        if (AttackTypes["Grappling"] == true && attacker.GrappledUnitId == target.UnitId)
        {
            // Pokaż panel
            _grapplingActionChoice = "";
            _grapplingActionPanel.SetActive(true);
            _escapeGrappleButton.gameObject.SetActive(false);
            _releaseGrappleButton.gameObject.SetActive(true);

            yield return new WaitUntil(() => !_grapplingActionPanel.activeSelf);

            if (_grapplingActionChoice == "release")
            {
                // Rozluźnij chwyt
                target.Grappled = false;
                if (attacker.GrappledUnitId == target.UnitId) attacker.GrappledUnitId = 0;

                attacker.CanMove = true;
                target.CanMove = true;

                Debug.Log($"{attackerStats.Name} rozluźnia chwyt i puszcza {targetStats.Name} (koniec Grapple).");
                ChangeAttackType(); // wróć do StandardAttack
                yield break;
            }
            else if (_grapplingActionChoice == "attack")
            {
                Attack(attacker, target);
                yield break;
            }
        }
        else if (AttackTypes["Grappling"] == true && target.GrappledUnitId == attacker.UnitId)
        {
            // Pokaż panel
            _grapplingActionChoice = "";
            _grapplingActionPanel.SetActive(true);
            _escapeGrappleButton.gameObject.SetActive(true);
            _releaseGrappleButton.gameObject.SetActive(false);

            yield return new WaitUntil(() => !_grapplingActionPanel.activeSelf);

            if (_grapplingActionChoice == "escape")
            {
                int[] attackerTest = null;
                int bestStat = Mathf.Max(attackerStats.S, attackerStats.Zw);
                string statName = (bestStat == attackerStats.S) ? "Siłę" : "Zwinność";
                string statKey = (bestStat == attackerStats.S) ? "S" : "Zw";

                // 1) Rzut atakującego (czyli pochwyconego): Siła lub Zwinność
                if (!GameManager.IsAutoDiceRollingMode && attacker.CompareTag("PlayerUnit"))
                {
                    yield return StartCoroutine(DiceRollManager.Instance.WaitForRollValue(attackerStats, statName, statKey, callback: result => attackerTest = result));
                    if (attackerTest == null) yield break;
                }
                else
                {
                    attackerTest = DiceRollManager.Instance.TestSkill(attackerStats, statName, statKey);
                }
                int attackerRoll = attackerTest[3];

                // 2) Rzut celu (czyli pochwytującego)
                int[] targetTest = null;
                if (!GameManager.IsAutoDiceRollingMode && target.CompareTag("PlayerUnit"))
                {
                    yield return StartCoroutine(DiceRollManager.Instance.WaitForRollValue(targetStats, "Siłę", "S", callback: result => targetTest = result));
                    if (targetTest == null) yield break;
                }
                else
                {
                    targetTest = DiceRollManager.Instance.TestSkill(targetStats, "Siłę", "S");
                }
                int targetRoll = targetTest[3];

                //Wykonuje akcję
                RoundsManager.Instance.DoAction(attacker);

                // 3) Rozstrzygnięcie
                if (attackerRoll >= targetRoll)
                {
                    target.GrappledUnitId = 0;
                    attacker.Grappled = false;
                    attacker.Entangled = false;

                    attacker.CanMove = true;
                    target.CanMove = true;

                    MovementManager.Instance.SetCanMoveToggle(true);

                    Debug.Log($"<color=#FF7F50>{attackerStats.Name} uwolnił/a się z pochwycenia przez {targetStats.Name}.</color>");
                }
                else
                {
                    Debug.Log($"<color=#FF7F50>{attackerStats.Name} nie udaje się uwolnić z pochwycenia przez {targetStats.Name}.</color>");
                }

                //Szczęście i pech
                if (DiceRollManager.Instance.IsDoubleDigit(targetTest[0], targetTest[1]))
                {
                    if (attackerRoll < targetRoll)
                    {
                        Debug.Log($"{targetStats.Name} wyrzucił <color=green>SZCZĘŚCIE</color>!");
                        targetStats.FortunateEvents++;
                    }
                    else
                    {
                        Debug.Log($"{targetStats.Name} wyrzucił <color=red>PECHA</color>!");
                        targetStats.UnfortunateEvents++;
                    }
                }

                if (DiceRollManager.Instance.IsDoubleDigit(attackerTest[0], attackerTest[1]))
                {
                    if (attackerRoll >= targetRoll)
                    {
                        Debug.Log($"{attackerStats.Name} wyrzucił <color=green>SZCZĘŚCIE</color>!");
                        attackerStats.FortunateEvents++;
                    }
                    else
                    {
                        Debug.Log($"{attackerStats.Name} wyrzucił <color=red>PECHA</color>!");
                        attackerStats.UnfortunateEvents++;
                    }
                }

                yield break;
            }
            else if (_grapplingActionChoice == "attack")
            {
                Attack(attacker, target);
                yield break;
            }
        }

        // Próba pochwycenia
        if (AttackTypes["Grappling"] && attacker.GrappledUnitId != target.UnitId && !target.Grappled)
        {
            if (targetStats.Size - attackerStats.Size > 1)
            {
                Debug.Log($"<color=#FF7F50>{attackerStats.Name} nie jest w stanie pochwycić {targetStats.Name}. Cel jest zbyt duży.</color>");
                yield break;
            }

            // 1) Rzut atakującego: Siła
            int attackerRoll = 0;
            int[] attackerTest = null;
            if (!GameManager.IsAutoDiceRollingMode && attacker.CompareTag("PlayerUnit"))
            {
                yield return StartCoroutine(DiceRollManager.Instance.WaitForRollValue(attackerStats, "Siłę", "S", callback: result => attackerTest = result));
                if (attackerTest == null) yield break;
            }
            else
            {
                attackerTest = DiceRollManager.Instance.TestSkill(attackerStats, "Siłę", "S");
            }
            attackerRoll = attackerTest[3];

            // 2) Rzut celu: wybieramy wyższą cechę z S / Zw
            int[] targetTest = null;
            int bestStat = Mathf.Max(targetStats.S, targetStats.Zw);
            string statName = (bestStat == targetStats.S) ? "Siłę" : "Zwinność";
            string statKey = (bestStat == targetStats.S) ? "S" : "Zw";

            if (!GameManager.IsAutoDiceRollingMode && target.CompareTag("PlayerUnit"))
            {
                yield return StartCoroutine(DiceRollManager.Instance.WaitForRollValue(targetStats, statName, statKey, callback: result => targetTest = result));
                if (targetTest == null) yield break;
            }
            else
            {
                targetTest = DiceRollManager.Instance.TestSkill(targetStats, statName, statKey);
            }
            int targetRoll = targetTest[3];

            //Wykonuje akcję
            RoundsManager.Instance.DoAction(attacker);

            // 3) Rozstrzygnięcie
            if (attackerRoll >= targetRoll)
            {
                attacker.GrappledUnitId = target.UnitId;
                target.Grappled = true;
                target.Entangled = true;

                attacker.CanMove = false;
                target.CanMove = false;

                MovementManager.Instance.SetCanMoveToggle(false);

                Debug.Log($"<color=#FF7F50>{attackerStats.Name} pochwycił/a {targetStats.Name}.</color>");
                RoundsManager.Instance.FinishTurn();
            }
            else
            {
                Debug.Log($"<color=#FF7F50> {attackerStats.Name} bezskutecznie próbuje pochwycić {targetStats.Name}.</color>");
                RoundsManager.Instance.FinishTurn();
            }
            yield break;
        }
    }

    private void GrapplingActionButtonClick(string action)
    {
        _grapplingActionChoice = action;
    }

    public IEnumerator EscapeFromEntanglement(Stats entanglingUnitStats, Stats entangledUnitStats)
    {
        Unit entangledUnit = entangledUnitStats.GetComponent<Unit>();
        Unit entanglingUnit = entanglingUnitStats.GetComponent<Unit>();

        if (!entangledUnit.Entangled)
            yield break;

        if (!entangledUnitStats.GetComponent<Unit>().CanDoAction)
        {
            Debug.Log("Ta jednostka nie może w tej rundzie wykonać więcej akcji.");
            yield break;
        }

        // Dezycja, czy chce próbować się uwolnić
        if (!GameManager.IsAutoDiceRollingMode)
        {
            bool attempt = false;
            bool decided = false;

            _entaglingPanel.SetActive(true);
            _escapeYesButton.onClick.RemoveAllListeners();
            _escapeNoButton.onClick.RemoveAllListeners();

            _escapeYesButton.onClick.AddListener(() => { attempt = true; decided = true; });
            _escapeNoButton.onClick.AddListener(() => { attempt = false; decided = true; });

            while (!decided) yield return null;

            _entaglingPanel.SetActive(false);

            if (!attempt) yield break;
        }

        //Wykonuje akcję
        RoundsManager.Instance.DoAction(entangledUnitStats.GetComponent<Unit>());

        int targetRoll = 0;
        int[] targetTest = null;
        int difficultyLevel = 12;
        if (!GameManager.IsAutoDiceRollingMode && entangledUnit.CompareTag("PlayerUnit"))
        {
            yield return StartCoroutine(DiceRollManager.Instance.WaitForRollValue(entangledUnitStats, "Zwinność", "Zw", difficultyLevel: difficultyLevel, callback: result => targetTest = result));
            if (targetTest == null) yield break;
        }
        else
        {
            targetTest = DiceRollManager.Instance.TestSkill(entangledUnitStats, "Zwinność", "Zw", difficultyLevel: difficultyLevel);
        }

        targetRoll = targetTest[3];

        if (targetRoll > difficultyLevel)
        {
            entangledUnit.Entangled = false;
            entanglingUnit.EntangledUnitId = 0;
            entangledUnit.CanMove = true;
            MovementManager.Instance.SetCanMoveToggle(true);

            Debug.Log($"<color=#FF7F50>{entangledUnitStats.Name} uwolnił się z Unieruchomienia przez {entanglingUnitStats.Name}.</color>");
        }
        else
        {
            Debug.Log($"<color=#FF7F50>{entangledUnitStats.Name} bezskutecznie próbuje się uwolnić z Unieruchomienia przez {entanglingUnitStats.Name}.</color>");
        }
    }

    public void ReleaseEntangledUnit(Unit attacker, Unit target, Weapon weapon = null)
    {
        target.CanMove = true;

        ChangeAttackType();
        UnitsManager.Instance.UpdateUnitPanel(attacker.gameObject);

        if(weapon)
        {
            target.Entangled = false;
            attacker.EntangledUnitId = 0;
            Debug.Log($"<color=#FF7F50>{attacker.Stats.Name} oddala się poza zasięg {weapon.Name} i pozwala {target.Stats.Name} uwolnić się z pochwycenia.</color>");
        }
        else
        {
            target.Grappled = false;
            attacker.GrappledUnitId = 0;
            Debug.Log($"<color=#FF7F50>{attacker.Stats.Name} rozluźnia chwyt i pozwala {target.Stats.Name} uwolnić się.</color>");
        }
    }

    #endregion

    #region Reloading
    public void Reload()
    {
        StartCoroutine(ReloadCoroutine());
    }
    private IEnumerator ReloadCoroutine()
    {
        if (Unit.SelectedUnit == null) yield break;

        Weapon weapon = Unit.SelectedUnit.GetComponent<Inventory>().EquippedWeapons[0];
        Stats stats = Unit.SelectedUnit.GetComponent<Stats>();

        if (weapon == null || weapon.ReloadLeft == 0)
        {
            Debug.Log($"Wybrana broń nie wymaga ładowania.");
            yield break;
        }

        if (weapon.ReloadLeft > 0)
        {
            if (!Unit.SelectedUnit.GetComponent<Unit>().CanDoAction)
            {
                Debug.Log("Ta jednostka nie może w tej rundzie wykonać więcej akcji.");
                yield break;
            }

            //Wykonuje akcję
            RoundsManager.Instance.DoAction(Unit.SelectedUnit.GetComponent<Unit>());

            weapon.ReloadLeft--;

            StartCoroutine(AnimationManager.Instance.PlayAnimation("reload", Unit.SelectedUnit));
        }

        if (weapon.ReloadLeft == 0)
        {
            Debug.Log($"Broń {stats.Name} załadowana.");

            SetActionsButtonsInteractable();
        }
        else
        {
            Debug.Log($"{stats.Name} ładuje broń. Pozostał/y {weapon.ReloadLeft} akcje do pełnego załadowania.");
        }

        InventoryManager.Instance.DisplayReloadTime();
    }

    private void ResetWeaponLoad(Weapon attackerWeapon, Stats attackerStats)
    {
        if (attackerWeapon.ReloadTime == 0) return;

        //Sprawia, że po ataku należy przeładować broń
        attackerWeapon.ReloadLeft = attackerWeapon.ReloadTime;
        attackerWeapon.WeaponsWithReloadLeft[attackerWeapon.Id] = attackerWeapon.ReloadLeft;

        //Zapobiega ujemnej wartości czasu przeładowania
        if (attackerWeapon.ReloadLeft <= 0)
        {
            attackerWeapon.ReloadLeft = 0;
        }

        InventoryManager.Instance.DisplayReloadTime();
    }
    #endregion

    #region Aiming
    public void SetAim() // DDOAĆ TU DRGUĄ MOŻLIWOŚĆ, CZYLI WYBÓR LOKALIZACJI
    {
        if (Unit.SelectedUnit == null) return;

        Unit unit = Unit.SelectedUnit.GetComponent<Unit>();

        //Sprawdza, czy postać już celuje i chce przestać, czy chce dopiero przycelować
        if (unit.AimingBonus != 0)
        {
            unit.AimingBonus = 0;
        }
        else
        {
            // Sprawdzamy, czy atakujący może wykonać akcję
            if (!unit.CanDoAction)
            {
                Debug.Log("Ta jednostka nie może w tej rundzie wykonać więcej akcji.");
                return;
            }

            //Wykonuje akcję
            RoundsManager.Instance.DoAction(Unit.SelectedUnit.GetComponent<Unit>());

            if(unit.GetComponent<Stats>().P > 0)
            {
                unit.AimingBonus += unit.GetComponent<Stats>().P;
            }

            Debug.Log($"{unit.GetComponent<Stats>().Name} przycelowuje.");

            StartCoroutine(AnimationManager.Instance.PlayAnimation("aim", Unit.SelectedUnit));
        }

        UpdateAimButtonColor();
    }
    public void UpdateAimButtonColor()
    {
        if (Unit.SelectedUnit != null && Unit.SelectedUnit.GetComponent<Unit>().AimingBonus != 0)
        {
            _aimButton.GetComponent<UnityEngine.UI.Image>().color = UnityEngine.Color.green;
        }
        else
        {
            _aimButton.GetComponent<UnityEngine.UI.Image>().color = UnityEngine.Color.white;
        }
    }
    #endregion

    #region Opportunity attack
    // Sprawdza czy ruch powoduje atak okazyjny
    public void CheckForOpportunityAttack(GameObject movingUnit, Vector2 selectedTilePosition)
    {
        //Przy bezpiecznym odwrocie nie występuje atak okazyjny.
        if (Unit.SelectedUnit != null && Unit.SelectedUnit.GetComponent<Unit>().IsRetreating) return;

        List<Unit> adjacentOpponents = AdjacentOpponents(movingUnit.transform.position, movingUnit.tag);

        if (adjacentOpponents.Count == 0) return;

        // Atak okazyjny wywolywany dla kazdego wroga bedacego w zwarciu z bohaterem gracza
        foreach (Unit unit in adjacentOpponents)
        {
            Weapon weapon = InventoryManager.Instance.ChooseWeaponToAttack(unit.gameObject);

            //Jeżeli jest to jednostka unieruchomiona, nieprzytomna lub jednostka z bronią dystansową to ją pomijamy
            if (weapon.Type.Contains("ranged") || unit.Unconscious || unit.EntangledUnitId != 0 || unit.Entangled) continue;

            // Sprawdzenie czy ruch powoduje oddalenie się od przeciwników (czyli atak okazyjny)
            float distanceFromOpponentAfterMove = Vector2.Distance(selectedTilePosition, unit.transform.position);

            if (distanceFromOpponentAfterMove > 1.8f)
            {
                Debug.Log($"Ruch spowodował atak okazyjny od {unit.GetComponent<Stats>().Name}.");

                // Wywołanie ataku okazyjnego
                Attack(unit, movingUnit.GetComponent<Unit>(), true);
            }
        }
    }

    // Funkcja pomocnicza do sprawdzania jednostek w sąsiedztwie danej pozycji
    public List<Unit> AdjacentOpponents(Vector2 center, string movingUnitTag)
    {
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

        List<Unit> units = new List<Unit>();

        foreach (var pos in positions)
        {
            Collider2D collider = Physics2D.OverlapPoint(pos);
            if (collider == null || collider.GetComponent<Unit>() == null) continue;

            if (!collider.CompareTag(movingUnitTag))
            {
                units.Add(collider.GetComponent<Unit>());
            }
        }

        return units;
    }
    #endregion

    #region Stun
    private IEnumerator Stun(Stats attackerStats, Stats targetStats)
    {
        Unit attackerUnit = attackerStats.GetComponent<Unit>();
        Unit targetUnit = targetStats.GetComponent<Unit>();

        int targetRoll = 0;
        int attackerRoll = 0;

        // Dla atakowanego
        int[] targetTest = null;
        if (!GameManager.IsAutoDiceRollingMode && targetUnit.CompareTag("PlayerUnit"))
        {
            yield return StartCoroutine(DiceRollManager.Instance.WaitForRollValue(targetStats, "Kondycję", "K", callback: result => targetTest = result));
            if (targetTest == null) yield break;
        }
        else
        {
            targetTest = DiceRollManager.Instance.TestSkill(targetStats, "Kondycję", "K");
        }
        targetRoll = targetTest[3];

        // Dla atakującego
        int[] attackerTest= null;
        if (!GameManager.IsAutoDiceRollingMode && attackerUnit.CompareTag("PlayerUnit"))
        {
            yield return StartCoroutine(DiceRollManager.Instance.WaitForRollValue(attackerStats, "Siłę", "S", callback: result => attackerTest = result));
            if (attackerTest == null) yield break;
        }
        else
        {
            attackerTest = DiceRollManager.Instance.TestSkill(attackerStats, "Siłę", "S");
        }
        attackerRoll = attackerTest[3];


        if (attackerRoll > targetRoll)
        {
            Debug.Log($"<color=#FF7F50>Atak {attackerStats.Name} ogłuszył {targetStats.Name}.</color>");
            targetUnit.Unconscious = true;
        }
        else
        {
            Debug.Log($"<color=#FF7F50>Atak {attackerStats.Name} nie dał rady ogłuszyć {targetStats.Name}.</color>");
        }
    }
    #endregion

    #region Disarm
    private IEnumerator Disarm(Stats attackerStats, Stats targetStats, Weapon attackerWeapon, Weapon targetWeapon)
    {
        if (targetWeapon.Type.Contains("natural-weapon"))
        {
            Debug.Log("Nie można rozbrajać jednostek walczących bronią naturalną.");
            yield break;
        }

        Unit attackerUnit = attackerStats.GetComponent<Unit>();
        Unit targetUnit = targetStats.GetComponent<Unit>();

        //Wykonuje akcję
        RoundsManager.Instance.DoAction(attackerUnit);

        int targetRoll = 0;
        int attackerRoll = 0;

        // Dla atakowanego
        int[] targetTest = null;
        if (!GameManager.IsAutoDiceRollingMode && targetUnit.CompareTag("PlayerUnit"))
        {
            yield return StartCoroutine(DiceRollManager.Instance.WaitForRollValue(targetStats, "Walkę Wręcz", "Zr", "MeleeCombat", callback: result => targetTest = result));
            if (targetTest == null) yield break;
        }
        else
        {
            targetTest = DiceRollManager.Instance.TestSkill(targetStats, "Walkę Wręcz", "Zr", "MeleeCombat");
        }
        targetRoll = targetTest[3];

        // Dla atakującego
        int[] attackerTest = null;
        if (!GameManager.IsAutoDiceRollingMode && attackerUnit.CompareTag("PlayerUnit"))
        {
            yield return StartCoroutine(DiceRollManager.Instance.WaitForRollValue(attackerStats, "Walkę Wręcz", "Zr", "MeleeCombat", callback: result => attackerTest = result));
            if (attackerTest == null) yield break;
        }
        else
        {
            attackerTest = DiceRollManager.Instance.TestSkill(attackerStats, "Walkę Wręcz", "Zr", "MeleeCombat");
        }
        attackerRoll = attackerTest[3];

        if (attackerRoll > targetRoll)
        {
            Debug.Log($"{attackerStats.Name} rozbroił {targetStats.Name}.");
            targetStats.GetComponent<Weapon>().ResetWeapon();

            //Aktualizujemy tablicę dobytych broni
            Weapon[] equippedWeapons = targetStats.GetComponent<Inventory>().EquippedWeapons;
            for (int i = 0; i < equippedWeapons.Length; i++)
            {
                equippedWeapons[i] = null;
            }
        }
        else
        {
            Debug.Log($"{attackerStats.Name} nie dał rady rozbroić {targetStats.Name}.");
        }

        // Zresetowanie ładowania broni dystansowej celu rozbrajania, bo musi się bronić
        if (targetWeapon.ReloadLeft != 0)
        {
            ResetWeaponLoad(targetWeapon, targetStats);
        }

        // Zresetowanie typu ataku
        ChangeAttackType();
    }
    #endregion

    #region Find adjacent units
    public Unit[] GetAdjacentUnits(Vector2 centerPosition, Unit exclude = null)
    {
        List<Unit> adjacentUnits = new List<Unit>();
        Vector2[] directions = {
        Vector2.zero,
        Vector2.right,
        Vector2.left,
        Vector2.up,
        Vector2.down,
        new Vector2(1, 1),
        new Vector2(-1, -1),
        new Vector2(-1, 1),
        new Vector2(1, -1)
    };

        foreach (var dir in directions)
        {
            Vector2 pos = centerPosition + dir;
            Collider2D collider = Physics2D.OverlapPoint(pos);
            if (collider != null)
            {
                Unit unit = collider.GetComponent<Unit>();
                if (unit != null && unit != exclude)
                {
                    adjacentUnits.Add(unit);
                }
            }
        }

        return adjacentUnits.ToArray();
    }

    public void UnitOrGroupButtonClick(string unitOrGroup)
    {
        _groupOfTargetsPenalty = unitOrGroup == "unit";
    }
    #endregion

}
