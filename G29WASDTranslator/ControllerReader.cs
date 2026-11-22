using SharpDX.DirectInput;

namespace G29WASDTranslator;

public sealed class ControllerSnapshot
{
    public bool Connected { get; init; }
    public string DeviceName { get; init; } = "No device";
    public IReadOnlyDictionary<AxisName, int> Axes { get; init; } =
        new Dictionary<AxisName, int>();
}

public sealed class ControllerReader : IDisposable
{
    private readonly DirectInput _directInput = new();
    private Joystick? _joystick;
    private Thread? _thread;
    private volatile bool _running;
    private volatile ControllerSnapshot _snapshot = new();

    public ControllerSnapshot Snapshot => _snapshot;

    public void Start()
    {
        if (_running) return;
        _running = true;
        _thread = new Thread(PollLoop) { IsBackground = true, Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        _thread?.Join(500);
        _joystick?.Unacquire();
        _joystick?.Dispose();
        _joystick = null;
    }

    private void PollLoop()
    {
        var lastScan = DateTime.UtcNow - TimeSpan.FromSeconds(1);
        while (_running)
        {
            if (_joystick == null)
            {
                if (DateTime.UtcNow - lastScan > TimeSpan.FromMilliseconds(500))
                {
                    lastScan = DateTime.UtcNow;
                    TryConnect();
                }
                Thread.Sleep(20);
                continue;
            }

            try
            {
                _joystick.Acquire();
                _joystick.Poll();
                var state = _joystick.GetCurrentState();
                var sliders = state.Sliders ?? [];
                _snapshot = new ControllerSnapshot
                {
                    Connected = true,
                    DeviceName = _joystick.Information.ProductName,
                    Axes = new Dictionary<AxisName, int>
                    {
                        [AxisName.X] = state.X,
                        [AxisName.Y] = state.Y,
                        [AxisName.Z] = state.Z,
                        [AxisName.RotationX] = state.RotationX,
                        [AxisName.RotationY] = state.RotationY,
                        [AxisName.RotationZ] = state.RotationZ,
                        [AxisName.Slider1] = sliders.Length > 0 ? sliders[0] : 32768,
                        [AxisName.Slider2] = sliders.Length > 1 ? sliders[1] : 32768
                    }
                };
            }
            catch (SharpDX.SharpDXException)
            {
                Disconnect();
                Thread.Sleep(50);
            }
            Thread.Sleep(5);
        }
    }

    private void TryConnect()
    {
        try
        {
            var devices = _directInput.GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AllDevices);
            var device = devices.FirstOrDefault(d =>
                d.ProductName.Contains("G29", StringComparison.OrdinalIgnoreCase) ||
                d.ProductName.Contains("Logitech", StringComparison.OrdinalIgnoreCase))
                ?? devices.FirstOrDefault();
            if (device == null) return;
            if (device.InstanceGuid == Guid.Empty) return;

            var joystick = new Joystick(_directInput, device.InstanceGuid);
            joystick.SetCooperativeLevel(IntPtr.Zero, CooperativeLevel.Background | CooperativeLevel.NonExclusive);
            joystick.Properties.BufferSize = 128;
            joystick.Acquire();
            _joystick = joystick;
        }
        catch
        {
            _snapshot = new ControllerSnapshot();
        }
    }

    private void Disconnect()
    {
        try { _joystick?.Unacquire(); } catch { }
        _joystick?.Dispose();
        _joystick = null;
        _snapshot = new ControllerSnapshot();
    }

    public void Dispose()
    {
        Stop();
        _directInput.Dispose();
    }
}
