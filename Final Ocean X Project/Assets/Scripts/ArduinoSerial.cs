using System;
using System.Collections.Concurrent;
using System.IO.Ports;
using System.Threading;
using UnityEngine;

/// <summary>
/// Reads Victoria's ESP32-S3 controller over serial (115200 baud, one text line per message):
///   B:0-255                           potentiometer -> light brightness
///   J1:UP/DOWN/LEFT/RIGHT/CENTER      facing stick: up/down = rise/dive, left/right = turn
///   J2:UP/DOWN/LEFT/RIGHT/CENTER      movement stick: up = forward, left/right = strafe
///   EAT                                joystick 2 click
/// Replies "EATEN" immediately on every click so the hardware's plain LED blinks.
///
/// Put this on ONE GameObject in the scene (e.g. an empty called "ESP32", or the Player).
/// Reading happens on a background thread so the game never freezes.
/// Requires: Edit > Project Settings > Player > Api Compatibility Level = .NET Framework.
///
/// Kept the class name ArduinoSerial (even though this is the ESP32, not the old Uno) so
/// PlayerEat.cs doesn't need any changes -- it already reads ClickedThisFrame from this class.
/// </summary>
[DefaultExecutionOrder(-100)]
public class ArduinoSerial : MonoBehaviour
{
    [Header("Connection")]
    [Tooltip("Check Arduino IDE (bottom status bar, or Tools > Port) for the exact name.")]
    public string portName = "/dev/cu.usbserial-0001";
    public int baudRate = 115200;

    [Header("Debug")]
    [Tooltip("Logs every line from the ESP32.")]
    public bool logRawLines = false;

    [Header("Live values (read-only, watch these in Play mode)")]
    [SerializeField] bool connected;
    [SerializeField] Vector2 joystick1;
    [SerializeField] Vector2 joystick2;
    [SerializeField] float brightness01;
    [SerializeField] string lastLine = "";

    /// <summary>True while the serial port is open.</summary>
    public bool IsConnected => connected;
    /// <summary>Facing stick: x = turn left(-1)/right(+1), y = dive(-1)/rise(+1). Zero when centered.</summary>
    public Vector2 Joystick1 => joystick1;
    /// <summary>Movement stick: x = strafe left(-1)/right(+1), y = forward(+1)/back(unused). Zero when centered.</summary>
    public Vector2 Joystick2 => joystick2;
    /// <summary>Potentiometer, 0-1.</summary>
    public float Brightness01 => brightness01;
    /// <summary>True for exactly one frame after joystick 2 is clicked.</summary>
    public bool ClickedThisFrame => eatFrame == Time.frameCount;

    SerialPort port;
    Thread readThread;
    volatile bool running;
    readonly ConcurrentQueue<string> lines = new ConcurrentQueue<string>();
    int eatFrame = -1;

    void OnEnable()
    {
        try
        {
            port = new SerialPort(portName, baudRate)
            {
                NewLine = "\n",
                ReadTimeout = 100,
                WriteTimeout = 100,
            };
            port.Open();
        }
        catch (Exception e)
        {
            Debug.LogError($"[ArduinoSerial] Could not open {portName}: {e.Message}\n" +
                           "Check the port name (Arduino IDE > Tools > Port), close the Arduino Serial Monitor, " +
                           "and make sure Api Compatibility Level is .NET Framework.");
            port = null;
            return;
        }

        running = true;
        readThread = new Thread(ReadLoop) { IsBackground = true };
        readThread.Start();
        connected = true;
        Debug.Log($"[ArduinoSerial] Opened {portName} at {baudRate} (ESP32 protocol)");
    }

    void ReadLoop()
    {
        while (running)
        {
            try
            {
                string line = port.ReadLine().Trim();
                if (line.Length > 0) lines.Enqueue(line);
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
        while (lines.TryDequeue(out string line))
        {
            if (logRawLines) Debug.Log($"[ESP32] {line}");
            lastLine = line;
            Handle(line);
        }

        if (connected && (port == null || !port.IsOpen))
        {
            connected = false;
            joystick1 = Vector2.zero;
            joystick2 = Vector2.zero;
        }
    }

    void Handle(string line)
    {
        if (line.StartsWith("B:"))
        {
            if (int.TryParse(line.Substring(2), out int val))
                brightness01 = Mathf.Clamp01(val / 255f);
        }
        else if (line.StartsWith("J1:"))
        {
            joystick1 = DirectionToVector(line.Substring(3));
        }
        else if (line.StartsWith("J2:"))
        {
            joystick2 = DirectionToVector(line.Substring(3));
        }
        else if (line == "EAT")
        {
            eatFrame = Time.frameCount;
            Send("EATEN"); // confirms the click so the hardware's plain LED blinks
        }
    }

    static Vector2 DirectionToVector(string dir)
    {
        switch (dir)
        {
            case "UP": return new Vector2(0f, 1f);
            case "DOWN": return new Vector2(0f, -1f);
            case "LEFT": return new Vector2(-1f, 0f);
            case "RIGHT": return new Vector2(1f, 0f);
            default: return Vector2.zero; // CENTER
        }
    }

    /// <summary>Sends one text line to the ESP32.</summary>
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
        joystick1 = Vector2.zero;
        joystick2 = Vector2.zero;
    }
}
