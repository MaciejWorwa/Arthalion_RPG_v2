using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEngine;

public class DataManager : MonoBehaviour
{
    // Prywatne statyczne pole przechowujące instancję
    private static DataManager instance;

    // Publiczny dostęp do instancji
    public static DataManager Instance
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

    [SerializeField] private GameObject _buttonPrefab; // Przycisk odpowiadający każdej z broni
    [SerializeField] private Transform _weaponScrollViewContent; // Lista wszystkich dostępnych broni
    [SerializeField] private Transform _spellbookScrollViewContent; // Lista wszystkich dostępnych zaklęć
    [SerializeField] private TMP_Dropdown _spellArcanesDropdown; // Lista tradycji magii potrzebna do sortowania listy zaklęć
    [SerializeField] private TMP_Dropdown _weaponQualityDropdown; // Lista jakości broni
    [SerializeField] private Transform _unitScrollViewContent; // Lista wszystkich dostępnych ras (jednostek)
    [SerializeField] private TMP_InputField _searchInputFieldForUnits;
    [SerializeField] private TMP_InputField _searchInputFieldForWeapons;

    [SerializeField] private UnityEngine.UI.Toggle _weaponToggle;
    [SerializeField] private UnityEngine.UI.Toggle _armorToggle;



    public List<string> TokensPaths = new List<string>();

    #region Loading units stats
    public void ChangeUnitListByToggle()
    {
        foreach (Transform child in _unitScrollViewContent)
        {
            child.gameObject.SetActive(false);
        }

        _unitScrollViewContent.GetComponent<CustomDropdown>().ClearButtons();

        LoadAndUpdateStats();
    }

    public void FilterList(string listName)
    {
        Transform scrollViewContent = listName switch
        {
            "unitList" => _unitScrollViewContent,
            "weaponList" => _weaponScrollViewContent,
            _ => null
        };

        if (scrollViewContent == null) return;

        string searchText = listName == "unitList"
            ? _searchInputFieldForUnits.text.ToLower()
            : _searchInputFieldForWeapons.text.ToLower();

        bool applyWeaponFilter = listName == "weaponList"; // Filtr działa tylko dla listy broni

        foreach (Transform child in scrollViewContent)
        {
            var buttonText = child.GetComponentInChildren<TextMeshProUGUI>();
            if (buttonText == null) continue;

            bool matchesSearch = buttonText.text.ToLower().Contains(searchText);
            bool passesFilter = true;

            if (applyWeaponFilter)
            {
                // Pobieramy dane broni na podstawie jej nazwy
                WeaponData weapon = InventoryManager.Instance.AllWeaponData
                    .FirstOrDefault(w => w.Name == buttonText.text);

                if (weapon != null)
                {
                    bool isArmor = weapon.Type.Any(t => t == "head" || t == "torso" || t == "arms" || t == "legs");

                    if (_weaponToggle.isOn && isArmor) passesFilter = false;
                    if (_armorToggle.isOn && !isArmor) passesFilter = false;
                }
            }

            child.gameObject.SetActive(matchesSearch && passesFilter);
        }
    }

    public void LoadAndUpdateStats(GameObject unitObject = null)
    {
        Stats statsToUpdate = null;
        Unit unit = null;

        if (unitObject != null)
        {
            statsToUpdate = unitObject.GetComponent<Stats>();
            unit = unitObject.GetComponent<Unit>();
        }

        if (UnitsManager.Instance.IsSavedUnitsManaging) //Obsługa listy zapisanych jednostek
        {
            string savedUnitsFolder = Path.Combine(Application.persistentDataPath, "savedUnitsList");

            if (!Directory.Exists(savedUnitsFolder))
            {
                return;
            }

            string[] statsFiles = Directory.GetFiles(savedUnitsFolder, "*_stats.json");

            // Sortowanie zapisów w zależności od stanu Toggle
            if (UnitsManager.Instance.SortSavedUnitsByDateToggle.isOn)
            {
                statsFiles = statsFiles.OrderByDescending(folder => Directory.GetLastWriteTime(folder)).ToArray(); // Sortowanie według daty modyfikacji
            }
            else
            {
                statsFiles = statsFiles.OrderBy(folder => folder).ToArray(); // Sortowanie alfabetyczne
            }

            foreach (string statsFile in statsFiles)
            {
                string jsonContent = File.ReadAllText(statsFile);
                StatsData statsData = JsonUtility.FromJson<StatsData>(jsonContent);

                if (statsData == null)
                {
                    Debug.LogError($"Nie udało się załadować statystyk z pliku {statsFile}.");
                    continue;
                }

                UpdateUnitStatsAndButtons(statsData, statsToUpdate, unit);
            }
        }
        else // Obsługa standardowej listy jednostek
        {
            TextAsset jsonFile = Resources.Load<TextAsset>("units");

            if (jsonFile == null)
            {
                Debug.LogError("Nie znaleziono pliku JSON units.");
                return;
            }

            StatsData[] statsArray = JsonHelper.FromJson<StatsData>(jsonFile.text);
            if (statsArray == null || statsArray.Length == 0)
            {
                Debug.LogError("Deserializacja JSON nie powiodła się lub lista jest pusta.");
                return;
            }

            // Posortowanie ID zgodnie z kolejnością jednostek w pliku json (czyli alfabetycznie)
            for (int i = 0; i < statsArray.Length; i++)
            {
                statsArray[i].Id = i + 1;
            }

            foreach (var stats in statsArray)
            {
                UpdateUnitStatsAndButtons(stats, statsToUpdate, unit);
            }
        }
    }

