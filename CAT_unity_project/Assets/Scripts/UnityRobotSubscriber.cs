using System;
using UnityEngine;
using OpenDDSharp;
using OpenDDSharp.DDS;
using OpenDDSharp.OpenDDS.DCPS;
using RobotDDS;
using System.Collections.Generic;
using System.Collections;

public class UnityRobotSubscriber : MonoBehaviour
{
    [Header("DDS Configuration")]
    public string TopicName = "RobotState_Topic";

    [Header("Robot Visuals")]
    public Transform FanucRobotTCP;
    public ArticulationBody[] Joints;

    [Header("Debug")]
    public bool VerboseLogging = true;

    private RobotStateDataReader _robotReader;
    private RobotStateListener _listener;
    private Subscriber _subscriber;
    private Topic _topic;

    // Thread-safe Data Exchange
    private object _dataLock = new object();
    private RobotState _latestState = null;
    private bool _hasNewData = false;

    IEnumerator Start()
    {
        // Wait for DDSManager to be ready
        while (DDSManager.Instance == null || !DDSManager.Instance.IsInitialized)
        {
            yield return null;
        }

        InitializeSubscriber();
    }

    void InitializeSubscriber()
    {
        try
        {
            DomainParticipant participant = DDSManager.Instance.Participant;
            if (participant == null)
            {
                LogError("Participant is null in DDSManager.");
                return;
            }

            LogInfo("--- Initializing Subscriber ---");

            // 1. Register Type
            RobotStateTypeSupport support = new RobotStateTypeSupport();
            ReturnCode ret = support.RegisterType(participant, support.GetTypeName());
            if (ret != ReturnCode.Ok)
            {
                // It might be already registered. Log warning but proceed.
                LogInfo($"RegisterType returned {ret}. Assuming type might already be registered.");
            }

            // 2. Create Topic
            _topic = participant.CreateTopic(TopicName, support.GetTypeName());
            if (_topic == null)
            {
                // If CreateTopic fails, it might already exist. 
                // However, FindTopic requires a Duration which is causing build issues.
                LogError($"CRITICAL: Failed to create Topic '{TopicName}'.");
                return;
            }

            // 3. Create Subscriber
            _subscriber = participant.CreateSubscriber();
            if (_subscriber == null)
            {
                LogError("CRITICAL: Failed to create Subscriber.");
                return;
            }

            // 4. QoS Setup
            DataReaderQos readerQos = new DataReaderQos();
            _subscriber.GetDefaultDataReaderQos(readerQos);

            // Use settings from DDSManager if available
            bool useReliable = true;
            bool useTransientLocal = true;
            int historyDepth = 1;

            if (DDSManager.Instance != null && DDSManager.Instance.Settings != null)
            {
                useReliable = DDSManager.Instance.Settings.UseReliable;
                useTransientLocal = DDSManager.Instance.Settings.UseTransientLocal;
                historyDepth = DDSManager.Instance.Settings.HistoryDepth;
            }

            readerQos.Reliability.Kind = useReliable ? ReliabilityQosPolicyKind.ReliableReliabilityQos : ReliabilityQosPolicyKind.BestEffortReliabilityQos;
            readerQos.Durability.Kind = useTransientLocal ? DurabilityQosPolicyKind.TransientLocalDurabilityQos : DurabilityQosPolicyKind.VolatileDurabilityQos;
            readerQos.History.Kind = HistoryQosPolicyKind.KeepLastHistoryQos;
            readerQos.History.Depth = historyDepth;

            // 5. Create Reader & Listener
            _listener = new RobotStateListener(this);
            DataReader genericReader = _subscriber.CreateDataReader(_topic, readerQos, _listener, (uint)StatusKind.DataAvailableStatus);

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

    void ApplyRobotState(RobotState msg)
    {
        if (FanucRobotTCP != null)
        {
            // Position Logic: Negate X for Left-Handed System
            float unityX = (float)msg.X / 1000f;
            float unityY = (float)msg.Y / 1000f;
            float unityZ = (float)msg.Z / 1000f;

            FanucRobotTCP.localPosition = new Vector3(-unityX, unityY, unityZ);

            // Rotation Logic
            Vector3 eulerAngles = CreateQuaternionFromFanucWPR((float)msg.W, (float)msg.P, (float)msg.R).eulerAngles;
            FanucRobotTCP.localEulerAngles = new Vector3(eulerAngles.x, -eulerAngles.y, -eulerAngles.z);
        }

        if (Joints != null && Joints.Length >= 6)
        {
            SetJoint(0, (float)msg.J1);
            SetJoint(1, (float)msg.J2);
            // Fanuc/Parallel linkage robots usually require J3 + J2
            SetJoint(2, (float)msg.J3 + (float)msg.J2);
            SetJoint(3, (float)msg.J4);
            SetJoint(4, (float)msg.J5);
            SetJoint(5, (float)msg.J6);
        }
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
    void OnDestroy()
    {
        CleanupSubscriber();
    }

    void CleanupSubscriber()
    {
        if (_subscriber != null)
        {
            _subscriber.DeleteContainedEntities();

            if (DDSManager.Instance != null && DDSManager.Instance.Participant != null)
            {
                try
                {
                    DDSManager.Instance.Participant.DeleteSubscriber(_subscriber);
                }
                catch { }
            }
            _subscriber = null;
        }
        _robotReader = null;
        _listener = null;
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