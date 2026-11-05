using UnityEngine;

/// <summary>
/// Procedural fish swimming motion, no animation clips needed.
/// Two ways to use it (or both together):
///   1) Body only: assign "Body" (the fish mesh) and the whole fish sways side to side.
///   2) Spine bones: assign bones head-to-tail in "Spine Bones" for a travelling S-wave
///      that gets stronger toward the tail, which is the most fish-like.
/// The wiggle speeds up and gets bigger the faster the fish moves.
/// </summary>
public class FishWiggle : MonoBehaviour
{
    [Header("Targets")]
    public Transform body;                  // the fish model (child of your movement object)
    public Transform[] spineBones;          // optional: bones from head to tail

    [Header("Wiggle Shape")]
    public Vector3 wiggleAxis = Vector3.up; // axis the fish bends around (try X or Z if your rig differs)
    public float bodyAngle = 12f;           // side-to-side sway of the whole body (degrees)
    public float spineAngle = 25f;          // max bend at the tail end of the spine (degrees)
    public float phaseOffset = 0.6f;        // delay between bones, makes the wave travel to the tail
    public Vector3 rollAxis = Vector3.forward;
    public float rollAmount = 4f;           // slight roll that sells the swim

    [Header("Speed Response")]
    public float idleFrequency = 1.5f;      // wiggles per second when barely moving
    public float swimFrequency = 5f;        // wiggles per second at max speed
    [Range(0f, 1f)] public float idleAmplitude = 0.3f;  // how much it wiggles when still
    public float maxSpeed = 8f;             // movement speed that counts as "full swim"
    public float smoothing = 6f;            // how quickly it eases between idle and swim

    Quaternion bodyBaseRot;
    Quaternion[] boneBaseRots;
    Vector3 lastPos;
    float phase;
    float swimAmount;                       // 0 = idle, 1 = full speed
    Vector3 axis, roll;

    void Start()
    {
        if (body == null && spineBones.Length == 0) body = transform;

        if (body != null) bodyBaseRot = body.localRotation;

        boneBaseRots = new Quaternion[spineBones.Length];
        for (int i = 0; i < spineBones.Length; i++)
            boneBaseRots[i] = spineBones[i].localRotation;

        axis = wiggleAxis.normalized;
        roll = rollAxis.normalized;
        lastPos = transform.position;
    }

    void LateUpdate()   // LateUpdate so it layers on top of any Animator
    {
        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        // measure how fast the fish is actually moving
        float speed = (transform.position - lastPos).magnitude / dt;
        lastPos = transform.position;

        float target = Mathf.Clamp01(speed / maxSpeed);
        swimAmount = Mathf.Lerp(swimAmount, target, smoothing * dt);

        float freq = Mathf.Lerp(idleFrequency, swimFrequency, swimAmount);
        float amp = Mathf.Lerp(idleAmplitude, 1f, swimAmount);

        // accumulate phase so changing speed never makes the wiggle jump
        phase += freq * Mathf.PI * 2f * dt;

        // whole-body sway
        if (body != null)
        {
            float yaw = Mathf.Sin(phase) * bodyAngle * amp;
            float r = Mathf.Cos(phase) * rollAmount * amp;
            body.localRotation = bodyBaseRot
                                 * Quaternion.AngleAxis(yaw, axis)
                                 * Quaternion.AngleAxis(r, roll);
        }

        // travelling wave down the spine (head barely moves, tail moves most)
        int n = spineBones.Length;
        for (int i = 0; i < n; i++)
        {
            float t = (i + 1f) / n;
            float angle = Mathf.Sin(phase - i * phaseOffset) * spineAngle * t * amp;
            spineBones[i].localRotation = boneBaseRots[i] * Quaternion.AngleAxis(angle, axis);
        }
    }
}
