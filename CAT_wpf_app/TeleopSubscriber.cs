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
        public int TotalRobotWrites { get; private set; } // Track actual writes
        public bool IsReachable { get; private set; }

        // Rate Limiting
        private DateTime _lastRobotWriteTime = DateTime.MinValue;
        private const double MinWriteIntervalMs = 50.0; // 20Hz Limit

        // Configurable Register IDs
        public int PositionRegisterId { get; set; } = 1;
        public int BufferPositionRegisterId { get; set; } = 9; // Buffer PR
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
                    }
                }

                // Rate Limiting Check
                if (dataReceived && robot != null && robot.IsConnected)
                {
                    if ((DateTime.Now - _lastRobotWriteTime).TotalMilliseconds >= MinWriteIntervalMs)
                    {
                        UpdateRobotRegister(robot, LastX, LastY, LastZ, LastW, LastP, LastR, LastSpeed);
                        _lastRobotWriteTime = DateTime.Now;
                        TotalRobotWrites++;
                    }
                }
            }
            catch (Exception ex)
            {
                _logAction?.Invoke($"[TeleopSubscriber] Process Error: {ex.Message}", "#F44336", "ProcessError");
            }

            return dataReceived;
        }

        /// <summary>
        /// Updates the robot's Position Register using a Buffer Strategy.
        /// 1. Write to Buffer PR (e.g., PR[9]).
        /// 2. Check Reachability on Buffer PR.
        /// 3. If Valid, Copy Buffer PR to Target PR (e.g., PR[1]).
        /// </summary>
        private void UpdateRobotRegister(FRCRobot robot, float x, float y, float z, float w, float p, float r, float speed)
        {
            try
            {
                // 1. Get Access to BUFFER Register (PR[9])
                FRCSysPositions positions = robot.RegPositions;
                FRCSysPosition bufferSysPosition = positions[BufferPositionRegisterId];
                FRCSysGroupPosition bufferGroupPos = bufferSysPosition.Group[1];
                FRCXyzWpr bufferXyzWpr = bufferGroupPos.Formats[FRETypeCodeConstants.frXyzWpr];

                // Safety Check: Ignore (0,0,0)
                if (Math.Abs(x) < 0.1f && Math.Abs(y) < 0.1f && Math.Abs(z) < 0.1f)
                {
                    _logAction?.Invoke("[Teleop] Ignored Zero Pose (0,0,0)", "#FFA500", "ZeroPose");
                    return;
                }

                // 2. Set Coordinates on BUFFER
                bufferXyzWpr.X = x;
                bufferXyzWpr.Y = y;
                bufferXyzWpr.Z = z;
                bufferXyzWpr.W = w;
                bufferXyzWpr.P = p;
                bufferXyzWpr.R = r;

                // 3. Configuration Handling (Copy Current Robot Config to Buffer)
                FRCCurGroupPosition currentGroupPos = robot.CurPosition.Group[1, FRECurPositionConstants.frWorldDisplayType];
                FRCXyzWpr currentXyzWpr = currentGroupPos.Formats[FRETypeCodeConstants.frXyzWpr];
                bufferXyzWpr.Config.Text = currentXyzWpr.Config.Text;

                // 4. Reachability Check on BUFFER
                object missing = System.Type.Missing;
                FRCMotionErrorInfo motionErrorInfo;
                bool isReachable = false;

                // Check Linear First
                if (bufferGroupPos.IsReachable[missing, FREMotionTypeConstants.frLinearMotionType, FREOrientTypeConstants.frAESWorldOrientType, missing, out motionErrorInfo])
                {
                    isReachable = true;
                }
                else
                {
                    // Fallback: Reset Turns
                    try
                    {
                        // bufferXyzWpr.Config.Turn1 = 0; // Not available in this PCDK version
                        // bufferXyzWpr.Config.Turn2 = 0;
                        // bufferXyzWpr.Config.Turn3 = 0;

                        // Parse and reset turns via Text property
                        // Format is usually "N U T, 0, 0, 0"
                        string currentConfigStr = bufferXyzWpr.Config.Text;
                        if (!string.IsNullOrEmpty(currentConfigStr))
                        {
                            string[] parts = currentConfigStr.Split(',');
                            if (parts.Length > 0)
                            {
                                // Keep the first part (Posture: N U T) and append 0,0,0
                                bufferXyzWpr.Config.Text = $"{parts[0]}, 0, 0, 0";
                            }
                        }
                        if (bufferGroupPos.IsReachable[missing, FREMotionTypeConstants.frLinearMotionType, FREOrientTypeConstants.frAESWorldOrientType, missing, out motionErrorInfo])
                        {
                            isReachable = true;
                        }
                    }
                    catch { }
                }

                IsReachable = isReachable;

                if (isReachable)
                {
                    // 5. Commit to TARGET Register (PR[1])
                    // We know the values in 'bufferXyzWpr' are valid (including the potentially reset turns).
                    // We copy them to PR[1].
                    FRCSysPosition targetSysPosition = positions[PositionRegisterId];
                    FRCSysGroupPosition targetGroupPos = targetSysPosition.Group[1];
                    FRCXyzWpr targetXyzWpr = targetGroupPos.Formats[FRETypeCodeConstants.frXyzWpr];

                    targetXyzWpr.X = bufferXyzWpr.X;
                    targetXyzWpr.Y = bufferXyzWpr.Y;
                    targetXyzWpr.Z = bufferXyzWpr.Z;
                    targetXyzWpr.W = bufferXyzWpr.W;
                    targetXyzWpr.P = bufferXyzWpr.P;
                    targetXyzWpr.R = bufferXyzWpr.R;
                    targetXyzWpr.Config.Text = bufferXyzWpr.Config.Text; // Copy the validated config

                    targetGroupPos.Update(); // Write to Controller

                    // 6. Update Speed
                    object rawSpeedObj = robot.RegNumerics[SpeedRegisterId];
                    if (rawSpeedObj is FRCRegNumeric speedReg)
                    {
                        float maxLinearSpeed = 2000.0f;
                        float calculatedSpeed = (speed / 100.0f) * maxLinearSpeed;
                        float safeSpeed = Math.Max(calculatedSpeed, 50.0f);
                        safeSpeed = Math.Min(safeSpeed, maxLinearSpeed);
                        speedReg.RegFloat = safeSpeed;
                        _logAction?.Invoke($"[Teleop] Speed Update: {safeSpeed:F1} mm/s (Reg[{SpeedRegisterId}])", "#2196F3", "SpeedUpdate");
                    }
                }
                else
                {
                    _logAction?.Invoke($"[TeleopSubscriber] Unreachable: X={x:F2}, Y={y:F2}, Z={z:F2}", "#FFA500", "Unreachable");
                }
            }
            catch (Exception ex)
            {
                _logAction?.Invoke($"[Robot] Write Error: {ex.Message}", "#F44336", "WriteError");
            }
        }
    }
}
