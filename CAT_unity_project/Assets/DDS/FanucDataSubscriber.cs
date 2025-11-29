using Rti.Dds.Subscription;
using Rti.Types.Dynamic;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The FanucDataSubscriber class is responsible for receiving robot state data from the DDS network
/// and synchronizing the digital twin's movement with the physical (or virtual) robot.
/// 
/// Functionality:
/// 1. Subscribes to the "RobotState_Topic" using RTI Connext DDS.
/// 2. Decodes the dynamic data packet containing joint angles and Cartesian coordinates.
/// 3. Updates the Unity ArticulationBody components to drive the physics-based robot model.
/// 4. Handles coordinate system transformations between the industrial robot (Fanuc) and Unity.
/// </summary>
public class FanucDataSubscriber : MonoBehaviour
{
    [Header("Robot Configuration")]
    [Tooltip("List of ArticulationBodies representing the robot joints.")]
    public List<ArticulationBody> joints;

    [Tooltip("The root transform for the robot's world position.")]
    public Transform worldPosition;

    [Header("DDS Configuration")]
    [Tooltip("The name of the DDS topic to subscribe to.")]
    [SerializeField] private string topicName = "RobotState_Topic";

    [Tooltip("The name of the struct type in DDS.")]
    [SerializeField] private string typeName = "RobotState";

    private DataReader<DynamicData> _reader;
    private bool _hasReceivedData = false;

    // Member IDs
    private int _idJ1, _idJ2, _idJ3, _idJ4, _idJ5, _idJ6;
    private int _idX, _idY, _idZ;
    private int _idW, _idP, _idR;

    private void Start()
    {
        InitializeDDS();
    }

    /// <summary>
    /// Initializes the DDS DataReader for the robot state topic.
    /// This method defines the expected data structure (DynamicType) to match the publisher's schema
    /// and registers the subscription via the central DDSHandler.
    /// </summary>
    private void InitializeDDS()
    {
        if (DDSHandler.Instance == null)
        {
            Debug.LogError("FanucDataSubscriber: DDSHandler Instance is null.");
            return;
        }

        try
        {
            // --- IMPORTANT: DATA TYPE DEFINITION ---
            // This MUST match the Publisher's definition EXACTLY.
            // Publisher has: Clock (String), Sample (Int), then Joints.

            var typeFactory = DynamicTypeFactory.Instance;

            // Define struct
            StructType robotStateType = typeFactory.BuildStruct()
                .WithName(typeName)
                .AddMember(new StructMember("Clock", typeFactory.CreateString(bounds: 50))) // MUST BE HERE
                .AddMember(new StructMember("Sample", typeFactory.GetPrimitiveType<int>())) // MUST BE HERE
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

            // Cache IDs to ensure we are looking up valid members
            // If these throw an error, it means the Type Build failed
            _idJ1 = robotStateType.GetMember("J1").Id;
            _idX = robotStateType.GetMember("X").Id; // Checking just a couple for safety

            Debug.Log("FanucDataSubscriber: Type Definition Built. Attempting to create Reader...");

            _reader = DDSHandler.Instance.SetupDataReader(topicName, robotStateType);

            if (_reader != null)
            {
                Debug.Log($"FanucDataSubscriber: SUCCESS. Subscribed to '{topicName}'. Waiting for data...");
            }
            else
            {
                Debug.LogError($"FanucDataSubscriber: FAILURE. SetupDataReader returned null.");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"FanucDataSubscriber: CRITICAL ERROR in InitializeDDS: {e.Message}\n{e.StackTrace}");
        }
    }

    private void Update()
    {
        if (_reader != null)
        {
            ProcessData(_reader);
        }
    }

