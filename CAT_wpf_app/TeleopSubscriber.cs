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
        private const string TOPIC_NAME = "OperatorNewPose_Topic";
        private const string TYPE_NAME = "OperatorNewPose";

        private readonly DataReader<DynamicData> _reader;
        private readonly Action<string> _logAction;

        // Data holders for UI visualization
        public float LastX { get; private set; }
        public float LastY { get; private set; }
        public float LastZ { get; private set; }
        public float LastW { get; private set; }
        public float LastP { get; private set; }
        public float LastR { get; private set; }
        public float LastSpeed { get; private set; }
        public int TotalSamplesReceived { get; private set; }

        // Configurable Register IDs
        public int PositionRegisterId { get; set; } = 1;
        public int SpeedRegisterId { get; set; } = 1;

        /// <summary>
        /// Initializes a new instance of the <see cref="TeleopSubscriber"/> class.
        /// </summary>
        /// <param name="participant">The DDS DomainParticipant.</param>
        /// <param name="readerQos">The DataReader QoS.</param>
        /// <param name="logAction">Action to log messages.</param>
        public TeleopSubscriber(DomainParticipant participant, DataReaderQos readerQos, Action<string> logAction = null)
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

                _logAction?.Invoke($"[TeleopSubscriber] Subscribed to {TOPIC_NAME}");
            }
            catch (Exception ex)
            {
                _logAction?.Invoke($"[TeleopSubscriber] Init Error: {ex.Message}");
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
                        // Writing to Register Position 3 as per requirements
                        if (robot != null && robot.IsConnected)
                        {
                            UpdateRobotRegister(robot, LastX, LastY, LastZ, LastW, LastP, LastR, LastSpeed);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logAction?.Invoke($"[TeleopSubscriber] Process Error: {ex.Message}");
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
                // 1. Update Position Register PR[x]
                FRCSysPositions positions = robot.RegPositions;
                FRCSysPosition sysPosition = positions[PositionRegisterId];
                FRCSysGroupPosition groupPos = sysPosition.Group[1];
                FRCXyzWpr xyzWpr = groupPos.Formats[FRETypeCodeConstants.frXyzWpr];

                // Assign values
                xyzWpr.X = x;
                xyzWpr.Y = y;
                xyzWpr.Z = z;
                xyzWpr.W = w;
                xyzWpr.P = p;
                xyzWpr.R = r;

                // Commit changes to the controller
                groupPos.Update();

                // 2. Update Data Register R[x] for Speed
                FRCVar varSpeed = robot.RegNumerics[SpeedRegisterId];
                varSpeed.Value.Value = speed;
                if (!varSpeed.Update())
                {
                    // _logAction?.Invoke($"[TeleopSubscriber] Failed to update Speed R[{SpeedRegisterId}]");
                }
            }
            catch (Exception)
            {
                // Often fails if robot is moving or locked
                // _logAction?.Invoke($"[TeleopSubscriber] Robot Write Error: {ex.Message}");
            }
        }
    }
}
