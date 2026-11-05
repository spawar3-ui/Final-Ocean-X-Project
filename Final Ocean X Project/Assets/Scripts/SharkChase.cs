using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Put this on the shark. It stays dormant (hidden, not moving) until Activate() is called,
/// usually by a SharkTrigger. After that it chases the player through the canyons:
///  - If it can see the player, it swims to the player's BACK and lunges from behind.
///  - If a mountain is in the way, it follows the player's own trail (breadcrumbs).
///  - Whisker casts push it away from the walls so it doesn't scrape the mesh collider.
///  - After it has knocked the player maxHits times, it stops and disappears.
/// Needs: mesh colliders on the mountains/seabed. The shark does not need a Rigidbody.
/// </summary>
public class SharkChase : MonoBehaviour
{
    [Header("Activation")]
    [Tooltip("Off = shark stays hidden and idle until Activate() is called (e.g. by a SharkTrigger).")]
    public bool startActive = false;
    [Tooltip("Optional. The shark teleports here when activated. Put it somewhere the player can't see.")]
    public Transform spawnPoint;
    [Tooltip("Seconds after activation before it starts moving.")]
    public float startDelay = 3f;

    [Header("Target")]
    public Transform player;
    [Tooltip("Aim at this height offset above the player's pivot (e.g. the middle of the body).")]
    public Vector3 aimOffset = Vector3.zero;

    [Header("Movement")]
    public float speed = 4f;
    [Tooltip("Speed multiplier while it has a clear line to the player.")]
    public float chargeMultiplier = 1.3f;
    public float turnSpeed = 2.5f;
    [Tooltip("If the shark falls this far behind, it speeds up so it never loses you.")]
    public float catchUpDistance = 30f;
    public float catchUpMultiplier = 1.6f;
    [Tooltip("If your shark model swims sideways or backwards, adjust this (e.g. Y = 90 or -90).")]
    public Vector3 modelRotationOffset = Vector3.zero;

    [Header("Stay Behind The Player")]
    [Tooltip("On = shark gets behind you and lunges from there. Off = straight-at-you chase.")]
    public bool attackFromBehind = true;
    [Tooltip("How far behind the player it waits between attacks.")]
    public float behindDistance = 7f;
    [Tooltip("Within this many degrees of 'directly behind' it is allowed to lunge.")]
    public float lungeAngle = 40f;
    [Tooltip("Farther than this from the player it just charges straight in. Closer, it works its way behind you.")]
    public float circleStartDistance = 20f;
    [Tooltip("If the shark is in front of or beside you, it swings around you at this distance to get behind. Bigger = wider berth.")]
    public float sweepRadius = 10f;
    [Tooltip("How quickly 'behind' follows your direction of travel. Lower = steadier.")]
    public float headingSmoothing = 3f;

    [Header("Wall Avoidance")]
    public LayerMask obstacleMask = ~0;
    [Tooltip("Roughly half the shark's width.")]
    public float bodyRadius = 1f;
    public float avoidDistance = 8f;
    public float avoidStrength = 2.5f;
    public float whiskerAngle = 35f;

    [Header("Following The Player's Trail")]
    public float crumbSpacing = 2f;
    public int maxCrumbs = 300;

    [Header("Bite / Knock-back (player never dies)")]
    [Tooltip("Shark this close to the player = a hit.")]
    public float hitDistance = 2f;
    [Tooltip("Seconds before the shark can hit again.")]
    public float hitCooldown = 2.5f;
    public float knockForce = 9f;
    [Tooltip("Extra upward shove so it feels like a real bump.")]
    public float knockUp = 2f;
    [Range(0f, 1f)] public float shakeAmount = 0.9f;
    [Tooltip("After a hit the shark is stunned and slow for this long, so you can get away.")]
    public float recoilTime = 1.2f;
    [Range(0f, 1f)] public float recoilSpeedMultiplier = 0.2f;
    public int hitsTaken = 0;
    public UnityEvent onHitPlayer; // hook up sound, red flash, UI...

    [Header("Disappear After Hits")]
    [Tooltip("After this many hits the shark stops chasing and disappears.")]
    public int maxHits = 3;
    public bool disappearAfterMaxHits = true;
    public UnityEvent onSharkGone; // optional: hook up music change, next objective, etc.

    public bool IsActive => active;
    public bool IsFinished => finished;

    readonly List<Vector3> crumbs = new List<Vector3>();
    readonly RaycastHit[] hits = new RaycastHit[16];
    Renderer[] renderers;
    Collider[] colliders;
    Vector3 lastCrumb;
    Vector3 lastPlayerPos;
    Vector3 heading = Vector3.forward; // flat direction the player is travelling
    float timer;
    float nextHitTime;
    float recoilUntil;
    bool active;
    bool finished;
    FirstPersonController playerController;

    void Awake()
    {
        renderers = GetComponentsInChildren<Renderer>(true);
        colliders = GetComponentsInChildren<Collider>(true);

        if (startActive) active = true;
        else SetVisible(false); // dormant: invisible and not bump-able until activated
    }

