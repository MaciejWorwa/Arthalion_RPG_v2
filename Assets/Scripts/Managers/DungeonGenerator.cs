using System;
using System.Collections.Generic;
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

    private void RemoveUnitsOutsideDungeonIfNeeded()
    {
        if (!_removeUnitsOutsideDungeon || UnitsManager.Instance == null) return;

        List<Unit> unitsToRemove = new List<Unit>();

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
                unitsToRemove.Add(unit);
            }
        }

        foreach (Unit unit in unitsToRemove)
        {
            UnitsManager.Instance.DestroyUnit(unit.gameObject);
        }

        if (unitsToRemove.Count > 0)
        {
            Debug.Log($"Usunięto {unitsToRemove.Count} jednostek spoza wygenerowanego dungeonu.");
        }
    }
}
