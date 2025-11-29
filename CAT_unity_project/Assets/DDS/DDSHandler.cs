using System;
using System.Collections.Generic;
using Rti.Dds.Core;
using Rti.Dds.Domain;
using Rti.Dds.Publication;
using Rti.Dds.Subscription;
using Rti.Dds.Topics;
using Rti.Types.Dynamic;
using UnityEngine;

/// <summary>
/// The DDSHandler class serves as the central manager for the Data Distribution Service (DDS) integration within the Unity application.
/// It implements the Singleton design pattern to ensure a unified entry point for all DDS communications (Publishing and Subscribing).
/// 
/// Key Responsibilities:
/// 1. Lifecycle Management: Initializes and disposes of the DDS DomainParticipant.
/// 2. Resource Optimization: Manages shared Publisher and Subscriber entities to reduce resource overhead.
/// 3. QoS Configuration: Loads and applies Quality of Service (QoS) profiles from an external XML file.
/// 4. Topic Management: Prevents duplicate topic creation by maintaining a registry of active topics.
/// </summary>
public class DDSHandler : MonoBehaviour
{
    /// <summary>
    /// Singleton instance of the DDSHandler.
    /// Access this via DDSHandler.Instance from other scripts.
    /// </summary>
    public static DDSHandler Instance { get; private set; }

    [Header("DDS Configuration")]
    [Tooltip("The name of the XML file containing QoS profiles.")]
    [SerializeField] private string qosFileName = "QOS.xml";

    [Tooltip("The name of the QoS library defined in the XML file.")]
    [SerializeField] private string qosLibrary = "RigQoSLibrary";

    [Tooltip("The name of the QoS profile to use.")]
    [SerializeField] private string qosProfile = "RigQoSProfile";

    private QosProvider _provider;

    /// <summary>
    /// The main DDS DomainParticipant. This entity is the entry point for all DDS communication.
    /// </summary>
    public DomainParticipant Participant { get; private set; }

    // Shared Publisher and Subscriber instances to reduce overhead.
    private Publisher _sharedPublisher;
    private Subscriber _sharedSubscriber;

    // Cached full profile name to avoid repeated string concatenation.
    private string _fullProfileName;

    // Dictionary to cache created topics and avoid duplicate creation.
    private readonly Dictionary<string, Topic<DynamicData>> _registeredTopics = new Dictionary<string, Topic<DynamicData>>();

    /// <summary>
    /// Awake is called when the script instance is being loaded.
    /// Initializes the Singleton instance and sets up DDS.
    /// </summary>
    private void Awake()
    {
        // This ensures Unity continues processing DDS packets even if you click another window.
        Application.runInBackground = true;

        // Singleton pattern implementation
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject); // Persist across scene loads

