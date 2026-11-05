using UnityEngine;

// Put this on the First Person Controller (same object that has an AudioSource).
// Walking into bacteria: point light + bacteria glow step up
// (speck clouds with a SpeckAbsorber are soaked up gradually instead).
// Bleached coral stays hidden until every bacteria is collected.
// Walking into bleached coral: light and coral glow step down.
// Once every bleached coral is collected, the light fades back to nothing.
public class OceanLightController : MonoBehaviour
{
    [Header("Light")]
    public Light pointLight;
    public Color lightColor = new Color(0.3f, 0.9f, 0.9f); // light blue-teal
    public float bacteriaStep = 0.5f;   // added per bacteria
    public float coralStep = 0.5f;      // removed per bleached coral
    public float maxIntensity = 5f;
    public float easeSpeed = 3f;        // how quickly the light moves toward its target
    public float fadeOutSpeed = 0.6f;   // slower fade when all bleached coral is collected

    [Header("Light orb at the top of the screen")]
    public Renderer lightOrb;           // soft teal ball of light, child of the camera
    public float orbMinScale = 0.5f;    // size when the light is almost off
    public float orbMaxScale = 2f;      // size at max intensity

    [Header("Glow around models")]
    public float bacteriaBaseGlow = 1f; // bacteria glow a little before any pickup
    public float glowPerIntensity = 2f; // bacteria glow = base + light intensity * this
    public Color coralColor = Color.white;
    public float coralGlowPerIntensity = 0.6f; // coral glow = light intensity * this

    [Header("Pickup")]
    public float pickupCheckRadius = 1f;

    [Header("Sound")]
    public AudioClip bacteriaClip;      // water drop
    public AudioClip coralClip;         // bone crack; falls back to bacteriaClip
    public float pitchStep = 0.05f;     // pitch rises with each bacteria
    public float coralPitch = 0.7f;     // dull, lower pitch for coral
    public float tickVolume = 0.25f;    // soft ticks while soaking up a speck cloud
    public float tickInterval = 0.15f;  // at most one tick this often
    public Vector2 tickPitch = new Vector2(1.3f, 1.7f);

    private AudioSource audioSource;
    private AudioSource tickSource;     // separate, so tick pitches don't bend the main sounds
    private float lastTick = -1f;
    private float targetIntensity;
    private float currentIntensity;
    private int bacteriaLeft;
    private int coralLeft;
    private int bacteriaCollected;
    private bool fadingOut;
    private OceanPickup[] allPickups;
    private Material orbMaterial;

    void Start()
    {
        // No cursor on screen: hide it and keep it in the game window.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        audioSource = GetComponent<AudioSource>();
        tickSource = gameObject.AddComponent<AudioSource>();
        tickSource.playOnAwake = false;
        tickSource.spatialBlend = 0f;
        if (pointLight == null) pointLight = FindAnyObjectByType<Light>();
        pointLight.color = lightColor;
        pointLight.intensity = 0f;
        if (lightOrb != null) orbMaterial = lightOrb.material;
        ApplyOrb();

        allPickups = FindObjectsByType<OceanPickup>(FindObjectsSortMode.None);
        foreach (var p in allPickups)
        {
            if (p.kind == OceanPickup.Kind.Bacteria) bacteriaLeft++;
            else coralLeft++;
        }

        // Coral only appears once the bacteria are gone.
        SetCoralVisible(bacteriaLeft <= 0);

        ApplyGlow();
    }

    void Update()
    {
        Collider[] nearby = Physics.OverlapSphere(transform.position, pickupCheckRadius);
        foreach (var col in nearby)
        {
            var pickup = col.GetComponentInParent<OceanPickup>();
            // speck clouds are soaked up gradually by their SpeckAbsorber instead
            if (pickup != null && pickup.GetComponent<SpeckAbsorber>() == null) Collect(pickup);
        }

        float speed = fadingOut ? fadeOutSpeed : easeSpeed;
        currentIntensity = Mathf.MoveTowards(currentIntensity, targetIntensity, speed * Time.deltaTime);
        pointLight.intensity = currentIntensity;
        ApplyOrb();
        ApplyGlow();
    }

    // The orb fades in and grows with the light, so the player can see it.
    private void ApplyOrb()
    {
        if (orbMaterial == null) return;
        float t = Mathf.Clamp01(currentIntensity / maxIntensity);
        orbMaterial.SetColor("_BaseColor", lightColor * t);
        lightOrb.transform.localScale = Vector3.one * Mathf.Lerp(orbMinScale, orbMaxScale, t);
    }

    private void Collect(OceanPickup pickup)
    {
        if (pickup.kind == OceanPickup.Kind.Bacteria)
        {
            AbsorbBacteria(1f);
            FinishBacteria(pickup);
        }
        else
        {
            targetIntensity = Mathf.Max(targetIntensity - coralStep, 0f);
            coralLeft--;
            Play(coralClip != null ? coralClip : bacteriaClip, coralPitch);
            if (coralLeft <= 0)
            {
                fadingOut = true;
                targetIntensity = 0f;
            }
        }

        // Remove from the list so it stops glowing / being tracked, then destroy.
        for (int i = 0; i < allPickups.Length; i++)
            if (allPickups[i] == pickup) allPickups[i] = null;
        Destroy(pickup.gameObject);
    }

    // Called by SpeckAbsorber as specks are soaked up: adds a share (0-1) of one bacteria's light.
    public void AbsorbBacteria(float share)
    {
        targetIntensity = Mathf.Min(targetIntensity + bacteriaStep * share, maxIntensity);
    }

    // A soft, quick tick while soaking up specks (rate-limited so it never machine-guns).
    public void PlayTick()
    {
        if (tickSource == null || bacteriaClip == null || Time.time - lastTick < tickInterval) return;
        lastTick = Time.time;
        tickSource.pitch = Random.Range(tickPitch.x, tickPitch.y);
        tickSource.PlayOneShot(bacteriaClip, tickVolume);
    }

    // Called once when a bacteria is fully collected: the louder sound, and the coral appears after the last one.
    public void FinishBacteria(OceanPickup pickup)
    {
        bacteriaCollected++;
        bacteriaLeft--;
        Play(bacteriaClip, 1f + pitchStep * bacteriaCollected);
        if (bacteriaLeft <= 0) SetCoralVisible(true);
    }

    private void SetCoralVisible(bool visible)
    {
        foreach (var p in allPickups)
            if (p != null && p.kind == OceanPickup.Kind.BleachedCoral) p.gameObject.SetActive(visible);
    }

    private void Play(AudioClip clip, float pitch)
    {
        if (audioSource == null || clip == null) return;
        audioSource.pitch = pitch;
        audioSource.PlayOneShot(clip);
    }

    private void ApplyGlow()
    {
        foreach (var p in allPickups)
        {
            if (p == null) continue;
            if (p.kind == OceanPickup.Kind.Bacteria) p.SetGlow(lightColor, bacteriaBaseGlow + currentIntensity * glowPerIntensity);
            else p.SetGlow(coralColor, currentIntensity * coralGlowPerIntensity);
        }
    }
}
