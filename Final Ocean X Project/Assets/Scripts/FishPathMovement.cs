using UnityEngine;
using System.Collections.Generic;
using System.Globalization;

public class FishPathMovement : MonoBehaviour
{
    public enum Recenter { None, FirstPoint, Centroid }
    public enum FitMode { Off, KeepProportions, StretchToBox }

    [Header("Path Data")]
    public TextAsset pathData;

    [Header("Movement")]
    public float speed = 2f;
    public float turnSpeed = 3f;
    public bool loop = true;
    [Tooltip("At the end of the path, swim back along it instead of jumping to the start.")]
    public bool pingPong = true;

    [Header("Adjustable Bounding Box")]
    [Tooltip("A BoxCollider (set to Is Trigger) that the whole path is fitted inside. " +
             "Move, rotate and resize it in the Scene view. Right-click this component > Create Fit Box to make one.")]
    public BoxCollider fitBox;

    [Tooltip("Off = ignore the box. KeepProportions = path scaled to fit inside the box without distorting it. " +
             "StretchToBox = path stretched to fill the box on every axis.")]
    public FitMode fitMode = FitMode.KeepProportions;

    [Header("Placement (used when no Fit Box is set)")]
    [Tooltip("Optional. Move/rotate/scale this empty object to place the whole path. " +
             "If empty, the path is placed at the fish's position in the scene.")]
    public Transform pathOrigin;

    [Tooltip("Which part of the CSV path sits on the origin / start position.")]
    public Recenter recenter = Recenter.FirstPoint;

    [Tooltip("Extra offset in Unity units, applied after recentering.")]
    public Vector3 positionOffset = Vector3.zero;

    [Tooltip("Scale the path (1 = same size as in Blender).")]
    public float pathScale = 1f;

    [Tooltip("Only tick this if the CSV was exported raw from Blender (Z-up). " +
             "Your current fish_path.csv is already Y-up, so leave it off.")]
    public bool convertFromBlender = false;

    [Header("Swimming Motion")]
    public float verticalAmount = 0.3f;
    public float verticalSpeed = 1.5f;

    [Header("Debug")]
    public bool drawPathGizmos = true;

    private readonly List<Vector3> points = new List<Vector3>();
    private Vector3 pivot;
    private Vector3 pathMin, pathMax, pathCenter;
    private Vector3 fitScale = Vector3.one;
    private Vector3 startPos;
    private Vector3 basePos;   // position along the path, without the bob
    private int index = 1;
    private int dir = 1;

    bool UsingBox { get { return fitMode != FitMode.Off && fitBox != null; } }

