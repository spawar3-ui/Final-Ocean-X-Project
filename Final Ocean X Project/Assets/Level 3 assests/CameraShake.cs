using System.Collections;
using UnityEngine;

// Put this on the Main Camera. If your camera is moved by another script
// (e.g. Cinemachine), put this on a parent object of the camera instead.
public class CameraShake : MonoBehaviour
{
    Vector3 originalLocalPos;
    Coroutine running;

    void Awake()
    {
        originalLocalPos = transform.localPosition;
    }

    public void Shake(float duration, float magnitude)
    {
        if (running != null)
        {
            StopCoroutine(running);
            transform.localPosition = originalLocalPos;
        }
        running = StartCoroutine(DoShake(duration, magnitude));
    }

    IEnumerator DoShake(float duration, float magnitude)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            float falloff = 1f - (elapsed / duration);
            Vector3 offset = Random.insideUnitSphere * magnitude * falloff;
            transform.localPosition = originalLocalPos + offset;
            elapsed += Time.deltaTime;
            yield return null;
        }
        transform.localPosition = originalLocalPos;
        running = null;
    }
}
