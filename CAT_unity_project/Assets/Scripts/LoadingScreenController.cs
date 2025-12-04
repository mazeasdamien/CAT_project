using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class LoadingScreenController : MonoBehaviour
{
    [Header("UI References")]
    public GameObject LoadingPanel;
    public TextMeshProUGUI StatusText;

    private void Awake()
    {
        // Ensure loading screen is visible on start
        if (LoadingPanel != null)
        {
            LoadingPanel.SetActive(true);
        }
    }

    public void SetStatus(string status)
    {
        if (StatusText != null)
        {
            StatusText.text = status;
        }
    }

    public void Hide()
    {
        if (LoadingPanel != null)
        {
            LoadingPanel.SetActive(false);
        }
    }

    public void Show()
    {
        if (LoadingPanel != null)
        {
            LoadingPanel.SetActive(true);
        }
    }
}
