using System.Collections.Generic;
using UnityEngine;

// HDRP's Unlit shader calls its color "_UnlitColor", but the ocean scripts change "_BaseColor".
// This copies one to the other every frame so the halos, specks, jelly veil and light orb still glow.
// Added automatically by Tools > Ocean Scene > Convert To HDRP (on the "Ocean HDRP Volume" object).
public class OceanHDRPColorSync : MonoBehaviour
{
    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    static readonly int UnlitColor = Shader.PropertyToID("_UnlitColor");

    private readonly List<Renderer> renderers = new List<Renderer>();

    void Start()
    {
        // includes the hidden coral, which appears later
        foreach (var r in FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            foreach (var m in r.sharedMaterials)
            {
                if (m != null && m.name.StartsWith("DD_") && m.HasProperty(UnlitColor))
                {
                    renderers.Add(r);
                    break;
                }
            }
        }
    }

    void LateUpdate()
    {
        foreach (var r in renderers)
        {
            if (r == null) continue; // collected pickups get destroyed
            // sharedMaterials also picks up the per-object copies the ocean scripts make
            foreach (var m in r.sharedMaterials)
                if (m != null && m.HasProperty(UnlitColor)) m.SetColor(UnlitColor, m.GetColor(BaseColor));
        }
    }
}
