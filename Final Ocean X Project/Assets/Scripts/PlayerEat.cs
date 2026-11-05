using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Put this on the Player. Fish are drawn toward the Lure (the anglerfish's light).
/// When fish are close enough, the GameHUD shows "Click the joystick to eat the fish".
/// One click of the Arduino joystick (or Space / gamepad X / Square) eats EVERY fish in range.
/// </summary>
public class PlayerEat : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The player's camera. Found automatically if left empty.")]
    public Transform cameraTransform;
    [Tooltip("The anglerfish's glowing lure/light. Fish swim toward this.")]
    public Transform lure;
    [Tooltip("Found automatically if left empty.")]
    public ArduinoSerial arduino;

    [Header("Eating")]
    public float eatRange = 2.5f;
    [Tooltip("Fish must be within this angle of where you're looking.")]
    public float viewAngle = 60f;
    public bool requireFacing = false;

    [Header("Info")]
    public int fishEaten = 0;

    /// <summary>How many fish are close enough to eat right now.</summary>
    public int FishInRange => edible.Count;

    readonly List<EdibleFish> edible = new List<EdibleFish>();

    void Awake()
    {
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        if (arduino == null)
            arduino = FindAnyObjectByType<ArduinoSerial>();
    }

    void OnEnable()
    {
        // Tell every fish where the light is.
        EdibleFish.Lure = lure != null ? lure : cameraTransform;
    }

    void Update()
    {
        FindEdibleFish();

        if (edible.Count > 0 && (EatPressed() || ArduinoClicked()))
        {
            // Eat them all at once. Copy first: eating removes a fish from EdibleFish.All.
            foreach (EdibleFish fish in edible.ToArray())
            {
                if (fish == null) continue;
                fish.Eat();
                fishEaten++;
            }
            edible.Clear();
        }
    }

    bool ArduinoClicked()
    {
        return arduino != null && arduino.ClickedThisFrame;
    }

    void FindEdibleFish()
    {
        edible.Clear();

        Transform cam = cameraTransform != null ? cameraTransform : transform;
        // Measure from the light, since that's where the fish gather.
        Vector3 origin = lure != null ? lure.position : cam.position;

        foreach (EdibleFish fish in EdibleFish.All)
        {
            if (fish == null) continue;
            if (Vector3.Distance(fish.transform.position, origin) > eatRange) continue;

            if (requireFacing)
            {
                Vector3 toFish = fish.transform.position - cam.position;
                if (Vector3.Angle(cam.forward, toFish) > viewAngle) continue;
            }

            edible.Add(fish);
        }
    }

#if ENABLE_INPUT_SYSTEM
    bool EatPressed()
    {
        return (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame)
            || (Gamepad.current != null && Gamepad.current.buttonWest.wasPressedThisFrame);
    }
#elif ENABLE_LEGACY_INPUT_MANAGER
    bool EatPressed()
    {
        return Input.GetKeyDown(KeyCode.Space);
    }
#endif
}
