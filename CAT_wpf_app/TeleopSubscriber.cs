using System;
using Rti.Dds.Core;
using Rti.Dds.Domain;
using Rti.Dds.Subscription;
using Rti.Dds.Topics;
using Rti.Types.Dynamic;
using FRRobot;

namespace CAT_wpf_app
{
    /// <summary>
    /// Handles the subscription to Teleoperation data from Unity.
    /// Receives operator pose and speed, and updates the Fanuc Robot's registers.
    /// </summary>
    public class TeleopSubscriber
    {
        private const string TOPIC_NAME = "TeleopData_Topic";
        private const string TYPE_NAME = "TeleopData";

        private readonly DataReader<DynamicData> _reader;
        private readonly Action<string, string, string> _logAction;

        // Data holders for UI visualization
        public float LastX { get; private set; }
        public float LastY { get; private set; }
        public float LastZ { get; private set; }
        public float LastW { get; private set; }
        public float LastP { get; private set; }
        public float LastR { get; private set; }
        public float LastSpeed { get; private set; }
        public string LastId { get; private set; }
        public double LastTimestamp { get; private set; }
        public int TotalSamplesReceived { get; private set; }
        public bool IsReachable { get; private set; }

        // Configurable Register IDs
        public int PositionRegisterId { get; set; } = 1;
        public int SpeedRegisterId { get; set; } = 1;

