using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Windows.Devices.Geolocation;

namespace DynamicIsland;

enum Sky
{
    Clear,
    Cloudy,
    Overcast,
    Fog,
    Rain,
    Snow,
    Storm,
}

sealed record Place(string Name, string Region, double Latitude, double Longitude);

sealed class WeatherService
{
    public enum Stage { Off, Loading, Ready, NoPlace, Failed }

    public readonly record struct Hour(int At, int Degrees, Sky Sky, bool Night);

    public sealed record Report(int Degrees, int Feels, int Wind, Sky Sky, bool Night, Hour[] Hours);

    public const int ShownHours = 5;

    const string ForecastUrl = "https://api.open-meteo.com/v1/forecast"
        + "?current=temperature_2m,apparent_temperature,weather_code,wind_speed_10m,is_day"
        + "&hourly=temperature_2m,weather_code,is_day&forecast_hours=6&wind_speed_unit=ms&timezone=auto";
    const string SearchUrl = "https://geocoding-api.open-meteo.com/v1/search?language=ru&format=json";
    const int FoundPlaces = 5, AskedPlaces = 15;
    const uint PlaceAccuracyMeters = 5000;
    const int PlaceDigits = 2;
    static readonly TimeSpan ReadEvery = TimeSpan.FromMinutes(15);
    static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(1);
    static readonly TimeSpan StaleAfter = TimeSpan.FromHours(2);
    static readonly TimeSpan PlaceAge = TimeSpan.FromHours(1);
    static readonly TimeSpan PlaceTimeout = TimeSpan.FromSeconds(20);
    static readonly HttpClient Http = CreateClient();

    Geolocator? _locator;
    DateTime _readAgainAt, _reportAt;
    bool _reading;
    int _moves;

    public event Action? Changed;

    public Stage State { get; private set; } = Settings.Weather ? Stage.Loading : Stage.Off;

    public Report? Now { get; private set; }

    public async Task RefreshAsync(bool force = false)
    {
        if (!Settings.Weather)
        {
            if (State != Stage.Off) Set(Stage.Off);
            return;
        }
        if (_reading || !force && DateTime.UtcNow < _readAgainAt) return;

        _reading = true;
        if (Now == null) Set(Stage.Loading);
        Stage reached;
        int moves;
        do
        {
            moves = _moves;
            reached = await TryReadAsync();
        }
        while (moves != _moves && Settings.Weather);
        _reading = false;

        _readAgainAt = DateTime.UtcNow + (reached == Stage.Ready ? ReadEvery : RetryAfter);
        bool fresh = Now != null && DateTime.UtcNow - _reportAt < StaleAfter;
        Set(!Settings.Weather ? Stage.Off : fresh ? Stage.Ready : reached);
    }

    public void Relocate()
    {
        _moves++;
        Set(Settings.Weather ? Stage.Loading : Stage.Off);
        _ = RefreshAsync(true);
    }

    public static async Task<Place[]> SearchAsync(string query, CancellationToken cancel)
    {
        string url = string.Create(CultureInfo.InvariantCulture, $"{SearchUrl}&count={AskedPlaces}&name={Uri.EscapeDataString(query)}");
        using var stream = await Http.GetStreamAsync(url, cancel);
        using JsonDocument json = await JsonDocument.ParseAsync(stream, cancellationToken: cancel);
        if (!json.RootElement.TryGetProperty("results", out JsonElement found)) return [];

        return found.EnumerateArray().Select(place =>
        {
            string name = Text(place, "name");
            string[] around = new[] { Text(place, "admin1"), Text(place, "country") }
                .Where(part => part.Length > 0 && part != name).Distinct().ToArray();
            return new Place(name, string.Join(", ", around), place.GetProperty("latitude").GetDouble(), place.GetProperty("longitude").GetDouble());
        }).DistinctBy(place => (place.Name, place.Region)).Take(FoundPlaces).ToArray();
    }

    static string Text(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";

    void Set(Stage stage)
    {
        State = stage;
        if (stage != Stage.Ready) Now = null;
        Changed?.Invoke();
    }

    async Task<Stage> TryReadAsync()
    {
        try
        {
            return await ReadAsync();
        }
        catch (Exception ex)
        {
            if (ex is not (HttpRequestException or TaskCanceledException)) App.Log(ex);
            return Stage.Failed;
        }
    }

    async Task<Stage> ReadAsync()
    {
        int moves = _moves;
        if (await LocateAsync() is not { } place) return Stage.NoPlace;

        string url = string.Create(CultureInfo.InvariantCulture,
            $"{ForecastUrl}&latitude={Math.Round(place.Latitude, PlaceDigits)}&longitude={Math.Round(place.Longitude, PlaceDigits)}");
        using var stream = await Http.GetStreamAsync(url);
        using JsonDocument json = await JsonDocument.ParseAsync(stream);
        if (moves != _moves) return Stage.Loading;

        Now = Parse(json.RootElement);
        _reportAt = DateTime.UtcNow;
        return Stage.Ready;
    }

    async Task<BasicGeoposition?> LocateAsync()
    {
        if (Settings.Place is { } chosen) return new BasicGeoposition { Latitude = chosen.Latitude, Longitude = chosen.Longitude };

        try
        {
            if (await Geolocator.RequestAccessAsync() != GeolocationAccessStatus.Allowed) return null;
            _locator ??= new Geolocator { DesiredAccuracyInMeters = PlaceAccuracyMeters };
            Geoposition found = await _locator.GetGeopositionAsync(PlaceAge, PlaceTimeout);
            return found.Coordinate.Point.Position;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    static Report Parse(JsonElement root)
    {
        JsonElement now = root.GetProperty("current"), hourly = root.GetProperty("hourly");
        JsonElement times = hourly.GetProperty("time"), degrees = hourly.GetProperty("temperature_2m");
        JsonElement codes = hourly.GetProperty("weather_code"), days = hourly.GetProperty("is_day");

        var hours = new List<Hour>();
        for (int i = 1; i < times.GetArrayLength() && hours.Count < ShownHours; i++)
        {
            int at = DateTime.Parse(times[i].GetString()!, CultureInfo.InvariantCulture).Hour;
            hours.Add(new Hour(at, Whole(degrees[i]), SkyOf(codes[i].GetInt32()), days[i].GetInt32() == 0));
        }
        return new Report(Whole(now.GetProperty("temperature_2m")), Whole(now.GetProperty("apparent_temperature")),
            Whole(now.GetProperty("wind_speed_10m")), SkyOf(now.GetProperty("weather_code").GetInt32()),
            now.GetProperty("is_day").GetInt32() == 0, hours.ToArray());
    }

    static int Whole(JsonElement number) => (int)Math.Round(number.GetDouble(), MidpointRounding.AwayFromZero);

    static Sky SkyOf(int code) => code switch
    {
        0 or 1 => Sky.Clear,
        2 => Sky.Cloudy,
        3 => Sky.Overcast,
        45 or 48 => Sky.Fog,
        >= 71 and <= 77 or 85 or 86 => Sky.Snow,
        >= 95 => Sky.Storm,
        _ => Sky.Rain,
    };

    static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DynamicIsland/1.0");
        return http;
    }
}
