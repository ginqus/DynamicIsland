using System.Windows.Media;
using Microsoft.Win32;

namespace DynamicIsland;

/// <summary>
/// The switches of the menu and the looks picked on the page next to them, remembered in the registry between runs.
/// All the switches start on.
/// </summary>
static class Settings
{
    const string Key = @"Software\DynamicIsland";
    const int MinScale = 85, MaxScale = 130, MaxGap = 24;

    static bool _lyrics = Read(nameof(Lyrics)), _lyricEffects = Read(nameof(LyricEffects));
    static bool _network = Read(nameof(Network)), _hideFullscreen = Read(nameof(HideFullscreen));
    static bool _rim = Read(nameof(Rim)), _appVolume = Read(nameof(AppVolume));
    static bool _appSpectrum = Read(nameof(AppSpectrum));
    static int _scale = Math.Clamp(Read(nameof(Scale), 100), MinScale, MaxScale);
    static int _gap = Math.Clamp(Read(nameof(Gap), 8), 0, MaxGap);
    static int _accent = Read(nameof(Accent), 0);

    /// <summary>Look the lyrics of the track up and show them. Off: nothing is sent to LRCLIB.</summary>
    public static bool Lyrics
    {
        get => _lyrics;
        set => Write(nameof(Lyrics), _lyrics = value);
    }

    /// <summary>
    /// In the expanded player the line being sung fills with light and the others sit back smaller and out of focus.
    /// Off: the lines only differ in brightness.
    /// </summary>
    public static bool LyricEffects
    {
        get => _lyricEffects;
        set => Write(nameof(LyricEffects), _lyricEffects = value);
    }

    /// <summary>While music plays, the island's light edge takes the colour of the cover. Off: it stays white.</summary>
    public static bool Rim
    {
        get => _rim;
        set => Write(nameof(Rim), _rim = value);
    }

    /// <summary>Over the open player the wheel turns the app that plays up and down. Off: the whole system, as everywhere else.</summary>
    public static bool AppVolume
    {
        get => _appVolume;
        set => Write(nameof(AppVolume), _appVolume = value);
    }

        /// <summary>
        /// The spectrum follows the app that is playing instead of the whole system.
        /// Off: every sound the device outputs moves the bars.
        /// </summary>
        public static bool AppSpectrum
        {
            get => _appSpectrum;
            set => Write(nameof(AppSpectrum), _appSpectrum = value);
        }

    /// <summary>Notices about Wi-Fi, Ethernet and VPN.</summary>
    public static bool Network
    {
        get => _network;
        set => Write(nameof(Network), _network = value);
    }

    /// <summary>Slide away while a game or a video covers the whole screen.</summary>
    public static bool HideFullscreen
    {
        get => _hideFullscreen;
        set => Write(nameof(HideFullscreen), _hideFullscreen = value);
    }

    /// <summary>Size of the island, in percent of the one it was drawn at.</summary>
    public static int Scale
    {
        get => _scale;
        set => Write(nameof(Scale), _scale = Math.Clamp(value, MinScale, MaxScale));
    }

    /// <summary>Px between the top of the screen and the island.</summary>
    public static int Gap
    {
        get => _gap;
        set => Write(nameof(Gap), _gap = Math.Clamp(value, 0, MaxGap));
    }

    /// <summary>One colour for everything that otherwise takes the colours of the cover; null leaves it to the cover.</summary>
    public static Color? Accent
    {
        get => _accent == 0 ? null : Color.FromRgb((byte)(_accent >> 16), (byte)(_accent >> 8), (byte)_accent);
        set => Write(nameof(Accent), _accent = value is { } c ? c.R << 16 | c.G << 8 | c.B : 0);
    }

    /// <summary>Paths of the files lying on the shelf, in the order they were put there.</summary>
    public static string[] Shelf
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(Key);
                return key?.GetValue(nameof(Shelf)) as string[] ?? [];
            }
            catch
            {
                return [];
            }
        }
        set
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(Key);
                key.SetValue(nameof(Shelf), value, RegistryValueKind.MultiString);
            }
            catch (Exception ex)
            {
                App.Log(ex);
            }
        }
    }

    static bool Read(string name) => Read(name, 1) != 0;

    static int Read(string name, int fallback)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(Key);
            return key?.GetValue(name) is int value ? value : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    static void Write(string name, bool value) => Write(name, value ? 1 : 0);

    static void Write(string name, int value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(Key);
            key.SetValue(name, value, RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            // not saved: the setting still holds until the island is closed
            App.Log(ex);
        }
    }
}