    void LoadPoints()
    {
        points.Clear();
        if (pathData == null) return;

        foreach (string line in pathData.text.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            string[] v = line.Trim().Split(',');
            if (v.Length < 3) continue;

            if (!float.TryParse(v[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)) continue;
            if (!float.TryParse(v[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)) continue;
            if (!float.TryParse(v[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)) continue;

            // Blender (X, Y, Z-up) -> Unity (X, Y-up, Z)
            points.Add(convertFromBlender ? new Vector3(x, z, y) : new Vector3(x, y, z));
        }

        if (points.Count == 0) return;

        pathMin = pathMax = points[0];
        Vector3 sum = Vector3.zero;
        foreach (var p in points)
        {
            pathMin = Vector3.Min(pathMin, p);
            pathMax = Vector3.Max(pathMax, p);
            sum += p;
        }
        pathCenter = (pathMin + pathMax) * 0.5f;

        pivot = Vector3.zero;
        if (recenter == Recenter.FirstPoint) pivot = points[0];
        else if (recenter == Recenter.Centroid) pivot = sum / points.Count;
    }

    // Recalculated every frame so you can resize the box while the game runs
    void RefreshFit()
    {
        if (!UsingBox || points.Count == 0) return;

        Vector3 size = pathMax - pathMin;
        Vector3 box = fitBox.size;
        const float eps = 0.0001f;

        float rx = size.x > eps ? box.x / size.x : float.PositiveInfinity;
        float ry = size.y > eps ? box.y / size.y : float.PositiveInfinity;
        float rz = size.z > eps ? box.z / size.z : float.PositiveInfinity;

        if (fitMode == FitMode.KeepProportions)
        {
            float u = Mathf.Min(rx, Mathf.Min(ry, rz));
            if (float.IsInfinity(u)) u = 1f;
            fitScale = new Vector3(u, u, u);
        }
        else
        {
            fitScale = new Vector3(
                float.IsInfinity(rx) ? 1f : rx,
                float.IsInfinity(ry) ? 1f : ry,
                float.IsInfinity(rz) ? 1f : rz);
        }
    }

    Vector3 ToWorld(Vector3 p)
    {
        if (UsingBox)
        {
            // Path centre sits at the box centre; box transform handles move/rotate/scale
            Vector3 local = fitBox.center + Vector3.Scale(p - pathCenter, fitScale);
            return fitBox.transform.TransformPoint(local);
        }

        Vector3 offset = (p - pivot) * pathScale + positionOffset;
        if (pathOrigin != null)
            return pathOrigin.TransformPoint(offset);

        Vector3 origin = Application.isPlaying ? startPos : transform.position;
        return origin + offset;
    }

    void Start()
    {
        if (pathData == null)
        {
            Debug.LogError("Please assign fish_path.csv!");
            enabled = false;
            return;
        }

        startPos = transform.position;
        LoadPoints();

        if (points.Count < 2)
        {
            Debug.LogError("Not enough path points!");
            enabled = false;
            return;
        }

        RefreshFit();
        basePos = ToWorld(points[0]);
        transform.position = basePos;
    }

    void Update()
    {
        if (points.Count < 2) return;

        RefreshFit();

        Vector3 target = ToWorld(points[index]);
        Vector3 direction = target - basePos;

        basePos = Vector3.MoveTowards(basePos, target, speed * Time.deltaTime);

        // Bob up and down on top of the path position
        float bob = Mathf.Sin(Time.time * verticalSpeed) * verticalAmount;
        transform.position = basePos + Vector3.up * bob;

        if (direction.sqrMagnitude > 0.0001f)
        {
            Quaternion look = Quaternion.LookRotation(direction.normalized);
            transform.rotation = Quaternion.Slerp(transform.rotation, look, turnSpeed * Time.deltaTime);
        }

        if ((basePos - target).sqrMagnitude < 0.0001f)
            Advance();
    }

    void Advance()
    {
        index += dir;

        if (index >= points.Count)
        {
            if (!loop) { index = points.Count - 1; enabled = false; return; }

            if (pingPong) { dir = -1; index = points.Count - 2; }
            else { index = 0; basePos = ToWorld(points[0]); }   // jump back to start
        }
        else if (index < 0)
        {
            dir = 1;
            index = 1;
        }
    }

    // Right-click the component header > Create Fit Box
    [ContextMenu("Create Fit Box")]
    void CreateFitBox()
    {
        LoadPoints();

        GameObject go = new GameObject("FishPathBox");   // not parented, so it doesn't move with the fish
        go.transform.position = transform.position;

        BoxCollider bc = go.AddComponent<BoxCollider>();
        bc.isTrigger = true;
        bc.size = points.Count > 0 ? (pathMax - pathMin) * pathScale : new Vector3(20f, 5f, 20f);

        fitBox = bc;
        if (fitMode == FitMode.Off) fitMode = FitMode.KeepProportions;
    }

    // Select the fish in the Scene view to preview the path (also works in edit mode)
    void OnDrawGizmosSelected()
    {
        if (!drawPathGizmos || pathData == null) return;
        if (!Application.isPlaying) LoadPoints();
        if (points.Count < 2) return;

        RefreshFit();

        Gizmos.color = Color.cyan;
        for (int i = 0; i < points.Count - 1; i++)
            Gizmos.DrawLine(ToWorld(points[i]), ToWorld(points[i + 1]));

        Gizmos.color = Color.green;
        Gizmos.DrawSphere(ToWorld(points[0]), 0.3f);
    }
}