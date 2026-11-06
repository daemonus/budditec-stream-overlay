using System.Text.Json;

namespace Budditec.Overlay.Core.Obs;

/// <summary>
/// Keeps an obs-websocket connection alive and tracks recording/streaming state, the audio inputs live in
/// the current program scene (plus global audio devices), their mute state and their levels.
/// </summary>
public sealed class ObsMonitor(Uri url, string? password)
{
    private const ObsEventSubscription Subscriptions =
        ObsEventSubscription.General | ObsEventSubscription.Scenes | ObsEventSubscription.Inputs |
        ObsEventSubscription.Outputs | ObsEventSubscription.SceneItems | ObsEventSubscription.InputVolumeMeters;

    private readonly object _gate = new();
    private ObsStatus _status = ObsStatus.Disconnected("Connecting");

    // Audio-capable inputs in the scene, and the inputs actually sending audio to OBS. Guarded by _gate.
    private IReadOnlyList<ObsAudioSource> _candidates = [];
    private HashSet<string> _audible = [];
    private ObsWebSocketClient? _client;
    private CancellationTokenSource? _refreshDebounce;

    public ObsStatus Status
    {
        get { lock (_gate) return _status; }
    }

    /// <summary>Connection, recording, streaming, scene or source list changed.</summary>
    public event Action<ObsStatus>? StatusChanged;