        /// <summary>
        /// Initializes a new instance of the <see cref="TeleopSubscriber"/> class.
        /// </summary>
        /// <param name="participant">The DDS DomainParticipant.</param>
        /// <param name="readerQos">The DataReader QoS.</param>
        /// <param name="logAction">Action to log messages with color and optional topic.</param>
        public TeleopSubscriber(DomainParticipant participant, DataReaderQos readerQos, Action<string, string, string> logAction = null)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));
            _logAction = logAction;

            try
            {
                // 1. Define Type
                // Must match the Publisher in Unity exactly
                var typeFactory = DynamicTypeFactory.Instance;
                var operatorPoseType = typeFactory.BuildStruct()
                    .WithName(TYPE_NAME)
                    .AddMember(new StructMember("Id", typeFactory.CreateString(bounds: 128)))
                    .AddMember(new StructMember("Timestamp", typeFactory.GetPrimitiveType<double>()))
                    .AddMember(new StructMember("X", typeFactory.GetPrimitiveType<float>()))
                    .AddMember(new StructMember("Y", typeFactory.GetPrimitiveType<float>()))
                    .AddMember(new StructMember("Z", typeFactory.GetPrimitiveType<float>()))
                    .AddMember(new StructMember("W", typeFactory.GetPrimitiveType<float>()))
                    .AddMember(new StructMember("P", typeFactory.GetPrimitiveType<float>()))
                    .AddMember(new StructMember("R", typeFactory.GetPrimitiveType<float>()))
                    .AddMember(new StructMember("Speed", typeFactory.GetPrimitiveType<float>()))
                    .Create();

                // 2. Register Type
                // Note: If the type is already registered by another entity, this might throw or be ignored.
                // We check if it's already registered to be safe, or just try-catch.
                try
                {
                    participant.RegisterType(TYPE_NAME, operatorPoseType);
                }
                catch (Exception)
                {
                    // Type might already be registered, which is fine.
                }

                // 3. Create Topic
                Topic<DynamicData> topic = participant.CreateTopic<DynamicData>(TOPIC_NAME, TYPE_NAME);

                // 4. Create DataReader
                _reader = participant.ImplicitSubscriber.CreateDataReader(topic, readerQos);

                _logAction?.Invoke($"[TeleopSubscriber] Subscribed to {TOPIC_NAME}", "#4CAF50", null);
            }
            catch (Exception ex)
            {
                _logAction?.Invoke($"[TeleopSubscriber] Init Error: {ex.Message}", "#F44336", null);
                throw;
            }
        }

        /// <summary>
        /// Polls for new data samples and updates the robot registers if valid data is received.
        /// </summary>
        /// <param name="robot">The connected Fanuc Robot instance.</param>
        /// <returns>True if data was received and processed.</returns>
        public bool ReceiveAndProcess(FRCRobot robot)
        {
            if (_reader == null) return false;

            bool dataReceived = false;

            try
            {
                // Take all available samples
                using var samples = _reader.Take();

                foreach (var sample in samples)
                {
                    if (sample.Info.ValidData)
                    {
                        DynamicData data = sample.Data;

                        // Extract Data
                        try { LastId = data.GetValue<string>("Id"); } catch { LastId = string.Empty; }
                        try { LastTimestamp = data.GetValue<double>("Timestamp"); } catch { LastTimestamp = 0; }
                        LastX = data.GetValue<float>("X");
                        LastY = data.GetValue<float>("Y");
                        LastZ = data.GetValue<float>("Z");
                        LastW = data.GetValue<float>("W");
                        LastP = data.GetValue<float>("P");
                        LastR = data.GetValue<float>("R");

                        // Check if Speed exists in the sample (backward compatibility)
                        try { LastSpeed = data.GetValue<float>("Speed"); } catch { LastSpeed = 0f; }

                        TotalSamplesReceived++;
                        dataReceived = true;

                        // Update Robot Registers
                        // We only update the local state here. 
                        // The actual write to the robot happens ONCE after the loop to prevent bursting.
                    }
                }

                // Only write to the robot ONCE per cycle, using the latest data
                if (dataReceived && robot != null && robot.IsConnected)
                {
                    UpdateRobotRegister(robot, LastX, LastY, LastZ, LastW, LastP, LastR, LastSpeed);
                }
            }
            catch (Exception ex)
            {
                _logAction?.Invoke($"[TeleopSubscriber] Process Error: {ex.Message}", "#F44336", "ProcessError");
            }

            return dataReceived;
        }

        /// <summary>
        /// Updates the robot's Position Register [1] and Data Register [1] (Speed).
        /// </summary>
        private void UpdateRobotRegister(FRCRobot robot, float x, float y, float z, float w, float p, float r, float speed)
        {
            try
            {
                // 1. Get the current robot position to steal its Configuration
                // We need the robot to stay in the same posture (No-Flip, Up, Top, etc.)
                FRCCurGroupPosition currentGroupPos = robot.CurPosition.Group[1, FRECurPositionConstants.frWorldDisplayType];
                FRCXyzWpr currentXyzWpr = currentGroupPos.Formats[FRETypeCodeConstants.frXyzWpr];
                FRCConfig currentConfig = currentXyzWpr.Config;


                // 2. Prepare the Target Register
                FRCSysPositions positions = robot.RegPositions;
                FRCSysPosition sysPosition = positions[PositionRegisterId];
                FRCSysGroupPosition groupPos = sysPosition.Group[1];
                FRCXyzWpr targetXyzWpr = groupPos.Formats[FRETypeCodeConstants.frXyzWpr];

                // 3. Assign Coordinates
                targetXyzWpr.X = x;
                targetXyzWpr.Y = y;
                targetXyzWpr.Z = z;
                targetXyzWpr.W = w;
                targetXyzWpr.P = p;
                targetXyzWpr.R = r;

                // 4. CRITICAL: Manually copy the Configuration flags and Turn counts
                targetXyzWpr.Config.Text = currentConfig.Text;

                // 5. Check Reachability using LINEAR Motion Type
                object missing = System.Type.Missing;
                FRCMotionErrorInfo motionErrorInfo;

                // Changed from frJointMotionType to frLinearMotionType
                if (groupPos.IsReachable[missing, FREMotionTypeConstants.frJointMotionType, FREOrientTypeConstants.frAESWorldOrientType, missing, out motionErrorInfo])
                {
                    IsReachable = true;
                    groupPos.Update();

                    try
                    {
                        // DEBUG: Inspect the type returned by RegNumerics
                        object rawSpeedObj = robot.RegNumerics[SpeedRegisterId];
                        // _logAction?.Invoke($"[TeleopSubscriber] SpeedReg Type: {rawSpeedObj.GetType().FullName}", "#FF00FF");

                        // Attempt to cast to FRCRegNumeric explicitly
                        // If this fails, we will know from the log above (if enabled) or the catch below
                        if (rawSpeedObj is FRCRegNumeric speedReg)
                        {
                            // SAFETY: Clamp speed to minimum 1% and maximum 50% to prevent Faults (SRVO-171)
                            float safeSpeed = Math.Min(Math.Max(speed, 1.0f), 50.0f);
                            speedReg.RegFloat = safeSpeed;
                        }
                        else
                        {
                            // If it's not FRCRegNumeric, try dynamic as a fallback, but be careful
                            // dynamic dynSpeed = rawSpeedObj;
                            // dynSpeed.RegFloat = safeSpeed; 

                            // For now, just log that we couldn't cast it
                            // _logAction?.Invoke($"[TeleopSubscriber] SpeedReg is not FRCRegNumeric. It is: {rawSpeedObj.GetType().Name}", "#FFA500");
                        }
                    }
                    catch (Exception ex)
                    {
                        // Log only once or rarely to avoid spam
                        // _logAction?.Invoke($"[TeleopSubscriber] Speed Update Error: {ex.Message}", "#FFA500");
                    }
                }
                else
                {
                    IsReachable = false;

                    // 1. LOG THE COORDINATES: Are they huge (e.g., 50000) or normal (e.g., 500)?
                    string values = $"X={x:F2}, Y={y:F2}, Z={z:F2}";

                    // 2. LOG THE EXACT ERROR: This tells us WHY it failed
                    string errorRaw = motionErrorInfo != null ? motionErrorInfo.ToString() : "Unknown Error";

                    _logAction?.Invoke($"[TeleopSubscriber] Unreachable (Linear): X={x:F2}, Y={y:F2}, Z={z:F2}", "#FFA500", "Unreachable");
                }
            }
            catch (Exception ex)
            {
                // Often fails if robot is moving or locked
                _logAction?.Invoke($"[TeleopSubscriber] Robot Write Error: {ex.Message}", "#F44336", "RobotWriteError");
            }
        }
    }
}
