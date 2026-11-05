using System.Collections;
using UnityEngine;

public class TriggerAnimation : MonoBehaviour
{
    public string enterAnimationTrigger = "PlayAnimation"; // Animator trigger to fire after the delay
    public string exitAnimationTrigger = "StopAnimation";  // Animator trigger to fire on trigger exit
    public Animator animator; // Reference to the Animator component

    [Header("Delay")]
    public float delayBeforePlay = 5f; // Seconds to wait after entering before the animation plays

    [Header("Player Check")]
    public string playerTag = "Player"; // Only objects with this tag count. Leave empty to accept anything.

    [Header("After Animation Finishes")]
    [Tooltip("Exact name of the Animator state that plays (the box in the Animator window), e.g. submersible.")]
    public string animationStateName = "submersible";

    [Tooltip("Used only if Animation State Name is empty: how many seconds to wait before toggling.")]
    public float fallbackAnimationDuration = 5f;

    [Tooltip("Objects to switch when the animation is done.")]
    public GameObject[] objectsToToggle;

    public enum ToggleMode { TurnOn, TurnOff, Flip }
    [Tooltip("TurnOn = enable them, TurnOff = disable them, Flip = switch each one to the opposite of its current state.")]
    public ToggleMode toggleMode = ToggleMode.TurnOn;

    private Coroutine delayRoutine;
    private Coroutine finishRoutine;
    private bool hasPlayed = false;

    private void Start()
    {
        if (animator == null)
        {
            animator = GetComponent<Animator>(); // Get the Animator if not assigned
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsPlayer(other)) return;
        if (hasPlayed || finishRoutine != null) return; // Already played (or playing), don't restart

        Debug.Log($"[{name}] Trigger ENTERED by {other.name}. Animation will play in {delayBeforePlay} seconds.");

        if (delayRoutine != null) StopCoroutine(delayRoutine);
        delayRoutine = StartCoroutine(PlayAfterDelay());
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other)) return;

        Debug.Log($"[{name}] Trigger EXITED by {other.name}.");

        // Cancel the pending animation if the player leaves before the delay ends
        if (delayRoutine != null)
        {
            StopCoroutine(delayRoutine);
            delayRoutine = null;
            Debug.Log($"[{name}] Pending animation cancelled because the player left.");
        }

        if (animator != null)
        {
            animator.SetTrigger(exitAnimationTrigger);
            Debug.Log($"[{name}] Exit trigger '{exitAnimationTrigger}' fired.");
        }
        else
        {
            Debug.LogWarning("Animator component is missing on " + gameObject.name);
        }
    }

    private IEnumerator PlayAfterDelay()
    {
        yield return new WaitForSeconds(delayBeforePlay);
        delayRoutine = null;

        if (animator == null)
        {
            Debug.LogWarning("Animator component is missing on " + gameObject.name);
            yield break;
        }

        animator.SetTrigger(enterAnimationTrigger);
        Debug.Log($"[{name}] Delay finished. Animation trigger '{enterAnimationTrigger}' fired.");

        // Wait for the animation to finish. This keeps running even if the player leaves.
        finishRoutine = StartCoroutine(WaitForAnimationToFinish());
    }

    private IEnumerator WaitForAnimationToFinish()
    {
        if (string.IsNullOrEmpty(animationStateName))
        {
            // No state name given: just wait a fixed time
            yield return new WaitForSeconds(fallbackAnimationDuration);
        }
        else
        {
            // 1) Wait until the Animator actually enters the state (with a safety timeout)
            float timeout = 5f;
            while (!animator.GetCurrentAnimatorStateInfo(0).IsName(animationStateName))
            {
                timeout -= Time.deltaTime;
                if (timeout <= 0f)
                {
                    Debug.LogWarning($"[{name}] Animator never entered state '{animationStateName}'. " +
                                     "Check the state name and the transition. Toggling objects anyway.");
                    break;
                }
                yield return null;
            }

            // 2) Wait until that state has played all the way through
            while (animator.GetCurrentAnimatorStateInfo(0).IsName(animationStateName) &&
                   animator.GetCurrentAnimatorStateInfo(0).normalizedTime < 1f)
            {
                yield return null;
            }
        }

        Debug.Log($"[{name}] Animation finished. Toggling {objectsToToggle.Length} object(s) ({toggleMode}).");
        ToggleObjects();

        hasPlayed = true;
        finishRoutine = null;
    }

    private void ToggleObjects()
    {
        foreach (GameObject obj in objectsToToggle)
        {
            if (obj == null) continue;

            switch (toggleMode)
            {
                case ToggleMode.TurnOn:
                    obj.SetActive(true);
                    break;
                case ToggleMode.TurnOff:
                    obj.SetActive(false);
                    break;
                case ToggleMode.Flip:
                    obj.SetActive(!obj.activeSelf);
                    break;
            }

            Debug.Log($"[{name}] {obj.name} is now {(obj.activeSelf ? "ON" : "OFF")}.");
        }
    }

    private bool IsPlayer(Collider other)
    {
        return string.IsNullOrEmpty(playerTag) || other.CompareTag(playerTag);
    }
}