    private void UpdateUnitStatsAndButtons(StatsData statsData, Stats statsToUpdate, Unit unit)
    {
        if (statsToUpdate != null && ((statsData.Id == statsToUpdate.Id && UnitsManager.Instance.IsSavedUnitsManaging == false) || (UnitsManager.Instance.IsSavedUnitsManaging == true && statsData.Name == statsToUpdate.Name)))
        {
            FieldInfo[] fields = typeof(StatsData).GetFields(BindingFlags.Instance | BindingFlags.Public);
            foreach (var field in fields)
            {
                if (field.Name == "Name" || field.Name == "ActiveSpellEffects")
                    continue; // pomiń pola, których nie chcemy kopiować refleksyjnie
                var targetField = typeof(Stats).GetField(field.Name, BindingFlags.Instance | BindingFlags.Public);
                targetField?.SetValue(statsToUpdate, field.GetValue(statsData));
            }
        }

        _searchInputFieldForUnits.text = "";

        bool buttonExists;

        if (UnitsManager.Instance.IsSavedUnitsManaging)
        {
            buttonExists = _unitScrollViewContent.GetComponentsInChildren<TextMeshProUGUI>().Any(t => t.text == statsData.Name);
        }
        else
        {
            buttonExists = _unitScrollViewContent.GetComponentsInChildren<TextMeshProUGUI>().Any(t => t.text == statsData.Race);
        }

        if (!buttonExists)
        {
            GameObject buttonObj = Instantiate(_buttonPrefab, _unitScrollViewContent);
            TextMeshProUGUI buttonText = buttonObj.GetComponentInChildren<TextMeshProUGUI>();

            if (UnitsManager.Instance.IsSavedUnitsManaging)
            {
                buttonText.text = statsData.Name;
            }
            else
            {
                buttonText.text = statsData.Race;
            }

            buttonObj.AddComponent<CreateUnitButton>();

            UnityEngine.UI.Button button = buttonObj.GetComponent<UnityEngine.UI.Button>();
            _unitScrollViewContent.GetComponent<CustomDropdown>().Buttons.Add(button);

            int currentIndex = _unitScrollViewContent.GetComponent<CustomDropdown>().Buttons.Count;
            button.onClick.AddListener(() =>
            {
                _unitScrollViewContent.GetComponent<CustomDropdown>().SetSelectedIndex(currentIndex);
            });
        }
    }

    public void DeleteSavedUnit()
    {
        // Pobierz wybrany indeks z CustomDropdown
        int selectedIndex = _unitScrollViewContent.GetComponent<CustomDropdown>().GetSelectedIndex();

        // Sprawdzenie, czy indeks jest prawidłowy
        if (selectedIndex < 1 || selectedIndex > _unitScrollViewContent.childCount)
        {
            Debug.LogError("Nieprawidłowy indeks przycisku.");
            return;
        }

        // Korekta indeksu, ponieważ w CustomDropdown indeksy zaczynają się od 1
        int adjustedIndex = selectedIndex - 1;

        // Pobierz Transform i Button dla wybranego przycisku
        Transform buttonTransform = _unitScrollViewContent.GetChild(adjustedIndex);
        UnityEngine.UI.Button buttonToDelete = buttonTransform.GetComponent<UnityEngine.UI.Button>();
        if (buttonToDelete == null)
        {
            Debug.LogError("Nie znaleziono przycisku w podanym indeksie.");
            return;
        }

        // Pobierz nazwę przycisku (tekst) do zidentyfikowania plików w Resources
        string buttonName = buttonToDelete.GetComponentInChildren<TextMeshProUGUI>().text;
        if (string.IsNullOrEmpty(buttonName))
        {
            Debug.LogError("Nie można znaleźć nazwy przycisku.");
            return;
        }

        // Ścieżka do katalogu Resources/savedUnits
        string savedUnitsFolder = Path.Combine(Application.persistentDataPath, "savedUnitsList");

        // Usuwanie plików powiązanych z przyciskiem
        string[] relatedFiles = {
            Path.Combine(savedUnitsFolder, buttonName + "_unit.json"),
            Path.Combine(savedUnitsFolder, buttonName + "_stats.json"),
            Path.Combine(savedUnitsFolder, buttonName + "_weapon.json"),
            Path.Combine(savedUnitsFolder, buttonName + "_inventory.json"),
            Path.Combine(savedUnitsFolder, buttonName + "_token.json")
        };

        foreach (string filePath in relatedFiles)
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }

