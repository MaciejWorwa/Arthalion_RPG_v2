using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;

public class SaveAndLoadManager : MonoBehaviour
{
    // Prywatne statyczne pole przechowujące instancję
    private static SaveAndLoadManager instance;

    // Publiczny dostęp do instancji
    public static SaveAndLoadManager Instance
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

    [SerializeField] private TMP_InputField _saveNameInput;
    [SerializeField] private TMP_InputField _searchInputField;
    [SerializeField] private Transform _savesScrollViewContent;
    [SerializeField] private GameObject _buttonPrefab; // Przycisk odpowiadający każdemu zapisowi na liście
    [SerializeField] private GameObject _loadGamePanel;
    [SerializeField] private GameObject _saveGamePanel;
    [SerializeField] private GameObject _removeSaveFilePanel;
    [SerializeField] private UnityEngine.UI.Toggle _sortByDateToggle;

    public bool IsLoading;
    public bool IsOnlyUnitsLoading;
    public bool IsOnlyMapLoading;
    public string CurrentGameName;
    private bool _isSchedulingLearningAutoStart;

    #region Saving methods
    public void SaveSettings()
    {
        // Ścieżka do pliku ustawień
        string settingsFilePath = Path.Combine(Application.persistentDataPath, "GameSettings.json");

        // Tworzenie obiektu ustawień
        GameSettings settings = new GameSettings();

        // Pobieranie wszystkich pól typu bool z GameManager i przypisywanie ich do settings
        foreach (var field in typeof(GameManager).GetFields(BindingFlags.Static | BindingFlags.Public))
        {
            if (field.FieldType == typeof(bool))
            {
                var settingField = typeof(GameSettings).GetField(field.Name, BindingFlags.Instance | BindingFlags.Public);
                if (settingField != null && settingField.FieldType == typeof(bool))
                {
                    settingField.SetValue(settings, field.GetValue(null));
                }
            }
        }

        // Ustawienia dla kolorów tła
        settings.BackgroundColorR = CameraManager.BackgroundColor.r;
        settings.BackgroundColorG = CameraManager.BackgroundColor.g;
        settings.BackgroundColorB = CameraManager.BackgroundColor.b;

        // Serializacja do JSON
        string json = JsonUtility.ToJson(settings, true);
        File.WriteAllText(settingsFilePath, json);
    }

    public void SaveGame(string saveName = "")
    {
        if (saveName != null && saveName.Length > 0)
        {
            _saveNameInput.text = saveName;
        }

        if (_saveNameInput.text.Length < 1 || _saveNameInput.text == "autosave" || _saveNameInput.text == "temp" || _saveNameInput.text == "savedUnitsList")
        {
            Debug.Log($"<color=red>Zapis nieudany. Niepoprawna nazwa pliku.</color>");
            return;
        }

        List<Unit> allUnits = UnitsManager.Instance.AllUnits;

        if (allUnits.Count < 1)
        {
            Debug.Log($"<color=red>Zapis nieudany. Aby zapisać grę, musisz umieścić na polu bitwy chociaż jedną jednostkę.</color>");
            return;
        }

        SaveUnits(allUnits);

        SaveRoundsManager(_saveNameInput.text);

        //Zapisanie wszystkich elementów mapy
        SaveMap();

        //Przechowanie nazwy aktualnej gry (potrzebne do wykonywania automatycznego zapisu)
        CurrentGameName = _saveNameInput.text;

        //Resetuje input fielda i zamyka panel
        _saveNameInput.text = "";

        Debug.Log($"<color=green>Zapisano stan gry: {CurrentGameName}</color>");
    }

    public void SaveUnitToUnitsList()
    {
        List<Unit> selectedUnit = new List<Unit>();
        selectedUnit.Add(Unit.SelectedUnit.GetComponent<Unit>());

        SaveUnits(selectedUnit, "savedUnitsList");
    }

