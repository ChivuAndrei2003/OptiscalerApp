using System.Runtime.InteropServices;
using OptiscalerApp.ViewModels;

namespace OptiscalerApp.Views;

/// <summary>
///     Opens OptiScaler's in-game menu from a controller. OptiScaler only listens to the keyboard, so while a game
///     has the focus the chosen buttons are answered by pressing its menu key, Insert, for the game.
/// </summary>
public sealed class ControllerShortcut : IControllerShortcut, IDisposable
{
    private static readonly TimeSpan CaptureTimeout = TimeSpan.FromSeconds(8);

    /// <summary>One name per bit of a button mask: SDL's gamepad buttons in its order, then the two triggers.</summary>
    private static readonly string[] Names =
    [
        "A", "B", "X", "Y", "View (Select)", "Guide", "Menu (Start)", "L3", "R3", "LB", "RB", "D-pad up",
        "D-pad down", "D-pad left", "D-pad right", "Share", "R4 (P1)", "L4 (P3)", "R5 (P2)", "L5 (P4)", "Touchpad",
        "Extra 2", "Extra 3", "Extra 4", "Extra 5", "Extra 6", "LT", "RT"
    ];

    private readonly Func<IMenuKey?> _createKey;
    private readonly Lazy<string?> _keyProblem = new(MenuKey.FindProblem);
    private uint _buttons;
    private TaskCompletionSource<uint>? _capture;
    private bool _captureArmed;
    private uint _captured;
    private IMenuKey? _key;
    private bool _released = true;

    public ControllerShortcut() : this(MenuKey.TryCreate) { }

    internal ControllerShortcut(Func<IMenuKey?> createKey) { _createKey = createKey; }

    /// <summary>Set once controllers are being read.</summary>
    public bool CanReadControllers { get; set; }

    public bool IsCapturing => _capture is not null;

    /// <summary>Whether the buttons can do anything for a game: some are chosen and the menu key can be pressed.</summary>
    public bool IsArmed => _buttons != 0 && _key is not null;

    public string? Problem => CanReadControllers ? _keyProblem.Value : "Controller support could not start.";

    public uint Buttons
    {
        get => _buttons;
        set
        {
            _buttons = value;

            // Made ahead of the first press: a game only sees a new keyboard a moment after it appears.
            if (value != 0) _key ??= _createKey();
        }
    }

    public string Describe(uint buttons)
    {
        return string.Join(" + ", Names.Where((_, bit) => (buttons & (1u << bit)) != 0));
    }

    public async Task<uint> CaptureAsync()
    {
        if (!CanReadControllers) return 0;

        var capture = _capture = new TaskCompletionSource<uint>();
        _captured = 0;
        _captureArmed = false;
        await Task.WhenAny(capture.Task, Task.Delay(CaptureTimeout));
        if (_capture == capture) _capture = null;

        return capture.Task.IsCompletedSuccessfully ? capture.Task.Result : 0;
    }

    /// <summary>Takes the buttons held on the controllers right now.</summary>
    public void Update(uint down, bool windowActive)
    {
        if (_capture is { } capture)
        {
            // The press that started the capture, such as A on the Set button, has to end first.
            if (!_captureArmed)
            {
                _captureArmed = down == 0;

                return;
            }

            // Everything held on the way counts, so a combination can be pressed one button at a time.
            _captured |= down;

            if (down == 0 && _captured != 0)
            {
                _capture = null;
                capture.TrySetResult(_captured);
            }

            return;
        }

        // Once per press, and only for a game: in this app's own window the buttons navigate.
        var held = _buttons != 0 && (down & _buttons) == _buttons;
        if (held && _released && !windowActive) _ = _key?.PressAsync();
        _released = !held;
    }

    /// <summary>The container disposes this once per type it is registered under, so only the first call counts.</summary>
    public void Dispose()
    {
        _key?.Dispose();
        _key = null;
    }
}

/// <summary>Presses OptiScaler's menu key for whichever window has the keyboard.</summary>
internal interface IMenuKey : IDisposable
{
    Task PressAsync();
}

internal static class MenuKey
{
    // OptiScaler looks at the keyboard once per frame, so the key stays down for a few frames.
    private static readonly TimeSpan HoldTime = TimeSpan.FromMilliseconds(80);

