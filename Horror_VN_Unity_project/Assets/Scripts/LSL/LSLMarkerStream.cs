using UnityEngine;
using LSL; // Esta librería s?la tienes gracias al paquete que instalaste

public class LSLMarkerStream : MonoBehaviour
{
    [Header("Stream Config")]
    public string streamName = "UnityEvents";
    public string streamType = "Markers";
    public string streamID = "Unity_ID_12345";

    private StreamOutlet outlet;
    private StreamInfo streamInfo;

    void Start()
    {
        // 1. Define stream information
        // Arguments: Name, Type, Channels (1), Frequency (0=Irregular), Format (String), ID
        streamInfo = new StreamInfo(streamName, streamType, 1, 0, channel_format_t.cf_string, streamID);

        // 2. Create the "Outlet" (the exit door)
        outlet = new StreamOutlet(streamInfo);

        Debug.Log($"[LSL] Stream '{streamName}' created and waiting to sendd markers.");
    }

    // Function that calls the DataLogger
    public void Write(string markerText)
    {
        if (outlet != null)
        {
            // LSL requiere un array, aunque sea de un solo elemento
            string[] sample = { markerText };
            outlet.push_sample(sample);
        }
    }
}