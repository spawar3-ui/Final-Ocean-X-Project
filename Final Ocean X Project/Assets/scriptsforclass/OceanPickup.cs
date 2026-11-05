using UnityEngine;

// Put this on each collectible model (the root object, which needs a Collider).
// Bacteria brighten the light and glow; bleached coral dims them.
// The model keeps its own materials; the glow is a small light plus a soft halo around it.
public class OceanPickup : MonoBehaviour
{
    public enum Kind { Bacteria, BleachedCoral }
    public Kind kind = Kind.Bacteria;

    [Header("Glow around the model")]
    public Light glowLight;          // small point light around the model
    public Renderer glowHalo;        // soft additive halo that always faces the camera
    public float haloPerGlow = 0.15f; // halo brightness = glow * this

    [Header("Extra parts that brighten with the glow")]
    public Renderer[] glowParts;        // e.g. the glowing specks and the jelly veil
    public float partsPerGlow = 0.15f;  // part brightness = its own color * (1 + glow * this)

    [HideInInspector]
    public float glowScale = 1f;        // set by SpeckAbsorber: the light, halo and veil fade as the cloud empties

    private Material haloMaterial;
    private Material[] partMaterials;
    private Color[] partBaseColors;

    void Awake()
    {
        // .material makes a per-object copy so each halo can glow on its own
        if (glowHalo != null) haloMaterial = glowHalo.material;

        if (glowParts == null) glowParts = new Renderer[0];
        partMaterials = new Material[glowParts.Length];
        partBaseColors = new Color[glowParts.Length];
        for (int i = 0; i < glowParts.Length; i++)
        {
            if (glowParts[i] == null) continue;
            partMaterials[i] = glowParts[i].material;
            partBaseColors[i] = partMaterials[i].GetColor("_BaseColor");
        }
    }

    // Called by OceanLightController every frame the glow changes.
    public void SetGlow(Color color, float intensity)
    {
        if (glowLight != null)
        {
            glowLight.color = color;
            glowLight.intensity = intensity * glowScale;
        }
        if (haloMaterial != null) haloMaterial.SetColor("_BaseColor", color * intensity * haloPerGlow * glowScale);
        for (int i = 0; i < partMaterials.Length; i++)
        {
            if (partMaterials[i] == null) continue;
            // the specks themselves keep their brightness; everything else fades with the cloud
            float scale = glowParts[i] is ParticleSystemRenderer ? 1f : glowScale;
            partMaterials[i].SetColor("_BaseColor", partBaseColors[i] * (1f + intensity * partsPerGlow) * scale);
        }
    }

    void LateUpdate()
    {
        // Turn the halo toward the camera so it reads as a soft ball of light.
        var cam = Camera.main;
        if (glowHalo != null && cam != null)
            glowHalo.transform.rotation = cam.transform.rotation;
    }
}