    void Start()
    {
        if (player == null)
        {
            var p = FindFirstObjectByType<FirstPersonController>();
            if (p != null) player = p.transform;
        }
        if (player != null)
        {
            lastCrumb = player.position;
            lastPlayerPos = player.position;
            playerController = player.GetComponentInParent<FirstPersonController>();

            Vector3 f = player.forward;
            f.y = 0f;
            if (f.sqrMagnitude > 0.001f) heading = f.normalized;
        }
    }

    /// <summary>Wakes the shark up. Called by SharkTrigger, or call it from your own code.</summary>
    public void Activate()
    {
        if (active || finished) return;

        active = true;
        timer = 0f;
        SetVisible(true);

        if (spawnPoint != null)
            transform.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);
    }

    void SetVisible(bool visible)
    {
        if (renderers != null)
            foreach (var r in renderers) if (r != null) r.enabled = visible;
        if (colliders != null)
            foreach (var c in colliders) if (c != null) c.enabled = visible;
    }

    void HitPlayer(Vector3 playerPos)
    {
        // Shove the player away from the shark.
        Vector3 away = (playerPos - transform.position).normalized;
        Vector3 impulse = away * knockForce + Vector3.up * knockUp;

        if (playerController != null)
            playerController.Knock(impulse, shakeAmount);

        hitsTaken++;
        nextHitTime = Time.time + hitCooldown;
        recoilUntil = Time.time + recoilTime;
        onHitPlayer?.Invoke();

        // Last hit: stop chasing and vanish.
        if (disappearAfterMaxHits && hitsTaken >= maxHits)
            StartCoroutine(VanishRoutine());
    }

    IEnumerator VanishRoutine()
    {
        finished = true; // stops Update from moving/chasing

        // Let the knock-back play out before the shark vanishes.
        yield return new WaitForSeconds(recoilTime);

        onSharkGone?.Invoke();
        gameObject.SetActive(false);
    }

    void Update()
    {
        if (player == null || finished) return;

        // Keep tracking the player even while dormant, so the trail is ready when the shark wakes up.
        RecordCrumbs();
        UpdateHeading();

        if (!active) return;

        timer += Time.deltaTime;
        if (timer < startDelay) return;

        Vector3 playerPos = player.position + aimOffset;
        float distToPlayer = Vector3.Distance(transform.position, playerPos);

        if (distToPlayer <= hitDistance && Time.time >= nextHitTime)
        {
            HitPlayer(playerPos);
            if (finished) return; // that was the last hit, don't move this frame
        }

        bool recoiling = Time.time < recoilUntil;
        bool cooling = Time.time < nextHitTime; // just bit, so back off instead of crowding the camera

        bool canSeePlayer = !IsBlocked(transform.position, playerPos);
        bool holding = false;
        Vector3 target;

        if (canSeePlayer)
            target = attackFromBehind
                ? ChooseBehindTarget(playerPos, distToPlayer, recoiling || cooling, out holding)
                : playerPos;
        else
            target = PickCrumbTarget(playerPos);

        Vector3 desired = holding
            ? (playerPos - transform.position).normalized   // parked behind you, just face you
            : (target - transform.position).normalized;

        if (!holding)
            desired = (desired + Avoidance()).normalized;

        // Turn smoothly, then swim forward.
        if (desired.sqrMagnitude > 0.0001f)
        {
            Quaternion look = Quaternion.LookRotation(desired) * Quaternion.Euler(modelRotationOffset);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, turnSpeed * Time.deltaTime);
        }

        if (holding) return; // waiting behind the player, don't drift

        float currentSpeed = speed;
        if (canSeePlayer) currentSpeed *= chargeMultiplier;
        if (distToPlayer > catchUpDistance) currentSpeed *= catchUpMultiplier;
        if (recoiling) currentSpeed *= recoilSpeedMultiplier; // stunned after a hit

        Vector3 forward = transform.rotation * Quaternion.Inverse(Quaternion.Euler(modelRotationOffset)) * Vector3.forward;
        transform.position += forward * currentSpeed * Time.deltaTime;
    }

    // ---------------- Staying behind the player ----------------

    /// <summary>
    /// The player's "back" is opposite their direction of TRAVEL (not where the camera looks),
    /// so looking around never makes the shark swing around you.
    /// </summary>
    void UpdateHeading()
    {
        Vector3 delta = player.position - lastPlayerPos;
        lastPlayerPos = player.position;
        delta.y = 0f;

        if (Time.deltaTime <= 0f) return;

        // Only update while actually moving, so standing still keeps the last known heading.
        if (delta.magnitude / Time.deltaTime > 0.3f)
        {
            Vector3 dir = delta.normalized;
            Vector3 blended = Vector3.Lerp(heading, dir, Mathf.Clamp01(headingSmoothing * Time.deltaTime));
            heading = blended.sqrMagnitude > 0.001f ? blended.normalized : dir;
        }
    }

    /// <summary>
    /// Picks where to swim while the player is visible.
    /// Far away: charge straight in. Closer: get behind the player and lunge.
    /// If the shark is in front of or beside the player, it swings out wide around the side
    /// (never through or over the player). After a bite it retreats behind and waits.
    /// </summary>
    Vector3 ChooseBehindTarget(Vector3 playerPos, float distToPlayer, bool backOff, out bool holding)
    {
        holding = false;

        // Far away: just close the gap.
        if (distToPlayer > circleStartDistance && !backOff)
            return playerPos;

        Vector3 back = -heading;

        Vector3 toShark = transform.position - playerPos;
        toShark.y = 0f;
        Vector3 toSharkDir = toShark.sqrMagnitude > 0.01f ? toShark.normalized : back;

        // 1 = directly behind the player, 0 = beside, -1 = directly in front.
        float behindness = Vector3.Dot(toSharkDir, back);
        bool isBehind = behindness >= Mathf.Cos(lungeAngle * Mathf.Deg2Rad);

        // Behind the player and allowed to attack: lunge.
        if (isBehind && !backOff)
            return playerPos;

        // The spot just behind the player, level with them.
        Vector3 point = playerPos + back * behindDistance;
        point.y = playerPos.y;

        // In front of or beside the player: swing wide round the side first, so it passes by, not over.
        if (behindness < 0.3f)
        {
            Vector3 right = Vector3.Cross(Vector3.up, heading);
            float side = Vector3.Dot(toSharkDir, right) >= 0f ? 1f : -1f;

            point = playerPos + right * side * sweepRadius + back * (sweepRadius * 0.6f);
            point.y = playerPos.y;
        }
        // Arrived behind the player after a bite: park there until the cooldown ends.
        else if (backOff && isBehind && Vector3.Distance(transform.position, point) < 1f)
        {
            holding = true;
            return point;
        }

        // Can't swim there (wall in the way)? Follow the player's trail instead.
        if (IsBlocked(transform.position, point))
            return PickCrumbTarget(playerPos);

        return point;
    }

    // ---------------- Player trail ----------------

    void RecordCrumbs()
    {
        if (Vector3.Distance(player.position, lastCrumb) < crumbSpacing) return;

        crumbs.Add(player.position + aimOffset);
        lastCrumb = player.position;

        if (crumbs.Count > maxCrumbs) crumbs.RemoveAt(0);
    }

    /// <summary>Newest crumb the shark can see. Older crumbs behind it are dropped.</summary>
    Vector3 PickCrumbTarget(Vector3 fallback)
    {
        for (int i = crumbs.Count - 1; i >= 0; i--)
        {
            if (!IsBlocked(transform.position, crumbs[i]))
            {
                if (i > 0) crumbs.RemoveRange(0, i);
                return crumbs[0];
            }
        }

        // Can't see any crumb (e.g. just got shoved by avoidance): head for the oldest one.
        return crumbs.Count > 0 ? crumbs[0] : fallback;
    }

    // ---------------- Obstacles ----------------

    bool IsBlocked(Vector3 from, Vector3 to)
    {
        Vector3 dir = to - from;
        float dist = dir.magnitude;
        if (dist < 0.01f) return false;

        int count = Physics.SphereCastNonAlloc(from, bodyRadius * 0.8f, dir / dist, hits,
            dist, obstacleMask, QueryTriggerInteraction.Ignore);

        for (int i = 0; i < count; i++)
        {
            if (IsSelfOrPlayer(hits[i].collider.transform)) continue;
            return true;
        }
        return false;
    }

    /// <summary>Pushes away from nearby walls, in front and to the sides.</summary>
    Vector3 Avoidance()
    {
        Vector3 forward = transform.rotation * Quaternion.Inverse(Quaternion.Euler(modelRotationOffset)) * Vector3.forward;
        Vector3 up = transform.up;
        Vector3 right = Vector3.Cross(up, forward).normalized;

        Vector3[] whiskers =
        {
            forward,
            Quaternion.AngleAxis(whiskerAngle, up) * forward,
            Quaternion.AngleAxis(-whiskerAngle, up) * forward,
            Quaternion.AngleAxis(whiskerAngle, right) * forward,
            Quaternion.AngleAxis(-whiskerAngle, right) * forward,
        };

        Vector3 push = Vector3.zero;

        foreach (Vector3 dir in whiskers)
        {
            int count = Physics.SphereCastNonAlloc(transform.position, bodyRadius, dir, hits,
                avoidDistance, obstacleMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                if (IsSelfOrPlayer(hits[i].collider.transform)) continue;

                float closeness = 1f - hits[i].distance / avoidDistance; // 1 = touching
                push += hits[i].normal * closeness * avoidStrength;
            }
        }

        return push;
    }

    bool IsSelfOrPlayer(Transform t)
    {
        return t.root == transform.root || t.root == player.root;
    }
}