    /// <summary>
    /// Processes incoming data samples from the DDS DataReader.
    /// Iterates through the received batch, validates data, and triggers the robot state update.
    /// </summary>
    /// <param name="anyReader">The untyped DataReader received from the middleware.</param>
    private void ProcessData(AnyDataReader anyReader)
    {
        var reader = (DataReader<DynamicData>)anyReader;

        try
        {
            // Attempt to take data
            using var samples = reader.Take();

            if (samples.Count > 0)
            {
                // DEBUG: Log that we actually touched the network
                Debug.Log($"FanucDataSubscriber: Received batch of {samples.Count} samples.");
            }

            foreach (var sample in samples)
            {
                if (sample.Info.ValidData)
                {
                    DynamicData data = sample.Data;

                    // DEBUG: Print raw values of the first joint to verify content
                    double debugJ1 = data.GetValue<double>("J1");
                    int debugSampleId = data.GetValue<int>("Sample");

                    Debug.Log($"<color=green>DATA RECEIVED:</color> SampleID: {debugSampleId} | J1: {debugJ1}");

                    if (!_hasReceivedData)
                    {
                        Debug.Log("FanucDataSubscriber: Connection confirmed. Processing First Packet...");
                        _hasReceivedData = true;
                    }

                    // Extract Full Data
                    float j1 = (float)data.GetValue<double>("J1");
                    float j2 = (float)data.GetValue<double>("J2");
                    float j3 = (float)data.GetValue<double>("J3");
                    float j4 = (float)data.GetValue<double>("J4");
                    float j5 = (float)data.GetValue<double>("J5");
                    float j6 = (float)data.GetValue<double>("J6");

                    float x = (float)data.GetValue<double>("X") / 100;
                    float y = (float)data.GetValue<double>("Y") / 100;
                    float z = (float)data.GetValue<double>("Z") / 100;
                    float w = (float)data.GetValue<double>("W");
                    float p = (float)data.GetValue<double>("P");
                    float r = (float)data.GetValue<double>("R");

                    // DEBUG: Log all joint values
                    Debug.Log($"<color=cyan>JOINTS:</color> J1:{j1:F2} | J2:{j2:F2} | J3:{j3:F2} | J4:{j4:F2} | J5:{j5:F2} | J6:{j6:F2}");

                    UpdateRobotState(j1, j2, j3, j4, j5, j6, x, y, z, w, p, r);
                }
                else
                {
                    // This happens when a publisher disconnects or changes liveness
                    Debug.LogWarning($"FanucDataSubscriber: Received Meta-Data (Invalid Data). State: {sample.Info.State.Instance}");
                }
            }
        }
        catch (System.Exception e)
        {
            // This is where Type Mismatches usually show up
            Debug.LogError($"FanucDataSubscriber: ERROR processing samples: {e.Message}\nStack: {e.StackTrace}");
        }
    }

    /// <summary>
    /// Updates the visual and physical representation of the robot based on the received state.
    /// Performs necessary coordinate system conversions (Right-Handed to Left-Handed) and 
    /// handles mechanical coupling logic.
    /// </summary>
    private void UpdateRobotState(float j1, float j2, float j3, float j4, float j5, float j6, float x, float y, float z, float w, float p, float r)
    {
        if (worldPosition != null)
        {
            // Position is already scaled in ProcessData (divided by 100), so we use it directly here.
            // Note: Coordinate conversion (X inverted) is still applied.
            worldPosition.localPosition = new Vector3(-x, y, z);
            Vector3 eulerAngles = CreateQuaternionFromFanucWPR(w, p, r).eulerAngles;
            worldPosition.localEulerAngles = new Vector3(eulerAngles.x, -eulerAngles.y, -eulerAngles.z);
        }

        if (joints != null && joints.Count >= 6)
        {
            UpdateJoint(0, j1);
            UpdateJoint(1, j2);
            UpdateJoint(2, j3 + j2);
            UpdateJoint(3, j4);
            UpdateJoint(4, j5);
            UpdateJoint(5, j6);
        }
    }

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
    /// Converts Fanuc Euler angles (Yaw-Pitch-Roll) to a Unity Quaternion.
    /// Fanuc uses a specific rotation order (W, P, R) which corresponds to rotation around X, Y, and Z axes.
    /// </summary>
    /// <param name="W">Yaw (Rotation around X) in degrees.</param>
    /// <param name="P">Pitch (Rotation around Y) in degrees.</param>
    /// <param name="R">Roll (Rotation around Z) in degrees.</param>
    /// <returns>Unity Quaternion representing the orientation.</returns>
    public Quaternion CreateQuaternionFromFanucWPR(float W, float P, float R)
    {
        float Wrad = W * Mathf.Deg2Rad;
        float Prad = P * Mathf.Deg2Rad;
        float Rrad = R * Mathf.Deg2Rad;

        float cosR = Mathf.Cos(Rrad * 0.5f);
        float sinR = Mathf.Sin(Rrad * 0.5f);
        float cosP = Mathf.Cos(Prad * 0.5f);
        float sinP = Mathf.Sin(Prad * 0.5f);
        float cosW = Mathf.Cos(Wrad * 0.5f);
        float sinW = Mathf.Sin(Wrad * 0.5f);

        float qx = (cosR * cosP * sinW) - (sinR * sinP * cosW);
        float qy = (cosR * sinP * cosW) + (sinR * cosP * sinW);
        float qz = (sinR * cosP * cosW) - (cosR * sinP * sinW);
        float qw = (cosR * cosP * cosW) + (sinR * sinP * sinW);

        return new Quaternion(qx, qy, qz, qw);
    }
}