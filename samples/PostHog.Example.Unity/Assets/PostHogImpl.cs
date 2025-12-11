using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PostHog;
using UnityEngine;

public class PostHogImpl : MonoBehaviour
{
    [Header("PostHog Configuration")]
    [SerializeField] private string projectApiKey = "<your-project-api-key>";
    [SerializeField] private string hostUrl = "https://us.i.posthog.com";

    private PostHogClient _client = null!;
    private string _distinctId = null!;

    public static PostHogImpl Instance { get; private set; }

    public IPostHogClient Client => _client;

    public string DistinctId => _distinctId;

    private void Awake()
    {
        // Singleton pattern
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        InitializePostHog();
    }

    private void InitializePostHog()
    {
        // Get or create a distinct ID for this user
        _distinctId = GetOrCreateDistinctId();

        // Initialize the PostHog client
        _client = new PostHogClient(new PostHogOptions
        {
            ProjectApiKey = projectApiKey,
            HostUrl = new Uri(hostUrl)
        });

        Debug.Log($"[PostHog] Initialized with distinct ID: {_distinctId}");
    }

    private string GetOrCreateDistinctId()
    {
        const string key = "posthog_distinct_id";

        if (PlayerPrefs.HasKey(key))
        {
            return PlayerPrefs.GetString(key);
        }

        var newId = Guid.NewGuid().ToString();
        PlayerPrefs.SetString(key, newId);
        PlayerPrefs.Save();
        return newId;
    }
    
    public void Capture(string eventName)
    {
        _client.Capture(_distinctId, eventName);
    }

    public void Capture(string eventName, Dictionary<string, object> properties)
    {
        _client.Capture(_distinctId, eventName, properties);
    }

    public async Task FlushAsync()
    {
        await _client.FlushAsync();
    }

    private async void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            // flush events when app is paused/backgrounded
            await _client.FlushAsync();
        }
    }

    private async void OnApplicationQuit()
    {
        await _client.FlushAsync();
    }

    private async void OnDestroy()
    {
        await _client.DisposeAsync();
    }
}
