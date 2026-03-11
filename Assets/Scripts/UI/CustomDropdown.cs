using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CustomDropdown : MonoBehaviour
{
    public List<Button> Buttons = new List<Button>();
    public int SelectedIndex = 0;
    public Button SelectedButton;

    private readonly Color _defaultColor = new Color(0.55f, 0.66f, 0.66f, 0.05f);
    private readonly Color _selectedColor = new Color(1f, 1f, 1f, 0.2f);
    private readonly Color _activeColor = new Color(0.15f, 1f, 0.45f, 0.2f);
    private readonly Color _selectedActiveColor = new Color(0.15f, 1f, 0.45f, 0.4f);

    private void Awake()
    {
        InitializeButtons();
    }

    public void ClearButtons()
    {
        foreach (var button in Buttons)
        {
            if (button != null)
            {
                Destroy(button.gameObject);
            }
        }

        Buttons.Clear();
        SelectedIndex = 0;
        SelectedButton = null;
    }

    public void ResetSelectedOption()
    {
        if (SelectedIndex >= 1 && SelectedIndex <= Buttons.Count)
        {
            ResetColor(SelectedIndex);
        }

        SelectedIndex = 0;
        SelectedButton = null;
    }

    public void InitializeButtons()
    {
        for (int i = 0; i < Buttons.Count; i++)
        {
            int capturedIndex = i;
            Buttons[capturedIndex].onClick.RemoveAllListeners();
            Buttons[capturedIndex].onClick.AddListener(() => SelectOption(capturedIndex + 1));
        }
    }

    private void SelectOption(int index)
    {
        if (index < 1 || index > Buttons.Count)
        {
            Debug.LogError($"Nieprawidlowy indeks: {index}");
            return;
        }

        if (SelectedIndex >= 1 && SelectedIndex <= Buttons.Count)
        {
            Image oldImage = Buttons[SelectedIndex - 1].GetComponent<Image>();
            if (oldImage != null)
            {
                if (oldImage.color != _activeColor && oldImage.color != _selectedActiveColor)
                {
                    ResetColor(SelectedIndex);
                }
                else if (oldImage.color == _selectedActiveColor)
                {
                    oldImage.color = _activeColor;
                }
            }
        }

        SelectedIndex = index;
        SelectedButton = Buttons[SelectedIndex - 1];

        Image selectedImage = Buttons[SelectedIndex - 1].GetComponent<Image>();
        if (selectedImage != null)
        {
            if (selectedImage.color != _activeColor)
            {
                selectedImage.color = _selectedColor;
            }
            else
            {
                selectedImage.color = _selectedActiveColor;
            }
        }
    }

    public void MakeOptionActive(int index)
    {
        if (index < 1 || index > Buttons.Count) return;

        Image image = Buttons[index - 1].GetComponent<Image>();
        if (image != null)
        {
            image.color = _activeColor;
        }

        if (InventoryManager.Instance != null)
        {
            InventoryManager.Instance.DisplayHandInfo(Buttons[index - 1]);
        }
    }

    public void ResetColor(int index)
    {
        if (index < 1 || index > Buttons.Count) return;

        Image image = Buttons[index - 1].GetComponent<Image>();
        if (image != null)
        {
            image.color = _defaultColor;
        }

        Transform handInfo = Buttons[index - 1].transform.Find("hand_text");
        if (handInfo != null)
        {
            handInfo.gameObject.SetActive(false);
        }
    }

    public int GetSelectedIndex()
    {
        return SelectedIndex;
    }

    public void SetSelectedIndex(int index)
    {
        if (index == 0)
        {
            ResetSelectedOption();
            return;
        }

        if (index >= 1 && index <= Buttons.Count)
        {
            SelectOption(index);
        }
        else
        {
            Debug.LogError($"Nieprawidlowy indeks: {index}. Lista Buttons ma {Buttons.Count} elementow.");
        }
    }
}
