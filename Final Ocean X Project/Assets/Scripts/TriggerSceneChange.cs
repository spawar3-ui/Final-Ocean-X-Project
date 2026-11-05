using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public enum SceneTransitionType
{
    Instant,
    FadeBlack,
    FadeWhite,
    WipeLeftToRight,
    WipeTopToBottom
}

/*
SETUP
1. Player object must be tagged "Player" (exact spelling and case).
2. Create an empty object or cube, add a Collider, tick "Is Trigger".
3. Attach this script to it.
4. Type the scene name in "Scene Name" (must match exactly).
5. Pick a transition from the dropdown and set the duration.
6. File > Build Profiles > Scene List > add every scene you want to load.
7. At least one of the two objects (player or trigger) needs a Rigidbody
   (a CharacterController on the player also works).

No other scripts or UI are needed, the fade panel builds itself at runtime.
*/
public class TriggerSceneChange : MonoBehaviour
{
    public string sceneName;
    public SceneTransitionType transition = SceneTransitionType.FadeBlack;
    public float duration = 1f;
    public string playerTag = "Player";

    bool used;

    void OnTriggerEnter(Collider other)
    {
        if (used || !other.CompareTag(playerTag)) return;
        used = true;
        SceneTransitioner.GoToScene(sceneName, transition, duration);
    }
}

// Lives across scenes, created automatically the first time it is needed.
public class SceneTransitioner : MonoBehaviour
{
    static SceneTransitioner instance;

    Canvas canvas;
    RectTransform panelRect;
    Image panel;
    bool busy;

    public static void GoToScene(string sceneName, SceneTransitionType type, float duration)
    {
        if (instance == null)
        {
            var go = new GameObject("SceneTransitioner");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SceneTransitioner>();
            instance.Build();
        }

        if (instance.busy) return;
        instance.StartCoroutine(instance.Run(sceneName, type, duration));
    }

    void Build()
    {
        canvas = gameObject.AddComponent<Canvas>();
        canvas.sortingOrder = 999;

        var panelGO = new GameObject("Panel");
        panelGO.transform.SetParent(transform, false);
        panel = panelGO.AddComponent<Image>();
        panel.raycastTarget = false;
        panelRect = panel.rectTransform;
        panelGO.SetActive(false);
    }

    // Screen Space - Camera works in VR, Overlay does not.
    void BindCamera()
    {
        Camera cam = Camera.main;
        if (cam != null)
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = cam.nearClipPlane + 0.1f;
        }
        else
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        }
    }

    IEnumerator Run(string sceneName, SceneTransitionType type, float duration)
    {
        busy = true;

        if (type == SceneTransitionType.Instant || duration <= 0f)
        {
            SceneManager.LoadScene(sceneName);
            busy = false;
            yield break;
        }

        BindCamera();
        panel.color = type == SceneTransitionType.FadeWhite ? new Color(1, 1, 1, 0) : new Color(0, 0, 0, 0);
        SetFull();
        panel.gameObject.SetActive(true);

        yield return Animate(type, true, duration);

        AsyncOperation op = SceneManager.LoadSceneAsync(sceneName);
        while (!op.isDone) yield return null;

        BindCamera(); // new scene, new camera
        yield return Animate(type, false, duration);

        panel.gameObject.SetActive(false);
        busy = false;
    }

    // cover = true: screen gets hidden. cover = false: screen is revealed.
    IEnumerator Animate(SceneTransitionType type, bool cover, float duration)
    {
        float t = 0f;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
            Apply(type, cover, k);
            yield return null;
        }
        Apply(type, cover, 1f);
    }

    void Apply(SceneTransitionType type, bool cover, float k)
    {
        switch (type)
        {
            case SceneTransitionType.FadeBlack:
            case SceneTransitionType.FadeWhite:
                SetFull();
                Color c = panel.color;
                c.a = cover ? k : 1f - k;
                panel.color = c;
                break;

            case SceneTransitionType.WipeLeftToRight:
                panel.color = Color.black;
                panelRect.offsetMin = panelRect.offsetMax = Vector2.zero;
                if (cover) { panelRect.anchorMin = new Vector2(0, 0); panelRect.anchorMax = new Vector2(k, 1); }
                else       { panelRect.anchorMin = new Vector2(k, 0); panelRect.anchorMax = new Vector2(1, 1); }
                break;

            case SceneTransitionType.WipeTopToBottom:
                panel.color = Color.black;
                panelRect.offsetMin = panelRect.offsetMax = Vector2.zero;
                if (cover) { panelRect.anchorMin = new Vector2(0, 1f - k); panelRect.anchorMax = new Vector2(1, 1); }
                else       { panelRect.anchorMin = new Vector2(0, 0);      panelRect.anchorMax = new Vector2(1, 1f - k); }
                break;
        }
    }

    void SetFull()
    {
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;
    }
}
