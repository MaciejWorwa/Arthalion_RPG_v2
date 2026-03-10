using UnityEngine;

public class WindowResizer : MonoBehaviour
{
    private const float _aspectRatio = 16f / 9f;

    private int _lastWidth = -1;
    private int _lastHeight = -1;
    private bool _lastFullscreen;

    void Update()
    {
        bool isFullscreen = Screen.fullScreen;

        // Do not force window ratio in fullscreen mode.
        if (isFullscreen)
        {
            _lastFullscreen = true;
            _lastWidth = Screen.width;
            _lastHeight = Screen.height;
            return;
        }

        int currentWidth = Screen.width;
        int currentHeight = Screen.height;

        // Skip work when size did not change.
        if (!_lastFullscreen && currentWidth == _lastWidth && currentHeight == _lastHeight)
        {
            return;
        }

        _lastFullscreen = false;
        _lastWidth = currentWidth;
        _lastHeight = currentHeight;

        int newHeight = Mathf.RoundToInt(currentWidth / _aspectRatio);
        if (currentHeight != newHeight)
        {
            Screen.SetResolution(currentWidth, newHeight, false);
            _lastHeight = newHeight;
        }
    }
}