// <copyright file="TerminalContainer.cs" company="Microsoft Corporation">
// Copyright (c) Microsoft Corporation.
// Licensed under the MIT license.
// </copyright>

namespace Microsoft.Terminal.Wpf
{
    using System;
    using System.Runtime.InteropServices;
    using System.Windows;
    using System.Windows.Automation.Peers;
    using System.Windows.Input;
    using System.Windows.Interop;
    using System.Windows.Media;

    /// <summary>
    /// The container class that hosts the native hwnd terminal.
    /// </summary>
    /// <remarks>
    /// This class is only left public since xaml cannot work with internal classes.
    /// </remarks>
    public class TerminalContainer : HwndHost
    {
        // The vendored Microsoft.Terminal.Control.dll (see src/Microsoft-Terminal/native/NOTICE.md)
        // exports no keybindings/paste API, so Ctrl+V has to be implemented by hand at this layer;
        // without this, Ctrl+V falls through as the raw control character 0x16, which most shells
        // interpret as "quoted insert" rather than a paste.
        private const ushort VkV = 0x56;
        private const ushort VkC = 0x43;
        private const ushort VkZ = 0x5A;

        private const ushort VkReturn = 0x0D;

        // Standard "bracketed paste" markers (xterm convention) - see PasteFromClipboard's remarks.
        private const string BracketedPasteStart = "\x1b[200~";
        private const string BracketedPasteEnd = "\x1b[201~";

        // What a real Ctrl+_ sends - see RemapCtrlZForUndo's WM_KEYDOWN case.
        private static readonly string UnitSeparator = ((char)0x1F).ToString();

        private ITerminalConnection connection;
        private IntPtr hwnd;
        private IntPtr terminal;
        private NativeMethods.ScrollCallback scrollCallback;
        private NativeMethods.WriteCallback writeCallback;
        private bool suppressNextCtrlVChar;
        private bool suppressNextCtrlCChar;
        private bool suppressNextCtrlLetterChar;
        private bool suppressNextEnterChar;

        /// <summary>
        /// Initializes a new instance of the <see cref="TerminalContainer"/> class.
        /// </summary>
        public TerminalContainer()
        {
            // WPF & TSF can't deal with us setting TF_TMAE_CONSOLE on the UI thread.
            // It simply crashes on Windows 10 if you use the Emoji picker.
            // (On later versions of Windows it just doesn't work.)
            NativeMethods.AvoidBuggyTSFConsoleFlags();

            this.MessageHook += this.TerminalContainer_MessageHook;
            this.GotFocus += this.TerminalContainer_GotFocus;
            this.Focusable = true;
        }

        /// <summary>
        /// Event that is fired when the terminal buffer scrolls from text output.
        /// </summary>
        internal event EventHandler<(int viewTop, int viewHeight, int bufferSize)> TerminalScrolled;

        /// <summary>
        /// Event that is fired when the user engages in a mouse scroll over the terminal hwnd.
        /// </summary>
        internal event EventHandler<int> UserScrolled;

        /// <summary>
        /// Event that is fired once the native terminal hwnd has actually been created (see
        /// BuildWindowCore). WPF's HwndHost defers this until the control is arranged, which never
        /// happens while an ancestor is Visibility.Collapsed - so a SetTheme call made beforehand
        /// (e.g. on a freshly-launched, not-yet-shown session pane) silently has nothing to act on.
        /// Consumers that called SetTheme early should listen for this and call it again.
        /// </summary>
        internal event EventHandler WindowCreated;

