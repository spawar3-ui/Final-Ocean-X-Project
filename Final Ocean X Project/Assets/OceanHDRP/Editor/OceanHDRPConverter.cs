using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

// Converts the imported ocean scene (bacteria & coral) from URP to HDRP.
// Put this file in a folder called "Editor", open the scene that has the ocean objects in it,
// then click Tools > Ocean Scene > Convert To HDRP. Running it again is safe.
//
// What it does:
//  - the DeepDiversion materials (pink) get HDRP shaders, keeping their colors, textures and glow
//  - the ocean's lights get HDRP light data and real-world units so they are bright enough
//  - the old URP effects volume is turned off, and a new HDRP volume around the ocean
//    adds the dark blue fog, the dark sky / ambient light, the bloom glow and a fixed exposure.
//    It is a local volume, so the rest of your scene keeps its own look.
public static class OceanHDRPConverter
{
    const string UrpLitGuid = "933532a4fcc9baf4fa0491de14d08ed7";
    const string UrpParticlesUnlitGuid = "0406db5a14f94604a8c57ccfbc9f3b46";
    const string ProfilePath = "Assets/DeepDiversion/DeepDiversion_HDRP_PostFX.asset";
    const string VolumeName = "Ocean HDRP Volume";

    // The look of the original URP scene
    static readonly Color FogColor = new Color(0.02f, 0.05f, 0.11f);
    static readonly Color AmbientColor = new Color(0.06f, 0.1f, 0.18f);
    const float FogDistance = 30f;   // how far you can see through the water, roughly
    const float Exposure = -1f;      // lower = brighter; change it on the volume if the scene is too dark or bright

    [MenuItem("Tools/Ocean Scene/Convert To HDRP")]
    static void Convert()
    {
        if (Shader.Find("HDRP/Lit") == null)
        {
            Debug.LogError("Ocean converter: HDRP shaders not found. Is this an HDRP project?");
            return;
        }

        int materials = ConvertMaterials();
        int lights = ConvertLights();
        bool volume = SetUpVolume();

        EditorSceneManager.MarkAllScenesDirty();
        Debug.Log("Ocean converter: " + materials + " materials and " + lights + " lights converted" +
                  (volume ? ", fog / glow volume added (\"" + VolumeName + "\")." : ".") +
                  " Save the scene (Cmd/Ctrl+S). Too dark or too bright? Select \"" + VolumeName +
                  "\" and change Exposure > Fixed Exposure (lower = brighter).");
    }

    // ---------- Materials ----------

