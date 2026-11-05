using System;
using System.Collections.Concurrent;
using System.IO.Ports;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;

// Reads the lines sent by game_controller.ino and moves the character this script is on.
//   MOVE LEFT|RIGHT|UP|DOWN|NONE   -> joystick
//   FORWARD 1|0                    -> button held / released
//   EAT                            -> joystick click
//   LIGHT BRIGHT|DIM               -> encoder
//
// Needs Edit > Project Settings > Player > Other Settings > Api Compatibility Level = ".NET Framework"
// (otherwise System.IO.Ports is missing). Close the Arduino Serial Monitor first, only one program can use the port.
public class ArduinoGameController : MonoBehaviour
{
    [Header("Serial")]
    [Tooltip("Mac: /dev/cu.usbmodemXXXX (Arduino IDE > Tools > Port). Windows: COM3 etc.")]
    public string portName = "/dev/cu.usbmodem1101";
    public int baudRate = 9600;

    [Header("Movement (local space of this object)")]
    public float sideSpeed = 3f;      // joystick left / right
    public float verticalSpeed = 3f;  // joystick up / down
    public float forwardSpeed = 4f;   // button held

    [Header("Light (encoder)")]
    public Light controlledLight;
    public float brightIntensity = 1.5f;
    public float dimIntensity = 0.2f;

    [Header("Events")]
    public UnityEvent onEat;
    public UnityEvent onForwardStart;
    public UnityEvent onForwardStop;

    public bool logMessages = false;

    SerialPort port;
    Thread reader;
    volatile bool running;
    readonly ConcurrentQueue<string> lines = new ConcurrentQueue<string>();

    Vector2 stick;          // x = left/right, y = up/down
    bool forwardHeld;

    void OnEnable()
    {
        try
        {
            port = new SerialPort(portName, baudRate) { ReadTimeout = 100, NewLine = "\n" };
            port.Open();
        }
        catch (Exception e)
        {
            Debug.LogError("Could not open " + portName + ": " + e.Message);
            port = null;
            return;
        }

        running = true;
        reader = new Thread(ReadLoop) { IsBackground = true };
        reader.Start();
    }

    void OnDisable()
    {
        running = false;
        if (reader != null) reader.Join(500);
        if (port != null && port.IsOpen) port.Close();
        port = null;
    }

    void ReadLoop()
    {
        while (running)
        {
            try { lines.Enqueue(port.ReadLine().Trim()); }
            catch (TimeoutException) { }
            catch (Exception) { if (!running) break; Thread.Sleep(50); }
        }
    }

    void Update()
    {
        while (lines.TryDequeue(out string line))
        {
            if (logMessages) Debug.Log("Arduino: " + line);
            Handle(line);
        }

        Vector3 step = new Vector3(stick.x * sideSpeed, stick.y * verticalSpeed, forwardHeld ? forwardSpeed : 0f);
        transform.Translate(step * Time.deltaTime, Space.Self);
    }

    void Handle(string line)
    {
        string[] p = line.Split(' ');
        if (p.Length == 0) return;

        switch (p[0])
        {
            case "MOVE":
                if (p.Length < 2) return;
                switch (p[1])
                {
                    case "LEFT":  stick = new Vector2(-1, 0); break;
                    case "RIGHT": stick = new Vector2(1, 0);  break;
                    case "UP":    stick = new Vector2(0, 1);  break;
                    case "DOWN":  stick = new Vector2(0, -1); break;
                    default:      stick = Vector2.zero;       break;
                }
                break;

            case "FORWARD":
                if (p.Length < 2) return;
                forwardHeld = p[1] == "1";
                (forwardHeld ? onForwardStart : onForwardStop)?.Invoke();
                break;

            case "EAT":
                onEat?.Invoke();
                break;

            case "LIGHT":
                if (p.Length < 2 || controlledLight == null) return;
                controlledLight.intensity = p[1] == "DIM" ? dimIntensity : brightIntensity;
                break;
        }
    }
}
