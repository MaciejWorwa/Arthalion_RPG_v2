using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using System.Linq;

public class GridManager : MonoBehaviour
{
    // Prywatne statyczne pole przechowujące instancję
    private static GridManager instance;

    // Publiczny dostęp do instancji
    public static GridManager Instance
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

    [SerializeField] private Tile _tileInnerPrefab;
    [SerializeField] private Tile _tileRightEdgePrefab;
    [SerializeField] private Tile _tileBottomEdgePrefab;
    [SerializeField] private Tile _tileCornerBottomRightPrefab;

    public Tile[,] Tiles;
    public static int Width = 22;
    public static int Height = 16;
    public static string GridColor = "white";

    [SerializeField] private TMP_InputField _inputX;
    [SerializeField] private TMP_InputField _inputY;
    [SerializeField] private Slider _sliderX;
    [SerializeField] private Slider _sliderY;
    [SerializeField] private Button _gridColorbutton;

    void Start()
    {
        GenerateGrid();
        CameraManager.ChangeCameraRange(Width, Height);

        if(SceneManager.GetActiveScene().buildIndex != 0 && MapEditor.Instance != null)
        {
            MapEditor.Instance.SetAllElementsColliders(false);
            MapEditor.Instance.MakeTileBlockersTransparent(true);
            MapEditor.IsElementRemoving = false;
        }
        else if (MapEditor.Instance != null)
        {
            MapEditor.Instance.SetAllElementsColliders(true);
            MapEditor.Instance.MakeTileBlockersTransparent(false);

            //Zaktualizowanie koloru przycisku odpowiadającemu za zmianę koloru siatki
            Color newColor = GridColor == "white" ? Color.white : Color.black;
            _gridColorbutton.GetComponent<Image>().color = newColor;
        }

        CheckTileOccupancy();
        UpdateGridColorButton();
    }

    public void GenerateGrid()
    {
        //Usuwa poprzednią siatkę
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            Destroy(child);
        }

        Tiles = new Tile[Width, Height];
        bool isOffset;
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                Tile spawnedTile;

                // Ustal, jaki prefabrykat wybrać na podstawie pozycji na siatce
                if (x == Width - 1 && y == 0)
                    spawnedTile = Instantiate(_tileCornerBottomRightPrefab, new Vector3(x, y, 1), Quaternion.identity);
                else if (y == 0)
                    spawnedTile = Instantiate(_tileBottomEdgePrefab, new Vector3(x, y, 1), Quaternion.identity);
                else if (x == Width - 1)
                    spawnedTile = Instantiate(_tileRightEdgePrefab, new Vector3(x, y, 1), Quaternion.identity);
                else
                    spawnedTile = Instantiate(_tileInnerPrefab, new Vector3(x, y, 1), Quaternion.identity);

                spawnedTile.name = $"Tile {x} {y}";
                isOffset = (x % 2 == 0 && y % 2 != 0) || (x % 2 != 0 && y % 2 == 0);
                spawnedTile.Init(isOffset);
                Tiles[x, y] = spawnedTile;
                spawnedTile.transform.SetParent(this.transform, false);