        // Usuwa przycisk z listy CustomDropdown i widoku
        CustomDropdown dropdown = _unitScrollViewContent.GetComponent<CustomDropdown>();

        // Usunięcie przycisku z UI
        int indexToRemove = dropdown.Buttons.IndexOf(dropdown.SelectedButton);

        Destroy(dropdown.Buttons[indexToRemove].gameObject);
        dropdown.Buttons.RemoveAt(indexToRemove);

        // Aktualizuje SelectedIndex i zaznaczenie
        dropdown.SelectedIndex = 0;
        dropdown.SelectedButton = null;
        dropdown.InitializeButtons();

        UnitsManager.IsTileSelecting = false;

        Debug.Log($"Jednostka '{buttonName}' została usunięta z listy zapisanych jednostek.");
    }
    #endregion

    #region Loading weapons stats
    public void LoadAndUpdateWeapons(WeaponData weaponData = null)
    {
        // Ładowanie danych JSON
        TextAsset jsonFile = Resources.Load<TextAsset>("weapons");
        if (jsonFile == null)
        {
            Debug.LogError("Nie znaleziono pliku JSON.");
            return;
        }

        // Deserializacja danych JSON
        WeaponData[] weaponsArray = null;
        if (weaponData == null)
        {
            weaponsArray = JsonHelper.FromJson<WeaponData>(jsonFile.text);
            InventoryManager.Instance.AllWeaponData = weaponsArray.ToList();
        }
        else
        {
            weaponsArray = new WeaponData[] { weaponData };
        }

        if (weaponsArray == null)
        {
            Debug.LogError("Deserializacja JSON nie powiodła się. Sprawdź strukturę JSON.");
            return;
        }

        // Posortowanie ID zgodnie z kolejnością broni w pliku json (czyli alfabetycznie)
        for (int i = 0; i < weaponsArray.Length; i++)
        {
            if (weaponsArray[i].Id != 0) break;
            weaponsArray[i].Id = i + 1;
        }

        _searchInputFieldForWeapons.text = "";

        //Odniesienie do broni postaci
        Weapon weaponToUpdate = null;
        if (Unit.SelectedUnit != null)
        {
            weaponToUpdate = Unit.SelectedUnit.GetComponent<Weapon>();
        }


        foreach (var weapon in weaponsArray)
        {
            if (_weaponQualityDropdown.transform.parent.gameObject.activeSelf) //Sprawdza, czy jest otwarte okno wyboru broni. W innym wypadku oznacza to, że bronie są wczytywane z pliku i nie chcemy zmieniać ich jakości
            {
                //Ustala jakość wykonania broni
                weapon.Quality = _weaponQualityDropdown.options[_weaponQualityDropdown.value].text;
            }

            if (weaponToUpdate != null && weapon.Id == weaponToUpdate.Id)
            {
                // Używanie refleksji do aktualizacji wartości wszystkich pól w klasie Weapon
                FieldInfo[] fields = typeof(WeaponData).GetFields(BindingFlags.Instance | BindingFlags.Public);
                foreach (var field in fields)
                {
                    var targetField = typeof(Weapon).GetField(field.Name, BindingFlags.Instance | BindingFlags.Public);
                    if (targetField != null)
                    {
                        //Zapobiega zresetowaniu się czasu przeładowania przy zmianach broni. Gdy postać wcześniej posiadała/używała daną broń to jej ReloadLeft zostaje zapamiętany
                        if (weaponToUpdate.WeaponsWithReloadLeft.ContainsKey(weapon.Id) && field.Name == "ReloadLeft")
                        {
                            weaponToUpdate.ReloadLeft = weaponToUpdate.WeaponsWithReloadLeft[weapon.Id];
                            continue;
                        }

                        targetField.SetValue(weaponToUpdate, field.GetValue(weapon));
                    }
                }

                //Dodaje przedmiot do ekwipunku postaci
                InventoryManager.Instance.AddWeaponToInventory(weapon, Unit.SelectedUnit);

                //Dodaje Id broni do słownika ekwipunku postaci
                if (weaponToUpdate.WeaponsWithReloadLeft.ContainsKey(weapon.Id) == false)
                {
                    weaponToUpdate.WeaponsWithReloadLeft.Add(weapon.Id, 0);
                }
            }

            //Gdy wczytujemy ekwipunek postaci to przerywamy funkcję, żeby na liście dostępnych broni nie pojawiały się customowe przedmioty
            if (weaponData != null) return;

            bool buttonExists = _weaponScrollViewContent.GetComponentsInChildren<TextMeshProUGUI>(true).Any(t => t.text == weapon.Name);

            if (buttonExists == false)
            {
                //Dodaje broń do ScrollViewContent w postaci buttona
                GameObject buttonObj = Instantiate(_buttonPrefab, _weaponScrollViewContent);
                TextMeshProUGUI buttonText = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
                //Ustala text buttona
                buttonText.text = weapon.Name;

                UnityEngine.UI.Button button = buttonObj.GetComponent<UnityEngine.UI.Button>();

                //Dodaje opcję do CustomDropdowna ze wszystkimi brońmi
                _weaponScrollViewContent.GetComponent<CustomDropdown>().Buttons.Add(button);

                int currentIndex = _weaponScrollViewContent.GetComponent<CustomDropdown>().Buttons.Count; // Pobiera indeks nowego przycisku

                // Zdarzenie po kliknięciu na konkretny item z listy
                button.onClick.AddListener(() =>
                {
                    _weaponScrollViewContent.GetComponent<CustomDropdown>().SetSelectedIndex(currentIndex); // Wybiera element i aktualizuje jego wygląd
                });
            }
        }

        FilterList("weaponList");
    }
    #endregion

    #region Loading spells
    public void LoadAndUpdateSpells(string spellName = null)
    {
        // Ładowanie danych JSON
        TextAsset jsonFile = Resources.Load<TextAsset>("spells");
        if (jsonFile == null)
        {
            Debug.LogError("Nie znaleziono pliku JSON.");
            return;
        }

        // Deserializacja danych JSON
        List<SpellData> spellsList = null;
        spellsList = JsonHelper.FromJson<SpellData>(jsonFile.text).ToList();

        if (spellsList == null)
        {
            Debug.LogError("Deserializacja JSON nie powiodła się. Sprawdź strukturę JSON.");
            return;
        }

        // Posortowanie ID zgodnie z kolejnością broni w pliku json (czyli alfabetycznie)
        for (int i = 0; i < spellsList.Count; i++)
        {
            if (spellsList[i].Id != 0) break;
            spellsList[i].Id = i + 1;
        }

        //Odniesienie do klasy spell postaci
        Spell spellToUpdate = null;
        if (Unit.SelectedUnit != null && Unit.SelectedUnit.GetComponent<Spell>() != null && !string.IsNullOrEmpty(spellName))
        {
            spellToUpdate = Unit.SelectedUnit.GetComponent<Spell>();
        }

        //Filtrowanie listy zaklęć wg wybranej tradycji
        string selectedArcane = _spellArcanesDropdown.options[_spellArcanesDropdown.value].text;

        // Czyści obecną listę
        _spellbookScrollViewContent.GetComponent<CustomDropdown>().ClearButtons();

        foreach (var spell in spellsList)
        {
            //Filtrowanie listy zaklęć wg wybranej tradycji
            if (spell.Arcane != selectedArcane && selectedArcane != "Wszystkie zaklęcia") continue;

            // // Filtrowanie zaklęć, których Poziom Mocy jest poza możliwościami aktualnej jednostki
            // if (Unit.SelectedUnit != null && spell.CastingNumber > Unit.SelectedUnit.GetComponent<Stats>().Mag * 11) continue;

            //Dodaje zaklęcie do ScrollViewContent w postaci buttona
            GameObject buttonObj = Instantiate(_buttonPrefab, _spellbookScrollViewContent);
            TextMeshProUGUI buttonText = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
            //Ustala text buttona
            buttonText.text = spell.Name;

            UnityEngine.UI.Button button = buttonObj.GetComponent<UnityEngine.UI.Button>();

            CustomDropdown spellbookDropdown = _spellbookScrollViewContent.GetComponent<CustomDropdown>();

            //Dodaje opcję do CustomDropdowna ze wszystkimi zaklęciami
            spellbookDropdown.Buttons.Add(button);

            // Wyświetla przy zaklęciu wymagany poziom mocy
            DisplayCastingNumberInfo(button, spell.CastingNumber);

            int currentIndex = _spellbookScrollViewContent.GetComponent<CustomDropdown>().Buttons.Count; // Pobiera indeks nowego przycisku

            // Zdarzenie po kliknięciu na konkretny item z listy
            button.onClick.AddListener(() =>
            {
                _spellbookScrollViewContent.GetComponent<CustomDropdown>().SetSelectedIndex(currentIndex); // Wybiera element i aktualizuje jego wygląd
            });

            if (spellToUpdate != null && spell.Name == spellName)
            {
                // Używanie refleksji do aktualizacji wartości wszystkich pól w klasie Spell
                FieldInfo[] fields = typeof(SpellData).GetFields(BindingFlags.Instance | BindingFlags.Public);
                foreach (var field in fields)
                {
                    var targetField = typeof(Spell).GetField(field.Name, BindingFlags.Instance | BindingFlags.Public);
                    if (targetField != null)
                    {
                        targetField.SetValue(spellToUpdate, field.GetValue(spell));
                    }
                }
            }
        }
    }

    public void DisplayCastingNumberInfo(UnityEngine.UI.Button button, int castingNumber)
    {
        button.transform.Find("castingNumber_text").gameObject.SetActive(true);

        string castingNumberText = castingNumber.ToString();

        button.transform.Find("castingNumber_text").GetComponent<TMP_Text>().text = castingNumberText;
    }
    #endregion
}

