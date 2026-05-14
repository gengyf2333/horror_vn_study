using UnityEngine;
using System.IO;
using System;

public class DataLogger : MonoBehaviour
{
    public static DataLogger Instance;

    [Header("Connect LSL")]
    public LSLMarkerStream lslStream;

    private string filePath;
    private StreamWriter writer;

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        // CSV Config
        string folder = Application.dataPath + "/DatosUsuarios/";
        if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
        string fileName = "Sesion_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".csv";
        filePath = Path.Combine(folder, fileName);
        writer = new StreamWriter(filePath, true);
        writer.WriteLine("UnixTimestamp_ms;Event;Details");
    }

    public void LogEvento(string evento, string details = "")
    {
        // 1. SEND TO LSL
        if (lslStream != null)
        {
            string msgLSL = $"{evento}: {details}";
            lslStream.Write(msgLSL);
        }

        // 2. SAVE CSV
        long unixTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        string line = $"{unixTime};{evento};{details}";
        if (writer != null) writer.WriteLine(line);
    }

    void OnApplicationQuit()
    {
        if (writer != null) writer.Close();
    }
}