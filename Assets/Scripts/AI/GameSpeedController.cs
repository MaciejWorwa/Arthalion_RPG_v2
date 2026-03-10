using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class GameSpeedController : MonoBehaviour
{
    [Range(1f, 100f)]
    public float gameSpeed = 1f;

    private float _lastAppliedSpeed = -1f;
    private readonly List<Animator> _uiAnimators = new List<Animator>();

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void Start()
    {
        CacheUiAnimators();
    }

    private void Update()
    {
        float targetTimeScale = gameSpeed < 0.1f ? 0.1f : gameSpeed;
        Time.timeScale = targetTimeScale;

        if (Mathf.Approximately(_lastAppliedSpeed, gameSpeed))
        {
            return;
        }

        _lastAppliedSpeed = gameSpeed;
        ToggleUI(gameSpeed < 5f);
    }

    private void ToggleUI(bool value)
    {
        for (int i = 0; i < _uiAnimators.Count; i++)
        {
            Animator anim = _uiAnimators[i];
            if (anim != null)
            {
                anim.enabled = value;
            }
        }
    }

    private void CacheUiAnimators()
    {
        _uiAnimators.Clear();

        Animator[] animators = FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < animators.Length; i++)
        {
            Animator anim = animators[i];
            if (anim != null && anim.GetComponentInParent<Canvas>() != null)
            {
                _uiAnimators.Add(anim);
            }
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        CacheUiAnimators();
        _lastAppliedSpeed = -1f;
    }
}