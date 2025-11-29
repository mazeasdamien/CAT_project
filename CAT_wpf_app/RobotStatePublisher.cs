using System;
using Rti.Dds.Core;
using Rti.Dds.Domain;
using Rti.Dds.Publication;
using Rti.Dds.Topics;
using Rti.Types.Dynamic;
using FRRobot;

namespace CAT_wpf_app
{
    /// <summary>
    /// Handles the publication of Fanuc Robot state data to DDS.
    /// Uses Dynamic Data to define the data structure at runtime.
    /// </summary>
    public class RobotStatePublisher
    {
        private const string TOPIC_NAME = "RobotState_Topic";
        private const string TYPE_NAME = "RobotState";

        private readonly DataWriter<DynamicData> _writer;
        private readonly DynamicData _sample;
        private readonly StructType _robotStateType;
        private readonly Action<string> _logAction;

        // State tracking for change detection to avoid publishing redundant data
        private readonly float[] _prevJoints = new float[6];
        private int _sampleId = 0;
        private bool _firstRun = true;

        /// <summary>
        /// Initializes a new instance of the <see cref="RobotStatePublisher"/> class.
        /// Sets up the Dynamic Type, Topic, and DataWriter.
        /// </summary>
        /// <param name="participant">The DDS DomainParticipant.</param>
        /// <param name="writerQos">The DataWriter QoS.</param>
        /// <param name="logAction">Action to log messages.</param>
        public RobotStatePublisher(DomainParticipant participant, DataWriterQos writerQos, Action<string> logAction = null)
        {
            if (participant == null) throw new ArgumentNullException(nameof(participant));
            _logAction = logAction;

            // 1. Define Type
            var typeFactory = DynamicTypeFactory.Instance;
            _robotStateType = typeFactory.BuildStruct()
                .WithName(TYPE_NAME)
                .AddMember(new StructMember("Clock", typeFactory.CreateString(bounds: 50)))
                .AddMember(new StructMember("Sample", typeFactory.GetPrimitiveType<int>()))
                .AddMember(new StructMember("J1", typeFactory.GetPrimitiveType<double>()))
                .AddMember(new StructMember("J2", typeFactory.GetPrimitiveType<double>()))
                .AddMember(new StructMember("J3", typeFactory.GetPrimitiveType<double>()))
                .AddMember(new StructMember("J4", typeFactory.GetPrimitiveType<double>()))
                .AddMember(new StructMember("J5", typeFactory.GetPrimitiveType<double>()))
                .AddMember(new StructMember("J6", typeFactory.GetPrimitiveType<double>()))
                .AddMember(new StructMember("X", typeFactory.GetPrimitiveType<double>()))
                .AddMember(new StructMember("Y", typeFactory.GetPrimitiveType<double>()))
                .AddMember(new StructMember("Z", typeFactory.GetPrimitiveType<double>()))
                .AddMember(new StructMember("W", typeFactory.GetPrimitiveType<double>()))
                .AddMember(new StructMember("P", typeFactory.GetPrimitiveType<double>()))
                .AddMember(new StructMember("R", typeFactory.GetPrimitiveType<double>()))
                .Create();

            // 2. Register Type
            participant.RegisterType(TYPE_NAME, _robotStateType);

            // 3. Create Topic
            Topic<DynamicData> topic = participant.CreateTopic<DynamicData>(TOPIC_NAME, TYPE_NAME);

            // 4. Create DataWriter with the specific QoS
            _writer = participant.ImplicitPublisher.CreateDataWriter(topic, writerQos);

            // 5. Create Sample
            _sample = new DynamicData(_robotStateType);
        }

        /// <summary>
        /// Reads the current state from the robot and publishes it to DDS if changes are detected.
        /// </summary>
        /// <param name="robot">The connected Fanuc Robot object.</param>
        /// <returns>True if a new sample was written to DDS; otherwise, false.</returns>
        public bool Publish(FRCRobot robot)
        {
            if (robot == null || !robot.IsConnected) return false;

            try
            {
                // Access Robot Data
                FRCCurPosition curPosition = robot.CurPosition;
                FRCCurGroupPosition groupPositionJoint = curPosition.Group[1, FRECurPositionConstants.frJointDisplayType];
                FRCCurGroupPosition groupPositionWorld = curPosition.Group[1, FRECurPositionConstants.frWorldDisplayType];

                // Refresh data from the controller
                groupPositionJoint.Refresh();
                groupPositionWorld.Refresh();

                FRCJoint joint = groupPositionJoint.Formats[FRETypeCodeConstants.frJoint];
                FRCXyzWpr xyzWpr = groupPositionWorld.Formats[FRETypeCodeConstants.frXyzWpr];

                // Extract current joint values
                float[] currentJoints = new float[]
                {
                    (float)joint[1], (float)joint[2], (float)joint[3],
                    (float)joint[4], (float)joint[5], (float)joint[6]
                };

                // Check for changes
                bool hasChanged = _firstRun;
                if (!_firstRun)
                {
                    for (int i = 0; i < 6; i++)
                    {
                        if (Math.Abs(currentJoints[i] - _prevJoints[i]) > 0.0001f)
                        {
                            hasChanged = true;
                            break;
                        }
                    }
                }

                if (hasChanged)
                {
                    // Update Sample fields
                    _sample.SetValue("Clock", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"));
                    _sample.SetValue("Sample", _sampleId);

                    _sample.SetValue("J1", (double)currentJoints[0]);
                    _sample.SetValue("J2", (double)currentJoints[1]);
                    _sample.SetValue("J3", (double)currentJoints[2]);
                    _sample.SetValue("J4", (double)currentJoints[3]);
                    _sample.SetValue("J5", (double)currentJoints[4]);
                    _sample.SetValue("J6", (double)currentJoints[5]);

                    _sample.SetValue("X", (double)xyzWpr.X);
                    _sample.SetValue("Y", (double)xyzWpr.Y);
                    _sample.SetValue("Z", (double)xyzWpr.Z);
                    _sample.SetValue("W", (double)xyzWpr.W);
                    _sample.SetValue("P", (double)xyzWpr.P);
                    _sample.SetValue("R", (double)xyzWpr.R);

                    // Write the sample to DDS
                    _writer.Write(_sample);

                    // Log
                    _logAction?.Invoke($"[Publisher] Sample {_sampleId} sent. J1: {currentJoints[0]:F2}, X: {xyzWpr.X:F2}");

                    // Update state
                    Array.Copy(currentJoints, _prevJoints, 6);
                    _sampleId++;
                    _firstRun = false;
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logAction?.Invoke($"[Publisher] Error: {ex.Message}");
            }
            return false;
        }
    }
}
