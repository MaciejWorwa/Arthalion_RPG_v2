using UnityEngine;
using SimpleFileBrowser;
using System.Collections;
using UnityEngine.UI;
using System.IO;
using System.Collections.Generic;
using System;

public class TokensManager : MonoBehaviour
{
    // Prywatne statyczne pole przechowujące instancję
    private static TokensManager instance;

    // Publiczny dostęp do instancji
    public static TokensManager Instance
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

    [SerializeField] private GameObject _tokenDisplayPanel; // Panel do wyświetlania tokena
    [SerializeField] private Image _tokenImage; // UI Image w panelu

    void Start()
    {
        // Konfiguracja SimpleFileBrowser po opóźnieniu
        FileBrowser.SetFilters(true, new FileBrowser.Filter("Images", ".jpg", ".png"));
        FileBrowser.SetDefaultFilter(".jpg");
    }

    public void ApplyDefaultTokenIfMissing(GameObject unitObject)
    {
        if (unitObject == null) return;

        Unit unit = unitObject.GetComponent<Unit>();
        if (unit == null) return;
        if (unit.HasTokenSprite) return;
        if (!string.IsNullOrWhiteSpace(unit.TokenFilePath)) return;

        TryApplyDefaultTokenFromResources(unitObject);
    }

    public bool TryApplyDefaultTokenFromResources(GameObject unitObject)
    {
        if (unitObject == null) return false;

        Unit unit = unitObject.GetComponent<Unit>();
        Stats stats = unitObject.GetComponent<Stats>();
        if (unit == null || stats == null) return false;

        List<string> tokenCandidates = new List<string>();
        HashSet<string> usedKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddCandidate(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;

            string trimmed = key.Trim();
            if (usedKeys.Add(trimmed))
            {
                tokenCandidates.Add(trimmed);
            }

            string underscored = trimmed.Replace(" ", "_");
            if (usedKeys.Add(underscored))
            {
                tokenCandidates.Add(underscored);
            }
        }

        AddCandidate(stats.TokenKey);
        AddCandidate(stats.Race);
        AddCandidate("_default");

        for (int i = 0; i < tokenCandidates.Count; i++)
        {
            if (TryApplyTokenFromResources(unitObject, tokenCandidates[i]))
            {
                unit.TokenFilePath = string.Empty;
                unit.HasTokenSprite = true;
                return true;
            }
        }

        unit.HasTokenSprite = false;
        return false;
    }

    private bool TryApplyTokenFromResources(GameObject unitObject, string tokenName)
    {
        if (unitObject == null || string.IsNullOrWhiteSpace(tokenName)) return false;

        Sprite sourceSprite = Resources.Load<Sprite>($"Tokens/{tokenName}");
        if (sourceSprite == null) return false;

        Transform tokenTransform = unitObject.transform.Find("Token");
        if (tokenTransform == null) return false;

        SpriteRenderer tokenRenderer = tokenTransform.GetComponent<SpriteRenderer>();
        if (tokenRenderer == null) return false;

        tokenTransform.gameObject.SetActive(true);

        if (!TryApplyTokenTextureToRenderer(sourceSprite.texture, sourceSprite.rect, tokenRenderer))
        {
            return false;
        }

        return true;
    }

    private bool TryApplyTokenTextureToRenderer(Texture2D sourceTexture, Rect sourceRect, SpriteRenderer tokenRenderer)
    {
        if (sourceTexture == null || tokenRenderer == null) return false;

        // Set token tint to neutral so unit highlight color does not overlay token art.
        tokenRenderer.material.color = Color.white;
        tokenRenderer.material.SetColor("_EmissionColor", Color.black);

        float width = sourceRect.width;
        float height = sourceRect.height;
        if (width <= 0f || height <= 0f) return false;

        // Center-crop to square, same behavior as loading token from disk.
        float minSize = Mathf.Min(width, height);
        float offsetX = sourceRect.x + (width - minSize) / 2f;
        float offsetY = sourceRect.y + (height - minSize) / 2f;
        Rect rect = new Rect(offsetX, offsetY, minSize, minSize);

        // Match world-space size to the renderer, as in LoadTokenImage.
        float spriteSize = Mathf.Min(tokenRenderer.size.x, tokenRenderer.size.y);
        if (spriteSize <= 0f) spriteSize = 1f;
        float pixelsPerUnit = minSize / spriteSize;

        Sprite newSprite = Sprite.Create(sourceTexture, rect, new Vector2(0.5f, 0.5f), pixelsPerUnit);
        tokenRenderer.sprite = newSprite;
        return true;
    }

    public void OpenFileBrowser()
    {
        if(Unit.SelectedUnit == null) return;

        StartCoroutine(ShowLoadDialogCoroutine());
    }
    
    IEnumerator ShowLoadDialogCoroutine()
    {
        yield return FileBrowser.WaitForLoadDialog(FileBrowser.PickMode.Files, false, null, null, "Wybierz obraz", "Zatwierdź");

        if (FileBrowser.Success)
        {
            string filePath = FileBrowser.Result[0];
            StartCoroutine(LoadTokenImage(filePath, Unit.SelectedUnit));
        }
    }

    public IEnumerator LoadTokenImage(string filePath, GameObject unitObject)
    {
        if(unitObject == null || filePath.Length < 1) yield break;

        Unit unitComponent = unitObject.GetComponent<Unit>();

        // Sprawdza, czy plik istnieje
        if (!File.Exists(filePath))
        {
            Debug.LogError($"<color=red>Plik graficzny z tokenem nie został znaleziony: {filePath}</color>");
            yield break;
        }

        //Aktywuje token
        unitObject.transform.Find("Token").gameObject.SetActive(true);

        SpriteRenderer imageRenderer = unitObject.transform.Find("Token").GetComponent<SpriteRenderer>();

        byte[] byteTexture = File.ReadAllBytes(filePath);
        Texture2D texture = new Texture2D(2, 2);
        if (texture.LoadImage(byteTexture))
        {
            // Sprawdź rozdzielczość obrazu
            if (texture.width > 2048 || texture.height > 2048) // Ograniczenia rozdzielczości
            {
                Debug.LogError("Obraz jest za duży.");
            }
            else
            {
                //Ustawienie koloru na biały, żeby nie było overlaya koloru na tokenie
                if (!TryApplyTokenTextureToRenderer(texture, new Rect(0f, 0f, texture.width, texture.height), imageRenderer))
                {
                    Debug.LogError("Nie udało się dopasować tokena.");
                    yield break;
                }

                // Aktualizacja ścieżki do tokena jednostki
                if (unitComponent != null)
                {
                    unitComponent.TokenFilePath = filePath;
                    unitComponent.HasTokenSprite = true;
                }

                UnitsManager.Instance.UpdateUnitPanel(unitObject);
            }
        }
        else
        {
            if (unitComponent != null && !unitComponent.HasTokenSprite)
            {
                unitObject.transform.Find("Token").gameObject.SetActive(false);
            }
            Debug.LogError("Nie udało się załadować obrazu.");
        }

        yield return null;
    }

    public void ShowTokenDisplayPanel(Sprite tokenSprite)
    {
        if (tokenSprite == null) return;

        _tokenImage.sprite = tokenSprite; // Ustaw obraz tokena
        _tokenDisplayPanel.SetActive(!_tokenDisplayPanel.activeSelf); // Wyświetla lub chowa panel
    }
}