public static class JsonHelper
{
    public static T[] FromJson<T>(string json)
    {
        Wrapper<T> wrapper = JsonUtility.FromJson<Wrapper<T>>(json);

        // Sprawdzenie, które dane zostały wczytane i zwrócenie odpowiedniej tablicy
        if (wrapper.Units != null)
        {
            return wrapper.Units;
        }
        else if (wrapper.Weapons != null)
        {
            return wrapper.Weapons;
        }
        else if (wrapper.Spells != null)
        {
            return wrapper.Spells;
        }
        else
        {
            Debug.LogError("Deserializacja JSON nie powiodła się. Sprawdź składnię i strukturę JSON.");
            return null;
        }
    }

    [System.Serializable]
    private class Wrapper<T>
    {
        public T[] Units;
        public T[] Weapons;
        public T[] Spells;
    }
}

#region Data classes
[System.Serializable]
public class TokenData
{
    public string filePath;
}
[System.Serializable]
public class UnitData
{
    public int UnitId; // Unikalny Id jednostki

    public string Tag;
    public string TokenFilePath;
    public float[] position;

    public bool IsSelected;
    public bool IsTurnFinished;
    public bool IsRunning; // Biegnie
    public bool IsCharging; // Szarżuje
    public bool IsFlying; // Leci
    public bool IsRetreating; // Wycofuje się


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

