using UnityEngine;

public class MapElement : MonoBehaviour
{
    public bool IsHighObstacle;
    public bool IsLowObstacle;
    public bool IsCollider;

    void Awake()
    {
        DontDestroyOnLoad(gameObject);
    }

    public void SetColliderState(bool state)
    {
        BoxCollider2D boxCollider = GetComponent<BoxCollider2D>();
        if (boxCollider != null)
        {
            boxCollider.enabled = state;
        }
    }

    private void OnMouseUp()
    {
        if (MapEditor.Instance == null) return;

        if (MapElementUI.SelectedElement != null)
        {
            MapEditor.Instance.PlaceElementOnSelectedTile(transform.position);
        }
    }

    private void OnMouseOver()
    {
        if (MapEditor.Instance == null) return;

        if (GameManager.IsMousePressed && MapEditor.Instance.TryDragElementAtTile(transform.position))
        {
            return;
        }

        if (GameManager.IsMousePressed)
        {
            if (Input.GetMouseButtonDown(1))
            {
                // Obrot o 90 stopni
                transform.rotation *= Quaternion.Euler(0, 0, 90);
            }

            if (Input.GetKeyDown(KeyCode.Delete))
            {
                MapEditor.Instance.RemoveElement(gameObject);
            }
        }

        if (GameManager.IsMousePressed)
        {
            if (MapEditor.IsElementRemoving || Input.GetKeyDown(KeyCode.Delete))
            {
                MapEditor.Instance.RemoveElement(gameObject);
            }
            else if (MapElementUI.SelectedElement != null)
            {
                Vector3 originalPosition = transform.position;
                BoxCollider2D boxCollider = GetComponent<BoxCollider2D>();
                float rotationZ = transform.eulerAngles.z;

                Vector3 mouseWorldPos = Camera.main.ScreenToWorldPoint(Input.mousePosition);

                if (boxCollider != null)
                {
                    if (boxCollider.size.y > boxCollider.size.x)
                    {
                        if (rotationZ < 45 || (rotationZ >= 135 && rotationZ < 225) || rotationZ > 315)
                        {
                            originalPosition.y -= 0.5f;
                            if (mouseWorldPos.y > originalPosition.y) originalPosition.y += 1.0f;
                        }
                        else
                        {
                            originalPosition.x += 0.5f;
                            if (mouseWorldPos.x < originalPosition.x) originalPosition.x -= 1.0f;
                        }
                    }
                    else if (boxCollider.size.y < boxCollider.size.x)
                    {
                        if ((rotationZ >= 45 && rotationZ < 135) || (rotationZ >= 225 && rotationZ < 315))
                        {
                            originalPosition.y -= 0.5f;
                            if (mouseWorldPos.y > originalPosition.y) originalPosition.y += 1.0f;
                        }
                        else
                        {
                            originalPosition.x += 0.5f;
                            if (mouseWorldPos.x < originalPosition.x) originalPosition.x -= 1.0f;
                        }
                    }
                    else if (transform.localScale.x > 1.5f || (boxCollider.size.x > 1.7f && boxCollider.size.y > 1.7f))
                    {
                        if ((rotationZ >= 45 && rotationZ < 135) || (rotationZ >= 225 && rotationZ < 315))
                        {
                            originalPosition.x += 0.5f;
                            originalPosition.y -= 0.5f;
                            if (mouseWorldPos.x < originalPosition.x) originalPosition.x -= 1.0f;
                            if (mouseWorldPos.y > originalPosition.y) originalPosition.y += 1.0f;
                        }
                        else
                        {
                            originalPosition.x -= 0.5f;
                            originalPosition.y += 0.5f;
                            if (mouseWorldPos.x > originalPosition.x) originalPosition.x += 1.0f;
                            if (mouseWorldPos.y < originalPosition.y) originalPosition.y -= 1.0f;
                        }
                    }
                }

                MapEditor.Instance.PlaceElementOnSelectedTile(originalPosition);
            }
        }
    }
}
