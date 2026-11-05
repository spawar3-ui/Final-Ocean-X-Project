using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Put this on a cube with a Box Collider (Is Trigger = ON).
/// When the player enters it, the shark spawns behind them, chases them,
/// lunges at random moments (jerking the camera), and when a lunge lands
/// the scene restarts.
/// </summary>
[RequireComponent(typeof(Collider))]
public class SharkAmbush : MonoBehaviour
{
    [Header("References")]
    public GameObject sharkPrefab;          // shark prefab to spawn
    public Transform spawnPoint;            // optional - if empty, spawns behind the player
    public Camera playerCamera;             // optional - falls back to Camera.main
    public Light warningLight;              // optional - turns red when triggered
    public Color warningColor = Color.red;
    public string playerTag = "Player";
    public Collider despawnZone;            // optional - when the player enters this zone, the shark is destroyed

    [Header("Spawn")]
    public float spawnDistanceBehind = 25f;
    public float spawnHeightOffset = 0f;

    [Header("Chase")]
    public float chaseSpeed = 9f;
    public float turnSpeed = 3f;
    public float followDistance = 12f;      // how far behind the player the shark trails
    public float weaveAmount = 3f;          // side-to-side swim motion
    public float weaveSpeed = 1.5f;
    public float targetHeightOffset = 1f;   // aim point above the player's pivot

    [Header("Random Attacks")]
    public Vector2 attackDelayRange = new Vector2(3f, 7f);  // seconds between lunges
    public float lungeSpeed = 22f;
    public float hitDistance = 2.5f;        // how close counts as a bite
    [Range(0f, 1f)] public float hitChance = 0.5f;
    public int maxMissesBeforeHit = 3;      // guarantees a hit eventually
    public float lungeTimeout = 2.5f;
    public float recoverTime = 1.5f;        // swims past after a miss

    [Header("Camera Jerk")]
    public float hitShakeDuration = 0.5f;
    public float hitShakeMagnitude = 0.6f;
    public float missShakeDuration = 0.25f;
    public float missShakeMagnitude = 0.25f;
    public float fovPunch = 12f;            // extra FOV on impact
    public float restartDelay = 0.6f;       // pause after the bite before reloading

    [Header("Obstacle Avoidance")]
    public LayerMask obstacleMask = ~0;     // set to your mountain + ground layers (Everything also works)
    public float sharkRadius = 1.5f;        // roughly half the shark's width
    public float lookAhead = 8f;            // how far ahead it spots mountains and turns away
    [Range(0f, 1f)] public float avoidStrength = 0.9f;
    public float skin = 0.05f;

    [Header("Model")]
    public Vector3 modelRotationOffset;     // use if your shark model faces the wrong way

    enum State { Idle, Chasing, Lunging, Recovering, Done }
    State state = State.Idle;

    Transform player;
    Transform shark;
    Transform cam;
    Camera camComponent;
    Vector3 camBasePos;
    float camBaseFov;

    float attackTimer;
    float stateTimer;
    int misses;
    float weaveClock;
    Vector3 recoverTarget;

    float shakeLeft, shakeDuration, shakeMagnitude;
    bool shaking;