        /// <summary>
        /// Gets or sets a value indicating whether if the renderer should automatically resize to fill the control
        /// on user action.
        /// </summary>
        internal bool AutoResize { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether Shift+Enter (with no other modifier) sends a
        /// literal newline to the connection instead of forwarding Enter's normal behavior -
        /// mirrors how Ctrl+Enter is written directly, bypassing the write path entirely so no
        /// escape-sequence guessing about what the connected process expects is needed.
        /// </summary>
        internal bool NewlineOnShiftEnter { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether Ctrl+Z sends whatever a real Ctrl+_ (0x1F,
        /// Unit Separator) would, instead of Ctrl+Z's own control character (0x1A) - see the
        /// WM_KEYDOWN case's remarks for why: Claude Code's CLI reserves Ctrl+Z for Unix
        /// process-suspend and binds undo to Ctrl+_/Ctrl+Shift+- instead, so plain Ctrl+Z has no
        /// effect there.
        /// </summary>
        internal bool RemapCtrlZForUndo { get; set; }

        /// <summary>
        /// Gets or sets the size of the parent user control that hosts the terminal hwnd.
        /// </summary>
        /// <remarks>Control size is in device independent units, but for simplicity all sizes should be scaled.</remarks>
        internal Size TerminalControlSize { get; set; }

        /// <summary>
        /// Gets or sets the size of the terminal renderer.
        /// </summary>
        internal Size TerminalRendererSize { get; set; }

        /// <summary>
        /// Gets the current character rows available to the terminal.
        /// </summary>
        internal int Rows { get; private set; }

        /// <summary>
        /// Gets the current character columns available to the terminal.
        /// </summary>
        internal int Columns { get; private set; }

        /// <summary>
        /// Gets the window handle of the terminal.
        /// </summary>
        internal IntPtr Hwnd => this.hwnd;

        /// <summary>
        /// Sets the connection to the terminal backend.
        /// </summary>
        internal ITerminalConnection Connection
        {
            private get
            {
                return this.connection;
            }

            set
            {
                if (this.connection != null)
                {
                    this.connection.TerminalOutput -= this.Connection_TerminalOutput;
                }

                this.Connection_TerminalOutput(this, new TerminalOutputEventArgs("\x001bc\x1b]104\x1b\\")); // reset console/clear screen - https://github.com/microsoft/terminal/pull/15062#issuecomment-1505654110
                var wasNull = this.connection == null;
                this.connection = value;
                if (this.connection != null)
                {
                    if (wasNull)
                    {
                        this.Connection_TerminalOutput(this, new TerminalOutputEventArgs("\x1b[?25h")); // show cursor
                    }

                    this.connection.TerminalOutput += this.Connection_TerminalOutput;
                    this.connection.Start();
                }
                else
                {
                    this.Connection_TerminalOutput(this, new TerminalOutputEventArgs("\x1b[?25l")); // hide cursor
                }
            }
        }

        /// <summary>
        /// Manually invoke a scroll of the terminal buffer.
        /// </summary>
        /// <param name="viewTop">The top line to show in the terminal.</param>
        internal void UserScroll(int viewTop)
        {
            NativeMethods.TerminalUserScroll(this.terminal, viewTop);
        }

        /// <summary>
        /// Sets the theme for the terminal. This includes font family, size, color, as well as background and foreground colors.
        /// </summary>
        /// <param name="theme">The color theme for the terminal to use.</param>
        /// <param name="fontFamily">The font family to use in the terminal.</param>
        /// <param name="fontSize">The font size to use in the terminal.</param>
        internal void SetTheme(TerminalTheme theme, string fontFamily, short fontSize)
        {
            var dpiScale = VisualTreeHelper.GetDpi(this);

            NativeMethods.TerminalSetTheme(this.terminal, theme, fontFamily, fontSize, (int)dpiScale.PixelsPerInchX);

            // Validate before resizing that we have a non-zero size.
            if (!this.RenderSize.IsEmpty && !this.TerminalControlSize.IsEmpty
                && this.TerminalControlSize.Width != 0 && this.TerminalControlSize.Height != 0)
            {
                this.Resize(this.TerminalControlSize);
            }
        }

        /// <summary>
        /// Gets the selected text from the terminal renderer and clears the selection.
        /// </summary>
        /// <returns>The selected text, empty if no text is selected.</returns>
        internal string GetSelectedText()
        {
            if (NativeMethods.TerminalIsSelectionActive(this.terminal))
            {
                return NativeMethods.TerminalGetSelection(this.terminal);
            }

            return string.Empty;
        }

        /// <summary>
        /// Triggers a resize of the terminal with the given size, redrawing the rendered text.
        /// </summary>
        /// <param name="renderSize">Size of the rendering window.</param>
        internal void Resize(Size renderSize)
        {
            if (renderSize.Width == 0 || renderSize.Height == 0)
            {
                throw new ArgumentException("Terminal column or row count cannot be 0.", nameof(renderSize));
            }

            NativeMethods.TerminalTriggerResize(
                this.terminal,
                (int)renderSize.Width,
                (int)renderSize.Height,
                out NativeMethods.TilSize dimensions);

            this.Rows = dimensions.Y;
            this.Columns = dimensions.X;
            this.TerminalRendererSize = renderSize;

            this.Connection?.Resize((uint)dimensions.Y, (uint)dimensions.X);
        }

        /// <summary>
        /// Resizes the terminal using row and column count as the new size.
        /// </summary>
        /// <param name="rows">Number of rows to show.</param>
        /// <param name="columns">Number of columns to show.</param>
        internal void Resize(uint rows, uint columns)
        {
            if (rows == 0)
            {
                throw new ArgumentException("Terminal row count cannot be 0.", nameof(rows));
            }

            if (columns == 0)
            {
                throw new ArgumentException("Terminal column count cannot be 0.", nameof(columns));
            }

            NativeMethods.TilSize dimensions = new NativeMethods.TilSize
            {
                X = (int)columns,
                Y = (int)rows,
            };

            NativeMethods.TerminalTriggerResizeWithDimension(this.terminal, dimensions, out var dimensionsInPixels);

            this.Columns = dimensions.X;
            this.Rows = dimensions.Y;

            this.TerminalRendererSize = new Size
            {
                Width = dimensionsInPixels.X,
                Height = dimensionsInPixels.Y,
            };

            this.Connection?.Resize((uint)dimensions.Y, (uint)dimensions.X);
        }

        /// <summary>
        /// Calculates the rows and columns that would fit in the given size.
        /// </summary>
        /// <param name="size">DPI scaled size.</param>
        /// <returns>Amount of rows and columns that would fit the given size.</returns>
        internal (int columns, int rows) CalculateRowsAndColumns(Size size)
        {
            NativeMethods.TerminalCalculateResize(this.terminal, (int)size.Width, (int)size.Height, out NativeMethods.TilSize dimensions);

            return (dimensions.X, dimensions.Y);
        }

        /// <summary>
        /// Triggers the terminal resize event if more space is available in the terminal control.
        /// </summary>
        internal void RaiseResizedIfDrawSpaceIncreased()
        {
            var (columns, rows) = this.CalculateRowsAndColumns(this.TerminalControlSize);

            if (this.Columns < columns || this.Rows < rows)
            {
                this.connection?.Resize((uint)rows, (uint)columns);
            }
        }

        /// <summary>
        /// WPF's HwndHost likes to mark the WM_GETOBJECT message as handled to
        /// force the usage of the WPF automation peer. We explicitly mark it as
        /// not handled and don't return an automation peer in "OnCreateAutomationPeer" below.
        /// This forces the message to go down to the HwndTerminal where we return terminal's UiaProvider.
        /// </summary>
        /// <inheritdoc/>
        protected override IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == (int)NativeMethods.WindowMessage.WM_GETOBJECT)
            {
                handled = false;
                return IntPtr.Zero;
            }

            return base.WndProc(hwnd, msg, wParam, lParam, ref handled);
        }

        /// <inheritdoc/>
        protected override AutomationPeer OnCreateAutomationPeer()
        {
            return null;
        }

        /// <inheritdoc/>
        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            if (this.terminal != IntPtr.Zero)
            {
                NativeMethods.TerminalDpiChanged(this.terminal, (int)(NativeMethods.USER_DEFAULT_SCREEN_DPI * newDpi.DpiScaleX));
            }
        }

