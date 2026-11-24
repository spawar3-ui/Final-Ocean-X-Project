using UnityEngine;

/// <summary>
/// Moves a jellyfish upward while its animation is playing.
/// Attach to the jellyfish root object (the one that has the Animator).
/// </summary>
public class JellyfishRise : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float riseSpeed = 1.0f;          // units per second
    [SerializeField] private bool pulseWithSwim = true;       // speed up/slow down like a real jellyfish
    [SerializeField] private float pulsesPerSecond = 1.0f;    // match this to your animation's loop speed
    [Range(0f, 1f)]
    [SerializeField] private float pulseStrength = 0.6f;      // 0 = constant speed, 1 = full stop between pulses

    [Header("Optional sideways drift")]
    [SerializeField] private float driftAmount = 0.3f;
    [SerializeField] private float driftSpeed = 0.5f;

    [Header("Limits")]
    [SerializeField] private bool useMaxHeight = true;
    [SerializeField] private float maxHeight = 20f;
    [SerializeField] private bool respawnAtStart = true;      // false = destroy at max height

    private Animator animator;
    private Vector3 startPosition;
    private float timeOffset;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        startPosition = transform.position;
        timeOffset = Random.Range(0f, 10f); // so a group of jellyfish don't move in sync
    }

    private void Update()
    {
        // Only move while the animator is actually running
        if (animator != null && (!animator.enabled || animator.speed <= 0f))
            return;

        float speed = riseSpeed;

        if (pulseWithSwim)
        {
            // Smooth wave between (1 - pulseStrength) and 1
            float wave = (Mathf.Sin((Time.time + timeOffset) * pulsesPerSecond * Mathf.PI * 2f) + 1f) * 0.5f;
            speed *= Mathf.Lerp(1f - pulseStrength, 1f, wave);
        }

        Vector3 move = Vector3.up * speed;

        if (driftAmount > 0f)
        {
            float sway = Mathf.Sin((Time.time + timeOffset) * driftSpeed) * driftAmount;
            move += transform.right * sway;
        }

        transform.Translate(move * Time.deltaTime, Space.World);

        if (useMaxHeight && transform.position.y - startPosition.y >= maxHeight)
        {
            if (respawnAtStart) transform.position = startPosition;
            else Destroy(gameObject);
        }
    }
}
