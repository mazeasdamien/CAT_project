using System;
using System.IO;
using UnityEngine;
using OpenDDSharp;
using OpenDDSharp.DDS;
using OpenDDSharp.OpenDDS.DCPS;
using RobotDDS;
using System.Collections.Generic;

public class UnityRobotSubscriber : MonoBehaviour
{
    [Header("DDS Configuration")]
    public int DomainId = 0;
    public string TopicName = "RobotState_Topic";

    [Header("Robot Visuals")]
    public Transform RobotRoot;
    public ArticulationBody[] Joints;

    [Header("Debug")]
    public bool VerboseLogging = true;

    private DomainParticipantFactory _dpf;
    private DomainParticipant _participant;
    private RobotStateDataReader _robotReader;
    private RobotStateListener _listener;

    // Thread-safe Data Exchange
    private object _dataLock = new object();
    private RobotState _latestState = null;
    private bool _hasNewData = false;

    void Start()
    {
        InitializeDDS();
    }

    void InitializeDDS()
    {
        try
        {
            LogInfo("--- Starting DDS Initialization ---");

            // 1. Check Config File
            string configPath = Path.Combine(Application.streamingAssetsPath, "rtps.ini");
            if (!File.Exists(configPath))
            {
                LogError($"CRITICAL: Config file not found at: {configPath}");
                return;
            }
            LogInfo($"Config file found: {configPath}");

            // 2. Init ACE
            Ace.Init();
            LogInfo("ACE Initialized.");

            // 3. Get Factory
            _dpf = ParticipantService.Instance.GetDomainParticipantFactory("-DCPSConfigFile", configPath);
            if (_dpf == null)
            {
                LogError("CRITICAL: Failed to get DomainParticipantFactory.");
                return;
            }

            // 4. Create Participant
            _participant = _dpf.CreateParticipant(DomainId);
            if (_participant == null)
            {
                LogError($"CRITICAL: Could not create DomainParticipant for Domain ID {DomainId}.");
                return;
            }

            // 5. Register Type
            RobotStateTypeSupport support = new RobotStateTypeSupport();
            if (support.RegisterType(_participant, support.GetTypeName()) != ReturnCode.Ok)
            {
                LogError("CRITICAL: Failed to register RobotState type.");
                return;
            }

            // 6. Create Topic
            Topic topic = _participant.CreateTopic(TopicName, support.GetTypeName());
            if (topic == null)
            {
                LogError($"CRITICAL: Failed to create Topic '{TopicName}'.");
                return;
            }

            // 7. Create Subscriber
            Subscriber subscriber = _participant.CreateSubscriber();
            if (subscriber == null)
            {
                LogError("CRITICAL: Failed to create Subscriber.");
                return;
            }

            // 8. QoS Setup
            DataReaderQos readerQos = new DataReaderQos();
            subscriber.GetDefaultDataReaderQos(readerQos);
            readerQos.Reliability.Kind = ReliabilityQosPolicyKind.ReliableReliabilityQos;
            readerQos.Durability.Kind = DurabilityQosPolicyKind.TransientLocalDurabilityQos;
            readerQos.History.Kind = HistoryQosPolicyKind.KeepLastHistoryQos;
            readerQos.History.Depth = 1;

            // 9. Create Reader & Listener
            _listener = new RobotStateListener(this);
            DataReader genericReader = subscriber.CreateDataReader(topic, readerQos, _listener, (uint)StatusKind.DataAvailableStatus);

            if (genericReader != null)
            {
                _robotReader = new RobotStateDataReader(genericReader);
                LogInfo("<color=green>SUCCESS: Subscriber Initialized. Waiting for Publisher...</color>");
            }
            else
            {
                LogError("CRITICAL: Failed to create DataReader.");
            }
        }
        catch (Exception e)
        {
            LogError($"EXCEPTION during Init: {e.Message}\n{e.StackTrace}");
        }
    }

    public void OnDataReceived(RobotState msg)
    {
        lock (_dataLock)
        {
            _latestState = msg;
            _hasNewData = true;
        }
    }

    void Update()
    {
        lock (_dataLock)
        {
            if (_hasNewData && _latestState != null)
            {
                ApplyRobotState(_latestState);
                _hasNewData = false;
            }
        }
    }

