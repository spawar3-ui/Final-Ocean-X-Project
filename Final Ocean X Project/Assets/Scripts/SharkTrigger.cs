using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Put this on a cube with a Box Collider (Is Trigger ticked).
/// When the player walks into it:
///  - the shark wakes up and starts hunting,
///  - the area light(s) turn red.
/// </summary>
[RequireComponent(typeof(Collider))]
public class SharkTrigger : MonoBehaviour
{
    [Header("Shark")]
    public SharkChase shark;

    [Header("Lights")]
    [Tooltip("Drag your area light(s) here.")]
    public Light[] lights;
    public Color alertColor = Color.red;
    [Tooltip("Seconds to fade to red. 0 = instant.")]
    public float fadeTime = 1f;

    [Header("Options")]
    public bool triggerOnce = true;
    public UnityEvent onTriggered; // optional: sound, music, camera shake...

    bool triggered;

    // Makes the collider a trigger automatically when you add the script.
    void Reset()
    {
        var c = GetComponent<Collider>();
        if (c != null) c.isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (triggered && triggerOnce) return;
        if (other.GetComponentInParent<FirstPersonController>() == null) return;

        triggered = true;

        if (shark != null && !shark.IsFinished)
        {
            if (!shark.gameObject.activeSelf) shark.gameObject.SetActive(true);
            shark.Activate();
        }

        foreach (Light l in lights)
            if (l != null) StartCoroutine(FadeLight(l));

        onTriggered?.Invoke();
    }

    IEnumerator FadeLight(Light l)
    {
        if (fadeTime <= 0f)
        {
            l.color = alertColor;
            yield break;
        }

        Color from = l.color;
        for (float t = 0f; t < 1f; t += Time.deltaTime / fadeTime)
        {
            l.color = Color.Lerp(from, alertColor, t);
            yield return null;
        }
        l.color = alertColor;
    }
}
