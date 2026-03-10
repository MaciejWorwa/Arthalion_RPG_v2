using System.Linq;
using TMPro;
using UnityEngine;


public class MountsManager : MonoBehaviour
{
    // Prywatne statyczne pole przechowujące instancję
    private static MountsManager instance;

    // Publiczny dostęp do instancji
    public static MountsManager Instance
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

    public static Unit SelectedMount;
    public Unit ActiveMount;
    public Transform MountsScrollViewContent;
    [SerializeField] private GameObject _mountOptionPrefab; // Prefab odpowiadający każdej jednostce na liście wierzchowców
    private Color _defaultColor = new Color(0f, 0f, 0f, 0f); // Domyślny kolor przycisku
    private Color _selectedColor = new Color(0f, 0f, 0f, 0.5f); // Kolor wybranego przycisku (zaznaczonej jednostki)
    private Color _activeColor = new Color(0.15f, 1f, 0.45f, 0.2f); // Kolor aktywnego przycisku (aktualnie dosiadany wierzchowiec)
    private Color _selectedActiveColor = new Color(0.08f, 0.5f, 0.22f, 0.5f); // Kolor wybranego przycisku, gdy jednocześnie jest to obecnie dosiadany wierzchowiec

    [SerializeField] private UnityEngine.UI.Button _mountButton;
    [SerializeField] private GameObject _mountsPanel; // Panel do wyboru wierzchowca

    public void DisplayMountsList()
    {
        if (Unit.SelectedUnit == null) return;

        // Resetuje wyświetlaną kolejkę, usuwając wszystkie obiekty "dzieci"
        ResetScrollViewContent(MountsScrollViewContent);

        ActiveMount = null;

        // Ustala wyświetlaną kolejkę inicjatywy
        foreach (Unit unit in UnitsManager.Instance.AllUnits)
        {
            if(unit == null) continue;

            if(unit.CompareTag(Unit.SelectedUnit.tag) && unit != Unit.SelectedUnit.GetComponent<Unit>() && Vector2.Distance(unit.transform.position, Unit.SelectedUnit.transform.position) < 1.5f && unit.GetComponent<Stats>().Size > Unit.SelectedUnit.GetComponent<Stats>().Size && !unit.HasRider)
            {
                // Dodaje jednostkę do głównej kolejki ScrollViewContent
                GameObject optionObj = CreateOption(unit, MountsScrollViewContent);

                // Sprawdza, czy jest to obecnie dosiadany mount przez aktywną jednostkę
                if (Unit.SelectedUnit.GetComponent<Unit>().Mount == unit && Unit.SelectedUnit.GetComponent<Unit>().IsMounted)
                {
                    ActiveMount = unit;
                    SetOptionColor(optionObj, _activeColor);
                }

                // Wyróżnia zaznaczoną jednostkę
                if (SelectedMount != null && unit == SelectedMount)
                {
                    Color selectedColor = unit == ActiveMount ? _selectedActiveColor : _selectedColor;
                    SetOptionColor(optionObj, selectedColor);
                }
                else if (unit != ActiveMount)
                {
                    SetOptionColor(optionObj, _defaultColor);
                }
            } 
        }
    }

    private void ResetScrollViewContent(Transform scrollViewContent)
    {
        for (int i = scrollViewContent.childCount - 1; i >= 0; i--)
        {
            Transform child = scrollViewContent.GetChild(i);
            Destroy(child.gameObject);
        }
    }

    private GameObject CreateOption(Unit unit, Transform scrollViewContent)
    {
        GameObject optionObj = Instantiate(_mountOptionPrefab, scrollViewContent);

        // Odniesienie do nazwy postaci
        TextMeshProUGUI nameText = optionObj.transform.Find("Name_Text").GetComponent<TextMeshProUGUI>();
        nameText.text = unit.GetComponent<Stats>().Name;

        return optionObj;
    }

    private void SetOptionColor(GameObject optionObj, Color color)
    {
        optionObj.GetComponent<UnityEngine.UI.Image>().color = color;
    }

    public void GetOnSelectedMount()
    {
        if(Unit.SelectedUnit != null)
        {
            Unit unit = Unit.SelectedUnit.GetComponent<Unit>();

            if (unit.Mount != SelectedMount)
            {
                unit.Mount = SelectedMount;
                unit.MountId = SelectedMount.UnitId;
                GetOnMount();
                _mountsPanel.SetActive(false);
            }
            //else
            //{
            //    unit.Mount = null;
            //    unit.MountId = 0;
            //}

            DisplayMountsList();
            UpdateMountButtonColor();
            GridManager.Instance.HighlightTilesInMovementRange(unit.Stats);
        }
    }

    public void GetOnMountByButton()
    {
        GetOnMount();
    }

