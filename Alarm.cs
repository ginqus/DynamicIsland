using System.IO;
using System.Media;

namespace DynamicIsland;

sealed class Alarm
{
    SoundPlayer? _player;

    public void Ring()
    {
        Stop();
        try
        {
            string wav = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Alarm01.wav");
            if (!File.Exists(wav))
            {
                SystemSounds.Exclamation.Play();
                return;
            }
            _player = new SoundPlayer(wav);
            _player.PlayLooping();
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    public void Stop()
    {
        try
        {
            _player?.Stop();
            _player?.Dispose();
        }
        catch { }
        _player = null;
    }
}