    void Reset()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (state != State.Idle || !other.CompareTag(playerTag)) return;
        StartAmbush(other.transform);
    }

    void StartAmbush(Transform playerTransform)
    {
        player = playerTransform;

        camComponent = playerCamera != null ? playerCamera : Camera.main;
        if (camComponent != null)
        {
            cam = camComponent.transform;
            camBasePos = cam.localPosition;
            camBaseFov = camComponent.fieldOfView;
        }

        if (warningLight != null) warningLight.color = warningColor;

        Vector3 spawnPos = spawnPoint != null
            ? spawnPoint.position
            : player.position - player.forward * spawnDistanceBehind + Vector3.up * spawnHeightOffset;

        // if the spawn spot is inside a mountain or the ground, nudge it toward the player until it's clear
        if (Physics.CheckSphere(spawnPos, sharkRadius, obstacleMask, QueryTriggerInteraction.Ignore))
        {
            for (int i = 0; i < 30; i++)
            {
                spawnPos = Vector3.MoveTowards(spawnPos, player.position + Vector3.up * targetHeightOffset, 2f);
                if (!Physics.CheckSphere(spawnPos, sharkRadius, obstacleMask, QueryTriggerInteraction.Ignore)) break;
            }
        }

        shark = Instantiate(sharkPrefab, spawnPos, Quaternion.identity).transform;
        shark.rotation = Quaternion.LookRotation(AimPoint() - spawnPos) * Quaternion.Euler(modelRotationOffset);

        attackTimer = Random.Range(attackDelayRange.x, attackDelayRange.y);
        state = State.Chasing;
    }

    Vector3 AimPoint()
    {
        return player.position + Vector3.up * targetHeightOffset;
    }

    void Update()
    {
        if (shark == null || state == State.Idle || state == State.Done) return;

        // player reached the safe zone: the shark disappears
        if (despawnZone != null)
        {
            Vector3 p = player.position;
            if ((despawnZone.ClosestPoint(p) - p).sqrMagnitude < 0.0001f)
            {
                DespawnShark();
                return;
            }
        }

        switch (state)
        {
            case State.Chasing:   UpdateChase();   break;
            case State.Lunging:   UpdateLunge();   break;
            case State.Recovering: UpdateRecover(); break;
        }
    }

    void UpdateChase()
    {
        weaveClock += Time.deltaTime * weaveSpeed;

        // Trail behind the player and weave side to side
        Vector3 desired = player.position
                          - player.forward * followDistance
                          + player.right * Mathf.Sin(weaveClock) * weaveAmount
                          + Vector3.up * targetHeightOffset;

        float dist = Vector3.Distance(shark.position, desired);
        float speed = dist > followDistance * 2f ? chaseSpeed * 2f : chaseSpeed; // catch up if left behind
        MoveSafe(desired, speed);

        FaceTowards(shark.position + lastMoveDir, turnSpeed);

        attackTimer -= Time.deltaTime;
        if (attackTimer <= 0f)
        {
            stateTimer = lungeTimeout;
            state = State.Lunging;
        }
    }

    void UpdateLunge()
    {
        Vector3 target = AimPoint();
        MoveSafe(target, lungeSpeed);
        FaceTowards(shark.position + lastMoveDir, turnSpeed * 3f);

        stateTimer -= Time.deltaTime;

        if (Vector3.Distance(shark.position, target) <= hitDistance)
        {
            bool hit = misses >= maxMissesBeforeHit || Random.value < hitChance;
            if (hit) Hit();
            else Miss();
        }
        else if (stateTimer <= 0f)
        {
            // couldn't reach the player in time, back off and retry
            BackOff();
        }
    }

    void UpdateRecover()
    {
        MoveSafe(recoverTarget, chaseSpeed);
        FaceTowards(shark.position + lastMoveDir, turnSpeed);

        stateTimer -= Time.deltaTime;
        if (stateTimer <= 0f)
        {
            attackTimer = Random.Range(attackDelayRange.x, attackDelayRange.y);
            state = State.Chasing;
        }
    }

    void Miss()
    {
        misses++;
        Shake(missShakeDuration, missShakeMagnitude, fovPunch * 0.4f);
        BackOff();
    }

    void BackOff()
    {
        // keep going past the player, then circle back
        recoverTarget = shark.position + shark.forward * 15f + Vector3.up * 3f;
        stateTimer = recoverTime;
        state = State.Recovering;
    }

    void Hit()
    {
        state = State.Done;
        Shake(hitShakeDuration, hitShakeMagnitude, fovPunch);
        StartCoroutine(RestartRoutine());
    }

    void DespawnShark()
    {
        if (shark != null) Destroy(shark.gameObject);
        shark = null;
        state = State.Done;   // ambush is over, it can't trigger again
    }

    IEnumerator RestartRoutine()
    {
        yield return new WaitForSeconds(restartDelay);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ---------- Obstacle avoidance (mountains + ground) ----------

    Vector3 lastMoveDir;

    // Sphere-cast that ignores the shark, the player and trigger volumes.
    // Works with non-convex mesh colliders like your mountains.
    bool Cast(Vector3 origin, Vector3 dir, float dist, out RaycastHit best)
    {
        best = default;
        float nearest = float.MaxValue;
        bool found = false;

        RaycastHit[] hits = Physics.SphereCastAll(origin, sharkRadius, dir, dist,
                                                  obstacleMask, QueryTriggerInteraction.Ignore);
        foreach (RaycastHit h in hits)
        {
            Transform t = h.collider.transform;
            if (t.IsChildOf(shark) || t.IsChildOf(player)) continue;
            if (h.distance <= 0f) continue;   // already overlapping: let it swim out instead of freezing
            if (h.distance < nearest)
            {
                nearest = h.distance;
                best = h;
                found = true;
            }
        }
        return found;
    }

    void MoveSafe(Vector3 target, float speed)
    {
        Vector3 to = target - shark.position;
        float dist = to.magnitude;
        if (dist < 0.01f) return;
        Vector3 dir = to / dist;

        // 1) steer away from anything ahead (smooth turn along the surface)
        if (Cast(shark.position, dir, lookAhead, out RaycastHit ahead))
        {
            float closeness = 1f - Mathf.Clamp01(ahead.distance / lookAhead);
            Vector3 slide = Vector3.ProjectOnPlane(dir, ahead.normal).normalized;
            if (slide.sqrMagnitude < 0.001f) slide = Vector3.up;   // head-on: go up and over
            dir = Vector3.Slerp(dir, slide, closeness * avoidStrength).normalized;
        }

        // 2) never step into a collider: stop at the surface and slide along it
        float step = Mathf.Min(speed * Time.deltaTime, dist);
        Vector3 pos = shark.position;
        for (int i = 0; i < 2 && step > 0.0001f; i++)
        {
            if (Cast(pos, dir, step + skin, out RaycastHit hit))
            {
                float free = Mathf.Max(0f, hit.distance - skin);
                pos += dir * free;
                step -= free;
                dir = Vector3.ProjectOnPlane(dir, hit.normal).normalized;
                if (dir.sqrMagnitude < 0.001f) break;
            }
            else
            {
                pos += dir * step;
                step = 0f;
            }
        }

        Vector3 delta = pos - shark.position;
        if (delta.sqrMagnitude > 1e-6f) lastMoveDir = delta.normalized;
        shark.position = pos;
    }

    void FaceTowards(Vector3 point, float speed)
    {
        Vector3 dir = point - shark.position;
        if (dir.sqrMagnitude < 0.001f) return;
        Quaternion look = Quaternion.LookRotation(dir) * Quaternion.Euler(modelRotationOffset);
        shark.rotation = Quaternion.Slerp(shark.rotation, look, speed * Time.deltaTime);
    }

    // ---------- Camera jerk ----------

    float currentFovPunch;

    void Shake(float duration, float magnitude, float fov)
    {
        shakeDuration = duration;
        shakeLeft = duration;
        shakeMagnitude = magnitude;
        currentFovPunch = fov;
        shaking = true;
    }

    void LateUpdate()
    {
        if (cam == null || !shaking) return;

        shakeLeft -= Time.deltaTime;
        if (shakeLeft <= 0f)
        {
            // restore
            cam.localPosition = camBasePos;
            camComponent.fieldOfView = camBaseFov;
            shaking = false;
            return;
        }

        float t = shakeLeft / shakeDuration; // 1 -> 0, strong jerk that fades out
        cam.localPosition = camBasePos + Random.insideUnitSphere * shakeMagnitude * t;
        camComponent.fieldOfView = camBaseFov + currentFovPunch * t;
    }
}