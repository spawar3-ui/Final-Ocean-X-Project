using UnityEngine;

// Put this on the root of the ocean scene prefab.
// Fog and ambient light are saved in the scene, not on objects, so a prefab would lose them.
// This keeps a copy and applies it whenever the prefab is in a scene (in the editor and in play mode).
[ExecuteAlways]
public class OceanAtmosphere : MonoBehaviour
{
    [Header("Fog")]
    public bool fog = true;
    public FogMode fogMode = FogMode.ExponentialSquared;
    public Color fogColor = new Color(0.02f, 0.05f, 0.11f);
    public float fogDensity = 0.04f;
    public float fogStart = 0f;
    public float fogEnd = 300f;

    [Header("Ambient light")]
    public UnityEngine.Rendering.AmbientMode ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
    public Color ambientColor = new Color(0.06f, 0.1f, 0.18f);
    public float ambientIntensity = 1f;

    [Header("Sky")]
    public bool clearSkybox = true;  // the ocean has no sky; the camera clears to the fog color
    public Light sun;                // the scene's directional light

    void OnEnable()
    {
        Apply();
    }

    void OnValidate()
    {
        Apply();
    }

    public void Apply()
    {
        RenderSettings.fog = fog;
        RenderSettings.fogMode = fogMode;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogDensity = fogDensity;
        RenderSettings.fogStartDistance = fogStart;
        RenderSettings.fogEndDistance = fogEnd;

        RenderSettings.ambientMode = ambientMode;
        RenderSettings.ambientLight = ambientColor;
        RenderSettings.ambientIntensity = ambientIntensity;

        if (clearSkybox) RenderSettings.skybox = null;
        if (sun != null) RenderSettings.sun = sun;
    }
}