    public void SaveUnits(List<Unit> allUnits, string savesFolderName = "")
    {
        // Jeśli nazwa folderu nie została podana, użyj nazwy z pola wejściowego
        if (string.IsNullOrEmpty(savesFolderName))
        {
            savesFolderName = _saveNameInput.text;
        }

        String filePath = Application.persistentDataPath + "/" + savesFolderName;

        //Jeśli kopiujemy jednostki do schowka, to czyścimy schowek
        if (Directory.Exists(filePath) && savesFolderName != "savedUnitsList")
        {
            Directory.Delete(filePath, true); // Usuwa katalog wraz z zawartością
        }

        if (!Directory.Exists(filePath))
        {
            Directory.CreateDirectory(filePath);
        }

        foreach (var unit in allUnits)
        {
            string unitName = unit.GetComponent<Stats>().Name;

            string unitPath = Path.Combine(Application.persistentDataPath, savesFolderName, unitName + "_unit.json");
            string statsPath = Path.Combine(Application.persistentDataPath, savesFolderName, unitName + "_stats.json");
            string weaponPath = Path.Combine(Application.persistentDataPath, savesFolderName, unitName + "_weapon.json");
            string inventoryPath = Path.Combine(Application.persistentDataPath, savesFolderName, unitName + "_inventory.json");
            string tokenJsonPath = Path.Combine(Application.persistentDataPath, savesFolderName, unitName + "_token.json");

            // Resetowanie broni do wartości bazowych przed zapisem
            foreach (Weapon weapon in unit.GetComponent<Inventory>().AllWeapons)
            {
                InventoryManager.Instance.ResetToBaseWeaponStats(weapon);
            }

            UnitData unitData = new UnitData(unit);

            StatsData statsData = new StatsData(unit.GetComponent<Stats>());
            WeaponData weaponData = new WeaponData(unit.GetComponent<Weapon>());
            InventoryData inventoryData = new InventoryData(unit.GetComponent<Inventory>());
            TokenData tokenData = new TokenData { filePath = unit.TokenFilePath };

            string unitJsonData = JsonUtility.ToJson(unitData, true);
            string statsJsonData = JsonUtility.ToJson(statsData, true);
            string weaponJsonData = JsonUtility.ToJson(weaponData, true);
            string inventoryJsonData = JsonUtility.ToJson(inventoryData, true);
            string tokenJsonData = JsonUtility.ToJson(tokenData, true);

            File.WriteAllText(unitPath, unitJsonData);
            File.WriteAllText(statsPath, statsJsonData);
            File.WriteAllText(weaponPath, weaponJsonData);
            File.WriteAllText(inventoryPath, inventoryJsonData);
            File.WriteAllText(tokenJsonPath, tokenJsonData);

            // Po zapisaniu ponownie nakładamy efekty amunicji
            foreach (Weapon weapon in unit.GetComponent<Inventory>().AllWeapons)
            {
                InventoryManager.Instance.ApplyAmmoModifiers(weapon);
            }
        }

        if (savesFolderName == "savedUnitsList")
        {
            Debug.Log($"<color=green>Jednostka '{Unit.SelectedUnit.GetComponent<Stats>().Name}' została zapisana.</color>");
            DataManager.Instance.LoadAndUpdateStats();
        }
    }

    private void SaveRoundsManager(string savesFolderName)
    {
        string roundsManagerPath = Path.Combine(Application.persistentDataPath, savesFolderName, "RoundsManager.json");

        RoundsManagerData roundsManagerData = new RoundsManagerData();

        string roundsManagerJsonData = JsonUtility.ToJson(roundsManagerData, true);

        // Zapisanie danych do pliku
        File.WriteAllText(roundsManagerPath, roundsManagerJsonData);
    }

    private void SaveGridManager(string savesFolderName)
    {
        string gridManagerPath = Path.Combine(Application.persistentDataPath, savesFolderName, "GridManager.json");

        GridManagerData gridManagerData = new GridManagerData();

        string gridManagerJsonData = JsonUtility.ToJson(gridManagerData, true);

        // Zapisanie danych do pliku
        File.WriteAllText(gridManagerPath, gridManagerJsonData);
    }

    public void SaveFortunePoints(string savesFolderName, Stats stats, int TempPL)
    {
        string unitName = stats.Name;

        string statsPath = Path.Combine(Application.persistentDataPath, savesFolderName, unitName + "_stats.json");

        StatsData statsData = new StatsData(stats);
        statsData.TempPL = TempPL;

        string statsJsonData = JsonUtility.ToJson(statsData, true);
        File.WriteAllText(statsPath, statsJsonData);
    }

    public void SaveMap()
    {
        string savesFolderName;

        // Stworzenie folderu dla zapisów
        savesFolderName = _saveNameInput.text;
        Directory.CreateDirectory(Application.persistentDataPath + "/" + savesFolderName);

        // Pobranie listy zapisanych plików
        string previousFile = Path.Combine(Application.persistentDataPath, savesFolderName, "MapElements.json");

        // Usuwa plik z poprzedniego zapisu
        File.Delete(previousFile);

        MapElementsContainer container = new MapElementsContainer();

        if (MapEditor.Instance == null) return;

        // Zbieranie danych z każdego elementu
        foreach (var element in MapEditor.Instance.AllElements)
        {
            if(element == null) continue;

            MapElement mapElement = element.GetComponent<MapElement>();
            if (mapElement != null)
            {
                MapElementsData data = new MapElementsData(mapElement);
                container.Elements.Add(data);
            }
        }

        // Zbieranie danych TileCover
        foreach (var tileCover in MapEditor.Instance.AllTileCovers)
        {
            if (tileCover == null) continue;
            TileCoverData data = new TileCoverData(tileCover.transform.position, tileCover.GetComponent<TileCover>().Number);
            container.TileCovers.Add(data);
        }

        container.BackgroundImagePath = MapEditor.BackgroundImagePath;
        container.BackgroundPositionX = MapEditor.BackgroundPositionX;
        container.BackgroundPositionY = MapEditor.BackgroundPositionY;
        container.BackgroundScale = MapEditor.BackgroundScale;

        // Zapis koloru tła
        Color backgroundColor = CameraManager.BackgroundColor;
        container.BackgroundColorR = backgroundColor.r;
        container.BackgroundColorG = backgroundColor.g;
        container.BackgroundColorB = backgroundColor.b;

        // Ścieżka do pliku JSON
        string mapElementsPath = Path.Combine(Application.persistentDataPath, savesFolderName, "MapElements.json");

        // Konwersja kontenera z listą danych do JSON
        string mapElementsJsonData = JsonUtility.ToJson(container, true);

        // Zapis do pliku
        File.WriteAllText(mapElementsPath, mapElementsJsonData);

        //Zapisanie siatki
        SaveGridManager(savesFolderName);

        Debug.Log($"<color=green>Zapisano mapę.</color>");
    }
    #endregion

