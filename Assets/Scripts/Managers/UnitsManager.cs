using NUnit.Framework.Internal;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using static UnityEngine.GraphicsBuffer;
using static UnityEngine.Rendering.DebugUI;

public class UnitsManager : MonoBehaviour
{
    // Prywatne statyczne pole przechowujące instancję
    private static UnitsManager instance;

    // Publiczny dostęp do instancji
    public static UnitsManager Instance
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

    [SerializeField] private GameObject _unitPanel;
    [SerializeField] private UnityEngine.UI.Button _spellbookButton;
    [SerializeField] private GameObject _spellListPanel;
    [SerializeField] private TMP_Text _raceDisplay;
    [SerializeField] private TMP_Text _healthDisplay;
    [SerializeField] private UnityEngine.UI.Slider _healthBar;
    [SerializeField] private UnityEngine.UI.Image _tokenDisplay;
    [SerializeField] private UnityEngine.UI.Image _tokenBorder;
    [SerializeField] private GameObject _unitPrefab;
    [SerializeField] private CustomDropdown _unitsDropdown;
    public Transform UnitsScrollViewContent;
    [SerializeField] private UnityEngine.UI.Toggle _unitTagToggle;
    [SerializeField] private UnityEngine.UI.Button _createUnitButton; // Przycisk do tworzenia jednostek na losowych pozycjach
    [SerializeField] private UnityEngine.UI.Button _removeUnitButton;
    public UnityEngine.UI.Toggle SortSavedUnitsByDateToggle;
    [SerializeField] private UnityEngine.UI.Button _selectUnitsButton; // Przycisk do zaznaczania wielu jednostek
    [SerializeField] private UnityEngine.UI.Button _removeSavedUnitFromListButton; // Przycisk do usuwania zapisanych jednostek z listy
    [SerializeField] private UnityEngine.UI.Button _updateUnitButton;
    [SerializeField] private UnityEngine.UI.Button _removeUnitConfirmButton;
    [SerializeField] private GameObject _removeUnitConfirmPanel;
    public static bool IsTileSelecting;
    public static bool IsMultipleUnitsSelecting = false;
    public static bool IsUnitRemoving = false;
    public static bool IsUnitEditing = false;
    public bool IsSavedUnitsManaging = false;
    public List<Unit> AllUnits = new List<Unit>();

    private bool _isPopulatingUI;

