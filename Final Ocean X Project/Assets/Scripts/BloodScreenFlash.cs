using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Put this on the full-screen blood UI Image. It starts hidden.
/// Call Show() (SharkAmbush does this when the shark bites) and the image
/// pops up at full opacity, holds for a moment, then fades out.
/// </summary>
[RequireComponent(typeof(Image))]
public class BloodScreenFlash : MonoBehaviour
{
    [Tooltip("How long the image stays fully visible.")]
    public float holdTime = 1.5f;
    [Tooltip("How long it takes to fade away afterwards. Set to 0 to keep it visible.")]
    public float fadeDuration = 1f;

    Image image;
    Coroutine routine;

    void Awake()
    {
        image = GetComponent<Image>();
        SetAlpha(0f);
        image.raycastTarget = false; // never blocks clicks
    }

    public void Show()
    {
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(ShowRoutine());
    }

    IEnumerator ShowRoutine()
    {
        SetAlpha(1f);

        if (fadeDuration <= 0f) yield break;

        yield return new WaitForSeconds(holdTime);

        float t = 0f;
        while (t < fadeDuration)
        {
            SetAlpha(Mathf.Lerp(1f, 0f, t / fadeDuration));
            t += Time.deltaTime;
            yield return null;
        }
        SetAlpha(0f);
    }

    void SetAlpha(float a)
    {
        Color c = image.color;
        c.a = a;
        image.color = c;
    }
}
