using UnityEngine;
using Rti.Dds.Core;
using Rti.Dds.Domain;
using Rti.Dds.Publication;
using Rti.Dds.Topics;

/// <summary>
/// The BioDataPublisher class simulates the broadcasting of operator biometric data (e.g., stress levels, pupil diameter)
/// to the DDS network. This data is intended to be consumed by the robot control system to adapt its behavior
/// based on the human operator's state (Human-Robot Collaboration).
/// 
/// Functionality:
/// 1. Reads a CSV file (`sim_trace.csv`) containing time-series biometric data.
/// 2. Publishes this data at a fixed rate (50Hz) to the "Operator_Bio_State" topic.
/// 3. Simulates physiological responses (e.g., pupil dilation) based on stress metrics.
/// </summary>
public class BioDataPublisher : MonoBehaviour
{
    public TextAsset csvFile; // Drag sim_trace.csv here
    private string[] _lines;
    private int _index = 1; // Skip header

    // DDS
    private DomainParticipant _participant;
    private DataWriter<Operator_Bio_State> _writer;

    void Start()
    {
        // 1. Setup DDS
        _participant = DomainParticipantFactory.Instance.CreateParticipant(0);
        var topic = _participant.CreateTopic<Operator_Bio_State>("Operator_Bio_State");
        _writer = _participant.ImplicitPublisher.CreateDataWriter(topic);

        // 2. Load Data
        _lines = csvFile.text.Split('\n');
    }

    void FixedUpdate() // Runs at 50Hz (Simulation Speed)
    {
        if (_index < _lines.Length)
        {
            var line = _lines[_index];
            var parts = line.Split(',');
            if (parts.Length >= 2)
            {
                var sample = new Operator_Bio_State();
                sample.timestamp = System.DateTime.Now.ToString("HH:mm:ss.fff");

                // Parse the Stress (0.0 - 1.0)
                if (float.TryParse(parts[1], out float stress))
                {
                    sample.stress_index = stress;
                    // Simulate Pupil opening with stress
                    sample.pupil_diameter_mm = 3.0f + (stress * 2.0f);
                    sample.gaze_on_robot = true; // Assume looking at robot

                    _writer.Write(sample);
                }
            }
            _index++;
        }
        else
        {
            _index = 1; // Loop the dataset
        }
    }

    void OnDestroy()
    {
        if (_participant != null) _participant.Dispose();
    }
}

/// <summary>
/// Data Structure representing the Operator's Biometric State.
/// This class is used as the data type for the DDS Topic "Operator_Bio_State".
/// </summary>
public class Operator_Bio_State
{
    /// <summary>
    /// Timestamp of the sample (HH:mm:ss.fff).
    /// </summary>
    public string timestamp;

    /// <summary>
    /// Normalized stress index (0.0 to 1.0).
    /// </summary>
    public float stress_index;

    /// <summary>
    /// Pupil diameter in millimeters.
    /// </summary>
    public float pupil_diameter_mm;

    /// <summary>
    /// Boolean flag indicating if the operator is looking at the robot.
    /// </summary>
    public bool gaze_on_robot;
}