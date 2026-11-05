using System;
using System.Collections.Concurrent;
using System.IO.Ports;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Reads the Arduino Uno game controller over serial (9600 baud, one text line per message):
///   MOVE LEFT | MOVE RIGHT | MOVE UP | MOVE DOWN | MOVE NONE   joystick direction
///   FORWARD 1 | FORWARD 0                                       button pressed / released
///   EAT                                                         joystick pressed (once per click)
///   LIGHT BRIGHT | LIGHT DIM                                    LED level (encoder)
/// Put this on ONE GameObject in the scene (e.g. an empty called "Arduino").
/// Reading happens on a background thread so the game never freezes.
/// Requires: Edit > Project Settings > Player > Api Compatibility Level = .NET Framework.
/// </summary>
[DefaultExecutionOrder(-100)]
public class ArduinoSerial : MonoBehaviour
{
    public enum StickDir { None, Left, Right, Up, Down }

    [Header("Connection")]
    [Tooltip("Mac: run  ls /dev/cu.*  in Terminal (Uno looks like /dev/cu.usbmodem1101). Windows: Device Manager > Ports, e.g. COM3.")]
    public string portName = "/dev/cu.usbmodem1101";
    public int baudRate = 9600;
    [Tooltip("If the port above isn't found, try the first Arduino-looking port (usbmodem / ttyACM / COM).")]
    public bool autoDetect = true;

    [Header("Joystick")]
    [Tooltip("Tick if pushing the stick up moves you down.")]
    public bool invertUpDown = false;
    [Tooltip("Tick if pushing the stick right turns you left.")]
    public bool invertLeftRight = false;

    [Header("Events")]
    [Tooltip("Fires once each time the joystick is pressed.")]
    public UnityEvent onEat;

    [Header("Debug")]
    [Tooltip("Logs every line from the Arduino.")]
    public bool logRawLines = false;

    [Header("Live values (read-only, watch these in Play mode)")]
    [SerializeField] bool connected;
    [SerializeField] StickDir direction = StickDir.None;
    [SerializeField] bool forwardHeld;
    [SerializeField] bool lightDim;
    [SerializeField] string lastLine = "";

    /// <summary>True while the serial port is open.</summary>
    public bool IsConnected => connected;
    /// <summary>Raw joystick direction.</summary>
    public StickDir Direction => direction;
    /// <summary>x = left(-1)/right(+1), y = down(-1)/up(+1). Both 0 when centred.</summary>
    public Vector2 Joystick { get; private set; }
    /// <summary>-1 down, 0 centred, +1 up.</summary>
    public float Vertical => Joystick.y;
    /// <summary>-1 left, 0 centred, +1 right.</summary>
    public float Horizontal => Joystick.x;
    /// <summary>True while the forward button is held.</summary>
    public bool ForwardHeld => forwardHeld;
    /// <summary>True for exactly one frame after the joystick is pressed.</summary>
    public bool EatPressedThisFrame => eatFrame == Time.frameCount;
    /// <summary>True when the encoder has set the LED to dim.</summary>
    public bool LightDim => lightDim;

    // ---- Names used by the older ESP32 scripts (FirstPersonController, PlayerEat, ...), kept so they still compile ----
    /// <summary>Always true now: the joystick's up/down always moves the player up/down (forward is the button).</summary>
    public bool UpDownMode => true;
    /// <summary>Same as EatPressedThisFrame (joystick press).</summary>
    public bool ClickedThisFrame => EatPressedThisFrame;
    /// <summary>1 = bright, 0 = dim (the Uno has no potentiometer, only the two-level knob).</summary>
    public float Knob => lightDim ? 0f : 1f;
    /// <summary>White LED, dimmed when the knob is turned.</summary>
    public Color LedColor => lightDim ? new Color(0.08f, 0.08f, 0.08f) : Color.white;
    /// <summary>The Uno has no boat alarm.</summary>
    public bool AlarmOn => false;

    SerialPort port;
    Thread readThread;
    volatile bool running;
    readonly ConcurrentQueue<string> lines = new ConcurrentQueue<string>();
    int eatFrame = -1;

