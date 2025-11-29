using System.Collections.Generic;
using Rti.Dds.Core;
using Rti.Dds.Domain;
using Rti.Dds.Publication;
using Rti.Dds.Subscription;
using Rti.Dds.Topics;
using Rti.Types.Dynamic;
using UnityEngine;

// The DDSHandler class manages the setup and configuration of DDS components within a Unity application.
public class DDSHandler : MonoBehaviour
{
    public static DDSHandler Instance { get; private set; }

    private const string QosProfile = "CAT_Industrial_Library::SafetyCritical_Profile";
    private QosProvider provider;  // Quality of Service provider for configuring DDS settings.
    public DomainParticipant participant;  // The main DDS domain participant for communication.

    private Dictionary<string, Topic<DynamicData>> registeredTopics = new Dictionary<string, Topic<DynamicData>>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Debug.LogWarning("Multiple DDSHandler instances found. Destroying duplicate.");
            Destroy(gameObject);
            return;
        }

        try
        {
            // Explicitly locate and set the license file
            string projectRoot = System.IO.Directory.GetParent(Application.dataPath).FullName;
            string licensePath = System.IO.Path.Combine(projectRoot, "rti_license.dat");

            if (System.IO.File.Exists(licensePath))
            {
                Debug.Log($"[DDSHandler] Found license file at: {licensePath}");
                System.Environment.SetEnvironmentVariable("RTI_LICENSE_FILE", licensePath);
            }
            else
            {
                Debug.LogError($"[DDSHandler] License file NOT found at: {licensePath}");
            }

            string qosPath = System.IO.Path.Combine(projectRoot, "USER_QOS_PROFILES.xml");
            Debug.Log($"[DDSHandler] Working Directory: {System.IO.Directory.GetCurrentDirectory()}");
            Debug.Log($"[DDSHandler] Attempting to load QoS from: {qosPath}");
            
            if (!System.IO.File.Exists(qosPath))
            {
                Debug.LogError($"[DDSHandler] QoS file not found at: {qosPath}");
            }

            provider = new QosProvider(qosPath);
            Debug.Log("[DDSHandler] QoS Provider loaded successfully.");

            var participantQos = provider.GetDomainParticipantQos();
            Debug.Log("[DDSHandler] Retrieved DomainParticipantQos.");

            participant = DomainParticipantFactory.Instance.CreateParticipant(0, participantQos);
            Debug.Log("[DDSHandler] DomainParticipant created successfully.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to initialize DDS: {e.Message}\nStack Trace: {e.StackTrace}");
            if (e.InnerException != null)
            {
                Debug.LogError($"Inner Exception: {e.InnerException.Message}");
            }
        }
    }

    // Sets up a DataReader for a specified topic with dynamic data type.
    public DataReader<DynamicData> SetupDataReader(string topicName, DynamicType dynamicData)
    {
        if (participant == null)
        {
            Debug.LogError("DDS Participant is not initialized.");
            return null;
        }

        Topic<DynamicData> topic = GetOrCreateTopic(topicName, dynamicData);
        if (topic == null) return null;

        try
        {
            // Creates a subscriber with QoS settings
            var subscriberQos = provider.GetSubscriberQos(QosProfile);
            Subscriber subscriber = participant.CreateSubscriber(subscriberQos);

            // Creates a DataReader with QoS settings
            var readerQos = provider.GetDataReaderQos(QosProfile);
            return subscriber.CreateDataReader(topic, readerQos);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to create DataReader for {topicName}: {e.Message}");
            return null;
        }
    }

    // Sets up a DataWriter for a specified topic with dynamic data type.
    public DataWriter<DynamicData> SetupDataWriter(string topicName, DynamicType dynamicData)
    {
        if (participant == null)
        {
            Debug.LogError("DDS Participant is not initialized.");
            return null;
        }

        Topic<DynamicData> topic = GetOrCreateTopic(topicName, dynamicData);
        if (topic == null) return null;

        try
        {
            // Creates a publisher with QoS settings
            var publisherQos = provider.GetPublisherQos(QosProfile);
            Publisher publisher = participant.CreatePublisher(publisherQos);

            // Creates a DataWriter with QoS settings
            var writerQos = provider.GetDataWriterQos(QosProfile);
            return publisher.CreateDataWriter(topic, writerQos);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to create DataWriter for {topicName}: {e.Message}");
            return null;
        }
    }

    private Topic<DynamicData> GetOrCreateTopic(string topicName, DynamicType dynamicData)
    {
        if (registeredTopics.TryGetValue(topicName, out var existingTopic))
        {
            return existingTopic;
        }

        try
        {
            // Check if topic description already exists to avoid duplication errors if DDS keeps track globally
            Topic<DynamicData> topic = participant.CreateTopic(topicName, dynamicData);
            registeredTopics[topicName] = topic;
            return topic;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to create topic {topicName}: {e.Message}");
            return null;
        }
    }

    // Called when the application is quitting. Disposes of the DDS participant.
    private void OnApplicationQuit()
    {
        if (participant != null)
        {
            participant.Dispose();
        }
    }
}
