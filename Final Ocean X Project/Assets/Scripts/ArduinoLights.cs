using UnityEngine;

/// <summary>
/// Makes the game's player light match the real LED.
/// The Uno sketch has two LED levels (LIGHT BRIGHT / LIGHT DIM), so the game light
/// fades smoothly between a bright and a dim intensity when you turn the knob.
/// </summary>
public class ArduinoLights : MonoBehaviour
{
    [Tooltip("Found automatically if left empty.")]
    public ArduinoSerial arduino;

    [Header("Player light")]
    [Tooltip("The light in front of the player's camera.")]
    public Light playerLight;
    [Tooltip("Light color (the real LED is white).")]
    public Color lightColor = Color.white;
    [Tooltip("Multiplier on the light's Inspector intensity when the LED is bright.")]
    [Range(0f, 3f)] public float brightLevel = 1f;
    [Tooltip("Multiplier when the LED is dim. The sketch's dim LED is 5/64 of bright, about 0.08.")]
    [Range(0f, 3f)] public float dimLevel = 0.08f;
    [Tooltip("Higher = fades faster.")]
    public float smoothing = 10f;
    [Tooltip("Keep the light on at full brightness while no Arduino is connected (handy for testing).")]
    public bool brightWhenDisconnected = true;

    float baseIntensity;
    float currentLevel;

    void Start()
    {
        if (arduino == null) arduino = FindAnyObjectByType<ArduinoSerial>();
        if (playerLight != null)
        {
            baseIntensity = playerLight.intensity;
            playerLight.color = lightColor;
        }
        currentLevel = brightLevel;
    }

    void Update()
    {
        if (playerLight == null) return;

        bool connected = arduino != null && arduino.IsConnected;
        float target = brightLevel;
        if (connected) target = arduino.LightDim ? dimLevel : brightLevel;
        else if (!brightWhenDisconnected) target = 0f;

        currentLevel = Mathf.Lerp(currentLevel, target, 1f - Mathf.Exp(-smoothing * Time.deltaTime));

        playerLight.enabled = true;
        playerLight.color = lightColor;
        playerLight.intensity = baseIntensity * currentLevel;
    }
}