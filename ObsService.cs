using System.Diagnostics;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;

namespace DynamicIsland;

sealed class ObsService
{
    public enum Link { Closed, Disabled, Refused, Ready }

    enum Op { Hello = 0, Identify = 1, Identified = 2, Event = 5, Request = 6, RequestResponse = 7 }

    const int DefaultPort = 4455;
    const int RpcVersion = 1;
    const int GeneralEvents = 1 << 0, SceneEvents = 1 << 2, OutputEvents = 1 << 6;
    const int AuthenticationFailed = 4009;
    const int ConnectEveryTicks = 3;
    const int ReceiveChunk = 16 * 1024;
    static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);
    static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(2);
    static readonly string[] ProcessNames = ["obs64", "obs"];
    static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obs-studio", "plugin_config", "obs-websocket", "config.json");
    static readonly JsonSerializerOptions Wire = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    readonly DispatcherTimer _poll = new() { Interval = PollInterval };
    readonly Dictionary<string, TaskCompletionSource<JsonElement?>> _pending = [];
    readonly SemaphoreSlim _sending = new(1, 1);
    readonly Stopwatch _sinceStatus = new();
    ClientWebSocket? _socket;
    string? _refusedPassword, _triedPassword, _recordFile;
    TimeSpan _statusDuration;
    bool _connecting;
    int _ticks, _requests;

    public ObsService()
    {
        _poll.Tick += (_, _) => OnPoll();
    }

    public event Action? Changed;
    public event Action<string, TimeSpan>? Saved;

    public Link State { get; private set; }
    public bool Recording { get; private set; }
    public bool Paused { get; private set; }
    public bool Busy { get; private set; }
    public string Scene { get; private set; } = "";
    public long Bytes { get; private set; }

    public bool IsReady => State == Link.Ready;

    public TimeSpan Elapsed => Recording && !Paused ? _statusDuration + _sinceStatus.Elapsed : _statusDuration;

    public void Start()
    {
        _poll.Start();
        _ = ConnectAsync();
    }

    public async Task StartRecordAsync()
    {
        if (Recording || Busy) return;
        SetBusy(true);
        if (await RequestAsync("StartRecord") == null) SetBusy(false);
    }

    public async Task StopRecordAsync()
    {
        if (!Recording || Busy) return;
        SetBusy(true);
        if (await RequestAsync("StopRecord") == null) SetBusy(false);
    }

    public async Task TogglePauseAsync()
    {
        if (!Recording || Busy) return;
        await RequestAsync(Paused ? "ResumeRecord" : "PauseRecord");
    }

    void OnPoll()
    {
        _ticks++;
        if (IsReady)
        {
            if (Recording) _ = RefreshRecordAsync();
            return;
        }
        if (_ticks % ConnectEveryTicks == 0) _ = ConnectAsync();
    }

    static bool IsObsRunning() => ProcessNames.Any(name =>
    {
        Process[] found = Process.GetProcessesByName(name);
        foreach (Process process in found) process.Dispose();
        return found.Length > 0;
    });

    async Task ConnectAsync()
    {
        if (_connecting || _socket != null) return;
        _connecting = true;
        try
        {
            if (!await Task.Run(IsObsRunning))
            {
                SetState(Link.Closed);
                return;
            }
            (bool enabled, int port, string password) = await Task.Run(ReadConfig);
            if (!enabled)
            {
                SetState(Link.Disabled);
                return;
            }
            if (password == _refusedPassword)
            {
                SetState(Link.Refused);
                return;
            }

            var socket = new ClientWebSocket();
            using (var timeout = new CancellationTokenSource(ConnectTimeout))
            {
                try { await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), timeout.Token); }
                catch
                {
                    socket.Dispose();
                    SetState(Link.Closed);
                    return;
                }
            }
            _socket = socket;
            _triedPassword = password;
            _ = ListenAsync(socket, password);
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
        finally
        {
            _connecting = false;
        }
    }

    static (bool Enabled, int Port, string Password) ReadConfig()
    {
        try
        {
            using JsonDocument config = JsonDocument.Parse(File.ReadAllText(ConfigPath));
            JsonElement root = config.RootElement;
            bool enabled = !root.TryGetProperty("server_enabled", out JsonElement on) || on.ValueKind != JsonValueKind.False;
            int port = root.TryGetProperty("server_port", out JsonElement p) && p.TryGetInt32(out int value) ? value : DefaultPort;
            bool locked = root.TryGetProperty("auth_required", out JsonElement auth) && auth.ValueKind == JsonValueKind.True;
            string password = locked && root.TryGetProperty("server_password", out JsonElement secret) ? secret.GetString() ?? "" : "";
            return (enabled, port, password);
        }
        catch
        {
            return (true, DefaultPort, "");
        }
    }

    async Task ListenAsync(ClientWebSocket socket, string password)
    {
        var buffer = new byte[ReceiveChunk];
        using var message = new MemoryStream();
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult part = await socket.ReceiveAsync(buffer, CancellationToken.None);
                if (part.MessageType == WebSocketMessageType.Close) break;
                message.Write(buffer, 0, part.Count);
                if (!part.EndOfMessage) continue;

                JsonElement received;
                using (JsonDocument document = JsonDocument.Parse(message.ToArray())) received = document.RootElement.Clone();
                message.SetLength(0);
                await HandleAsync(socket, received, password);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or JsonException)
        {
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
        Drop(socket);
    }

    async Task HandleAsync(ClientWebSocket socket, JsonElement message, string password)
    {
        JsonElement data = message.GetProperty("d");
        switch ((Op)message.GetProperty("op").GetInt32())
        {
            case Op.Hello:
                string? proof = data.TryGetProperty("authentication", out JsonElement auth)
                    ? Prove(password, auth.GetProperty("salt").GetString()!, auth.GetProperty("challenge").GetString()!)
                    : null;
                await SendAsync(socket, Op.Identify, new
                {
                    rpcVersion = RpcVersion,
                    authentication = proof,
                    eventSubscriptions = GeneralEvents | SceneEvents | OutputEvents,
                });
                break;
            case Op.Identified:
                SetState(Link.Ready);
                _ = RefreshRecordAsync();
                _ = RefreshSceneAsync();
                break;
            case Op.Event:
                OnEvent(data.GetProperty("eventType").GetString(), data.TryGetProperty("eventData", out JsonElement details) ? details : default);
                break;
            case Op.RequestResponse:
                if (!_pending.Remove(data.GetProperty("requestId").GetString()!, out TaskCompletionSource<JsonElement?>? reply)) break;
                bool done = data.GetProperty("requestStatus").GetProperty("result").GetBoolean();
                reply.SetResult(done && data.TryGetProperty("responseData", out JsonElement response) ? response : done ? default(JsonElement) : null);
                break;
        }
    }

    static string Prove(string password, string salt, string challenge)
    {
        string secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    void OnEvent(string? type, JsonElement data)
    {
        switch (type)
        {
            case "RecordStateChanged":
                OnRecordState(data.GetProperty("outputState").GetString(), data.TryGetProperty("outputPath", out JsonElement path) ? path.GetString() : null);
                break;
            case "RecordFileChanged":
                _recordFile = data.TryGetProperty("newOutputPath", out JsonElement next) ? next.GetString() : null;
                break;
            case "CurrentProgramSceneChanged":
                Scene = data.GetProperty("sceneName").GetString() ?? "";
                Changed?.Invoke();
                break;
            case "ExitStarted":
                if (_socket is { } socket) Drop(socket);
                break;
        }
    }

    void OnRecordState(string? state, string? path)
    {
        switch (state)
        {
            case "OBS_WEBSOCKET_OUTPUT_STARTING":
            case "OBS_WEBSOCKET_OUTPUT_STOPPING":
                Busy = true;
                break;
            case "OBS_WEBSOCKET_OUTPUT_STARTED":
                Busy = Paused = false;
                Recording = true;
                Bytes = 0;
                _recordFile = path;
                MarkStatus(TimeSpan.Zero);
                break;
            case "OBS_WEBSOCKET_OUTPUT_PAUSED":
                MarkStatus(Elapsed);
                Paused = true;
                break;
            case "OBS_WEBSOCKET_OUTPUT_RESUMED":
                MarkStatus(Elapsed);
                Paused = false;
                break;
            case "OBS_WEBSOCKET_OUTPUT_STOPPED":
                TimeSpan length = Elapsed;
                Busy = Recording = Paused = false;
                Bytes = 0;
                _recordFile = null;
                MarkStatus(TimeSpan.Zero);
                Changed?.Invoke();
                if (!string.IsNullOrEmpty(path)) Saved?.Invoke(path, length);
                return;
        }
        Changed?.Invoke();
        if (Recording) _ = RefreshRecordAsync();
    }

    async Task RefreshRecordAsync()
    {
        if (await RequestAsync("GetRecordStatus") is not { ValueKind: JsonValueKind.Object } status) return;
        Recording = status.GetProperty("outputActive").GetBoolean();
        Paused = Recording && status.TryGetProperty("outputPaused", out JsonElement paused) && paused.GetBoolean();
        MarkStatus(Recording && status.TryGetProperty("outputDuration", out JsonElement ms) && ms.TryGetDouble(out double length)
            ? TimeSpan.FromMilliseconds(length)
            : TimeSpan.Zero);
        if (!Recording) _recordFile = null;
        else if (_recordFile == null && await RequestAsync("GetRecordDirectory") is { ValueKind: JsonValueKind.Object } folder
            && folder.TryGetProperty("recordDirectory", out JsonElement directory) && directory.GetString() is { } path)
            _recordFile = await Task.Run(() => FindNewestFile(path));
        Bytes = Recording && _recordFile is { } file ? await Task.Run(() => MeasureFile(file)) : 0;
        Changed?.Invoke();
    }

    static string? FindNewestFile(string directory)
    {
        try { return new DirectoryInfo(directory).EnumerateFiles().MaxBy(file => file.CreationTimeUtc)?.FullName; }
        catch { return null; }
    }

    static long MeasureFile(string path)
    {
        try
        {
            using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return RandomAccess.GetLength(handle);
        }
        catch
        {
            return 0;
        }
    }

    async Task RefreshSceneAsync()
    {
        if (await RequestAsync("GetCurrentProgramScene") is not { ValueKind: JsonValueKind.Object } scene) return;
        Scene = (scene.TryGetProperty("sceneName", out JsonElement name) ? name : scene.GetProperty("currentProgramSceneName")).GetString() ?? "";
        Changed?.Invoke();
    }

    void MarkStatus(TimeSpan duration)
    {
        _statusDuration = duration;
        _sinceStatus.Restart();
    }

    void SetBusy(bool busy)
    {
        Busy = busy;
        Changed?.Invoke();
    }

    async Task<JsonElement?> RequestAsync(string type)
    {
        if (_socket is not { State: WebSocketState.Open } socket || !IsReady) return null;
        string id = (++_requests).ToString();
        var reply = new TaskCompletionSource<JsonElement?>();
        _pending[id] = reply;
        try { await SendAsync(socket, Op.Request, new { requestType = type, requestId = id }); }
        catch
        {
            _pending.Remove(id);
            return null;
        }
        return await reply.Task;
    }

    async Task SendAsync(ClientWebSocket socket, Op op, object data)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(new { op = (int)op, d = data }, Wire);
        await _sending.WaitAsync();
        try { await socket.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None); }
        finally { _sending.Release(); }
    }

    void Drop(ClientWebSocket socket)
    {
        if (_socket != socket) return;
        _socket = null;
        bool refused = socket.CloseStatus == (WebSocketCloseStatus)AuthenticationFailed;
        _refusedPassword = refused ? _triedPassword : null;
        socket.Abort();
        socket.Dispose();
        foreach (TaskCompletionSource<JsonElement?> reply in _pending.Values) reply.TrySetResult(null);
        _pending.Clear();
        Recording = Paused = Busy = false;
        Bytes = 0;
        _recordFile = null;
        MarkStatus(TimeSpan.Zero);
        SetState(refused ? Link.Refused : Link.Closed, true);
    }

    void SetState(Link state, bool force = false)
    {
        if (State == state && !force) return;
        State = state;
        Changed?.Invoke();
    }
}