    #region Loading methods

    //Ustala, czy wczytujemy całą grę, czy jedynie jednostki
    public void SetLoadingType(string value)
    {
        IsOnlyUnitsLoading = value == "units" ? true : false;
        IsOnlyMapLoading = value == "map" ? true : false;
    }

    public void FilterList()
    {
        string searchText = _searchInputField.text.ToLower();

        foreach (Transform child in _savesScrollViewContent)
        {
            var buttonText = child.GetComponentInChildren<TextMeshProUGUI>();

            if (buttonText == null) continue;

            // Sprawdzaj, czy tekst zawiera wyszukiwaną frazę
            bool matchesSearch = buttonText.text.ToLower().Contains(searchText);

            // Ukryj/wyświetl przycisk na podstawie wyniku wyszukiwania
            child.gameObject.SetActive(matchesSearch);
        }
    }
    public void LoadSettings()
    {
        // Ścieżka do pliku ustawień
        string settingsFilePath = Path.Combine(Application.persistentDataPath, "GameSettings.json");

        // Sprawdzanie, czy plik istnieje
        if (File.Exists(settingsFilePath))
        {
            // Deserializacja z JSON
            string json = File.ReadAllText(settingsFilePath);
            GameSettings settings = JsonUtility.FromJson<GameSettings>(json);

            // Ustawianie wartości pól typu bool w GameManager na podstawie załadowanych danych
            foreach (var field in typeof(GameManager).GetFields(BindingFlags.Static | BindingFlags.Public))
            {
                if (field.FieldType == typeof(bool))
                {
                    var settingField = typeof(GameSettings).GetField(field.Name, BindingFlags.Instance | BindingFlags.Public);
                    if (settingField != null && settingField.FieldType == typeof(bool))
                    {
                        field.SetValue(null, settingField.GetValue(settings));
                    }
                }
            }

            // Wczytuje kolor tła
            Color loadedColor = new Color(
                settings.BackgroundColorR,
                settings.BackgroundColorG,
                settings.BackgroundColorB
            );

            // Ustawienie koloru tła
            if (ColorPicker.Instance != null)
            {
                ColorPicker.Instance.SetColor(loadedColor);
            }
            else
            {
                GameObject mainCamera = GameObject.Find("Main Camera");
                GameObject playersCamera = GameObject.Find("Players Camera");

                if (mainCamera != null)
                {
                    mainCamera.GetComponent<CameraManager>().ChangeBackgroundColor(loadedColor);
                }

                if (playersCamera != null)
                {
                    GameObject.Find("Players Camera").GetComponent<CameraManager>().ChangeBackgroundColor(loadedColor);
                }
            }
        }
    }

