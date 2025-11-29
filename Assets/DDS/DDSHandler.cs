using System.Collections.Generic;
using Rti.Dds.Core;
using Rti.Dds.Domain;
using Rti.Dds.Publication;
using Rti.Dds.Subscription;
using Rti.Dds.Topics;
using Rti.Types.Dynamic;
using UnityEngine;

public class DDSHandler : MonoBehaviour
{
    public static DDSHandler Instance { get; private set; }

    private const string QosProfile = "CAT_Industrial_Library::SafetyCritical_Profile";
    private QosProvider provider;
    public DomainParticipant participant;

    private Dictionary<string, Topic<DynamicData>> registeredTopics = new Dictionary<string, Topic<DynamicData>>();

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        try
        {
            string projectRoot = System.IO.Directory.GetParent(Application.dataPath).FullName;
            string licensePath = System.IO.Path.Combine(projectRoot, "rti_license.dat");

            if (System.IO.File.Exists(licensePath))
            {
                System.Environment.SetEnvironmentVariable("RTI_LICENSE_FILE", licensePath);
            }
            else
            {
                Debug.LogError($"[DDSHandler] License file NOT found at: {licensePath}");
            }

            string qosPath = System.IO.Path.Combine(projectRoot, "USER_QOS_PROFILES.xml");
            
            if (!System.IO.File.Exists(qosPath))
            {
                Debug.LogError($"[DDSHandler] QoS file not found at: {qosPath}");
                return;
            }

            string qosUri = "file:///" + qosPath.Replace("\\", "/");

            provider = new QosProvider(qosUri);

            var participantQos = provider.GetDomainParticipantQos();

            participant = DomainParticipantFactory.Instance.CreateParticipant(0, participantQos);
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
            var subscriberQos = provider.GetSubscriberQos(QosProfile);
            Subscriber subscriber = participant.CreateSubscriber(subscriberQos);

            var readerQos = provider.GetDataReaderQos(QosProfile);
            return subscriber.CreateDataReader(topic, readerQos);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Failed to create DataReader for {topicName}: {e.Message}");
            return null;
        }
    }

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
            var publisherQos = provider.GetPublisherQos(QosProfile);
            Publisher publisher = participant.CreatePublisher(publisherQos);

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

    private void OnApplicationQuit()
    {
        if (participant != null)
        {
            participant.Dispose();
        }
    }
}