using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MultiClod.App.Splash;

/// <summary>
/// Interaction logic for SplashWindow.xaml. Always constructed and shown on its own dedicated
/// thread - see <see cref="StartupSplash"/> - never on the main application thread.
/// </summary>
public partial class SplashWindow : Window {
	public SplashWindow() {
		this.InitializeComponent();

		// Animated via BeginAnimation rather than an XAML Storyboard/trigger - this window only
		// ever needs the one animation, running continuously for its whole (short) lifetime.
		var spin = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.6)) {
			RepeatBehavior = RepeatBehavior.Forever,
		};
		this.SpinnerRotate.BeginAnimation(RotateTransform.AngleProperty, spin);
	}

	public void SetStatusText(string text) {
		this.StatusText.Text = text;
	}
}
