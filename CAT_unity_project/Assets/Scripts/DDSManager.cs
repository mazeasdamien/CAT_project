using System;
using System.IO;
using System.Collections;
using UnityEngine;
using OpenDDSharp;
using OpenDDSharp.DDS;
using OpenDDSharp.OpenDDS.DCPS;

public class DDSManager : MonoBehaviour
{
    private static DDSManager _instance;
    public static DDSManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindFirstObjectByType<DDSManager>();
                if (_instance == null)
                {
                    GameObject go = new GameObject("DDSManager");
                    _instance = go.AddComponent<DDSManager>();
                }
            }
            return _instance;
        }
    }

    [Header("Settings Asset")]
    public DDSSettings Settings;

    // Fallback values if Settings is null
    private int _domainId => Settings != null ? Settings.DomainId : 0;
    private string _configFileName => Settings != null ? Settings.ConfigFileName : "rtps.ini";
    private bool _verboseLogging => Settings != null ? Settings.VerboseLogging : true;

    public bool IsInitialized { get; private set; } = false;
    public DomainParticipant Participant { get; private set; }

    private DomainParticipantFactory _dpf;
    private bool _isCleanedUp = false;
    private static bool _isAceInitialized = false;

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(this.gameObject);
            return;
        }
        _instance = this;
        // We don't use DontDestroyOnLoad by default so it cleans up with the scene, 
        // but you can enable it if you want persistent DDS across scenes.
    }

    void Start()
    {
        InitializeDDS();
    }

    private void InitializeDDS()
    {
        try
        {
            LogInfo("--- Starting DDS Initialization (Manager) ---");

            // 1. Check Config File
            string configPath = Path.Combine(Application.streamingAssetsPath, _configFileName);
            if (!File.Exists(configPath))
            {
                LogError($"CRITICAL: Config file not found at: {configPath}");
                return;
            }
            LogInfo($"Config file found: {configPath}");

            // 2. Init ACE (Only once per Unity Process)
            if (!_isAceInitialized)
            {
                try
                {
                    Ace.Init();
                    _isAceInitialized = true;
                    LogInfo("ACE Initialized.");
                }
                catch (Exception ex)
                {
                    LogInfo($"ACE Init warning (likely already running): {ex.Message}");
                    _isAceInitialized = true; // Assume it's running if it failed
                }
            }

            // 3. Get Factory
            // Note: In Editor, this retrieves the existing factory from the persistent native memory
            _dpf = ParticipantService.Instance.GetDomainParticipantFactory("-DCPSConfigFile", configPath);
            if (_dpf == null)
            {
                LogError("CRITICAL: Failed to get DomainParticipantFactory.");
                return;
            }

            // 4. Create Participant
            Participant = _dpf.CreateParticipant(_domainId);
            if (Participant == null)
            {
                LogError($"CRITICAL: Could not create DomainParticipant for Domain ID {_domainId}.");
                return;
            }

            IsInitialized = true;
            LogInfo("DDS Manager Initialized Successfully.");
        }
        catch (Exception e)
        {
            LogError($"EXCEPTION during Init: {e.Message}\n{e.StackTrace}");
        }
    }

    public void LogInfo(string msg)
    {
        if (_verboseLogging) Debug.Log($"[DDS-Manager] {msg}");
    }

    public void LogError(string msg)
    {
        Debug.LogError($"[DDS-Manager] {msg}");
    }

    // --- CLEANUP ---
    void OnDestroy()
    {
        CleanupDDS();
    }

    void OnApplicationQuit()
    {
        CleanupDDS();
    }

    public void CleanupDDS()
    {
        if (_isCleanedUp) return;

        LogInfo("Cleaning up DDS Entities...");

        // 1. Delete the Participant (This deletes Subscriber, Reader, Topic automatically)
        if (Participant != null)
        {
            try
            {
                Participant.DeleteContainedEntities();
                if (_dpf != null)
                {
                    _dpf.DeleteParticipant(Participant);
                }
            }
            catch (Exception e)
            {
                LogError($"Error deleting participant: {e.Message}");
            }
            Participant = null;
        }

        // 2. CRITICAL CHANGE: Handling Service Shutdown
        // In Unity Editor, we MUST keep the Service and ACE alive.
        if (Application.isEditor)
        {
            LogInfo("Unity Editor detected: Keeping ACE and ParticipantService alive for next run.");
            // We purposefully do NOT call ParticipantService.Instance.Shutdown() here
            // We purposefully do NOT call Ace.Fini() here
        }
        else
        {
            // In a Standalone Build (EXE), we want a clean exit.
            try
            {
                if (ParticipantService.Instance != null)
                {
                    LogInfo("Shutting down ParticipantService...");
                    ParticipantService.Instance.Shutdown();
                }
                LogInfo("Shutting down ACE...");
                Ace.Fini();
            }
            catch (Exception e)
            {
                LogInfo($"Shutdown warning: {e.Message}");
            }
        }

        _isCleanedUp = true;
        IsInitialized = false;
    }
}
