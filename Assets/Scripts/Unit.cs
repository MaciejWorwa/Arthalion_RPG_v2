using NUnit.Framework;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.VisualScripting.Antlr3.Runtime.Misc;
using UnityEngine;

public class Unit : MonoBehaviour
{
    public int UnitId; // Unikalny Id jednostki

    public static GameObject SelectedUnit;
    public static GameObject LastSelectedUnit;
    public string TokenFilePath;
    public Color DefaultColor;
    public Color HighlightColor;

    public bool IsSelected = false;
    public bool IsTurnFinished; // Określa, czy postać zakończyła swoją turę (bo mogła to zrobić, np. zostawiając jedną akcję)
    public bool IsRunning; // Biegnie
    public bool IsCharging; // Szarżuje
    public bool IsFlying; // Leci
    public bool IsRetreating; // Wycofuje się

    [Header("Stany")]
    public bool Ablaze; // Podpalenie
    public int Bleeding; // Krwawienie
    public bool Blinded; // Oślepienie
    public bool Entangled; // Unierumomienie
    public int Poison; // Zatrucie
    public bool Prone; // Powalenie
    public bool Scared; // Strach
    public bool Unconscious; // Utrata Przytomności

    public bool Grappled; // Pochwycenie
    public int GrappledUnitId; // Cel pochwycenia
    public int EntangledUnitId; // Cel unieruchomienia

    public int FearTestedLevel; // max poziom strachu, przeciw któremu ta jednostka już testowała Opanowanie (0 = brak)

    [Header("Modyfikatory")]
    public int AimingBonus;

    [Header("Dostępne działania")]
    public bool CanMove = true;
    public bool CanDoAction = true;
    public bool CanCastSpell = false;

    public Stats Stats;
    public TMP_Text NameDisplay;
    public TMP_Text HealthDisplay;

    public Stats LastAttackerStats; // Ostatni przeciwnik, który zadał obrażenia tej jednostce (jest to niezbędne do aktualizowania osiągnięcia "Najsilniejszy pokonany przeciwnik" poza trybem automatycznej śmierci)

    public int MountId;
    public Unit Mount; // Wierzchowiec
    public bool IsMounted; // Zmienna określająca, czy jednostka w danej chwili dosiada wierzchowca, czy nie
    public bool HasRider; // Zmienna określająca, czy jednostka w danej chwili jest przez kogoś dosiadana

    void Start()
    {
        Stats = GetComponent<Stats>();

        DisplayUnitName();

        StartCoroutine(MovementManager.Instance.UpdateMovementRange(1, this));

        if (Stats.Name.Contains(Stats.Race)) // DO POKMINIENIA, JAKI INNY WARUNEK DAĆ, BO TEN NIE JEST IDEALNY, BO KTOŚ MOŻE NAZWAĆ ZAPISANEGO GOBLINA NP. "FAJNY GOBLIN"
        {
            Stats.TempHealth = Stats.MaxHealth;
        }

        DisplayUnitHealthPoints();

        //Aktualizuje kolejkę inicjatywy
        InitiativeQueueManager.Instance.UpdateInitiativeQueue();
    }
    private void OnMouseUp()
    {
        if (GameManager.Instance.IsPointerOverUI() || GameManager.IsMapHidingMode || UnitsManager.IsMultipleUnitsSelecting || MovementManager.Instance.IsMoving) return;

        SelectUnit();
    }