    //public bool IsFearTestPassed; // Zdał test strachu
    //public bool IsTerrorTestPassed; // Zdał test grozy
    //public int FearLevel; // Poziom strachu
    //public List<int> FearedUnitIds = new List<int>(); // Lista Id jednostek, których się boi

    //STARE
    public int SpellDuration; // Czas trwania zaklęcia mającego wpływ na tą jednostkę

    public int AimingBonus;

    public bool CanMove;
    public bool CanAttack;
    public bool CanCastSpell;

    public int MountId;
    public bool IsMounted; // Zmienna określająca, czy jednostka w danej chwili dosiada wierzchowca, czy nie
    public bool HasRider; // Zmienna określająca, czy jednostka w danej chwili jest przez kogoś dosiadana

    public UnitData(Unit unit)
    {
        // Pobiera wszystkie pola (zmienne) z klasy Stats
        var fields = unit.GetType().GetFields();
        var thisFields = this.GetType().GetFields();

        // Dla każdego pola z klasy stats odnajduje pole w klasie this (czyli UnitData) i ustawia mu wartość jego odpowiednika z klasy Unit
        foreach (var thisField in thisFields)
        {
            var field = fields.FirstOrDefault(f => f.Name == thisField.Name); // Znajduje pierwsze pole o tej samej nazwie wśród pol z klasy Unit

            if (field != null && field.GetValue(unit) != null)
            {
                thisField.SetValue(this, field.GetValue(unit));
            }
        }

        Tag = unit.gameObject.tag;

        position = new float[3];
        position[0] = unit.gameObject.transform.position.x;
        position[1] = unit.gameObject.transform.position.y;
        position[2] = unit.gameObject.transform.position.z;
    }
}

[System.Serializable]
public class StatsData
{
    public int Id;
    public int Exp; // Punkty doświadczenia
    public string Name;
    public string Race;
    public string Type;

    public SizeCategory Size; // Rozmiar

    public List<PairString> PrimaryWeaponAttributes = new List<PairString>();
    public List<string> PrimaryWeaponNames = new List<string>();

    [Header("Cechy")]
    public int S;
    public int K;
    public int Zw;
    public int Zr;
    public int Int;
    public int P;
    public int Ch;
    public int SW;

    [Header("Cechy drugorzędowe")]
    public int Sz;
    public int TempSz;
    public int MaxHealth;
    public int TempHealth;
    public int CriticalWounds; // Ilość Ran Krytycznych
    public int SinPoints; // Punkty Grzechu (istotne dla kapłanów)
    public int TempPL; // Punkty losu aktualne
    public int MaxPL; // Punkty Losu Maksymalne
    public int PB; // Punkty Bohatera
    public int ExtraPoints; // Dodatkowe punkty do rozdania między PL a PB
    public int Initiative; // Inicjatywa
    public int CurrentEncumbrance; // Aktualne obciążenie ekwipunkiem
    public int MaxEncumbrance; // Maksymalny udźwig
    public int ExtraEncumbrance; // Dodatkowe obciążenie za przedmioty niebędące uzbrojeniem

