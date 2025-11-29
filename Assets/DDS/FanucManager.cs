using Rti.Dds.Subscription;
using Rti.Types.Dynamic;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The FanucManager class handles the reception and processing of Fanuc robotic system data through DDS.
/// It subscribes to a specified DDS Topic and updates the Unity ArticulationBodies and Transform accordingly.
/// </summary>
public class FanucManager : MonoBehaviour
{
    [Header("Robot Configuration")]
    [Tooltip("List of ArticulationBody components representing robotic joints.")]
    public List<ArticulationBody> joints;

    [Tooltip("Transform representing the world position of the robotic system.")]
    public Transform worldPosition;

    [Header("DDS Configuration")]
    [Tooltip("Name of the DDS Topic to subscribe to.")]
    [SerializeField] private string topicName = "RobotState_Topic";

    // DDS DataReader for dynamic data.
    private DataReader<DynamicData> _reader;
    
    // Flag to log the first received packet for debugging.
    private bool _hasReceivedData = false;

    // Cached Member IDs for the Dynamic Data Struct to optimize lookup performance.
    private int _idJ1, _idJ2, _idJ3, _idJ4, _idJ5, _idJ6;
    private int _idX, _idY, _idZ;
    private int _idW, _idP, _idR;

    /// <summary>
    /// Called when the script starts. Initializes the DDS connection.
    /// </summary>
    private void Start()
    {
        InitializeDDS();
    }

    /// <summary>
    /// Initializes the DDS DataReader and the DynamicType definition.
    /// </summary>
    private void InitializeDDS()
    {
        if (DDSHandler.Instance == null)
        {
            Debug.LogError("FanucManager: DDSHandler Instance is null. Ensure DDSHandler is present in the scene and initialized.");
            return;
        }

        try
        {
            // DynamicType setup for the "RobotState" data structure.
            var typeFactory = DynamicTypeFactory.Instance;
            var doubleType = typeFactory.GetPrimitiveType<double>();

            StructType robotStateType = typeFactory.BuildStruct()
                .WithName("RobotState")
                .AddMember(new StructMember("J1", doubleType))
                .AddMember(new StructMember("J2", doubleType))
                .AddMember(new StructMember("J3", doubleType))
                .AddMember(new StructMember("J4", doubleType))
                .AddMember(new StructMember("J5", doubleType))
                .AddMember(new StructMember("J6", doubleType))
                .AddMember(new StructMember("X", doubleType))
                .AddMember(new StructMember("Y", doubleType))
                .AddMember(new StructMember("Z", doubleType))
                .AddMember(new StructMember("W", doubleType))
                .AddMember(new StructMember("P", doubleType))
                .AddMember(new StructMember("R", doubleType))
                .Create();

            // Cache Member IDs for performance
            _idJ1 = robotStateType.GetMember("J1").Id;
            _idJ2 = robotStateType.GetMember("J2").Id;
            _idJ3 = robotStateType.GetMember("J3").Id;
            _idJ4 = robotStateType.GetMember("J4").Id;
            _idJ5 = robotStateType.GetMember("J5").Id;
            _idJ6 = robotStateType.GetMember("J6").Id;
            _idX = robotStateType.GetMember("X").Id;
            _idY = robotStateType.GetMember("Y").Id;
            _idZ = robotStateType.GetMember("Z").Id;
            _idW = robotStateType.GetMember("W").Id;
            _idP = robotStateType.GetMember("P").Id;
            _idR = robotStateType.GetMember("R").Id;

            // Setup DDS DataReader for the configured topic.
            _reader = DDSHandler.Instance.SetupDataReader(topicName, robotStateType);

            if (_reader != null)
            {
                Debug.Log($"FanucManager: Successfully subscribed to topic '{topicName}'.");
            }
            else
            {
                Debug.LogError($"FanucManager: Failed to subscribe to topic '{topicName}'.");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"FanucManager: Exception during initialization - {e.Message}");
        }
    }

    /// <summary>
    /// Called every frame. Processes incoming data from DDS.
    /// </summary>
    private void Update()
    {
        if (_reader != null)
        {
            ProcessData(_reader);
        }
    }