    /// <summary>Levels for every input with audio, about every 50 ms. Raised off the UI thread.</summary>
    public event Action<IReadOnlyDictionary<string, AudioLevel>>? LevelsUpdated;

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var closed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            await using (var client = new ObsWebSocketClient())
            {
                try
                {
                    client.Closed += reason => closed.TrySetResult(reason);
                    client.EventReceived += (type, data) => OnEvent(client, type, data, ct);
                    await client.ConnectAsync(url, password, Subscriptions, ct);
                    _client = client;
                    await LoadAllAsync(client, ct);
                    var reason = await closed.Task.WaitAsync(ct);
                    SetStatus(ObsStatus.Disconnected(reason));
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    SetStatus(ObsStatus.Disconnected(ex.Message));
                }
                catch (OperationCanceledException)
                {
                }
                finally
                {
                    _client = null;
                }
            }
            if (ct.IsCancellationRequested) break;
            await Task.Delay(TimeSpan.FromSeconds(3), ct).ConfigureAwait(false);
        }
    }

    public Task SetMutedAsync(string inputName, bool muted, CancellationToken ct = default) =>
        _client?.RequestAsync("SetInputMute", new { inputName, inputMuted = muted }, ct) ?? Task.CompletedTask;

    private async Task LoadAllAsync(ObsWebSocketClient client, CancellationToken ct)
    {
        var record = await client.RequestAsync("GetRecordStatus", ct: ct);
        var stream = await client.RequestAsync("GetStreamStatus", ct: ct);
        var scene = await client.RequestAsync("GetCurrentProgramScene", ct: ct);
        var sceneName = scene.GetProperty("currentProgramSceneName").GetString();
        var sources = await LoadAudioSourcesAsync(client, sceneName, ct);

        Update(_ => new ObsStatus(
            Connected: true,
            Detail: "Connected",
            Record: !record.GetProperty("outputActive").GetBoolean() ? RecordState.Stopped
                : record.GetProperty("outputPaused").GetBoolean() ? RecordState.Paused
                : RecordState.Recording,
            Streaming: stream.GetProperty("outputActive").GetBoolean(),
            CurrentScene: sceneName,
            AudioSources: SetCandidates(sources)));
    }

    /// <summary>
    /// Call inside Update. Returns the candidates that are sending audio, which is what OBS's mixer lists. Many
    /// video inputs (game/window capture, browser sources) are audio-capable but only send audio when set to
    /// (capture audio, control audio via OBS). obs-websocket v5 can't report that directly, but its volume meter
    /// for an input has no channels until audio arrives.
    /// </summary>
    private IReadOnlyList<ObsAudioSource> SetCandidates(IReadOnlyList<ObsAudioSource> candidates)
    {
        _candidates = candidates;
        return candidates.Where(c => _audible.Contains(c.Name)).ToList();
    }

    private static async Task<IReadOnlyList<ObsAudioSource>> LoadAudioSourcesAsync(
        ObsWebSocketClient client, string? sceneName, CancellationToken ct)
    {
        // Global devices first, as in OBS's audio mixer (which websocket doesn't expose the order of).
        var candidates = new List<string>();
        var special = await client.RequestAsync("GetSpecialInputs", ct: ct);
        foreach (var key in new[] { "desktop1", "desktop2", "mic1", "mic2", "mic3", "mic4" })
            if (special.TryGetProperty(key, out var name) && name.GetString() is { } input)
                candidates.Add(input);

        if (sceneName is not null)
            await CollectSceneInputsAsync(client, sceneName, isGroup: false, candidates, new HashSet<string>(), ct);

        // Inputs without audio reject GetInputMute, which doubles as the "has audio" check.
        var sources = new List<ObsAudioSource>();
        foreach (var name in candidates.Distinct())
        {
            try
            {
                var mute = await client.RequestAsync("GetInputMute", new { inputName = name }, ct);
                sources.Add(new ObsAudioSource(name, mute.GetProperty("inputMuted").GetBoolean()));
            }
            catch (ObsRequestException)
            {
            }
        }
        return sources;
    }

    private static async Task CollectSceneInputsAsync(
        ObsWebSocketClient client, string sceneName, bool isGroup, List<string> inputs, HashSet<string> visited, CancellationToken ct)
    {
        if (!visited.Add(sceneName)) return;
        var list = await client.RequestAsync(isGroup ? "GetGroupSceneItemList" : "GetSceneItemList", new { sceneName }, ct);
        foreach (var item in list.GetProperty("sceneItems").EnumerateArray())
        {
            if (!item.GetProperty("sceneItemEnabled").GetBoolean()) continue;
            var name = item.GetProperty("sourceName").GetString()!;
            var group = item.TryGetProperty("isGroup", out var g) && g.ValueKind == JsonValueKind.True;
            if (group || item.GetProperty("sourceType").GetString() == "OBS_SOURCE_TYPE_SCENE")
                await CollectSceneInputsAsync(client, name, group, inputs, visited, ct);
            else
                inputs.Add(name);
        }
    }

    private void OnEvent(ObsWebSocketClient client, string type, JsonElement data, CancellationToken ct)
    {
        switch (type)
        {
            case "InputVolumeMeters":
                LevelsUpdated?.Invoke(ParseLevels(data));
                var audible = AudibleInputs(data);
                bool changed;
                lock (_gate) changed = !_audible.SetEquals(audible);
                if (changed)
                    Update(s =>
                    {
                        _audible = audible;
                        return s.Connected ? s with { AudioSources = SetCandidates(_candidates) } : s;
                    });
                break;
            case "RecordStateChanged":
                var state = data.GetProperty("outputState").GetString();
                Update(s => s with
                {
                    Record = state switch
                    {
                        "OBS_WEBSOCKET_OUTPUT_PAUSED" => RecordState.Paused,
                        "OBS_WEBSOCKET_OUTPUT_STARTED" or "OBS_WEBSOCKET_OUTPUT_RESUMED" => RecordState.Recording,
                        "OBS_WEBSOCKET_OUTPUT_STOPPED" => RecordState.Stopped,
                        _ => s.Record,
                    },
                });
                break;
            case "StreamStateChanged":
                var active = data.GetProperty("outputActive").GetBoolean();
                Update(s => s with { Streaming = active });
                break;
            case "InputMuteStateChanged":
                var input = data.GetProperty("inputName").GetString();
                var muted = data.GetProperty("inputMuted").GetBoolean();
                Update(s => s with
                {
                    AudioSources = SetCandidates(_candidates.Select(a => a.Name == input ? a with { Muted = muted } : a).ToList()),
                });
                break;
            case "CurrentProgramSceneChanged" or "SceneItemCreated" or "SceneItemRemoved" or "SceneItemEnableStateChanged"
                or "InputCreated" or "InputRemoved" or "InputNameChanged":
                ScheduleRefresh(client, ct);
                break;
        }
    }

    /// <summary>Scene edits arrive in bursts; refresh once they settle.</summary>
    private void ScheduleRefresh(ObsWebSocketClient client, CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Interlocked.Exchange(ref _refreshDebounce, cts)?.Cancel();
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(200, cts.Token);
                var scene = await client.RequestAsync("GetCurrentProgramScene", ct: cts.Token);
                var sceneName = scene.GetProperty("currentProgramSceneName").GetString();
                var sources = await LoadAudioSourcesAsync(client, sceneName, cts.Token);
                Update(s => s with { CurrentScene = sceneName, AudioSources = SetCandidates(sources) });
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObsRequestException or InvalidOperationException)
            {
            }
        }, CancellationToken.None);
    }

    internal static Dictionary<string, AudioLevel> ParseLevels(JsonElement data)
    {
        var levels = new Dictionary<string, AudioLevel>();
        foreach (var input in data.GetProperty("inputs").EnumerateArray())
        {
            double peak = 0, magnitude = 0;
            foreach (var channel in input.GetProperty("inputLevelsMul").EnumerateArray())
            {
                // [magnitude, peak, input peak]
                magnitude = Math.Max(magnitude, channel[0].GetDouble());
                peak = Math.Max(peak, channel[1].GetDouble());
            }
            levels[input.GetProperty("inputName").GetString()!] = new AudioLevel(AudioLevel.ToDb(peak), AudioLevel.ToDb(magnitude));
        }
        return levels;
    }

    /// <summary>Inputs whose meter has channels, i.e. that have sent OBS audio since they became active.</summary>
    internal static HashSet<string> AudibleInputs(JsonElement data) =>
        data.GetProperty("inputs").EnumerateArray()
            .Where(i => i.GetProperty("inputLevelsMul").GetArrayLength() > 0)
            .Select(i => i.GetProperty("inputName").GetString()!)
            .ToHashSet();

    private void Update(Func<ObsStatus, ObsStatus> change)
    {
        ObsStatus next;
        lock (_gate) next = _status = change(_status);
        StatusChanged?.Invoke(next);
    }

    private void SetStatus(ObsStatus status) => Update(_ => status);
}
