using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using OptiscalerApp.ViewModels;
using SDL;
using static SDL.SDL3;

namespace OptiscalerApp.Views;

[Flags]
internal enum PadButtons
{
    None = 0,
    Up = 1,
    Down = 2,
    Left = 4,
    Right = 8,
    Accept = 16,
    Back = 32,
    PreviousPage = 64,
    NextPage = 128,
    Directions = Up | Down | Left | Right
}

/// <summary>
///     Lets a controller drive the window: the D-pad or left stick moves focus, A activates, B goes back, the
///     shoulder buttons switch pages and the right stick scrolls. Presses become the key events a keyboard would
///     send, so every control behaves the same for both.
/// </summary>
internal sealed class GamepadNavigation
{
    private static readonly TimeSpan RepeatDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(110);
    private const double ScrollSpeed = 24;

    private static readonly (PadButtons Button, Key Key)[] Keys =
    [
        (PadButtons.Up, Key.Up), (PadButtons.Down, Key.Down), (PadButtons.Left, Key.Left),
        (PadButtons.Right, Key.Right), (PadButtons.Accept, Key.Enter), (PadButtons.Back, Key.Escape)
    ];

    private readonly Window _window;
    private PadButtons _held;
    private InputElement? _lastFocused;
    private DateTime _nextRepeat;

    public GamepadNavigation(Window window)
    {
        _window = window;

        // Remembered so the focus can return to a control that loses it by being disabled while its command runs.
        window.GotFocus += (_, e) =>
        {
            if (e.Source is InputElement element && !ReferenceEquals(element, window)) _lastFocused = element;
        };
    }

    /// <summary>
    ///     Polls the controllers: for navigation while the window is active, and in the background only when a
    ///     shortcut for OptiScaler's in-game menu is set and can press its key. Does nothing when SDL cannot start.
    /// </summary>
    public static void Attach(Window window, ControllerShortcut shortcut)
    {
        if (SdlGamepads.TryStart() is not { } pads) return;

        shortcut.CanReadControllers = true;
        var navigation = new GamepadNavigation(window);
        var timer = new DispatcherTimer(DispatcherPriority.Input);
        timer.Tick += (_, _) =>
        {
            var state = pads.Read();

            // Only the active window navigates, so playing a game with this app behind it presses nothing here.
            // Neither does choosing the shortcut's buttons in Settings.
            if (window.IsActive && !shortcut.IsCapturing)
            {
                navigation.Update(state.Held, state.Scroll);
            }
            else
            {
                // A button still down when the window returns, such as the one that closed the game, is not a
                // press, and a direction still held does not start repeating.
                navigation._held = ~PadButtons.None;
                navigation._nextRepeat = DateTime.MaxValue;
            }

            shortcut.Update(state.Buttons, window.IsActive);

            if (!window.IsActive && !shortcut.IsArmed)
            {
                timer.Stop();

                return;
            }

            // Without a controller there is only its arrival to wait for.
            var interval = TimeSpan.FromMilliseconds(pads.Count == 0 ? 500 : window.IsActive ? 16 : 40);
            if (timer.Interval != interval) timer.Interval = interval;
        };

        void Start()
        {
            timer.Interval = TimeSpan.FromMilliseconds(16);
            timer.Start();
        }

        window.Activated += (_, _) => Start();
        window.Closed += (_, _) =>
        {
            timer.Stop();
            pads.Dispose();
        };

        // The window is usually up by now: controllers are looked for once it is shown.
        Start();
    }

    /// <summary>Takes the buttons held right now; a held direction repeats like a held key.</summary>
    public void Update(PadButtons held, double scroll = 0)
    {
        var pressed = held & ~_held;
        _held = held;

        if ((pressed & PadButtons.Directions) != 0)
        {
            _nextRepeat = DateTime.UtcNow + RepeatDelay;
        }
        else if ((held & PadButtons.Directions) != 0 && DateTime.UtcNow >= _nextRepeat)
        {
            pressed |= held & PadButtons.Directions;
            _nextRepeat = DateTime.UtcNow + RepeatInterval;
        }

        foreach (var (button, key) in Keys)
            if (pressed.HasFlag(button))
                SendKey(key);

        if (_window.DataContext is MainWindowViewModel shell)
        {
            if (pressed.HasFlag(PadButtons.PreviousPage)) shell.ShowAdjacentPage(-1);
            if (pressed.HasFlag(PadButtons.NextPage)) shell.ShowAdjacentPage(1);
        }

        if (scroll != 0 && PageScroller() is { } scroller)
            scroller.Offset = scroller.Offset.WithY(scroller.Offset.Y + scroll * ScrollSpeed);
    }

