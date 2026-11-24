using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Blurry vision that clears up as the player eats fish.
/// Put this on any object (e.g. the Player). It reads PlayerEat.fishEaten and
/// drives the Depth of Field override on a Global Volume.
///
/// SETUP:
/// 1. Create a Global Volume (GameObject > Volume > Global Volume) with a new Profile.
/// 2. Add Override > Post-processing > Depth Of Field to that profile.
///    (No need to tick anything, this script switches it to Manual mode.)
/// 3. Drag that Volume into this script's "Volume" slot.
/// 4. Make sure the Main Camera has Post Processing enabled (HDCamera / Camera component).
/// </summary>
public class FishVisionBlur : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Global Volume that has a Depth Of Field override.")]
    public Volume volume;
    [Tooltip("Found automatically if left empty.")]
    public PlayerEat playerEat;

    [Header("Progress")]
    [Tooltip("How many fish must be eaten for the vision to be fully clear.")]
    public int fishForFullClarity = 10;
    [Tooltip("How fast the blur eases to its new level (higher = faster).")]
    public float smoothing = 2f;

    [Header("Blurry (0 fish eaten)")]
    [Tooltip("Distance where blur starts. Small = even nearby things are blurry.")]
    public float blurryFocusStart = 0.5f;
    [Tooltip("Distance where blur reaches maximum.")]
    public float blurryFocusEnd = 4f;
    [Tooltip("Blur strength (HDRP caps this at 8).")]
    [Range(0f, 8f)] public float blurryMaxBlur = 8f;

    [Header("Clear (fully fed)")]
    public float clearFocusStart = 300f;
    public float clearFocusEnd = 400f;

    DepthOfField dof;
    float current; // 0 = blurry, 1 = clear

    void Awake()
    {
        if (playerEat == null)
            playerEat = FindAnyObjectByType<PlayerEat>();

        if (volume == null)
        {
            Debug.LogWarning("FishVisionBlur: no Volume assigned.");
            enabled = false;
            return;
        }

        // .profile makes a runtime copy so we don't edit the asset on disk.
        if (!volume.profile.TryGet(out dof))
        {
            Debug.LogWarning("FishVisionBlur: the Volume's profile has no Depth Of Field override. Add one.");
            enabled = false;
            return;
        }

        dof.focusMode.Override(DepthOfFieldMode.Manual);
        Apply(0f);
    }

    void Update()
    {
        if (dof == null) return;

        int eaten = playerEat != null ? playerEat.fishEaten : 0;
        float target = Mathf.Clamp01(eaten / (float)Mathf.Max(1, fishForFullClarity));

        // ease toward the target so clarity changes gradually
        current = Mathf.Lerp(current, target, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
        Apply(current);
    }

    void Apply(float clarity)
    {
        // far blur: starts close when blurry, pushed far away when clear
        dof.farFocusStart.Override(Mathf.Lerp(blurryFocusStart, clearFocusStart, clarity));
        dof.farFocusEnd.Override(Mathf.Lerp(blurryFocusEnd, clearFocusEnd, clarity));
        // these two are plain floats in HDRP (not parameters), so they're set directly
        dof.farMaxBlur = Mathf.Lerp(blurryMaxBlur, 0f, clarity);

        // no near blur, so only the far side is controlled
        dof.nearMaxBlur = 0f;
    }
}