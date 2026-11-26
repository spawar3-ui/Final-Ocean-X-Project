using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// Shark-style chaser. Put this on the sphere (it can start disabled).
// Behavior loop:  STALK (lurk behind the player, weaving)
//              -> LUNGE (fast attack run, can miss)
//              -> RETREAT (peel off to the side and drop back)
//              -> STALK again ... at random times.
public class SphereChaser : MonoBehaviour
{
    enum State { Stalk, Lunge, Retreat, Restarting }

    [Header("References")]
    public Transform player;
    public CameraShake cameraShake;
    public BloodScreenFlash bloodScreen;   // drag the blood UI Image here

    [Header("Hard hit sound")]
    public AudioClip hardHitSound;         // drag your sound file here
    [Range(0f, 1f)] public float hardHitVolume = 1f;
    public bool waitForSoundToFinish = false; // true = don't restart until the sound is done
    AudioSource audioSource;

    [Header("Stalking (lurking behind the player)")]
    public Vector2 stalkDistance = new Vector2(6f, 10f);  // random distance behind the player
    public Vector2 stalkTime = new Vector2(3f, 7f);       // random seconds before it lunges
    public float weaveAmount = 2.5f;                      // side-to-side swim
    public float weaveSpeed = 1.2f;
    public float maxStalkSpeed = 14f;                     // top speed while catching up
    public float catchUpFactor = 1.5f;
    public float heightOffset = 0f;                       // height relative to the player

    [Header("Lunge (attack run)")]
    public float lungeSpeed = 13f;                        // keep this higher than the player's run speed
    public Vector2 lungeTime = new Vector2(1.0f, 1.6f);   // how long an attack run lasts
    public float lungeTurnRate = 70f;                     // degrees/sec. lower = easier to dodge
    public float leadTime = 0.5f;                         // aims ahead of the player by this many seconds
    public float hitRadius = 1.5f;
    public float nearMissRadius = 3.5f;

    [Header("Retreat (after a lunge)")]
    public Vector2 retreatTime = new Vector2(1f, 2f);
    public float retreatSpeed = 5f;

    [Header("Hits")]
    [Range(0f, 1f)] public float hardHitChance = 0.3f;
    public float restartDelay = 1.5f;

    [Header("Camera shake")]
    public float lightShakeDuration = 0.25f;
    public float lightShakeMagnitude = 0.15f;
    public float hardShakeDuration = 0.7f;
    public float hardShakeMagnitude = 0.5f;
    public float nearMissShakeDuration = 0.15f;
    public float nearMissShakeMagnitude = 0.05f;       // set to 0 to turn off

    State state;
    bool active;
    float stateEndTime;
    float currentStalkDistance;
    float weavePhase;
    Vector3 lungeDir;
    float lungeMinDist;
    Vector3 retreatDir;
    Vector3 lastPlayerPos;
    Vector3 playerVel;
    Vector3 moveDir = Vector3.forward;   // direction the player is running (flat)

    // Called by the spawn trigger
    public void StartChase(Vector3 spawnPosition)
    {
        transform.position = spawnPosition;
        gameObject.SetActive(true);

        lastPlayerPos = player.position;
        playerVel = Vector3.zero;

        Vector3 toPlayer = player.position - spawnPosition;
        toPlayer.y = 0f;
        moveDir = toPlayer.sqrMagnitude > 0.01f ? toPlayer.normalized : player.forward;

        active = true;
        BeginStalk();
    }

    // Called by the despawn trigger
    public void StopChase()
    {
        if (state == State.Restarting) return;
        active = false;
        gameObject.SetActive(false);
    }

    void Update()
    {
        if (!active || player == null || state == State.Restarting) return;

        TrackPlayer();

        switch (state)
        {
            case State.Stalk: UpdateStalk(); break;
            case State.Lunge: UpdateLunge(); break;
            case State.Retreat: UpdateRetreat(); break;
        }
    }

    // ---------- tracking ----------
    void TrackPlayer()
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        Vector3 vel = (player.position - lastPlayerPos) / dt;
        playerVel = Vector3.Lerp(playerVel, vel, 10f * dt);
        lastPlayerPos = player.position;

