using Rti.Dds.Subscription;
using Rti.Types.Dynamic;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Omg.Types;

public class FanucDataSubscriber : MonoBehaviour
{
    [Header("Robot Configuration")]
    public List<ArticulationBody> joints;
    public Transform worldPosition;

    [Header("UI Configuration")]
    public TextMeshProUGUI jointDisplay;

    [Header("DDS Configuration")]
    [SerializeField] private string topicName = "RobotState_Topic";
    [SerializeField] private string typeName = "RobotDDS::RobotState";

    [Header("Debugging")]
    [Tooltip("If true, prints received values to console.")]
    [SerializeField] private bool debugLogging = true;
    [Tooltip("How often to print logs in seconds (prevents console flooding).")]
    [SerializeField] private float logInterval = 1.0f;
    private float _lastLogTime = 0f;

    private DataReader<DynamicData> _reader;
    private bool _hasReceivedData = false;

    private void Start()
    {
        InitializeDDS();
    }

    private void InitializeDDS()
    {
        if (DDSHandler.Instance == null)
        {
            Debug.LogError("FanucDataSubscriber: DDSHandler Instance is null.");
            return;
        }

        try
        {
            var typeFactory = DynamicTypeFactory.Instance;

            StructType robotStateType = typeFactory.BuildStruct()
                .WithName(typeName)
                .WithExtensibility(ExtensibilityKind.Final) 
                .AddMember(new StructMember("Clock", typeFactory.CreateString(bounds: 50)))
                .AddMember(new StructMember("SampleId", typeFactory.GetPrimitiveType<int>()))
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
            CheckConnectionStatus();
            ProcessData(_reader);
        }
    }

    private bool _isConnected = false;

    private void CheckConnectionStatus()
    {
        // Check if we have matched with a Publisher
        var status = _reader.SubscriptionMatchedStatus;
        if (status.CurrentCount.Value > 0 && !_isConnected)
        {
            _isConnected = true;
            Debug.Log("<color=green><b>SUCCESS: DDS Publisher Discovered!</b></color>");
        }
        else if (status.CurrentCount.Value == 0 && _isConnected)
        {
            _isConnected = false;
            Debug.LogWarning("<color=red><b>WARNING: DDS Publisher Lost!</b></color>");
        }
    }

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
                    DynamicData data = sample.Data;

                    if (!_hasReceivedData)
                    {
                        Debug.Log("FanucDataSubscriber: Connection confirmed. Processing First Packet...");
                        _hasReceivedData = true;
                    }

                    // 1. Extract Raw Data
                    int sampleId = data.GetValue<int>("SampleId");
                    string clock = data.GetValue<string>("Clock");

                    float j1 = (float)data.GetValue<double>("J1");
                    float j2 = (float)data.GetValue<double>("J2");
                    float j3 = (float)data.GetValue<double>("J3");
                    float j4 = (float)data.GetValue<double>("J4");
                    float j5 = (float)data.GetValue<double>("J5");
                    float j6 = (float)data.GetValue<double>("J6");

                    // Raw Cartesian (Direct from Robot)
                    float rawX = (float)data.GetValue<double>("X");
                    float rawY = (float)data.GetValue<double>("Y");
                    float rawZ = (float)data.GetValue<double>("Z");
                    float w = (float)data.GetValue<double>("W");
                    float p = (float)data.GetValue<double>("P");
                    float r = (float)data.GetValue<double>("R");

                    // 2. Debug Logging (Throttled)
                    if (debugLogging && (Time.time - _lastLogTime > logInterval))
                    {
                        Debug.Log($"<color=cyan><b>DDS RX [#{sampleId}]</b></color> | Time: {clock}\n" +
                                  $"<b>Joints:</b> {j1:F2}, {j2:F2}, {j3:F2}, {j4:F2}, {j5:F2}, {j6:F2}\n" +
                                  $"<b>Pos:</b> X:{rawX:F2} Y:{rawY:F2} Z:{rawZ:F2} W:{w:F2} P:{p:F2} R:{r:F2}");
                        _lastLogTime = Time.time;
                    }

                    // 3. Unity Conversion (Scale & Axis Flip)
                    float unityX = rawX / 1000f;
                    float unityY = rawY / 1000f;
                    float unityZ = rawZ / 1000f;

                    UpdateRobotState(j1, j2, j3, j4, j5, j6, unityX, unityY, unityZ, w, p, r);
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"FanucDataSubscriber: ERROR processing samples: {e.Message}\nStack: {e.StackTrace}");
        }
    }

    private void UpdateRobotState(float j1, float j2, float j3, float j4, float j5, float j6, float x, float y, float z, float w, float p, float r)
    {
        if (worldPosition != null)
        {
            // Position Logic: Negate X for Left-Handed System
            worldPosition.localPosition = new Vector3(-x, y, z);
            
            // Rotation Logic
            Vector3 eulerAngles = CreateQuaternionFromFanucWPR(w, p, r).eulerAngles;
            worldPosition.localEulerAngles = new Vector3(eulerAngles.x, -eulerAngles.y, -eulerAngles.z);
        }

        if (joints != null && joints.Count >= 6)
        {
            UpdateJoint(0, j1);
            UpdateJoint(1, j2);
            UpdateJoint(2, j3 + j2); // Coupling J2/J3 is common in some robots
            UpdateJoint(3, j4);
            UpdateJoint(4, j5);
            UpdateJoint(5, j6);
        }

        if (jointDisplay != null)
        {
            jointDisplay.text = $"J1: {j1:F2}\nJ2: {j2:F2}\nJ3: {j3:F2}\nJ4: {j4:F2}\nJ5: {j5:F2}\nJ6: {j6:F2}";
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