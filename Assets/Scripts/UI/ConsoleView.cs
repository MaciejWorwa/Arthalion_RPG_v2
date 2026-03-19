using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ConsoleView : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI _consoleText;
    [SerializeField] private RectTransform _contentRect;
    [SerializeField] private ScrollRect _scrollRect;
    [SerializeField] private RectTransform _scrollRectTransform;
    [SerializeField] private Animator _animator;

    private readonly List<string> _myLogs = new List<string>(200);
    private readonly StringBuilder _logBuilder = new StringBuilder(8192);
    private bool _doShow = true;
    private bool _isDirty;
    private const char ReplacementChar = '\uFFFD';

    void Start()
    {
        if (_consoleText != null)
        {
            _consoleText.text = string.Empty;
        }
    }

    private void OnEnable()
    {
        Application.logMessageReceived += HandleLog;
    }

    private void OnDisable()
    {
        Application.logMessageReceived -= HandleLog;
    }

    private void LateUpdate()
    {
        if (_isDirty && _doShow)
        {
            UpdateConsoleText();
            _isDirty = false;
        }
    }

    private void HandleLog(string logString, string stackTrace, LogType type)
    {
        _myLogs.Add(SanitizeForTmp(logString));

        if (_myLogs.Count > 200)
        {
            _myLogs.RemoveAt(0);
        }

        _isDirty = true;
    }

    private static string SanitizeForTmp(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        // Remove Unicode replacement characters (U+FFFD) that can appear after
        // decoding errors and trigger TMP missing-glyph warnings.
        if (value.IndexOf(ReplacementChar) >= 0)
        {
            value = value.Replace(ReplacementChar.ToString(), string.Empty);
        }

        return value;
    }

    private void UpdateConsoleText()
    {
        if (_consoleText == null)
        {
            return;
        }

        _logBuilder.Clear();
        for (int i = 0; i < _myLogs.Count; i++)
        {
            if (i > 0)
            {
                _logBuilder.Append('\n');
            }

            _logBuilder.Append(_myLogs[i]);
        }

        _consoleText.text = _logBuilder.ToString();

        AdjustContentHeight();

        Canvas.ForceUpdateCanvases();
        _scrollRect.verticalNormalizedPosition = 0f;
    }

    private void AdjustContentHeight()
    {
        if (_consoleText != null && _contentRect != null)
        {
            float preferredHeight = _consoleText.preferredHeight;
            _contentRect.sizeDelta = new Vector2(_contentRect.sizeDelta.x, preferredHeight);
        }
    }

    public void ShowOrHideConsole()
    {
        _doShow = !_doShow;

        if (_consoleText != null)
        {
            _consoleText.gameObject.SetActive(_doShow);
        }

        if (_doShow)
        {
            _isDirty = true;
        }
    }
}
