using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

public class DungeonGenerator : MonoBehaviour
{
    private enum RoomShape
    {
        Rectangle,
        Ellipse,
        Diamond
    }

    private struct RoomData
    {
        public RectInt Rect;
        public RoomShape Shape;
        public Vector2Int Center;
    }

    private class EnemyTemplate
    {
        public int UnitId;
        public string Race = string.Empty;
        public int EstimatedOverall;
        public int Size;
    }

    private class EnemySpawnCandidate
    {
        public EnemyTemplate Template;
        public int Count;
        public int PredictedOverall;
        public int Diff;
        public int Score;
        public bool InTolerance;
    }

    [Serializable]
    private class EncounterCatalog
    {
        public EncounterGroupData[] Encounters = Array.Empty<EncounterGroupData>();
    }

    [Serializable]
    private class EncounterGroupData
    {
        public string Name = string.Empty;
        public int Weight = 1;
        public int MinUnits = 1;
        public int MaxUnits = 6;
        public EncounterMemberData[] Members = Array.Empty<EncounterMemberData>();
    }

    [Serializable]
    private class EncounterMemberData
    {
        public string Race = string.Empty;
        public int Weight = 1;
        public int MaxPerGroup = 0;
        public bool CanAppearOnFoot = true;
        public float MountedChance = 0f;
        public EncounterMountData[] Mounts = Array.Empty<EncounterMountData>();
    }

    [Serializable]
    private class EncounterMountData
    {
        public string Race = string.Empty;
        public int Weight = 1;
    }

    private class EncounterGroupRuntime
    {
        public string Name = string.Empty;
        public int Weight;
        public int MinUnits;
        public int MaxUnits;
        public List<EncounterMemberRuntime> Members = new List<EncounterMemberRuntime>();
    }

    private class EncounterMemberRuntime
    {
        public EnemyTemplate Template;
        public int Weight;
        public int MaxPerGroup;
        public bool CanAppearOnFoot;
        public float MountedChance;
        public List<EnemyTemplate> MountTemplates = new List<EnemyTemplate>();
        public List<int> MountWeights = new List<int>();
    }

    private class PlannedEnemySpawn
    {
        public EnemyTemplate RiderTemplate;
        public EnemyTemplate MountTemplate;
        public bool IsMounted;
        public int PredictedOverall;
    }

    private class EncounterSpawnCandidate
    {
        public EncounterGroupRuntime Group;
        public List<PlannedEnemySpawn> Spawns = new List<PlannedEnemySpawn>();
        public int PredictedOverall;
        public int Diff;
        public int Score;
        public bool InTolerance;
        public int TileUsage;
    }

    [Header("Rooms")]
    [SerializeField, Min(2)] private int _minRooms = 6;
    [SerializeField, Min(2)] private int _maxRooms = 12;
    [SerializeField, Min(3)] private int _roomMinSize = 4;
    [SerializeField, Min(3)] private int _roomMaxSize = 10;
    [SerializeField, Min(0)] private int _roomPadding = 1;
    [SerializeField, Min(1)] private int _placementAttemptsPerRoom = 12;

    [Header("Corridors")]
    [SerializeField, Min(1)] private int _corridorWidth = 1;
    [SerializeField, Range(0f, 1f)] private float _extraConnectionChance = 0.15f;

    [Header("Room shape weights")]
    [SerializeField, Min(0)] private int _rectangleWeight = 60;
    [SerializeField, Min(0)] private int _ellipseWeight = 25;
    [SerializeField, Min(0)] private int _diamondWeight = 15;

    [Header("Seed")]
    [SerializeField] private bool _useRandomSeed = true;
    [SerializeField] private int _seed = 12345;
    [SerializeField] private TMP_InputField _seedInputField;

    [Header("Safety")]
    [SerializeField] private bool _removeUnitsOutsideDungeon = true;
    [SerializeField] private bool _removeMapElementsOutsideDungeon = true;


    [Header("Enemy generation")]
    [SerializeField] private bool _generateEnemiesByPlayerStrength = true;
    [SerializeField] private bool _clearExistingEnemiesBeforeSpawn = true;
    [SerializeField, Range(0.5f, 2f)] private float _enemyOverallMultiplier = 1f;
    [SerializeField, Range(0f, 0.5f)] private float _overallTolerance = 0.15f;
    [SerializeField, Min(1)] private int _minGeneratedEnemies = 1;
    [SerializeField, Min(1)] private int _maxGeneratedEnemies = 6;
    [SerializeField] private bool _includePlayableRacesAsEnemies = false;
    [SerializeField, Range(1, 8)] private int _enemySelectionTopCandidates = 4;
    [SerializeField] private bool _useEncounterGroups = true;
    [SerializeField] private string _encountersResourceName = "encounters";
    [SerializeField, Range(0f, 1f)] private float _mountedOverallContribution = 0.35f;
    [SerializeField, Min(1)] private int _encounterBuildAttemptsPerGroup = 24;
    [SerializeField] private List<string> _disallowedStandaloneEnemyRaces = new List<string> { "Koń", "Kuc" };
    private int _lastGeneratedEnemyUnitId = -1;
    private string _lastGeneratedEncounterName = string.Empty;

    // Prywatne statyczne pole przechowujące instancję
    private static DungeonGenerator instance;

    // Publiczny dostęp do instancji
    public static DungeonGenerator Instance
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

    public void GenerateDungeonFromButton()
    {
        if (_seedInputField != null && int.TryParse(_seedInputField.text, out int parsedSeed))
        {
            _seed = parsedSeed;
            _useRandomSeed = false;
        }

        GenerateDungeon();
    }

    public void GenerateDungeon()
    {
        bool suppressDungeonCrawlerRewards = GameManager.Instance != null && GameManager.IsDungeonCrawlerMode;
        if (suppressDungeonCrawlerRewards)
        {
            GameManager.Instance.SetDungeonCrawlerRewardSuppression(true);
            GameManager.Instance.ResetDungeonCrawlerRewardProgress();
        }

        try
        {
            if (GridManager.Instance == null || GridManager.Instance.Tiles == null)
            {
                Debug.LogError("Brak GridManagera lub siatka nie jest jeszcze gotowa.");
                return;
            }

            int width = GridManager.Width;
            int height = GridManager.Height;

            if (width < 8 || height < 8)
            {
                Debug.LogWarning("Siatka jest zbyt mała do sensownego generowania dungeonu. Minimalnie zalecane: 8x8.");
                return;
            }

            NormalizeSettings(width, height);

            int seedToUse = _useRandomSeed ? UnityEngine.Random.Range(int.MinValue, int.MaxValue) : _seed;
            _seed = seedToUse;

            if (_seedInputField != null)
            {
                _seedInputField.text = seedToUse.ToString();
            }

            System.Random rng = new System.Random(seedToUse);
            bool[,] walkableMask = new bool[width, height];

            List<RoomData> rooms = CreateRooms(rng, walkableMask, width, height);

            if (rooms.Count == 0)
            {
                Debug.LogWarning("Generator nie utworzył żadnego pokoju. Przywracam pełną siatkę.");
                RestoreFullGrid();
                return;
            }

            ConnectRoomsWithMst(rng, rooms, walkableMask, width, height);
            AddExtraConnections(rng, rooms, walkableMask, width, height);
            EnsureRoomCentersAreWalkable(rooms, walkableMask, width, height);

            GridManager.Instance.ApplyWalkableMask(walkableMask);
            RemoveMapElementsOutsideDungeonIfNeeded();
            RemoveUnitsOutsideDungeonIfNeeded();
            GenerateEnemiesByPlayerStrength(rng);

            if (SceneManager.GetActiveScene().buildIndex == 1)
            {
                UnitsManager.Instance.UpdateUnitPanel(null);
            }

            Debug.Log($"<color=green>Wygenerowano losową mapę. Seed: {seedToUse}, liczba pomieszczeń: {rooms.Count}.</color>");
        }
        finally
        {
            if (suppressDungeonCrawlerRewards && GameManager.Instance != null)
            {
                GameManager.Instance.SetDungeonCrawlerRewardSuppression(false);
            }
        }
    }

    public void RestoreFullGrid()
    {
        if (GridManager.Instance == null || GridManager.Instance.Tiles == null) return;

        GridManager.Instance.SetAllTilesState(true);
        GridManager.Instance.CheckTileOccupancy();

        Debug.Log("Przywrócono pełną siatkę.");
    }