        InitializeDDS();
    }

    /// <summary>
    /// Initializes the DDS ecosystem.
    /// This method sets up the DomainParticipant, loads the QoS XML configuration, and pre-allocates
    /// shared Publisher and Subscriber entities to optimize performance during runtime.
    /// </summary>
    private void InitializeDDS()
    {
        try
        {
            // Load QoS settings from the specified XML file
            _provider = new QosProvider(qosFileName);

            // Create the DomainParticipant (domain ID 0 is standard default)
            Participant = DomainParticipantFactory.Instance.CreateParticipant(0, _provider.GetDomainParticipantQos());

            // Construct the full profile name string once
            _fullProfileName = $"{qosLibrary}::{qosProfile}";

            // Pre-create a shared Publisher and Subscriber. 
            // This is a performance optimization to avoid creating new entities for every DataWriter/DataReader.
            var publisherQos = _provider.GetPublisherQos(_fullProfileName);
            _sharedPublisher = Participant.CreatePublisher(publisherQos);

            var subscriberQos = _provider.GetSubscriberQos(_fullProfileName);
            _sharedSubscriber = Participant.CreateSubscriber(subscriberQos);

            Debug.Log("DDS Participant initialized successfully.");
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to initialize DDS: {e.Message}");
        }
    }

    /// <summary>
    /// Retrieves an existing Topic from the registry or creates a new one if it does not exist.
    /// This ensures that multiple scripts requesting the same Topic share the same underlying DDS entity,
    /// preventing "Duplicate Topic" errors and saving resources.
    /// </summary>
    /// <param name="topicName">The unique name of the DDS Topic.</param>
    /// <param name="dynamicData">The DynamicType definition describing the data structure.</param>
    /// <returns>The active Topic instance.</returns>
    private Topic<DynamicData> GetOrCreateTopic(string topicName, DynamicType dynamicData)
    {
        // Check if the topic is already registered
        if (_registeredTopics.TryGetValue(topicName, out var existingTopic))
        {
            return existingTopic;
        }

        try
        {
            // Create a new topic and register it
            Topic<DynamicData> topic = Participant.CreateTopic(topicName, dynamicData);
            _registeredTopics[topicName] = topic;
            return topic;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to create topic '{topicName}': {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Configures and creates a DataReader for a specific Topic.
    /// This method abstracts the complexity of DDS Reader creation, automatically handling Topic registration
    /// and QoS application.
    /// </summary>
    /// <param name="topicName">The name of the Topic to subscribe to.</param>
    /// <param name="dynamicData">The structure definition of the data to be received.</param>
    /// <returns>A typed DataReader for DynamicData, or null if initialization fails.</returns>
    public DataReader<DynamicData> SetupDataReader(string topicName, DynamicType dynamicData)
    {
        if (Participant == null)
        {
            Debug.LogError("Cannot setup DataReader: Participant is not initialized.");
            return null;
        }

        try
        {
            Topic<DynamicData> topic = GetOrCreateTopic(topicName, dynamicData);
            if (topic == null) return null;

            // Use the shared Subscriber to create the DataReader
            var readerQos = _provider.GetDataReaderQos(_fullProfileName);
            DataReader<DynamicData> reader = _sharedSubscriber.CreateDataReader(topic, readerQos);

            return reader;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to setup DataReader for topic '{topicName}': {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Configures and creates a DataWriter for a specific Topic.
    /// This method abstracts the complexity of DDS Writer creation, allowing other scripts to easily
    /// publish data without managing the underlying DDS entities.
    /// </summary>
    /// <param name="topicName">The name of the Topic to publish to.</param>
    /// <param name="dynamicData">The structure definition of the data to be sent.</param>
    /// <returns>A typed DataWriter for DynamicData, or null if initialization fails.</returns>
    public DataWriter<DynamicData> SetupDataWriter(string topicName, DynamicType dynamicData)
    {
        if (Participant == null)
        {
            Debug.LogError("Cannot setup DataWriter: Participant is not initialized.");
            return null;
        }

        try
        {
            Topic<DynamicData> topic = GetOrCreateTopic(topicName, dynamicData);
            if (topic == null) return null;

            // Use the shared Publisher to create the DataWriter
            var writerQos = _provider.GetDataWriterQos(_fullProfileName);
            DataWriter<DynamicData> writer = _sharedPublisher.CreateDataWriter(topic, writerQos);

            return writer;
        }
        catch (Exception e)
        {
            Debug.LogError($"Failed to setup DataWriter for topic '{topicName}': {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Called when the script instance is being destroyed.
    /// Cleans up DDS resources to prevent leaks.
    /// </summary>
    private void OnDestroy()
    {
        if (Participant != null)
        {
            Participant.Dispose();
            Participant = null;
        }
    }

    /// <summary>
    /// Called when the application quits.
    /// Ensures resources are disposed of if OnDestroy wasn't called (redundant safety check).
    /// </summary>
    private void OnApplicationQuit()
    {
        if (Participant != null)
        {
            Participant.Dispose();
            Participant = null;
        }
    }
}
