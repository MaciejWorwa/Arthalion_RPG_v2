using System.Collections.Generic;
using UnityEngine;
using System;
using System.Collections;
using TMPro;
using System.Linq;

public class AnimationManager : MonoBehaviour
{
     // Prywatne statyczne pole przechowujące instancję
    private static AnimationManager instance;

    // Publiczny dostęp do instancji
    public static AnimationManager Instance
    {
        get { return instance; }
    }

    void Awake()
    {
        if (instance == null)
        {
            instance = this;
        }
        else if (instance != this)
        {
            // Jeśli instancja już istnieje, a próbujemy utworzyć kolejną, niszczymy nadmiarową
            Destroy(gameObject);
        }
    }
    
    public Dictionary<Animator, bool> PanelStates = new Dictionary<Animator, bool>(); // Mapowanie Animator -> stan panelu

    public void TogglePanel(Animator panelAnimator)
    {
        // Sprawdza, czy animator ma już zapisany stan; jeśli nie, ustaw domyślny na "schowany"
        if (!PanelStates.ContainsKey(panelAnimator))
        {
            PanelStates[panelAnimator] = false; // Domyślnie panel jest schowany
        }

        // Zmiana stanu panelu
        bool isPanelOpen = PanelStates[panelAnimator];
        isPanelOpen = !isPanelOpen;
        PanelStates[panelAnimator] = isPanelOpen;

        // Sprawdza, czy animator ma parametr "IsExpanded"
        if (HasParameter(panelAnimator, "IsExpanded"))
        {
            bool isExpanded = panelAnimator.GetBool("IsExpanded");

            if (isExpanded)
            {
                if (isPanelOpen)
                {
                    panelAnimator.Play("ShowExpandedPanel"); // Nazwa animacji otwierającej rozszerzony panel
                }
                else
                {
                    panelAnimator.Play("HideExpandedPanel"); // Nazwa animacji zamykającej rozszerzony panel
                }
            }
            else
            {
                PlayDefaultPanelAnimation(panelAnimator, isPanelOpen);
            }
        }
        else
        {
            PlayDefaultPanelAnimation(panelAnimator, isPanelOpen);
        }

        // Rotacja przycisku strzałki
        Transform arrowButtonTransform = panelAnimator.gameObject.transform.Find("arrow_button");
        if (arrowButtonTransform != null)
        {
            float currentZRotation = arrowButtonTransform.localRotation.eulerAngles.z; // Obecny kąt Z w stopniach
            float rotationAngle = isPanelOpen ? currentZRotation - 180 : currentZRotation + 180; // Oblicza nowy kąt
            arrowButtonTransform.localRotation = Quaternion.Euler(0, 0, rotationAngle); // Ustawia nową rotację
        }
    }

    // Funkcja pomocnicza do sprawdzania, czy animator posiada parametr o podanej nazwie
    private bool HasParameter(Animator animator, string paramName)
    {
        foreach (AnimatorControllerParameter param in animator.parameters)
        {
            if (param.name == paramName)
            {
                return true;
            }
        }
        return false;
    }

    // Funkcja do odtwarzania domyślnej animacji
    private void PlayDefaultPanelAnimation(Animator panelAnimator, bool isPanelOpen)
    {
        if (isPanelOpen)
        {
            panelAnimator.Play("ShowPanel"); // Nazwa animacji otwierającej panel
        }
        else
        {
            panelAnimator.Play("HidePanel"); // Nazwa animacji zamykającej panel
        }
    }

    public void ExpandPanel(Animator panelAnimator)
    {
        panelAnimator.Play("ExpandPanel");
        panelAnimator.SetBool("IsExpanded", true);
    }

    public void CollapsePanel(Animator panelAnimator)
    {
        panelAnimator.Play("CollapsePanel");
        panelAnimator.SetBool("IsExpanded", false);
    }

