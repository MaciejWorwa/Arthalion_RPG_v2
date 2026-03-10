using System;
using System.Collections.Generic;
using System.Reflection;
using TMPro;
using UnityEngine;

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
        public string Race;
        public int EstimatedOverall;
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


    [Header("Enemy generation")]
    [SerializeField] private bool _generateEnemiesByPlayerStrength = true;
    [SerializeField] private bool _clearExistingEnemiesBeforeSpawn = true;
    [SerializeField, Range(0.5f, 2f)] private float _enemyOverallMultiplier = 1f;
    [SerializeField, Range(0f, 0.5f)] private float _overallTolerance = 0.15f;
    [SerializeField, Min(1)] private int _minGeneratedEnemies = 1;
    [SerializeField, Min(1)] private int _maxGeneratedEnemies = 6;
    [SerializeField] private bool _includePlayableRacesAsEnemies = false;
    [SerializeField, Range(1, 8)] private int _enemySelectionTopCandidates = 4;
    private int _lastGeneratedEnemyUnitId = -1;
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
        RemoveUnitsOutsideDungeonIfNeeded();
        GenerateEnemiesByPlayerStrength(rng);

        Debug.Log($"<color=green>Wygenerowano dungeon. Seed: {seedToUse}, liczba pokoi: {rooms.Count}.</color>");
    }

    public void RestoreFullGrid()
    {
        if (GridManager.Instance == null || GridManager.Instance.Tiles == null) return;

        GridManager.Instance.SetAllTilesState(true);
        GridManager.Instance.CheckTileOccupancy();

        Debug.Log("Przywrócono pełną prostokątną siatkę.");
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
        EnemyTemplate selectedTemplate = null;
        int selectedCount = 1;

        List<EnemySpawnCandidate> allCandidates = new List<EnemySpawnCandidate>();

        // Wybieramy JEDEN typ przeciwnika i liczbę jego kopii (1..N).
        for (int i = 0; i < templates.Count; i++)
        {
            EnemyTemplate template = templates[i];
            int estimated = Mathf.Max(1, template.EstimatedOverall);

            for (int count = 1; count <= maxSpawnBySpace; count++)
            {
                if (count < minEnemies) continue;

                int predictedOverall = estimated * count;
                int diff = Mathf.Abs(targetOverall - predictedOverall);
                bool inTolerance = predictedOverall >= minTargetOverall && predictedOverall <= maxTargetOverall;

                int score = diff;

                // Mocno karzemy przeszacowanie ponad tolerancję.
                if (predictedOverall > maxTargetOverall)
                {
                    score += (predictedOverall - maxTargetOverall) * 3;
                }
                // Lekką karą traktujemy zbyt niski poziom.
                else if (predictedOverall < minTargetOverall)
                {
                    score += (minTargetOverall - predictedOverall);
                }

                // Delikatna preferencja dla kilku takich samych jednostek.
                if (count == 1)
                {
                    score += Mathf.Max(1, targetOverall / 20);
                }

                // Delikatnie unikamy ciągle tego samego typu, gdy są podobne alternatywy.
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

        if (allCandidates.Count > 0)
        {
            // Zostawiamy najlepszy wariant dla każdego typu jednostki.
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
        }

        if (selectedTemplate == null)
        {
            Debug.LogWarning("Nie udało się dobrać żadnego wariantu przeciwników.");
            return;
        }

        int generatedOverall = 0;
        int generatedEnemies = 0;

        for (int i = 0; i < selectedCount && availablePositions.Count > 0; i++)
        {
            int positionIndex = rng.Next(0, availablePositions.Count);
            Vector2 spawnPosition = availablePositions[positionIndex];
            availablePositions.RemoveAt(positionIndex);

            GameObject enemyObject = UnitsManager.Instance.CreateUnitById(selectedTemplate.UnitId, spawnPosition, false);
            if (enemyObject == null) continue;

            Stats enemyStats = enemyObject.GetComponent<Stats>();
            int realOverall = selectedTemplate.EstimatedOverall;
            if (enemyStats != null)
            {
                realOverall = enemyStats.CalculateOverall();
                enemyStats.Overall = realOverall;
            }

            generatedOverall += Mathf.Max(1, realOverall);
            generatedEnemies++;
        }

        GridManager.Instance.CheckTileOccupancy();
        InitiativeQueueManager.Instance.UpdateInitiativeQueue();
        InitiativeQueueManager.Instance.CalculateDominance();

        Debug.Log($"<color=green>Wygenerowano przeciwników: {generatedEnemies}, typ: {selectedTemplate.Race}, łączny Overall: {generatedOverall}, cel: {targetOverall} (+/- {toleranceAbs}).</color>");
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
                EstimatedOverall = estimatedOverall
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
            target.Quality = "Zwykla";
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