    public void LoadGame(string saveName = "")
    {
        CustomDropdown dropdown = _savesScrollViewContent.GetComponent<CustomDropdown>();
        if (dropdown == null || (saveName == "" && dropdown.SelectedButton == null))
        {
            Debug.Log($"<color=red>Aby wczytać grę musisz wybrać plik z listy.</color>");
            return;
        }

        if (saveName.Length < 1)
        {
            saveName = dropdown.SelectedButton.GetComponentInChildren<TextMeshProUGUI>().text;
        }

        string saveFolderPath = Path.Combine(Application.persistentDataPath, saveName);

        if (!Directory.Exists(saveFolderPath))
        {
            Debug.Log("Nie znaleziono pliku o podanej nazwie.");
            return;
        }

        //Automatycznie zapisuje aktualną grę przed wczytaniem innej
        if (GameManager.IsAutosaveMode && CurrentGameName != null && CurrentGameName.Length > 0 && !IsOnlyMapLoading && !IsOnlyUnitsLoading)
        {
            SaveGame(CurrentGameName);
        }

        _searchInputField.text = "";

        //Przechowanie nazwy aktualnej gry (potrzebne do wykonywania automatycznego zapisu)
        CurrentGameName = saveName;

        IsLoading = true;

        if (RoundsManager.Instance != null)
        {
            RoundsManager.Instance.ResetRoundFlowState();
        }

        if(IsOnlyMapLoading != true)
        {
            //Odznaczenie zaznaczonej postaci
            if (Unit.SelectedUnit != null)
            {
                Unit.SelectedUnit.GetComponent<Unit>().SelectUnit();
            }

            // Kopiuje listę jednostek do nowej listy, aby móc bezpiecznie modyfikować oryginalną listę
            List<Unit> unitsToRemove = new List<Unit>(UnitsManager.Instance.AllUnits);

            // Usuwa wszystkie obecne na polu bitwy jednostki
            foreach (var unit in unitsToRemove)
            {
                if (unit != null)
                {
                    UnitsManager.Instance.DestroyUnit(unit.gameObject);
                }
            }
        }

        if (saveName != "autosave" && IsOnlyUnitsLoading != true)
        {
            //Wczytanie mapy
            LoadMap();
        }

        if (IsOnlyMapLoading != true)
        {
            StartCoroutine(LoadAllUnitsWithDelay(saveFolderPath));
        }

        if (_loadGamePanel != null)
        {
            _loadGamePanel.SetActive(false);
        }
    }

