using Rti.Dds.Publication;
using Rti.Types.Dynamic;
using UnityEngine;

public class TeleopDataPublisher : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Reference to the IK Solver script to get joint angles.")]
    public FanucJacobianIK robotIK;

    [Header("DDS Configuration")]
    [Tooltip("The name of the DDS topic to publish to.")]
    [SerializeField] private string topicName = "OperatorNewPose_Topic";

    [Tooltip("The name of the struct type in DDS.")]
    [SerializeField] private string typeName = "OperatorNewPose";

    private DataWriter<DynamicData> _writer;
    private DynamicData _sample;
    private bool _isInitialized = false;

    private Transform _transform;
    private Vector3 _lastPosition;
    private Quaternion _lastRotation;

    private float _publishRateHz = 10f;
    private float _nextPublishTime = 0f;

    private void Start()
    {
        _transform = transform;

        _lastPosition = Vector3.negativeInfinity;
        _lastRotation = Quaternion.identity;

        if (robotIK == null)
        {
            robotIK = FindObjectOfType<FanucJacobianIK>();
            if (robotIK == null)
            {
                Debug.LogError("TeleopDataPublisher: FanucJacobianIK script not found! Please assign it in the Inspector.");
            }
        }

        InitializeDDS();
    }

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
            var typeFactory = DynamicTypeFactory.Instance;

            // Define struct with J1-J6 instead of XYZWPR
            var operatorPoseType = typeFactory.BuildStruct()
                .WithName(typeName)
                .AddMember(new StructMember("J1", typeFactory.GetPrimitiveType<double>()))
                .AddMember(new StructMember("J2", typeFactory.GetPrimitiveType<double>()))
                .AddMember(new StructMember("J3", typeFactory.GetPrimitiveType<double>()))
                .AddMember(new StructMember("J4", typeFactory.GetPrimitiveType<double>()))
                .AddMember(new StructMember("J5", typeFactory.GetPrimitiveType<double>()))
                .AddMember(new StructMember("J6", typeFactory.GetPrimitiveType<double>()))
                .AddMember(new StructMember("Samples", typeFactory.GetPrimitiveType<int>()))
                .Create();

            _writer = ddsHandler.SetupDataWriter(topicName, operatorPoseType);

            if (_writer != null)
            {
                _sample = new DynamicData(operatorPoseType);
                _isInitialized = true;
                Debug.Log($"TeleopDataPublisher: Successfully initialized writer for topic '{topicName}' with Joint Data.");
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

    private void Update()
    {
        if (!_isInitialized || robotIK == null) return;

        if (Time.time < _nextPublishTime) return;

        // Check if the target transform has moved, which would cause IK to update joints
        bool positionChanged = Vector3.SqrMagnitude(_transform.localPosition - _lastPosition) > 0.0001f;
        bool rotationChanged = Quaternion.Angle(_transform.localRotation, _lastRotation) > 0.01f;

        if (positionChanged || rotationChanged)
        {
            PublishJoints();
            _lastPosition = _transform.localPosition;
            _lastRotation = _transform.localRotation;
            _nextPublishTime = Time.time + (1f / _publishRateHz);
        }
    }

    private int _sequenceId = 0;

    private void PublishJoints()
    {
        try
        {
            if (robotIK.joints == null || robotIK.joints.Count < 6)
            {
                Debug.LogWarning("TeleopDataPublisher: Robot joints not fully assigned in IK script.");
                return;
            }

            _sequenceId++;

            // Extract Joint Angles from ArticulationBodies (in Radians, convert to Degrees)
            // Note: ArticulationBody.jointPosition returns radians
            double j1 = robotIK.joints[0].jointPosition[0] * Mathf.Rad2Deg;
            double j2 = robotIK.joints[1].jointPosition[0] * Mathf.Rad2Deg;
            double j3 = robotIK.joints[2].jointPosition[0] * Mathf.Rad2Deg;
            double j4 = robotIK.joints[3].jointPosition[0] * Mathf.Rad2Deg;
            double j5 = robotIK.joints[4].jointPosition[0] * Mathf.Rad2Deg;
            double j6 = robotIK.joints[5].jointPosition[0] * Mathf.Rad2Deg;

            // Apply Fanuc Coupling Correction for J3
            // Fanuc J3 = Unity J3 - Unity J2
            j3 = j3 - j2;

            _sample.SetValue("J1", j1);
            _sample.SetValue("J2", j2);
            _sample.SetValue("J3", j3);
            _sample.SetValue("J4", j4);
            _sample.SetValue("J5", j5);
            _sample.SetValue("J6", j6);
            _sample.SetValue("Samples", _sequenceId);

            _writer.Write(_sample);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"TeleopDataPublisher: Error publishing sample: {ex.Message}");
        }
    }
    /*
        private Vector3 CreateFanucWPRFromQuaternion(Quaternion q)
        {
            float W = Mathf.Atan2(2 * ((q.w * q.x) + (q.y * q.z)), 1 - 2 * (Mathf.Pow(q.x, 2) + Mathf.Pow(q.y, 2))) * (180 / Mathf.PI);
            float P = Mathf.Asin(2 * ((q.w * q.y) - (q.z * q.x))) * (180 / Mathf.PI);
            float R = Mathf.Atan2(2 * ((q.w * q.z) + (q.x * q.y)), 1 - 2 * (Mathf.Pow(q.y, 2) + Mathf.Pow(q.z, 2))) * (180 / Mathf.PI);
            return new Vector3(W, -P, -R);
        }
    */
}