    [Header("Zbroja")]
    public int Armor_head;
    public int Armor_arms;
    public int Armor_torso;
    public int Armor_legs;

    public int ArmorPenaltyZw; // bieżąca kara z pancerza zastosowana do Zw
    public int ArmorPenaltyP;  // bieżąca kara z pancerza zastosowana do P


    [Header("Umiejętności")]
    public int Athletics; // Atletyka
    public int Cool; // Opanowanie
    public int Dodge; // Unik
    public int Endurance; // Odporność
    public int MeleeCombat; // Walka Wręcz
    public int RangedCombat; // Walka Dystansowa
    public int Reflex; // Refleks
    public int Spellcasting; // Rzucanie zaklęć

    public int Pray; // Modlitwa
    public int Channeling; // Splatanie magii
    public int MagicLanguage; // Język magiczny


    [Header("Talenty")]
    public bool AccurateShot; // Celny strzał
    public bool Chosen; //Wybraniec Boży
    public bool CombatMaster; // Wojownik
    public bool Fast; // Szybki
    public bool Fencing; // Szermierka
    public bool Hardy; // Twardziel
    public int Pitiless; // Bezlitosny
    public int Religious; // Pobożny
    public bool Sharpshooter; // Strzelec wyborowy
    public int SurvivalInstinct; // Instynkt Przetrwania

    public string[] Magic = new string[5]; // ścieżki magii
    public string[] Resistance = new string[4]; // np. ["Fizyczne", "Ogień"]
    public string[] Slayer = new string[3];
    public string[] Specialist = new string[3]; // null/"" = pusty slot


    [Header("Cechy stworzeń")]
    public bool BlackMagic; // Czarna Magia
    public int Flight; // Latający
    public bool Hungry; // Żarłoczny
    public int NaturalArmor;
    public int Scary; // Straszny
    public bool Slow; // Powolny
    public bool Stink; // Smród
    public bool Tough; // Wytrzymały
    public bool Undead; // Nieumarły
    public bool Unmeaning; // Bezrozumny

    [Header("Statystyki")]
    public int HighestDamageDealt; // Największe zadane obrażenia
    public int TotalDamageDealt; // Suma zadanych obrażeń
    public int HighestDamageTaken; // Największe otrzymane obrażenia
    public int TotalDamageTaken; // Suma otrzymanych obrażeń
    public int OpponentsKilled; // Zabici przeciwnicy
    public string StrongestDefeatedOpponent; // Najsilniejszy pokonany przeciwnik
    public int StrongestDefeatedOpponentOverall; // Overall najsilniejszego pokonanego przeciwnika
    public int RoundsPlayed; // Suma rozegranych rund
    public int FortunateEvents; // Ilość "Szczęść"
    public int UnfortunateEvents; // Ilość "Pechów"

    public string Notebook; // Notatka

    // lista efektów zaklęcia
    public List<SpellEffectData> ActiveSpellEffects = new List<SpellEffectData>();

    public StatsData(Stats stats)
    {
        var fields = stats.GetType().GetFields();
        var thisFields = this.GetType().GetFields();

        foreach (var thisField in thisFields)
        {
            // Pomijamy pole ActiveSpellEffects, bo wykonamy je ręcznie
            if (thisField.Name == "ActiveSpellEffects")
                continue;

            var field = fields.FirstOrDefault(f => f.Name == thisField.Name);
            if (field != null && field.GetValue(stats) != null)
            {
                if (thisField.FieldType == typeof(List<string>) && field.FieldType == typeof(List<string>))
                {
                    var listValue = (List<string>)field.GetValue(stats);
                    thisField.SetValue(this, new List<string>(listValue));
                }
                else
                {
                    thisField.SetValue(this, field.GetValue(stats));
                }
            }
        }

        // Ręczna konwersja aktywnych efektów zaklęć
        if (stats.ActiveSpellEffects != null && stats.ActiveSpellEffects.Count > 0)
        {
            ActiveSpellEffects = stats.ActiveSpellEffects.Select(e => new SpellEffectData(e)).ToList();
        }

        if (stats.PrimaryWeaponAttributes != null)
        {
            PrimaryWeaponAttributes = stats.PrimaryWeaponAttributes
                .Select(a => new PairString { Key = a.Key, Value = a.Value })
                .ToList();
        }
    }

}

//Klasa pomocnicza do zapisywania słowników
[System.Serializable]
public class Pair
{
    public string Key;
    public int Value;
}

//Klasa pomocnicza do zapisywania słowników stringów
[System.Serializable]
public class PairString
{
    public string Key;
    public string Value;
}

[System.Serializable]
public class WeaponData
{
    public int Id;
    public string Name;
    public string[] Type;
    public string Quality;
    public int Damage; // Obrażenia
    public bool Broken; // Uszkodzenie broni
    public bool TwoHanded;
    public bool NaturalWeapon;
    public float AttackRange;
    public int ReloadTime;
    public int ReloadLeft;
    public string AmmoType; // Rodzaj amunicji

