using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;

public class InitiativeQueueManager : MonoBehaviour
{
    // Prywatne statyczne pole przechowujÄ…ce instancjÄ™
    private static InitiativeQueueManager instance;

    // Publiczny dostÄ™p do instancji
    public static InitiativeQueueManager Instance
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
            // JeĹ›li instancja juĹĽ istnieje, a prĂłbujemy utworzyÄ‡ kolejnÄ…, niszczymy nadmiarowÄ…
            Destroy(gameObject);
        }
    }
    public Dictionary<Unit, int> InitiativeQueue = new Dictionary<Unit, int>();
    public Unit ActiveUnit;
    public Transform InitiativeScrollViewContent;
    public Transform PlayersCamera_InitiativeScrollViewContent;
    [SerializeField] private GameObject _initiativeOptionPrefab; // Prefab odpowiadajÄ…cy kaĹĽdej jednostce na liĹ›cie inicjatywy
    private Color _defaultColor = new Color(0f, 0f, 0f, 0f); // DomyĹ›lny kolor przycisku
    private Color _selectedColor = new Color(0f, 0f, 0f, 0.5f); // Kolor wybranego przycisku (zaznaczonej jednostki)
    private Color _activeColor = new Color(0.15f, 1f, 0.45f, 0.2f); // Kolor aktywnego przycisku (jednostka, ktĂłrej tura obecnie trwa)
    private Color _selectedActiveColor = new Color(0.08f, 0.5f, 0.22f, 0.5f); // Kolor wybranego przycisku, gdy jednoczeĹ›nie jest to aktywna jednostka
    public UnityEngine.UI.Slider DominanceBar; // Pasek przewagi siĹ‚ w bitwie

    private Coroutine _selectUnitByQueueCoroutine;
    #region Initiative queue
    public void AddUnitToInitiativeQueue(Unit unit)
    {
        //Nie dodaje do kolejki inicjatywy jednostek, ktĂłre sÄ… ukryte
        Collider2D collider = Physics2D.OverlapPoint(unit.gameObject.transform.position);
        if (collider.CompareTag("TileCover") || InitiativeQueue.ContainsKey(unit)) return;

        InitiativeQueue.Add(unit, unit.GetComponent<Stats>().Initiative);

        //Aktualizuje pasek przewagi w bitwie
        unit.GetComponent<Stats>().Overall = unit.GetComponent<Stats>().CalculateOverall();

        CalculateDominance();
    }

    public void RemoveUnitFromInitiativeQueue(Unit unit)
    {
        if (!InitiativeQueue.ContainsKey(unit)) return;

        InitiativeQueue.Remove(unit);

        //Aktualizuje pasek przewagi w bitwie
        unit.GetComponent<Stats>().Overall = unit.GetComponent<Stats>().CalculateOverall();
        CalculateDominance();
    }

    public void UpdateInitiativeQueue()
    {
        //Sortowanie malejÄ…co wedĹ‚ug wartoĹ›ci inicjatywy
        InitiativeQueue = InitiativeQueue.OrderByDescending(pair => pair.Value).ToDictionary(pair => pair.Key, pair => pair.Value);

        DisplayInitiativeQueue();
    }

    private void DisplayInitiativeQueue()
    {
        // Resetuje wyĹ›wietlanÄ… kolejkÄ™, usuwajÄ…c wszystkie obiekty "dzieci"
        ResetScrollViewContent(InitiativeScrollViewContent);
        ResetScrollViewContent(PlayersCamera_InitiativeScrollViewContent);

        ActiveUnit = null;

        // Ustala wyĹ›wietlanÄ… kolejkÄ™ inicjatywy
        foreach (var pair in InitiativeQueue)
        {
            // Dodaje jednostkÄ™ do gĹ‚Ăłwnej kolejki ScrollViewContent
            GameObject optionObj = CreateInitiativeOption(pair, InitiativeScrollViewContent);

            // Dodaje jednostkÄ™ do Players kolejki ScrollViewContent
            GameObject playersOptionObj = CreateInitiativeOption(pair, PlayersCamera_InitiativeScrollViewContent);

            // Sprawdza, czy jest aktywna tura dla tej jednostki
            if ((pair.Key.CanDoAction || pair.Key.CanMove) && ActiveUnit == null && pair.Key.IsTurnFinished != true)
            {
                ActiveUnit = pair.Key;
                SetOptionColor(optionObj, _activeColor);
                SetOptionColor(playersOptionObj, _activeColor);
            }

            // WyrĂłĹĽnia zaznaczonÄ… jednostkÄ™
            if (Unit.SelectedUnit != null && pair.Key == Unit.SelectedUnit.GetComponent<Unit>())
            {
                Color selectedColor = pair.Key == ActiveUnit ? _selectedActiveColor : _selectedColor;
                SetOptionColor(optionObj, selectedColor);
                SetOptionColor(playersOptionObj, selectedColor);
            }
            else if (pair.Key != ActiveUnit)
            {
                SetOptionColor(optionObj, _defaultColor);
                SetOptionColor(playersOptionObj, _defaultColor);
            }
        }

        // Aktualiuje listÄ™ dostÄ™pnych do wyboru wierzchowcĂłw
        MountsManager.Instance.DisplayMountsList();
    }

    private void ResetScrollViewContent(Transform scrollViewContent)
    {
        for (int i = scrollViewContent.childCount - 1; i >= 0; i--)
        {
            Transform child = scrollViewContent.GetChild(i);
            Destroy(child.gameObject);
        }
    }

    private GameObject CreateInitiativeOption(KeyValuePair<Unit, int> pair, Transform scrollViewContent)
    {
        GameObject optionObj = Instantiate(_initiativeOptionPrefab, scrollViewContent);

        // Odniesienie do nazwy postaci
        TextMeshProUGUI nameText = optionObj.transform.Find("Name_Text").GetComponent<TextMeshProUGUI>();
        nameText.text = pair.Key.GetComponent<Stats>().Name;

        // Odniesienie do wartoĹ›ci inicjatywy
        TextMeshProUGUI initiativeText = optionObj.transform.Find("Initiative_Text").GetComponent<TextMeshProUGUI>();
        initiativeText.text = pair.Value.ToString();

        return optionObj;
    }

    private void SetOptionColor(GameObject optionObj, Color color)
    {
        optionObj.GetComponent<UnityEngine.UI.Image>().color = color;
    }

    public void CancelPendingSelectUnitByQueue()
    {
        if (_selectUnitByQueueCoroutine != null)
        {
            StopCoroutine(_selectUnitByQueueCoroutine);
            _selectUnitByQueueCoroutine = null;
        }
    }

    public void SelectUnitByQueue()
    {
        CancelPendingSelectUnitByQueue();
        _selectUnitByQueueCoroutine = StartCoroutine(InvokeSelectUnitCoroutine());
    }

    private IEnumerator InvokeSelectUnitCoroutine()
    {
        yield return new WaitForSeconds(0.05f);

        // Wait until current unit finishes movement/combat sequence.
        while ((MovementManager.Instance != null && MovementManager.Instance.IsMoving)
            || (DiceRollManager.Instance != null && DiceRollManager.Instance.IsWaitingForRoll)
            || (CombatManager.Instance != null && CombatManager.Instance.IsAttackSequenceRunning))
        {
            yield return null; // wait next frame
        }

        DisplayInitiativeQueue();

        // Auto-select active unit; if none is active, try advancing to next round safely.

        if (GameManager.IsAutoSelectUnitMode && ActiveUnit != null && ActiveUnit.gameObject != Unit.SelectedUnit)
        {
            ActiveUnit.SelectUnit();
        }
        else if (GameManager.IsAutoSelectUnitMode && ActiveUnit == null)
        {
            while (DiceRollManager.Instance != null && DiceRollManager.Instance.IsWaitingForRoll)
            {
                yield return null;
            }

            yield return new WaitForSeconds(0.05f);
            RoundsManager.Instance?.TryAdvanceRoundIfComplete();
        }

        _selectUnitByQueueCoroutine = null;
    }
    #endregion

    public void CalculateDominance()
    {
        int playerTotal = 0;
        int enemyTotal = 0;

        // Przechodzimy przez caĹ‚Ä… kolejkÄ™ inicjatywy i sumujemy "Overall" dla obu stron
        foreach (var unit in InitiativeQueue.Keys)
        {
            Stats unitStats = unit.GetComponent<Stats>();

            if (unit.CompareTag("PlayerUnit"))
                playerTotal += unitStats.Overall;
            else if (unit.CompareTag("EnemyUnit"))
                enemyTotal += unitStats.Overall;
        }

        int totalPower = playerTotal + enemyTotal;
        if (totalPower == 0)
        {
            DominanceBar.maxValue = 1; // Zapobiega dzieleniu przez 0
            DominanceBar.value = 0;
            DominanceBar.gameObject.SetActive(false);
            return;
        }

        DominanceBar.maxValue = totalPower;
        DominanceBar.value = playerTotal;

        // Aktywujemy pasek, jeĹ›li ma sens go wyĹ›wietlaÄ‡
        if (DominanceBar.maxValue > 1 && !DominanceBar.gameObject.activeSelf && !GameManager.IsStatsHidingMode)
        {
            DominanceBar.gameObject.SetActive(true);
        }
    }
}