    private void OnMouseOver()
    {
        if (Input.GetMouseButton(1) && SelectedUnit != null && SelectedUnit != this.gameObject && !MagicManager.IsTargetSelecting)
        {
            StartCoroutine(CombatManager.Instance.OpenHitLocationPanel());
        }
        else if (Input.GetMouseButtonUp(1) && SelectedUnit != null && SelectedUnit != this.gameObject && !MagicManager.IsTargetSelecting)
        {
            //Sprawdza, czy atakowanym jest nasz sojusznik i czy tryb Friendly Fire jest aktywny
            if (GameManager.IsFriendlyFire == false && this.gameObject.CompareTag(SelectedUnit.tag))
            {
                Debug.Log("Nie możesz atakować swoich sojuszników. Jest to możliwe tylko w trybie Friendly Fire.");
                return;
            }

            if (CombatManager.Instance.AttackTypes["Grappling"]) // Zapasy
            {
                CombatManager.Instance.Grappling(SelectedUnit.GetComponent<Unit>(), this);
            }
            else if (IsMounted && Mount != null)
            {
                StartCoroutine(CombatManager.Instance.SelectRiderOrMount(this));
            }
            else // Atak
            {
                CombatManager.Instance.Attack(SelectedUnit.GetComponent<Unit>(), this, false);
            }
        }
        else if (Input.GetMouseButtonUp(1) && SelectedUnit != null && MagicManager.IsTargetSelecting)
        {
            //StartCoroutine(MagicManager.Instance.CastSpell(this.gameObject));
            MagicManager.Instance.Targets.Add(this.gameObject);

            if(SelectedUnit.GetComponent<Spell>() != null && MagicManager.Instance.Targets.Count < SelectedUnit.GetComponent<Spell>().Targets)
            {
                Debug.Log($"Wskaż kolejny cel. Musisz wybrać {SelectedUnit.GetComponent<Spell>().Targets} unikalne cele. Dotychczas wybrano {MagicManager.Instance.Targets.Count}. Aby pominąć wybór kolejnych - naciśnij \"Enter\".");
            }
        }
    }
    public void SelectUnit()
    {
        if (!gameObject.activeSelf) return;

        if (SelectedUnit == null)
        {
            SelectedUnit = this.gameObject;

            //Odświeża listę ekwipunku
            InventoryManager.Instance.InventoryScrollViewContent.GetComponent<CustomDropdown>().SelectedIndex = 0;
            InventoryManager.Instance.UpdateInventoryDropdown(SelectedUnit.GetComponent<Inventory>().AllWeapons, true);
            InventoryManager.Instance.DisplayEncumbrance(Stats);

            CombatManager.Instance.SetActionsButtonsInteractable();
        }
        else if (SelectedUnit == this.gameObject)
        {
            CombatManager.Instance.ChangeAttackType(); // Resetuje wybrany typ ataku
            StartCoroutine(MovementManager.Instance.UpdateMovementRange(1)); //Resetuje szarżę lub bieg, jeśli były aktywne
            MovementManager.Instance.Retreat(false); //Resetuje bezpieczny odwrót

            //Zamyka aktywne panele
            GameManager.Instance.HideActivePanels();

            LastSelectedUnit = SelectedUnit;
            SelectedUnit = null;
        }
        else
        {
            SelectedUnit.GetComponent<Unit>().IsSelected = false;

            ChangeUnitColor(SelectedUnit);
            LastSelectedUnit = SelectedUnit;
            SelectedUnit = this.gameObject;

            CombatManager.Instance.ChangeAttackType(); // Resetuje wybrany typ ataku
            StartCoroutine(MovementManager.Instance.UpdateMovementRange(1)); //Resetuje szarżę lub bieg, jeśli były aktywne   
            MovementManager.Instance.Retreat(false); //Resetuje bezpieczny odwrót    

            //Odświeża listę ekwipunku
            InventoryManager.Instance.InventoryScrollViewContent.GetComponent<CustomDropdown>().SelectedIndex = 0;
            InventoryManager.Instance.UpdateInventoryDropdown(SelectedUnit.GetComponent<Inventory>().AllWeapons, true);
            InventoryManager.Instance.DisplayEncumbrance(Stats);
        }
        IsSelected = SelectedUnit == this.gameObject;
        ChangeUnitColor(this.gameObject);

        if(IsSelected && Entangled && !Grappled && CanDoAction)
        {
            Stats entanglingUnitStats = null;
            foreach (var u in UnitsManager.Instance.AllUnits)
            {
                if (u.EntangledUnitId == UnitId)
                {
                    entanglingUnitStats = u.GetComponent<Stats>();
                }
            }

            if(entanglingUnitStats != null) StartCoroutine(CombatManager.Instance.EscapeFromEntanglement(entanglingUnitStats, Stats));
        }

        if(IsSelected && (EntangledUnitId != 0))
        {
            bool entangledUnitExist = false;

            foreach (var u in UnitsManager.Instance.AllUnits)
            {
                if (u.UnitId == EntangledUnitId && u.Entangled)
                {
                    entangledUnitExist = true;
                }
            }

            if (!entangledUnitExist)
            {
                EntangledUnitId = 0;
            }
        }

        if (IsSelected && (GrappledUnitId != 0))
        {
            bool grappledUnitExist = false;

            foreach (var u in UnitsManager.Instance.AllUnits)
            {
                if (u.UnitId == GrappledUnitId && u.Grappled)
                {
                    grappledUnitExist = true;
                }
            }

            if (!grappledUnitExist)
            {
                GrappledUnitId = 0;
            }
        }

        // Dla jednostek z Szybkością 0, unieruchomionych lub pochwyconych wyłącza możliwość poruszania się
        if(IsSelected)
        {
            if (GetComponent<Stats>().Sz == 0 || Entangled || Grappled)
            {
                CanMove = false;
                MovementManager.Instance.SetCanMoveToggle(false);
            }
        }

        //// Jeżeli MountId nie jest puste, ale Mount tak, znajduje wierzchowca na podstawie Id
        //if (MountId != 0 && Mount == null)
        //{
        //    Mount = MountsManager.Instance.FindMountById(MountId);
        //}

        // Aktualizuje szybkość, jeśli jednostka dosiada wierzchowca
        if (IsMounted && Mount != null)
        {
            Stats.TempSz = Mount.GetComponent<Stats>().TempSz;
            if (Mount.GetComponent<Stats>().Flight != 0) Stats.TempSz = Mount.GetComponent<Stats>().Flight;
        }

        GridManager.Instance.HighlightTilesInMovementRange(Stats);

        //Wczytuje osiągnięcia jednostki
        UnitsManager.Instance.LoadAchievements(SelectedUnit);

        //Aktualizuje panel ze statystykami jednostki
        UnitsManager.Instance.UpdateUnitPanel(SelectedUnit);
        StatesManager.Instance.LoadUnitStates();

        //Zaznacza lub odznacza jednostkę na kolejce inicjatywy
        InitiativeQueueManager.Instance.UpdateInitiativeQueue();

        //Zresetowanie rzucania zaklęć
        MagicManager.Instance.ResetSpellCasting();
    }

