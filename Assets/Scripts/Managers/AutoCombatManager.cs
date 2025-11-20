using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
using Unity.VisualScripting.Antlr3.Runtime.Misc;
using UnityEngine;

public class AutoCombatManager : MonoBehaviour
{
    // Prywatne statyczne pole przechowujące instancję
    private static AutoCombatManager instance;

    // Publiczny dostęp do instancji
    public static AutoCombatManager Instance
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

    public Tile TargetTile;

    [Header("Czy w Auto-Combacie jednostki powinny używać Szaleńczego Ataku?")]
    [SerializeField] private bool _allOutAttackEnabled = true;
    public void Act(Unit unit)
    {
        Weapon weapon = InventoryManager.Instance.ChooseWeaponToAttack(unit.gameObject);
        bool isRangedAttack = !weapon.Type.Contains("melee");

        var (closestOpponent, distance) = GetClosestOpponent(unit.gameObject, isRangedAttack);

        //Jeżeli jednostka walczy bronią dystansową ale nie ma żadnego przeciwnika do którego może strzelać to obiera za cel przeciwnika w zwarciu. Będzie się to wiązało z próbą dobycia broni typu "melee"
        if (closestOpponent == null && isRangedAttack)
        {
            isRangedAttack = false;
            (closestOpponent, distance) = GetClosestOpponent(unit.gameObject, isRangedAttack);
        }
        if (closestOpponent == null || !unit.CanDoAction) return;

        float effectiveAttackRange = weapon.Type.Contains("ranged") && !weapon.Type.Contains("entangling") ? weapon.AttackRange * 3 : weapon.AttackRange;
        if (weapon.Type.Contains("throwing")) // Oblicza właściwy zasięg ataku, uwzględniając broń miotaną
        {
            effectiveAttackRange *= unit.GetComponent<Stats>().S / 10;
        }

        // Jeśli rywal jest w zasięgu ataku to wykonuje atak
        if (distance <= effectiveAttackRange)
        {
            if (weapon.Type.Contains("ranged"))
            {
                //Sprawdza konieczne warunki do wykonania ataku dystansowego
                if (CombatManager.Instance.ValidateRangedAttack(unit, closestOpponent.GetComponent<Unit>(), weapon, distance) == false)
                {
                    if(distance < 1.5f)
                    {
                        // Jeśli broń nie wymaga naladowania to wykonuje atak, w przeciwnym razie wykonuje przeładowanie
                        if (weapon.ReloadLeft == 0)
                        {
                            StartCoroutine(ExecuteAttack(unit, closestOpponent, weapon, distance));
                        }
                        else
                        {
                            Debug.Log($"<color=#4dd2ff>{unit.GetComponent<Stats>().Name} przeładowuje broń.</color>");
                            CombatManager.Instance.Reload();
                        }
                     
                        return;
                    }
                    else
                    {
                        // Jeśli broń nie wymaga naladowania to wykonuje atak, w przeciwnym razie wykonuje przeładowanie
                        if (weapon.ReloadLeft == 0)
                        {
                            StartCoroutine(AttemptToChangeDistanceAndAttack(unit, closestOpponent, weapon));
                        }
                        else
                        {
                            Debug.Log($"<color=#4dd2ff>{unit.GetComponent<Stats>().Name} przeładowuje broń.</color>");
                            CombatManager.Instance.Reload();
                        }

                        return;
                    }
                }
            }

            StartCoroutine(ExecuteAttack(unit, closestOpponent, weapon, distance));
        }
        else
        {
            StartCoroutine(AttemptToChangeDistanceAndAttack(unit, closestOpponent, weapon));
        }
    }

