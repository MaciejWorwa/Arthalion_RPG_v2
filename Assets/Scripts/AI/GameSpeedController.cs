using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class GameSpeedController : MonoBehaviour
{
    [Range(1f, 100f)]
    public float gameSpeed = 1f;  // Prędkość gry kontrolowana z inspektora

    void Update()
    {
        // Ustawianie prędkości gry
        Time.timeScale = gameSpeed;
        if (Time.timeScale < 0.1f)
            Time.timeScale = 0.1f;

        // Zarządzanie animatorami UI
        if (gameSpeed >= 5)
        {
            ToggleUI(false);
        }
        else
        {
            ToggleUI(true);
        }
    }

    private void ToggleUI(bool value)
    {
        var animators = FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var anim in animators)
        {
            if (anim.GetComponentInParent<Canvas>() != null)
                anim.enabled = value;
        }
    }
}