    void Start()
    {
        //Wczytuje listę wszystkich jednostek
        DataManager.Instance.LoadAndUpdateStats();

        _removeUnitConfirmButton.onClick.AddListener(() =>
        {
            if (Unit.SelectedUnit != null)
            {
                DestroyUnit(Unit.SelectedUnit);
                _removeUnitConfirmPanel.SetActive(false);
            }
            else
            {
                Debug.Log("Aby usunąć jednostkę, musisz najpierw ją wybrać.");
            }
        });
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Delete) && (Unit.SelectedUnit != null || (AreaSelector.Instance.SelectedUnits != null && AreaSelector.Instance.SelectedUnits.Count > 1)))
        {
            if (_removeUnitConfirmPanel.activeSelf == false)
            {
                _removeUnitConfirmPanel.SetActive(true);
            }
            else
            {
                //Jeśli jest zaznaczone więcej jednostek, to usuwa wszystkie
                if (AreaSelector.Instance.SelectedUnits != null && AreaSelector.Instance.SelectedUnits.Count > 1)
                {
                    for (int i = AreaSelector.Instance.SelectedUnits.Count - 1; i >= 0; i--)
                    {
                        Unit unit = AreaSelector.Instance.SelectedUnits[i];

                        //Usuwa też wierzchowce
                        if (unit.Mount != null && unit.IsMounted)
                        {
                            DestroyUnit(unit.Mount.gameObject);
                        }

                        DestroyUnit(unit.gameObject);
                    }
                    AreaSelector.Instance.SelectedUnits.Clear();
                }
                else
                {
                    DestroyUnit(Unit.SelectedUnit);
                }
                _removeUnitConfirmPanel.SetActive(false);
            }
        }
    }

    #region Creating units
    public void CreateUnitMode()
    {
        IsTileSelecting = true;

        Debug.Log("Wybierz pole, na którym chcesz stworzyć jednostkę.");
        return;
    }

    public void CreateUnitOnSelectedTile(Vector2 position)
    {
        CreateUnit(_unitsDropdown.GetSelectedIndex(), "", position);

        //Resetuje kolor przycisku z wybraną jednostką na liście jednostek
        CreateUnitButton.SelectedUnitButtonImage.color = new Color(0.55f, 0.66f, 0.66f, 0.05f);
    }

    public void CreateUnitOnRandomTile()
    {
        List<Vector2> availablePositions = GridManager.Instance.AvailablePositions();
        Vector2 position = Vector2.zero;

        if (!SaveAndLoadManager.Instance.IsLoading)
        {
            if (availablePositions.Count == 0)
            {
                Debug.Log("Nie można stworzyć nowej jednostki. Brak wolnych pól.");
                return;
            }

            // Wybranie losowej pozycji z dostępnych
            int randomIndex = UnityEngine.Random.Range(0, availablePositions.Count);
            position = availablePositions[randomIndex];
        }

        CreateUnit(_unitsDropdown.GetSelectedIndex(), "", position);
    }

    public GameObject CreateUnit(int unitId, string unitName, Vector2 position)
    {
        if (_unitsDropdown.SelectedButton == null && SaveAndLoadManager.Instance.IsLoading != true)
        {
            Debug.Log("Wybierz jednostkę z listy.");
            return null;
        }

        // Pole na którym chcemy stworzyć jednostkę
        GameObject selectedTile = GameObject.Find($"Tile {position.x - GridManager.Instance.transform.position.x} {position.y - GridManager.Instance.transform.position.y}");

        //Gdy próbujemy wczytać jednostkę na polu, które nie istnieje (bo np. siatka jest obecnie mniejsza niż siatka, na której były zapisywane jednostki) lub jest zajęte to wybiera im losową pozycję
        if ((selectedTile == null || selectedTile.GetComponent<Tile>().IsOccupied) && SaveAndLoadManager.Instance.IsLoading == true)
        {
            List<Vector2> availablePositions = GridManager.Instance.AvailablePositions();

            if (availablePositions.Count == 0)
            {
                Debug.Log("Nie można stworzyć nowej jednostki. Brak wolnych pól.");
                return null;
            }

            // Wybranie losowej pozycji z dostępnych
            int randomIndex = UnityEngine.Random.Range(0, availablePositions.Count);
            position = availablePositions[randomIndex];

            selectedTile = GameObject.Find($"Tile {position.x - GridManager.Instance.transform.position.x} {position.y - GridManager.Instance.transform.position.y}");
        }
        else if (selectedTile == null)
        {
            Debug.Log("Nie można stworzyć jednostki.");
            return null;
        }

        //Odnacza jednostkę, jeśli jakaś jest zaznaczona
        if (Unit.SelectedUnit != null && IsTileSelecting)
        {
            Unit.SelectedUnit.GetComponent<Unit>().SelectUnit();
        }

        //Tworzy nową postać na odpowiedniej pozycji
        GameObject newUnitObject = Instantiate(_unitPrefab, position, Quaternion.identity);
        Stats stats = newUnitObject.GetComponent<Stats>();
        Unit unit = newUnitObject.GetComponent<Unit>();

        //Umieszcza postać jako dziecko EmptyObject'u do którego są podpięte wszystkie jednostki
        newUnitObject.transform.SetParent(GameObject.Find("----------Units-------------------").transform);

        //Ustawia Id postaci, które będzie definiować jego rasę i statystyki
        stats.Id = unitId;

        //Zmienia status wybranego pola na zajęte
        selectedTile.GetComponent<Tile>().IsOccupied = true;

        // Aktualizuje liczbę wszystkich postaci
        AllUnits.Add(unit);

        // Ustala unikalne Id jednostki
        int newUnitId = 1;
        bool idExists;
        // Pętla sprawdzająca, czy inne jednostki mają takie samo Id
        do
        {
            idExists = false;

            foreach (var u in AllUnits)
            {
                if (u.UnitId == newUnitId)
                {
                    idExists = true;
                    newUnitId++; // Zwiększa id i sprawdza ponownie
                    break;
                }
            }
        }
        while (idExists);
        unit.UnitId = newUnitId;

        //Ustala nazwę jednostki (potrzebne, do wczytywania jednostek z listy zapisanych jednostek)
        if (_unitsDropdown.SelectedButton != null && IsSavedUnitsManaging)
        {
            stats.Name = _unitsDropdown.SelectedButton.GetComponentInChildren<TextMeshProUGUI>().text;
        }

        //Wczytuje statystyki dla danego typu jednostki
        DataManager.Instance.LoadAndUpdateStats(newUnitObject);

        //Ustala nazwę GameObjectu jednostki
        if (unitName.Length < 1)
        {
            newUnitObject.name = stats.Race + $" {newUnitId}";
        }
        else
        {
            newUnitObject.name = unitName;
        }

        // Wczytuje dane zapisanej jednostki
        if (IsSavedUnitsManaging && IsTileSelecting)
        {
            //Jeżeli gra już jest w trakcie wczytywania to nie powielamy tego. Jest to istotne, żeby nie wystąpiły błędy przy wczytywaniu gry, jeśli na mapie są zapisane jednostki
            bool wasLoadingInitially = SaveAndLoadManager.Instance.IsLoading;

            if (!wasLoadingInitially)
            {
                SaveAndLoadManager.Instance.IsLoading = true;
            }

            string savedUnitsFolder = Path.Combine(Application.persistentDataPath, "savedUnitsList");
            string baseFileName = stats.Name;

            //string unitFilePath = Path.Combine(savedUnitsFolder, baseFileName + "_unit.json");
            string weaponFilePath = Path.Combine(savedUnitsFolder, baseFileName + "_weapon.json");
            string inventoryFilePath = Path.Combine(savedUnitsFolder, baseFileName + "_inventory.json");
            string tokenJsonPath = Path.Combine(savedUnitsFolder, baseFileName + "_token.json");

            // SaveAndLoadManager.Instance.LoadComponentDataWithReflection<UnitData, Unit>(newUnit, unitFilePath);
            SaveAndLoadManager.Instance.LoadComponentDataWithReflection<WeaponData, Weapon>(newUnitObject, weaponFilePath);

            // Wczytaj ekwipunek
            InventoryData inventoryData = JsonUtility.FromJson<InventoryData>(File.ReadAllText(inventoryFilePath));
            Inventory inventory = newUnitObject.GetComponent<Inventory>();
            if (File.Exists(inventoryFilePath))
            {
                foreach (var weapon in inventoryData.AllWeapons)
                {
                    InventoryManager.Instance.AddWeaponToInventory(weapon, newUnitObject);
                }

                //Wczytanie aktualnie dobytych broni
                foreach (var weapon in inventory.AllWeapons)
                {
                    if (weapon.Id == inventoryData.EquippedWeaponsId[0])
                    {
                        inventory.EquippedWeapons[0] = weapon;
                    }
                    if (weapon.Id == inventoryData.EquippedWeaponsId[1])
                    {
                        inventory.EquippedWeapons[1] = weapon;
                    }
                    if (inventoryData.EquippedArmorsId.Contains(weapon.Id))
                    {
                        inventory.EquippedArmors.Add(weapon);
                    }
                }
                InventoryManager.Instance.CheckForEquippedWeapons();
            }

            //Wczytanie pieniędzy
            inventory.CopperCoins = inventoryData.CopperCoins;
            inventory.SilverCoins = inventoryData.SilverCoins;
            inventory.GoldCoins = inventoryData.GoldCoins;

            // Wczytaj token, jeśli istnieje
            if (File.Exists(tokenJsonPath))
            {
                string tokenJson = File.ReadAllText(tokenJsonPath);
                TokenData tokenData = JsonUtility.FromJson<TokenData>(tokenJson);

                if (tokenData.filePath.Length > 1)
                {
                    StartCoroutine(TokensManager.Instance.LoadTokenImage(tokenData.filePath, newUnitObject));
                }
            }

            if (!wasLoadingInitially)
            {
                SaveAndLoadManager.Instance.IsLoading = false;
            }
        }

        IsTileSelecting = false;

        if (SaveAndLoadManager.Instance.IsLoading != true)
        {
            //Ustawia tag postaci, który definiuje, czy jest to sojusznik, czy przeciwnik, a także jej domyślny kolor.
            if (_unitTagToggle.isOn)
            {

                newUnitObject.tag = "PlayerUnit";
                unit.DefaultColor = new Color(0f, 0.54f, 0.17f, 1.0f);
            }
            else
            {
                newUnitObject.tag = "EnemyUnit";
                unit.DefaultColor = new Color(0.59f, 0.1f, 0.19f, 1.0f);
            }
            unit.ChangeUnitColor(newUnitObject);

            stats.ChangeTokenSize((int)stats.Size);

            //Losuje początkowe statystyki dla każdej jednostki
            if (!IsSavedUnitsManaging)
            {
                stats.SetBaseStats();
            }

            // Dodaje do ekwipunku początkową broń adekwatną dla danej jednostki i wyposaża w nią
            if (stats.PrimaryWeaponNames != null && stats.PrimaryWeaponNames.Count > 0 && !IsSavedUnitsManaging)
            {
                Unit.LastSelectedUnit = Unit.SelectedUnit != null ? Unit.SelectedUnit : null;
                Unit.SelectedUnit = newUnitObject;
                SaveAndLoadManager.Instance.IsLoading = true; // Tylko po to, żeby informacja o dobyciu broni i dodaniu do ekwipunku z metody GrabWeapon i LoadWeapon nie były wyświetlane w oknie wiadomości

                InventoryManager.Instance.GrabPrimaryWeapons();
            }

            //  Wczytanie dodatkowych statystyk początkowej broni
            if (stats.PrimaryWeaponAttributes != null && stats.PrimaryWeaponAttributes.Count > 0)
            {
                Weapon weapon = InventoryManager.Instance.ChooseWeaponToAttack(newUnitObject);

                foreach (var attr in stats.PrimaryWeaponAttributes)
                {
                    var field = typeof(Weapon).GetField(attr.Key,
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    if (field == null) continue;

                    object convertedValue = null;
                    var ft = field.FieldType;

                    try
                    {
                        if (ft == typeof(string))
                        {
                            convertedValue = attr.Value;
                        }
                        else if (ft == typeof(int) && int.TryParse(attr.Value, out var iVal))
                        {
                            convertedValue = iVal;
                        }
                        else if (ft == typeof(bool) && bool.TryParse(attr.Value, out var bVal))
                        {
                            convertedValue = bVal;
                        }
                        else if (ft == typeof(float) && float.TryParse(attr.Value, out var fVal))
                        {
                            convertedValue = fVal;
                        }
                    }
                    catch
                    {
                        Debug.LogWarning($"Nie udało się przekonwertować PrimaryWeaponAttribute {attr.Key}:{attr.Value} na {ft.Name}");
                        continue;
                    }

                    if (convertedValue != null)
                    {
                        // Ustaw wartość na broni
                        field.SetValue(weapon, convertedValue);

                        // Próbujemy też ustawić to samo pole w BaseWeaponStats, jeśli istnieje
                        var baseStats = weapon.BaseWeaponStats;
                        if (baseStats != null)
                        {
                            var baseField = typeof(WeaponBaseStats).GetField(attr.Key,
                                BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                            if (baseField != null && baseField.FieldType == ft)
                            {
                                baseField.SetValue(baseStats, convertedValue);
                            }
                        }
                    }
                }
            }
            InventoryManager.Instance.CalculateEncumbrance(stats);

            //Ustala początkową inicjatywę i dodaje jednostkę do kolejki inicjatywy
            Unit.SelectedUnit = unit.gameObject;
            UpdateInitiative();
            Unit.SelectedUnit = null;

            InitiativeQueueManager.Instance.AddUnitToInitiativeQueue(unit);
        }

        return newUnitObject;
    }

    public void SetSavedUnitsManaging(bool value)
    {
        IsSavedUnitsManaging = value;
        IsTileSelecting = false;

        if (IsSavedUnitsManaging)
        {
            IsUnitEditing = false;

            _createUnitButton.gameObject.SetActive(false);
            _removeUnitButton.gameObject.SetActive(false);
            SortSavedUnitsByDateToggle.gameObject.SetActive(true);
            _selectUnitsButton.gameObject.SetActive(false);
            _updateUnitButton.gameObject.SetActive(false);
            _removeSavedUnitFromListButton.gameObject.SetActive(true);
        }
        else
        {
            _removeSavedUnitFromListButton.gameObject.SetActive(false);
            SortSavedUnitsByDateToggle.gameObject.SetActive(false);
            EditUnitModeOff();
        }
    }
    #endregion

    #region Removing units
    public void DestroyUnitMode()
    {
        if (GameManager.IsMapHidingMode)
        {
            Debug.Log("Aby usuwać jednostki, wyjdź z trybu ukrywania obszarów.");
            return;
        }

        IsUnitRemoving = !IsUnitRemoving;

        //Zmienia kolor przycisku usuwania jednostek na aktywny
        _removeUnitButton.GetComponent<UnityEngine.UI.Image>().color = IsUnitRemoving ? Color.green : Color.white;

        if (IsUnitRemoving)
        {
            //Jeżeli jest włączony tryb zaznaczania wielu jednostek to go resetuje
            if (IsMultipleUnitsSelecting)
            {
                SelectMultipleUnitsMode();
            }
            Debug.Log("Wybierz jednostkę, którą chcesz usunąć. Możesz również zaznaczyć obszar, wtedy zostaną usunięte wszystkie znajdujące się w nim jednostki.");
        }
    }
    public void DestroyUnit(GameObject unitObject = null)
    {
        if (unitObject == null)
        {
            unitObject = Unit.SelectedUnit;
        }
        else if (unitObject == Unit.SelectedUnit)
        {
            unitObject.GetComponent<Unit>().SelectUnit();
        }

        Unit unit = unitObject.GetComponent<Unit>();
        Stats stats = unit.Stats;

        //Usunięcie jednostki z kolejki inicjatywy
        InitiativeQueueManager.Instance.RemoveUnitFromInitiativeQueue(unit);

        //Uwolnienie jednostki uwięzionej przez jednostkę, która umiera
        if (unit.EntangledUnitId != 0)
        {
            foreach (var u in AllUnits)
            {
                if (u.UnitId == unit.GetComponent<Unit>().EntangledUnitId && u.Entangled)
                {
                    u.Entangled = false;
                }
            }
        }

        // Jeśli umiera jednostka Straszna to należy zaktualizować stan Strachu u przeciwników
        if (stats.Scary > 0) StartCoroutine(ScaryUnitDeath(unit));

        if (unit.IsMounted && unit.Mount != null && !SaveAndLoadManager.Instance.IsLoading && (AreaSelector.Instance.SelectedUnits == null || !AreaSelector.Instance.SelectedUnits.Contains(unit)))
        {
            unit.Mount.transform.SetParent(GameObject.Find("----------Units-------------------").transform);
            InitiativeQueueManager.Instance.AddUnitToInitiativeQueue(unit.Mount);
            unit.Mount.gameObject.SetActive(true);
            unit.Mount.HasRider = false;
        }

        //Aktualizuje kolejkę inicjatywy
        InitiativeQueueManager.Instance.UpdateInitiativeQueue();

        //Usuwa jednostkę z listy wszystkich jednostek
        AllUnits.Remove(unit);

        //Resetuje Tile, żeby nie było uznawane jako zajęte
        GridManager.Instance.ResetTileOccupancy(unit.transform.position);

        // Aktualizuje osiągnięcia
        if (unit.LastAttackerStats != null)
        {
            unit.LastAttackerStats.OpponentsKilled++;
            if (unit.LastAttackerStats.StrongestDefeatedOpponentOverall < stats.Overall)
            {
                unit.LastAttackerStats.StrongestDefeatedOpponentOverall = stats.Overall;
                unit.LastAttackerStats.StrongestDefeatedOpponent = stats.Name;
            }

            // Uwzględnia cechę Żarłoczny
            if (unit.LastAttackerStats.Hungry)
            {
                StartCoroutine(HungryTrait(unit.LastAttackerStats, stats));
            }
        }

        Destroy(unitObject);

        //Resetuje kolor przycisku usuwania jednostek
        _removeUnitButton.GetComponent<UnityEngine.UI.Image>().color = Color.white;
    }

    private IEnumerator ScaryUnitDeath(Unit deadUnit)
    {
        if (deadUnit == null) yield break;

        Stats stats = deadUnit.GetComponent<Stats>();

        string scaryTag = deadUnit.tag;                                           // strona zmarłego źródła strachu
        string otherTag = scaryTag == "PlayerUnit" ? "EnemyUnit" : "PlayerUnit";  // przeciwnicy

        // 1) Ustal, czy po stronie zmarłego zostały inne źródła strachu i ich maksymalną siłę
        int maxRemainingScary = 0;
        foreach (var pair in InitiativeQueueManager.Instance.InitiativeQueue)
        {
            Unit u = pair.Key;
            if (u == null || u == deadUnit) continue;
            if (!u.CompareTag(scaryTag)) continue;

            Stats s = u.GetComponent<Stats>();
            if (s.Scary > maxRemainingScary) maxRemainingScary = s.Scary;
        }

        // 2) Jeśli NIE ma już żadnych strasznych po tej stronie → zdejmij Scared z przeciwników
        if (maxRemainingScary == 0)
        {
            foreach (var pair in InitiativeQueueManager.Instance.InitiativeQueue)
            {
                Unit u = pair.Key;
                if (!u.CompareTag(otherTag)) continue;

                if (u.Scared)
                {
                    u.Scared = false;
                    String n = u.GetComponent<Stats>()?.Name ?? u.name;
                    Debug.Log($"<color=#FF7F50>{n} przestaje się bać (źródło strachu pokonane).</color>");
                }
            }
            yield break;
        }

        // 3) Jeśli pozostało równie silne lub silniejsze źródło strachu → nic nie zmieniaj
        if (maxRemainingScary >= stats.Scary)
            yield break;

        // 4) Pozostały TYLKO słabsze źródła → ponowny test strachu DLA WSZYSTKICH, którzy mają stan Strachu
        foreach (var pair in InitiativeQueueManager.Instance.InitiativeQueue)
        {
            Unit u = pair.Key;
            if (u == null) continue;
            if (!u.CompareTag(otherTag)) continue;
            if (!u.Scared) continue;

            // test odniesie się do NAJSTRASZNIEJSZEGO istniejącego przeciwnika wewnątrz FearTest
            yield return StartCoroutine(StatesManager.Instance.FearTest(u));
        }
    }

    private IEnumerator HungryTrait(Stats stats, Stats deadBodyStats)
    {

        int[] test = null;
        if (!GameManager.IsAutoDiceRollingMode && stats.CompareTag("PlayerUnit"))
        {
            yield return StartCoroutine(DiceRollManager.Instance.WaitForRollValue(
                stats: stats,
                rollContext: $"Siłę Woli w związku z cechą Żarłoczny",
                attributeName: "SW",
                difficultyLevel: 12,
                callback: res => test = res
            ));
            if (test == null) yield break; // anulowano panel
        }
        else
        {
            test = DiceRollManager.Instance.TestSkill(
                stats: stats,
                rollContext: $"Siłę Woli w związku z cechą Żarłoczny",
                attributeName: "SW",
                difficultyLevel: 12
            );
        }

        int finalScore = test[3];

        if (finalScore < 12)
        {
            Debug.Log($"<color=red>{stats.Name} traci następną turę, ucztując na martwym ciele {deadBodyStats.Name}. Pamiętaj, aby to uwzględnić.</color>");
        }
    }

    public void RemoveUnitFromList(GameObject confirmPanel)
    {
        if (_unitsDropdown.SelectedButton == null)
        {
            Debug.Log("Wybierz jednostkę z listy.");
        }
        else
        {
            confirmPanel.SetActive(true);
        }
    }
    #endregion

    #region Unit selecting
    public void SelectMultipleUnitsMode(bool value = true)
    {
        // Jeśli `value` jest false, wyłącza tryb zaznaczania, w przeciwnym razie przełącza tryb
        IsMultipleUnitsSelecting = value ? !IsMultipleUnitsSelecting : false;

        // Ustawia kolor przycisku w zależności od stanu
        _selectUnitsButton.GetComponent<UnityEngine.UI.Image>().color = IsMultipleUnitsSelecting ? Color.green : Color.white;

        // Wyświetla komunikat, jeśli tryb zaznaczania jest aktywny
        if (IsMultipleUnitsSelecting)
        {
            //Jeżeli jest włączony tryb usuwania jednostek to go resetuje
            if (IsUnitRemoving)
            {
                DestroyUnitMode();
            }
            Debug.Log("Zaznacz jednostki na wybranym obszarze przy użyciu myszy. Klikając Ctrl+C możesz je skopiować, a następnie wkleić przy pomocy Ctrl+V.");
        }
    }
    #endregion

    #region Unit editing
    public void EditUnitModeOn(Animator panelAnimator)
    {
        IsUnitEditing = true;

        _createUnitButton.gameObject.SetActive(false);
        _removeUnitButton.gameObject.SetActive(false);
        _selectUnitsButton.gameObject.SetActive(false);
        _updateUnitButton.gameObject.SetActive(true);
        _removeSavedUnitFromListButton.gameObject.SetActive(false);

        if (!AnimationManager.Instance.PanelStates.ContainsKey(panelAnimator))
        {
            AnimationManager.Instance.PanelStates[panelAnimator] = false; // Domyślny stan panelu
        }

        //Jeśli panel edycji jednostek jest schowany to wysuwamy go
        if (AnimationManager.Instance.PanelStates[panelAnimator] == false)
        {
            AnimationManager.Instance.TogglePanel(panelAnimator);
        }

        // Jeżeli mamy wybraną jednostkę, pobieramy jej rasę
        string currentRace = Unit.SelectedUnit.GetComponent<Stats>().Race;

        int foundIndex = -1;
        for (int i = 0; i < _unitsDropdown.Buttons.Count; i++)
        {
            // Tutaj sprawdzamy text w komponencie TextMeshProUGUI
            var txt = _unitsDropdown.Buttons[i].GetComponentInChildren<TextMeshProUGUI>();
            if (txt != null && txt.text == currentRace)
            {
                foundIndex = i;
                break;
            }
        }

        // Jeśli znaleźliśmy pasujący przycisk, wywołujemy `SetSelectedIndex(foundIndex+1)`
        if (foundIndex != -1)
        {
            // Indeksy w `Buttons` idą od 0, a `SelectOption` od 1
            _unitsDropdown.SetSelectedIndex(foundIndex + 1);
        }
    }

    public void EditUnitModeOff()
    {
        IsUnitEditing = false;

        _createUnitButton.gameObject.SetActive(true);
        _removeUnitButton.gameObject.SetActive(true);
        _selectUnitsButton.gameObject.SetActive(true);
        _updateUnitButton.gameObject.SetActive(false);

        if (IsSavedUnitsManaging)
        {
            _removeSavedUnitFromListButton.gameObject.SetActive(false);
        }
    }

    public void UpdateUnitNameOrRace()
    {
        if (Unit.SelectedUnit == null) return;

        if (_unitsDropdown.SelectedButton == null)
        {
            Debug.Log("Wybierz rasę z listy. Zmiana rasy wpłynie na statystyki.");
            return;
        }

        GameObject unit = Unit.SelectedUnit;
        Stats stats = unit.GetComponent<Stats>();

        //Ustawia tag postaci, który definiuje, czy jest to sojusznik, czy przeciwnik, a także jej domyślny kolor.
        if (_unitTagToggle.isOn)
        {
            unit.tag = "PlayerUnit";
            unit.GetComponent<Unit>().DefaultColor = new Color(0f, 0.54f, 0.17f, 1.0f);
        }
        else
        {
            unit.tag = "EnemyUnit";
            unit.GetComponent<Unit>().DefaultColor = new Color(0.59f, 0.1f, 0.19f, 1.0f); ;
        }
        unit.GetComponent<Unit>().ChangeUnitColor(unit);

        stats.ChangeTokenSize((int)stats.Size);

        //Sprawdza, czy rasa jest zmieniana
        if (stats.Id != _unitsDropdown.GetSelectedIndex())
        {
            bool changeName = false;

            if (stats.Name.Contains(stats.Race))
            {
                changeName = true;
            }

            // Sprawdza, czy ostatni jeden lub dwa znaki to liczba
            string currentName = stats.Name;
            string numberSuffix = "";
            System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(currentName, @"(\d{1,2})$");
            if (match.Success)
            {
                numberSuffix = match.Value; // Przechowuje numer znaleziony na końcu nazwy
            }

            // Ustala nową rasę na podstawie rasy wybranej z listy
            stats.Id = _unitsDropdown.GetSelectedIndex();

            string newRaceName = _unitsDropdown.SelectedButton.GetComponentInChildren<TextMeshProUGUI>().text;

            if (changeName)
            {
                // Jeśli zmieniamy nazwę, dodajemy zachowaną liczbę (jeśli istnieje)
                if (!string.IsNullOrEmpty(numberSuffix))
                {
                    stats.Name = $"{newRaceName} {numberSuffix}";
                }
                else
                {
                    stats.Name = newRaceName;
                }
            }

            //Aktualizuje statystyki
            DataManager.Instance.LoadAndUpdateStats(unit);

            //Losuje początkowe statystyki dla człowieka, elfa, krasnoluda i niziołka
            if (stats.Id <= 4 && !IsSavedUnitsManaging)
            {
                stats.SetBaseStats();
                unit.GetComponent<Unit>().DisplayUnitHealthPoints();
            }

            //Aktualizuje aktualną żywotność
            stats.CalculateMaxHealth();
            stats.TempHealth = stats.MaxHealth;

            // Aktualizuje udźwig
            stats.MaxEncumbrance = 6 + stats.S;
            InventoryManager.Instance.DisplayEncumbrance(stats);

            //Ustala inicjatywę i aktualizuje kolejkę inicjatywy
            InitiativeQueueManager.Instance.RemoveUnitFromInitiativeQueue(unit.GetComponent<Unit>());
            InitiativeQueueManager.Instance.AddUnitToInitiativeQueue(unit.GetComponent<Unit>());
            UpdateInitiative();

            //Dodaje do ekwipunku początkową broń adekwatną dla danej jednostki i wyposaża w nią
            if (unit.GetComponent<Stats>().PrimaryWeaponNames != null && unit.GetComponent<Stats>().PrimaryWeaponNames.Count > 0 && changeName)
            {
                //Usuwa posiadane bronie
                InventoryManager.Instance.RemoveAllWeaponsFromInventory();

                Unit.LastSelectedUnit = Unit.SelectedUnit != null ? Unit.SelectedUnit : null;
                Unit.SelectedUnit = unit;
                SaveAndLoadManager.Instance.IsLoading = true; // Tylko po to, żeby informacja o dobyciu broni i dodaniu do ekwipunku z metody GrabWeapon i LoadWeapon nie były wyświetlane w oknie wiadomości

                InventoryManager.Instance.GrabPrimaryWeapons();
            }

            unit.GetComponent<Unit>().DisplayUnitName();
            unit.GetComponent<Unit>().DisplayUnitHealthPoints();
        }

        //Aktualizuje wyświetlany panel ze statystykami
        UpdateUnitPanel(unit);
    }

    public void UpdateInitiative()
    {
        if (Unit.SelectedUnit == null) return;

        GameObject unitGO = Unit.SelectedUnit;
        Unit unit = unitGO.GetComponent<Unit>();
        Stats stats = unitGO.GetComponent<Stats>();
        Inventory inventory = unitGO.GetComponent<Inventory>();
        if (unit == null || stats == null) return;

        // Modyfikator za cechę broni (Fast/Slow) – na podstawie EquippedWeapons
        int weaponMod = 0;
        bool hasFast = false, hasSlow = false;

        if (inventory != null && inventory.EquippedWeapons != null)
        {
            hasFast = inventory.EquippedWeapons.Any(w => w != null && w.Fast);
            hasSlow = inventory.EquippedWeapons.Any(w => w != null && w.Slow);

            // Jeżeli występują obie cechy naraz (np. dwie bronie), traktujemy jako 0 (neutralizują się).
            if (hasFast && !hasSlow) weaponMod = 3;
            else if (hasSlow && !hasFast) weaponMod = -3;
            else weaponMod = 0;
        }

        // Finalna inicjatywa
        stats.Initiative = DiceRollManager.Instance.TestSkill(stats, "Refleks, aby określić inicjatywę", null, "Reflex", weaponMod)[3];

        // Aktualizacja kolejki inicjatywy — wpisujemy RZECZYWISTĄ wartość
        InitiativeQueueManager.Instance.InitiativeQueue[unit] = stats.Initiative;
        InitiativeQueueManager.Instance.UpdateInitiativeQueue();

        UpdateUnitPanel(unitGO);
    }

    public void EditAttribute(GameObject textInput)
    {
        if (Unit.SelectedUnit == null || _isPopulatingUI) return;

        Unit unit = Unit.SelectedUnit.GetComponent<Unit>();
        Stats stats = Unit.SelectedUnit.GetComponent<Stats>();

        // Pobiera nazwę cechy z nazwy obiektu InputField (bez "_input")
        string attributeName = textInput.name.Replace("_input", "");

        // --- Talenty i cechy w formie tablic ---
        if (HandleTalentListEdit("Specialist", attributeName, textInput, stats, ToSkillKey))
        {
            UpdateUnitPanel(Unit.SelectedUnit);
            if (!SaveAndLoadManager.Instance.IsLoading)
            {
                int newOverall = stats.CalculateOverall();
                InitiativeQueueManager.Instance.CalculateDominance();
            }
            return; // nie wchodzimy w refleksję
        }
        if (HandleTalentListEdit("Slayer", attributeName, textInput, stats, ToSlayerKey))
        {
            UpdateUnitPanel(Unit.SelectedUnit);
            if (!SaveAndLoadManager.Instance.IsLoading)
            {
                int newOverall = stats.CalculateOverall();
                InitiativeQueueManager.Instance.CalculateDominance();
            }
            return; // nie wchodzimy w refleksję
        }
        if (HandleTalentListEdit("Resistance", attributeName, textInput, stats, ToResistanceKey, slots: 5))
        {
            UpdateUnitPanel(Unit.SelectedUnit);
            if (!SaveAndLoadManager.Instance.IsLoading)
            {
                int newOverall = stats.CalculateOverall();
                InitiativeQueueManager.Instance.CalculateDominance();
            }
            return; // nie wchodzimy w refleksję
        }
        if (HandleTalentListEdit("Magic", attributeName, textInput, stats, ToMagicKey, slots: 6))
        {
            UpdateUnitPanel(Unit.SelectedUnit);
            if (!SaveAndLoadManager.Instance.IsLoading)
            {
                int newOverall = stats.CalculateOverall();
                InitiativeQueueManager.Instance.CalculateDominance();
            }
            return; // nie wchodzimy w refleksję
        }

        // Szukamy zwykłego pola w klasie Stats
        FieldInfo field = stats.GetType().GetField(attributeName);

        // Jeżeli pole nie istnieje, kończymy metodę
        if (field == null)
        {
            Debug.Log($"Nie znaleziono pola '{attributeName}' w klasie Stats.");
            return;
        }

        // Zależnie od typu pola...
        if (field.FieldType == typeof(int) && textInput.GetComponent<UnityEngine.UI.Slider>() == null)
        {
            // int przez InputField
            int value = int.TryParse(textInput.GetComponent<TMP_InputField>().text, out int inputValue)
                        ? inputValue
                        : 0;

            if (attributeName == "Flight") value /= 2;

            field.SetValue(stats, value);

            if (attributeName == "ExtraEncumbrance")
            {
                InventoryManager.Instance.CalculateEncumbrance(stats);
            }         
        }
        else if (field.FieldType == typeof(int) && textInput.GetComponent<UnityEngine.UI.Slider>() != null)
        {
            // int przez Slider
            int value = (int)textInput.GetComponent<UnityEngine.UI.Slider>().value;
            field.SetValue(stats, value);
        }
        else if (field.FieldType == typeof(bool))
        {
            bool boolValue = textInput.GetComponent<UnityEngine.UI.Toggle>().isOn;

            if (field.Name == "Fast")
            {
                bool oldValue = (bool)field.GetValue(stats);
                if (oldValue != boolValue) // tylko jeśli wartość się zmienia
                {
                    field.SetValue(stats, boolValue);
                    stats.Sz += boolValue ? 1 : -1;
                    StartCoroutine(MovementManager.Instance.UpdateMovementRange(1));
                }
            }
            else
            {
                field.SetValue(stats, boolValue);
            }
        }

        else if (field.FieldType == typeof(string))
        {
            string value = textInput.GetComponent<TMP_InputField>().text;
            field.SetValue(stats, value);
        }
        else if (field.FieldType.IsEnum && textInput.GetComponent<TMP_Dropdown>() != null)
        {
            // Obsługa TMP_Dropdown dla enumów
            TMP_Dropdown dropdown = textInput.GetComponent<TMP_Dropdown>();
            Array enumValues = Enum.GetValues(field.FieldType);

            if (dropdown.value >= 0 && attributeName == "Size")
            {
                stats.ChangeUnitSize(dropdown.value);
            }
            else if (dropdown.value >= 0 && dropdown.value < enumValues.Length)
            {
                object selectedEnumValue = enumValues.GetValue(dropdown.value);
                field.SetValue(stats, selectedEnumValue);
            }
        }
        else
        {
            Debug.Log($"Nie udało się zmienić wartości cechy '{attributeName}'.");
        }

        if (attributeName == "S" || attributeName == "K")
        {
            stats.CalculateMaxHealth();
            unit.DisplayUnitHealthPoints();
            stats.MaxEncumbrance = 6 + stats.S;
            InventoryManager.Instance.DisplayEncumbrance(stats);
        }
        else if (attributeName == "Hardy" || attributeName == "SW") // Talent Twardziel
        {
            stats.CalculateMaxHealth();
            unit.DisplayUnitHealthPoints();
        }
        else if(attributeName == "NaturalArmor")
        {
            InventoryManager.Instance.CheckForEquippedWeapons();
        }
        else if (attributeName == "Name")
        {
            unit.DisplayUnitName();
        }

        UpdateUnitPanel(Unit.SelectedUnit);

        if (!SaveAndLoadManager.Instance.IsLoading)
        {
            //Aktualizuje pasek przewagi w bitwie
            int newOverall = stats.CalculateOverall();
            int difference = newOverall - stats.Overall;

            InitiativeQueueManager.Instance.CalculateDominance();
        }
    }

    #endregion

    #region Update unit panel (at the top of the screen)
    public void UpdateUnitPanel(GameObject unit)
    {
        if (unit == null || SaveAndLoadManager.Instance.IsLoading)
        {
            _unitPanel.SetActive(false);
            return;
        }
        else
        {
            _unitPanel.SetActive(true);

            //W trybie ukrywania statystyk, panel wrogich jednostek pozostaje wyłączony
            if (GameManager.IsStatsHidingMode && unit.CompareTag("EnemyUnit"))
            {
                _unitPanel.transform.Find("VerticalLayoutGroup/Stats_Panel/Stats_display").gameObject.SetActive(false);
            }
            else
            {
                _unitPanel.transform.Find("VerticalLayoutGroup/Stats_Panel/Stats_display").gameObject.SetActive(true);
            }

            //Ukrywa lub pokazuje nazwę jednostki w panelu
            if (GameManager.IsNamesHidingMode && !MultiScreenDisplay.Instance.PlayersCamera.gameObject.activeSelf && Display.displays.Length == 1)
            {
                _unitPanel.transform.Find("Name_input").gameObject.SetActive(false);
            }
            else
            {
                _unitPanel.transform.Find("Name_input").gameObject.SetActive(true);
            }
        }

        Stats stats = unit.GetComponent<Stats>();

        if (stats.Spellcasting > 0)
        {
            _spellbookButton.interactable = true;
            DataManager.Instance.LoadAndUpdateSpells(); //Aktualizuje listę zaklęć, które może rzucić jednostka

            if (unit.GetComponent<Spell>() == null)
            {
                unit.AddComponent<Spell>();
            }
        }
        else
        {
            _spellbookButton.interactable = false;
            _spellListPanel.SetActive(false);
        }

        //_nameDisplay.text = stats.Name;
        _raceDisplay.text = stats.Race;

        _healthDisplay.text = stats.TempHealth + "/" + stats.MaxHealth;
        _healthBar.maxValue = stats.MaxHealth;
        _healthBar.value = stats.TempHealth;
        UpdateHealthBarColor(stats.TempHealth, stats.MaxHealth, _healthBar.transform.Find("Fill Area/Fill").GetComponent<UnityEngine.UI.Image>());

        _tokenDisplay.sprite = unit.transform.Find("Token").GetComponent<SpriteRenderer>().sprite;
        _tokenBorder.color = unit.tag == "EnemyUnit" ? new Color(0.59f, 0.1f, 0.19f, 1.0f) : new Color(0f, 0.54f, 0.17f, 1.0f);

        InventoryManager.Instance.DisplayEquippedWeaponsName();

        RoundsManager.Instance.DisplayActionsLeft();

        CombatManager.Instance.UpdateAimButtonColor();
        MountsManager.Instance.UpdateMountButtonColor();

        LoadAttributes(unit);

        MountsManager.Instance.ShowOrHideMountButton(stats.Flight == 0);
        MovementManager.Instance.ShowOrHideFlightButton(stats.Flight != 0);
    }

    private void UpdateHealthBarColor(float tempHealth, float maxHealth, UnityEngine.UI.Image image)
    {
        float percentage = tempHealth / maxHealth * 100;

        if (percentage <= 30)
        {
            image.color = new Color(0.81f, 0f, 0.137f); // Kolor czerwony, jeśli wartość <= 30%
        }
        else if (percentage > 30 && percentage <= 70)
        {
            image.color = new Color(1f, 0.6f, 0f); // Kolor pomarańczowy, jeśli wartość jest między 31% a 70%
        }
        else
        {
            image.color = new Color(0.3f, 0.65f, 0.125f); // Kolor zielony, jeśli wartość > 70%
        }
    }

    public void LoadAttributesByButtonClick()
    {
        if (Unit.SelectedUnit == null) return;

        GameObject unit = Unit.SelectedUnit;
        LoadAttributes(unit);
    }

    public void LoadAchievementsByButtonClick()
    {
        if (Unit.SelectedUnit == null) return;

        GameObject unit = Unit.SelectedUnit;
        LoadAchievements(unit);
    }

    public void LoadAttributes(GameObject unit)
    {
        _isPopulatingUI = true;
        try
        {
            var stats = unit.GetComponent<Stats>();
            var u = unit.GetComponent<Unit>(); // do inicjatywy
            GameObject[] attributeInputFields = GameObject.FindGameObjectsWithTag("Attribute");

            foreach (var inputField in attributeInputFields)
            {
                string attributeName = inputField.name.Replace("_input", "");

                // --- Talenty/cechy w formie tablic 3-slotowych (bez eventów!) ---
                if (HandleTalentListLoad("Specialist", attributeName, inputField, stats, ToPolishSkill))
                    continue;
                if (HandleTalentListLoad("Slayer", attributeName, inputField, stats, ToPolishSlayer))
                    continue;
                if (HandleTalentListLoad("Resistance", attributeName, inputField, stats, ToPolishResistance, slots: 5))
                    continue;
                if (HandleTalentListLoad("Magic", attributeName, inputField, stats, ToPolishMagic, slots: 6))
                    continue;

                FieldInfo field = stats.GetType().GetField(attributeName);
                if (field == null) continue;

                object value = field.GetValue(stats);

                if (field.FieldType == typeof(int))
                {
                    int intValue = (int)value;
                    if (attributeName == "Flight") intValue *= 2;

                    SetInputFieldValueWithoutNotify(inputField, intValue);
                }
                else if (field.FieldType == typeof(bool))
                {
                    SetToggleValueWithoutNotify(inputField, (bool)value);
                }
                else if (field.FieldType == typeof(string))
                {
                    SetInputFieldValueWithoutNotify(inputField, (string)value);
                }
                else if (field.FieldType.IsEnum)
                {
                    SetDropdownValueWithoutNotify(inputField, value);
                    continue;
                }

                if (attributeName == "Initiative")
                {
                    InitiativeQueueManager.Instance.InitiativeQueue[u] = stats.Initiative;
                    InitiativeQueueManager.Instance.UpdateInitiativeQueue();
                }
            }
        }
        finally { _isPopulatingUI = false; }
    }

    private void SetInputFieldValueWithoutNotify(GameObject go, int value)
    {
        var inp = go.GetComponent<TMPro.TMP_InputField>();
        if (inp != null) inp.SetTextWithoutNotify(value.ToString());
    }
    private void SetInputFieldValueWithoutNotify(GameObject go, string value)
    {
        var inp = go.GetComponent<TMPro.TMP_InputField>();
        if (inp != null) inp.SetTextWithoutNotify(value ?? "");
    }
    private void SetToggleValueWithoutNotify(GameObject go, bool value)
    {
        var t = go.GetComponent<UnityEngine.UI.Toggle>();
        if (t != null) t.SetIsOnWithoutNotify(value);
    }
    private void SetDropdownValueWithoutNotify(GameObject go, object enumValue)
    {
        var dd = go.GetComponentInChildren<TMPro.TMP_Dropdown>(true);
        if (dd == null || enumValue == null) return;
        var values = Enum.GetValues(enumValue.GetType());
        int idx = Array.IndexOf(values, enumValue);
        if (idx >= 0) dd.SetValueWithoutNotify(idx);
    }
    private int FindDropdownIndexByText(TMP_Dropdown dd, string text)
    {
        for (int i = 0; i < dd.options.Count; i++)
            if (dd.options[i].text == text) return i;
        return -1;
    }

    public void ChangeTemporaryHealthPoints(int amount)
    {
        if (Unit.SelectedUnit == null) return;

        Unit.SelectedUnit.GetComponent<Stats>().TempHealth += amount;

        Unit.SelectedUnit.GetComponent<Unit>().DisplayUnitHealthPoints();

        UpdateUnitPanel(Unit.SelectedUnit);
    }

    public void LoadAchievements(GameObject unit)
    {
        // Wyszukuje wszystkie pola tekstowe i przyciski do ustalania statystyk postaci wewnatrz gry
        GameObject[] achievementGameObjects = GameObject.FindGameObjectsWithTag("Achievement");

        foreach (var obj in achievementGameObjects)
        {
            string achivementName = obj.name.Replace("_text", "");
            FieldInfo field = unit.GetComponent<Stats>().GetType().GetField(achivementName);

            if (field == null) continue;

            // Jeśli znajdzie takie pole, to zmienia wartość wyświetlanego tekstu na wartość tej cechy
            if (field.FieldType == typeof(int)) // to działa dla cech opisywanych wartościami int
            {
                int value = (int)field.GetValue(unit.GetComponent<Stats>());

                if (obj.GetComponent<TMP_Text>() != null)
                {
                    obj.GetComponent<TMP_Text>().text = value.ToString();
                }
            }
            else if (field.FieldType == typeof(string)) // to działa dla cech opisywanych wartościami string
            {
                string value = (string)field.GetValue(unit.GetComponent<Stats>());

                if (obj.GetComponent<TMP_Text>() != null)
                {
                    obj.GetComponent<TMP_Text>().text = value;
                }
            }
        }
    }
    #endregion

    public bool BothTeamsExist()
    {
        bool enemyUnitExists = false;
        bool playerUnitExists = false;

        foreach (var pair in InitiativeQueueManager.Instance.InitiativeQueue)
        {
            Stats unitStats = pair.Key.GetComponent<Stats>();

            if (pair.Key.CompareTag("EnemyUnit")) enemyUnitExists = true;
            if (pair.Key.CompareTag("PlayerUnit")) playerUnitExists = true;
        }

        if (enemyUnitExists && playerUnitExists) return true;
        else return false;
    }



    // === FUNKCJE POMOCNICZE DO SPECJALISTY ===
    // PL -> EN
    private static readonly Dictionary<string, string> SkillPlToEn = new()
    {
        { "Leczenie", "Healing" },
        { "Rzucanie Zaklęć", "Spellcasting" },
        { "Walka Dystansowa", "RangedCombat" },
        { "Walka Wręcz", "MeleeCombat" },
        // ...uzupełnij pełną listę
    };

    private static readonly Dictionary<string, string> SkillEnToPl =
        SkillPlToEn.ToDictionary(kv => kv.Value, kv => kv.Key);

    private string ToSkillKey(string polishName) // PL -> EN
    {
        return SkillPlToEn.TryGetValue(polishName, out var en)
            ? en
            : polishName.Replace(" ", "");
    }

    private string ToPolishSkill(string englishKey) // EN -> PL
    {
        return SkillEnToPl.TryGetValue(englishKey, out var pl)
            ? pl
            : englishKey;
    }

    // === FUNKCJE POMOCNICZE DO ZABÓJCY ===
    // PL -> EN
    public static readonly Dictionary<string, string> SlayerPlToEn = new()
    {
        { "Bestie i zwierzęta", "Beast" },
        { "Nieumarli", "Undead" },
        { "Smoki", "Dragon" },
        { "Trolle i olbrzymy", "Giant" },
        // ...uzupełnij pełną listę
    };

    public static readonly Dictionary<string, string> SlayerEnToPl =
        SlayerPlToEn.ToDictionary(kv => kv.Value, kv => kv.Key);

    private string ToSlayerKey(string polishName) // PL -> EN
    {
        return SlayerPlToEn.TryGetValue(polishName, out var en)
            ? en
            : polishName.Replace(" ", "");
    }

    private string ToPolishSlayer(string englishKey) // EN -> PL
    {
        return SlayerEnToPl.TryGetValue(englishKey, out var pl)
            ? pl
            : englishKey;
    }

    // === FUNKCJE POMOCNICZE DO ODPORNOŚCI ===
    // PL -> EN
    public static readonly Dictionary<string, string> ResistancePlToEn = new()
    {
        { "Zimno", "Ice" },
        { "Obrażenia fizyczne", "Physical" },
        { "Ogień", "Fire" },
        { "Elektryczność", "Electric" },
        { "Zatrucie", "Poison" }
    };
    public static readonly Dictionary<string, string> ResistanceEnToPl =
        ResistancePlToEn.ToDictionary(kv => kv.Value, kv => kv.Key);

    private string ToResistanceKey(string label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        label = label.Trim();
        // PL -> EN; jeśli już EN lub brak w słowniku, zwracamy jak jest
        return ResistancePlToEn.TryGetValue(label, out var en) ? en : label;
    }

    private string ToPolishResistance(string englishKey) // EN -> PL
    {
        return ResistanceEnToPl.TryGetValue(englishKey, out var pl)
            ? pl
            : englishKey;
    }

    // === FUNKCJE POMOCNICZE DO MAGII ===
    // PL -> EN (uzupełnij wg swoich ścieżek magii; brak w słowniku = przepuszczamy)
    public static readonly Dictionary<string, string> MagicPlToEn = new()
    {
         { "Lód", "Ice" },
         { "Ogień", "Fire" },
         { "Powietrze", "Air" },
         { "Śmierć", "Death" },
         { "Woda", "Water" },
         { "Ziemia", "Earth" },
    };
    public static readonly Dictionary<string, string> MagicEnToPl =
        MagicPlToEn.ToDictionary(kv => kv.Value, kv => kv.Key);

    private string ToMagicKey(string label) // PL -> EN (fallback: surowy tekst bez spacji)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        label = label.Trim();
        return MagicPlToEn.TryGetValue(label, out var en) ? en : label.Replace(" ", "");
    }

    private string ToPolishMagic(string englishKey) // EN -> PL (fallback: klucz)
    {
        return MagicEnToPl.TryGetValue(englishKey, out var pl) ? pl : englishKey;
    }





    // Edycja jednego slotu listy-3 (Specialist/Slayer/itp.) na podstawie Toggle + Dropdown
    private bool HandleTalentListEdit(string baseName, string attributeName, GameObject textInput, Stats stats, Func<string, string> toKey, int slots = 3)
    {
        if (!attributeName.StartsWith(baseName)) return false;
        if (!int.TryParse(attributeName.Substring(baseName.Length), out int number) || number < 1 || number > slots) return false;

        int slot = number - 1;

        // Pobierz pole tablicy z Stats via refleksję
        var arrField = typeof(Stats).GetField(baseName, BindingFlags.Public | BindingFlags.Instance);
        if (arrField == null)
        {
            Debug.LogWarning($"W Stats brakuje pola '{baseName}'.");
            return true; // zatrzymujemy standardową ścieżkę
        }

        var arr = arrField.GetValue(stats) as string[];
        if (arr == null)
        {
            arr = new string[slots];
            arrField.SetValue(stats, arr);
        }
        else if (arr.Length != slots)
        {
            var newArr = new string[slots];
            Array.Copy(arr, newArr, Math.Min(arr.Length, slots)); // zachowaj to, co było
            arr = newArr;
            arrField.SetValue(stats, arr);
        }

        var toggle = textInput.GetComponent<UnityEngine.UI.Toggle>();
        var dropdown = textInput.GetComponentInChildren<TMP_Dropdown>(true);
        if (toggle == null || dropdown == null)
        {
            Debug.LogWarning($"[{attributeName}] Brak Toggle lub TMP_Dropdown.");
            return true;
        }

        bool isOn = toggle.isOn;
        string pl = (dropdown.options != null && dropdown.options.Count > 0) ? dropdown.options[dropdown.value].text : null;
        string key = !string.IsNullOrEmpty(pl) ? toKey(pl) : null;

        // Odznaczone lub brak klucza => czyścimy slot
        if (!isOn || string.IsNullOrEmpty(key))
        {
            arr[slot] = null;
            arrField.SetValue(stats, arr);
            return true;
        }

        // Deduplikacja: usuń ten sam klucz z innych slotów
        for (int i = 0; i < slots; i++)
        {
            if (i != slot && string.Equals(arr[i], key, StringComparison.Ordinal))
                arr[i] = null;
        }

        // Zapis
        arr[slot] = key;
        arrField.SetValue(stats, arr);

        return true; // obsłużone, nie wchodzimy w standardową refleksję
    }

    // Wczytywanie jednego slotu listy-3 do UI (Dropdown + Toggle)
    private bool HandleTalentListLoad(string baseName, string attributeName, GameObject inputField,
                                      Stats stats, Func<string, string> toPolish, int slots = 3)
    {
        if (!attributeName.StartsWith(baseName)) return false;
        if (!int.TryParse(attributeName.Substring(baseName.Length), out int number) || number < 1 || number > slots)
            return true; // traktujemy jako obsłużone (nic nie robimy)

        int slot = number - 1;

        var toggle = inputField.GetComponent<UnityEngine.UI.Toggle>();
        var dropdown = inputField.GetComponentInChildren<TMPro.TMP_Dropdown>(true);

        var arrField = typeof(Stats).GetField(baseName, BindingFlags.Public | BindingFlags.Instance);
        if (arrField == null) return true;

        var arr = arrField.GetValue(stats) as string[];
        if (arr == null)
        {
            arr = new string[slots];
            arrField.SetValue(stats, arr);
        }
        else if (arr.Length != slots)
        {
            var newArr = new string[slots];
            Array.Copy(arr, newArr, Math.Min(arr.Length, slots)); // zachowaj to, co było
            arr = newArr;
            arrField.SetValue(stats, arr);
        }

        string key = arr[slot];
        bool has = !string.IsNullOrEmpty(key);

        if (toggle != null) toggle.SetIsOnWithoutNotify(has);
        if (has && dropdown != null)
        {
            string pl = toPolish(key);
            int idx = FindDropdownIndexByText(dropdown, pl);
            if (idx >= 0) dropdown.SetValueWithoutNotify(idx);
        }
        return true;
    }
}
