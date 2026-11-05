using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns many copies of ONE shark prefab and makes them circle in a spiral / vortex,
/// like sharks swirling around a point (or around the player).
///
/// Setup:
/// 1. Create an empty GameObject where the spiral should be centered, add this script.
/// 2. Drag your shark prefab into "Shark Prefab" and pick "Shark Count".
/// 3. (Optional) Drag the player or any Transform into "Center" so the sharks circle it.
/// 4. Press Play. Select the object to see the spiral preview rings in the Scene view.
///
/// To start it later (e.g. from a trigger), untick "Spawn On Start" and call Spawn()
/// from another script:  FindObjectOfType<SharkSpiral>().Spawn();
/// </summary>
public class SharkSpiral : MonoBehaviour
{
    [Header("Prefab")]
    public GameObject sharkPrefab;
    [Min(1)] public int sharkCount = 12;
    public bool spawnOnStart = true;

    [Header("Spiral Shape")]
    [Tooltip("What the sharks circle around. Leave empty to use this object's position.")]
    public Transform center;
    [Tooltip("Radius of the lowest / innermost shark.")]
    public float innerRadius = 8f;
    [Tooltip("Radius of the highest / outermost shark.")]
    public float outerRadius = 25f;
    [Tooltip("Total height from the lowest shark to the highest shark.")]
    public float verticalSpread = 20f;
    [Tooltip("How many full turns the starting spiral arm makes (1 = one full circle).")]
    public float spiralTurns = 1.5f;
    public bool clockwise = false;

    [Header("Movement")]
    [Tooltip("Swim speed in units per second.")]
    public float swimSpeed = 6f;
    [Tooltip("Each shark gets a random speed between swimSpeed x (1 - this) and swimSpeed x (1 + this).")]
    [Range(0f, 0.5f)] public float speedVariation = 0.2f;
    public float radiusJitter = 2f;
    public float heightJitter = 2f;
    [Tooltip("Gentle up/down drifting so it doesn't look mechanical.")]
    public float bobAmount = 1.5f;
    public float bobSpeed = 0.5f;

    [Header("Look")]
    [Tooltip("Random scale multiplier range for each shark.")]
    public Vector2 scaleRange = new Vector2(0.8f, 1.2f);
    [Tooltip("Leans the shark into the turn. Use a negative number if it leans the wrong way.")]
    public float bankAngle = 12f;
    [Tooltip("If your shark swims sideways, set this to 90 or -90 (depends on how the model was exported).")]
    public float modelYawOffset = 0f;

    class SharkData
    {
        public Transform t;
        public float angle;
        public float radius;
        public float height;
        public float speed;
        public float bobPhase;
    }

    readonly List<SharkData> sharks = new List<SharkData>();

    void Start()
    {
        if (spawnOnStart) Spawn();
    }

    /// <summary>Creates the sharks (clears old ones first). Call this from a trigger if you want.</summary>
    public void Spawn()
    {
        Clear();

        if (sharkPrefab == null)
        {
            Debug.LogWarning("SharkSpiral: no Shark Prefab assigned.");
            return;
        }

        for (int i = 0; i < sharkCount; i++)
        {
            // t goes 0 -> 1 along the spiral arm
            float t = sharkCount == 1 ? 0.5f : (float)i / (sharkCount - 1);

            var data = new SharkData
            {
                radius = Mathf.Max(1f, Mathf.Lerp(innerRadius, outerRadius, t) + Random.Range(-radiusJitter, radiusJitter)),
                height = Mathf.Lerp(-verticalSpread * 0.5f, verticalSpread * 0.5f, t) + Random.Range(-heightJitter, heightJitter),
                angle = t * spiralTurns * Mathf.PI * 2f,
                speed = swimSpeed * Random.Range(1f - speedVariation, 1f + speedVariation),
                bobPhase = Random.Range(0f, Mathf.PI * 2f)
            };

            GameObject go = Instantiate(sharkPrefab, transform);
            go.name = sharkPrefab.name + "_" + i;
            go.transform.localScale = sharkPrefab.transform.localScale * Random.Range(scaleRange.x, scaleRange.y);

            // Desync the swim animation so they don't all flap in unison
            Animator anim = go.GetComponentInChildren<Animator>();
            if (anim != null) anim.speed = Random.Range(0.85f, 1.15f);

            data.t = go.transform;
            sharks.Add(data);
            ApplyTransform(data); // place it right away so it doesn't pop in at the origin
        }
    }

    /// <summary>Removes all spawned sharks.</summary>
    public void Clear()
    {
        foreach (var s in sharks)
            if (s.t != null) Destroy(s.t.gameObject);
        sharks.Clear();
    }

    void Update()
    {
        float dir = clockwise ? -1f : 1f;

        foreach (var s in sharks)
        {
            if (s.t == null) continue;
            // angular speed = linear speed / radius, so every shark swims at its own real speed
            s.angle += dir * (s.speed / s.radius) * Time.deltaTime;
            ApplyTransform(s);
        }
    }

    void ApplyTransform(SharkData s)
    {
        float dir = clockwise ? -1f : 1f;
        Vector3 c = center != null ? center.position : transform.position;

        float bob = Mathf.Sin(Time.time * bobSpeed + s.bobPhase);
        float y = s.height + bob * bobAmount;

        s.t.position = c + new Vector3(Mathf.Cos(s.angle) * s.radius, y, Mathf.Sin(s.angle) * s.radius);

        // Face along the circle (tangent), with a tiny pitch from the up/down bobbing
        Vector3 forward = new Vector3(-Mathf.Sin(s.angle), 0f, Mathf.Cos(s.angle)) * dir;
        float verticalVelocity = Mathf.Cos(Time.time * bobSpeed + s.bobPhase) * bobAmount * bobSpeed;
        forward += Vector3.up * (verticalVelocity / Mathf.Max(0.01f, s.speed));
        forward.Normalize();

        s.t.rotation = Quaternion.LookRotation(forward, Vector3.up)
                       * Quaternion.Euler(0f, 0f, dir * bankAngle)
                       * Quaternion.Euler(0f, modelYawOffset, 0f);
    }

    // Preview rings in the Scene view (select the object to see them)
    void OnDrawGizmosSelected()
    {
        Vector3 c = center != null ? center.position : transform.position;
        Gizmos.color = Color.cyan;
        DrawCircle(c + Vector3.up * (-verticalSpread * 0.5f), innerRadius);
        DrawCircle(c + Vector3.up * (verticalSpread * 0.5f), outerRadius);
        Gizmos.color = new Color(0f, 1f, 1f, 0.3f);
        Gizmos.DrawLine(c + Vector3.up * (-verticalSpread * 0.5f), c + Vector3.up * (verticalSpread * 0.5f));
    }

    static void DrawCircle(Vector3 pos, float radius)
    {
        const int steps = 48;
        Vector3 prev = pos + new Vector3(radius, 0f, 0f);
        for (int i = 1; i <= steps; i++)
        {
            float a = i / (float)steps * Mathf.PI * 2f;
            Vector3 next = pos + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
}