    private void NormalizeSettings(int width, int height)
    {
        _maxRooms = Mathf.Max(_maxRooms, _minRooms);
        _roomMaxSize = Mathf.Max(_roomMaxSize, _roomMinSize);

        int maxAllowedRoomSize = Mathf.Max(3, Mathf.Min(width, height) - 2);
        _roomMinSize = Mathf.Clamp(_roomMinSize, 3, maxAllowedRoomSize);
        _roomMaxSize = Mathf.Clamp(_roomMaxSize, _roomMinSize, maxAllowedRoomSize);

        _corridorWidth = Mathf.Max(1, _corridorWidth);
        _placementAttemptsPerRoom = Mathf.Max(1, _placementAttemptsPerRoom);
    }

    private List<RoomData> CreateRooms(System.Random rng, bool[,] walkableMask, int width, int height)
    {
        List<RoomData> rooms = new List<RoomData>();

        int targetRooms = rng.Next(_minRooms, _maxRooms + 1);
        int maxAttempts = targetRooms * _placementAttemptsPerRoom;

        for (int i = 0; i < maxAttempts && rooms.Count < targetRooms; i++)
        {
            int roomWidth = rng.Next(_roomMinSize, _roomMaxSize + 1);
            int roomHeight = rng.Next(_roomMinSize, _roomMaxSize + 1);

            if (roomWidth >= width - 2 || roomHeight >= height - 2)
            {
                continue;
            }

            int roomX = rng.Next(1, width - roomWidth - 1);
            int roomY = rng.Next(1, height - roomHeight - 1);

            RectInt candidate = new RectInt(roomX, roomY, roomWidth, roomHeight);

            if (OverlapsWithPadding(candidate, rooms))
            {
                continue;
            }

            RoomShape shape = PickRoomShape(rng);
            CarveRoom(candidate, shape, walkableMask, width, height);

            Vector2Int center = new Vector2Int(candidate.xMin + candidate.width / 2, candidate.yMin + candidate.height / 2);
            SetWalkable(center.x, center.y, walkableMask, width, height);

            rooms.Add(new RoomData
            {
                Rect = candidate,
                Shape = shape,
                Center = center
            });
        }

        if (rooms.Count == 0)
        {
            int roomWidth = Mathf.Clamp(width / 3, 3, width - 2);
            int roomHeight = Mathf.Clamp(height / 3, 3, height - 2);
            int roomX = Mathf.Max(1, (width - roomWidth) / 2);
            int roomY = Mathf.Max(1, (height - roomHeight) / 2);

            RectInt fallbackRoom = new RectInt(roomX, roomY, roomWidth, roomHeight);
            CarveRoom(fallbackRoom, RoomShape.Rectangle, walkableMask, width, height);

            Vector2Int center = new Vector2Int(fallbackRoom.xMin + fallbackRoom.width / 2, fallbackRoom.yMin + fallbackRoom.height / 2);
            SetWalkable(center.x, center.y, walkableMask, width, height);

            rooms.Add(new RoomData
            {
                Rect = fallbackRoom,
                Shape = RoomShape.Rectangle,
                Center = center
            });
        }

        return rooms;
    }

    private bool OverlapsWithPadding(RectInt candidate, List<RoomData> rooms)
    {
        RectInt padded = new RectInt(
            candidate.xMin - _roomPadding,
            candidate.yMin - _roomPadding,
            candidate.width + _roomPadding * 2,
            candidate.height + _roomPadding * 2
        );

        foreach (RoomData room in rooms)
        {
            if (RectsOverlap(padded, room.Rect))
            {
                return true;
            }
        }

        return false;
    }

    private static bool RectsOverlap(RectInt a, RectInt b)
    {
        return a.xMin < b.xMax && a.xMax > b.xMin && a.yMin < b.yMax && a.yMax > b.yMin;
    }

    private RoomShape PickRoomShape(System.Random rng)
    {
        int totalWeight = _rectangleWeight + _ellipseWeight + _diamondWeight;
        if (totalWeight <= 0)
        {
            return RoomShape.Rectangle;
        }

        int roll = rng.Next(0, totalWeight);

        if (roll < _rectangleWeight)
        {
            return RoomShape.Rectangle;
        }

        roll -= _rectangleWeight;

        if (roll < _ellipseWeight)
        {
            return RoomShape.Ellipse;
        }

        return RoomShape.Diamond;
    }

    private void CarveRoom(RectInt roomRect, RoomShape shape, bool[,] walkableMask, int width, int height)
    {
        switch (shape)
        {
            case RoomShape.Rectangle:
                for (int x = roomRect.xMin; x < roomRect.xMax; x++)
                {
                    for (int y = roomRect.yMin; y < roomRect.yMax; y++)
                    {
                        SetWalkable(x, y, walkableMask, width, height);
                    }
                }
                break;

            case RoomShape.Ellipse:
                float ellipseCenterX = roomRect.xMin + roomRect.width / 2f;
                float ellipseCenterY = roomRect.yMin + roomRect.height / 2f;
                float radiusX = Mathf.Max(1f, roomRect.width / 2f);
                float radiusY = Mathf.Max(1f, roomRect.height / 2f);

                for (int x = roomRect.xMin; x < roomRect.xMax; x++)
                {
                    for (int y = roomRect.yMin; y < roomRect.yMax; y++)
                    {
                        float dx = ((x + 0.5f) - ellipseCenterX) / radiusX;
                        float dy = ((y + 0.5f) - ellipseCenterY) / radiusY;

                        if (dx * dx + dy * dy <= 1f)
                        {
                            SetWalkable(x, y, walkableMask, width, height);
                        }
                    }
                }
                break;

            case RoomShape.Diamond:
                float diamondCenterX = roomRect.xMin + roomRect.width / 2f;
                float diamondCenterY = roomRect.yMin + roomRect.height / 2f;
                float halfWidth = Mathf.Max(1f, roomRect.width / 2f);
                float halfHeight = Mathf.Max(1f, roomRect.height / 2f);

                for (int x = roomRect.xMin; x < roomRect.xMax; x++)
                {
                    for (int y = roomRect.yMin; y < roomRect.yMax; y++)
                    {
                        float dx = Mathf.Abs((x + 0.5f) - diamondCenterX) / halfWidth;
                        float dy = Mathf.Abs((y + 0.5f) - diamondCenterY) / halfHeight;

                        if (dx + dy <= 1f)
                        {
                            SetWalkable(x, y, walkableMask, width, height);
                        }
                    }
                }
                break;
        }
    }