    public IEnumerator LoadAllUnitsWithDelay(string saveFolderPath)
    {
        var unitFiles = Directory.GetFiles(saveFolderPath, "*_unit.json");

        if (unitFiles == null)
        {
            IsLoading = false;
            yield break;
        }

        if (Unit.SelectedUnit != null)
        {
            Unit selectedUnitComponent = Unit.SelectedUnit.GetComponent<Unit>();
            if (selectedUnitComponent != null)
            {
                selectedUnitComponent.SelectUnit();
            }
        }

        foreach (string unitFile in unitFiles)
        {
            string baseFileName = Path.GetFileNameWithoutExtension(unitFile).Replace("_unit", "");
            if (string.IsNullOrWhiteSpace(baseFileName))
            {
                Debug.LogWarning($"Pominieto nieprawidlowy plik jednostki: {unitFile}");
                continue;
            }

            // Sciezki do konkretnych plikow z danymi
            string unitFilePath = Path.Combine(saveFolderPath, baseFileName + "_unit.json");
            string statsFilePath = Path.Combine(saveFolderPath, baseFileName + "_stats.json");
            string weaponFilePath = Path.Combine(saveFolderPath, baseFileName + "_weapon.json");
            string inventoryFilePath = Path.Combine(saveFolderPath, baseFileName + "_inventory.json");
            string tokenJsonPath = Path.Combine(saveFolderPath, baseFileName + "_token.json");

            if (!File.Exists(unitFilePath) || !File.Exists(statsFilePath) || !File.Exists(weaponFilePath) || !File.Exists(inventoryFilePath))
            {
                Debug.LogWarning($"Pominieto niekompletny zapis jednostki: {baseFileName}");
                continue;
            }

            StatsData statsData = null;
            UnitData unitData = null;
            InventoryData inventoryData = null;

            try
            {
                statsData = JsonUtility.FromJson<StatsData>(File.ReadAllText(statsFilePath));
                unitData = JsonUtility.FromJson<UnitData>(File.ReadAllText(unitFilePath));
                inventoryData = JsonUtility.FromJson<InventoryData>(File.ReadAllText(inventoryFilePath));
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Pominieto uszkodzony zapis jednostki '{baseFileName}': {ex.Message}");
                continue;
            }

            if (statsData == null || unitData == null || inventoryData == null)
            {
                Debug.LogWarning($"Pominieto zapis jednostki '{baseFileName}' - brak poprawnych danych JSON.");
                continue;
            }

            // Sprawdzamy, czy istnieje juz jednostka o tej nazwie
            bool unitExist = UnitsManager.Instance.AllUnits.Any(
                unit => unit != null && unit.GetComponent<Stats>() != null && unit.GetComponent<Stats>().Name == statsData.Name
            );

            // Sprawdzenie, czy istnieje juz jednostka bedaca kopia
            bool copyExist = UnitsManager.Instance.AllUnits.Any(
                unit => unit != null && unit.GetComponent<Stats>() != null && unit.GetComponent<Stats>().Name == statsData.Name + " (kopia)"
            );

            // Jesli istnieje kopia, pomijamy tworzenie nowej jednostki
            if ((unitExist && copyExist) || (!string.IsNullOrEmpty(statsData.Name) && statsData.Name.Contains("(kopia)")))
            {
                Debug.Log($"Istnieje juz kopia {statsData.Name}.");
                continue;
            }

            // Ustalenie pozycji jednostki
            Vector3 position = new Vector3(unitData.position[0], unitData.position[1], unitData.position[2]);

            // Jezeli jest to proces uczenia AI to wczytujemy jednostki na losowych pozycjach
            if (ReinforcementLearningManager.Instance != null && ReinforcementLearningManager.Instance.IsLearning)
            {
                List<Vector2> availablePositions = GridManager.Instance.AvailablePositions();

                if (availablePositions.Count != 0)
                {
                    // Wybranie losowej pozycji z dostepnych
                    int randomIndex = UnityEngine.Random.Range(0, availablePositions.Count);
                    position = availablePositions[randomIndex];
                }
            }

            GameObject unitGameObject = UnitsManager.Instance.CreateUnit(statsData.Id, statsData.Name, position);
            if (unitGameObject == null) continue;

            Unit createdUnit = unitGameObject.GetComponent<Unit>();
            Stats createdStats = unitGameObject.GetComponent<Stats>();
            if (createdUnit == null || createdStats == null) continue;

            // Wczytanie taga i koloru jednostki
            if (unitData.Tag == "PlayerUnit")
            {
                createdUnit.DefaultColor = new Color(0f, 0.54f, 0.17f, 1.0f);
            }
            else if (unitData.Tag == "EnemyUnit")
            {
                createdUnit.DefaultColor = new Color(0.59f, 0.1f, 0.19f, 1.0f);
            }
            unitGameObject.tag = unitData.Tag;
            createdUnit.ChangeUnitColor(unitGameObject);

            // Ustawia rozmiar jednostek
            if ((int)statsData.Size != 2)
            {
                createdStats.ChangeTokenSize((int)statsData.Size);
            }

            yield return new WaitForSeconds(0.05f); // Oczekiwanie na zainicjowanie komponentow

            if (unitGameObject == null) continue;

            // Kontynuacja wczytywania i aktualizacji pozostalych danych jednostki
            LoadComponentDataWithReflection<StatsData, Stats>(unitGameObject, statsFilePath);
            LoadComponentDataWithReflection<UnitData, Unit>(unitGameObject, unitFilePath);
            LoadComponentDataWithReflection<WeaponData, Weapon>(unitGameObject, weaponFilePath);

            if (unitGameObject == null) continue;

            createdUnit = unitGameObject.GetComponent<Unit>();
            createdStats = unitGameObject.GetComponent<Stats>();
            if (createdUnit == null || createdStats == null) continue;

            if (TokensManager.Instance != null)
            {
                TokensManager.Instance.ApplyDefaultTokenIfMissing(unitGameObject);
            }

            // Dodaje jednostke do kolejki inicjatywy
            InitiativeQueueManager.Instance.AddUnitToInitiativeQueue(createdUnit);

            // Wczytanie ekwipunku jednostki
            Unit.SelectedUnit = unitGameObject;
            if (inventoryData.AllWeapons != null)
            {
                foreach (var weapon in inventoryData.AllWeapons)
                {
                    if (Unit.SelectedUnit == null) break;

                    Weapon unitWeapon = Unit.SelectedUnit.GetComponent<Weapon>();
                    if (unitWeapon == null || weapon == null) continue;

                    var fields = typeof(Weapon).GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                    var thisFields = weapon.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);

                    foreach (var field in fields)
                    {
                        var thisField = thisFields.FirstOrDefault(f => f.Name == field.Name);
                        if (thisField != null)
                        {
                            var value = thisField.GetValue(weapon); // Pobieranie wartosci z obiektu zrodlowego
                            if (value != null)
                            {
                                field.SetValue(unitWeapon, value); // Ustawianie wartosci na obiekt docelowy
                            }
                        }
                    }

                    DataManager.Instance.LoadAndUpdateWeapons(weapon);
                }
            }

            if (Unit.SelectedUnit != null)
            {
                // Wczytanie aktualnie dobytych broni
                Inventory inventory = Unit.SelectedUnit.GetComponent<Inventory>();
                if (inventory != null)
                {
                    foreach (var weapon in inventory.AllWeapons)
                    {
                        if (inventoryData.EquippedWeaponsId != null && inventoryData.EquippedWeaponsId.Length > 0 && weapon.Id == inventoryData.EquippedWeaponsId[0])
                        {
                            inventory.EquippedWeapons[0] = weapon;
                        }

                        if (inventoryData.EquippedWeaponsId != null && inventoryData.EquippedWeaponsId.Length > 1 && weapon.Id == inventoryData.EquippedWeaponsId[1])
                        {
                            inventory.EquippedWeapons[1] = weapon;
                        }

                        if (inventoryData.EquippedArmorsId != null && inventoryData.EquippedArmorsId.Contains(weapon.Id))
                        {
                            inventory.EquippedArmors.Add(weapon);
                        }
                    }

                    InventoryManager.Instance.CheckForEquippedWeapons();

                    // Wczytanie pieniedzy
                    inventory.CopperCoins = inventoryData.CopperCoins;
                    inventory.SilverCoins = inventoryData.SilverCoins;
                    inventory.GoldCoins = inventoryData.GoldCoins;
                }
            }

            // Odtworzenie zapisanych efektow zaklec
            if (statsData.ActiveSpellEffects != null && statsData.ActiveSpellEffects.Count > 0 && unitGameObject != null)
            {
                Stats unitStats = unitGameObject.GetComponent<Stats>();
                if (unitStats != null)
                {
                    unitStats.ActiveSpellEffects = statsData.ActiveSpellEffects.Select(seData => seData.ToSpellEffect()).ToList();
                }
            }

            // Wczytanie tokena, jesli istnieje
            if (unitGameObject != null && File.Exists(tokenJsonPath))
            {
                string tokenJson = File.ReadAllText(tokenJsonPath);
                TokenData tokenData = JsonUtility.FromJson<TokenData>(tokenJson);

                if (tokenData != null && !string.IsNullOrEmpty(tokenData.filePath) && tokenData.filePath.Length > 1)
                {
                    StartCoroutine(TokensManager.Instance.LoadTokenImage(tokenData.filePath, unitGameObject));
                }
            }

            if (unitGameObject != null)
            {
                Unit unitComponent = unitGameObject.GetComponent<Unit>();
                if (unitComponent != null && unitComponent.IsSelected)
                {
                    unitComponent.SelectUnit();
                }
            }

            // Jezeli wklejamy jednostki, a istnieja juz jednostki o tej nazwie, to zmieniamy im nazwe
            if (saveFolderPath == Path.Combine(Application.persistentDataPath, "temp") && unitGameObject != null)
            {
                for (int i = 0; i < UnitsManager.Instance.AllUnits.Count; i++)
                {
                    Unit unit = UnitsManager.Instance.AllUnits[i];
                    if (unit != null && unitExist && unit.gameObject != unitGameObject)
                    {
                        Stats loadedStats = unitGameObject.GetComponent<Stats>();
                        Unit loadedUnit = unitGameObject.GetComponent<Unit>();

                        if (loadedStats != null && loadedUnit != null && !loadedStats.Name.Contains("(kopia)"))
                        {
                            loadedStats.Name += " (kopia)";
                            unitGameObject.name += " (kopia)";
                            loadedUnit.DisplayUnitName();

                            // Nie kopiujemy wierzchowcow
                            loadedUnit.MountId = 0;
                            loadedUnit.IsMounted = false;
                        }
                    }
                }
            }

            if (unitGameObject != null)
            {
                Unit loadedUnit = unitGameObject.GetComponent<Unit>();
                Stats loadedStats = unitGameObject.GetComponent<Stats>();

                if (loadedUnit != null)
                {
                    loadedUnit.DisplayUnitName();
                }

                // Aktualizuje pasek przewagi w bitwie
                if (loadedStats != null)
                {
                    loadedStats.Overall = loadedStats.CalculateOverall();
                }
            }

            InitiativeQueueManager.Instance.CalculateDominance();
        }