    private IEnumerator ExecuteAttack(Unit unit, GameObject closestOpponent, Weapon weapon, float distance)
    {
        if (distance > 1.5f) //atak dystansowy
        {
            // Jeśli broń nie wymaga naladowania to wykonuje atak, w przeciwnym razie wykonuje przeładowanie
            if (weapon.ReloadLeft == 0)
            {
                Debug.Log($"<color=#4dd2ff>{unit.GetComponent<Stats>().Name} atakuje {closestOpponent.GetComponent<Stats>().Name}.</color>");
                CombatManager.Instance.Attack(unit, closestOpponent.GetComponent<Unit>());
            }
            else
            {
                Debug.Log($"<color=#4dd2ff>{unit.GetComponent<Stats>().Name} przeładowuje broń.</color>");
                CombatManager.Instance.Reload();
            }
        }
        else //atak w zwarciu
        {
            //Dobycie broni, jeśli obecna broń uniemożliwia walkę w zwarciu.
            if (!weapon.Type.Contains("melee"))
            {
                // Sprawdzenie, czy jednostka posiada więcej niż jedną broń
                if (InventoryManager.Instance.InventoryScrollViewContent.GetComponent<CustomDropdown>().Buttons.Count > 1)
                {
                    SaveAndLoadManager.Instance.IsLoading = true;
                    int selectedIndex = 1;

                    //  Zmienia bronie dopóki nie znajdzie takiej, którą może walczyć w zwarciu
                    for (int i = 0; i < InventoryManager.Instance.InventoryScrollViewContent.GetComponent<CustomDropdown>().Buttons.Count; i++)
                    {
                        if (weapon.Type.Contains("melee") && weapon.Id != 0) break;

                        InventoryManager.Instance.InventoryScrollViewContent.GetComponent<CustomDropdown>().SetSelectedIndex(selectedIndex);
                        InventoryManager.Instance.InventoryScrollViewContent.GetComponent<CustomDropdown>().SelectedButton = InventoryManager.Instance.InventoryScrollViewContent.GetComponent<CustomDropdown>().Buttons[selectedIndex - 1];

                        InventoryManager.Instance.GrabWeapon();
                        weapon = InventoryManager.Instance.ChooseWeaponToAttack(unit.gameObject);

                        selectedIndex++;
                    }

                    Debug.Log($"{unit.GetComponent<Stats>().Name} dobył/a {weapon.Name}.");
                    SaveAndLoadManager.Instance.IsLoading = false;
                }
                else // Oddala się od przeciwnika
                {
                    MoveAwayFromOpponent(unit, closestOpponent);

                    // Czeka aż ruch się zakończy
                    yield return new WaitUntil(() => MovementManager.Instance.IsMoving == false);

                    //Ponownie sprawdza, czy można wykonać atak dystansowy
                    if (CombatManager.Instance.ValidateRangedAttack(unit, closestOpponent.GetComponent<Unit>(), weapon, distance) == false) yield break;
                }
            }

            // Decyzja: zwykły atak czy Szaleńczy Atak?
            bool useAllOut =
                !unit.IsCharging &&                         // podczas szarży nie używamy Szaleńczego Ataku
                ShouldUseAllOutAttack(unit, closestOpponent.GetComponent<Unit>(), weapon); // dystans w zwarciu ≈ 1

            if (useAllOut)
            {
                CombatManager.Instance.ChangeAttackType("AllOutAttack");
                Debug.Log($"{unit.GetComponent<Stats>().Name} decyduje się na Szaleńczy Atak przeciwko {closestOpponent.GetComponent<Stats>().Name}.");
            }
            else
            {
                CombatManager.Instance.ChangeAttackType("StandardAttack");
            }

            Debug.Log($"<color=#4dd2ff>{unit.GetComponent<Stats>().Name} atakuje {closestOpponent.GetComponent<Stats>().Name}.</color>");
            CombatManager.Instance.Attack(unit, closestOpponent.GetComponent<Unit>());
        }
    }

    private IEnumerator AttemptToChangeDistanceAndAttack(Unit unit, GameObject closestOpponent, Weapon weapon)
    {
        // Szuka wolnej pozycji obok celu, do której droga postaci jest najkrótsza
        GameObject targetTile = CombatManager.Instance.GetTileAdjacentToTarget(unit.gameObject, closestOpponent);
        Vector2 targetTilePosition; ;

        if (targetTile != null)
        {
            targetTilePosition = new Vector2(targetTile.transform.position.x, targetTile.transform.position.y);
        }
        else
        {
            Debug.Log($"<color=#4dd2ff>{unit.GetComponent<Stats>().Name} nie jest w stanie podejść do {closestOpponent.GetComponent<Stats>().Name}.</color>");
            WaitForMovementOrAttackOpportunity(unit);
            yield break;
        }

        //Ścieżka ruchu atakującego
        List<Vector2> path = MovementManager.Instance.FindPath(unit.transform.position, targetTilePosition);

        int movementRange = unit.IsMounted && unit.Mount != null ? unit.Mount.GetComponent<Stats>().TempSz : unit.Stats.TempSz;

        if ((!weapon.Type.Contains("ranged")) && path.Count <= movementRange * 2 && path.Count >= movementRange / 2f && unit.CanDoAction && unit.CanMove && !unit.Prone && !unit.Stats.Slow) // Jeśli rywal jest w zasięgu szarży to wykonuje szarżę
        {
            Debug.Log($"<color=#4dd2ff>{unit.Stats.Name} szarżuje na {closestOpponent.GetComponent<Stats>().Name}.</color>");

            StartCoroutine(MovementManager.Instance.UpdateMovementRange(2, null, true));
            CombatManager.Instance.ChangeAttackType("Charge");

            CombatManager.Instance.Attack(unit, closestOpponent.GetComponent<Unit>());
        }
        else if (path.Count < movementRange / 2 && path.Count > 0 && unit.CanDoAction && unit.CanMove) //Wykonuje ruch w kierunku przeciwnika, a następnie atak
        {
            // Uruchomia korutynę odpowiedzialną za ruch i atak
            StartCoroutine(MoveAndAttack(unit, targetTile, closestOpponent.GetComponent<Unit>(), weapon));
        }
        else if (path.Count > 0) // Wykonuje ruch w kierunku przeciwnika
        {
            bool moved = false;

            if (unit.CanMove) // Marsz
            {
                StartCoroutine(MovementManager.Instance.UpdateMovementRange(1));
                MovementManager.Instance.MoveSelectedUnit(targetTile, unit.gameObject);
                Debug.Log($"<color=#4dd2ff>{unit.Stats.Name} idzie w stronę {closestOpponent.GetComponent<Stats>().Name}.</color>");
                moved = true;
            }

            // Czeka aż ruch się zakończy
            yield return new WaitUntil(() => MovementManager.Instance.IsMoving == false);

            if (unit.CanDoAction && !unit.Prone && !unit.Stats.Slow) // Bieg (dodatkowo)
            {
                MovementManager.Instance.Run();
                MovementManager.Instance.MoveSelectedUnit(targetTile, unit.gameObject);
                Debug.Log($"<color=#4dd2ff>{unit.Stats.Name} biegnie w stronę {closestOpponent.GetComponent<Stats>().Name}.</color>");
                moved = true;
            }

            if (!moved) // Nie ruszył się
            {
                WaitForMovementOrAttackOpportunity(unit);
                yield break;
            }
        }

        else //Gdy nie jest w stanie podejść do najbliższego przeciwnika, a stoi on poza zasięgiem jego ataku
        {
            WaitForMovementOrAttackOpportunity(unit);
        }

        // Synchronizuje collidery
        Physics2D.SyncTransforms();
    }

