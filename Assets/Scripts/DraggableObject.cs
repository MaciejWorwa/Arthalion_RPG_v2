using UnityEngine;

public class DraggableObject : MonoBehaviour
{
    private Vector3 _offset; // Przechowuje różnicę między pozycją obiektu a pozycją kursora
    public static bool IsDragging = false;
    private Camera _mainCamera;
    private Vector3 _startPosition; // Pozycja przed przesunięciem
    public static DraggableObject CurrentlyDragging = null;
    private static DraggableObject _inputDriver;


        private void Start()
    {
        _mainCamera = Camera.main;

        if (_inputDriver == null)
        {
            _inputDriver = this;
        }
    }

    private void OnDestroy()
    {
        if (_inputDriver == this)
        {
            _inputDriver = null;
            var objects = FindObjectsByType<DraggableObject>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] != null)
                {
                    _inputDriver = objects[i];
                    break;
                }
            }
        }

        if (CurrentlyDragging == this)
        {
            CurrentlyDragging = null;
            IsDragging = false;
        }
    }

        void Update()
    {
        if (_inputDriver != null && _inputDriver != this)
        {
            return;
        }

        if (_inputDriver == null)
        {
            _inputDriver = this;
        }

        if (Input.GetMouseButtonDown(0) && !GameManager.Instance.IsPointerOverUI())
        {
            DraggableObject obj = GetDraggableObjectUnderMouse();
            if (obj != null)
            {
                // Rozpoczynamy przeciąganie wybranego obiektu
                CurrentlyDragging = obj;
                CurrentlyDragging.BeginDrag();
            }
        }

        // Aktualizacja pozycji dla przeciąganego obiektu
        if (CurrentlyDragging != null && Input.GetMouseButton(0))
        {
            CurrentlyDragging.UpdateDrag();
        }

        // Zakończenie przeciągania
        if (Input.GetMouseButtonUp(0) && CurrentlyDragging != null)
        {
            CurrentlyDragging.EndDrag();
            CurrentlyDragging = null;
        }
    }

        private DraggableObject GetDraggableObjectUnderMouse()
    {
        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null)
            {
                return null;
            }
        }

        Vector3 mousePos = Input.mousePosition;
        Ray ray = _mainCamera.ScreenPointToRay(mousePos);
        RaycastHit2D[] hits = Physics2D.GetRayIntersectionAll(ray);

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit2D hit = hits[i];
            if (hit.collider == null) continue;

            DraggableObject draggable = hit.collider.GetComponent<DraggableObject>();
            if (draggable != null)
            {
                return draggable;
            }
        }

        return null;
    }

    public void BeginDrag()
    {
        if(GameManager.IsMapHidingMode || MapEditor.IsElementRemoving || MapEditor.IsElementPlacing || UnitsManager.IsMultipleUnitsSelecting || (MovementManager.Instance != null && MovementManager.Instance.IsMoving))
            return;

        IsDragging = true;
        _startPosition = transform.position;
        _offset = transform.position - GetMouseWorldPosition();
    }

    public void UpdateDrag()
    {
        if (!IsDragging) return;

        Vector3 newPosition = GetMouseWorldPosition() + _offset;

        newPosition.z = 0;

        transform.position = newPosition;
    }

    public void EndDrag()
    {
        if(GameManager.IsMapHidingMode || MapEditor.IsElementRemoving || MapEditor.IsElementPlacing || UnitsManager.IsMultipleUnitsSelecting || (MovementManager.Instance != null && MovementManager.Instance.IsMoving))
            return;

        if (transform.position != _startPosition)
        {
            SnapToGrid();
        }
        IsDragging = false;
    }

    private Vector3 GetMouseWorldPosition()
    {
        if (_mainCamera == null)
        {
            _mainCamera= Camera.main;
        }

        // Pobiera pozycję myszy w świecie gry
        Vector3 mouseScreenPosition = Input.mousePosition;
        mouseScreenPosition.z = _mainCamera.WorldToScreenPoint(transform.position).z;
        return _mainCamera.ScreenToWorldPoint(mouseScreenPosition);
    }

    private bool SnapToGrid()
    {
        // Sprawdzamy, czy przesuwany obiekt jest jednostką
        Unit unit = GetComponent<Unit>();

        // Odznaczamy jednostkę, którą przesuwamy
        if (unit != null && Unit.SelectedUnit == this.gameObject)
        {
            unit.SelectUnit();
        }

        Vector2 offset = Vector2.zero; // Zmienna do przechowywania przesunięcia obiektu

        if (GetComponent<MapElement>() != null) // Sprawdzamy, czy obiekt ma komponent MapElement
        {
            BoxCollider2D boxCollider = GetComponent<BoxCollider2D>(); // Pobieramy BoxCollider2D, aby poznać wymiary obiektu

            // Sprawdzamy, czy obiekt zajmuje dwa pola w pionie (wyższy niż szerszy)
            if (boxCollider.size.y > boxCollider.size.x)
            {
                float rotationZ = transform.eulerAngles.z; // Pobieramy wartość kąta obrotu obiektu w stopniach

                // Sprawdzamy, w jakim zakresie znajduje się kąt obrotu, aby odpowiednio ustawić offset
                if (rotationZ < 45 || (rotationZ >= 135 && rotationZ < 225) || rotationZ > 315)
                {
                    offset = new Vector2(0, 0.5f); // Przesunięcie w górę
                    Collider2D pointCollider = Physics2D.OverlapPoint(transform.position + (Vector3)offset);
                    if (pointCollider != null && !pointCollider.CompareTag("Tile") && CountOccupyingObjects(pointCollider) >= 3)
                    {
                        transform.position = _startPosition; // Jeśli miejsce jest zajęte, wracamy na początkową pozycję
                        return false; // Nie możemy umieścić obiektu w tym miejscu
                    }
                }
                else
                {
                    offset = new Vector2(-0.5f, 0); // Przesunięcie w lewo
                    Collider2D pointCollider = Physics2D.OverlapPoint(transform.position + (Vector3)offset);
                    if (pointCollider != null && !pointCollider.CompareTag("Tile") && CountOccupyingObjects(pointCollider) >= 3)
                    {
                        transform.position = _startPosition;
                        return false;
                    }
                }
            }
            // Sprawdzamy, czy obiekt zajmuje dwa pola w poziomie (szerszy niż wyższy)
            else if (boxCollider.size.y < boxCollider.size.x)
            {
                float rotationZ = transform.eulerAngles.z;

                // Sprawdzamy kąt obrotu i odpowiednio zmieniamy offset
                if ((rotationZ >= 45 && rotationZ < 135) || (rotationZ >= 225) && rotationZ < 315)
                {
                    offset = new Vector2(0, 0.5f); // Przesunięcie w górę
                    Collider2D pointCollider = Physics2D.OverlapPoint(transform.position + (Vector3)offset);
                    if (pointCollider != null && !pointCollider.CompareTag("Tile") && CountOccupyingObjects(pointCollider) >= 3)
                    {
                        transform.position = _startPosition;
                        return false;
                    }
                }
                else
                {
                    offset = new Vector2(-0.5f, 0); // Przesunięcie w lewo
                    Collider2D pointCollider = Physics2D.OverlapPoint(transform.position + (Vector3)offset);
                    if (pointCollider != null && !pointCollider.CompareTag("Tile") && CountOccupyingObjects(pointCollider) >= 3)
                    {
                        transform.position = _startPosition;
                        return false;
                    }
                }
            }
            // Sprawdzamy, czy obiekt zajmuje cztery pola
            else if (transform.localScale.x > 1.5f || (boxCollider.size.y > 1.7f && boxCollider.size.x > 1.7f))
            {
                offset = new Vector2(-0.5f, 0.5f); // Przesunięcie o połowę w lewo i w górę

                // Sprawdzamy wszystkie pola zajmowane przez obiekt
                Vector2[] positionsToCheck = new Vector2[]
                {
                    transform.position + (Vector3)offset, // Pole 1
                    transform.position - (Vector3)offset, // Pole 2
                    transform.position + new Vector3(offset.x, -offset.y), // Pole 3
                    transform.position + new Vector3(-offset.x, offset.y) // Pole 4
                };

                foreach (var pos in positionsToCheck)
                {
                    Collider2D pointCollider = Physics2D.OverlapPoint(pos);

                    // Jeśli pole jest zajęte przez więcej niż 3 obiekty, wracamy na początkową pozycję
                    if (pointCollider != null && !pointCollider.CompareTag("Tile") && CountOccupyingObjects(pointCollider) >= 3)
                    {
                        transform.position = _startPosition;
                        return false;
                    }
                }
            }
        }

        // Sprawdzamy, które pola w pobliżu są zajęte
        Collider2D[] colliders = Physics2D.OverlapPointAll(transform.position);
        bool hasTileCover = false;

        foreach (var collider in colliders)
        {
            if (collider != null)
            {
                // Sprawdzamy, czy którykolwiek z colliderów ma tag TileCover
                if (collider.CompareTag("TileCover"))
                {
                    hasTileCover = true;
                    continue;
                }

                // Jeśli collider ma tag Tile, kontynuujemy sprawdzanie
                if (collider.CompareTag("Tile"))
                {
                    Tile tile = collider.GetComponent<Tile>();

                    // Zliczamy liczbę obiektów na tym samym polu
                    int occupyingCount = CountOccupyingObjects(collider);

                    if (tile != null && occupyingCount < 3) // Sprawdzamy, czy pole nie jest zajęte przez więcej niż 2 obiekty
                    {
                        // Przesuwamy obiekt na środek pustego pola
                        transform.position = (Vector2)collider.transform.position + offset;

                        // Jeśli element ma komponent MapElement
                        MapElement mapElement = GetComponent<MapElement>();

                        // Sprawdzamy, czy MapElement ma IsCollider = true
                        if (mapElement != null)
                        {
                            // Ustawiamy z na 1, jeśli IsCollider == true, w przeciwnym razie na 2.7f
                            float zPosition = mapElement.IsCollider ? 1f : 2.7f;

                            // Ustawiamy pozycję "z" uwzględniając liczbę obiektów
                            transform.position = new Vector3(transform.position.x, transform.position.y, zPosition - (occupyingCount * 0.05f));
                        }
                        else
                        {
                            // Jeśli nie ma komponentu MapElement, domyślnie ustawiamy "z" na 0f
                            transform.position = new Vector3(transform.position.x, transform.position.y, 0f);
                        }

                        Physics2D.SyncTransforms(); // Synchronizujemy transformacje obiektu

                        // Aktualizowanie zajętości pól
                        GridManager.Instance.CheckTileOccupancy();

                        // Jeśli jednostka jest zaznaczona, aktualizujemy dostępne pola w zasięgu ruchu
                        if (Unit.SelectedUnit != null)
                        {
                            GridManager.Instance.HighlightTilesInMovementRange(Unit.SelectedUnit.GetComponent<Stats>());
                        }

                        // Jeżeli jednostka jest na polu z TileCover, usuwamy ją z kolejki inicjatywy
                        if (GetComponent<Unit>() != null && hasTileCover)
                        {
                            InitiativeQueueManager.Instance.RemoveUnitFromInitiativeQueue(GetComponent<Unit>());
                            InitiativeQueueManager.Instance.UpdateInitiativeQueue();
                        }
                        // Jeżeli jednostka jest na polu odsłoniętym, dodajemy ją do kolejki inicjatywy (jeśli jeszcze w niej nie jest)
                        else if (GetComponent<Unit>() != null && !InitiativeQueueManager.Instance.InitiativeQueue.ContainsKey(GetComponent<Unit>()))
                        {
                            InitiativeQueueManager.Instance.AddUnitToInitiativeQueue(GetComponent<Unit>());
                            InitiativeQueueManager.Instance.UpdateInitiativeQueue();
                            InitiativeQueueManager.Instance.SelectUnitByQueue();
                        }

                        return true; // Sukces, udało się umieścić obiekt
                    }
                }
            }
        }

        // Jeśli pole jest zajęte, wracamy na początkową pozycję
        transform.position = _startPosition;
        return false; // Nie udało się umieścić obiektu
    }

    // Funkcja zliczająca liczbę obiektów na danym polu
    private int CountOccupyingObjects(Collider2D pointCollider)
    {
        Collider2D[] collidersAtPoint = Physics2D.OverlapPointAll(pointCollider.transform.position);
        int occupiedCount = 0;

        foreach (var collider in collidersAtPoint)
        {
            if (collider != null && collider.CompareTag("MapElement"))
            {
                occupiedCount++;
                continue;
            }
        }

        return occupiedCount - 1;
    }
}