    void OnEnable()
    {
        string name = ResolvePortName();

        // Windows needs this prefix for COM10 and above.
        if (name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(name.Substring(3), out int n) && n > 9)
            name = @"\\.\" + name;

        try
        {
            port = new SerialPort(name, baudRate)
            {
                NewLine = "\n",
                ReadTimeout = 100, // lets the thread notice when it should stop
                WriteTimeout = 100,
                // The Uno restarts when the port opens - that's fine, it re-sends READY and its light state.
                DtrEnable = true
            };
            port.Open();
        }
        catch (Exception e)
        {
            Debug.LogError($"[ArduinoSerial] Could not open {name}: {e.Message}\n" +
                           "Check the port name (ls /dev/cu.* in Terminal), close the Arduino Serial Monitor " +
                           "and the Arduino IDE's upload, and make sure Api Compatibility Level is .NET Framework.");
            port = null;
            return;
        }

        running = true;
        readThread = new Thread(ReadLoop) { IsBackground = true };
        readThread.Start();
        connected = true;
        Debug.Log($"[ArduinoSerial] Opened {name} at {baudRate}");
    }

    string ResolvePortName()
    {
        if (!autoDetect) return portName;
        try
        {
            string[] names = SerialPort.GetPortNames();
            foreach (string n in names) if (n == portName) return portName;
            foreach (string n in names)
                if (n.Contains("usbmodem") || n.Contains("ttyACM") || n.StartsWith("COM", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.Log($"[ArduinoSerial] {portName} not found, using {n} instead.");
                    return n;
                }
        }
        catch (Exception e) { Debug.LogWarning($"[ArduinoSerial] Port search failed: {e.Message}"); }
        return portName;
    }

    void ReadLoop()
    {
        while (running)
        {
            try
            {
                lines.Enqueue(port.ReadLine().Trim());
            }
            catch (TimeoutException) { }
            catch (Exception e)
            {
                if (running) Debug.LogWarning($"[ArduinoSerial] Read error: {e.Message}");
                break;
            }
        }
    }

    void Update()
    {
        // Handle every line in order (unlike a stream of positions, a missed EAT would be a missed click).
        while (lines.TryDequeue(out string line))
        {
            if (line.Length == 0) continue;
            if (logRawLines) Debug.Log($"[Arduino] {line}");
            lastLine = line;
            Handle(line);
        }

        if (connected && (port == null || !port.IsOpen))
        {
            connected = false;
            Joystick = Vector2.zero;
            direction = StickDir.None;
            forwardHeld = false;
        }
    }

    void Handle(string line)
    {
        string[] p = line.ToUpperInvariant().Split(' ');

        switch (p[0])
        {
            case "MOVE":
                if (p.Length < 2) return;
                switch (p[1])
                {
                    case "LEFT":  direction = StickDir.Left;  break;
                    case "RIGHT": direction = StickDir.Right; break;
                    case "UP":    direction = StickDir.Up;    break;
                    case "DOWN":  direction = StickDir.Down;  break;
                    default:      direction = StickDir.None;  break;
                }
                float x = direction == StickDir.Right ? 1f : direction == StickDir.Left ? -1f : 0f;
                float y = direction == StickDir.Up ? 1f : direction == StickDir.Down ? -1f : 0f;
                Joystick = new Vector2(invertLeftRight ? -x : x, invertUpDown ? -y : y);
                break;

            case "FORWARD":
                forwardHeld = p.Length > 1 && p[1] == "1";
                break;

            case "EAT":
                eatFrame = Time.frameCount;
                onEat?.Invoke();
                break;

            case "LIGHT":
                lightDim = p.Length > 1 && p[1] == "DIM";
                break;

            case "READY":
                Debug.Log("[ArduinoSerial] Controller is ready.");
                break;
        }
    }

    /// <summary>Sends one text line to the Arduino (the current sketch doesn't listen for any, but it's here if you add some).</summary>
    public void Send(string command)
    {
        if (port == null || !port.IsOpen) return;
        try { port.Write(command + "\n"); }
        catch (Exception e) { Debug.LogWarning($"[ArduinoSerial] Write error: {e.Message}"); }
    }

    void OnDisable() => Close();
    void OnApplicationQuit() => Close();

    void Close()
    {
        running = false;
        if (readThread != null && readThread.IsAlive) readThread.Join(500);
        readThread = null;
        if (port != null)
        {
            try { if (port.IsOpen) port.Close(); } catch { }
            port = null;
        }
        connected = false;
        Joystick = Vector2.zero;
        direction = StickDir.None;
        forwardHeld = false;
    }
}