        Vector3 flat = playerVel;
        flat.y = 0f;
        if (flat.magnitude > 0.5f)
        {
            Vector3 blended = Vector3.Lerp(moveDir, flat.normalized, 3f * dt);
            if (blended.sqrMagnitude > 0.001f) moveDir = blended.normalized;
        }
    }

    // ---------- stalk ----------
    void BeginStalk()
    {
        state = State.Stalk;
        stateEndTime = Time.time + Random.Range(stalkTime.x, stalkTime.y);
        currentStalkDistance = Random.Range(stalkDistance.x, stalkDistance.y);
        weavePhase = Random.Range(0f, 10f);
    }

    void UpdateStalk()
    {
        Vector3 side = Vector3.Cross(Vector3.up, moveDir);
        float weave = Mathf.Sin(Time.time * weaveSpeed + weavePhase) * weaveAmount;

        Vector3 target = player.position
                         - moveDir * currentStalkDistance
                         + side * weave
                         + Vector3.up * heightOffset;

        Vector3 to = target - transform.position;
        float dist = to.magnitude;

        float speed = Mathf.Clamp(playerVel.magnitude + dist * catchUpFactor, 0f, maxStalkSpeed);
        float step = Mathf.Min(speed * Time.deltaTime, dist);
        if (dist > 0.001f) Move(to.normalized * step);

        if (Time.time >= stateEndTime) BeginLunge();
    }

    // ---------- lunge ----------
    void BeginLunge()
    {
        state = State.Lunge;
        stateEndTime = Time.time + Random.Range(lungeTime.x, lungeTime.y);
        lungeMinDist = float.MaxValue;

        Vector3 predicted = player.position + playerVel * leadTime;
        lungeDir = (predicted - transform.position).normalized;
    }

    void UpdateLunge()
    {
        Vector3 toPlayer = player.position - transform.position;
        float dist = toPlayer.magnitude;
        lungeMinDist = Mathf.Min(lungeMinDist, dist);

        // limited turning, so a moving player can make it miss
        if (dist > 0.001f)
        {
            lungeDir = Vector3.RotateTowards(lungeDir, toPlayer.normalized,
                                             lungeTurnRate * Mathf.Deg2Rad * Time.deltaTime, 0f);
        }
        Move(lungeDir * lungeSpeed * Time.deltaTime);

        if (dist <= hitRadius)
        {
            HitPlayer();
            return;
        }

        if (Time.time >= stateEndTime)
        {
            // missed
            if (cameraShake != null && nearMissShakeMagnitude > 0f && lungeMinDist <= nearMissRadius)
                cameraShake.Shake(nearMissShakeDuration, nearMissShakeMagnitude);
            BeginRetreat();
        }
    }

    // ---------- retreat ----------
    void BeginRetreat()
    {
        state = State.Retreat;
        stateEndTime = Time.time + Random.Range(retreatTime.x, retreatTime.y);

        // peel off to whichever side it is already on, and drop back
        Vector3 side = Vector3.Cross(Vector3.up, moveDir);
        float sign = Vector3.Dot(transform.position - player.position, side) >= 0f ? 1f : -1f;
        retreatDir = (side * sign + (-moveDir) * 0.7f).normalized;
    }

    void UpdateRetreat()
    {
        Move(retreatDir * retreatSpeed * Time.deltaTime);
        if (Time.time >= stateEndTime) BeginStalk();
    }

    // ---------- hit ----------
    void HitPlayer()
    {
        bool hard = Random.value < hardHitChance;

        if (cameraShake != null)
        {
            if (hard) cameraShake.Shake(hardShakeDuration, hardShakeMagnitude);
            else cameraShake.Shake(lightShakeDuration, lightShakeMagnitude);
        }

        if (hard)
        {
            state = State.Restarting;
            if (bloodScreen != null) bloodScreen.Show();
            PlayHardHitSound();
            StartCoroutine(RestartScene());
        }
        else
        {
            BeginRetreat();
        }
    }

    IEnumerator RestartScene()
    {
        // wait long enough for the blood image to be fully seen before reloading
        float wait = restartDelay;
        if (bloodScreen != null) wait = Mathf.Max(wait, bloodScreen.holdTime);
        if (waitForSoundToFinish && hardHitSound != null) wait = Mathf.Max(wait, hardHitSound.length);
        yield return new WaitForSeconds(wait);
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    void PlayHardHitSound()
    {
        if (hardHitSound == null) return;

        if (audioSource == null) audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0f; // 2D so it's heard clearly no matter where the sphere is
        }
        audioSource.PlayOneShot(hardHitSound, hardHitVolume);
    }

    // ---------- helpers ----------
    void Move(Vector3 delta)
    {
        transform.position += delta;
        if (delta.sqrMagnitude > 0.000001f)
        {
            transform.forward = Vector3.Slerp(transform.forward, delta.normalized, 8f * Time.deltaTime);
        }
    }
}