    #region Unit actions animations
    public IEnumerator PlayAnimation(string animationName, GameObject attacker = null, GameObject target = null, int damage = 0)
    {   
        if (ReinforcementLearningManager.Instance != null && ReinforcementLearningManager.Instance.IsFastSimulation) yield break;
        if(GameManager.IsShowAnimationsMode == false) yield break;

        if (attacker != null && !attacker.activeSelf && attacker.GetComponent<Unit>().HasRider)
        {
            Unit rider = UnitsManager.Instance.AllUnits.FirstOrDefault(u => u != null && u.Mount == attacker.GetComponent<Unit>());
            attacker = rider.gameObject;
        }

        if (target != null && !target.activeSelf && target.GetComponent<Unit>().HasRider)
        {
            Unit rider = UnitsManager.Instance.AllUnits.FirstOrDefault(u => u != null && u.Mount == target.GetComponent<Unit>());
            target = rider.gameObject;
        }

        Animator animator;
        GameObject animationObject;

        if(animationName == "attack" && target != null && attacker != null)
        {
            if(target == null) yield break;
            animationObject = attacker.transform.Find("AttackAnimation/Attack_animation").gameObject;
            animationObject.SetActive(true);
            animator = animationObject.GetComponent<Animator>();

            animationObject.transform.parent.position = new Vector3(target.transform.position.x, target.transform.position.y, -2f);

            // Porównanie współrzędnych X
            if (target.transform.position.x > attacker.transform.position.x)
            {
                animator.Play("RightAttackAnimation");
            }
            else
            {
                animator.Play("LeftAttackAnimation");  
            }

            yield return new WaitForSeconds(1f);
            if(attacker != null)
            {
                animationObject.transform.parent.position = new Vector3(attacker.transform.position.x, attacker.transform.position.y, -2f);
                animationObject.SetActive(false);
            }
        }
        else if (animationName == "damage" && damage > 0 && target != null)
        {
            animationObject = target.transform.Find("Animations/Damage_animation").gameObject;
            animationObject.SetActive(true);
            animator = animationObject.GetComponent<Animator>();

            animationObject.GetComponent<TMP_Text>().text = "-" + damage.ToString();

            animator.Play("DamageAnimation");

            yield return new WaitForSeconds(1f);
            if(target != null)
            {
                animationObject.SetActive(false);
            }
        }
        else if (animationName == "miss" && target != null)
        {
            animationObject = target.transform.Find("Animations/Miss_animation").gameObject;
            animationObject.SetActive(true);
            animator = animationObject.GetComponent<Animator>();

            animationObject.GetComponent<TMP_Text>().text = "Pudło!";

            animator.Play("MissAnimation");

            yield return new WaitForSeconds(1f);
            if(target != null)
            {
                animationObject.SetActive(false);
            }
        }
        else if (animationName == "parry" && target != null)
        {
            animationObject = target.transform.Find("Animations/Parry_animation").gameObject;
            animationObject.SetActive(true);
            animator = animationObject.GetComponent<Animator>();

            animator.Play("ParryAnimation");

            yield return new WaitForSeconds(1f);
            if(target != null)
            {
                animationObject.SetActive(false);
            }
        }
        else if (animationName == "aim" && attacker != null)
        {
            animationObject = attacker.transform.Find("Animations/Aim_animation").gameObject;
            animationObject.SetActive(true);
            animator = animationObject.GetComponent<Animator>();

            animator.Play("AimAnimation");

            yield return new WaitForSeconds(1f);
            if(attacker != null)
            {
                animationObject.SetActive(false);
            }
        }
        else if (animationName == "reload" && attacker != null)
        {
            animationObject = attacker.transform.Find("Animations/Reload_animation").gameObject;
            animationObject.SetActive(true);
            animator = animationObject.GetComponent<Animator>();

            animator.Play("ReloadAnimation");

            yield return new WaitForSeconds(1f);
            if(attacker != null)
            {
                animationObject.SetActive(false);
            }
        }

        if (animationName == "kill" && target != null && attacker != null)
        {
            animationObject = attacker.transform.Find("AttackAnimation/Kill_animation").gameObject;
            animationObject.SetActive(true);
            animator = animationObject.GetComponent<Animator>();

            animationObject.transform.parent.position = new Vector3(target.transform.position.x, target.transform.position.y, -2f);

            animator.Play("KillAnimation");

            yield return new WaitForSeconds(1f);
            if(attacker != null)
            {
                animationObject.transform.parent.position = new Vector3(attacker.transform.position.x, attacker.transform.position.y, -2f);
                animationObject.SetActive(false);
            }
        }
    }
    #endregion
}