        if (saveFolderPath != Path.Combine(Application.persistentDataPath, "temp"))
        {
            // Teraz, gdy wszystkie jednostki sa juz na mapie, mozemy odtworzyc relacje z wierzchowcami
            foreach (var unit in UnitsManager.Instance.AllUnits)
            {
                if (unit == null) continue;

                Stats unitStats = unit.GetComponent<Stats>();
                if (unitStats == null || string.IsNullOrWhiteSpace(unitStats.Name)) continue;

                string unitPath = Path.Combine(saveFolderPath, unitStats.Name + "_unit.json");
                if (!File.Exists(unitPath))
                {
                    continue;
                }

                UnitData unitData = null;
                try
                {
                    unitData = JsonUtility.FromJson<UnitData>(File.ReadAllText(unitPath));
                }
                catch
                {
                    continue;
                }

                MountsManager.Instance.GetOnMount(unit, true);

                // Ponowne ustalenie pozycji jednostki, bo przez to, ze jest na wierzchowcu
                // moze pojawic sie w losowym miejscu
                if (unit.IsMounted && unitData != null && unitData.position != null && unitData.position.Length >= 3)
                {
                    unit.transform.position = new Vector3(unitData.position[0], unitData.position[1], unitData.position[2]);
                }
            }

            LoadRoundsManager(saveFolderPath);
        }

        GridManager.Instance?.CheckTileOccupancy();

        Unit.SelectedUnit = null;
        InitiativeQueueManager.Instance?.UpdateInitiativeQueue();
        GridManager.Instance?.ResetColorOfTilesInMovementRange();
        MountsManager.Instance?.DisplayAllMountIcons();

        IsLoading = false;

        if (saveFolderPath != Path.Combine(Application.persistentDataPath, "temp"))
        {
            Debug.Log($"<color=green>Wczytano stan gry: {CurrentGameName}</color>");
        }

        bool shouldAutoStartLearningRound =
            ReinforcementLearningManager.Instance != null
            && ReinforcementLearningManager.Instance.IsLearning
            && GameManager.IsAutoCombatMode
            && string.Equals(CurrentGameName, "AIlearning", StringComparison.OrdinalIgnoreCase);

