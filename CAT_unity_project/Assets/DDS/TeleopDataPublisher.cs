using Rti.Dds.Publication;
using Rti.Types.Dynamic;
using UnityEngine;

/// <summary>
/// Manages teleoperation by publishing the transform's position, rotation (as Fanuc WPR), and speed to a DDS topic.
/// This script monitors the GameObject's transform and publishes updates whenever the pose or speed changes.
/// </summary>
public class TeleopDataPublisher : MonoBehaviour
{
    [Header("DDS Configuration")]
    [Tooltip("The name of the DDS topic to publish to.")]
    [SerializeField] private string topicName = "TeleopData_Topic";

    [Tooltip("The name of the struct type in DDS.")]
    [SerializeField] private string typeName = "TeleopData";

    [Header("Teleoperation Settings")]
    [Tooltip("Speed value to publish (0-100%).")]
    [Range(0, 100)]
    [SerializeField] private float speed = 10.0f;

    // DDS Entities
    private DataWriter<DynamicData> _writer;
    private DynamicData _sample;
    private bool _isInitialized = false;

    // State Tracking
    private Transform _transform;
    private Vector3 _lastPosition;
    private Quaternion _lastRotation;
    private float _lastSpeed;

    /// <summary>
    /// Initialization.
    /// </summary>
    private void Start()
    {
        _transform = transform;

        // Initialize state trackers to ensure first update is sent
        _lastPosition = Vector3.negativeInfinity;
        _lastRotation = Quaternion.identity;
        _lastSpeed = -1f;

        InitializeDDS();
    }

    /// <summary>
    /// Sets up the DDS DataWriter and defines the data type.
    /// </summary>
    private void InitializeDDS()
    {
        var ddsHandler = DDSHandler.Instance;
        if (ddsHandler == null)
        {
            Debug.LogError("TeleopDataPublisher: DDSHandler instance not found. Ensure DDSHandler is present in the scene.");
            return;
        }

        try
        {
            // Define the struct type dynamically
            // We add "Speed" to the user's original structure
            var typeFactory = DynamicTypeFactory.Instance;
            var operatorPoseType = typeFactory.BuildStruct()
                .WithName(typeName)
                .AddMember(new StructMember("Id", typeFactory.CreateString(128)))
                .AddMember(new StructMember("Timestamp", typeFactory.GetPrimitiveType<double>()))
                .AddMember(new StructMember("X", typeFactory.GetPrimitiveType<float>()))
                .AddMember(new StructMember("Y", typeFactory.GetPrimitiveType<float>()))
                .AddMember(new StructMember("Z", typeFactory.GetPrimitiveType<float>()))
                .AddMember(new StructMember("W", typeFactory.GetPrimitiveType<float>()))
                .AddMember(new StructMember("P", typeFactory.GetPrimitiveType<float>()))
                .AddMember(new StructMember("R", typeFactory.GetPrimitiveType<float>()))
                .AddMember(new StructMember("Speed", typeFactory.GetPrimitiveType<float>()))
                .Create();

            // Setup the writer using the centralized DDSHandler
            _writer = ddsHandler.SetupDataWriter(topicName, operatorPoseType);

            if (_writer != null)
            {
                _sample = new DynamicData(operatorPoseType);
                _isInitialized = true;
                Debug.Log($"TeleopDataPublisher: Successfully initialized writer for topic '{topicName}'.");
            }
            else
            {
                Debug.LogError($"TeleopDataPublisher: Failed to create DataWriter for topic '{topicName}'.");
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"TeleopDataPublisher: Exception during initialization: {ex.Message}");
        }
    }

    /// <summary>
    /// Checks for changes and publishes updates.
    /// </summary>
    private void Update()
    {
        if (!_isInitialized) return;

        // Check if position, rotation, or speed has changed
        // Using a smaller epsilon to detect fine movements
        bool positionChanged = Vector3.SqrMagnitude(_transform.localPosition - _lastPosition) > 1e-7f;
        bool rotationChanged = Quaternion.Angle(_transform.localRotation, _lastRotation) > 0.01f;
        bool speedChanged = !Mathf.Approximately(speed, _lastSpeed);

        if (positionChanged || rotationChanged || speedChanged)
        {
            PublishPose();

            // Update tracked state
            _lastPosition = _transform.localPosition;
            _lastRotation = _transform.localRotation;
            _lastSpeed = speed;
        }
    }

    private int _sequenceId = 0;

    /// <summary>
    /// Populates and writes the DDS sample.
    /// </summary>
    private void PublishPose()
    {
        try
        {
            _sequenceId++;
            _sample.SetValue("Id", _sequenceId.ToString());
            _sample.SetValue("Timestamp", (System.DateTime.UtcNow - new System.DateTime(1970, 1, 1)).TotalSeconds);

            // Position conversion (Unity -> Fanuc)
            _sample.SetValue("X", -_transform.localPosition.x * 1000);
            _sample.SetValue("Y", _transform.localPosition.y * 1000);
            _sample.SetValue("Z", _transform.localPosition.z * 1000);

            // Rotation conversion (Quaternion -> Fanuc WPR)
            Vector3 wpr = CreateFanucWPRFromQuaternion(_transform.localRotation);
            _sample.SetValue("W", wpr.x);
            _sample.SetValue("P", wpr.y);
            _sample.SetValue("R", wpr.z);

            // Speed
            _sample.SetValue("Speed", speed);

            // Publish
            _writer.Write(_sample);

            // Debug Log
            Debug.Log($"[TeleopPublisher] Sent: Id={_sequenceId}, X={-_transform.localPosition.x * 1000:F2}, Y={_transform.localPosition.y * 1000:F2}, Z={_transform.localPosition.z * 1000:F2}, Speed={speed:F1}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"TeleopDataPublisher: Error publishing sample: {ex.Message}");
        }
    }

    /// <summary>
    /// Converts a Unity Quaternion to Fanuc World Position Representation (WPR).
    /// </summary>
    /// <param name="q">Unity Quaternion</param>
    /// <returns>Vector3 containing (W, P, R)</returns>
    private Vector3 CreateFanucWPRFromQuaternion(Quaternion q)
    {
        // Calculate Euler angles manually to match Fanuc convention
        float W = Mathf.Atan2(2 * ((q.w * q.x) + (q.y * q.z)), 1 - 2 * (Mathf.Pow(q.x, 2) + Mathf.Pow(q.y, 2))) * (180 / Mathf.PI);
        float P = Mathf.Asin(2 * ((q.w * q.y) - (q.z * q.x))) * (180 / Mathf.PI);
        float R = Mathf.Atan2(2 * ((q.w * q.z) + (q.x * q.y)), 1 - 2 * (Mathf.Pow(q.y, 2) + Mathf.Pow(q.z, 2))) * (180 / Mathf.PI);

        return new Vector3(W, -P, -R);
    }
}
