using System.Runtime.InteropServices;

namespace G29WASDTranslator;

public sealed class KeyboardEmitter : IDisposable
{
    private readonly object _lock = new();
    private readonly HashSet<ushort> _held = [];

    [StructLayout(LayoutKind.Sequential)]
    private struct KeybdInput
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public nuint dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public KeybdInput ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint type;
        public InputUnion union;
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint cInputs, Input[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nuint dwExtraInfo);

    private const uint KEYEVENTF_KEYBD_EVENT_KEYUP = 0x0002;

    public void SetHeld(ushort virtualKey, bool held)
    {
        lock (_lock)
        {
            if (held && _held.Add(virtualKey))
                Send(virtualKey, false);
            else if (!held && _held.Remove(virtualKey))
                Send(virtualKey, true);
        }
    }

    public void ReleaseAll()
    {
        lock (_lock)
        {
            foreach (var key in _held.ToArray())
                Send(key, true);
            _held.Clear();
        }
    }

    private void Send(ushort virtualKey, bool keyUp)
    {
        var input = new Input
        {
            type = INPUT_KEYBOARD,
            union = new InputUnion
            {
                ki = new KeybdInput
                {
                    wVk = virtualKey,
                    wScan = 0,
                    dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
                    dwExtraInfo = 0
                }
            }
        };
        var cbSize = Marshal.SizeOf<Input>();
        var sent = SendInput(1, [input], cbSize);
        if (sent != 1)
            Log.Write($"SendInput failed for 0x{virtualKey:X2}; sent={sent}; cbSize={cbSize}; win32 error={Marshal.GetLastPInvokeError()}");

        keybd_event((byte)virtualKey, 0,
            (keyUp ? KEYEVENTF_KEYBD_EVENT_KEYUP : 0), 0);
    }

    public void Dispose() => ReleaseAll();
}
