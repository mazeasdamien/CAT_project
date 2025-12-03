using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro; // <--- REQUIRED NAMESPACE

public class InGameConsoleLogger : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text LogText; // <--- Changed from Text to TMP_Text
    public ScrollRect ScrollRect;

    [Header("Settings")]
    public bool ShowStackTrace = false;
    public int MaxLogLines = 50;
    public List<string> FilterKeywords = new List<string>();

    private ConcurrentQueue<string> _logQueue = new ConcurrentQueue<string>();
    private StringBuilder _finalText = new StringBuilder();
    private List<string> _keptLines = new List<string>();

    void OnEnable() { Application.logMessageReceivedThreaded += HandleLog; }
    void OnDisable() { Application.logMessageReceivedThreaded -= HandleLog; }

    void HandleLog(string logString, string stackTrace, LogType type)
    {
        if (!IsLogRelevant(logString)) return;

        // Colors work the same in TMP
        string color = "white";
        string prefix = "";

        switch (type)
        {
            case LogType.Error:
            case LogType.Exception:
                color = "#FF4444"; // Hex colors often look better in TMP
                prefix = "[ERR] ";
                break;
            case LogType.Warning:
                color = "yellow";
                prefix = "[WARN] ";
                break;
            default:
                color = "#DDDDDD";
                prefix = "[INFO] ";
                break;
        }

        string formattedLog = $"<color={color}>{prefix}{logString}</color>";

        if (ShowStackTrace && (type == LogType.Error || type == LogType.Exception))
        {
            formattedLog += $"\n<size=80%><color=#888888>{stackTrace}</color></size>";
        }

        _logQueue.Enqueue(formattedLog);
    }

    bool IsLogRelevant(string message)
    {
        if (FilterKeywords.Count == 0) return true;
        foreach (var keyword in FilterKeywords)
            if (message.Contains(keyword)) return true;
        return false;
    }

    void Update()
    {
        if (_logQueue.IsEmpty) return;

        while (_logQueue.TryDequeue(out string newLog))
        {
            _keptLines.Add(newLog);
        }

        if (_keptLines.Count > MaxLogLines)
        {
            _keptLines.RemoveRange(0, _keptLines.Count - MaxLogLines);
        }

        _finalText.Clear();
        foreach (var line in _keptLines)
        {
            _finalText.AppendLine(line);
        }

        if (LogText != null)
        {
            LogText.text = _finalText.ToString();

            // Auto-scroll trick for TMP
            if (ScrollRect != null)
            {
                // We wait for the canvas to rebuild the layout before scrolling
                Canvas.ForceUpdateCanvases();
                ScrollRect.verticalNormalizedPosition = 0f;
            }
        }
    }
}