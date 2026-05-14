using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Test : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        DataLogger.Instance.LogEvento("TestStart", "Unity connected");
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            DataLogger.Instance.LogEvento("KeyPress", "Space");
        }
    }
}
