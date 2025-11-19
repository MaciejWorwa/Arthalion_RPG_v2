using System.Reflection;
using TMPro;
using UnityEngine;

public class InputFieldFilter : MonoBehaviour
{
    [SerializeField] private TMP_InputField _inputField;

    [Header("Rodzaj pola")]
    [SerializeField] private bool _isSkillInput;      // 0–5
    [SerializeField] private bool _isTalentInput;     // 0–3
    [SerializeField] private bool _isTwoDigitNumber;  // +/- 2 cyfry (np. modyfikatory)
    [SerializeField] private bool _isMoneyInput;      // cyfry + +/-
    [SerializeField] private bool _isDiceRoll;        // rzut k10: 1–10
    [SerializeField] private bool _isDamageRoll;      // suma obrażeń: 1–99

    private void Start()
    {
        _inputField = GetComponent<TMP_InputField>();

        if (_inputField != null)
        {
            _inputField.onValidateInput += ValidateInput;

            if (_isDiceRoll)
                _inputField.onEndEdit.AddListener(ValidateDiceRollValue);

            if (_isDamageRoll)
                _inputField.onEndEdit.AddListener(ValidateDamageRollValue);

            if (_isSkillInput)
                _inputField.onEndEdit.AddListener(ValidateSkillValue);

            if (_isTalentInput)
                _inputField.onEndEdit.AddListener(ValidateTalentValue);
        }
    }

    private char ValidateInput(string text, int charIndex, char addedChar)
    {
        // *** SKILL: 1 cyfra, tylko 0–5 ***
        if (_isSkillInput)
        {
            if (char.IsDigit(addedChar))
            {
                string newText = text.Insert(charIndex, addedChar.ToString());
                if (newText.Length <= 1 && addedChar >= '0' && addedChar <= '5')
                    return addedChar;
            }
            return '\0';
        }
        // *** TALENT: 1 cyfra, tylko 0–3 ***
        else if (_isTalentInput)
        {
            if (char.IsDigit(addedChar))
            {
                string newText = text.Insert(charIndex, addedChar.ToString());
                if (newText.Length <= 1 && addedChar >= '0' && addedChar <= '3')
                    return addedChar;
            }
            return '\0';
        }
        else if (_isTwoDigitNumber)
        {
            if (addedChar == '-' && text.Length == 0) return addedChar;

            if (char.IsDigit(addedChar))
            {
                int digitCount = text.StartsWith("-") ? text.Length - 1 : text.Length;
                if (digitCount < 2) return addedChar;
            }
            return '\0';
        }
        else if (_isMoneyInput)
        {
            if (char.IsDigit(addedChar) || addedChar == '+' || addedChar == '-')
                return addedChar;
            return '\0';
        }
        // *** RZUT K10: 1–10 (jak było) ***
        else if (_isDiceRoll)
        {
            if (!char.IsDigit(addedChar)) return '\0';

            string newText = text.Insert(charIndex, addedChar.ToString());

            if (newText.Length == 1)
                return addedChar; // 0–9 (0 skorygujemy na endEdit)

            if (newText.Length == 2 && newText == "10")
                return addedChar; // jedyna dozwolona dwucyfrowa wartość

            return '\0'; // blokuj 11–99 i >2 znaki
        }
        // *** OBRAŻENIA: suma 1–99 ***
        else if (_isDamageRoll)
        {
            if (!char.IsDigit(addedChar)) return '\0';

            string newText = text.Insert(charIndex, addedChar.ToString());

            // pozwalamy na max 2 cyfry → 1–99
            if (newText.Length <= 2)
                return addedChar;

            return '\0';
        }
        else
        {
            if (char.IsLetterOrDigit(addedChar) || char.IsWhiteSpace(addedChar))
                return addedChar;
            return '\0';
        }
    }

    private void ValidateDiceRollValue(string input)
    {
        if (int.TryParse(input, out int value))
        {
            value = Mathf.Clamp(value, 1, 10);
            _inputField.text = value.ToString();
        }
    }

    private void ValidateDamageRollValue(string input)
    {
        if (int.TryParse(input, out int value))
        {
            value = Mathf.Clamp(value, 1, 99);
            _inputField.text = value.ToString();
        }
    }

    private void ValidateSkillValue(string input)
    {
        if (!int.TryParse(input, out int v)) v = 0;
        v = Mathf.Clamp(v, 0, 5);
        _inputField.text = v.ToString();
    }

    private void ValidateTalentValue(string input)
    {
        if (!int.TryParse(input, out int v)) v = 0;
        v = Mathf.Clamp(v, 0, 3);
        _inputField.text = v.ToString();
    }

    public void SetBool(string boolName, bool value)
    {
        var field = GetType().GetField(boolName, BindingFlags.NonPublic | BindingFlags.Instance);
        if (field == null || field.FieldType != typeof(bool))
            return;

        bool oldValue = (bool)field.GetValue(this);
        if (oldValue == value)
            return; // nic się nie zmienia

        field.SetValue(this, value);

        if (_inputField == null)
            _inputField = GetComponent<TMP_InputField>();

        // Zarządzanie listenerami zależnie od boole’a
        if (boolName == "_isDiceRoll")
        {
            if (value)
                _inputField.onEndEdit.AddListener(ValidateDiceRollValue);
            else
                _inputField.onEndEdit.RemoveListener(ValidateDiceRollValue);
        }
        else if (boolName == "_isDamageRoll")
        {
            if (value)
                _inputField.onEndEdit.AddListener(ValidateDamageRollValue);
            else
                _inputField.onEndEdit.RemoveListener(ValidateDamageRollValue);
        }
    }
}
