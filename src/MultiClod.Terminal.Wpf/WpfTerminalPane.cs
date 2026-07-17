using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Terminal.Wpf;
using MultiClod.Terminal.Abstractions;

namespace MultiClod.Terminal.Wpf;

/// <summary>
/// Hosts a vendored Microsoft.Terminal.Wpf.TerminalControl behind ITerminalPane. This is the only
/// class in the solution that references both Microsoft.Terminal.Wpf types and our abstraction
/// types - swapping in a future WebView2-backed pane means adding a sibling class here, nowhere
/// else.
/// </summary>
public sealed class WpfTerminalPane : ITerminalPane
{
    // The modern Windows Console "Campbell" 16-color palette, in Win32 COLORREF (0x00BBGGRR) order
    // matching TerminalTheme.ColorTable's expected index layout (0-7 dark, 8-15 bright).
    private static readonly Color[] DefaultColorTable =
    [
        Color.FromRgb(12, 12, 12),
        Color.FromRgb(197, 15, 31),
        Color.FromRgb(19, 161, 14),
        Color.FromRgb(193, 156, 0),
        Color.FromRgb(0, 55, 218),
        Color.FromRgb(136, 23, 152),
        Color.FromRgb(58, 150, 221),
        Color.FromRgb(204, 204, 204),
        Color.FromRgb(118, 118, 118),
        Color.FromRgb(231, 72, 86),
        Color.FromRgb(22, 198, 12),
        Color.FromRgb(249, 241, 165),
        Color.FromRgb(59, 120, 255),
        Color.FromRgb(180, 0, 158),
        Color.FromRgb(97, 214, 214),
        Color.FromRgb(242, 242, 242),
    ];

    private const double TitleBarHeight = 26;
    private static readonly Brush DefaultTitleBarBackground = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));

    private readonly Grid container;
    private readonly Grid titleBar;
    private readonly TerminalControl control;
    private readonly TextBlock titleText;
    private WpfTerminalConnectionAdapter? adapter;
    private TerminalPaneTheme? lastTheme;
    private bool disposed;

    public WpfTerminalPane()
    {
        this.control = new TerminalControl();

        // ApplyTheme is called on every freshly-launched session while its pane is still
        // Visibility.Collapsed (not yet part of a layout pass), so the native terminal hwnd
        // doesn't exist yet and that first SetTheme call has nothing to act on - the pane would
        // otherwise be stuck on whatever hardcoded default color the vendored control's native
        // side ships with until the theme happens to be changed again later. Reapplying once the
        // hwnd is actually built (first time the pane becomes visible) closes that gap.
        this.control.WindowCreated += (_, _) =>
        {
            if (this.lastTheme is { } theme)
            {
                this.ApplyTheme(theme);
            }
        };

        this.titleText = new TextBlock
        {
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        this.titleBar = new Grid { Background = DefaultTitleBarBackground, Height = TitleBarHeight };
        this.titleBar.Children.Add(this.titleText);

        this.container = new Grid();
        this.container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        this.container.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(this.titleBar, 0);
        Grid.SetRow(this.control, 1);
        this.container.Children.Add(this.titleBar);
        this.container.Children.Add(this.control);

        // KeyboardNavigation.DirectionalNavigation/TabNavigation must be set on a containing
        // panel, not on the focused leaf element itself - WPF's navigation search walks up to the
        // nearest ancestor with this property set, so setting it directly on TerminalControl (the
        // element that actually has focus) has no effect. Without this, arrow keys (and Tab) are
        // treated as focus-navigation requests and move Win32 keyboard focus off the hosted native
        // control before the keystroke ever reaches TerminalContainer's WndProc - the terminal then
        // looks like it silently loses focus the moment you press an arrow key.
        KeyboardNavigation.SetDirectionalNavigation(this.container, KeyboardNavigationMode.None);
        KeyboardNavigation.SetTabNavigation(this.container, KeyboardNavigationMode.None);
    }

    public FrameworkElement View => this.container;

    public string Title
    {
        set => this.titleText.Text = value;
    }

    public bool NewlineOnShiftEnter
    {
        get => this.control.NewlineOnShiftEnter;
        set => this.control.NewlineOnShiftEnter = value;
    }

    public void Attach(IPtyConnection connection)
    {
        this.adapter = new WpfTerminalConnectionAdapter(connection);
        this.control.Connection = this.adapter;
    }

    public void ApplyTheme(TerminalPaneTheme theme)
    {
        this.lastTheme = theme;

        var nativeTheme = new TerminalTheme
        {
            DefaultBackground = ToColorRef(theme.Background),
            DefaultForeground = ToColorRef(theme.Foreground),
            DefaultSelectionBackground = ToColorRef(theme.SelectionBackground),
            CursorStyle = CursorStyle.BlinkingBlockDefault,
            ColorTable = [.. DefaultColorTable.Select(ToColorRef)],
        };

        // The title bar strip sits directly above the terminal's own background and was, until
        // now, hardcoded to the original dark theme's window color - so it visibly failed to
        // follow along whenever a different theme (e.g. the low-contrast dark theme) changed the
        // terminal's background underneath it.
        this.titleBar.Background = new SolidColorBrush(theme.Background);

        this.control.SetTheme(nativeTheme, theme.FontFamily, theme.FontSize, theme.Background);

        // SetTheme alone only affects cells written after this call - already-rendered scrollback
        // (e.g. an existing session's whole prior conversation) keeps its old baked-in colors
        // until something forces a full repaint. A same-size resize is a cheap, safe way to force
        // one - it mirrors what already happens whenever the hosting window itself is resized, and
        // TriggerResize is a no-op (0,0) if the control hasn't been laid out yet, so this is also
        // harmless when ApplyTheme runs on a brand-new pane at launch, before Attach.
        this.control.TriggerResize(this.control.RenderSize);
    }

    public void Focus()
    {
        this.control.Focus();
    }

    public void Dispose()
    {
        if (this.disposed)
        {
            return;
        }

        this.disposed = true;
        this.adapter?.Close();
    }

    // TerminalTheme's colors are Win32 COLORREF (0x00BBGGRR) - the reverse byte order of a typical
    // 0xRRGGBB value, per the vendored TerminalControl.xaml.cs's own DefaultBackground handling.
    private static uint ToColorRef(Color color)
    {
        return (uint)(color.R | (color.G << 8) | (color.B << 16));
    }
}