        if (shouldAutoStartLearningRound && !_isSchedulingLearningAutoStart)
        {
            _isSchedulingLearningAutoStart = true;
            StartCoroutine(StartLearningRoundAfterLoad());
        }
    }

    private IEnumerator StartLearningRoundAfterLoad()
    {
        yield return null;
        _isSchedulingLearningAutoStart = false;

        if (IsLoading) yield break;
        if (ReinforcementLearningManager.Instance == null || !ReinforcementLearningManager.Instance.IsLearning) yield break;
        if (!GameManager.IsAutoCombatMode) yield break;
        if (!string.Equals(CurrentGameName, "AIlearning", StringComparison.OrdinalIgnoreCase)) yield break;
        if (RoundsManager.Instance == null) yield break;

        RoundsManager.Instance.NextRound();
    }

    public void LoadComponentDataWithReflection<TData, TComponent>(GameObject gameObject, string filePath)
        where TData : class
        where TComponent : Component
    {
        if (gameObject == null || !File.Exists(filePath)) return;

        TData dataObject = null;
        try
        {
            // Deserializacja JSON do obiektu danych
            string jsonData = File.ReadAllText(filePath);
            dataObject = JsonUtility.FromJson<TData>(jsonData);
        }
        catch
        {
            return;
        }

        if (dataObject == null || gameObject == null) return;

        // Pobranie komponentu z GameObject
        TComponent component = gameObject.GetComponent<TComponent>();
        if (component == null) return;

        // Uzyskanie dostepu do pol w komponencie i aktualizacja ich wartosci
        FieldInfo[] componentFields = component.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        FieldInfo[] dataFields = typeof(TData).GetFields(BindingFlags.Public | BindingFlags.Instance);

        foreach (FieldInfo dataField in dataFields)
        {
            // Pomijamy pole ActiveSpellEffects, aby nie probowac przypisywac List<SpellEffectData> do List<SpellEffect>
            if (dataField.Name == "ActiveSpellEffects")
                continue;

            FieldInfo componentField = componentFields.FirstOrDefault(f => f.Name == dataField.Name);
            if (componentField != null)
            {
                object value = dataField.GetValue(dataObject);
                componentField.SetValue(component, value);
            }
        }

        if (typeof(TComponent) != typeof(Weapon) && gameObject != null)
        {
            Unit unit = gameObject.GetComponent<Unit>();
            if (unit != null)
            {
                unit.DisplayUnitHealthPoints();
            }
        }
    }

    private void LoadRoundsManager(string savesFolderPath)
    {
        string filePath = Path.Combine(savesFolderPath, "RoundsManager.json");

        // Sprawdź, czy plik istnieje
        if (File.Exists(filePath))
        {
            // Deserializuj dane z pliku JSON do obiektu RoundsManagerData
            string jsonData = File.ReadAllText(filePath);
            RoundsManagerData data = JsonUtility.FromJson<RoundsManagerData>(jsonData);

            // Załaduj wczytane dane do istniejącego obiektu RoundsManager
            RoundsManager.Instance.LoadRoundsManagerData(data);
        }
        else
        {
            Debug.LogError("Pliku nie znaleziono.");
        }
    }

    private void LoadGridManager(string filePath)
    {
        // Sprawdź, czy plik istnieje
        if (File.Exists(filePath))
        {
            // Deserializuj dane z pliku JSON do obiektu GridManagerData
            string jsonData = File.ReadAllText(filePath);
            GridManagerData data = JsonUtility.FromJson<GridManagerData>(jsonData);

            // Załaduj wczytane dane do istniejącego obiektu GridManager
            GridManager.Instance.LoadGridManagerData(data);

            GridManager.Instance.GenerateGrid();
            GridManager.Instance.CheckTileOccupancy();
        }
        else
        {
            Debug.LogError("Pliku nie znaleziono.");
        }
    }

    public void LoadMap()
    {
        _searchInputField.text = "";

        CustomDropdown dropdown = _savesScrollViewContent.GetComponent<CustomDropdown>();
        if (dropdown == null || dropdown.SelectedButton == null)
        {
            Debug.Log($"<color=red>Aby wczytać grę musisz wybrać plik z listy.</color>");
            return;
        }

        string saveName = dropdown.SelectedButton.GetComponentInChildren<TextMeshProUGUI>().text;

        string mapElementsFilePath = Path.Combine(Application.persistentDataPath, saveName, "MapElements.json");
        string gridFilePath = Path.Combine(Application.persistentDataPath, saveName, "GridManager.json");

        LoadGridManager(gridFilePath);

        // Sprawdź, czy plik istnieje
        if (File.Exists(mapElementsFilePath) && MapEditor.Instance != null)
        {
            string jsonData = File.ReadAllText(mapElementsFilePath);
            MapElementsContainer data = JsonUtility.FromJson<MapElementsContainer>(jsonData);

            // Załaduj wczytane dane do istniejącego obiektu MapEditor
            MapEditor.Instance.LoadMapData(data);
        }
        else
        {
            Debug.LogError("Pliku z mapę nie znaleziono.");
        }

        if (_loadGamePanel != null)
        {
            _loadGamePanel.SetActive(false);
        }

        if (IsOnlyMapLoading)
        {
            Debug.Log($"<color=green>Wczytano mapę: {CurrentGameName}</color>");
        }
    }
    #endregion

    #region Managing saves dropdown
    public void OpenSaveGamePanel()
    {
        GameManager.Instance.HideActivePanels();
        _saveGamePanel.SetActive(true);
    }

    public void LoadSavesDropdown()
    {
        CustomDropdown dropdown = _savesScrollViewContent.GetComponent<CustomDropdown>();

        // Wczytanie wszystkich zapisanych folderów w Application.persistentDataPath
        string[] saveFolders = Directory.GetDirectories(Application.persistentDataPath);

        // Sortowanie zapisów w zależności od stanu Toggle
        if (_sortByDateToggle.isOn)
        {
            saveFolders = saveFolders.OrderByDescending(folder => Directory.GetLastWriteTime(folder)).ToArray(); // Sortowanie według daty modyfikacji
        }
        else
        {
            saveFolders = saveFolders.OrderBy(folder => folder).ToArray(); // Sortowanie alfabetyczne
        }

        // Usunięcie istniejących przycisków z listy i ekranu
        foreach (Transform child in _savesScrollViewContent)
        {
            Destroy(child.gameObject); // Usunięcie obiektów przycisków
        }
        dropdown.Buttons.Clear(); // Wyczyszczenie listy przycisków w dropdownie

        foreach (var folderPath in saveFolders)
        {
            // Uzyskanie nazwy folderu do wyświetlenia
            string folderName = new DirectoryInfo(folderPath).Name;

            // Sprawdź, czy jest to folder tymczasowy ze skopiowanymi jednostkami
            if (folderName == "temp" || folderName == "autosave" || folderName == "savedUnitsList") continue;

            //Dodaje nazwę pliku do ScrollViewContent w postaci buttona
            GameObject buttonObj = Instantiate(_buttonPrefab, _savesScrollViewContent);
            TextMeshProUGUI buttonText = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            //Ustala text buttona
            buttonText.text = folderName;

            UnityEngine.UI.Button button = buttonObj.GetComponent<UnityEngine.UI.Button>();

            //Dodaje opcję do CustomDropdowna ze wszystkimi zapisami
            dropdown.Buttons.Add(button);

            int currentIndex = dropdown.Buttons.Count; // Pobiera indeks nowego przycisku

            // Zdarzenie po kliknięciu na konkretny zapis z listy
            button.onClick.AddListener(() =>
            {
                dropdown.SetSelectedIndex(currentIndex); // Wybiera element i aktualizuje jego wygląd
            });
        }
    }

    public void OpenRemoveSaveFilePanel()
    {
        CustomDropdown dropdown = _savesScrollViewContent.GetComponent<CustomDropdown>();
        if (dropdown == null || dropdown.SelectedButton == null)
        {
            Debug.Log($"<color=red>Aby usunąć zapis musisz wybrać plik z listy.</color>");
            return;
        }

        _removeSaveFilePanel.SetActive(true);
        _loadGamePanel.SetActive(false);
    }

    public void RemoveSaveFile()
    {
        CustomDropdown dropdown = _savesScrollViewContent.GetComponent<CustomDropdown>();
        if (dropdown == null || dropdown.SelectedButton == null)
        {
            Debug.Log($"<color=red>Aby usunąć zapis musisz wybrać plik z listy.</color>");
            return;
        }

        string saveName = dropdown.SelectedButton.GetComponentInChildren<TextMeshProUGUI>().text;

        string saveFolderPath = Path.Combine(Application.persistentDataPath, saveName);

        // Usunięcie folderu zapisu
        if (Directory.Exists(saveFolderPath))
        {
            Directory.Delete(saveFolderPath, true); // Drugi argument 'true' pozwala na usunięcie niepustych folderów
            Debug.Log($"Plik '{saveName}' został usunięty.");
        }
        else
        {
            Debug.LogWarning($"Plik '{saveName}' nie istnieje.");
            return;
        }

        // Usunięcie przycisku z UI
        int indexToRemove = dropdown.Buttons.IndexOf(dropdown.SelectedButton);

        Destroy(dropdown.Buttons[indexToRemove].gameObject);
        dropdown.Buttons.RemoveAt(indexToRemove);

        // Aktualizuje SelectedIndex i zaznaczenie
        dropdown.SelectedIndex = 0;
        dropdown.SelectedButton = null;
        dropdown.InitializeButtons();
    }
    #endregion
}


