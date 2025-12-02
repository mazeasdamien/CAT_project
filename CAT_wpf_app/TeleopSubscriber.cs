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
    /// Simplified version based on user request.
    /// </summary>
    public class TeleopSubscriber
    {
        private const string TOPIC_NAME = "OperatorNewPose_Topic";
        private const string TYPE_NAME = "OperatorNewPose";

        private readonly DataReader<DynamicData> _reader;
        private readonly Action<string, string, string> _logAction;

        // Data holders for UI visualization
        public double LastJ1 { get; private set; }
        public double LastJ2 { get; private set; }
        public double LastJ3 { get; private set; }
        public double LastJ4 { get; private set; }
        public double LastJ5 { get; private set; }
        public double LastJ6 { get; private set; }

        public string LastId { get; private set; } = "N/A";
        public double LastTimestamp { get; private set; }
        public int TotalSamplesReceived { get; private set; }
        public int TotalRobotWrites { get; private set; }
        public bool IsReachable { get; private set; }

        // Configurable Register IDs
        public int PositionRegisterId { get; set; } = 1;
        public int SpeedRegisterId { get; set; } = 1;

        public TeleopSubscriber(DomainParticipant participant, DataReaderQos readerQos, Action<string, string, string> logAction = null)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));
            _logAction = logAction;

            try
            {
                // 1. Define Type
                var typeFactory = DynamicTypeFactory.Instance;
                var operatorPoseType = typeFactory.BuildStruct()
                   .WithName(TYPE_NAME)
                   .AddMember(new StructMember("J1", typeFactory.GetPrimitiveType<double>()))
                   .AddMember(new StructMember("J2", typeFactory.GetPrimitiveType<double>()))
                   .AddMember(new StructMember("J3", typeFactory.GetPrimitiveType<double>()))
                   .AddMember(new StructMember("J4", typeFactory.GetPrimitiveType<double>()))
                   .AddMember(new StructMember("J5", typeFactory.GetPrimitiveType<double>()))
                   .AddMember(new StructMember("J6", typeFactory.GetPrimitiveType<double>()))
                   .AddMember(new StructMember("Samples", typeFactory.GetPrimitiveType<int>()))
                   .Create();

                // 2. Register Type
                try
                {
                    participant.RegisterType(TYPE_NAME, operatorPoseType);
                }
                catch (Exception) { }

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

        public bool ReceiveAndProcess(FRCRobot robot)
        {
            if (_reader == null) return false;

            bool dataReceived = false;

            try
            {
                using var samples = _reader.Take();
                foreach (var sample in samples)
                {
                    if (sample.Info.ValidData)
                    {
                        DynamicData data = sample.Data;

                        // Update UI properties
                        LastJ1 = data.GetValue<double>("J1");
                        LastJ2 = data.GetValue<double>("J2");
                        LastJ3 = data.GetValue<double>("J3");
                        LastJ4 = data.GetValue<double>("J4");
                        LastJ5 = data.GetValue<double>("J5");
                        LastJ6 = data.GetValue<double>("J6");

                        TotalSamplesReceived++;
                        dataReceived = true;

                        if (robot != null && robot.IsConnected)
                        {
                            // Check for all zeros (invalid data)
                            if (Math.Abs(LastJ1) < 0.001 && Math.Abs(LastJ2) < 0.001 && Math.Abs(LastJ3) < 0.001 &&
                                Math.Abs(LastJ4) < 0.001 && Math.Abs(LastJ5) < 0.001 && Math.Abs(LastJ6) < 0.001)
                            {
                                // Skip writing to robot
                                return dataReceived;
                            }

                            FRCSysPositions positions = robot.RegPositions;
                            FRCSysPosition sysPosition = positions[PositionRegisterId];
                            FRCSysGroupPosition sysGroupPosition = sysPosition.Group[1];

                            // Use Joint Format
                            FRCJoint joint = sysGroupPosition.Formats[FRETypeCodeConstants.frJoint];

                            joint[1] = LastJ1;
                            joint[2] = LastJ2;
                            joint[3] = LastJ3;
                            joint[4] = LastJ4;
                            joint[5] = LastJ5;
                            joint[6] = LastJ6;

                            object missing = Type.Missing;
                            FRCMotionErrorInfo errorInfo; // Needed for out parameter

                            // Check reachability (Joint Motion)
                            if (sysGroupPosition.IsReachable[missing, FREMotionTypeConstants.frJointMotionType, FREOrientTypeConstants.frAESWorldOrientType, missing, out errorInfo])
                            {
                                IsReachable = true;
                                sysGroupPosition.Update();
                                TotalRobotWrites++;
                            }
                            else
                            {
                                IsReachable = false;
                                _logAction?.Invoke($"[Teleop] Unreachable: J1:{LastJ1:F2}...", "#FFA500", "Unreachable");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logAction?.Invoke($"[TeleopSubscriber] Process Error: {ex.Message}", "#F44336", "ProcessError");
            }

            return dataReceived;
        }
    }
}
