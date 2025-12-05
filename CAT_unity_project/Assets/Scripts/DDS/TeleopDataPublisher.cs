using System;
using UnityEngine;
using OpenDDSharp;
using OpenDDSharp.DDS;
using OpenDDSharp.OpenDDS.DCPS;
using RobotDDS;
using System.Collections.Generic;
using System.Collections;

public class TeleopDataPublisher : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Reference to the IK Solver script to get joint angles.")]
    public FanucIK robotIK;

    [Header("DDS Configuration")]
    [Tooltip("The name of the DDS topic to publish to.")]
    [SerializeField] private string topicName = "Teleop_Topic";

    [Tooltip("The name of the type in DDS.")]
    [SerializeField] private string typeName = "TeleopData";

    [Tooltip("Rate at which to publish data in Hz.")]
    [SerializeField] private float publishRateHz = 10f;

    private TeleopDataDataWriter _writer;
    private Topic _topic;
    private Publisher _publisher;
    private TeleopDataTypeSupport _typeSupport;

    private bool _isInitialized = false;

    private float _nextPublishTime = 0f;
    private int _sequenceId = 0;
    private double[] _lastJoints = new double[6];
    private bool _hasPublishedOnce = false;
    private const double JointTolerance = 0.01; // Degrees

    private void Start()
    {
        if (robotIK == null)
        {
            robotIK = FindFirstObjectByType<FanucIK>();
            if (robotIK == null)
            {
                Debug.LogError("TeleopDataPublisher: FanucIK script not found! Please assign it in the Inspector.");
            }
        }

        Debug.Log($"TeleopDataPublisher: Publishing to topic '{topicName}'");

        // Start initialization coroutine to wait for DDSManager
        StartCoroutine(InitializeDDS());
    }

    private System.Collections.IEnumerator InitializeDDS()
    {
        // Wait until DDSManager is ready
        while (DDSManager.Instance == null || !DDSManager.Instance.IsInitialized)
        {
            yield return null;
        }

        var participant = DDSManager.Instance.Participant;
        if (participant == null)
        {
            Debug.LogError("TeleopDataPublisher: DomainParticipant is null.");
            yield break;
        }

        try
        {
            // 1. Create Publisher
            _publisher = participant.CreatePublisher();
            if (_publisher == null)
            {
                Debug.LogError("TeleopDataPublisher: Failed to create Publisher.");
                yield break;
            }

            // 2. Register Type
            _typeSupport = new TeleopDataTypeSupport();
            if (_typeSupport.RegisterType(participant, typeName) != ReturnCode.Ok)
            {
                Debug.LogError($"TeleopDataPublisher: Failed to register type '{typeName}'.");
                yield break;
            }

            // 3. Create Topic
            _topic = participant.CreateTopic(topicName, typeName);
            if (_topic == null)
            {
                Debug.LogError($"TeleopDataPublisher: Failed to create Topic '{topicName}'.");
                yield break;
            }

            // 4. QoS Setup
            DataWriterQos writerQos = new();
            _publisher.GetDefaultDataWriterQos(writerQos);

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

            writerQos.Reliability.Kind = useReliable ? ReliabilityQosPolicyKind.ReliableReliabilityQos : ReliabilityQosPolicyKind.BestEffortReliabilityQos;
            writerQos.Durability.Kind = useTransientLocal ? DurabilityQosPolicyKind.TransientLocalDurabilityQos : DurabilityQosPolicyKind.VolatileDurabilityQos;
            writerQos.History.Kind = HistoryQosPolicyKind.KeepLastHistoryQos;
            writerQos.History.Depth = historyDepth;

            // 5. Create DataWriter
            DataWriter baseWriter = _publisher.CreateDataWriter(_topic, writerQos);
            if (baseWriter == null)
            {
                Debug.LogError($"TeleopDataPublisher: Failed to create DataWriter for topic '{topicName}'.");
                yield break;
            }

            // 5. Narrow to specific writer
            _writer = new TeleopDataDataWriter(baseWriter);

            _isInitialized = true;
            Debug.Log($"TeleopDataPublisher: Successfully initialized writer for topic '{topicName}'.");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"TeleopDataPublisher: Exception during initialization: {ex.Message}\n{ex.StackTrace}");
        }
    }

    private void Update()
    {
        if (!_isInitialized || robotIK == null) return;

        if (Time.time < _nextPublishTime) return;

        PublishJoints();
        _nextPublishTime = Time.time + (1f / publishRateHz);
    }

    private void PublishJoints()
    {
        try
        {
            if (robotIK.joints == null || robotIK.joints.Count < 6)
            {
                // Only log once or throttle this warning to avoid spam
                // Debug.LogWarning("TeleopDataPublisher: Robot joints not fully assigned in IK script.");
                return;
            }



            // Extract Joint Angles from ArticulationBodies (in Radians, convert to Degrees)
            // Note: ArticulationBody.jointPosition returns radians
            // We assume robotIK.joints is a List<ArticulationBody> or similar structure with jointPosition

            // Safety check for array bounds
            if (robotIK.joints[0].jointPosition.dofCount == 0) return;

            double j1 = robotIK.joints[0].jointPosition[0] * Mathf.Rad2Deg;
            double j2 = robotIK.joints[1].jointPosition[0] * Mathf.Rad2Deg;
            double j3 = robotIK.joints[2].jointPosition[0] * Mathf.Rad2Deg;
            double j4 = robotIK.joints[3].jointPosition[0] * Mathf.Rad2Deg;
            double j5 = robotIK.joints[4].jointPosition[0] * Mathf.Rad2Deg;
            double j6 = robotIK.joints[5].jointPosition[0] * Mathf.Rad2Deg;

            // Apply Fanuc Coupling Correction for J3
            // Fanuc J3 = Unity J3 - Unity J2
            j3 -= j2;

            // Check if joints have changed
            bool hasChanged = !_hasPublishedOnce;
            if (!hasChanged)
            {
                if (Math.Abs(j1 - _lastJoints[0]) > JointTolerance ||
                    Math.Abs(j2 - _lastJoints[1]) > JointTolerance ||
                    Math.Abs(j3 - _lastJoints[2]) > JointTolerance ||
                    Math.Abs(j4 - _lastJoints[3]) > JointTolerance ||
                    Math.Abs(j5 - _lastJoints[4]) > JointTolerance ||
                    Math.Abs(j6 - _lastJoints[5]) > JointTolerance)
                {
                    hasChanged = true;
                }
            }

            if (!hasChanged) return;

            // Update last joints
            _lastJoints[0] = j1;
            _lastJoints[1] = j2;
            _lastJoints[2] = j3;
            _lastJoints[3] = j4;
            _lastJoints[4] = j5;
            _lastJoints[5] = j6;
            _hasPublishedOnce = true;
            _sequenceId++;

            // Create Data Sample
            TeleopData sample = new()
            {
                Clock = DateTime.Now.ToString("HH:mm:ss.fff"),
                SampleId = _sequenceId,
                J1 = j1,
                J2 = j2,
                J3 = j3,
                J4 = j4,
                J5 = j5,
                J6 = j6
            };

            // Write Data
            ReturnCode ret = _writer.Write(sample);
            if (ret != ReturnCode.Ok)
            {
                Debug.LogWarning($"TeleopDataPublisher: Failed to write sample. ReturnCode: {ret}");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"TeleopDataPublisher: Error publishing sample: {ex.Message}");
        }
    }

    private void OnDestroy()
    {
        // Clean up entities
        if (_publisher != null && _writer != null)
        {
            _publisher.DeleteDataWriter(_writer);
        }
        if (DDSManager.Instance != null && DDSManager.Instance.Participant != null)
        {
            if (_publisher != null)
            {
                DDSManager.Instance.Participant.DeletePublisher(_publisher);
            }
            if (_topic != null)
            {
                DDSManager.Instance.Participant.DeleteTopic(_topic);
            }
            // Unregister type? Usually handled by participant cleanup or explicit unregister
        }
    }
}
