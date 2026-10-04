using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DynamicIsland;

/// <summary>Time-synced lyrics for the current track, from LRCLIB (lrclib.net).</summary>
sealed class LyricsService
{
    public readonly record struct Line(TimeSpan Time, string Text);

    /// <param name="End">When the last sung line starts, in seconds.</param>
    sealed record Candidate(double Duration, double End, Line[] Lines);

    // a version of the song that is longer or shorter than this is timed differently
    const double Tolerance = 4;

    static readonly Line[] None = [];
    static readonly HttpClient Http = CreateClient();
    // marks of a reworked take: LRCLIB only knows the song itself, so they are dropped from the search
    const string Rework = @"remix|rmx|sped\s*up|speed\s*up|slowed|reverb|nightcore|hardstyle|phonk|bootleg|mashup|ремикс";

    static readonly Regex Noise = new(
        @"\s*[\(\[][^\)\]]*\b(official|video|audio|lyrics?|visuali[sz]er|remaster(ed)?|hd|hq|4k|mv|feat|ft|prod|edit|mix|version|cover|клип|премьера|"
        + Rework + @")\b[^\)\]]*[\)\]]",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex Reworked = new(@"\b(" + Rework + @")\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex ReworkTail = new(@"\s*[-–—+]?\s*\b(" + Rework + @")\b.*$", RegexOptions.IgnoreCase | RegexOptions.Compiled); // "Song slowed + reverb"
    static readonly Regex Pipes = new(@"\s*\|[^|]*\|\s*|\s+\|\s.*$", RegexOptions.Compiled); // "Song |remix|", "Song | Channel"
    static readonly Regex Channel = new(@"\s*-\s*Topic$|\s*VEVO$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex Stamped = new(@"^((?:\[\d+:\d+(?:\.\d+)?\])+)(.*)$", RegexOptions.Compiled);
    static readonly Regex Stamp = new(@"\[(\d+):(\d+(?:\.\d+)?)\]", RegexOptions.Compiled);

    Candidate[] _candidates = [];
    Line[] _stretched = None;
    double _stretchedFor;
    bool _reworked;
    string _key = "";
    int _version;

    /// <summary>Raised on the calling (UI) thread once the search for the track's lyrics has come back, with or without them.</summary>
    public event Action? Changed;

    /// <summary>The search for the track's lyrics has not come back yet.</summary>
    public bool Pending { get; private set; }

    /// <summary>Call from the UI thread whenever the track may have changed.</summary>
    public void Track(string title, string artist)
    {
        string key = title + "\n" + artist;
        if (key == _key) return;

        _key = key;
        _candidates = [];
        _stretchedFor = 0;
        _reworked = Reworked.IsMatch(title);
        int version = ++_version;
        Pending = title.Length > 0;
        if (Pending) _ = LoadAsync(title, artist, version);
    }

    /// <summary>Lines of the version whose length matches the playing one; empty when there is none.</summary>
    public Line[] For(TimeSpan duration)
    {
        double seconds = duration.TotalSeconds;
        if (seconds < 1) return None; // no timeline, nothing to sync against

        Line[] best = None;
        double bestGap = Tolerance;
        foreach (Candidate c in _candidates)
        {
            // LRCLIB is full of entries whose timings were copied from a longer cut (a video with an intro):
            // the stated length matches, yet every line is late. Lines past the end of the track give them away.
            if (c.End > seconds + 1) continue;
            double gap = Math.Abs(c.Duration - seconds);
            if (gap > bestGap || (gap == bestGap && best.Length > 0)) continue;
            best = c.Lines;
            bestGap = gap;
        }
        if (best.Length > 0 || !_reworked) return best;

        // a remix, sped up, slowed...: nobody timed that take, so the usual one is stretched over its length
        if (Math.Abs(seconds - _stretchedFor) > 0.5)
        {
            _stretched = Stretch(seconds);
            _stretchedFor = seconds;
        }
        return _stretched;
    }

    Line[] Stretch(double seconds)
    {
        // the usual version is the length most entries agree on
        Candidate? usual = _candidates.Where(c => c.Duration >= 30 && c.End <= c.Duration + 1)
            .GroupBy(c => Math.Round(c.Duration)).OrderByDescending(g => g.Count()).FirstOrDefault()?.First();
        if (usual == null) return None;

        double ratio = seconds / usual.Duration;
        if (ratio is < 0.5 or > 2) return None;
        return Array.ConvertAll(usual.Lines, l => new Line(l.Time * ratio, l.Text));
    }

    async Task LoadAsync(string title, string artist, int version)
    {
        Candidate[] found = [];
        try
        {
            found = await Task.Run(() => FetchAsync(title, artist));
        }
        catch
        {
            // offline or LRCLIB is down: the island simply shows no lyrics
        }
        if (version != _version) return;

        _candidates = found;
        _stretchedFor = 0;
        Pending = false;
        Changed?.Invoke();
    }

    static async Task<Candidate[]> FetchAsync(string title, string artist)
    {
        foreach (string query in Queries(title, artist))
        {
            using JsonDocument? json = await GetAsync("https://lrclib.net/api/search?" + query);
            if (json == null || json.RootElement.ValueKind != JsonValueKind.Array) continue;

            var found = new List<Candidate>();
            foreach (JsonElement item in json.RootElement.EnumerateArray())
            {
                if (!item.TryGetProperty("syncedLyrics", out JsonElement synced) || synced.ValueKind != JsonValueKind.String) continue;
                if (!item.TryGetProperty("duration", out JsonElement duration) || duration.ValueKind != JsonValueKind.Number) continue;
                Line[] lines = Parse(synced.GetString()!);
                if (lines.Length == 0) continue;
                double end = lines.LastOrDefault(l => l.Text.Length > 0).Time.TotalSeconds;
                found.Add(new Candidate(duration.GetDouble(), end, lines));
            }
            if (found.Count > 0) return found.ToArray();
        }
        return [];
    }

    static async Task<JsonDocument?> GetAsync(string url)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                using var stream = await Http.GetStreamAsync(url);
                return await JsonDocument.ParseAsync(stream);
            }
            catch (HttpRequestException)
            {
                // LRCLIB answers 503 whenever it is busy; one more try is usually enough
                if (attempt == 0) await Task.Delay(1500);
            }
            catch (Exception ex) when (ex is TaskCanceledException or JsonException)
            {
                // timed out, or the answer is not JSON: this query is given up, the next way of asking may still find it
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

        // browser tabs: the title is "Artist - Song" and the artist is just the channel name
        int dash = song.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0)
        {
            yield return $"track_name={Uri.EscapeDataString(song[(dash + 3)..])}&artist_name={Uri.EscapeDataString(song[..dash])}";
            yield return "q=" + Uri.EscapeDataString(song.Replace(" - ", " "));
        }
        else
        {
            yield return "q=" + Uri.EscapeDataString(by.Length > 0 ? by + " " + song : song);
        }
    }

    /// <summary>LRC: "[mm:ss.xx] text", possibly with several stamps in front of one line.</summary>
    static Line[] Parse(string lrc)
    {
        var lines = new List<Line>();
        foreach (string raw in lrc.Split('\n'))
        {
            Match m = Stamped.Match(raw.Trim());
            if (!m.Success) continue;

            string text = m.Groups[2].Value.Trim();
            foreach (Match stamp in Stamp.Matches(m.Groups[1].Value))
            {
                // a stamp past what a number holds is a broken one: it is skipped, not the whole entry
                if (!int.TryParse(stamp.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int minutes)
                    || !double.TryParse(stamp.Groups[2].Value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double rest)
                    || minutes > 24 * 60) continue;
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