    // --- FIX: UPDATED COORDINATE LOGIC ---
    void ApplyRobotState(RobotState msg)
    {
        // 1. Position Conversion (mm to meters)
        float x = (float)msg.X / 1000.0f;
        float y = (float)msg.Y / 1000.0f;
        float z = (float)msg.Z / 1000.0f;

        // 2. Rotation Conversion (Fanuc WPR to Unity)
        float w = (float)msg.W;
        float p = (float)msg.P;
        float r = (float)msg.R;

        if (RobotRoot != null)
        {
            // Position: Negate X for Left-Handed Coordinate System
            RobotRoot.localPosition = new Vector3(-x, y, z);

            // Rotation: Calculate Quaternion, get Eulers, then flip Y and Z axes
            Vector3 tempEuler = CreateQuaternionFromFanucWPR(w, p, r).eulerAngles;
            RobotRoot.localEulerAngles = new Vector3(tempEuler.x, -tempEuler.y, -tempEuler.z);
        }

        // 3. Joint Application
        if (Joints != null && Joints.Length >= 6)
        {
            SetJoint(0, (float)msg.J1);
            SetJoint(1, (float)msg.J2);

            // FIX: Fanuc/Parallel linkage robots usually require J3 + J2
            SetJoint(2, (float)msg.J3 + (float)msg.J2);

            SetJoint(3, (float)msg.J4);
            SetJoint(4, (float)msg.J5);
            SetJoint(5, (float)msg.J6);
        }
    }

    // --- FIX: ADDED MATH HELPER ---
    // Converts Fanuc Yaw-Pitch-Roll (WPR) to a Quaternion
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

    void SetJoint(int index, float angle)
    {
        if (index < Joints.Length && Joints[index] != null)
        {
            var drive = Joints[index].xDrive;
            drive.target = angle;
            Joints[index].xDrive = drive;
        }
    }

    // --- Helper Logging Methods ---
    public void LogInfo(string msg)
    {
        if (VerboseLogging) Debug.Log($"[DDS-Sub] {msg}");
    }

    public void LogError(string msg)
    {
        Debug.LogError($"[DDS-Sub] {msg}");
    }

    // --- CLEANUP ---
    void OnApplicationQuit()
    {
        LogInfo("Cleaning up DDS Entities...");

        if (_participant != null)
        {
            _participant.DeleteContainedEntities();
            if (_dpf != null)
            {
                _dpf.DeleteParticipant(_participant);
            }
        }

#if UNITY_EDITOR
        LogInfo("Editor Mode: Keeping ACE/Factory alive.");
#else
        LogInfo("Build Mode: Shutting down ACE.");
        ParticipantService.Instance.Shutdown();
        Ace.Fini();
#endif
    }

    // --- LISTENER CLASS ---
    private class RobotStateListener : DataReaderListener
    {
        private UnityRobotSubscriber _parent;

        public RobotStateListener(UnityRobotSubscriber parent)
        {
            _parent = parent;
        }

        protected override void OnDataAvailable(DataReader reader)
        {
            RobotStateDataReader robotReader = new RobotStateDataReader(reader);
            List<RobotState> msgList = new List<RobotState>();
            List<SampleInfo> infoList = new List<SampleInfo>();

            ReturnCode ret = robotReader.Take(
                msgList,
                infoList,
                10,
                (SampleStateKind)0xFFFF,
                (ViewStateKind)0xFFFF,
                (InstanceStateKind)0xFFFF
            );

            if (ret == ReturnCode.Ok)
            {
                for (int i = 0; i < msgList.Count; i++)
                {
                    if (infoList[i].ValidData)
                    {
                        _parent.OnDataReceived(msgList[i]);
                    }
                }
            }
        }

        protected override void OnSubscriptionMatched(DataReader reader, SubscriptionMatchedStatus status)
        {
            if (status.CurrentCountChange > 0)
                _parent.LogInfo($"<color=cyan>Publisher FOUND! (Count: {status.TotalCount})</color>");
            else if (status.CurrentCountChange < 0)
                _parent.LogInfo($"<color=orange>Publisher LOST. (Count: {status.TotalCount})</color>");
        }

        protected override void OnLivelinessChanged(DataReader reader, LivelinessChangedStatus status) { }
        protected override void OnRequestedDeadlineMissed(DataReader reader, RequestedDeadlineMissedStatus status) { }
        protected override void OnRequestedIncompatibleQos(DataReader reader, RequestedIncompatibleQosStatus status)
        {
            _parent.LogError("Incompatible QoS detected!");
        }
        protected override void OnSampleLost(DataReader reader, SampleLostStatus status) { }
        protected override void OnSampleRejected(DataReader reader, SampleRejectedStatus status) { }
    }
}