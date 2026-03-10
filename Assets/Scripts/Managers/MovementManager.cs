using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using static UnityEngine.GraphicsBuffer;

public class MovementManager : MonoBehaviour
{
    // Prywatne statyczne pole przechowujące instancję
    private static MovementManager instance;

    // Publiczny dostęp do instancji
    public static MovementManager Instance
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
    [HideInInspector] public bool IsMoving;
    [SerializeField] private Button _runButton;
    [SerializeField] private Button _flightButton;
    [SerializeField] private Button _retreatButton;
    [SerializeField] private Toggle _canMoveToggle;

    [Header("Panel do manualnego zarządzania sposobem odwrotu")]
    [SerializeField] private GameObject _retreatPanel;
    [SerializeField] private UnityEngine.UI.Button _advantageButton;
    [SerializeField] private UnityEngine.UI.Button _dodgeButton;
    private string _retreatWay;
    private static readonly Vector2[] PathDirections = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };

    void Start()
    {
        _dodgeButton.onClick.AddListener(() => RetreatWayButtonClick("dodge"));
        _advantageButton.onClick.AddListener(() => RetreatWayButtonClick("advantage"));
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape) && _retreatPanel.activeSelf)
        {
            Retreat(false);
        }
    }

    private void RetreatWayButtonClick(string retreatWay)
    {
        _retreatWay = retreatWay;
    }

    #region Move functions
    public void MoveSelectedUnit(GameObject selectedTile, GameObject unitGameObject)
    {
        // Nie pozwala wykonać akcji ruchu, dopóki poprzedni ruch nie zostanie zakończony. Sprawdza też, czy gra nie jest wstrzymana (np. poprzez otwarcie dodatkowych paneli)
        if (IsMoving == true || GameManager.IsGamePaused) return;

        Unit unit = unitGameObject.GetComponent<Unit>();

        if (!unit.CanMove && !unit.IsRunning)
        {
            Debug.Log("Ta jednostka nie może wykonać ruchu w tej rundzie.");
            return;
        }
        else if (!unit.CanDoAction && unit.IsRunning)
        {
            Debug.Log("Ta jednostka nie może wykonać biegu w tej rundzie.");
            StartCoroutine(UpdateMovementRange(1));
            return;
        }

        // Jeśli jednostka pochwytująca inną wykonuje ruch, to pochwycenie zostane przerwane. Chyba, że jest ono w zasięgu broni, którą pochwytuje
        if (unit.EntangledUnitId != 0)
        {
            Weapon attackerWeapon = InventoryManager.Instance.ChooseWeaponToAttack(unit.gameObject);
            foreach (var u in UnitsManager.Instance.AllUnits)
            {
                if (u.UnitId == unit.EntangledUnitId && u.Entangled)
                { 
                    if (attackerWeapon != null && attackerWeapon.Type.Contains("entangling"))
                    {
                        float effectiveAttackRange = attackerWeapon.AttackRange;

                        if (attackerWeapon.Type.Contains("throwing"))
                        {
                            effectiveAttackRange *= unit.Stats.S / 10f;
                        }

                        float distance = Vector3.Distance(unit.transform.position, selectedTile.transform.position);

                        if (distance > effectiveAttackRange)
                        {
                            CombatManager.Instance.ReleaseEntangledUnit(unit, u, attackerWeapon);
                        }
                    }
                    else
                    {
                        // Jeśli broń nie jest typu "entangling", zawsze rozluźniamy chwyt
                        CombatManager.Instance.ReleaseEntangledUnit(unit, u);
                    }

                    break;
                }
            }
        }

        // Sprawdza zasięg ruchu postaci lub wierzchowca
        int movementRange = unit.GetComponent<Stats>().TempSz;

        // Pozycja postaci przed zaczęciem wykonywania ruchu
        Vector2 startCharPos = unit.transform.position;

        // Aktualizuje informację o zajęciu pola, które postać opuszcza
        GridManager.Instance.ResetTileOccupancy(startCharPos);

        // Pozycja pola wybranego jako cel ruchu
        Vector2 selectedTilePos = new Vector2(selectedTile.transform.position.x, selectedTile.transform.position.y);

        // Znajdź najkrótszą ścieżkę do celu
        List<Vector2> path = FindPath(startCharPos, selectedTilePos);

        // Sprawdza czy wybrane pole jest w zasięgu ruchu postaci. W przypadku automatycznej walki ten warunek nie jest wymagany.
        if (path.Count > 0 && (path.Count <= movementRange || GameManager.IsAutoCombatMode))
        {
            if (!unit.IsRunning && RoundsManager.RoundNumber != 0)
            {
                unit.CanMove = false;
                SetCanMoveToggle(false);

                if (!unit.IsRetreating) // Odwrót
                {
                    Debug.Log($"<color=green>{unit.GetComponent<Stats>().Name} wykonał/a ruch. </color>");
                }

                //Sprawdzamy, czy postać powinna zakończyć turę
                if (!unit.CanDoAction)
                {
                    RoundsManager.Instance.FinishTurn();
                }
            }
            else
            {
                RoundsManager.Instance.DoAction(unit);

                if (unit.IsRunning)
                {
                    StartCoroutine(UpdateMovementRange(1));
                }
            }

            // Oznacza wybrane pole jako zajęte (gdyż trochę potrwa, zanim postać tam dojdzie i gdyby nie zaznaczyć, to można na nie ruszyć inną postacią)
            selectedTile.GetComponent<Tile>().IsOccupied = true;

            //Zapobiega zaznaczeniu jako zajęte pola docelowego, do którego jednostka w trybie automatycznej walki niekoniecznie da radę dojść
            if (GameManager.IsAutoCombatMode)
            {
                AutoCombatManager.Instance.TargetTile = selectedTile.GetComponent<Tile>();
            }

            // Resetuje kolor pól w zasięgu ruchu na czas jego wykonywania
            GridManager.Instance.ResetColorOfTilesInMovementRange();

            //Sprawdza, czy ruch powoduje ataki okazyjne
            CombatManager.Instance.CheckForOpportunityAttack(unitGameObject, selectedTilePos);

            // Wykonuje pojedynczy ruch tyle razy ile wynosi zasięg ruchu postaci
            StartCoroutine(MoveWithDelay(unitGameObject, path, movementRange));
        }
        else
        {
            Debug.Log("Wybrane pole jest poza zasięgiem ruchu lub jest zajęte.");
        }
    }

    private IEnumerator MoveWithDelay(GameObject unitGameObject, List<Vector2> path, int movementRange)
    {
        // Ogranicz iterację do mniejszej wartości: movementRange lub liczby elementów w liście path
        int iterations = Mathf.Min(movementRange, path.Count);

        for (int i = 0; i < iterations; i++)
        {
            Vector2 nextPos = path[i];

            float elapsedTime = 0f;
            float duration = 0.2f; // Czas trwania interpolacji

            while (elapsedTime < duration && unitGameObject != null && !ReinforcementLearningManager.Instance.IsLearning)
            {
                IsMoving = true;

                unitGameObject.transform.position = Vector2.Lerp(unitGameObject.transform.position, nextPos, elapsedTime / duration);
                elapsedTime += Time.deltaTime;
                yield return null; // Poczekaj na odświeżenie klatki animacji
            }

            //Na wypadek, gdyby w wyniku ataku okazyjnego podczas ruchu jednostka została zabita i usunięta
            if (unitGameObject == null)
            {
                IsMoving = false;
                yield break;
            }

            unitGameObject.transform.position = nextPos;
        }

        if ((Vector2)unitGameObject.transform.position == path[iterations - 1])
        {
            IsMoving = false;
            Retreat(false);

            if (Unit.SelectedUnit != null)
            {
                GridManager.Instance.HighlightTilesInMovementRange(Unit.SelectedUnit.GetComponent<Stats>());
            }
        }

        //Zaznacza jako zajęte faktyczne pole, na którym jednostka zakończy ruch, a nie pole do którego próbowała dojść
        if (GameManager.IsAutoCombatMode || ReinforcementLearningManager.Instance.IsLearning)
        {
            AutoCombatManager.Instance.CheckForTargetTileOccupancy(unitGameObject);
        }
    }

    public List<Vector2> FindPath(Vector2 start, Vector2 goal)
    {
        bool isFlyingUnit = Unit.SelectedUnit != null && Unit.SelectedUnit.GetComponent<Unit>().IsFlying;

        List<Node> openNodes = new List<Node>();
        Dictionary<Vector2, Node> openNodesByPosition = new Dictionary<Vector2, Node>();
        HashSet<Vector2> closedNodes = new HashSet<Vector2>();

        int startH = CalculateDistance(start, goal);
        Node startNode = new Node
        {
            Position = start,
            G = 0,
            H = startH,
            F = startH,
            Parent = null
        };

        openNodes.Add(startNode);
        openNodesByPosition[start] = startNode;

        while (openNodes.Count > 0)
        {
            int minIndex = 0;
            Node current = openNodes[0];
            for (int i = 1; i < openNodes.Count; i++)
            {
                Node candidate = openNodes[i];
                if (candidate.F < current.F)
                {
                    minIndex = i;
                    current = candidate;
                }
            }

            openNodes.RemoveAt(minIndex);
            openNodesByPosition.Remove(current.Position);
            closedNodes.Add(current.Position);

            if (current.Position == goal)
            {
                List<Vector2> path = new List<Vector2>();
                Node node = current;

                while (node.Position != start)
                {
                    path.Add(node.Position);
                    node = node.Parent;
                }

                path.Reverse();
                return path;
            }

            for (int i = 0; i < PathDirections.Length; i++)
            {
                Vector2 neighborPosition = current.Position + PathDirections[i];

                if (closedNodes.Contains(neighborPosition))
                {
                    continue;
                }

                Collider2D collider = Physics2D.OverlapPoint(neighborPosition);
                if (collider == null)
                {
                    continue;
                }

                bool isTile = collider.CompareTag("Tile") && !collider.gameObject.GetComponent<Tile>().IsOccupied;
                if (!isTile && !isFlyingUnit)
                {
                    continue;
                }

                int gCost = current.G + 1;

                if (openNodesByPosition.TryGetValue(neighborPosition, out Node existingNode))
                {
                    if (gCost < existingNode.G)
                    {
                        existingNode.G = gCost;
                        existingNode.F = existingNode.G + existingNode.H;
                        existingNode.Parent = current;
                    }
                }
                else
                {
                    int hCost = CalculateDistance(neighborPosition, goal);
                    Node newNode = new Node
                    {
                        Position = neighborPosition,
                        G = gCost,
                        H = hCost,
                        F = gCost + hCost,
                        Parent = current
                    };

                    openNodes.Add(newNode);
                    openNodesByPosition[neighborPosition] = newNode;
                }
            }
        }

        return new List<Vector2>();
    }
    #endregion

    // Funkcja obliczająca odległość pomiędzy dwoma punktami na płaszczyźnie XY
    private int CalculateDistance(Vector2 a, Vector2 b)
    {
        return (int)(Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y));
    }

    #region Charge, run and flight modes
    public void Run()
    {
        if (Unit.SelectedUnit != null && Unit.SelectedUnit.GetComponent<Unit>().Prone)
        {
            Debug.Log("Jednostka w stanie powalenia nie może wykonywać biegu.");
            return;
        }

        if (Unit.SelectedUnit != null && Unit.SelectedUnit.GetComponent<Unit>().Entangled)
        {
            Debug.Log("Jednostka w stanie unieruchomienia nie może wykonywać biegu.");
            return;
        }

        //Uwzględnia cechę Długi Krok
        int modifier = 2;

        StartCoroutine(UpdateMovementRange(modifier));
        Retreat(false); // Zresetowanie bezpiecznego odwrotu
    }

    public void Flight()
    {
        if (Unit.SelectedUnit == null) return;
        if (Unit.SelectedUnit.GetComponent<Unit>().Prone)
        {
            Debug.Log("Jednostka w stanie powalenia nie może wykonywać lotu.");
            return;
        }

        int modifier = Unit.SelectedUnit.GetComponent<Stats>().Flight;

        StartCoroutine(UpdateMovementRange(modifier, null, false, true));
        Retreat(false); // Zresetowanie bezpiecznego odwrotu
    }

    public IEnumerator UpdateMovementRange(int modifier, Unit unit = null, bool isCharging = false, bool isFlying = false)
    {
        if (unit == null && Unit.SelectedUnit != null)
        {
            unit = Unit.SelectedUnit.GetComponent<Unit>();
        }

        if (unit == null) yield break;

        Stats stats = unit.GetComponent<Stats>();

        //Jeżeli postać już jest w trybie szarży, lotu lub biegu, resetuje je
        if ((isCharging && unit.IsCharging || unit.IsRunning && !isCharging || unit.IsFlying && isFlying) && modifier > 1)
        {
            modifier = 1;
        }

        stats.TempSz = stats.Sz;

        // Uwzględnienie szybkości wierzchowca
        if(unit.IsMounted && unit.Mount != null)
        {
            stats.TempSz = unit.Mount.GetComponent<Stats>().Sz;
            if (unit.Mount.GetComponent<Stats>().Flight != 0) unit.Stats.TempSz = unit.Mount.GetComponent<Stats>().Flight;
        }

        //Sprawdza, czy jednostka może wykonać bieg lub szarże
        if (modifier > 1 && !unit.CanDoAction && !isFlying)
        {
            Debug.Log("Ta jednostka nie może w tej rundzie wykonać więcej akcji.");
            yield break;
        }
        else if (modifier > 1 && stats.Slow && !isFlying)
        {
            Debug.Log("Ta jednostka nie może wykonywać akcji biegu.");
            yield break;
        }

        //Aktualizuje obecny tryb poruszania postaci
        unit.IsCharging = modifier > 1 && isCharging;
        unit.IsFlying = modifier > 1 && isFlying;
        unit.IsRunning = modifier > 1 && !isCharging && !isFlying? true : false;

        if (!unit.IsCharging)
        {
            CombatManager.Instance.ChangeAttackType("StandardAttack"); //Resetuje szarże jako obecny typ ataku i ustawia standardowy atak
        }

        if (unit.IsRunning)
        {
            int rollResult = 0;
            int[] runTest = null;
            if (!GameManager.IsAutoDiceRollingMode && stats.CompareTag("PlayerUnit"))
            {
                yield return StartCoroutine(DiceRollManager.Instance.WaitForRollValue(stats, "Atletykę", "Zw", "Athletics", callback: result => runTest = result));
                if (runTest == null) yield break;
            }
            else
            {
                runTest = DiceRollManager.Instance.TestSkill(stats, "Atletykę", "Zw", "Athletics");
            }
            rollResult = runTest[2];


            //Oblicza obecną szybkość
            stats.TempSz = Math.Max(stats.Sz, rollResult / 2);
        }
        else if(unit.IsFlying)
        {
            //Oblicza obecną szybkość
            stats.TempSz = modifier;
        }
        else
        {
            // Jednostki latające mogą podczas szarżu użyc lotu
            if(unit.IsCharging && stats.Flight != 0)
            {
                stats.TempSz = stats.Flight;
            }
            else
            {
                //Oblicza obecną szybkość
                stats.TempSz *= modifier;
            }
        }

        // Uwzględnia powalenie
        if (unit.Prone) stats.TempSz /= 2;

        ChangeButtonColor(modifier, unit.IsRunning, unit.IsFlying);

        // Aktualizuje podświetlenie pól w zasięgu ruchu
        GridManager.Instance.HighlightTilesInMovementRange(stats);
    }

    private void ChangeButtonColor(int modifier, bool isRunning, bool IsFlying)
    {
        //_chargeButton.GetComponent<Image>().color = modifier == 1 ? Color.white : modifier == 2 ? Color.green : Color.white;
        _runButton.GetComponent<Image>().color = modifier == 2 && isRunning ? Color.green : Color.white;
        _flightButton.GetComponent<Image>().color = modifier > 1 && IsFlying ? Color.green : Color.white;
    }

    public void ShowOrHideFlightButton(bool value)
    {
        _flightButton.gameObject.SetActive(value);
    }
    #endregion

    #region Retreat
    //Bezpieczny odwrót
    public void Retreat(bool value)
    {
        if (Unit.SelectedUnit == null) return;
        Unit unit = Unit.SelectedUnit.GetComponent<Unit>();

        if (value == true && (!unit.CanDoAction || !unit.CanMove)) //Sprawdza, czy jednostka może wykonać odwrót
        {
            Debug.Log("Ta jednostka nie może w tej rundzie wykonać odwrotu.");
            return;
        }

        unit.IsRetreating = value;
        _retreatButton.GetComponent<Image>().color = unit.IsRetreating ? Color.green : Color.white;
    }
    #endregion

    #region Highlight path
    public void HighlightPath(GameObject unit, GameObject tile)
    {
        var path = FindPath(unit.transform.position, new Vector2(tile.transform.position.x, tile.transform.position.y));

        if (path.Count <= unit.GetComponent<Stats>().TempSz)
        {
            foreach (Vector2 tilePosition in path)
            {
                Collider2D collider = Physics2D.OverlapPoint(tilePosition);
                if (collider == null || collider.GetComponent<Tile>() == null) continue;
                collider.GetComponent<Tile>().HighlightTile();
            }
        }
    }
    #endregion

    public void SetCanMoveToggle(bool canMove)
    {
        _canMoveToggle.isOn = canMove;
    }
    public void SetCanMoveByToggle()
    {
        if (Unit.SelectedUnit == null) return;
        Unit.SelectedUnit.GetComponent<Unit>().CanMove = _canMoveToggle.isOn;

        GridManager.Instance.HighlightTilesInMovementRange(Unit.SelectedUnit.GetComponent<Stats>());
    }
}

public class Node
{
    public Vector2 Position; // Pozycja węzła na siatce
    public int G; // Koszt dotarcia do węzła
    public int H; // Szacowany koszt dotarcia z węzła do celu
    public int F; // Całkowity koszt (G + H)
    public Node Parent; // Węzeł nadrzędny w ścieżce
}