    /// <summary>Why a key cannot be pressed for a game on this system; null when it can.</summary>
    public static string? FindProblem()
    {
        if (OperatingSystem.IsWindows()) return null;
        if (!OperatingSystem.IsLinux()) return "Opening the menu from a controller works on Windows and Linux only.";

        try
        {
            using var device = OpenUinput();

            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "This user cannot write to /dev/uinput, which is needed to press the menu key for a game.";
        }
    }

    public static IMenuKey? TryCreate()
    {
        try
        {
            return OperatingSystem.IsWindows() ? new WindowsKey() : OperatingSystem.IsLinux() ? new UinputKey() : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static FileStream OpenUinput()
    {
        return new FileStream("/dev/uinput", FileMode.Open, FileAccess.Write, FileShare.ReadWrite, 0);
    }

    private sealed class WindowsKey : IMenuKey
    {
        private const ushort VkInsert = 0x2D;
        private const ushort ScanInsert = 0x52;
        private const uint ExtendedKey = 1;
        private const uint KeyUp = 2;

        public async Task PressAsync()
        {
            Send(ExtendedKey);
            await Task.Delay(HoldTime);
            Send(ExtendedKey | KeyUp);
        }

        public void Dispose() { }

        private static unsafe void Send(uint flags)
        {
            var input = new Input { Type = 1, Key = VkInsert, Scan = ScanInsert, Flags = flags };
            SendInput(1, &input, sizeof(Input));
        }

        [DllImport("user32.dll")]
        private static extern unsafe uint SendInput(uint count, Input* inputs, int size);

        /// <summary>The keyboard form of Win32's INPUT on 64-bit Windows.</summary>
        [StructLayout(LayoutKind.Explicit, Size = 40)]
        private struct Input
        {
            [FieldOffset(0)] public uint Type;
            [FieldOffset(8)] public ushort Key;
            [FieldOffset(10)] public ushort Scan;
            [FieldOffset(12)] public uint Flags;
        }
    }

    /// <summary>A virtual keyboard with one key, which the desktop or gamescope treats like a real one.</summary>
    private sealed class UinputKey : IMenuKey
    {
        private const int EvKey = 1;
        private const int KeyInsert = 110;
        private const nuint SetEventBit = 0x40045564;
        private const nuint SetKeyBit = 0x40045565;
        private const nuint SetupDevice = 0x405C5503;
        private const nuint CreateDevice = 0x5501;
        private const nuint DestroyDevice = 0x5502;

        private readonly FileStream _device = OpenUinput();

        public unsafe UinputKey()
        {
            // struct uinput_setup: bus type, vendor, product, version, an 80-byte name, force-feedback effects.
            Span<byte> setup = stackalloc byte[92];
            setup.Clear();
            setup[0] = 0x06; // BUS_VIRTUAL
            "OptiScaler menu key"u8.CopyTo(setup[8..]);

            fixed (byte* pointer = setup)
            {
                if (ioctl(Handle, SetEventBit, EvKey) < 0 || ioctl(Handle, SetKeyBit, KeyInsert) < 0 ||
                    ioctl(Handle, SetupDevice, (nint)pointer) < 0 || ioctl(Handle, CreateDevice, 0) < 0)
                {
                    _device.Dispose();

                    throw new IOException("The virtual keyboard could not be created.");
                }
            }
        }

        private int Handle => (int)_device.SafeFileHandle.DangerousGetHandle();

        public async Task PressAsync()
        {
            try
            {
                Write(1);
                await Task.Delay(HoldTime);
                Write(0);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // The device went away, e.g. while the app closes; there is nobody left to tell.
            }
        }

        public void Dispose()
        {
            _ = ioctl(Handle, DestroyDevice, 0);
            _device.Dispose();
        }

        /// <summary>Writes the key's state and the report that ends it, as two input_event structs.</summary>
        private void Write(int value)
        {
            Span<byte> events = stackalloc byte[48];
            events.Clear();

            // After the 16-byte timestamp: type, code and value. The second event stays zero: EV_SYN, SYN_REPORT.
            BitConverter.TryWriteBytes(events[16..], (ushort)EvKey);
            BitConverter.TryWriteBytes(events[18..], (ushort)KeyInsert);
            BitConverter.TryWriteBytes(events[20..], value);
            _device.Write(events);
        }

        [DllImport("libc", SetLastError = true)]
        private static extern int ioctl(int descriptor, nuint request, nint argument);
    }
}