    public void GetOnMount(Unit unit = null, bool isLoading = false)
    {
        if (unit == null && Unit.SelectedUnit == null) return;
        if(unit == null)
        {
            unit = Unit.SelectedUnit.GetComponent<Unit>();
        }

        if(isLoading)
        {
            if (!unit.IsMounted) return;
            unit.Mount = FindMountById(unit.MountId);
            if (unit.Mount == null) return;
            unit.Mount.HasRider = true;

            unit.Mount.transform.position = unit.transform.position;
            unit.Mount.transform.SetParent(unit.transform);
            InitiativeQueueManager.Instance.RemoveUnitFromInitiativeQueue(unit.Mount);
            unit.Mount.gameObject.SetActive(false);

            unit.Stats.TempSz = unit.Mount.GetComponent<Stats>().Sz;

            return;
        }

        // Jeśli nie znalazło, otwiera panel do wyboru wierzchowca
        if (unit.Mount == null)
        {
            if (MountsScrollViewContent.childCount == 0)
            {
                Debug.Log($"Aby dosiąść wierzchowca, stań obok sojuszniczej jednostki o większym rozmiarze.");
            }
            else _mountsPanel.SetActive(true);

            return;
        }

        unit.IsMounted = !unit.IsMounted;
        unit.Mount.HasRider = !unit.Mount.HasRider;

        if(unit.IsMounted)
        {
            unit.Mount.transform.position = unit.transform.position;
            unit.Mount.transform.SetParent(unit.transform);
            InitiativeQueueManager.Instance.RemoveUnitFromInitiativeQueue(unit.Mount);
            unit.Mount.gameObject.SetActive(false);

            unit.Stats.TempSz = unit.Mount.GetComponent<Stats>().Sz;

            if (unit.Mount.GetComponent<Stats>().Flight != 0) unit.Stats.TempSz = unit.Mount.GetComponent<Stats>().Flight;

            Debug.Log($"{unit.Stats.Name} dosiadł/a {unit.Mount.GetComponent<Stats>().Name}.");
        }
        else
        {
            Vector2 riderPos = unit.transform.position;
            Vector2[] adjacentPositions = {
                    riderPos + Vector2.right,
                    riderPos + Vector2.left,
                    riderPos + Vector2.up,
                    riderPos + Vector2.down,
                    riderPos + new Vector2(1, 1),
                    riderPos + new Vector2(-1, -1),
                    riderPos + new Vector2(-1, 1),
                    riderPos + new Vector2(1, -1)
                };

            Vector2 newMountPosition = riderPos;
            foreach (var pos in adjacentPositions)
            {
                Collider2D[] colliders = Physics2D.OverlapPointAll(pos);
                foreach(Collider2D collider in colliders)
                {
                    if (collider != null && collider.gameObject.CompareTag("Tile") && !collider.gameObject.GetComponent<Tile>().IsOccupied)
                    {
                        newMountPosition = pos;
                        break;
                    }
                }
                if (newMountPosition != riderPos) break;
            }
            unit.Mount.transform.position = newMountPosition;

            unit.Mount.transform.SetParent(GameObject.Find("----------Units-------------------").transform);
            InitiativeQueueManager.Instance.AddUnitToInitiativeQueue(unit.Mount);
            unit.Mount.gameObject.SetActive(true);

            unit.Stats.TempSz = unit.Stats.Sz;
            Debug.Log($"{unit.Stats.Name} zsiadł/a z {unit.Mount.GetComponent<Stats>().Name}.");
            unit.Mount = null;
            unit.MountId = 0;
        }

        UpdateMountButtonColor();
        InitiativeQueueManager.Instance.UpdateInitiativeQueue();
        GridManager.Instance.CheckTileOccupancy();
        GridManager.Instance.HighlightTilesInMovementRange(unit.Stats);
        CombatManager.Instance.SetActionsButtonsInteractable();
        CombatManager.Instance.ChangeAttackType();
    }

    public void LoadMountedUnits()
    {
        foreach (var unit in UnitsManager.Instance.AllUnits)
        {
            // Ustawiamy wierzchowce na niewidoczne
            if (unit.HasRider)
            {
                unit.gameObject.SetActive(false);
                Unit rider = null;
                foreach (Unit u in UnitsManager.Instance.AllUnits)
                {
                    if (u.MountId == unit.UnitId)
                    {
                        rider = u;
                        break;
                    }
                }
                if (rider != null)
                {
                    unit.transform.position = rider.transform.position;
                    unit.transform.SetParent(rider.transform);
                }
            }
        }
    }

    public void UpdateMountButtonColor()
    {
        if (_mountButton != null)
        {
            _mountButton.GetComponent<UnityEngine.UI.Image>().color = UnityEngine.Color.white;
        }

        if (Unit.SelectedUnit == null) return;

        Unit unit = Unit.SelectedUnit.GetComponent<Unit>();
        if (unit == null) return;

        if (_mountButton != null)
        {
            _mountButton.GetComponent<UnityEngine.UI.Image>().color =
                unit.IsMounted && unit.Mount != null ? UnityEngine.Color.green : UnityEngine.Color.white;
        }

        UpdateMountIcon(unit);
    }

    public void UpdateMountIcon(Unit unit)
    {
        if (unit == null) return;

        Transform iconTransform = unit.transform.Find("Canvas/Mount_image");
        if (iconTransform == null) return;

        bool showIcon = unit.IsMounted && unit.Mount != null;
        iconTransform.gameObject.SetActive(showIcon);

        if (!showIcon) return;

        UIButtonTooltip tooltip = iconTransform.GetComponent<UIButtonTooltip>();
        Stats mountStats = unit.Mount.GetComponent<Stats>();
        if (tooltip != null && mountStats != null)
        {
            tooltip.ChangeTooltipText(mountStats.Name);
        }
    }

    public void DisplayAllMountIcons()
    {
        if (InitiativeQueueManager.Instance == null) return;

        foreach (var pair in InitiativeQueueManager.Instance.InitiativeQueue)
        {
            UpdateMountIcon(pair.Key);
        }
    }

    public Unit FindMountById(int Id)
    {
        foreach (Unit unit in UnitsManager.Instance.AllUnits)
        {
            if (unit.UnitId == Id)
            {
                return unit;
            }
        }
        return null;
    }

    public void ShowOrHideMountButton(bool value)
    {
        _mountButton.gameObject.SetActive(value);
    }
}


