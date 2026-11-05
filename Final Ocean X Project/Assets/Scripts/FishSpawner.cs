using UnityEngine;

/// <summary>
/// Spawns fish at random positions inside a box centered on this object.
/// Place the spawner in the middle of where you want the fish to swim.
/// </summary>
public class FishSpawner : MonoBehaviour
{
    [Header("What to spawn")]
    [Tooltip("Drag one or more fish models/prefabs here. One is picked at random for each fish.")]
    public GameObject[] fishPrefabs;
    public int fishCount = 20;

    [Header("Where")]
    [Tooltip("Size of the box the fish spawn and swim in.")]
    public Vector3 areaSize = new Vector3(30f, 10f, 30f);

    [Header("Fish settings")]
    public float minSpeed = 1f;
    public float maxSpeed = 2.5f;
    public float minScale = 0.8f;
    public float maxScale = 1.2f;
    public Vector3 modelRotationOffset = Vector3.zero;

    Transform container;

    void Start()
    {
        if (fishPrefabs == null || fishPrefabs.Length == 0)
        {
            Debug.LogWarning("FishSpawner: no fish prefabs assigned.");
            return;
        }

        // Fish go into their own container that always has scale (1,1,1).
        // If the spawner itself is scaled non-uniformly (e.g. Y = 0.18), parenting
        // fish directly to it would squash them flat.
        container = new GameObject(name + " Fish").transform;
        container.position = transform.position;

        for (int i = 0; i < fishCount; i++)
            SpawnFish();
    }

    void SpawnFish()
    {
        GameObject prefab = fishPrefabs[Random.Range(0, fishPrefabs.Length)];
        if (prefab == null) return;

        Vector3 pos = transform.position + new Vector3(
            Random.Range(-areaSize.x, areaSize.x) * 0.5f,
            Random.Range(-areaSize.y, areaSize.y) * 0.5f,
            Random.Range(-areaSize.z, areaSize.z) * 0.5f);

        // random heading only (no random tilt/roll), so fish start upright
        Quaternion rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

        GameObject fish = Instantiate(prefab, pos, rot, container);
        fish.transform.localScale *= Random.Range(minScale, maxScale);

        EdibleFish edible = fish.GetComponent<EdibleFish>();
        if (edible == null) edible = fish.AddComponent<EdibleFish>();

        edible.speed = Random.Range(minSpeed, maxSpeed);
        edible.modelRotationOffset = modelRotationOffset;
        edible.SetArea(transform.position, areaSize);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
        Gizmos.DrawWireCube(transform.position, areaSize);
    }
}