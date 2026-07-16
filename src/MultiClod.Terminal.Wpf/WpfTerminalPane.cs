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
    private static readonly Brush TitleBarBackground = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
    private static readonly Brush CloseButtonHoverBackground = new SolidColorBrush(Color.FromRgb(0xC4, 0x2B, 0x1C));

    private readonly Grid container;
    private readonly TerminalControl control;
    private readonly TextBlock titleText;
    private readonly Border closeButton;
    private WpfTerminalConnectionAdapter? adapter;
    private bool disposed;

    public WpfTerminalPane()
    {
        this.control = new TerminalControl();

        this.titleText = new TextBlock
        {
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

        this.closeButton = new Border
        {
            Width = 30,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = new TextBlock
            {
                Text = "✕",
                Foreground = Brushes.White,
                FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };

        // A plain Border+TextBlock instead of a real Button - WPF's default Button chrome has its
        // own IsMouseOver visual trigger that would need a full ControlTemplate override to get a
        // custom hover color, which is overkill for a single glyph.
        this.closeButton.MouseEnter += (_, _) => this.closeButton.Background = CloseButtonHoverBackground;
        this.closeButton.MouseLeave += (_, _) => this.closeButton.Background = Brushes.Transparent;
        this.closeButton.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            this.CloseRequested?.Invoke(this, EventArgs.Empty);
        };

        var titleBar = new Grid { Background = TitleBarBackground, Height = TitleBarHeight };
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(this.titleText, 0);
        Grid.SetColumn(this.closeButton, 1);
        titleBar.Children.Add(this.titleText);
        titleBar.Children.Add(this.closeButton);

        this.container = new Grid();
        this.container.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        this.container.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Grid.SetRow(titleBar, 0);
        Grid.SetRow(this.control, 1);
        this.container.Children.Add(titleBar);
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

    public event EventHandler? CloseRequested;

    public void Attach(IPtyConnection connection)
    {
        this.adapter = new WpfTerminalConnectionAdapter(connection);
        this.control.Connection = this.adapter;
    }

    public void ApplyTheme(TerminalPaneTheme theme)
    {
        var nativeTheme = new TerminalTheme
        {
            DefaultBackground = ToColorRef(theme.Background),
            DefaultForeground = ToColorRef(theme.Foreground),
            DefaultSelectionBackground = ToColorRef(theme.SelectionBackground),
            CursorStyle = CursorStyle.BlinkingBlockDefault,
            ColorTable = [.. DefaultColorTable.Select(ToColorRef)],
        };

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
