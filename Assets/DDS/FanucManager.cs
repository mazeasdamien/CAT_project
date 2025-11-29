using Rti.Dds.Subscription;
using Rti.Types.Dynamic;
using System.Collections.Generic;
using UnityEngine;

// The FanucManager class handles the reception and processing of Fanuc robotic system data through DDS.
public class FanucManager : MonoBehaviour
{
    public List<ArticulationBody> joints; // List of ArticulationBody components representing robotic joints.
    public Transform worldPosition; // Transform representing the world position of the robotic system.
    
    private protected DataReader<DynamicData> reader { get; private set; } // DDS DataReader for dynamic data.

    // Called when the script starts. Initializes the DDS connection.
    private void Start()
    {
        InitializeDDS();
    }

    private void InitializeDDS()
    {
        if (DDSHandler.Instance == null)
        {
            Debug.LogError("DDSHandler Instance is null. Ensure DDSHandler is present in the scene.");
            return;
        }

        // DynamicType setup for the "RobotState" data structure.
        var typeFactory = DynamicTypeFactory.Instance;
        StructType RobotState = typeFactory.BuildStruct()
            .WithName("RobotState")
            .AddMember(new StructMember("J1", typeFactory.GetPrimitiveType<double>()))
            .AddMember(new StructMember("J2", typeFactory.GetPrimitiveType<double>()))
            .AddMember(new StructMember("J3", typeFactory.GetPrimitiveType<double>()))
            .AddMember(new StructMember("J4", typeFactory.GetPrimitiveType<double>()))
            .AddMember(new StructMember("J5", typeFactory.GetPrimitiveType<double>()))
            .AddMember(new StructMember("J6", typeFactory.GetPrimitiveType<double>()))
            .AddMember(new StructMember("X", typeFactory.GetPrimitiveType<double>()))
            .AddMember(new StructMember("Y", typeFactory.GetPrimitiveType<double>()))
            .AddMember(new StructMember("Z", typeFactory.GetPrimitiveType<double>()))
            .AddMember(new StructMember("W", typeFactory.GetPrimitiveType<double>()))
            .AddMember(new StructMember("P", typeFactory.GetPrimitiveType<double>()))
            .AddMember(new StructMember("R", typeFactory.GetPrimitiveType<double>()))
            .Create();

        // Setup DDS DataReader for the "RobotState_Topic".
        reader = DDSHandler.Instance.SetupDataReader("RobotState_Topic", RobotState);
    }

    // Called every frame. Processes incoming data.
    private void Update()
    {
        if (reader != null)
        {
            ProcessData(reader);
        }
    }

    // Processes DDS data received from the DataReader.
    void ProcessData(AnyDataReader anyReader)
    {
        var reader = (DataReader<DynamicData>)anyReader;
        using var samples = reader.Take();
        
        foreach (var sample in samples)
        {
            if (sample.Info.ValidData)
            {
                DynamicData data = sample.Data;

                // Extract values once to avoid repeated string lookups
                float j1 = (float)data.GetValue<double>("J1");
                float j2 = (float)data.GetValue<double>("J2");
                float j3 = (float)data.GetValue<double>("J3");
                float j4 = (float)data.GetValue<double>("J4");
                float j5 = (float)data.GetValue<double>("J5");
                float j6 = (float)data.GetValue<double>("J6");

                float x = (float)data.GetValue<double>("X");
                float y = (float)data.GetValue<double>("Y");
                float z = (float)data.GetValue<double>("Z");

                float w = (float)data.GetValue<double>("W");
                float p = (float)data.GetValue<double>("P");
                float r = (float)data.GetValue<double>("R");

                // Update the world position (scaled down by 1000 for mm to m conversion).
                worldPosition.localPosition = new Vector3(-x / 1000f, y / 1000f, z / 1000f);

                // Create a Quaternion from Fanuc WPR angles and apply it to localEulerAngles.
                Vector3 eulerAngles = CreateQuaternionFromFanucWPR(w, p, r).eulerAngles;
                worldPosition.localEulerAngles = new Vector3(eulerAngles.x, -eulerAngles.y, -eulerAngles.z);

                // Update joint positions
                UpdateJoint(0, j1);
                UpdateJoint(1, j2);
                UpdateJoint(2, j3 + j2); // Apply coupling compensation
                UpdateJoint(3, j4);
                UpdateJoint(4, j5);
                UpdateJoint(5, j6);
            }
        }
    }

    // Helper to update a single joint
    void UpdateJoint(int index, float targetAngle)
    {
        if (index >= 0 && index < joints.Count)
        {
            var drive = joints[index].xDrive;
            drive.target = targetAngle;
            joints[index].xDrive = drive;
        }
    }

    // Creates a Quaternion from Fanuc WPR (Wrist Pitch Roll) angles in degrees.
    public Quaternion CreateQuaternionFromFanucWPR(float W, float P, float R)
    {
        // Conversion from degrees to radians using constant.
        float Wrad = W * Mathf.Deg2Rad;
        float Prad = P * Mathf.Deg2Rad;
        float Rrad = R * Mathf.Deg2Rad;

        float cosR = Mathf.Cos(Rrad / 2);
        float sinR = Mathf.Sin(Rrad / 2);
        float cosP = Mathf.Cos(Prad / 2);
        float sinP = Mathf.Sin(Prad / 2);
        float cosW = Mathf.Cos(Wrad / 2);
        float sinW = Mathf.Sin(Wrad / 2);

        // Quaternion calculation based on Fanuc WPR angles.
        float qx = (cosR * cosP * sinW) - (sinR * sinP * cosW);
        float qy = (cosR * sinP * cosW) + (sinR * cosP * sinW);
        float qz = (sinR * cosP * cosW) - (cosR * sinP * sinW);
        float qw = (cosR * cosP * cosW) + (sinR * sinP * sinW);

        return new Quaternion(qx, qy, qz, qw);
    }
}