        /// <inheritdoc/>
        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            var dpiScale = VisualTreeHelper.GetDpi(this);
            NativeMethods.CreateTerminal(hwndParent.Handle, out this.hwnd, out this.terminal);

            this.scrollCallback = this.OnScroll;
            this.writeCallback = this.OnWrite;

            NativeMethods.TerminalRegisterScrollCallback(this.terminal, this.scrollCallback);
            NativeMethods.TerminalRegisterWriteCallback(this.terminal, this.writeCallback);

            // If the saved DPI scale isn't the default scale, we push it to the terminal.
            if (dpiScale.PixelsPerInchX != NativeMethods.USER_DEFAULT_SCREEN_DPI)
            {
                NativeMethods.TerminalDpiChanged(this.terminal, (int)dpiScale.PixelsPerInchX);
            }

            this.WindowCreated?.Invoke(this, EventArgs.Empty);

            return new HandleRef(this, this.hwnd);
        }

        /// <inheritdoc/>
        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            NativeMethods.DestroyTerminal(this.terminal);
            this.terminal = IntPtr.Zero;
        }

        private static void UnpackKeyMessage(IntPtr wParam, IntPtr lParam, out ushort vkey, out ushort scanCode, out ushort flags)
        {
            ulong scanCodeAndFlags = ((ulong)lParam >> 16) & 0xFFFF;
            scanCode = (ushort)(scanCodeAndFlags & 0x00FFu);
            flags = (ushort)(scanCodeAndFlags & 0xFF00u);
            vkey = (ushort)wParam;
        }

        private static void UnpackCharMessage(IntPtr wParam, IntPtr lParam, out char character, out ushort scanCode, out ushort flags)
        {
            UnpackKeyMessage(wParam, lParam, out ushort vKey, out scanCode, out flags);
            character = (char)vKey;
        }

        private void TerminalContainer_GotFocus(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            NativeMethods.SetFocus(this.hwnd);
        }

        private IntPtr TerminalContainer_MessageHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (hwnd == this.hwnd)
            {
                switch ((NativeMethods.WindowMessage)msg)
                {
                    case NativeMethods.WindowMessage.WM_SETFOCUS:
                        NativeMethods.TerminalSetFocus(this.terminal);
                        break;
                    case NativeMethods.WindowMessage.WM_KILLFOCUS:
                        NativeMethods.TerminalKillFocus(this.terminal);
                        break;
                    case NativeMethods.WindowMessage.WM_MOUSEACTIVATE:
                        this.Focus();
                        NativeMethods.SetFocus(this.hwnd);
                        break;
                    case NativeMethods.WindowMessage.WM_SYSKEYDOWN: // fallthrough
                    case NativeMethods.WindowMessage.WM_KEYDOWN:
                        {
                            UnpackKeyMessage(wParam, lParam, out ushort vkey, out ushort scanCode, out ushort flags);

                            if (vkey == VkV && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                            {
                                this.PasteFromClipboard();
                                this.suppressNextCtrlVChar = true;
                                handled = true;
                                break;
                            }

                            // Mirrors real terminal convention (Windows Terminal, etc.): Ctrl+C
                            // copies the active selection instead of sending an interrupt, since
                            // that's almost always what a selection means the user wants. With no
                            // selection, Ctrl+C isn't special-cased here at all - it falls through
                            // to the generic TerminalSendKeyEvent call below like any other key,
                            // same as it always has, so the shell still gets its interrupt.
                            if (vkey == VkC && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && this.GetSelectedText() is { Length: > 0 } selectedText)
                            {
                                this.CopyToClipboard(selectedText);
                                this.suppressNextCtrlCChar = true;
                                handled = true;
                                break;
                            }

                            // Claude Code's CLI reserves Ctrl+Z for Unix process-suspend (a no-op
                            // on Windows) and binds its own undo to Ctrl+_/Ctrl+Shift+- instead -
                            // so plain Ctrl+Z's own control character (sent by the generic
                            // Ctrl+<letter> case below) has no effect there. When opted in (see
                            // AppSettings.RemapCtrlZForUndo), send Ctrl+_'s byte (0x1F, Unit
                            // Separator) instead - written directly to the connection rather than
                            // through the terminal core, same as Ctrl+V/Shift+Enter above, since a
                            // plain control character's byte never depends on terminal mode.
                            if (vkey == VkZ && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && this.RemapCtrlZForUndo)
                            {
                                this.Connection?.WriteInput(UnitSeparator);
                                this.suppressNextCtrlLetterChar = true;
                                handled = true;
                                break;
                            }

                            // Every other Ctrl+<letter> (plus Ctrl+Z itself when the remap above
                            // is off) needs its control character (the standard ASCII mapping -
                            // vkey & 0x1F, e.g. Ctrl+Z -> 0x1A) delivered explicitly, not left to a
                            // WM_CHAR TranslateMessage would normally generate: TerminalKeyRoutingHook
                            // redelivers these WM_KEYDOWNs via a direct SendMessage that bypasses
                            // TranslateMessage entirely (see its remarks), so nothing else would
                            // ever produce that WM_CHAR. Sending both the key and char events here,
                            // then suppressing whatever WM_CHAR (if any) still follows below, keeps
                            // this correct on either path.
                            if (vkey is >= 0x41 and <= 0x5A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                            {
                                NativeMethods.TerminalSendKeyEvent(this.terminal, vkey, scanCode, flags, true);
                                NativeMethods.TerminalSendCharEvent(this.terminal, (char)(vkey & 0x1F), scanCode, flags);
                                this.suppressNextCtrlLetterChar = true;
                                handled = true;
                                break;
                            }

                            // Written directly to the connection, same as paste above, rather than
                            // forwarded as a key event with remapped modifiers - there's no reliable
                            // way to make the native terminal core treat this as if Ctrl (not Shift)
                            // were held, and the connected process only ever sees the resulting byte
                            // anyway, not which key produced it.
                            if (this.NewlineOnShiftEnter && vkey == VkReturn && Keyboard.Modifiers == ModifierKeys.Shift)
                            {
                                this.Connection?.WriteInput("\n");
                                this.suppressNextEnterChar = true;
                                handled = true;
                                break;
                            }

                            NativeMethods.TerminalSendKeyEvent(this.terminal, vkey, scanCode, flags, true);
                            break;
                        }

                    case NativeMethods.WindowMessage.WM_SYSKEYUP: // fallthrough
                    case NativeMethods.WindowMessage.WM_KEYUP:
                        {
                            // WM_KEYUP lParam layout documentation: https://docs.microsoft.com/en-us/windows/win32/inputdev/wm-keyup
                            UnpackKeyMessage(wParam, lParam, out ushort vkey, out ushort scanCode, out ushort flags);
                            NativeMethods.TerminalSendKeyEvent(this.terminal, (ushort)wParam, scanCode, flags, false);
                            break;
                        }

                    case NativeMethods.WindowMessage.WM_CHAR:
                        {
                            // WM_CHAR lParam layout documentation: https://docs.microsoft.com/en-us/windows/win32/inputdev/wm-char
                            UnpackCharMessage(wParam, lParam, out char character, out ushort scanCode, out ushort flags);

                            // TranslateMessage turns the Ctrl+V we already handled in WM_KEYDOWN into the
                            // control character 0x16 (SYN); swallow it so it isn't also sent to the terminal
                            // on top of the clipboard paste.
                            if (this.suppressNextCtrlVChar)
                            {
                                this.suppressNextCtrlVChar = false;
                                handled = true;
                                break;
                            }

                            // Swallows the ETX (0x03) TranslateMessage generates for the Ctrl+C
                            // keydown already handled (as a clipboard copy) in WM_KEYDOWN above.
                            if (this.suppressNextCtrlCChar)
                            {
                                this.suppressNextCtrlCChar = false;
                                handled = true;
                                break;
                            }

                            // Swallows whatever control character TranslateMessage generates for
                            // the Ctrl+<letter> keydown already handled explicitly in WM_KEYDOWN
                            // above (only reachable on a path where TranslateMessage still ran -
                            // see that case's remarks).
                            if (this.suppressNextCtrlLetterChar)
                            {
                                this.suppressNextCtrlLetterChar = false;
                                handled = true;
                                break;
                            }

                            // Swallows the '\r' TranslateMessage generates for the Enter keydown
                            // already handled (and written as '\n') in WM_KEYDOWN above.
                            if (this.suppressNextEnterChar)
                            {
                                this.suppressNextEnterChar = false;
                                handled = true;
                                break;
                            }

                            NativeMethods.TerminalSendCharEvent(this.terminal, character, scanCode, flags);
                            break;
                        }

                    case NativeMethods.WindowMessage.WM_WINDOWPOSCHANGED:
                        var windowpos = (NativeMethods.WINDOWPOS)Marshal.PtrToStructure(lParam, typeof(NativeMethods.WINDOWPOS));
                        if (((NativeMethods.SetWindowPosFlags)windowpos.flags).HasFlag(NativeMethods.SetWindowPosFlags.SWP_NOSIZE)
                            || (windowpos.cx == 0 && windowpos.cy == 0))
                        {
                            break;
                        }

                        NativeMethods.TilSize dimensions;

                        if (this.AutoResize)
                        {
                            NativeMethods.TerminalTriggerResize(this.terminal, windowpos.cx, windowpos.cy, out dimensions);

                            this.Columns = dimensions.X;
                            this.Rows = dimensions.Y;

                            this.TerminalRendererSize = new Size
                            {
                                Width = windowpos.cx,
                                Height = windowpos.cy,
                            };
                        }
                        else
                        {
                            // Calculate the new columns and rows that fit the total control size and alert the control to redraw the margins.
                            NativeMethods.TerminalCalculateResize(this.terminal, (int)this.TerminalControlSize.Width, (int)this.TerminalControlSize.Height, out dimensions);
                        }

                        this.Connection?.Resize((uint)dimensions.Y, (uint)dimensions.X);
                        break;

                    case NativeMethods.WindowMessage.WM_MOUSEWHEEL:
                        var delta = (short)(((long)wParam) >> 16);
                        this.UserScrolled?.Invoke(this, delta);
                        break;
                }
            }

            return IntPtr.Zero;
        }

        private void PasteFromClipboard()
        {
            string text;
            try
            {
                // Clipboard access can throw intermittently (e.g. another process briefly owns it);
                // treat that as "nothing to paste" rather than letting it bubble up and crash the app.
                text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
            }
            catch (COMException)
            {
                return;
            }

            if (!string.IsNullOrEmpty(text))
            {
                // Wrapping in the standard "bracketed paste" markers lets the hosted Claude Code
                // CLI (every session here launches it - see MainWindow.LaunchSession) tell a paste
                // apart from typed input, same as a real terminal (Windows Terminal, iTerm2, etc.)
                // would. Without this the CLI can't recognize the burst of text as one atomic
                // paste, so it never collapses it into its own "[Pasted text #N +M lines]"
                // placeholder and instead treats embedded newlines as individual Enter presses.
                this.Connection?.WriteInput(BracketedPasteStart + text + BracketedPasteEnd);
            }
        }

        private void CopyToClipboard(string text)
        {
            try
            {
                // Clipboard access can throw intermittently (e.g. another process briefly owns
                // it) - same tolerance as PasteFromClipboard, just for the write side.
                Clipboard.SetText(text);
            }
            catch (COMException)
            {
            }
        }

        private void Connection_TerminalOutput(object sender, TerminalOutputEventArgs e)
        {
            if (this.terminal == IntPtr.Zero || string.IsNullOrEmpty(e.Data))
            {
                return;
            }

            NativeMethods.TerminalSendOutput(this.terminal, e.Data);
        }

        private void OnScroll(int viewTop, int viewHeight, int bufferSize)
        {
            this.TerminalScrolled?.Invoke(this, (viewTop, viewHeight, bufferSize));
        }

        private void OnWrite(string data)
        {
            this.Connection?.WriteInput(data);
        }
    }
}
