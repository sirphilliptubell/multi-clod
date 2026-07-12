using System.IO;
using System.Media;

namespace MultiClod.App;

// The two audible cues for SessionActivity.NeedsInput/Done - see MainWindow's session.PropertyChanged
// subscription in LaunchSession for the focused/visible check that gates whether either actually
// plays. Players are constructed once and reused rather than per-call, since SoundPlayer.Load()
// reads the wav from disk.
internal static class SessionActivitySounds
{
    private static readonly SoundPlayer NeedsInputPlayer = Load("stomp.wav");
    private static readonly SoundPlayer DonePlayer = Load("coin.wav");

    public static void PlayNeedsInput() => NeedsInputPlayer.Play();

    public static void PlayDone() => DonePlayer.Play();

    private static SoundPlayer Load(string fileName)
    {
        var player = new SoundPlayer(Path.Combine(AppContext.BaseDirectory, "Media", fileName));
        player.Load();
        return player;
    }
}