    IEnumerator MoveAndAttack(Unit unit, GameObject targetTile, Unit closestOpponent, Weapon weapon)
    {
        Debug.Log($"<color=#4dd2ff>{unit.Stats.Name} podchodzi do {closestOpponent.GetComponent<Stats>().Name} i atakuje.</color>");

        //Przywraca standardową szybkość
        StartCoroutine(MovementManager.Instance.UpdateMovementRange(1));

        // Ruch
        MovementManager.Instance.MoveSelectedUnit(targetTile, unit.gameObject);

        // Czeka aż ruch się zakończy
        yield return new WaitUntil(() => MovementManager.Instance.IsMoving == false);

        // Atak
        StartCoroutine(ExecuteAttack(unit, closestOpponent.gameObject, weapon, 1f));
    }

    private void MoveAwayFromOpponent(Unit unit, GameObject closestOpponent)
    {
        StartCoroutine(MovementManager.Instance.UpdateMovementRange(1));
        GameObject tile = GetTileAwayFromTarget(unit.gameObject, closestOpponent);

        if(tile != null)
        {      
            Debug.Log($"<color=#4dd2ff>{unit.Stats.Name} wycofuje się od {closestOpponent.GetComponent<Stats>().Name}.</color>");

            MovementManager.Instance.MoveSelectedUnit(tile, unit.gameObject);
        }
        else
        {
            Debug.Log($"<color=#4dd2ff>{unit.Stats.Name} nie ma dokąd uciec.</color>");
            WaitForMovementOrAttackOpportunity(unit);
        }
    }

    public void WaitForMovementOrAttackOpportunity(Unit unit)
    {
        //Resetuje szybkość jednostki
        StartCoroutine(MovementManager.Instance.UpdateMovementRange(1));

        RoundsManager.Instance.FinishTurn();
    }

    public void CheckForTargetTileOccupancy(GameObject unit)
    {
        //Zaznacza jako zajęte faktyczne pole, na którym jednostka zakończy ruch, a nie pole do którego próbowała dojść
        if (TargetTile != null)
        {
            Vector2 unitPos = new Vector2(unit.transform.position.x, unit.transform.position.y);
            if ((Vector2)TargetTile.transform.position != unitPos)
            {
                TargetTile.IsOccupied = false;

                // Ignoruje warstwę "Unit" podczas wykrywania kolizji, skupiając się tylko na warstwie 0 (default)
                Collider2D collider = Physics2D.OverlapPoint(unitPos, 0);
                if (collider != null && collider.GetComponent<Tile>() != null)
                {
                    collider.GetComponent<Tile>().IsOccupied = true;
                }
            }
        }
        TargetTile = null;
    }
    public (GameObject opponent, float distance) GetClosestOpponent(GameObject attacker, bool isRangedAttack)
    {
        GameObject closestOpponent = null;
        float minDistance = Mathf.Infinity;

        foreach (var pair in InitiativeQueueManager.Instance.InitiativeQueue)
        {
            if (pair.Key == null) continue;
            if (pair.Key.gameObject == attacker || pair.Key.CompareTag(attacker.tag)) continue;

            float distance = Vector2.Distance(pair.Key.transform.position, attacker.transform.position);

            if (isRangedAttack && distance < 1.5f) continue;

            if (distance < minDistance)
            {
                closestOpponent = pair.Key.gameObject;
                minDistance = distance;

                if (!isRangedAttack && distance < 1.5f)
                {
                    break;
                }
            }
        }

        return (closestOpponent, minDistance);
    }

