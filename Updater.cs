using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace DynamicIsland;

sealed class Updater
{
    public enum Stage { Idle, Checking, Latest, Available, Loading, Failed }

    public const string RestartedFlag = "--updated";

    const string LatestReleaseUrl = "https://api.github.com/repos/mihailkotovski/DynamicIsland/releases/latest";
    const string AssetName = "DynamicIsland.exe";
    const string HashPrefix = "sha256:";
    const int MaxNotes = 6;
    const int BufferSize = 1 << 16;
    const int CleanUpAttempts = 10;
    static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(30);
    static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(15);
    static readonly TimeSpan CleanUpRetry = TimeSpan.FromSeconds(1);
    static readonly HttpClient Http;

    static Updater()
    {
        CurrentVersion = Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));
        Http = CreateClient(CurrentVersion);
    }

    string _downloadUrl = "", _digest = "";
    DateTime _checkedAt;

    public static Version CurrentVersion { get; }

    public event Action? Changed;

    public Stage State { get; private set; }

    public Version? LatestVersion { get; private set; }

    public string[] Notes { get; private set; } = [];

    public int Percent { get; private set; }

    public long DownloadedBytes { get; private set; }

    public long TotalBytes { get; private set; }

    static string ExePath => Environment.ProcessPath ?? "";
    static string OldPath => ExePath + ".old";
    static string DownloadPath => ExePath + ".new";

    public async Task CheckAsync(bool force = false)
    {
        if (State is Stage.Checking or Stage.Loading) return;
        if (!force && State != Stage.Failed && DateTime.UtcNow - _checkedAt < CheckInterval) return;

        SetState(Stage.Checking);
        try
        {
            using var timeout = new CancellationTokenSource(CheckTimeout);
            using var document = JsonDocument.Parse(await Http.GetStringAsync(LatestReleaseUrl, timeout.Token));
            JsonElement release = document.RootElement;
            LatestVersion = Normalize(Version.Parse(release.GetProperty("tag_name").GetString()!.TrimStart('v')));
            Notes = release.TryGetProperty("body", out JsonElement body) ? ParseNotes(body.GetString() ?? "") : [];
            _downloadUrl = _digest = "";
            foreach (JsonElement asset in release.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != AssetName) continue;
                _downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? "";
                if (asset.TryGetProperty("digest", out JsonElement digest)) _digest = digest.GetString() ?? "";
                if (asset.TryGetProperty("size", out JsonElement size)) TotalBytes = size.GetInt64();
            }
            _checkedAt = DateTime.UtcNow;
            SetState(LatestVersion > CurrentVersion && _downloadUrl.Length > 0 ? Stage.Available : Stage.Latest);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            SetState(Stage.Failed);
        }
    }

    public async Task<bool> InstallAsync()
    {
        if (State != Stage.Available) return false;

        Percent = 0;
        DownloadedBytes = 0;
        SetState(Stage.Loading);
        try
        {
            await DownloadAsync();
            ReplaceExe();
            Process.Start(new ProcessStartInfo(ExePath, RestartedFlag) { UseShellExecute = false });
            return true;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            try { File.Delete(DownloadPath); }
            catch { }
            SetState(Stage.Failed);
            return false;
        }
    }

    async Task DownloadAsync()
    {
        using HttpResponseMessage response = await Http.GetAsync(_downloadUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        long total = response.Content.Headers.ContentLength ?? 0, done = 0;
        if (total > 0) TotalBytes = total;

        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using (Stream from = await response.Content.ReadAsStreamAsync())
        await using (var to = new FileStream(DownloadPath, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, true))
        {
            var buffer = new byte[BufferSize];
            int read;
            while ((read = await from.ReadAsync(buffer)) > 0)
            {
                await to.WriteAsync(buffer.AsMemory(0, read));
                sha.AppendData(buffer, 0, read);
                done += read;
                int percent = total > 0 ? (int)(done * 100 / total) : 0;
                if (percent == Percent) continue;
                Percent = percent;
                DownloadedBytes = done;
                Changed?.Invoke();
            }
        }

        if (total > 0 && done != total) throw new IOException($"The update came short: {done} of {total} bytes.");
        string hash = HashPrefix + Convert.ToHexString(sha.GetHashAndReset());
        if (_digest.Length > 0 && !hash.Equals(_digest, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The update does not match the hash GitHub gives for it.");
    }

    static void ReplaceExe()
    {
        File.Move(ExePath, OldPath, true);
        try
        {
            File.Move(DownloadPath, ExePath);
        }
        catch
        {
            File.Move(OldPath, ExePath);
            throw;
        }
    }

    public static void CleanUp() => Task.Run(async () =>
    {
        for (int attempt = 0; attempt < CleanUpAttempts && File.Exists(OldPath); attempt++)
        {
            try { File.Delete(OldPath); }
            catch { await Task.Delay(CleanUpRetry); }
        }
    });

    void SetState(Stage stage)
    {
        State = stage;
        Changed?.Invoke();
    }

    static string[] ParseNotes(string notes) => notes.Split('\n')
        .Select(line => line.Trim())
        .Where(line => line.Length > 2 && line[0] is '-' or '*' or '•' && line[1] == ' ')
        .Select(line => line[2..].Replace("**", "").Replace("`", "").Trim())
        .Where(line => line.Length > 0 && !line.Contains("http", StringComparison.OrdinalIgnoreCase))
        .Take(MaxNotes)
        .ToArray();

    static Version Normalize(Version version) => new(version.Major, version.Minor, Math.Max(version.Build, 0));

    static HttpClient CreateClient(Version current)
    {
        var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DynamicIsland/" + current);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }
}