                // Przypisanie odpowiedniego koloru na podstawie wartości GridColor
                Color color = GridColor == "white" ? Color.white : Color.black;
                spawnedTile.GetComponent<Renderer>().material.color = color;
            }

            // Przesunięcie rodzica do centrum generowanej siatki
            transform.position = new Vector3(-(Width / 2), -(Height / 2), 1);
        }

        if (_inputY != null && _inputX != null)
        {
            int width = Width;
            int height = Height;
            _sliderX.value = width;
            _sliderY.value = height;
            _inputX.text = Width.ToString();
            _inputY.text = Height.ToString();
        }
    }

    public void ChangeGridSize(bool isInputField)
    {
        if (isInputField)
        {
            // Parsowanie wartości z InputField i obsługa błędów
            if (int.TryParse(_inputX.text, out int parsedWidth))
                Width = Mathf.Clamp(parsedWidth, 1, 70); // Ograniczenie do 1-70
            else
                Width = (int)_sliderX.value;

            if (int.TryParse(_inputY.text, out int parsedHeight))
                Height = Mathf.Clamp(parsedHeight, 1, 70);
            else
                Height = (int)_sliderY.value;

            _sliderX.value = Width;
            _sliderY.value = Height;
        }
        else
        {
            Width = (int)_sliderX.value;
            Height = (int)_sliderY.value;

            _inputX.text = Mathf.Clamp(Width, 1, 70).ToString();
            _inputY.text = Mathf.Clamp(Height, 1, 70).ToString();
        }

        // Generowanie nowej siatki i aktualizacja kamery
        GenerateGrid();
        StartCoroutine(RemoveElementsOutsideTheGrid());
        CameraManager.ChangeCameraRange(Width, Height);
    }


    public void ChangeGridColor()
    {
        GridColor = GridColor == "white" ? "black" : "white";

        // Przypisanie odpowiedniego koloru na podstawie wartości GridColor
        Color newColor = GridColor == "white" ? Color.white : Color.black;

        //Zaktualizowanie koloru przycisku odpowiadającemu za zmianę koloru siatki
        _gridColorbutton.GetComponent<Image>().color = newColor;

        foreach (Tile tile in Tiles)
        {
            tile.GetComponent<Renderer>().material.color = newColor;
        }
    }

    IEnumerator RemoveElementsOutsideTheGrid()
    {
        //Opóźnienie, żeby wartości Sliderów zdążyły się zaktualizować w przypadku uruchamiania MapEditor z poziomu BattleScene. Inaczej elementy są usuwane nim sliderY zaktualizuje swoją wartość.
        yield return new WaitForSeconds(0.02f);

        // Usuwa przeszkody poza obszarem siatki
        MapEditor.Instance.RemoveElementsOutsideTheGrid();
    }

    public void HighlightTilesInMovementRange(Stats unitStats)
    {
        ResetColorOfTilesInMovementRange();

        if((GameManager.IsAutoCombatMode && !(unitStats.CompareTag("PlayerUnit") && GameManager.IsStatsHidingMode)) || Unit.SelectedUnit == null || (!Unit.SelectedUnit.GetComponent<Unit>().CanMove && !Unit.SelectedUnit.GetComponent<Unit>().IsRunning)) return;

        // Sprawdzenie zasięgu ruchu
        int movementRange = unitStats.TempSz;
        if (movementRange == 0) return;

        // Zestaw do przechowywania pól w zasięgu ruchu (unikamy duplikatów)
        HashSet<GameObject> objectsInMovementRange = new HashSet<GameObject>();

        // Dodaje pole startowe
        objectsInMovementRange.Add(unitStats.gameObject);

        // Lista do przeszukiwania kolejnych warstw
        Queue<GameObject> tilesToProcess = new Queue<GameObject>();
        tilesToProcess.Enqueue(unitStats.gameObject);

        // Wektory w prawo, lewo, góra, dół
        Vector2[] directions = { Vector2.right, Vector2.left, Vector2.up, Vector2.down };

        // Algorytm BFS (Breadth-First Search) do przeszukiwania pól w zasięgu
        for (int step = 0; step < movementRange; step++)
        {
            int currentQueueSize = tilesToProcess.Count;

            // Przetwarza wszystkie pola z bieżącego poziomu BFS
            for (int i = 0; i < currentQueueSize; i++)
            {
                GameObject currentTile = tilesToProcess.Dequeue();

                foreach (Vector2 direction in directions)
                {
                    // Znajduje sąsiadujące pole
                    Vector2 targetPosition = (Vector2)currentTile.transform.position + direction;
                    Collider2D collider = Physics2D.OverlapPoint(targetPosition);

                    if (collider != null && collider.gameObject.CompareTag("Tile"))
                    {
                        GameObject neighborTile = collider.gameObject;

                        // Dodaje pole do zestawu, jeśli jeszcze nie został odwiedzony
                        if (objectsInMovementRange.Add(neighborTile))
                        {
                            tilesToProcess.Enqueue(neighborTile);
                        }
                    }
                }
            }
        }

        // Wyróżnia wszystkie pola w zasięgu ruchu
        foreach (var tile in objectsInMovementRange)
        {
            if (tile.CompareTag("Tile")) // Dodatkowe sprawdzenie, aby uniknąć błędów
            {
                tile.GetComponent<Tile>().SetRangeColor();
            }
        }
    }


    public void ResetColorOfTilesInMovementRange()
    {
        // Resetuje wszystkie pola
        foreach (Tile tile in Tiles)
            tile.GetComponent<Tile>().ResetRangeColor();
    }

    public void HighlightTilesInSpellArea(GameObject tileUnderCursor)
    {
        ResetColorOfTilesInMovementRange();
        Spell spell = Unit.SelectedUnit.GetComponent<Spell>();

        int areaSize = spell.AreaSize;
        if (MagicManager.Instance.CriticalCastingString == "area_size") areaSize *= 2;

        Collider2D[] allColliders = Physics2D.OverlapCircleAll(tileUnderCursor.transform.position, areaSize);
        foreach (var collider in allColliders)
        {
            if (collider != null && collider.gameObject.CompareTag("Tile"))
            {
                collider.GetComponent<Tile>().SetRangeColor();
            }
        }
    }

    public void CheckTileOccupancy()
    {
        foreach (Tile tile in Tiles)
        {
            Vector2 tilePosition = new Vector2(tile.transform.position.x, tile.transform.position.y);
            Collider2D hitCollider = Physics2D.OverlapCircle(tilePosition, 0.1f);

            if (hitCollider != null && !hitCollider.CompareTag("Tile") && !hitCollider.CompareTag("TileCover"))
            {
                tile.IsOccupied = true;
            }
            else
            {
                tile.IsOccupied = false;
            }
        }
    }

    public List<Vector2> AvailablePositions()
    {
        List<Vector2> availablePositions = new List<Vector2>();

        // Przejście przez wszystkie Tile w tablicy Tiles
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                // Sprawdzenie, czy Tile nie jest zajęty
                if (!Instance.Tiles[x, y].IsOccupied)
                {
                    // Dodanie pozycji Tile do listy dostępnych pozycji
                    availablePositions.Add(Tiles[x, y].transform.position);
                }
            }
        }

        return availablePositions;
    }

    public void ResetTileOccupancy(Vector2 unitPosition)
    {
        foreach (Tile tile in Tiles)
        {
            if(unitPosition == (Vector2)tile.transform.position)
                tile.IsOccupied = false;
        }
    }

    public void LoadGridManagerData(GridManagerData data)
    {
        Width = data.Width;
        Height = data.Height;
        GridColor = data.GridColor;

        //Zaktualizowanie koloru przycisku odpowiadającemu za zmianę koloru siatki
        if(MapEditor.Instance != null)
        {
            UpdateGridColorButton();
        }

        CameraManager.ChangeCameraRange(Width, Height);
    }

    //Zaktualizowanie koloru przycisku odpowiadającemu za zmianę koloru siatki
    public void UpdateGridColorButton()
    {
        Color newColor = GridColor == "white" ? Color.white : Color.black;
        _gridColorbutton.GetComponent<Image>().color = newColor;
    }

    #region Uncovering map and removing MapEditor (this methods are useful only in BattleScene)
    public void UncoverAll()
    {
        if(MapEditor.Instance == null) return;

        MapEditor.Instance.UncoverAll();
    }

    public void DestroyMapEditor()
    {
        if(MapEditor.Instance == null) return;

        Destroy(MapEditor.Instance.gameObject);
    }

    #endregion
}