    public GameObject GetTileAwayFromTarget(GameObject attacker, GameObject target)
    {
        if (attacker == null || target == null) return null;

        Vector2 attackerPos = attacker.transform.position;
        int moveRange = attacker.GetComponent<Unit>().IsMounted && attacker.GetComponent<Unit>().Mount != null ? attacker.GetComponent<Unit>().Mount.GetComponent<Stats>().TempSz : attacker.GetComponent<Stats>().TempSz;

        GameObject bestTile = null;
        int bestPathLength = -1; // początkowo -1, bo chcemy maksymalne path.Count <= Sz

        // Przeszukujemy pola w promieniu `Sz` od jednostki
        for (int x = -moveRange; x <= moveRange; x++)
        {
            for (int y = -moveRange; y <= moveRange; y++)
            {
                Vector2 pos = attackerPos + new Vector2(x, y);

                GameObject tile = GameObject.Find($"Tile {pos.x - GridManager.Instance.transform.position.x} {pos.y - GridManager.Instance.transform.position.y}");

                if (tile == null || tile.GetComponent<Tile>().IsOccupied) continue;

                List<Vector2> path = MovementManager.Instance.FindPath(attackerPos, pos);

                if (path.Count == 0 || path.Count > moveRange) continue;

                // Preferuj dokładnie równe `Sz`, ale jeśli nie ma – weź największe mniejsze
                if (path.Count == moveRange)
                {
                    return tile; // priorytet – idealna odległość
                }

                if (path.Count > bestPathLength)
                {
                    bestPathLength = path.Count;
                    bestTile = tile;
                }
            }
        }

        return bestTile; // jeśli nie znaleziono idealnego, zwraca najlepszy możliwy
    }

    private bool ShouldUseAllOutAttack(Unit attacker, Unit target, Weapon attackerWeapon)
    {
        // Tylko broń do walki wręcz
        if (attackerWeapon == null || !attackerWeapon.Type.Contains("melee") || !_allOutAttackEnabled)
            return false;

        // --- ATAK ---

        int attackModifier = CombatManager.Instance.CalculateAttackModifier(attacker, attackerWeapon, target);
        int attackValueStandard = attacker.Stats.Zr + attackModifier;

        int bestAttr = Mathf.Max(attacker.Stats.S, attacker.Stats.SW);
        int attackValueAllOut = bestAttr + attackModifier;

        // --- OBRONA PRZECIWNIKA ---

        Inventory defInventory = target.Stats.GetComponent<Inventory>();
        Weapon defaultDefWeapon = InventoryManager.Instance.ChooseWeaponToAttack(target.gameObject);

        bool hasMeleeWeapon = defInventory.EquippedWeapons.Any(w => w != null && w.Type.Contains("melee"));
        bool hasShield = defInventory.EquippedWeapons.Any(w => w != null && w.Type.Contains("shield"));
        bool canParry = hasMeleeWeapon || hasShield;

        Weapon parryWeapon = canParry ? CombatManager.Instance.GetBestParryWeapon(target.Stats, defaultDefWeapon) : null;
        int parryModifier = canParry ? CombatManager.Instance.CalculateParryModifier(target, target.Stats, attacker.Stats, parryWeapon) : 0;
        int dodgeModifier = CombatManager.Instance.CalculateDodgeModifier(target, attacker);

        int parryValue = target.Stats.Zr + parryModifier;
        int dodgeValue = target.Stats.Zw + dodgeModifier;

        int defenceValue = canParry ? Mathf.Max(parryValue, dodgeValue) : dodgeValue;

        int deltaStandard = attackValueStandard - defenceValue;
        int deltaAllOut = attackValueAllOut - defenceValue;

        // --- PROSTE BEZPIECZNIKI ---

        // Nie ryzykuj Szaleńczego Ataku, jeśli jesteśmy już mocno poranieni
        bool isAttackerHealthy = attacker.Stats.TempHealth > attacker.Stats.MaxHealth / 3;
        if (!isAttackerHealthy)
            return false;

        // Szaleńczy Atak musi rzeczywiście poprawiać szanse, inaczej nie ma sensu
        if (deltaAllOut <= deltaStandard)
            return false;

        // Wymagamy wyraźnie lepszej przewagi (tu próg 3 "oczka")
        int gain = deltaAllOut - deltaStandard;
        return gain >= 3;
    }

}
