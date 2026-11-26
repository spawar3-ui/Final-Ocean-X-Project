using UnityEngine;

// Put this on an empty GameObject with a Collider (Is Trigger checked).
// Spawn trigger:   assign the Spawn Point.
// Despawn trigger: leave the Spawn Point empty.
public class ChaseTrigger : MonoBehaviour
{
    public SphereChaser sphere;      // drag the sphere here
    public Transform spawnPoint;     // only on the spawn trigger
    public bool triggerOnce = false;

    bool used;

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (triggerOnce && used) return;
        used = true;

        if (spawnPoint != null)
        {
            sphere.StartChase(spawnPoint.position);
        }
        else
        {
            sphere.StopChase();
        }
    }
}