    /// <summary>
    /// Processes DDS data received from the DataReader.
    /// </summary>
    /// <param name="anyReader">The DataReader instance.</param>
    private void ProcessData(AnyDataReader anyReader)
    {
        var reader = (DataReader<DynamicData>)anyReader;
        
        try 
        {
            using var samples = reader.Take();
            
            foreach (var sample in samples)
            {
                if (sample.Info.ValidData)
                {
                    if (!_hasReceivedData)
                    {
                        Debug.Log("FanucManager: First valid data packet received.");
                        _hasReceivedData = true;
                    }

                    DynamicData data = sample.Data;

                    // Extract values using cached Integer IDs (faster than string lookup)
                    float j1 = (float)data.GetValue<double>(_idJ1);
                    float j2 = (float)data.GetValue<double>(_idJ2);
                    float j3 = (float)data.GetValue<double>(_idJ3);
                    float j4 = (float)data.GetValue<double>(_idJ4);
                    float j5 = (float)data.GetValue<double>(_idJ5);
                    float j6 = (float)data.GetValue<double>(_idJ6);

                    float x = (float)data.GetValue<double>(_idX);
                    float y = (float)data.GetValue<double>(_idY);
                    float z = (float)data.GetValue<double>(_idZ);

                    float w = (float)data.GetValue<double>(_idW);
                    float p = (float)data.GetValue<double>(_idP);
                    float r = (float)data.GetValue<double>(_idR);

                    UpdateRobotState(j1, j2, j3, j4, j5, j6, x, y, z, w, p, r);
                }
            }
        }
        catch (System.Exception e)
        {
             Debug.LogError($"FanucManager: Error processing data - {e.Message}");
        }
    }

    /// <summary>
    /// Updates the robot's visual and physical state based on received data.
    /// </summary>
    private void UpdateRobotState(float j1, float j2, float j3, float j4, float j5, float j6, float x, float y, float z, float w, float p, float r)
    {
        // Update the world position (scaled down by 1000 for mm to m conversion).
        if (worldPosition != null)
        {
            worldPosition.localPosition = new Vector3(-x / 1000f, y / 1000f, z / 1000f);

            // Create a Quaternion from Fanuc WPR angles and apply it to localEulerAngles.
            Vector3 eulerAngles = CreateQuaternionFromFanucWPR(w, p, r).eulerAngles;
            worldPosition.localEulerAngles = new Vector3(eulerAngles.x, -eulerAngles.y, -eulerAngles.z);
        }

        // Update joint positions
        // Ensure we have enough joints configured
        if (joints != null && joints.Count >= 6)
        {
            UpdateJoint(0, j1);
            UpdateJoint(1, j2);
            UpdateJoint(2, j3 + j2); // Apply coupling compensation (J3 depends on J2 for this robot)
            UpdateJoint(3, j4);
            UpdateJoint(4, j5);
            UpdateJoint(5, j6);
        }
    }

    /// <summary>
    /// Helper to update a single joint's target position.
    /// </summary>
    /// <param name="index">Index of the joint in the joints list.</param>
    /// <param name="targetAngle">Target angle in degrees.</param>
    private void UpdateJoint(int index, float targetAngle)
    {
        if (index >= 0 && index < joints.Count)
        {
            var drive = joints[index].xDrive;
            drive.target = targetAngle;
            joints[index].xDrive = drive;
        }
    }

    /// <summary>
    /// Creates a Quaternion from Fanuc WPR (Wrist Pitch Roll) angles in degrees.
    /// Fanuc uses a specific Euler angle convention (often Z-Y-X intrinsic, but here implemented manually).
    /// </summary>
    /// <param name="W">Yaw (Rotation around Z) in degrees.</param>
    /// <param name="P">Pitch (Rotation around Y) in degrees.</param>
    /// <param name="R">Roll (Rotation around X) in degrees.</param>
    /// <returns>Calculated Quaternion.</returns>
    public Quaternion CreateQuaternionFromFanucWPR(float W, float P, float R)
    {
        // Conversion from degrees to radians.
        float Wrad = W * Mathf.Deg2Rad;
        float Prad = P * Mathf.Deg2Rad;
        float Rrad = R * Mathf.Deg2Rad;

        // Precompute trig values
        float cosR = Mathf.Cos(Rrad * 0.5f);
        float sinR = Mathf.Sin(Rrad * 0.5f);
        float cosP = Mathf.Cos(Prad * 0.5f);
        float sinP = Mathf.Sin(Prad * 0.5f);
        float cosW = Mathf.Cos(Wrad * 0.5f);
        float sinW = Mathf.Sin(Wrad * 0.5f);

        // Quaternion calculation
        float qx = (cosR * cosP * sinW) - (sinR * sinP * cosW);
        float qy = (cosR * sinP * cosW) + (sinR * cosP * sinW);
        float qz = (sinR * cosP * cosW) - (cosR * sinP * sinW);
        float qw = (cosR * cosP * cosW) + (sinR * sinP * sinW);

        return new Quaternion(qx, qy, qz, qw);
    }
}
