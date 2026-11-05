using UnityEngine;

/// Procedurally builds a physics rope/suspension bridge: a chain of plank
/// Rigidbodies connected by HingeJoints, suspended between two fixed
/// anchors. Sags under its own weight at rest and flexes/bounces when
/// walked on, because it's genuinely simulated, not animated.
///
/// Setup: drop this on an empty GameObject, position it at the gap, point
/// its blue (forward) arrow across the gap you want bridged. Either leave
/// "Left Anchor" / "Right Anchor" empty (it creates fixed points `span`
/// meters apart, centered on this object) or assign two Transforms sitting
/// exactly where you want the bridge to attach on each side (e.g. two empty
/// GameObjects placed at the cliff edges) for a precise fit.
///
/// It builds itself in Awake(), so press Play to see/feel it work — joints
/// don't resolve outside Play mode. Right-click the component header and
/// choose "Rebuild" to regenerate after tweaking values in the Inspector.
[DisallowMultipleComponent]
public class RopeBridgeBuilder : MonoBehaviour
{
    [Header("Anchors (leave empty to auto-place from Span)")]
    public Transform leftAnchor;
    public Transform rightAnchor;

    [Header("Layout")]
    [Tooltip("Used only when an anchor is left empty.")]
    public float span = 40f;
    public float width = 2f;
    public int plankCount = 16;
    public float plankThickness = 0.15f;
    public float gapBetweenPlanks = 0.05f;
    [Tooltip("How much the resting bridge dips in the middle.")]
    public float sag = 2.5f;

    [Header("Physics")]
    public float plankMass = 8f;
    public float linearDamping = 0.3f;
    public float angularDamping = 0.3f;

    [Header("Rope rails (visual only, optional)")]
    public bool drawRopeRails = true;
    public Material railMaterial;
    public float railWidth = 0.04f;
    [Tooltip("How far above the plank surface the rails sit.")]
    public float railHeight = 0.9f;

    private Transform[] plankTransforms;
    private LineRenderer railLeft, railRight;
    private float halfWidth, halfLength;

    private void Awake()
    {
        Build();
    }

    [ContextMenu("Rebuild")]
    public void Build()
    {
        ClearChildren();

        Vector3 dir = transform.forward;
        Vector3 start = leftAnchor != null ? leftAnchor.position : transform.position - dir * span * 0.5f;
        Vector3 end = rightAnchor != null ? rightAnchor.position : transform.position + dir * span * 0.5f;
        float actualSpan = Vector3.Distance(start, end);

        Rigidbody leftBody = MakeAnchorBody(leftAnchor, start, "LeftAnchor");
        Rigidbody rightBody = MakeAnchorBody(rightAnchor, end, "RightAnchor");

        float plankLength = Mathf.Max(0.2f, (actualSpan - gapBetweenPlanks * (plankCount - 1)) / plankCount);
        halfLength = plankLength * 0.5f;
        halfWidth = width * 0.5f;

        plankTransforms = new Transform[plankCount];
        Rigidbody previousBody = leftBody;

        for (int i = 0; i < plankCount; i++)
        {
            float t = (i + 0.5f) / plankCount;
            Vector3 flatPos = Vector3.Lerp(start, end, t);
            float dip = -sag * 4f * t * (1f - t); // simple parabolic sag, 0 at both ends
            Vector3 pos = flatPos + Vector3.up * dip;

            var plank = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plank.name = "Plank_" + i;
            plank.transform.SetParent(transform);
            plank.transform.position = pos;
            plank.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            plank.transform.localScale = new Vector3(width, plankThickness, plankLength);
            plankTransforms[i] = plank.transform;

            var rb = plank.AddComponent<Rigidbody>();
            rb.mass = plankMass;
            rb.linearDamping = linearDamping;
            rb.angularDamping = angularDamping;

            var hinge = plank.AddComponent<HingeJoint>();
            hinge.axis = Vector3.right; // local axis -> perpendicular to travel, lets the deck pitch up/down
            hinge.anchor = new Vector3(0, 0, -halfLength); // this plank's near edge
            hinge.connectedBody = previousBody;
            hinge.autoConfigureConnectedAnchor = false;
            hinge.connectedAnchor = previousBody == leftBody ? Vector3.zero : new Vector3(0, 0, halfLength);

            previousBody = rb;
        }

        // close the chain: last plank's far edge pins to the right anchor too,
        // so the bridge is suspended between two fixed points, not just cantilevered
        var endHinge = plankTransforms[plankCount - 1].gameObject.AddComponent<HingeJoint>();
        endHinge.axis = Vector3.right;
        endHinge.anchor = new Vector3(0, 0, halfLength);
        endHinge.connectedBody = rightBody;
        endHinge.autoConfigureConnectedAnchor = false;
        endHinge.connectedAnchor = Vector3.zero;

        if (drawRopeRails)
            BuildRails();
    }

    private Rigidbody MakeAnchorBody(Transform existing, Vector3 fallbackPos, string name)
    {
        GameObject go;
        if (existing != null)
        {
            go = existing.gameObject;
        }
        else
        {
            go = new GameObject(name);
            go.transform.SetParent(transform);
            go.transform.position = fallbackPos;
        }
        var rb = go.GetComponent<Rigidbody>();
        if (rb == null) rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true; // fixed in space; everything else swings from these two points
        return rb;
    }

    private void BuildRails()
    {
        railLeft = MakeRail("RailLeft");
        railRight = MakeRail("RailRight");
    }

    private LineRenderer MakeRail(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform);
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = plankCount;
        lr.startWidth = lr.endWidth = railWidth;
        lr.useWorldSpace = true;
        if (railMaterial != null) lr.material = railMaterial;
        return lr;
    }

    private void LateUpdate()
    {
        if (!drawRopeRails || plankTransforms == null || railLeft == null) return;

        for (int i = 0; i < plankTransforms.Length; i++)
        {
            var p = plankTransforms[i];
            if (p == null) return; // bridge not fully built yet / was cleared

            railLeft.SetPosition(i, p.TransformPoint(new Vector3(-halfWidth, railHeight, 0)));
            railRight.SetPosition(i, p.TransformPoint(new Vector3(halfWidth, railHeight, 0)));
        }
    }

    private void ClearChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
        plankTransforms = null;
        railLeft = railRight = null;
    }
}
