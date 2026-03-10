using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class GridManager : MonoBehaviour
{
    private static GridManager instance;
    public static GridManager Instance
    {
        get { return instance; }
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

    private List<GridTileData> _disabledTilesToLoad;
    private static List<GridTileData> _runtimeDisabledTilesForSceneChange;
    private static bool _shouldApplyRuntimeTopologyOnStart;

    void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
        else if (instance != this)
        {
            Destroy(gameObject);
        }
    }
    public static void CacheRuntimeTopologyForSceneChange()
    {
        if (Instance == null || Instance.Tiles == null)
        {
            _runtimeDisabledTilesForSceneChange = null;
            _shouldApplyRuntimeTopologyOnStart = false;
            return;
        }

        _runtimeDisabledTilesForSceneChange = Instance.GetDisabledTilesData();
        _shouldApplyRuntimeTopologyOnStart = true;
    }

    void Start()
    {
        GenerateGrid();
        CameraManager.ChangeCameraRange(Width, Height);

        if (SceneManager.GetActiveScene().buildIndex != 0 && MapEditor.Instance != null)
        {
            MapEditor.Instance.SetAllElementsColliders(false);
            MapEditor.Instance.MakeTileBlockersTransparent(true);
            MapEditor.IsElementRemoving = false;
        }
        else if (MapEditor.Instance != null)
        {
            MapEditor.Instance.SetAllElementsColliders(true);
            MapEditor.Instance.MakeTileBlockersTransparent(false);

            Color newColor = GridColor == "white" ? Color.white : Color.black;
            _gridColorbutton.GetComponent<Image>().color = newColor;
        }

        CheckTileOccupancy();
        UpdateGridColorButton();
    }

    public void GenerateGrid()
    {
        // Usuwa poprzednia siatke.
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

                if (x == Width - 1 && y == 0)
                {
                    spawnedTile = Instantiate(_tileCornerBottomRightPrefab, new Vector3(x, y, 1), Quaternion.identity);
                }
                else if (y == 0)
                {
                    spawnedTile = Instantiate(_tileBottomEdgePrefab, new Vector3(x, y, 1), Quaternion.identity);
                }
                else if (x == Width - 1)
                {
                    spawnedTile = Instantiate(_tileRightEdgePrefab, new Vector3(x, y, 1), Quaternion.identity);
                }
                else
                {
                    spawnedTile = Instantiate(_tileInnerPrefab, new Vector3(x, y, 1), Quaternion.identity);
                }

                spawnedTile.name = $"Tile {x} {y}";
                isOffset = (x % 2 == 0 && y % 2 != 0) || (x % 2 != 0 && y % 2 == 0);
                spawnedTile.Init(isOffset);
                Tiles[x, y] = spawnedTile;
                spawnedTile.transform.SetParent(transform, false);

                Color color = GridColor == "white" ? Color.white : Color.black;
                spawnedTile.GetComponent<Renderer>().material.color = color;
            }

            transform.position = new Vector3(-(Width / 2), -(Height / 2), 1);
        }

        if (_inputY != null && _inputX != null)
        {
            _sliderX.value = Width;
            _sliderY.value = Height;
            _inputX.text = Width.ToString();
            _inputY.text = Height.ToString();
        }

        if (_disabledTilesToLoad != null)
        {
            ApplyDisabledTilesData(_disabledTilesToLoad);
            _disabledTilesToLoad = null;
            _runtimeDisabledTilesForSceneChange = null;
            _shouldApplyRuntimeTopologyOnStart = false;
        }
        else if (_shouldApplyRuntimeTopologyOnStart && _runtimeDisabledTilesForSceneChange != null)
        {
            ApplyDisabledTilesData(_runtimeDisabledTilesForSceneChange);
            _runtimeDisabledTilesForSceneChange = null;
            _shouldApplyRuntimeTopologyOnStart = false;
        }
    }

    public void SetTileState(int x, int y, bool isActive)
    {
        if (Tiles == null || x < 0 || y < 0 || x >= Width || y >= Height) return;

        Tile tile = Tiles[x, y];
        if (tile == null) return;

        tile.gameObject.SetActive(isActive);

        if (!isActive)
        {
            tile.IsOccupied = true;
        }
    }

    public void SetAllTilesState(bool isActive)
    {
        if (Tiles == null) return;

        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                SetTileState(x, y, isActive);
            }
        }

        UpdateTileEdgePrefabsForCurrentTopology();

        if (isActive)
        {
            CheckTileOccupancy();
        }
    }

    public void ApplyWalkableMask(bool[,] walkableMask)
    {
        if (walkableMask == null || walkableMask.GetLength(0) != Width || walkableMask.GetLength(1) != Height)
        {
            Debug.LogError("Nie mozna zastosowac maski dungeonu. Niepoprawny rozmiar maski.");
            return;
        }

        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                SetTileState(x, y, walkableMask[x, y]);
            }
        }

        UpdateTileEdgePrefabsForCurrentTopology();
        CheckTileOccupancy();
    }

    public List<GridTileData> GetDisabledTilesData()
    {
        List<GridTileData> disabledTiles = new List<GridTileData>();

        if (Tiles == null) return disabledTiles;

        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                Tile tile = Tiles[x, y];
                if (tile != null && !tile.gameObject.activeSelf)
                {
                    disabledTiles.Add(new GridTileData(x, y));
                }
            }
        }

        return disabledTiles;
    }

    private void ApplyDisabledTilesData(List<GridTileData> disabledTiles)
    {
        SetAllTilesState(true);

        if (disabledTiles == null) return;

        foreach (GridTileData tileData in disabledTiles)
        {
            if (tileData == null) continue;
            SetTileState(tileData.X, tileData.Y, false);
        }

        UpdateTileEdgePrefabsForCurrentTopology();
        CheckTileOccupancy();
    }

    private bool IsTileActiveAt(int x, int y)
    {
        if (Tiles == null || x < 0 || y < 0 || x >= Width || y >= Height) return false;

        Tile tile = Tiles[x, y];
        return tile != null && tile.gameObject.activeInHierarchy;
    }

    private void UpdateTileEdgePrefabsForCurrentTopology()
    {
        if (Tiles == null) return;

        Sprite innerSprite = _tileInnerPrefab != null ? _tileInnerPrefab.GetComponent<SpriteRenderer>().sprite : null;
        Sprite rightEdgeSprite = _tileRightEdgePrefab != null ? _tileRightEdgePrefab.GetComponent<SpriteRenderer>().sprite : null;
        Sprite bottomEdgeSprite = _tileBottomEdgePrefab != null ? _tileBottomEdgePrefab.GetComponent<SpriteRenderer>().sprite : null;
        Sprite cornerSprite = _tileCornerBottomRightPrefab != null ? _tileCornerBottomRightPrefab.GetComponent<SpriteRenderer>().sprite : null;

        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                if (!IsTileActiveAt(x, y)) continue;

                bool hasRightNeighbor = IsTileActiveAt(x + 1, y);
                bool hasBottomNeighbor = IsTileActiveAt(x, y - 1);

                Sprite targetSprite = innerSprite;

                if (!hasRightNeighbor && !hasBottomNeighbor)
                {
                    targetSprite = cornerSprite;
                }
                else if (!hasBottomNeighbor)
                {
                    targetSprite = bottomEdgeSprite;
                }
                else if (!hasRightNeighbor)
                {
                    targetSprite = rightEdgeSprite;
                }

                if (targetSprite == null) continue;

                SpriteRenderer tileRenderer = Tiles[x, y].GetComponent<SpriteRenderer>();
                if (tileRenderer != null)
                {
                    tileRenderer.sprite = targetSprite;
                }
            }
        }
    }

    public void ChangeGridSize(bool isInputField)
    {
        if (isInputField)
        {
            if (int.TryParse(_inputX.text, out int parsedWidth))
            {
                Width = Mathf.Clamp(parsedWidth, 1, 70);
            }
            else
            {
                Width = (int)_sliderX.value;
            }

            if (int.TryParse(_inputY.text, out int parsedHeight))
            {
                Height = Mathf.Clamp(parsedHeight, 1, 70);
            }
            else
            {
                Height = (int)_sliderY.value;
            }

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

        GenerateGrid();
        StartCoroutine(RemoveElementsOutsideTheGrid());
        CameraManager.ChangeCameraRange(Width, Height);
    }

    public void ChangeGridColor()
    {
        GridColor = GridColor == "white" ? "black" : "white";

        Color newColor = GridColor == "white" ? Color.white : Color.black;
        _gridColorbutton.GetComponent<Image>().color = newColor;

        foreach (Tile tile in Tiles)
        {
            if (tile == null) continue;
            tile.GetComponent<Renderer>().material.color = newColor;
        }
    }

    IEnumerator RemoveElementsOutsideTheGrid()
    {
        yield return new WaitForSeconds(0.02f);

        if (MapEditor.Instance != null)
        {
            MapEditor.Instance.RemoveElementsOutsideTheGrid();
        }
    }

    public void HighlightTilesInMovementRange(Stats unitStats)
    {
        ResetColorOfTilesInMovementRange();

        if ((GameManager.IsAutoCombatMode && !(unitStats.CompareTag("PlayerUnit") && GameManager.IsStatsHidingMode)) || Unit.SelectedUnit == null || (!Unit.SelectedUnit.GetComponent<Unit>().CanMove && !Unit.SelectedUnit.GetComponent<Unit>().IsRunning))
        {
            return;
        }

        int movementRange = unitStats.TempSz;
        if (movementRange == 0) return;

        bool isFlyingUnit = Unit.SelectedUnit != null && Unit.SelectedUnit.GetComponent<Unit>().IsFlying;

        HashSet<GameObject> objectsInMovementRange = new HashSet<GameObject>
        {
            unitStats.gameObject
        };

        Queue<GameObject> tilesToProcess = new Queue<GameObject>();
        tilesToProcess.Enqueue(unitStats.gameObject);

        Vector2[] directions = { Vector2.right, Vector2.left, Vector2.up, Vector2.down };

        for (int step = 0; step < movementRange; step++)
        {
            int currentQueueSize = tilesToProcess.Count;

            for (int i = 0; i < currentQueueSize; i++)
            {
                GameObject currentTile = tilesToProcess.Dequeue();

                foreach (Vector2 direction in directions)
                {
                    Vector2 targetPosition = (Vector2)currentTile.transform.position + direction;
                    Collider2D collider = Physics2D.OverlapPoint(targetPosition);

                    if (collider != null && (collider.gameObject.CompareTag("Tile") || isFlyingUnit))
                    {
                        GameObject neighborTile = collider.gameObject;

                        if (objectsInMovementRange.Add(neighborTile))
                        {
                            tilesToProcess.Enqueue(neighborTile);
                        }
                    }
                }
            }
        }

        foreach (var tile in objectsInMovementRange)
        {
            if (tile != null && tile.CompareTag("Tile"))
            {
                tile.GetComponent<Tile>().SetRangeColor();
            }
        }
    }

    public void ResetColorOfTilesInMovementRange()
    {
        foreach (Tile tile in Tiles)
        {
            if (tile != null)
            {
                tile.GetComponent<Tile>().ResetRangeColor();
            }
        }
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
        if (Tiles == null) return;

        foreach (Tile tile in Tiles)
        {
            if (tile == null) continue;

            if (!tile.gameObject.activeInHierarchy)
            {
                tile.IsOccupied = true;
                continue;
            }

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

        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                Tile tile = Instance.Tiles[x, y];
                if (tile != null && tile.gameObject.activeInHierarchy && !tile.IsOccupied)
                {
                    availablePositions.Add(tile.transform.position);
                }
            }
        }

        return availablePositions;
    }

    public void ResetTileOccupancy(Vector2 unitPosition)
    {
        foreach (Tile tile in Tiles)
        {
            if (tile == null || !tile.gameObject.activeInHierarchy) continue;

            if (unitPosition == (Vector2)tile.transform.position)
            {
                tile.IsOccupied = false;
            }
        }
    }

    public void LoadGridManagerData(GridManagerData data)
    {
        Width = data.Width;
        Height = data.Height;
        GridColor = data.GridColor;
        _disabledTilesToLoad = data.DisabledTiles != null ? new List<GridTileData>(data.DisabledTiles) : null;

        if (MapEditor.Instance != null)
        {
            UpdateGridColorButton();
        }

        CameraManager.ChangeCameraRange(Width, Height);
    }

    public void UpdateGridColorButton()
    {
        Color newColor = GridColor == "white" ? Color.white : Color.black;
        _gridColorbutton.GetComponent<Image>().color = newColor;
    }

    #region Uncovering map and removing MapEditor (this methods are useful only in BattleScene)
    public void UncoverAll()
    {
        if (MapEditor.Instance == null) return;

        MapEditor.Instance.UncoverAll();
    }

    public void DestroyMapEditor()
    {
        if (MapEditor.Instance == null) return;

        Destroy(MapEditor.Instance.gameObject);
    }
    #endregion
}