    private void SendKey(Key key)
    {
        var focused = _window.FocusManager.GetFocusedElement();

        // Nothing is focused after a page change or a click on the background: a direction starts in the page. A
        // control disabled while its command ran also lost the focus; that one gets it back.
        if ((focused is null || ReferenceEquals(focused, _window)) && key != Key.Escape)
        {
            if (!ReferenceEquals(TopLevel.GetTopLevel(_lastFocused), _window)) _lastFocused = null;

            var scroller = PageScroller();
            IInputElement? start = _lastFocused is { IsEffectivelyEnabled: true, IsEffectivelyVisible: true }
                ? _lastFocused
                : scroller is null
                    ? null
                    : FocusManager.FindFirstFocusableElement(scroller);

            // A page that is working is disabled as a whole. The press waits for it rather than leaving for the
            // sidebar, which would lose the place.
            if (start is null && scroller is not null && scroller.GetVisualDescendants().OfType<InputElement>()
                    .Any(e => e is { Focusable: true, IsEffectivelyVisible: true, IsEffectivelyEnabled: false }))
                return;

            (start ?? _window.FocusManager.FindFirstFocusableElement())?.Focus(NavigationMethod.Directional);

            return;
        }

        (focused as Interactive ?? _window).RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyDeviceType = KeyDeviceType.Gamepad
        });
    }

    /// <summary>The page's own scroll area, not the ones text boxes and lists build from their templates.</summary>
    private ScrollViewer? PageScroller()
    {
        return _window.GetVisualDescendants().OfType<ScrollViewer>()
            .FirstOrDefault(s => s.TemplatedParent is null && s.IsEffectivelyVisible);
    }
}

/// <summary>
///     What the controllers hold right now: as navigation, as scrolling from -1 to 1, and as one bit per button for
///     shortcuts.
/// </summary>
internal readonly record struct PadState(PadButtons Held, double Scroll, uint Buttons);

/// <summary>Every connected controller, read as one through SDL.</summary>
internal sealed unsafe class SdlGamepads : IDisposable
{
    private const short StickThreshold = 16000;
    private const short ScrollDeadZone = 8000;

    // The triggers follow SDL's buttons in a button mask.
    private const int TriggerBit = (int)SDL_GamepadButton.SDL_GAMEPAD_BUTTON_COUNT;

    private static readonly (SDL_GamepadButton Button, PadButtons Action)[] Actions =
    [
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP, PadButtons.Up),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_DOWN, PadButtons.Down),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT, PadButtons.Left),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT, PadButtons.Right),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH, PadButtons.Accept),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST, PadButtons.Back),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER, PadButtons.PreviousPage),
        (SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER, PadButtons.NextPage)
    ];

    private readonly List<nint> _pads = [];

    private SdlGamepads() { Reopen(); }

    public int Count => _pads.Count;

    public static SdlGamepads? TryStart()
    {
        try
        {
            // SDL would otherwise take over Ctrl+C and SIGTERM, and stop reporting input without a window of its own.
            SDL_SetHint(SDL_HINT_NO_SIGNAL_HANDLERS, "1"u8);
            SDL_SetHint(SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1"u8);

            // In the Steam Deck's desktop mode Steam already turns its controls into arrow keys, Enter and Escape.
            // Reading the device directly as well would make every press count twice.
            SDL_SetHint(SDL_HINT_JOYSTICK_HIDAPI_STEAMDECK, "0"u8);

            return SDL_Init(SDL_InitFlags.SDL_INIT_GAMEPAD) ? new SdlGamepads() : null;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or
                                       BadImageFormatException)
        {
            // Controller support is optional; the app works with mouse and keyboard without the native library.
            return null;
        }
    }

    public PadState Read()
    {
        SDL_PumpEvents();
        if (SDL_HasEvents((uint)SDL_EventType.SDL_EVENT_GAMEPAD_ADDED, (uint)SDL_EventType.SDL_EVENT_GAMEPAD_REMOVED))
            Reopen();
        SDL_FlushEvents(SDL_EventType.SDL_EVENT_FIRST, SDL_EventType.SDL_EVENT_LAST);

        var held = PadButtons.None;
        var buttons = 0u;
        var scroll = 0.0;

        foreach (var handle in _pads)
        {
            var pad = (SDL_Gamepad*)handle;

            short Axis(SDL_GamepadAxis axis) { return SDL_GetGamepadAxis(pad, axis); }

            for (var button = 0; button < TriggerBit; button++)
                if (SDL_GetGamepadButton(pad, (SDL_GamepadButton)button))
                    buttons |= 1u << button;
            if (Axis(SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER) > StickThreshold) buttons |= 1u << TriggerBit;
            if (Axis(SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER) > StickThreshold) buttons |= 2u << TriggerBit;

            var x = Axis(SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX);
            var y = Axis(SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY);

            // Only the stick's stronger axis counts, so a diagonal moves the focus once, not along both.
            if (Math.Abs((int)y) >= Math.Abs((int)x))
            {
                if (y < -StickThreshold) held |= PadButtons.Up;
                if (y > StickThreshold) held |= PadButtons.Down;
            }
            else
            {
                if (x < -StickThreshold) held |= PadButtons.Left;
                if (x > StickThreshold) held |= PadButtons.Right;
            }

            var rightY = Axis(SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTY);
            if (Math.Abs((int)rightY) > ScrollDeadZone) scroll += rightY / (double)short.MaxValue;
        }

        foreach (var (button, action) in Actions)
            if ((buttons & (1u << (int)button)) != 0)
                held |= action;

        return new PadState(held, scroll, buttons);
    }

    public void Dispose()
    {
        Close();
        SDL_Quit();
    }

    private void Reopen()
    {
        Close();
        using var ids = SDL_GetGamepads();

        if (ids is null) return;

        foreach (var id in ids)
        {
            var pad = SDL_OpenGamepad(id);
            if (pad is not null) _pads.Add((nint)pad);
        }
    }

    private void Close()
    {
        foreach (var pad in _pads) SDL_CloseGamepad((SDL_Gamepad*)pad);
        _pads.Clear();
    }
}
