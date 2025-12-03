using UnityEngine;

[CreateAssetMenu(fileName = "DDSSettings", menuName = "CAT/DDS Settings")]
public class DDSSettings : ScriptableObject
{
    [Header("General Configuration")]
    [Tooltip("The DDS Domain ID to participate in.")]
    public int DomainId = 0;

    [Tooltip("The name of the configuration file in StreamingAssets.")]
    public string ConfigFileName = "rtps.ini";

    [Tooltip("Enable verbose logging to the Unity Console.")]
    public bool VerboseLogging = true;

    [Header("QoS Defaults")]
    [Tooltip("If true, late-joining subscribers will receive the last published sample.")]
    public bool UseTransientLocal = true;

    [Tooltip("If true, ensures data delivery.")]
    public bool UseReliable = true;

    [Tooltip("Number of samples to keep in history.")]
    public int HistoryDepth = 1;
}
