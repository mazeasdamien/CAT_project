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
    [SerializeField] private string topicName = "OperatorNewPose_Topic";

    [Tooltip("The name of the struct type in DDS.")]
    [SerializeField] private string typeName = "OperatorNewPose";

    [Header("Teleoperation Settings")]
    [Tooltip("Speed value to publish (0-100%).")]
    [SerializeField] private float speed = 100.0f;

    [Tooltip("Scale factor for position (e.g. Unity meters to millimeters).")]
    [SerializeField] private float positionScale = 1000.0f;

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
        // Using a small epsilon for float comparison to avoid noise
        bool positionChanged = Vector3.SqrMagnitude(_transform.localPosition - _lastPosition) > 0.0001f;
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

    /// <summary>
    /// Populates and writes the DDS sample.
    /// </summary>
    private void PublishPose()
    {
        try
        {
            // Position conversion (Unity -> Fanuc)
            // Based on user requirements: X inverted, others scaled
            _sample.SetValue("X", -_transform.localPosition.x * positionScale);
            _sample.SetValue("Y", _transform.localPosition.y * positionScale);
            _sample.SetValue("Z", _transform.localPosition.z * positionScale);

            // Rotation conversion (Quaternion -> Fanuc WPR)
            Vector3 wpr = CreateFanucWPRFromQuaternion(_transform.localRotation);
            _sample.SetValue("W", wpr.x);
            _sample.SetValue("P", wpr.y);
            _sample.SetValue("R", wpr.z);

            // Speed
            _sample.SetValue("Speed", speed);

            // Publish
            _writer.Write(_sample);
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
        // Note: Unity's eulerAngles might not match the specific sequence Fanuc expects, so we use the provided math.

        float W = Mathf.Atan2(2 * ((q.w * q.x) + (q.y * q.z)), 1 - 2 * (Mathf.Pow(q.x, 2) + Mathf.Pow(q.y, 2))) * (180 / Mathf.PI);
        float P = Mathf.Asin(2 * ((q.w * q.y) - (q.z * q.x))) * (180 / Mathf.PI);
        float R = Mathf.Atan2(2 * ((q.w * q.z) + (q.x * q.y)), 1 - 2 * (Mathf.Pow(q.y, 2) + Mathf.Pow(q.z, 2))) * (180 / Mathf.PI);

        // User specified return format: W, -P, -R
        return new Vector3(W, -P, -R);
    }
}
