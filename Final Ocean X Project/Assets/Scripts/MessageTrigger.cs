using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Put this on a cube with a Box Collider (Is Trigger ticked).
/// When the player walks into it, a message fades in on the UI.
///
/// Setup:
/// 1. Create a UI text: right-click Hierarchy > UI > Text - TextMeshPro.
/// 2. Drag that text into "Message Text" below.
/// 3. Edit the message in the Inspector if you want different wording.
/// </summary>
[RequireComponent(typeof(Collider))]
public class MessageTrigger : MonoBehaviour
{
    [Header("Message")]
    [Tooltip("Drag your TextMeshPro UI text here.")]
    public TMP_Text messageText;

    [TextArea(2, 4)]
    public string message = "As an Anglerfish, you can't be this high in the ocean, Good bye";

    [Header("Timing")]
    public float fadeInTime = 0.8f;
    [Tooltip("Seconds the message stays on screen. 0 = stays forever.")]
    public float stayTime = 5f;
    public float fadeOutTime = 1f;

    [Header("Options")]
    public bool triggerOnce = true;
    [Tooltip("Hook up anything extra here: sound, fade to black, reload the scene...")]
    public UnityEvent onTriggered;

    bool triggered;
    Coroutine routine;

    // Makes the collider a trigger automatically when you add the script.
    void Reset()
    {
        var c = GetComponent<Collider>();
        if (c != null) c.isTrigger = true;
    }

    void Start()
    {
        // Start with the text invisible.
        if (messageText != null)
            SetAlpha(0f);
    }

    void OnTriggerEnter(Collider other)
    {
        if (triggered && triggerOnce) return;
        if (other.GetComponentInParent<FirstPersonController>() == null) return;
        if (messageText == null)
        {
            Debug.LogWarning("MessageTrigger: no Message Text assigned.", this);
            return;
        }

        triggered = true;
        onTriggered?.Invoke();

        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(ShowMessage());
    }

    IEnumerator ShowMessage()
    {
        messageText.text = message;

        // Fade in
        for (float t = 0f; t < 1f; t += Time.deltaTime / Mathf.Max(fadeInTime, 0.01f))
        {
            SetAlpha(t);
            yield return null;
        }
        SetAlpha(1f);

        // Stay forever?
        if (stayTime <= 0f) yield break;

        yield return new WaitForSeconds(stayTime);

        // Fade out
        for (float t = 1f; t > 0f; t -= Time.deltaTime / Mathf.Max(fadeOutTime, 0.01f))
        {
            SetAlpha(t);
            yield return null;
        }
        SetAlpha(0f);
    }

    void SetAlpha(float a)
    {
        Color c = messageText.color;
        c.a = a;
        messageText.color = c;
    }
}
