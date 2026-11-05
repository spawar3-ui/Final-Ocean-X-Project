using UnityEngine;

// Put this on a bacteria pickup (next to OceanPickup) whose look is a cloud of glowing specks.
// Walking close soaks up the nearby specks a few at a time and they fly into the player.
// The light rises a little with each handful. Once most of the cloud is gone,
// the rest rushes in and the blob counts as collected.
public class SpeckAbsorber : MonoBehaviour
{
    public ParticleSystem specks;          // the cloud's glowing specks (a fixed set, no respawn)
    public float reach = 1.3f;             // how close specks must be to get pulled in
    public int specksPerGrab = 35;         // a few at a time, not the whole cloud
    public float grabInterval = 0.1f;      // seconds between grabs while in reach
    public float finishAt = 0.8f;          // share of the cloud that completes the blob
    public float flySpeed = 5f;            // how fast grabbed specks fly into the player
    public float destroyDelay = 1.2f;      // time for the last specks to arrive

    private OceanLightController player;
    private OceanPickup pickup;
    private ParticleSystem.Particle[] buffer;
    private int total;
    private int taken;
    private float nextGrab;
    private bool finished;

    // Specks being pulled in are marked by this start lifetime (the rest live "forever").
    private const float InFlight = 0.99f;

    void Start()
    {
        player = FindAnyObjectByType<OceanLightController>();
        pickup = GetComponent<OceanPickup>();
        buffer = new ParticleSystem.Particle[specks.main.maxParticles];
        // keep simulating even when off screen, so grabbed specks always finish flying in
        var main = specks.main;
        main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
    }

    void Update()
    {
        if (player == null || specks == null) return;
        int count = specks.GetParticles(buffer);
        if (total == 0) total = count; // the whole cloud is emitted at once at the start
        if (total == 0) return;

        Vector3 target = specks.transform.InverseTransformPoint(player.transform.position + Vector3.up * 1.1f);
        bool canGrab = !finished && Time.time >= nextGrab;
        int grabbed = 0;
        float shrink = Mathf.Clamp01(1f - 4f * Time.deltaTime);

        for (int i = 0; i < count; i++)
        {
            var p = buffer[i];
            if (p.startLifetime < 1f) // in flight (Unity rounds lifetimes, so no exact compare)
            {
                // fly into the player, shrinking on the way
                p.position = Vector3.MoveTowards(p.position, target, flySpeed * Time.deltaTime);
                p.startSize3D *= shrink;
                if ((p.position - target).sqrMagnitude < 0.01f) p.remainingLifetime = 0f;
            }
            else if (finished || (canGrab && grabbed < specksPerGrab && (p.position - target).sqrMagnitude < reach * reach))
            {
                p.startLifetime = InFlight;
                p.remainingLifetime = InFlight;
                if (!finished) grabbed++;
            }
            buffer[i] = p;
        }
        specks.SetParticles(buffer, count);

        if (grabbed > 0)
        {
            taken += grabbed;
            nextGrab = Time.time + grabInterval;
            player.AbsorbBacteria((float)grabbed / total);
            player.PlayTick();
            if (pickup != null) pickup.glowScale = 1f - (float)taken / total;

            if (taken >= finishAt * total)
            {
                // the rest of the cloud rushes in and the blob counts as collected
                finished = true;
                player.AbsorbBacteria((float)(total - taken) / total);
                if (pickup != null) pickup.glowScale = 0f;
                player.FinishBacteria(pickup);
                Destroy(gameObject, destroyDelay);
            }
        }
    }
}