    [Header("Wymagania")]
    public int S;
    public int Zr;

    [Header("Kary")]
    public int Zw;
    public int P;

    //BROŃ
    public int Defensive; // Parująca
    public bool Entangle; //Unieruchamiająca
    public bool Fast; // Szybka
    public bool Penetrating; // Przebijająca
    public bool Pummel; // Ogłuszająca
    public bool Slow; // Powolna
    public bool Magical; // Magiczna
    public int Poisonous; // Zatruta, np. kły jadowe Hasai

    //PANCERZ
    public int Armor;

    public WeaponData(Weapon weapons)
    {
        // Pobiera wszystkie pola (zmienne) z klasy Stats
        var fields = weapons.GetType().GetFields();
        var thisFields = this.GetType().GetFields();

        // Dla każdego pola z klasy stats odnajduje pole w klasie this (czyli WeaponData) i ustawia mu wartość jego odpowiednika z klasy Weapon
        foreach (var thisField in thisFields)
        {
            var field = fields.FirstOrDefault(f => f.Name == thisField.Name); // Znajduje pierwsze pole o tej samej nazwie wśród pol z klasy Weapon

            if (field != null && field.GetValue(weapons) != null)
            {
                thisField.SetValue(this, field.GetValue(weapons));
            }
        }
    }
}


[System.Serializable]
public class ArmorData
{
    public int Id;
    public string Name;
    public string Quality;
    public string Category;
    public int Encumbrance; // Obciążenie
    public bool Broken; // Uszkodzenie

    public bool Flexible;  // Można na niego ubierać inną zbroję

    public ArmorData(Armor armors)
    {
        // Pobiera wszystkie pola (zmienne) z klasy Stats
        var fields = armors.GetType().GetFields();
        var thisFields = this.GetType().GetFields();

        // Dla każdego pola z klasy stats odnajduje pole w klasie this (czyli WeaponData) i ustawia mu wartość jego odpowiednika z klasy Weapon
        foreach (var thisField in thisFields)
        {
            var field = fields.FirstOrDefault(f => f.Name == thisField.Name); // Znajduje pierwsze pole o tej samej nazwie wśród pol z klasy Weapon

            if (field != null && field.GetValue(armors) != null)
            {
                thisField.SetValue(this, field.GetValue(armors));
            }
        }
    }
}

[System.Serializable]
public class SpellData
{
    public int Id;
    public string Name;
    public string Arcane;
    public string[] Type; // np. offensive, buff, armor-ignoring, no-damage
    public int CastingNumber; //poziom mocy
    public float Range; // zasięg
    public int[] Strength; // siła zaklęcia
    public int AreaSize; // obszar działania
    public int Duration; // czas trwania zaklęcia
    public int Targets; // ilość celów

    public string DamageType; // Rodzaj obrażeń, np. ice, poison, physical
    public string SaveAttribute; // Cecha, która jest testowana u celu zaklęcia, aby się przed nim obronić
    public string SaveSkill; // Umiejętność, która jest testowana u celu zaklęcia, aby się przed nim obronić
    public int SaveDifficulty; // Trudność testu obronnego

    //public bool SaveTestRequiring; // określa, czy zaklęcie powoduje konieczność wykonania testu obronnego
    //public int AttributeValue; // określa o ile są zmieniane cechy opisane w tabeli Attribute
    //public string[] Attribute; // określa cechę, jaka jest testowana podczas próby oparcia się zaklęciu lub cechę na którą wpływa zaklęcie (np. podnosi ją lub obniża). Czasami jest to więcej cech, np. Pancerz Etery wpływa na każdą z lokalizacji

    //public Dictionary<string, int> Attributes;
    public List<AttributePair> Attributes;

    public bool ArmourIgnoring;
    public bool MetalArmourIgnoring;

    public SpellData(Spell spell)
    {
        var fields = spell.GetType().GetFields();
        var thisFields = this.GetType().GetFields();

        foreach (var thisField in thisFields)
        {
            var field = fields.FirstOrDefault(f => f.Name == thisField.Name);

            if (field != null && field.GetValue(spell) != null)
            {
                thisField.SetValue(this, field.GetValue(spell));
            }
        }

        if (spell.Attributes != null)
        {
            Attributes = new List<AttributePair>(spell.Attributes);
        }
    }
}

[System.Serializable]
public class SpellEffectData
{
    public string SpellName;
    public int RemainingRounds;
    // Używamy listy Pair, gdzie Pair ma pola Key (string) i Value (int)
    public List<Pair> StatModifiers;

    public SpellEffectData() { }