    static int ConvertMaterials()
    {
        int count = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Material"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.Contains("/DeepDiversion/")) continue;

            // The URP shader is missing in this project, so look at which one the file asks for
            string text = File.ReadAllText(path);
            bool lit = text.Contains(UrpLitGuid);
            bool unlit = text.Contains(UrpParticlesUnlitGuid);
            if (!lit && !unlit) continue;

            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            var saved = new SavedProperties(mat);
            Undo.RecordObject(mat, "Convert ocean materials to HDRP");
            if (lit) ToHdrpLit(mat, saved);
            else ToHdrpUnlit(mat, saved);
            Validate(mat);
            EditorUtility.SetDirty(mat);
            count++;
        }
        AssetDatabase.SaveAssets();
        return count;
    }

    // Ocean floor, trail, bacteria capsules, coral
    static void ToHdrpLit(Material mat, SavedProperties s)
    {
        mat.shader = Shader.Find("HDRP/Lit");
        mat.SetColor("_BaseColor", s.Color("_BaseColor", Color.white));
        mat.SetTexture("_BaseColorMap", s.Texture("_BaseMap"));
        mat.SetTexture("_NormalMap", s.Texture("_BumpMap"));
        mat.SetFloat("_Smoothness", s.Float("_Smoothness", 0.5f));
        mat.SetFloat("_Metallic", s.Float("_Metallic", 0f));

        Color emission = s.Color("_EmissionColor", Color.black);
        mat.SetFloat("_UseEmissiveIntensity", 0f);
        mat.SetColor("_EmissiveColor", emission);
        mat.SetColor("_EmissiveColorLDR", emission);
        mat.SetTexture("_EmissiveColorMap", s.Texture("_EmissionMap"));
    }

    // Glow halos, jelly veil, glowing specks, marine snow
    static void ToHdrpUnlit(Material mat, SavedProperties s)
    {
        Color color = s.Color("_BaseColor", Color.white);
        mat.shader = Shader.Find("HDRP/Unlit");
        mat.SetColor("_UnlitColor", color);
        mat.SetColor("_BaseColor", color); // the ocean scripts change this one; OceanHDRPColorSync copies it to _UnlitColor
        mat.SetTexture("_UnlitColorMap", s.Texture("_BaseMap"));
        mat.SetFloat("_SurfaceType", 1f); // transparent

        // URP: 0 alpha, 1 premultiply, 2 additive, 3 multiply.  HDRP: 0 alpha, 1 additive, 4 premultiply
        int urpBlend = Mathf.RoundToInt(s.Float("_Blend", 0f));
        mat.SetFloat("_BlendMode", urpBlend == 2 ? 1f : urpBlend == 1 ? 4f : 0f);
        mat.SetFloat("_DoubleSidedEnable", 1f);
    }

    // Lets HDRP set up the material's keywords and passes for the new settings
    static void Validate(Material mat)
    {
        var type = Type.GetType("UnityEngine.Rendering.HighDefinition.HDMaterial, Unity.RenderPipelines.HighDefinition.Runtime");
        var method = type?.GetMethod("ValidateMaterial", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Material) }, null);
        if (method != null) method.Invoke(null, new object[] { mat });
        else Debug.LogWarning("Ocean converter: couldn't refresh " + mat.name + ". Click it once in the Project window to refresh it.");
    }

    // Reads the values stored in a material file, even when its shader is missing
    class SavedProperties
    {
        readonly Dictionary<string, Color> colors = new Dictionary<string, Color>();
        readonly Dictionary<string, float> floats = new Dictionary<string, float>();
        readonly Dictionary<string, Texture> textures = new Dictionary<string, Texture>();

        public SavedProperties(Material mat)
        {
            var saved = new SerializedObject(mat).FindProperty("m_SavedProperties");
            var list = saved.FindPropertyRelative("m_Colors");
            for (int i = 0; i < list.arraySize; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                colors[e.FindPropertyRelative("first").stringValue] = e.FindPropertyRelative("second").colorValue;
            }
            list = saved.FindPropertyRelative("m_Floats");
            for (int i = 0; i < list.arraySize; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                floats[e.FindPropertyRelative("first").stringValue] = e.FindPropertyRelative("second").floatValue;
            }
            list = saved.FindPropertyRelative("m_TexEnvs");
            for (int i = 0; i < list.arraySize; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                textures[e.FindPropertyRelative("first").stringValue] =
                    e.FindPropertyRelative("second").FindPropertyRelative("m_Texture").objectReferenceValue as Texture;
            }
        }

        public Color Color(string name, Color fallback) { return colors.TryGetValue(name, out var c) ? c : fallback; }
        public float Float(string name, float fallback) { return floats.TryGetValue(name, out var f) ? f : fallback; }
        public Texture Texture(string name) { return textures.TryGetValue(name, out var t) ? t : null; }
    }

    // ---------- Lights ----------

    static int ConvertLights()
    {
        int count = 0;
        foreach (var light in InOpenScenes<Light>())
        {
            if (!IsOcean(light.transform)) continue;

            float intensity = light.intensity;
            if (!light.TryGetComponent<HDAdditionalLightData>(out _))
                Undo.AddComponent<HDAdditionalLightData>(light.gameObject);
            Undo.RecordObject(light, "Convert ocean lights to HDRP");
            // Candela for point lights matches the brightness numbers the ocean scripts use
            SetLightUnit(light, light.type == LightType.Directional ? "Lux" : "Candela");
            light.intensity = intensity;
            if (light.type == LightType.Directional) light.shadows = LightShadows.None;
            RemoveMissingScripts(light.gameObject);
            count++;
        }
        foreach (var cam in InOpenScenes<Camera>())
            if (IsOcean(cam.transform)) RemoveMissingScripts(cam.gameObject);
        return count;
    }

    // Newer Unity versions keep the unit on the Light, older ones on HDRP's light data; try both.
    static void SetLightUnit(Light light, string unit)
    {
        object[] targets = { light, light.GetComponent<HDAdditionalLightData>() };
        foreach (var target in targets)
        {
            if (target is UnityEngine.Object o && o == null) continue;
            var prop = target.GetType().GetProperty("lightUnit", BindingFlags.Public | BindingFlags.Instance);
            if (prop == null || !prop.CanWrite) continue;
            prop.SetValue(target, Enum.Parse(prop.PropertyType, unit));
            return;
        }
        Debug.LogWarning("Ocean converter: couldn't set the unit on " + light.name + ". Set it to " + unit + " in the Inspector.");
    }

    // The URP light / camera data scripts don't exist here and only cause warnings
    static void RemoveMissingScripts(GameObject go)
    {
        if (PrefabUtility.IsPartOfPrefabInstance(go)) return; // Unity doesn't allow it on prefab parts
        GameObjectUtility.RemoveMonoBehavioursWithMissingScript(go);
    }

    // ---------- Fog, sky, glow ----------

    static bool SetUpVolume()
    {
        // The old URP effects volume has nothing HDRP can use
        foreach (var v in InOpenScenes<Volume>())
        {
            if (v.sharedProfile == null || v.sharedProfile.name != "DeepDiversion_PostFX") continue;
            Undo.RecordObject(v.gameObject, "Turn off URP volume");
            v.gameObject.SetActive(false);
        }

        // Find the area the ocean covers
        Bounds area = default;
        bool found = false;
        Scene oceanScene = default;
        foreach (var r in InOpenScenes<Renderer>())
        {
            if (!r.gameObject.activeInHierarchy || !UsesOceanMaterial(r)) continue;
            if (!found) { area = r.bounds; oceanScene = r.gameObject.scene; found = true; }
            else area.Encapsulate(r.bounds);
        }
        if (!found)
        {
            Debug.LogError("Ocean converter: no ocean objects found in the open scene. Open the scene with the ocean in it and run it again.");
            return false;
        }

        GameObject go = null;
        foreach (var t in InOpenScenes<Transform>())
            if (t.name == VolumeName) { go = t.gameObject; break; }
        if (go == null)
        {
            go = new GameObject(VolumeName);
            SceneManager.MoveGameObjectToScene(go, oceanScene);
            Undo.RegisterCreatedObjectUndo(go, "Add ocean volume");
        }
        Undo.RecordObject(go.transform, "Place ocean volume");
        go.transform.SetPositionAndRotation(area.center, Quaternion.identity);
        go.transform.localScale = Vector3.one;
        go.layer = 2; // Ignore Raycast, so the player's ground check doesn't land on the volume's box

        // The look applies inside this box (plus a short blend at the edges)
        var box = GetOrAdd<BoxCollider>(go);
        box.isTrigger = true;
        box.center = Vector3.zero;
        box.size = area.size + new Vector3(10f, 30f, 10f);

        var volume = GetOrAdd<Volume>(go);
        volume.isGlobal = false;
        volume.blendDistance = 5f;
        volume.priority = 10f;
        volume.sharedProfile = GetOrCreateProfile(area);

        GetOrAdd<OceanHDRPColorSync>(go);
        return true;
    }

    static VolumeProfile GetOrCreateProfile(Bounds area)
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }

        var fog = Get<Fog>(profile);
        fog.enabled.Override(true);
        fog.colorMode.Override(FogColorMode.ConstantColor);
        fog.color.Override(FogColor);
        fog.meanFreePath.Override(FogDistance);
        fog.baseHeight.Override(area.max.y + 20f); // full-strength fog everywhere in the ocean
        fog.maximumHeight.Override(area.max.y + 60f);
        fog.maxFogDistance.Override(300f);

        // No bright sky underwater: a dark blue gradient that also gives the ambient light
        var environment = Get<VisualEnvironment>(profile);
        environment.skyType.Override((int)SkyType.Gradient);
        environment.skyAmbientMode.Override(SkyAmbientMode.Dynamic);
        var sky = Get<GradientSky>(profile);
        sky.top.Override(AmbientColor);
        sky.middle.Override(FogColor);
        sky.bottom.Override(FogColor * 0.5f);

        // HDRP's default exposure is made for daylight; the ocean is very dark
        var exposure = Get<UnityEngine.Rendering.HighDefinition.Exposure>(profile);
        exposure.mode.Override(ExposureMode.Fixed);
        exposure.fixedExposure.Override(Exposure);

        // The glow around the bacteria, coral and light orb
        var bloom = Get<Bloom>(profile);
        bloom.intensity.Override(0.35f);
        bloom.scatter.Override(0.7f);
        bloom.threshold.Override(0.9f);

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        return profile;
    }

    static T Get<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet<T>(out var component)) return component;
        component = profile.Add<T>(false);
        component.name = typeof(T).Name;
        component.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
        AssetDatabase.AddObjectToAsset(component, profile);
        return component;
    }

    // ---------- Helpers ----------

    // Part of the ocean scene: a pickup, the player with the ocean light, or the ocean's sun
    static bool IsOcean(Transform t)
    {
        for (; t != null; t = t.parent)
            if (t.GetComponent<OceanPickup>() || t.GetComponent<OceanLightController>() || t.GetComponent<OceanAtmosphere>())
                return true;
        return false;
    }

    static bool UsesOceanMaterial(Renderer r)
    {
        foreach (var m in r.sharedMaterials)
            if (m != null && AssetDatabase.GetAssetPath(m).Contains("/DeepDiversion/")) return true;
        return false;
    }

    static List<T> InOpenScenes<T>() where T : Component
    {
        var found = new List<T>();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded) continue;
            foreach (var root in scene.GetRootGameObjects())
                found.AddRange(root.GetComponentsInChildren<T>(true));
        }
        return found;
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        if (go.TryGetComponent<T>(out var c)) { Undo.RecordObject(c, "Set up ocean volume"); return c; }
        return Undo.AddComponent<T>(go);
    }
}
