using UnityEngine;

/// <summary>
/// Gentle underwater sway for a coral model.
/// Attach to the coral's root GameObject.
/// If "Wiggle Children" is on, each child part sways with its own phase,
/// so branches move a little out of sync with each other (looks more organic).
/// </summary>
public class CoralWiggle : MonoBehaviour
{
    [Header("Sway")]
    [Tooltip("Max rotation in degrees")]
    public float swayAngle = 4f;
    [Tooltip("How fast it sways")]
    public float swaySpeed = 0.8f;

    [Header("Squish (optional)")]
    [Tooltip("Tiny scale pulse, like the coral is breathing. 0 = off")]
    public float squishAmount = 0.02f;

    [Header("Children")]
    public bool wiggleChildren = true;
    [Tooltip("Phase offset added per child so they don't move in sync")]
    public float childPhaseOffset = 0.7f;

    Transform[] parts;
    Quaternion[] startRot;
    Vector3[] startScale;
    float seed;

    void Start()
    {
        seed = Random.Range(0f, 100f); // so multiple corals don't move identically

        if (wiggleChildren && transform.childCount > 0)
        {
            parts = new Transform[transform.childCount];
            for (int i = 0; i < parts.Length; i++) parts[i] = transform.GetChild(i);
        }
        else
        {
            parts = new Transform[] { transform };
        }

        startRot = new Quaternion[parts.Length];
        startScale = new Vector3[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            startRot[i] = parts[i].localRotation;
            startScale[i] = parts[i].localScale;
        }
    }

    void Update()
    {
        float t = Time.time * swaySpeed + seed;

        for (int i = 0; i < parts.Length; i++)
        {
            float phase = t + i * childPhaseOffset;

            // Smooth, non-repeating-looking motion using Perlin noise (-1..1)
            float x = (Mathf.PerlinNoise(phase, 0f) - 0.5f) * 2f;
            float z = (Mathf.PerlinNoise(0f, phase) - 0.5f) * 2f;

            parts[i].localRotation = startRot[i] * Quaternion.Euler(x * swayAngle, 0f, z * swayAngle);

            if (squishAmount > 0f)
            {
                float s = 1f + Mathf.Sin(phase * 1.3f) * squishAmount;
                parts[i].localScale = new Vector3(startScale[i].x * s, startScale[i].y / s, startScale[i].z * s);
            }
        }
    }
}