    public void ChangeUnitColor(GameObject unit)
    {
        Material mat = unit.GetComponent<Renderer>().material;
        Unit unitComponent = unit.GetComponent<Unit>();

        // Ustawia kolor główny (diffuse)
        mat.SetColor("_Color", unitComponent.DefaultColor);

        // Włącza emisję przy zaznaczeniu
        Color emission = IsSelected ? unitComponent.DefaultColor * 1f : Color.black;
        mat.SetColor("_EmissionColor", emission);

        // Jeśli nie ma obrazka tokena, ustaw też jego kolor i emisję
        if (unitComponent.TokenFilePath.Length < 1)
        {
            SpriteRenderer tokenRenderer = unit.transform.Find("Token").GetComponent<SpriteRenderer>();
            tokenRenderer.material = mat; ;

            tokenRenderer.material.SetColor("_Color", unitComponent.DefaultColor);
            tokenRenderer.material.SetColor("_EmissionColor", emission * 0.8f);
        }
    }

    public void DisplayUnitName()
    {
        if (NameDisplay == null) return;

        if (Stats.Name != null && Stats.Name.Length > 1)
        {
            NameDisplay.text = Stats.Name;
        }
        else
        {
            NameDisplay.text = this.gameObject.name;
            Stats.Name = this.gameObject.name;
        }

        if (GameManager.IsNamesHidingMode)
        {
            HideUnitName();
        }
    }

    public void HideUnitName()
    {
        if (NameDisplay == null) return;

        NameDisplay.text = "";
    }

    public void DisplayUnitHealthPoints()
    {
        if (HealthDisplay == null) return;

        HealthDisplay.text = Stats.TempHealth + "/" + Stats.MaxHealth;

        if (GameManager.IsHealthPointsHidingMode || GameManager.IsStatsHidingMode && this.gameObject.CompareTag("EnemyUnit"))
        {
            HideUnitHealthPoints();
        }
        else
        {
            ResetUnitHealthState();
        }
    }

    public void HideUnitHealthPoints()
    {
        UpdateUnitHealthState();

        if (HealthDisplay == null) return;

        HealthDisplay.text = "";
    }

    private void UpdateUnitHealthState()
    {
        ResetUnitHealthState();

        //Wyświetla symbol obrazujący stan zdrowia jednostki
        if (Stats.TempHealth < 0)
        {
            gameObject.transform.Find("Canvas/Dead_image").gameObject.SetActive(true);
        }
        else if (Stats.TempHealth <= Stats.MaxHealth / 3)
        {
            gameObject.transform.Find("Canvas/Heavy_wounded_image").gameObject.SetActive(true);
        }
        else if (Stats.TempHealth < Stats.MaxHealth)
        {
            gameObject.transform.Find("Canvas/Wounded_image").gameObject.SetActive(true);
        }
    }

    private void ResetUnitHealthState()
    {
        transform.Find("Canvas/Dead_image").gameObject.SetActive(false);
        transform.Find("Canvas/Heavy_wounded_image").gameObject.SetActive(false);
        transform.Find("Canvas/Wounded_image").gameObject.SetActive(false);
    }
}
