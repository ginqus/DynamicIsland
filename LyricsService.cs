using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DynamicIsland;

sealed class LyricsService
{
    public readonly record struct Line(TimeSpan Time, string Text);

    sealed record Candidate(double Duration, double End, Line[] Lines);

    const string SearchUrl = "https://lrclib.net/api/search?";
    const string TitleSeparator = " - ";
    const double DurationTolerance = 4;
    const double EndSlack = 1;
    const double MinTimeline = 1;
    const double RestretchAfter = 0.5;
    const double MinUsualDuration = 30;
    const double MinStretch = 0.5, MaxStretch = 2;
    const int Attempts = 2;
    const int RetryDelayMs = 1500;
    const int MaxMinutes = 24 * 60;

    static readonly Line[] None = [];
    static readonly HttpClient Http = CreateClient();
    const string Rework = @"remix|rmx|sped\s*up|speed\s*up|slowed|reverb|nightcore|hardstyle|phonk|bootleg|mashup|ремикс";

    static readonly Regex Noise = new(
        @"\s*[\(\[][^\)\]]*\b(official|video|audio|lyrics?|visuali[sz]er|remaster(ed)?|hd|hq|4k|mv|feat|ft|prod|edit|mix|version|cover|клип|премьера|"
        + Rework + @")\b[^\)\]]*[\)\]]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex Reworked = new(@"\b(" + Rework + @")\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex ReworkTail = new(@"\s*[-–—+]?\s*\b(" + Rework + @")\b.*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex Pipes = new(@"\s*\|[^|]*\|\s*|\s+\|\s.*$", RegexOptions.Compiled);
    static readonly Regex Channel = new(@"\s*-\s*Topic$|\s*VEVO$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex Stamped = new(@"^((?:\[\d+:\d+(?:\.\d+)?\])+)(.*)$", RegexOptions.Compiled);
    static readonly Regex Stamp = new(@"\[(\d+):(\d+(?:\.\d+)?)\]", RegexOptions.Compiled);

    Candidate[] _candidates = [];
    Line[] _stretched = None;
    double _stretchedFor;
    bool _reworked;
    string _trackKey = "";
    int _requestVersion;

    public event Action? Changed;

    public bool IsPending { get; private set; }

    public void Track(string title, string artist)
    {
        string key = title + "\n" + artist;
        if (key == _trackKey) return;

        _trackKey = key;
        _candidates = [];
        _stretchedFor = 0;
        _reworked = Reworked.IsMatch(title);
        int version = ++_requestVersion;
        IsPending = title.Length > 0;
        if (IsPending) _ = LoadAsync(title, artist, version);
    }

    public Line[] LinesFor(TimeSpan duration)
    {
        double seconds = duration.TotalSeconds;
        if (seconds < MinTimeline) return None;

        Line[] best = None;
        double bestGap = DurationTolerance;
        foreach (Candidate candidate in _candidates)
        {
            if (candidate.End > seconds + EndSlack) continue;
            double gap = Math.Abs(candidate.Duration - seconds);
            if (gap > bestGap || (gap == bestGap && best.Length > 0)) continue;
            best = candidate.Lines;
            bestGap = gap;
        }
        if (best.Length > 0 || !_reworked) return best;

        if (Math.Abs(seconds - _stretchedFor) > RestretchAfter)
        {
            _stretched = Stretch(seconds);
            _stretchedFor = seconds;
        }
        return _stretched;
    }

    public TimeSpan UsualLength => _reworked ? TimeSpan.Zero : TimeSpan.FromSeconds(UsualCandidate()?.Duration ?? 0);

    Candidate? UsualCandidate() => _candidates.Where(c => c.Duration >= MinUsualDuration && c.End <= c.Duration + EndSlack)
        .GroupBy(c => Math.Round(c.Duration)).OrderByDescending(g => g.Count()).FirstOrDefault()?.First();

    Line[] Stretch(double seconds)
    {
        Candidate? usual = UsualCandidate();
        if (usual == null) return None;

        double ratio = seconds / usual.Duration;
        if (ratio is < MinStretch or > MaxStretch) return None;
        return Array.ConvertAll(usual.Lines, line => new Line(line.Time * ratio, line.Text));
    }

    async Task LoadAsync(string title, string artist, int version)
    {
        Candidate[] found = [];
        try { found = await Task.Run(() => FetchAsync(title, artist)); }
        catch { }
        if (version != _requestVersion) return;

        _candidates = found;
        _stretchedFor = 0;
        IsPending = false;
        Changed?.Invoke();
    }

    static async Task<Candidate[]> FetchAsync(string title, string artist)
    {
        foreach (string query in Queries(title, artist))
        {
            using JsonDocument? json = await GetAsync(SearchUrl + query);
            if (json == null || json.RootElement.ValueKind != JsonValueKind.Array) continue;

            Candidate[] found = json.RootElement.EnumerateArray().Select(ToCandidate).OfType<Candidate>().ToArray();
            if (found.Length > 0) return found;
        }
        return [];
    }

    static Candidate? ToCandidate(JsonElement item)
    {
        if (!item.TryGetProperty("syncedLyrics", out JsonElement synced) || synced.ValueKind != JsonValueKind.String) return null;
        if (!item.TryGetProperty("duration", out JsonElement duration) || duration.ValueKind != JsonValueKind.Number) return null;
        Line[] lines = Parse(synced.GetString()!);
        if (lines.Length == 0) return null;
        double end = lines.LastOrDefault(line => line.Text.Length > 0).Time.TotalSeconds;
        return new Candidate(duration.GetDouble(), end, lines);
    }

    static async Task<JsonDocument?> GetAsync(string url)
    {
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            try
            {
                using var stream = await Http.GetStreamAsync(url);
                return await JsonDocument.ParseAsync(stream);
            }
            catch (HttpRequestException)
            {
                if (attempt + 1 < Attempts) await Task.Delay(RetryDelayMs);
            }
            catch (Exception ex) when (ex is TaskCanceledException or JsonException)
            {
                return null;
            }
        }
        return null;
    }

    static IEnumerable<string> Queries(string title, string artist)
    {
        string song = ReworkTail.Replace(Pipes.Replace(Noise.Replace(title, ""), " "), "").Replace('—', '-').Replace('–', '-').Trim();
        string by = Channel.Replace(artist, "").Trim();
        if (song.Length == 0) song = title;

        if (by.Length > 0) yield return $"track_name={Uri.EscapeDataString(song)}&artist_name={Uri.EscapeDataString(by)}";

        int dash = song.IndexOf(TitleSeparator, StringComparison.Ordinal);
        if (dash > 0)
        {
            yield return $"track_name={Uri.EscapeDataString(song[(dash + TitleSeparator.Length)..])}&artist_name={Uri.EscapeDataString(song[..dash])}";
            yield return "q=" + Uri.EscapeDataString(song.Replace(TitleSeparator, " "));
        }
        else
        {
            yield return "q=" + Uri.EscapeDataString(by.Length > 0 ? by + " " + song : song);
        }
    }

    static Line[] Parse(string lrc)
    {
        var lines = new List<Line>();
        foreach (string raw in lrc.Split('\n'))
        {
            Match stamped = Stamped.Match(raw.Trim());
            if (!stamped.Success) continue;

            string text = stamped.Groups[2].Value.Trim();
            foreach (Match stamp in Stamp.Matches(stamped.Groups[1].Value))
            {
                if (!int.TryParse(stamp.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int minutes)
                    || !double.TryParse(stamp.Groups[2].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double rest)
                    || minutes > MaxMinutes) continue;
                lines.Add(new Line(TimeSpan.FromSeconds(minutes * 60 + rest), text));
            }
        }
        lines.Sort((a, b) => a.Time.CompareTo(b.Time));
        return lines.ToArray();
    }

    static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DynamicIsland/1.0");
        return http;
    }
}
