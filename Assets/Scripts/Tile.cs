using UnityEngine;

public class Tile : MonoBehaviour
{
    [SerializeField] private Color _baseColor, _firstColor, _secondColor, _highlightColor, _rangeColor, _highlightRangeColor;
    private Renderer _renderer;
    public bool IsOccupied;

    private GameObject _tileFilling; //Wypełnienie pola kolorem

    private void Awake()
    {
        _tileFilling = transform.GetChild(0).gameObject;
        _renderer = _tileFilling.GetComponent<Renderer>();
    }

    public void Init(bool isOffset)
    {
        _renderer.material.color = isOffset ? _secondColor : _firstColor;
        _baseColor = _renderer.material.color;
        _rangeColor = _baseColor * 0.9f;
        _highlightRangeColor = Color.Lerp(_baseColor, Color.white, 0.3f);

        MakeTilesTransparent();
    }

    public void MakeTilesTransparent()
    {
        _renderer.material.color = Color.white * 0.3f;
        _baseColor = _renderer.material.color;
        _rangeColor = _firstColor * 0.6f;
        _highlightColor = _firstColor;
        _highlightRangeColor = _secondColor;
    }

    private void OnMouseEnter()
    {
        if(SaveAndLoadManager.Instance != null && SaveAndLoadManager.Instance.IsLoading) return; //Zapobiega podświetlaniu pól podczas wczytywania gry

        HighlightTile();

        if (_renderer.material.color == _highlightRangeColor 
            && Unit.SelectedUnit != null
            && !MovementManager.Instance.IsMoving 
            && !MagicManager.IsTargetSelecting 
            && !GameManager.IsAutoCombatMode 
            && (Unit.SelectedUnit.GetComponent<Unit>().CanMove || Unit.SelectedUnit.GetComponent<Unit>().IsRunning || Unit.SelectedUnit.GetComponent<Unit>().IsFlying))
        {
            MovementManager.Instance.HighlightPath(Unit.SelectedUnit, this.gameObject);
        }

        //Podświetla pola w obszarze działania zaklęcia
        if (MagicManager.IsTargetSelecting && Unit.SelectedUnit != null)
        {
            // Ustala obszar działania zaklęcia.
            if (Unit.SelectedUnit.GetComponent<Spell>().AreaSize > 0)
            {
                GridManager.Instance.HighlightTilesInSpellArea(this.gameObject);
            }
        }
    }

    private void OnMouseExit()
    {
        ResetTileHighlight();
    }

    private void OnMouseUp()
    {
        if(GameManager.IsMapHidingMode || UnitsManager.IsMultipleUnitsSelecting || UnitsManager.IsUnitRemoving || GameManager.Instance.IsPointerOverUI()) return;
        
        if(UnitsManager.IsTileSelecting == true)
        {
            if(IsOccupied)
            {
                Debug.Log("Wybrane pole jest zajęte. Nie można utworzyć nowej jednostki.");
                return;
            }
            
            //Tworzy jednostkę na klikniętym polu 
            UnitsManager.Instance.CreateUnitOnSelectedTile(this.gameObject.transform.position);
            return;
        }

        if(Unit.SelectedUnit != null)
        {
            if (!GameManager.IsAutoCombatMode || (Unit.SelectedUnit.CompareTag("PlayerUnit") && GameManager.IsStatsHidingMode))
            {
                Unit unit = Unit.SelectedUnit.GetComponent<Unit>();

                if (unit.IsCharging)
                {
                    Debug.Log("Wybierz przeciwnika, na którego chcesz zaszarżować.");
                    return;
                }

                if (MagicManager.IsTargetSelecting)
                {
                    //StartCoroutine(MagicManager.Instance.CastSpell(this.gameObject));
                    MagicManager.Instance.Targets.Add(this.gameObject);
                    return;
                }

                //Wykonuje ruch na kliknięte pole
                MovementManager.Instance.MoveSelectedUnit(this.gameObject, Unit.SelectedUnit);
            }
            else if (!GameManager.IsGamePaused)
            {
                Debug.Log("Aby poruszać się jednostkami, musisz wyłączyć tryb automatycznej walki.");
            }
        }
    }

    private void OnMouseOver()
    {
        if (Input.GetMouseButtonDown(1) && Unit.SelectedUnit != null && MagicManager.IsTargetSelecting)
        {
            //StartCoroutine(MagicManager.Instance.CastSpell(this.gameObject));
            MagicManager.Instance.Targets.Add(this.gameObject);
        }

        // Sprawdzamy, czy można umieszczać elementy na mapie
        if (!GameManager.IsMousePressed ||
            GameManager.Instance.IsPointerOverUI() ||
            DraggableObject.IsDragging) return;

        if (MapEditor.IsElementRemoving)
        {
            MapEditor.Instance.RemoveElement(gameObject);
        }
        else if(MapEditor.IsElementPlacing)
        {
            Vector3 position = new Vector3(transform.position.x, transform.position.y, 1);
            MapEditor.Instance.PlaceElementOnSelectedTile(position);
        }
    }

    public void HighlightTile()
    {
        _tileFilling.SetActive(true);

        if (_renderer.material.color == _baseColor)
        {
            _renderer.material.color = _highlightColor;
        }
        else if (_renderer.material.color == _rangeColor)
        {
            _renderer.material.color = _highlightRangeColor;
        }
    }
    public void ResetTileHighlight()
    {
        if(_renderer.material.color == _highlightColor)
        {
            _renderer.material.color = _baseColor;
            _tileFilling.SetActive(false);
        }
        // else if (_renderer.material.color == _highlightRangeColor)
        //     _renderer.material.color = _rangeColor;

        //Resetuje podświetlenie teoretycznej ścieżki postaci. NIE WIEM, CZY TEN SPOSÓB JEST NAJBARDZIEJ OPTYMALNY
        foreach (var tile in GridManager.Instance.Tiles)
        {
            if (tile._renderer.material.color == tile._highlightRangeColor)
            {
                tile._renderer.material.color = tile._rangeColor;
            }
        }
    }

    public void SetRangeColor()
    {
        ResetRangeColor();

        if (Unit.SelectedUnit != null)
        {
            _tileFilling.SetActive(true);
            _renderer.material.color = _rangeColor;
        }
    }

    public void ResetRangeColor()
    {      
        _renderer.material.color = _baseColor;
        _tileFilling.SetActive(false);
    }
}
