using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Put this on a fish (the FishSpawner adds it automatically).
/// Registers itself so the player can find it, wanders around inside an area,
/// gets drawn slowly toward the anglerfish's light (the Lure), and can be eaten.
/// </summary>
public class EdibleFish : MonoBehaviour
{
    // Every active fish, so PlayerEat can find the nearest one without colliders.
    public static readonly List<EdibleFish> All = new List<EdibleFish>();

    // The anglerfish's light. PlayerEat sets this automatically.
    public static Transform Lure;

    [Header("Wander")]
    public bool wander = true;
    public float speed = 1.5f;
    public float turnSpeed = 2f;
    [Tooltip("If your fish model swims sideways or backwards, adjust this (e.g. Y = 90 or -90).")]
    public Vector3 modelRotationOffset = Vector3.zero;

    [Header("Attracted To Light")]
    public bool attractedToLure = true;
    [Tooltip("Fish farther than this from the light ignore it.")]
    public float attractRange = 15f;
    [Tooltip("Slow, hypnotised approach speed.")]
    public float attractSpeed = 0.6f;
    [Tooltip("How close to the light the fish hover (keep this smaller than PlayerEat's Eat Range).")]
    public float hoverDistance = 1.2f;
    [Tooltip("Gentle drifting while hovering near the light.")]
    public float bobAmount = 0.15f;
    public float bobSpeed = 1.5f;

    [Header("Sound")]
    public AudioClip eatSound;
    [Range(0f, 1f)] public float eatVolume = 1f;

    [Header("Events")]
    public UnityEvent onEaten; // hook up particles, score, etc.

    Vector3 areaCenter;
    Vector3 areaSize;
    Vector3 target;
    bool hasArea;
    float bobOffset;

    void OnEnable() { All.Add(this); bobOffset = Random.value * 10f; }
    void OnDisable() { All.Remove(this); }

    /// <summary>Called by the spawner to tell the fish where it may swim.</summary>
    public void SetArea(Vector3 center, Vector3 size)
    {
        areaCenter = center;
        areaSize = size;
        hasArea = true;
        PickTarget();
    }

    void PickTarget()
    {
        target = areaCenter + new Vector3(
            Random.Range(-areaSize.x, areaSize.x) * 0.5f,
            Random.Range(-areaSize.y, areaSize.y) * 0.5f,
            Random.Range(-areaSize.z, areaSize.z) * 0.5f);
    }

    void Update()
    {
        if (attractedToLure && Lure != null)
        {
            float distToLure = Vector3.Distance(transform.position, Lure.position);
            if (distToLure <= attractRange)
            {
                MoveTowardLure(distToLure);
                return;
            }
        }

        Wander();
    }

    void MoveTowardLure(float distToLure)
    {
        Vector3 toLure = Lure.position - transform.position;

        // Always face the light.
        if (toLure.sqrMagnitude > 0.0001f)
        {
            Quaternion look = Quaternion.LookRotation(toLure) * Quaternion.Euler(modelRotationOffset);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, turnSpeed * Time.deltaTime);
        }

        if (distToLure > hoverDistance)
        {
            // Slow down as it gets close, so it drifts in rather than rushing.
            float slow = Mathf.Clamp01((distToLure - hoverDistance) / 2f);
            float move = attractSpeed * Mathf.Lerp(0.3f, 1f, slow);
            transform.position += toLure.normalized * move * Time.deltaTime;
        }
        else
        {
            // Hover in front of the light with a small bob.
            float bob = Mathf.Sin((Time.time + bobOffset) * bobSpeed) * bobAmount;
            transform.position += Vector3.up * bob * Time.deltaTime;
        }
    }

    void Wander()
    {
        if (!wander || !hasArea) return;

        Vector3 toTarget = target - transform.position;
        if (toTarget.magnitude < 0.5f)
        {
            PickTarget();
            return;
        }

        Quaternion look = Quaternion.LookRotation(toTarget) * Quaternion.Euler(modelRotationOffset);
        transform.rotation = Quaternion.Slerp(transform.rotation, look, turnSpeed * Time.deltaTime);
        transform.position += toTarget.normalized * speed * Time.deltaTime;
    }

    /// <summary>Eat the fish: fires the event, then destroys the fish model.</summary>
    public void Eat()
    {
        // Plays on a temporary object, so the sound keeps going after the fish is destroyed.
        if (eatSound != null)
            AudioSource.PlayClipAtPoint(eatSound, transform.position, eatVolume);

        onEaten?.Invoke();
        Destroy(gameObject);
    }
}