    public SpellEffectData(SpellEffect effect)
    {
        SpellName = effect.SpellName;
        RemainingRounds = effect.RemainingRounds;
        StatModifiers = effect.StatModifiers.Select(kvp => new Pair { Key = kvp.Key, Value = kvp.Value }).ToList();
    }

    // Metoda pomocnicza do odtworzenia obiektu SpellEffect po wczytaniu
    public SpellEffect ToSpellEffect()
    {
        Dictionary<string, int> modifiers = StatModifiers.ToDictionary(p => p.Key, p => p.Value);
        return new SpellEffect(SpellName, RemainingRounds, modifiers);
    }
}


[System.Serializable]
public class AttributePair
{
    public string Key;
    public int Value;
}

[System.Serializable]
public class InventoryData
{
    public List<WeaponData> AllWeapons = new List<WeaponData>(); //Wszystkie posiadane przez postać bronie
    public List<ArmorData> AllArmors = new List<ArmorData>(); //Wszystkie posiadane przez postać elementy zbroi
    public List<int> EquippedArmorsId = new List<int>(); //Wszystkie ubrane przez postać elementy zbroi
    public int[] EquippedWeaponsId = new int[2]; // Tablica identyfikatorów broni trzymanych w rękach
    public int CopperCoins;
    public int SilverCoins;
    public int GoldCoins;

    public InventoryData(Inventory inventory)
    {
        foreach (var weapon in inventory.AllWeapons)
        {
            WeaponData weaponData = new WeaponData(weapon);
            AllWeapons.Add(weaponData);
        }

        // Dodaj identyfikatory broni trzymanych w rękach do tablicy EquippedWeaponIds
        for (int i = 0; i < inventory.EquippedWeapons.Length; i++)
        {
            if (inventory.EquippedWeapons[i] != null)
            {
                EquippedWeaponsId[i] = inventory.EquippedWeapons[i].Id;
            }
        }

        foreach (var armor in inventory.EquippedArmors)
        {
            WeaponData armorData = new WeaponData(armor);
            EquippedArmorsId.Add(armorData.Id);
        }

        CopperCoins = inventory.CopperCoins;
        SilverCoins = inventory.SilverCoins;
        GoldCoins = inventory.GoldCoins;
    }
}

[System.Serializable]
public class GridManagerData
{
    public int Width;
    public int Height;
    public string GridColor;

    public GridManagerData()
    {
        Width = GridManager.Width;
        Height = GridManager.Height;
        GridColor = GridManager.GridColor;
    }
}

[System.Serializable]
public class RoundsManagerData
{
    public int RoundNumber;
    public RoundsManagerData()
    {
        RoundNumber = RoundsManager.RoundNumber;
    }
}

[System.Serializable]
public class MapElementsData
{
    public string Name;
    public string Tag;
    public bool IsHighObstacle;
    public bool IsLowObstacle;
    public bool IsCollider;
    public float[] position;
    public int rotationZ;

    public MapElementsData(MapElement mapElement)
    {
        Name = mapElement.gameObject.name.Replace("(Clone)", "");
        Tag = mapElement.gameObject.tag;
        IsHighObstacle = mapElement.IsHighObstacle;
        IsLowObstacle = mapElement.IsLowObstacle;
        IsCollider = mapElement.IsCollider;

        position = new float[3];
        position[0] = mapElement.gameObject.transform.position.x;
        position[1] = mapElement.gameObject.transform.position.y;
        position[2] = mapElement.gameObject.transform.position.z;
        rotationZ = (int)mapElement.gameObject.transform.eulerAngles.z;
    }
}

[System.Serializable]
public class TileCoverData
{
    public float[] Position; // Pozycja tile cover
    public int Number;

    public TileCoverData(Vector3 position, int number)
    {
        Position = new float[3];
        Position[0] = position.x;
        Position[1] = position.y;
        Position[2] = position.z;
        Number = number;
    }
}

[System.Serializable]
public class MapElementsContainer
{
    public List<MapElementsData> Elements = new List<MapElementsData>();
    public List<TileCoverData> TileCovers = new List<TileCoverData>();

    public string BackgroundImagePath;
    public float BackgroundPositionX;
    public float BackgroundPositionY;
    public float BackgroundScale;
    public float BackgroundColorR;
    public float BackgroundColorG;
    public float BackgroundColorB;
}

[System.Serializable]
public class GameSettings
{
    public bool IsAutoDiceRollingMode;
    public bool IsAutoDefenseMode;
    public bool IsAutoKillMode;
    public bool IsAutoSelectUnitMode;
    public bool IsFriendlyFire;
    public bool IsFearIncluded;
    public bool IsAutoCombatMode;
    public bool IsStatsHidingMode;
    public bool IsNamesHidingMode;
    public bool IsHealthPointsHidingMode;
    public bool IsAutosaveMode;
    public bool IsShowAnimationsMode;
    public float BackgroundColorR;
    public float BackgroundColorG;
    public float BackgroundColorB;
}

#endregion

