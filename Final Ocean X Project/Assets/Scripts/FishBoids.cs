using UnityEngine;

/// Attach to an empty GameObject. Assign a fish prefab and press Play.
/// Fish stay inside a box (this object's position +/- bounds) and flock using
/// separation, alignment and cohesion.
public class FishBoids : MonoBehaviour
{
    [Header("Spawning")]
    public GameObject fishPrefab;
    public int count = 150;
    public Vector3 bounds = new Vector3(20, 8, 20);

    [Header("Speed")]
    public float minSpeed = 2f;
    public float maxSpeed = 4.5f;
    public float turnSpeed = 4f;

    [Header("Flocking")]
    public float neighbourRadius = 3f;
    public float separationRadius = 1f;
    public float separationWeight = 1.6f;
    public float alignmentWeight = 1.0f;
    public float cohesionWeight = 0.8f;
    public float boundsWeight = 2.0f;

    [Header("Optional")]
    public Transform predator;          // fish flee from this if assigned
    public float predatorRadius = 6f;
    public float predatorWeight = 4f;

    Transform[] fish;
    Vector3[] vel;
    float[] speedBias;

    void Start()
    {
        fish = new Transform[count];
        vel = new Vector3[count];
        speedBias = new float[count];

        for (int i = 0; i < count; i++)
        {
            Vector3 p = transform.position + new Vector3(
                Random.Range(-bounds.x, bounds.x) * 0.5f,
                Random.Range(-bounds.y, bounds.y) * 0.5f,
                Random.Range(-bounds.z, bounds.z) * 0.5f);

            fish[i] = Instantiate(fishPrefab, p, Random.rotation, transform).transform;
            vel[i] = Random.onUnitSphere * Random.Range(minSpeed, maxSpeed);
            speedBias[i] = Random.Range(0.85f, 1.15f);   // each fish a bit different
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;

        for (int i = 0; i < count; i++)
        {
            Vector3 pos = fish[i].position;
            Vector3 sep = Vector3.zero, ali = Vector3.zero, coh = Vector3.zero;
            int n = 0;

            for (int j = 0; j < count; j++)
            {
                if (i == j) continue;
                Vector3 offset = fish[j].position - pos;
                float d2 = offset.sqrMagnitude;
                if (d2 > neighbourRadius * neighbourRadius) continue;

                n++;
                ali += vel[j];
                coh += fish[j].position;
                if (d2 < separationRadius * separationRadius && d2 > 0.0001f)
                    sep -= offset / d2;                  // stronger push when closer
            }

            Vector3 steer = Vector3.zero;
            if (n > 0)
            {
                ali = (ali / n).normalized;
                coh = ((coh / n) - pos).normalized;
                steer += sep * separationWeight + ali * alignmentWeight + coh * cohesionWeight;
            }

            // Stay inside the box: push back toward centre when outside
            Vector3 local = pos - transform.position;
            Vector3 half = bounds * 0.5f;
            Vector3 push = Vector3.zero;
            if (Mathf.Abs(local.x) > half.x) push.x = -Mathf.Sign(local.x);
            if (Mathf.Abs(local.y) > half.y) push.y = -Mathf.Sign(local.y);
            if (Mathf.Abs(local.z) > half.z) push.z = -Mathf.Sign(local.z);
            steer += push * boundsWeight;

            // Flee predator / player
            if (predator != null)
            {
                Vector3 away = pos - predator.position;
                float dist = away.magnitude;
                if (dist < predatorRadius)
                    steer += away.normalized * predatorWeight * (1f - dist / predatorRadius);
            }

            // Blend toward desired direction, clamp speed
            Vector3 desired = vel[i].normalized + steer * dt * turnSpeed;
            float speed = Mathf.Clamp(vel[i].magnitude, minSpeed, maxSpeed) * speedBias[i];
            if (predator != null && (pos - predator.position).sqrMagnitude < predatorRadius * predatorRadius)
                speed = maxSpeed * 1.4f;                 // panic burst
            vel[i] = desired.normalized * Mathf.Clamp(speed, minSpeed, maxSpeed * 1.4f);

            fish[i].position += vel[i] * dt;
            if (vel[i].sqrMagnitude > 0.001f)
                fish[i].rotation = Quaternion.Slerp(
                    fish[i].rotation, Quaternion.LookRotation(vel[i]), dt * turnSpeed);
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(transform.position, bounds);
    }
}