    private void ConnectRoomsWithMst(System.Random rng, List<RoomData> rooms, bool[,] walkableMask, int width, int height)
    {
        if (rooms.Count <= 1) return;

        HashSet<int> connected = new HashSet<int>();
        HashSet<int> pending = new HashSet<int>();

        int startIndex = rng.Next(0, rooms.Count);
        connected.Add(startIndex);

        for (int i = 0; i < rooms.Count; i++)
        {
            if (i != startIndex)
            {
                pending.Add(i);
            }
        }

        while (pending.Count > 0)
        {
            int bestFrom = -1;
            int bestTo = -1;
            int bestDistance = int.MaxValue;

            foreach (int from in connected)
            {
                foreach (int to in pending)
                {
                    int distance = ManhattanDistance(rooms[from].Center, rooms[to].Center);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestFrom = from;
                        bestTo = to;
                    }
                }
            }

            if (bestFrom == -1 || bestTo == -1)
            {
                break;
            }

            CarveCorridor(rooms[bestFrom].Center, rooms[bestTo].Center, walkableMask, width, height, rng);
            connected.Add(bestTo);
            pending.Remove(bestTo);
        }
    }

    private void AddExtraConnections(System.Random rng, List<RoomData> rooms, bool[,] walkableMask, int width, int height)
    {
        if (_extraConnectionChance <= 0f || rooms.Count < 3) return;

        for (int i = 0; i < rooms.Count; i++)
        {
            for (int j = i + 1; j < rooms.Count; j++)
            {
                if (rng.NextDouble() <= _extraConnectionChance)
                {
                    CarveCorridor(rooms[i].Center, rooms[j].Center, walkableMask, width, height, rng);
                }
            }
        }
    }

    private void CarveCorridor(Vector2Int from, Vector2Int to, bool[,] walkableMask, int width, int height, System.Random rng)
    {
        bool horizontalFirst = rng.NextDouble() < 0.5;
        Vector2Int pivot = horizontalFirst ? new Vector2Int(to.x, from.y) : new Vector2Int(from.x, to.y);

        CarveLine(from, pivot, walkableMask, width, height);
        CarveLine(pivot, to, walkableMask, width, height);
    }

    private void CarveLine(Vector2Int from, Vector2Int to, bool[,] walkableMask, int width, int height)
    {
        int x = from.x;
        int y = from.y;

        CarveBrush(x, y, walkableMask, width, height);

        while (x != to.x)
        {
            x += Math.Sign(to.x - x);
            CarveBrush(x, y, walkableMask, width, height);
        }

        while (y != to.y)
        {
            y += Math.Sign(to.y - y);
            CarveBrush(x, y, walkableMask, width, height);
        }
    }

    private void CarveBrush(int centerX, int centerY, bool[,] walkableMask, int width, int height)
    {
        int radius = (_corridorWidth - 1) / 2;
        int extra = _corridorWidth % 2 == 0 ? 1 : 0;

        for (int dx = -radius; dx <= radius + extra; dx++)
        {
            for (int dy = -radius; dy <= radius + extra; dy++)
            {
                SetWalkable(centerX + dx, centerY + dy, walkableMask, width, height);
            }
        }
    }

    private static void EnsureRoomCentersAreWalkable(List<RoomData> rooms, bool[,] walkableMask, int width, int height)
    {
        foreach (RoomData room in rooms)
        {
            if (room.Center.x < 0 || room.Center.y < 0 || room.Center.x >= width || room.Center.y >= height) continue;
            walkableMask[room.Center.x, room.Center.y] = true;
        }
    }

    private static int ManhattanDistance(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    private static void SetWalkable(int x, int y, bool[,] walkableMask, int width, int height)
    {
        if (x < 0 || y < 0 || x >= width || y >= height) return;
        walkableMask[x, y] = true;
    }
    private void GenerateEnemiesByPlayerStrength(System.Random rng)
    {
        if (!_generateEnemiesByPlayerStrength) return;
        if (UnitsManager.Instance == null || GridManager.Instance == null) return;

        int playerOverall = CalculateLivingPlayerOverall();
        if (playerOverall <= 0)
        {
            Debug.Log("Pominięto generowanie przeciwników: brak aktywnych jednostek gracza.");
            return;
        }

        if (_clearExistingEnemiesBeforeSpawn)
        {
            RemoveAllEnemyUnits();
        }

        List<EnemyTemplate> templates = BuildEnemyTemplates();
        if (templates.Count == 0)
        {
            Debug.LogWarning("Brak dostępnych szablonów przeciwników do wygenerowania.");
            return;
        }

        List<Vector2> availablePositions = GridManager.Instance.AvailablePositions();
        if (availablePositions.Count == 0)
        {
            Debug.LogWarning("Brak wolnych pól do wygenerowania przeciwników.");
            return;
        }

        int minEnemies = Mathf.Max(1, _minGeneratedEnemies);
        int maxEnemies = Mathf.Max(minEnemies, _maxGeneratedEnemies);
        int maxSpawnBySpace = Mathf.Min(maxEnemies, availablePositions.Count);
        if (maxSpawnBySpace <= 0) return;

        int targetOverall = Mathf.Max(1, Mathf.RoundToInt(playerOverall * Mathf.Max(0.1f, _enemyOverallMultiplier)));
        int toleranceAbs = Mathf.RoundToInt(targetOverall * Mathf.Clamp01(_overallTolerance));
        int minTargetOverall = Mathf.Max(1, targetOverall - toleranceAbs);
        int maxTargetOverall = targetOverall + toleranceAbs;

        bool generated = false;

        if (_useEncounterGroups)
        {
            generated = TryGenerateEnemiesFromEncounterGroups(
                rng,
                templates,
                availablePositions,
                minEnemies,
                maxSpawnBySpace,
                targetOverall,
                toleranceAbs,
                minTargetOverall,
                maxTargetOverall
            );
        }

        if (!generated)
        {
            GenerateEnemiesSingleTypeFallback(
                rng,
                templates,
                availablePositions,
                minEnemies,
                maxSpawnBySpace,
                targetOverall,
                toleranceAbs,
                minTargetOverall,
                maxTargetOverall
            );
        }
    }

    private void GenerateEnemiesSingleTypeFallback(
        System.Random rng,
        List<EnemyTemplate> templates,
        List<Vector2> availablePositions,
        int minEnemies,
        int maxSpawnBySpace,
        int targetOverall,
        int toleranceAbs,
        int minTargetOverall,
        int maxTargetOverall)
    {
        if (templates == null || templates.Count == 0 || availablePositions == null || availablePositions.Count == 0) return;

        EnemyTemplate selectedTemplate = null;
        int selectedCount = 1;
        HashSet<string> blockedStandaloneRaces = BuildDisallowedStandaloneRaceSet();

        List<EnemySpawnCandidate> allCandidates = new List<EnemySpawnCandidate>();

        // Fallback: jeden typ przeciwnika i liczba jego kopii (1..N).
        for (int i = 0; i < templates.Count; i++)
        {
            EnemyTemplate template = templates[i];
            if (template == null) continue;
            if (IsStandaloneRaceBlocked(template.Race, blockedStandaloneRaces)) continue;

            int estimated = Mathf.Max(1, template.EstimatedOverall);

            for (int count = 1; count <= maxSpawnBySpace; count++)
            {
                if (count < minEnemies) continue;

                int predictedOverall = estimated * count;
                int diff = Mathf.Abs(targetOverall - predictedOverall);
                bool inTolerance = predictedOverall >= minTargetOverall && predictedOverall <= maxTargetOverall;

                int score = diff;

                if (predictedOverall > maxTargetOverall)
                {
                    score += (predictedOverall - maxTargetOverall) * 3;
                }
                else if (predictedOverall < minTargetOverall)
                {
                    score += (minTargetOverall - predictedOverall);
                }

                if (count == 1)
                {
                    score += Mathf.Max(1, targetOverall / 20);
                }

                if (template.UnitId == _lastGeneratedEnemyUnitId)
                {
                    score += Mathf.Max(5, targetOverall / 8);
                }

                allCandidates.Add(new EnemySpawnCandidate
                {
                    Template = template,
                    Count = count,
                    PredictedOverall = predictedOverall,
                    Diff = diff,
                    Score = score,
                    InTolerance = inTolerance
                });
            }
        }

        if (allCandidates.Count == 0)
        {
            Debug.LogWarning("Fallback single-type: brak poprawnych kandydatów przeciwników.");
            return;
        }

        Dictionary<int, EnemySpawnCandidate> bestPerType = new Dictionary<int, EnemySpawnCandidate>();
        for (int i = 0; i < allCandidates.Count; i++)
        {
            EnemySpawnCandidate candidate = allCandidates[i];
            int unitId = candidate.Template.UnitId;

            if (!bestPerType.TryGetValue(unitId, out EnemySpawnCandidate existing))
            {
                bestPerType[unitId] = candidate;
                continue;
            }

            bool better = false;
            if (candidate.Score < existing.Score) better = true;
            else if (candidate.Score == existing.Score)
            {
                if (candidate.InTolerance && !existing.InTolerance) better = true;
                else if (candidate.InTolerance == existing.InTolerance)
                {
                    if (candidate.Diff < existing.Diff) better = true;
                    else if (candidate.Diff == existing.Diff && candidate.Count > existing.Count) better = true;
                }
            }

            if (better)
            {
                bestPerType[unitId] = candidate;
            }
        }

        List<EnemySpawnCandidate> candidates = new List<EnemySpawnCandidate>(bestPerType.Values);
        candidates.Sort((a, b) =>
        {
            int scoreCmp = a.Score.CompareTo(b.Score);
            if (scoreCmp != 0) return scoreCmp;

            if (a.InTolerance != b.InTolerance)
            {
                return a.InTolerance ? -1 : 1;
            }

            int diffCmp = a.Diff.CompareTo(b.Diff);
            if (diffCmp != 0) return diffCmp;

            return b.Count.CompareTo(a.Count);
        });

        int top = Mathf.Clamp(_enemySelectionTopCandidates, 1, candidates.Count);
        int totalWeight = 0;
        for (int i = 0; i < top; i++)
        {
            totalWeight += (top - i);
        }

        int roll = rng.Next(0, totalWeight);
        int acc = 0;
        int selectedIndex = 0;
        for (int i = 0; i < top; i++)
        {
            acc += (top - i);
            if (roll < acc)
            {
                selectedIndex = i;
                break;
            }
        }

        EnemySpawnCandidate chosen = candidates[selectedIndex];
        selectedTemplate = chosen.Template;
        selectedCount = chosen.Count;
        _lastGeneratedEnemyUnitId = selectedTemplate.UnitId;
        _lastGeneratedEncounterName = string.Empty;

        int generatedOverall = 0;
        int generatedEnemies = 0;

        for (int i = 0; i < selectedCount && availablePositions.Count > 0; i++)
        {
            Vector2 spawnPosition = TakeRandomPosition(availablePositions, rng);

            GameObject enemyObject = UnitsManager.Instance.CreateUnitById(selectedTemplate.UnitId, spawnPosition, false);
            if (enemyObject == null) continue;

            generatedOverall += CalculateSpawnedUnitOverall(enemyObject, selectedTemplate.EstimatedOverall);
            generatedEnemies++;
        }

        GridManager.Instance.CheckTileOccupancy();
        InitiativeQueueManager.Instance.UpdateInitiativeQueue();
        InitiativeQueueManager.Instance.CalculateDominance();

        Debug.Log($"<color=green>Wygenerowano przeciwników (fallback): {generatedEnemies}, typ: {selectedTemplate.Race}, łączny Overall: {generatedOverall}, cel: {targetOverall} (+/- {toleranceAbs}).</color>");
    }

    private bool TryGenerateEnemiesFromEncounterGroups(
        System.Random rng,
        List<EnemyTemplate> templates,
        List<Vector2> availablePositions,
        int minEnemies,
        int maxSpawnBySpace,
        int targetOverall,
        int toleranceAbs,
        int minTargetOverall,
        int maxTargetOverall)
    {
        EncounterCatalog catalog = TryLoadEncounterCatalog();
        if (catalog == null || catalog.Encounters == null || catalog.Encounters.Length == 0)
        {
            Debug.LogWarning($"Nie znaleziono poprawnych danych encounterów w Resources/{_encountersResourceName}.json. Używam fallback single-type.");
            return false;
        }

        List<EncounterGroupRuntime> groups = BuildEncounterGroupsRuntime(catalog, templates);
        if (groups.Count == 0)
        {
            Debug.LogWarning("Brak poprawnych grup encounterów po walidacji. Używam fallback single-type.");
            return false;
        }

        int availableTiles = availablePositions.Count;
        List<EncounterSpawnCandidate> allCandidates = new List<EncounterSpawnCandidate>();
        int attemptsPerGroup = Mathf.Max(1, _encounterBuildAttemptsPerGroup);

        for (int i = 0; i < groups.Count; i++)
        {
            EncounterGroupRuntime group = groups[i];
            for (int attempt = 0; attempt < attemptsPerGroup; attempt++)
            {
                EncounterSpawnCandidate candidate = BuildEncounterCandidate(
                    group,
                    minEnemies,
                    maxSpawnBySpace,
                    availableTiles,
                    targetOverall,
                    minTargetOverall,
                    maxTargetOverall,
                    rng
                );

                if (candidate != null)
                {
                    allCandidates.Add(candidate);
                }
            }
        }

        if (allCandidates.Count == 0)
        {
            Debug.LogWarning("Nie udało się zbudować żadnego poprawnego encounteru. Używam fallback single-type.");
            return false;
        }

        allCandidates.Sort((a, b) =>
        {
            int scoreCmp = a.Score.CompareTo(b.Score);
            if (scoreCmp != 0) return scoreCmp;

            if (a.InTolerance != b.InTolerance)
            {
                return a.InTolerance ? -1 : 1;
            }

            int diffCmp = a.Diff.CompareTo(b.Diff);
            if (diffCmp != 0) return diffCmp;

            int groupWeightCmp = b.Group.Weight.CompareTo(a.Group.Weight);
            if (groupWeightCmp != 0) return groupWeightCmp;

            return b.Spawns.Count.CompareTo(a.Spawns.Count);
        });

        int top = Mathf.Clamp(_enemySelectionTopCandidates, 1, allCandidates.Count);
        int totalWeight = 0;
        for (int i = 0; i < top; i++)
        {
            totalWeight += (top - i);
        }

        int roll = rng.Next(0, totalWeight);
        int acc = 0;
        int selectedIndex = 0;
        for (int i = 0; i < top; i++)
        {
            acc += (top - i);
            if (roll < acc)
            {
                selectedIndex = i;
                break;
            }
        }

        EncounterSpawnCandidate chosen = allCandidates[selectedIndex];
        bool spawned = SpawnEncounterCandidate(chosen, availablePositions, rng, targetOverall, toleranceAbs);
        if (!spawned) return false;

        _lastGeneratedEncounterName = chosen.Group != null ? chosen.Group.Name : string.Empty;
        if (chosen.Spawns.Count > 0 && chosen.Spawns[0].RiderTemplate != null)
        {
            _lastGeneratedEnemyUnitId = chosen.Spawns[0].RiderTemplate.UnitId;
        }

        return true;
    }

    private EncounterCatalog TryLoadEncounterCatalog()
    {
        if (string.IsNullOrWhiteSpace(_encountersResourceName))
        {
            return null;
        }

        TextAsset encountersJson = Resources.Load<TextAsset>(_encountersResourceName.Trim());
        if (encountersJson == null)
        {
            return null;
        }

        return JsonUtility.FromJson<EncounterCatalog>(encountersJson.text);
    }

    private List<EncounterGroupRuntime> BuildEncounterGroupsRuntime(EncounterCatalog catalog, List<EnemyTemplate> templates)
    {
        List<EncounterGroupRuntime> groups = new List<EncounterGroupRuntime>();
        if (catalog == null || catalog.Encounters == null || templates == null || templates.Count == 0)
        {
            return groups;
        }

        Dictionary<string, EnemyTemplate> templatesByRace = new Dictionary<string, EnemyTemplate>();
        for (int i = 0; i < templates.Count; i++)
        {
            EnemyTemplate template = templates[i];
            if (template == null) continue;

            string key = NormalizeRaceKey(template.Race);
            if (string.IsNullOrEmpty(key)) continue;

            templatesByRace[key] = template;
        }

        HashSet<string> blockedStandaloneRaces = BuildDisallowedStandaloneRaceSet();

        for (int i = 0; i < catalog.Encounters.Length; i++)
        {
            EncounterGroupData groupData = catalog.Encounters[i];
            if (groupData == null || groupData.Members == null || groupData.Members.Length == 0) continue;

            EncounterGroupRuntime group = new EncounterGroupRuntime
            {
                Name = string.IsNullOrWhiteSpace(groupData.Name) ? $"Encounter {i + 1}" : groupData.Name.Trim(),
                Weight = Mathf.Max(1, groupData.Weight),
                MinUnits = Mathf.Max(1, groupData.MinUnits),
                MaxUnits = Mathf.Max(Mathf.Max(1, groupData.MinUnits), groupData.MaxUnits)
            };

            for (int memberIndex = 0; memberIndex < groupData.Members.Length; memberIndex++)
            {
                EncounterMemberData memberData = groupData.Members[memberIndex];
                if (memberData == null || string.IsNullOrWhiteSpace(memberData.Race)) continue;

                string riderRaceKey = NormalizeRaceKey(memberData.Race);
                if (string.IsNullOrEmpty(riderRaceKey)) continue;

                if (!templatesByRace.TryGetValue(riderRaceKey, out EnemyTemplate riderTemplate))
                {
                    continue;
                }

                EncounterMemberRuntime member = new EncounterMemberRuntime
                {
                    Template = riderTemplate,
                    Weight = Mathf.Max(1, memberData.Weight),
                    MaxPerGroup = Mathf.Max(0, memberData.MaxPerGroup),
                    CanAppearOnFoot = memberData.CanAppearOnFoot && !IsStandaloneRaceBlocked(riderTemplate.Race, blockedStandaloneRaces),
                    MountedChance = Mathf.Clamp01(memberData.MountedChance)
                };

                if (memberData.Mounts != null)
                {
                    for (int mountIndex = 0; mountIndex < memberData.Mounts.Length; mountIndex++)
                    {
                        EncounterMountData mountData = memberData.Mounts[mountIndex];
                        if (mountData == null || string.IsNullOrWhiteSpace(mountData.Race)) continue;

                        string mountRaceKey = NormalizeRaceKey(mountData.Race);
                        if (string.IsNullOrEmpty(mountRaceKey)) continue;

                        if (!templatesByRace.TryGetValue(mountRaceKey, out EnemyTemplate mountTemplate))
                        {
                            continue;
                        }

                        if (mountTemplate.UnitId == riderTemplate.UnitId) continue;
                        if (mountTemplate.Size <= riderTemplate.Size) continue;

                        member.MountTemplates.Add(mountTemplate);
                        member.MountWeights.Add(Mathf.Max(1, mountData.Weight));
                    }
                }

                if (!member.CanAppearOnFoot && member.MountTemplates.Count == 0)
                {
                    continue;
                }

                group.Members.Add(member);
            }

            if (group.Members.Count > 0)
            {
                groups.Add(group);
            }
        }

        return groups;
    }

    private EncounterSpawnCandidate BuildEncounterCandidate(
        EncounterGroupRuntime group,
        int minEnemies,
        int maxSpawnBySpace,
        int availableTiles,
        int targetOverall,
        int minTargetOverall,
        int maxTargetOverall,
        System.Random rng)
    {
        if (group == null || group.Members == null || group.Members.Count == 0) return null;

        int maxBound = Mathf.Max(minEnemies, maxSpawnBySpace);
        int groupMin = Mathf.Clamp(group.MinUnits, minEnemies, maxBound);
        int groupMax = Mathf.Clamp(group.MaxUnits, groupMin, maxBound);
        if (groupMin > groupMax) return null;

        int desiredUnits = rng.Next(groupMin, groupMax + 1);
        int remainingTiles = availableTiles;

        EncounterSpawnCandidate candidate = new EncounterSpawnCandidate
        {
            Group = group
        };
        Dictionary<int, int> spawnedPerUnitId = new Dictionary<int, int>();

        HashSet<string> uniqueRaces = new HashSet<string>();
        int buildAttempts = desiredUnits * 6;

        while (candidate.Spawns.Count < desiredUnits && buildAttempts-- > 0)
        {
            PlannedEnemySpawn spawn = CreatePlannedSpawn(group, remainingTiles, spawnedPerUnitId, rng);
            if (spawn == null)
            {
                break;
            }

            int tileCost = spawn.IsMounted ? 2 : 1;
            if (tileCost > remainingTiles)
            {
                continue;
            }

            candidate.Spawns.Add(spawn);
            candidate.PredictedOverall += Mathf.Max(1, spawn.PredictedOverall);
            candidate.TileUsage += tileCost;
            remainingTiles -= tileCost;

            if (spawn.RiderTemplate != null)
            {
                int riderUnitId = spawn.RiderTemplate.UnitId;
                if (spawnedPerUnitId.TryGetValue(riderUnitId, out int currentCount))
                {
                    spawnedPerUnitId[riderUnitId] = currentCount + 1;
                }
                else
                {
                    spawnedPerUnitId[riderUnitId] = 1;
                }
            }

            if (spawn.RiderTemplate != null && !string.IsNullOrWhiteSpace(spawn.RiderTemplate.Race))
            {
                uniqueRaces.Add(NormalizeRaceKey(spawn.RiderTemplate.Race));
            }
        }

        if (candidate.Spawns.Count < minEnemies) return null;
        if (candidate.TileUsage > availableTiles) return null;

        candidate.Diff = Mathf.Abs(targetOverall - candidate.PredictedOverall);
        candidate.InTolerance = candidate.PredictedOverall >= minTargetOverall && candidate.PredictedOverall <= maxTargetOverall;

        int score = candidate.Diff;

        if (candidate.PredictedOverall > maxTargetOverall)
        {
            score += (candidate.PredictedOverall - maxTargetOverall) * 3;
        }
        else if (candidate.PredictedOverall < minTargetOverall)
        {
            score += (minTargetOverall - candidate.PredictedOverall);
        }

        if (uniqueRaces.Count <= 1 && candidate.Spawns.Count > 1)
        {
            score += Mathf.Max(4, targetOverall / 18);
        }

        if (!string.IsNullOrWhiteSpace(_lastGeneratedEncounterName) &&
            string.Equals(_lastGeneratedEncounterName, group.Name, StringComparison.OrdinalIgnoreCase))
        {
            score += Mathf.Max(6, targetOverall / 10);
        }

        score -= Mathf.Min(group.Weight, 10);
        candidate.Score = Mathf.Max(0, score);

        return candidate;
    }

    private PlannedEnemySpawn CreatePlannedSpawn(
        EncounterGroupRuntime group,
        int remainingTiles,
        Dictionary<int, int> spawnedPerUnitId,
        System.Random rng)
    {
        if (group == null || group.Members == null || group.Members.Count == 0) return null;
        if (remainingTiles <= 0) return null;

        List<EncounterMemberRuntime> validMembers = new List<EncounterMemberRuntime>();
        int totalWeight = 0;

        for (int i = 0; i < group.Members.Count; i++)
        {
            EncounterMemberRuntime member = group.Members[i];
            if (member == null || member.Template == null) continue;

            if (member.MaxPerGroup > 0)
            {
                int spawnedCount = 0;
                if (spawnedPerUnitId != null)
                {
                    spawnedPerUnitId.TryGetValue(member.Template.UnitId, out spawnedCount);
                }

                if (spawnedCount >= member.MaxPerGroup)
                {
                    continue;
                }
            }

            bool canMounted = member.MountTemplates != null && member.MountTemplates.Count > 0 && remainingTiles >= 2;
            bool canFoot = member.CanAppearOnFoot && remainingTiles >= 1;
            if (!canMounted && !canFoot) continue;

            validMembers.Add(member);
            totalWeight += Mathf.Max(1, member.Weight);
        }

        if (validMembers.Count == 0 || totalWeight <= 0)
        {
            return null;
        }

        int roll = rng.Next(0, totalWeight);
        int acc = 0;
        EncounterMemberRuntime selected = validMembers[0];
        for (int i = 0; i < validMembers.Count; i++)
        {
            EncounterMemberRuntime member = validMembers[i];
            acc += Mathf.Max(1, member.Weight);
            if (roll < acc)
            {
                selected = member;
                break;
            }
        }

        bool canUseMount = selected.MountTemplates != null && selected.MountTemplates.Count > 0 && remainingTiles >= 2;
        bool shouldMount = false;

        if (canUseMount)
        {
            if (!selected.CanAppearOnFoot)
            {
                shouldMount = true;
            }
            else
            {
                shouldMount = rng.NextDouble() < selected.MountedChance;
            }
        }

        EnemyTemplate mountTemplate = null;
        if (shouldMount)
        {
            mountTemplate = TryPickMountTemplate(selected, rng);
            if (mountTemplate == null && !selected.CanAppearOnFoot)
            {
                return null;
            }
        }

        if (mountTemplate != null)
        {
            int riderOverall = Mathf.Max(1, selected.Template.EstimatedOverall);
            int mountContribution = Mathf.RoundToInt(Mathf.Max(1, mountTemplate.EstimatedOverall) * Mathf.Clamp01(_mountedOverallContribution));

            return new PlannedEnemySpawn
            {
                RiderTemplate = selected.Template,
                MountTemplate = mountTemplate,
                IsMounted = true,
                PredictedOverall = Mathf.Max(1, riderOverall + mountContribution)
            };
        }

        return new PlannedEnemySpawn
        {
            RiderTemplate = selected.Template,
            MountTemplate = null,
            IsMounted = false,
            PredictedOverall = Mathf.Max(1, selected.Template.EstimatedOverall)
        };
    }

    private EnemyTemplate TryPickMountTemplate(EncounterMemberRuntime member, System.Random rng)
    {
        if (member == null || member.MountTemplates == null || member.MountTemplates.Count == 0) return null;

        int totalWeight = 0;
        for (int i = 0; i < member.MountTemplates.Count; i++)
        {
            EnemyTemplate template = member.MountTemplates[i];
            if (template == null) continue;

            int weight = 1;
            if (member.MountWeights != null && i < member.MountWeights.Count)
            {
                weight = Mathf.Max(1, member.MountWeights[i]);
            }

            totalWeight += weight;
        }

        if (totalWeight <= 0) return null;

        int roll = rng.Next(0, totalWeight);
        int acc = 0;

        for (int i = 0; i < member.MountTemplates.Count; i++)
        {
            EnemyTemplate template = member.MountTemplates[i];
            if (template == null) continue;

            int weight = 1;
            if (member.MountWeights != null && i < member.MountWeights.Count)
            {
                weight = Mathf.Max(1, member.MountWeights[i]);
            }

            acc += weight;
            if (roll < acc)
            {
                return template;
            }
        }

        return null;
    }

    private bool SpawnEncounterCandidate(EncounterSpawnCandidate chosen, List<Vector2> availablePositions, System.Random rng, int targetOverall, int toleranceAbs)
    {
        if (chosen == null || chosen.Spawns == null || chosen.Spawns.Count == 0) return false;
        if (availablePositions == null || availablePositions.Count == 0) return false;

        // Zmienia listę jednostek na ogólną, a nie na zapiane jednostki (zapobiega to błędom z Id tworzonych losowo jednostek)
        UnitsManager.Instance.SetSavedUnitsManaging(false);
        DataManager.Instance.ChangeUnitListByToggle();

        int generatedOverall = 0;
        int generatedEnemies = 0;
        List<string> composition = new List<string>();

        for (int i = 0; i < chosen.Spawns.Count && availablePositions.Count > 0; i++)
        {
            PlannedEnemySpawn planned = chosen.Spawns[i];
            if (planned == null || planned.RiderTemplate == null) continue;

            Vector2 riderPosition = TakeRandomPosition(availablePositions, rng);
            GameObject riderObject = UnitsManager.Instance.CreateUnitById(planned.RiderTemplate.UnitId, riderPosition, false);
            if (riderObject == null) continue;

            generatedOverall += CalculateSpawnedUnitOverall(riderObject, planned.RiderTemplate.EstimatedOverall);
            generatedEnemies++;

            string riderRace = string.IsNullOrWhiteSpace(planned.RiderTemplate.Race) ? "Nieznany" : planned.RiderTemplate.Race;
            bool mountedApplied = false;
            string mountRace = string.Empty;

            if (planned.IsMounted && planned.MountTemplate != null && availablePositions.Count > 0)
            {
                Vector2 mountPosition = TakeRandomPosition(availablePositions, rng);
                GameObject mountObject = UnitsManager.Instance.CreateUnitById(planned.MountTemplate.UnitId, mountPosition, false);
                if (mountObject != null)
                {
                    int mountOverall = CalculateSpawnedUnitOverall(mountObject, planned.MountTemplate.EstimatedOverall);
                    mountRace = string.IsNullOrWhiteSpace(planned.MountTemplate.Race) ? "Wierzchowiec" : planned.MountTemplate.Race;

                    Unit riderUnit = riderObject.GetComponent<Unit>();
                    Unit mountUnit = mountObject.GetComponent<Unit>();
                    mountedApplied = TryApplyMount(riderUnit, mountUnit);

                    if (mountedApplied)
                    {
                        generatedOverall += Mathf.RoundToInt(Mathf.Max(1, mountOverall) * Mathf.Clamp01(_mountedOverallContribution));
                    }
                    else
                    {
                        generatedEnemies++;
                        generatedOverall += Mathf.Max(1, mountOverall);
                    }
                }
            }

            if (mountedApplied)
            {
                composition.Add($"{riderRace} na {mountRace}");
            }
            else
            {
                composition.Add(riderRace);
                if (!string.IsNullOrWhiteSpace(mountRace))
                {
                    composition.Add(mountRace);
                }
            }
        }

        if (generatedEnemies <= 0)
        {
            return false;
        }

        GridManager.Instance.CheckTileOccupancy();
        InitiativeQueueManager.Instance.UpdateInitiativeQueue();
        InitiativeQueueManager.Instance.CalculateDominance();

        string groupName = chosen.Group != null && !string.IsNullOrWhiteSpace(chosen.Group.Name)
            ? chosen.Group.Name
            : "Nieznana grupa";
        string compositionText = composition.Count > 0 ? string.Join(", ", composition) : "brak";

        Debug.Log($"<color=green>Wygenerowano przeciwników (encounter: {groupName}): {generatedEnemies}, skład: {compositionText}, łączny Overall: {generatedOverall}, cel: {targetOverall} (+/- {toleranceAbs}).</color>");
        return true;
    }

    private bool TryApplyMount(Unit rider, Unit mount)
    {
        if (rider == null || mount == null) return false;

        Stats riderStats = rider.Stats != null ? rider.Stats : rider.GetComponent<Stats>();
        Stats mountStats = mount.Stats != null ? mount.Stats : mount.GetComponent<Stats>();
        if (riderStats == null || mountStats == null) return false;

        if (mountStats.Size <= riderStats.Size) return false;
        if (mount.HasRider) return false;

        rider.Stats = riderStats;
        mount.Stats = mountStats;
        rider.Mount = mount;
        rider.MountId = mount.UnitId;

        rider.IsMounted = true;
        mount.HasRider = true;
        mount.transform.position = rider.transform.position;
        mount.transform.SetParent(rider.transform);

        if (InitiativeQueueManager.Instance != null)
        {
            InitiativeQueueManager.Instance.RemoveUnitFromInitiativeQueue(mount);
        }

        mount.gameObject.SetActive(false);

        if (MountsManager.Instance != null)
        {
            MountsManager.Instance.UpdateMountIcon(rider);
        }

        int mountedSpeed = mountStats.Flight != 0 ? mountStats.Flight : mountStats.Sz;
        mountStats.TempSz = mountedSpeed;
        riderStats.TempSz = mountedSpeed;

        return true;
    }

    private static int CalculateSpawnedUnitOverall(GameObject unitObject, int fallbackOverall)
    {
        if (unitObject == null) return Mathf.Max(1, fallbackOverall);

        Stats stats = unitObject.GetComponent<Stats>();
        if (stats == null) return Mathf.Max(1, fallbackOverall);

        int overall = stats.CalculateOverall();
        stats.Overall = overall;
        return Mathf.Max(1, overall);
    }

    private static Vector2 TakeRandomPosition(List<Vector2> availablePositions, System.Random rng)
    {
        if (availablePositions == null || availablePositions.Count == 0) return Vector2.zero;

        int positionIndex = rng.Next(0, availablePositions.Count);
        Vector2 spawnPosition = availablePositions[positionIndex];
        availablePositions.RemoveAt(positionIndex);
        return spawnPosition;
    }

    private HashSet<string> BuildDisallowedStandaloneRaceSet()
    {
        HashSet<string> blocked = new HashSet<string>();
        if (_disallowedStandaloneEnemyRaces == null) return blocked;

        for (int i = 0; i < _disallowedStandaloneEnemyRaces.Count; i++)
        {
            string key = NormalizeRaceKey(_disallowedStandaloneEnemyRaces[i]);
            if (!string.IsNullOrEmpty(key))
            {
                blocked.Add(key);
            }
        }

        return blocked;
    }

    private static bool IsStandaloneRaceBlocked(string race, HashSet<string> blocked)
    {
        if (blocked == null || blocked.Count == 0) return false;

        string key = NormalizeRaceKey(race);
        if (string.IsNullOrEmpty(key)) return false;

        return blocked.Contains(key);
    }

    private static string NormalizeRaceKey(string race)
    {
        if (string.IsNullOrWhiteSpace(race)) return string.Empty;
        return race.Trim().ToLowerInvariant();
    }

    private int CalculateLivingPlayerOverall()
    {
        if (UnitsManager.Instance == null) return 0;

        int sum = 0;

        for (int i = 0; i < UnitsManager.Instance.AllUnits.Count; i++)
        {
            Unit unit = UnitsManager.Instance.AllUnits[i];
            if (unit == null || !unit.CompareTag("PlayerUnit")) continue;

            Stats stats = unit.GetComponent<Stats>();
            if (stats == null || stats.TempHealth <= 0) continue;

            int overall = stats.CalculateOverall();
            stats.Overall = overall;
            sum += Mathf.Max(1, overall);
        }

        return sum;
    }

    private void RemoveAllEnemyUnits()
    {
        if (UnitsManager.Instance == null) return;

        List<Unit> enemiesToRemove = new List<Unit>();

        for (int i = 0; i < UnitsManager.Instance.AllUnits.Count; i++)
        {
            Unit unit = UnitsManager.Instance.AllUnits[i];
            if (unit != null && unit.CompareTag("EnemyUnit"))
            {
                enemiesToRemove.Add(unit);
            }
        }

        for (int i = 0; i < enemiesToRemove.Count; i++)
        {
            if (enemiesToRemove[i] != null)
            {
                UnitsManager.Instance.DestroyUnit(enemiesToRemove[i].gameObject);
            }
        }
    }

    private List<EnemyTemplate> BuildEnemyTemplates()
    {
        List<EnemyTemplate> templates = new List<EnemyTemplate>();

        TextAsset unitsJson = Resources.Load<TextAsset>("units");
        if (unitsJson == null)
        {
            Debug.LogError("Brak pliku Resources/units.json.");
            return templates;
        }

        StatsData[] statsArray = JsonHelper.FromJson<StatsData>(unitsJson.text);
        if (statsArray == null || statsArray.Length == 0)
        {
            return templates;
        }

        for (int i = 0; i < statsArray.Length; i++)
        {
            if (statsArray[i] != null && statsArray[i].Id == 0)
            {
                statsArray[i].Id = i + 1;
            }
        }

        Dictionary<string, WeaponData> weaponsByName = LoadWeaponDataByName();

        for (int i = 0; i < statsArray.Length; i++)
        {
            StatsData statsData = statsArray[i];
            if (statsData == null) continue;

            if (!_includePlayableRacesAsEnemies && IsPlayableTemplate(statsData))
            {
                continue;
            }

            int estimatedOverall = EstimateTemplateOverall(statsData, weaponsByName);
            if (estimatedOverall <= 0) continue;

            templates.Add(new EnemyTemplate
            {
                UnitId = statsData.Id,
                Race = statsData.Race,
                EstimatedOverall = estimatedOverall,
                Size = (int)statsData.Size
            });
        }

        templates.Sort((a, b) => a.EstimatedOverall.CompareTo(b.EstimatedOverall));
        return templates;
    }

    private EnemyTemplate PickTemplateForRemainingOverall(List<EnemyTemplate> templates, int remaining, int toleranceAbs, System.Random rng)
    {
        if (templates == null || templates.Count == 0) return null;

        int maxPreferred = Mathf.Max(1, remaining + toleranceAbs);
        List<EnemyTemplate> preferred = new List<EnemyTemplate>();

        for (int i = 0; i < templates.Count; i++)
        {
            if (templates[i].EstimatedOverall <= maxPreferred)
            {
                preferred.Add(templates[i]);
            }
        }

        List<EnemyTemplate> pool = preferred.Count > 0 ? preferred : templates;

        pool.Sort((a, b) =>
        {
            int diffA = Mathf.Abs(remaining - a.EstimatedOverall);
            int diffB = Mathf.Abs(remaining - b.EstimatedOverall);
            return diffA.CompareTo(diffB);
        });

        int topCount = Mathf.Min(3, pool.Count);
        return pool[rng.Next(0, topCount)];
    }

    private int EstimateTemplateOverall(StatsData statsData, Dictionary<string, WeaponData> weaponsByName)
    {
        GameObject tempObject = new GameObject("__OverallEstimator");
        tempObject.SetActive(false);

        Unit tempUnit = tempObject.AddComponent<Unit>();
        Stats tempStats = tempObject.AddComponent<Stats>();
        Inventory tempInventory = tempObject.AddComponent<Inventory>();
        Weapon tempWeapon = tempObject.AddComponent<Weapon>();

        tempUnit.Stats = tempStats;
        CopyStatsDataToStats(statsData, tempStats);
        tempStats.SetBaseStats();

        int bestOverall = 0;
        bool testedWeapon = false;

        if (statsData.PrimaryWeaponNames != null)
        {
            for (int i = 0; i < statsData.PrimaryWeaponNames.Count; i++)
            {
                string weaponName = statsData.PrimaryWeaponNames[i];
                if (string.IsNullOrWhiteSpace(weaponName)) continue;
                if (!weaponsByName.TryGetValue(weaponName, out WeaponData weaponData)) continue;
                if (IsArmorType(weaponData)) continue;

                CopyWeaponDataToWeapon(weaponData, tempWeapon);
                ApplyPrimaryWeaponOverrides(statsData, tempWeapon);

                tempInventory.AllWeapons.Clear();
                tempInventory.AllWeapons.Add(tempWeapon);
                tempInventory.EquippedWeapons[0] = tempWeapon;
                tempInventory.EquippedWeapons[1] = null;

                int overall = tempStats.CalculateOverall();
                if (overall > bestOverall)
                {
                    bestOverall = overall;
                }

                testedWeapon = true;
            }
        }

        if (!testedWeapon)
        {
            tempWeapon.ResetWeapon();
            tempInventory.AllWeapons.Clear();
            tempInventory.EquippedWeapons[0] = null;
            tempInventory.EquippedWeapons[1] = null;

            bestOverall = tempStats.CalculateOverall();
        }

        Destroy(tempObject);
        return Mathf.Max(1, bestOverall);
    }

    private static Dictionary<string, WeaponData> LoadWeaponDataByName()
    {
        Dictionary<string, WeaponData> byName = new Dictionary<string, WeaponData>();

        TextAsset weaponsJson = Resources.Load<TextAsset>("weapons");
        if (weaponsJson == null)
        {
            Debug.LogError("Brak pliku Resources/weapons.json.");
            return byName;
        }

        WeaponData[] weapons = JsonHelper.FromJson<WeaponData>(weaponsJson.text);
        if (weapons == null) return byName;

        for (int i = 0; i < weapons.Length; i++)
        {
            WeaponData weapon = weapons[i];
            if (weapon == null || string.IsNullOrWhiteSpace(weapon.Name)) continue;
            byName[weapon.Name] = weapon;
        }

        return byName;
    }

    private static bool IsArmorType(WeaponData weaponData)
    {
        if (weaponData == null || weaponData.Type == null) return false;

        for (int i = 0; i < weaponData.Type.Length; i++)
        {
            string type = weaponData.Type[i];
            if (type == "head" || type == "torso" || type == "arms" || type == "legs" || type == "shield")
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPlayableTemplate(StatsData statsData)
    {
        if (statsData == null) return false;
        return statsData.Id >= 1 && statsData.Id <= 4;
    }

    private static void CopyStatsDataToStats(StatsData source, Stats target)
    {
        FieldInfo[] sourceFields = typeof(StatsData).GetFields(BindingFlags.Instance | BindingFlags.Public);

        for (int i = 0; i < sourceFields.Length; i++)
        {
            FieldInfo sourceField = sourceFields[i];
            FieldInfo targetField = typeof(Stats).GetField(sourceField.Name, BindingFlags.Instance | BindingFlags.Public);
            if (targetField == null) continue;

            object value = sourceField.GetValue(source);
            if (value == null) continue;

            if (value is List<string> stringList)
            {
                targetField.SetValue(target, new List<string>(stringList));
            }
            else if (value is List<PairString> pairList)
            {
                List<PairString> cloned = new List<PairString>(pairList.Count);
                for (int j = 0; j < pairList.Count; j++)
                {
                    PairString pair = pairList[j];
                    cloned.Add(new PairString { Key = pair.Key, Value = pair.Value });
                }
                targetField.SetValue(target, cloned);
            }
            else if (value is string[] stringArray)
            {
                targetField.SetValue(target, (string[])stringArray.Clone());
            }
            else
            {
                // Pomijamy pola, których nie da się bezpiecznie przypisać 1:1
                // (np. List<SpellEffectData> -> List<SpellEffect>).
                if (targetField.FieldType.IsAssignableFrom(sourceField.FieldType))
                {
                    targetField.SetValue(target, value);
                }
            }
        }
    }
    private static void CopyWeaponDataToWeapon(WeaponData source, Weapon target)
    {
        FieldInfo[] sourceFields = typeof(WeaponData).GetFields(BindingFlags.Instance | BindingFlags.Public);

        for (int i = 0; i < sourceFields.Length; i++)
        {
            FieldInfo sourceField = sourceFields[i];
            FieldInfo targetField = typeof(Weapon).GetField(sourceField.Name, BindingFlags.Instance | BindingFlags.Public);
            if (targetField == null) continue;

            object value = sourceField.GetValue(source);
            if (value == null) continue;

            if (value is List<int> listValue)
            {
                targetField.SetValue(target, new List<int>(listValue));
            }
            else if (value is string[] arrayValue)
            {
                targetField.SetValue(target, (string[])arrayValue.Clone());
            }
            else
            {
                targetField.SetValue(target, value);
            }
        }

        if (string.IsNullOrEmpty(target.Quality))
        {
            target.Quality = "Zwykła";
        }
    }

    private static void ApplyPrimaryWeaponOverrides(StatsData statsData, Weapon weapon)
    {
        if (statsData == null || weapon == null || statsData.PrimaryWeaponAttributes == null) return;

        for (int i = 0; i < statsData.PrimaryWeaponAttributes.Count; i++)
        {
            PairString attr = statsData.PrimaryWeaponAttributes[i];
            if (attr == null || string.IsNullOrWhiteSpace(attr.Key)) continue;

            FieldInfo field = typeof(Weapon).GetField(attr.Key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) continue;

            if (!TryConvertStringToFieldValue(field.FieldType, attr.Value, out object converted)) continue;
            field.SetValue(weapon, converted);
        }
    }

    private static bool TryConvertStringToFieldValue(Type fieldType, string value, out object converted)
    {
        converted = null;

        if (fieldType == typeof(string))
        {
            converted = value;
            return true;
        }

        if (fieldType == typeof(int))
        {
            if (int.TryParse(value, out int parsed))
            {
                converted = parsed;
                return true;
            }
            return false;
        }

        if (fieldType == typeof(float))
        {
            if (float.TryParse(value, out float parsed))
            {
                converted = parsed;
                return true;
            }
            return false;
        }

        if (fieldType == typeof(bool))
        {
            if (bool.TryParse(value, out bool parsed))
            {
                converted = parsed;
                return true;
            }
            return false;
        }

        if (fieldType == typeof(List<int>))
        {
            List<int> list = new List<int>();

            if (!string.IsNullOrWhiteSpace(value))
            {
                string[] parts = value.Split(',');
                for (int i = 0; i < parts.Length; i++)
                {
                    if (int.TryParse(parts[i].Trim(), out int die) && die > 0)
                    {
                        list.Add(die);
                    }
                }
            }

            if (list.Count == 0)
            {
                list.Add(0);
            }

            converted = list;
            return true;
        }

        return false;
    }


    private void RemoveUnitsOutsideDungeonIfNeeded()
    {
        if (!_removeUnitsOutsideDungeon || UnitsManager.Instance == null) return;

        List<Unit> unitsOutsideDungeon = new List<Unit>();
        List<Vector2> availablePositions = GridManager.Instance != null
            ? GridManager.Instance.AvailablePositions()
            : new List<Vector2>();

        // Dodatkowa walidacja pozycji: flaga IsOccupied może być chwilowo niespójna,
        // dlatego od razu odrzucamy pola, na których stoją inne obiekty.
        for (int i = availablePositions.Count - 1; i >= 0; i--)
        {
            if (!IsRelocationTargetFree(availablePositions[i], null))
            {
                availablePositions.RemoveAt(i);
            }
        }

        foreach (Unit unit in UnitsManager.Instance.AllUnits)
        {
            if (unit == null) continue;

            Collider2D[] collidersAtUnit = Physics2D.OverlapPointAll(unit.transform.position);
            bool hasActiveTileBelow = false;

            foreach (Collider2D collider in collidersAtUnit)
            {
                if (collider != null && collider.CompareTag("Tile"))
                {
                    hasActiveTileBelow = true;
                    break;
                }
            }

            if (!hasActiveTileBelow)
            {
                unitsOutsideDungeon.Add(unit);
            }
        }

        if (unitsOutsideDungeon.Count == 0) return;

        int movedUnits = 0;
        int notMovedUnits = 0;

        for (int i = 0; i < unitsOutsideDungeon.Count; i++)
        {
            Unit unit = unitsOutsideDungeon[i];
            if (unit == null) continue;

            if (availablePositions.Count == 0)
            {
                notMovedUnits++;
                continue;
            }

            int nearestIndex = -1;
            float nearestDistance = float.MaxValue;
            Vector2 currentPosition = unit.transform.position;

            for (int pos = availablePositions.Count - 1; pos >= 0; pos--)
            {
                Vector2 candidatePosition = availablePositions[pos];

                if (!IsRelocationTargetFree(candidatePosition, unit))
                {
                    availablePositions.RemoveAt(pos);
                    continue;
                }

                float sqrDistance = (candidatePosition - currentPosition).sqrMagnitude;
                if (sqrDistance < nearestDistance)
                {
                    nearestDistance = sqrDistance;
                    nearestIndex = pos;
                }
            }

            if (nearestIndex < 0)
            {
                notMovedUnits++;
                continue;
            }

            Vector2 targetPosition = availablePositions[nearestIndex];
            availablePositions.RemoveAt(nearestIndex);

            unit.transform.position = targetPosition;
            movedUnits++;
        }

        GridManager.Instance?.CheckTileOccupancy();

        if (movedUnits > 0)
        {
            Debug.Log($"Przeniesiono {movedUnits} jednostek spoza wygenerowanego dungeonu na najbliższe wolne pola.");
        }

        if (notMovedUnits > 0)
        {
            Debug.LogWarning($"Nie udało się przenieść {notMovedUnits} jednostek: brak wolnych pól w dungeonie.");
        }
    }

    private void RemoveMapElementsOutsideDungeonIfNeeded()
    {
        if (!_removeMapElementsOutsideDungeon) return;

        GameObject[] mapElements = GameObject.FindGameObjectsWithTag("MapElement");
        if (mapElements == null || mapElements.Length == 0) return;

        int removedElements = 0;
        List<GameObject> mapEditorElements = MapEditor.Instance != null ? MapEditor.Instance.AllElements : null;

        for (int i = 0; i < mapElements.Length; i++)
        {
            GameObject mapElement = mapElements[i];
            if (mapElement == null || !mapElement.activeInHierarchy) continue;

            if (IsMapElementWithinDungeonArea(mapElement))
            {
                continue;
            }

            if (mapEditorElements != null)
            {
                mapEditorElements.Remove(mapElement);
            }

            Destroy(mapElement);
            removedElements++;
        }

        if (removedElements <= 0) return;

        GridManager.Instance?.CheckTileOccupancy();
        Debug.Log($"Usunięto {removedElements} elementów mapy spoza obszaru dungeonu.");
    }

    private static bool IsMapElementWithinDungeonArea(GameObject mapElement)
    {
        if (mapElement == null) return false;

        if (!TryGetMapElementBounds(mapElement, out Bounds bounds))
        {
            return HasActiveTileNearPoint(mapElement.transform.position);
        }

        float safeSizeX = Mathf.Max(bounds.size.x, 0.1f);
        float safeSizeY = Mathf.Max(bounds.size.y, 0.1f);

        int sampleCountX = Mathf.Clamp(Mathf.CeilToInt(safeSizeX) + 1, 2, 6);
        int sampleCountY = Mathf.Clamp(Mathf.CeilToInt(safeSizeY) + 1, 2, 6);

        for (int x = 0; x < sampleCountX; x++)
        {
            float tx = sampleCountX == 1 ? 0.5f : x / (sampleCountX - 1f);
            float sampleX = Mathf.Lerp(bounds.min.x, bounds.max.x, tx);

            for (int y = 0; y < sampleCountY; y++)
            {
                float ty = sampleCountY == 1 ? 0.5f : y / (sampleCountY - 1f);
                float sampleY = Mathf.Lerp(bounds.min.y, bounds.max.y, ty);

                if (HasActiveTileNearPoint(new Vector2(sampleX, sampleY)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryGetMapElementBounds(GameObject mapElement, out Bounds bounds)
    {
        bounds = default;
        if (mapElement == null) return false;

        Collider2D collider = mapElement.GetComponent<Collider2D>();
        if (collider != null)
        {
            bounds = collider.bounds;
            return true;
        }

        SpriteRenderer spriteRenderer = mapElement.GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            bounds = spriteRenderer.bounds;
            return true;
        }

        bounds = new Bounds(mapElement.transform.position, new Vector3(0.1f, 0.1f, 0.1f));
        return true;
    }

    private static bool HasActiveTileNearPoint(Vector2 point, float radius = 0.16f)
    {
        Collider2D[] colliders = Physics2D.OverlapCircleAll(point, radius);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D collider = colliders[i];
            if (collider == null || !collider.gameObject.activeInHierarchy) continue;
            if (collider.CompareTag("Tile")) return true;
        }

        return false;
    }

    private static bool IsRelocationTargetFree(Vector2 targetPosition, Unit unitBeingMoved)
    {
        Collider2D[] colliders = Physics2D.OverlapCircleAll(targetPosition, 0.12f);

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider2D collider = colliders[i];
            if (collider == null || !collider.gameObject.activeInHierarchy) continue;

            if (unitBeingMoved != null && collider.transform == unitBeingMoved.transform) continue;
            if (collider.CompareTag("Tile") || collider.CompareTag("TileCover")) continue;

            return false;
        }

        return true;
    }
}





















