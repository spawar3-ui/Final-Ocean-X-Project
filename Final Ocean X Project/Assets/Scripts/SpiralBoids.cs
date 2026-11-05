using UnityEngine;

/// <summary>
/// Fish school = classic boids (separation / alignment / cohesion)
/// + a vortex force that makes the whole school orbit around a center point
/// in a spiral / funnel shape.
/// Setup: put on an empty GameObject, assign a fish prefab, optionally a center Transform.
/// </summary>
public class SpiralBoids : MonoBehaviour
{
    [Header("Spawn")]
    public GameObject fishPrefab;
    public int count = 250;
    public Transform center;                 // what the school circles around (e.g. the diver/player)
    public Vector3 modelForwardFix = Vector3.zero; // Euler offset if your fish model doesn't face +Z

    [Header("Spiral shape")]
    public float ringRadius = 6f;            // average orbit radius
    public float radiusSpread = 3f;          // each fish gets a random +/- offset -> thick, layered swirl
    public float heightRange = 8f;           // vertical thickness of the vortex
    [Range(-0.5f, 0.5f)] public float funnel = 0.15f; // >0 wider at top, <0 wider at bottom
    public float swirlSpeed = 3f;            // orbit speed
    public float swirlWeight = 1.5f;
    public float radialWeight = 1.5f;        // pull toward its own orbit radius
    public float verticalWeight = 1f;        // pull toward its own height
    public float heightDrift = 0.6f;         // slow up/down wave so the spiral breathes

    [Header("Boid rules")]
    public float neighborDist = 2f;
    public float separationDist = 0.7f;
    public float separationWeight = 1.6f;
    public float alignmentWeight = 1.0f;
    public float cohesionWeight = 0.5f;
    public float noiseWeight = 0.4f;

    [Header("Motion")]
    public float maxSpeed = 4.5f;
    public float maxForce = 8f;
    public float turnSpeed = 6f;

    class Boid
    {
        public Transform t;
        public Vector3 vel;
        public float radiusOffset;
        public float heightOffset;
        public float noiseSeed;
    }

    Boid[] boids;

    Vector3 Center => center ? center.position : transform.position;

    void Start()
    {
        boids = new Boid[count];
        Vector3 c = Center;

        for (int i = 0; i < count; i++)
        {
            float ang = Random.value * Mathf.PI * 2f;
            float r = ringRadius + Random.Range(-radiusSpread, radiusSpread);
            float h = Random.Range(-heightRange * 0.5f, heightRange * 0.5f);
            Vector3 pos = c + new Vector3(Mathf.Cos(ang) * r, h, Mathf.Sin(ang) * r);

            GameObject go = Instantiate(fishPrefab, pos, Quaternion.identity, transform);

            // desync swim animation so fish don't flap in unison
            var anim = go.GetComponentInChildren<Animator>();
            if (anim) anim.Play(0, 0, Random.value);

            boids[i] = new Boid
            {
                t = go.transform,
                vel = Random.onUnitSphere * 1f,
                radiusOffset = Random.Range(-radiusSpread, radiusSpread),
                heightOffset = h,
                noiseSeed = Random.value * 100f
            };
        }
    }

    void Update()
    {
        float dt = Time.deltaTime;
        Vector3 c = Center;

        for (int i = 0; i < boids.Length; i++)
        {
            Boid b = boids[i];
            Vector3 pos = b.t.position;
            Vector3 rel = pos - c;

            // ---------- Vortex force ----------
            Vector3 radial = new Vector3(rel.x, 0f, rel.z);
            float r = radial.magnitude;
            Vector3 radialDir = r > 0.001f ? radial / r : Vector3.right;
            Vector3 tangent = Vector3.Cross(Vector3.up, radialDir); // direction of orbit

            float funnelScale = 1f + funnel * (rel.y / Mathf.Max(heightRange, 0.01f));
            float targetR = (ringRadius + b.radiusOffset) * funnelScale;
            float targetY = b.heightOffset + Mathf.Sin(Time.time * 0.5f + b.noiseSeed) * heightDrift;

            Vector3 desired =
                tangent * (swirlSpeed * swirlWeight) +
                radialDir * ((targetR - r) * radialWeight) +
                Vector3.up * ((targetY - rel.y) * verticalWeight);

            Vector3 steer = Vector3.ClampMagnitude(desired - b.vel, maxForce);

            // ---------- Boid rules ----------
            Vector3 sep = Vector3.zero, ali = Vector3.zero, coh = Vector3.zero;
            int n = 0;
            float nd2 = neighborDist * neighborDist;
            float sd2 = separationDist * separationDist;

            for (int j = 0; j < boids.Length; j++)
            {
                if (j == i) continue;
                Vector3 d = pos - boids[j].t.position;
                float d2 = d.sqrMagnitude;
                if (d2 > nd2) continue;

                n++;
                ali += boids[j].vel;
                coh += boids[j].t.position;
                if (d2 < sd2 && d2 > 0.0001f) sep += d / d2;
            }

            if (n > 0)
            {
                ali = (ali / n) - b.vel;
                coh = (coh / n) - pos;
                steer += sep * separationWeight
                       + ali * alignmentWeight
                       + coh * cohesionWeight;
            }

            // small organic wobble
            float t = Time.time * 0.7f + b.noiseSeed;
            steer += new Vector3(
                Mathf.PerlinNoise(t, 0f) - 0.5f,
                Mathf.PerlinNoise(0f, t) - 0.5f,
                Mathf.PerlinNoise(t, t) - 0.5f) * noiseWeight * 2f;

            // ---------- Integrate ----------
            b.vel += Vector3.ClampMagnitude(steer, maxForce) * dt;
            b.vel = Vector3.ClampMagnitude(b.vel, maxSpeed);
            b.t.position += b.vel * dt;

            if (b.vel.sqrMagnitude > 0.001f)
            {
                Quaternion look = Quaternion.LookRotation(b.vel) * Quaternion.Euler(modelForwardFix);
                b.t.rotation = Quaternion.Slerp(b.t.rotation, look, turnSpeed * dt);
            }
        }
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Vector3 c = center ? center.position : transform.position;
        Gizmos.DrawWireSphere(c, ringRadius);
        Gizmos.DrawWireCube(c, new Vector3(ringRadius * 2, heightRange, ringRadius * 2));
    }
}
