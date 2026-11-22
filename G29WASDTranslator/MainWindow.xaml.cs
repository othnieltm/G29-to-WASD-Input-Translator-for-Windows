using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using WpfColor = System.Windows.Media.Color;

namespace G29WASDTranslator;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly ControllerReader _controller = new();
    private readonly KeyboardEmitter _keyboard = new();
    private Forms.NotifyIcon _trayIcon = null!;
    private readonly Forms.ContextMenuStrip _trayMenu = new();
    private readonly DispatcherTimer _uiTimer;
    private Thread? _translationThread;
    private volatile bool _translationRunning;
    private volatile bool _isStopping;
    private bool _allowClose;
    private bool _applyingSteeringPreset;
    private bool _pedalCalibration;
    private DateTime _pedalCalibrationStarted;
    private int _acceleratorCalibrationMin = 65535;
    private int _acceleratorCalibrationMax;
    private int _brakeCalibrationMin = 65535;
    private int _brakeCalibrationMax;

    public MainWindow()
    {
        InitializeComponent();
        InitializeControls();
        InitializeTray();

        _controller.Start();
        _uiTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _uiTimer.Tick += UiTimerTick;
        _uiTimer.Start();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => EmergencyRelease();
        SystemEvents.SessionEnding += (_, _) => EmergencyRelease();
    }

    private void InitializeControls()
    {
        foreach (var axis in Enum.GetValues<AxisName>())
        {
            var name = axis.ToString();
            SteeringAxisCombo.Items.Add(name);
            AcceleratorAxisCombo.Items.Add(name);
            BrakeAxisCombo.Items.Add(name);
        }
        SteeringAxisCombo.SelectedItem = _settings.SteeringAxis.ToString();
        AcceleratorAxisCombo.SelectedItem = _settings.AcceleratorAxis.ToString();
        BrakeAxisCombo.SelectedItem = _settings.BrakeAxis.ToString();

        DeadzoneSlider.Value = _settings.SteeringDeadzonePercent;
        SteeringThresholdSlider.Value = _settings.SteeringThresholdPercent;
        AcceleratorThresholdSlider.Value = _settings.AcceleratorThresholdPercent;
        BrakeThresholdSlider.Value = _settings.BrakeThresholdPercent;
        InvertSteeringCheck.IsChecked = _settings.InvertSteering;
        InvertAcceleratorCheck.IsChecked = _settings.InvertAccelerator;
        InvertBrakeCheck.IsChecked = _settings.InvertBrake;
        StartupCheck.IsChecked = _settings.LaunchAtStartup;
        MinimizeToTrayCheck.IsChecked = _settings.MinimizeToTray;
        SteeringPresetCombo.SelectedIndex =
            _settings.SteeringDeadzonePercent == 8 && _settings.SteeringThresholdPercent == 20 ? 0 :
            _settings.SteeringDeadzonePercent == 5 && _settings.SteeringThresholdPercent == 12 ? 1 :
            _settings.SteeringDeadzonePercent == 2 && _settings.SteeringThresholdPercent == 5 ? 2 : 3;
        UpdateSettingLabels();

        SteeringAxisCombo.SelectionChanged += (_, _) => SaveSettings();
        AcceleratorAxisCombo.SelectionChanged += (_, _) => SaveSettings();
        BrakeAxisCombo.SelectionChanged += (_, _) => SaveSettings();
        DeadzoneSlider.ValueChanged += (_, _) => { MarkCustomSteeringPreset(); UpdateSettingLabels(); SaveSettings(); };
        SteeringThresholdSlider.ValueChanged += (_, _) => { MarkCustomSteeringPreset(); UpdateSettingLabels(); SaveSettings(); };
        AcceleratorThresholdSlider.ValueChanged += (_, _) => { UpdateSettingLabels(); SaveSettings(); };
        BrakeThresholdSlider.ValueChanged += (_, _) => { UpdateSettingLabels(); SaveSettings(); };
        InvertSteeringCheck.Checked += (_, _) => SaveSettings();
        InvertSteeringCheck.Unchecked += (_, _) => SaveSettings();
        InvertAcceleratorCheck.Checked += (_, _) => SaveSettings();
        InvertAcceleratorCheck.Unchecked += (_, _) => SaveSettings();
        InvertBrakeCheck.Checked += (_, _) => SaveSettings();
        InvertBrakeCheck.Unchecked += (_, _) => SaveSettings();
        StartupCheck.Checked += (_, _) => { SetStartup(true); SaveSettings(); };
        StartupCheck.Unchecked += (_, _) => { SetStartup(false); SaveSettings(); };
        MinimizeToTrayCheck.Checked += (_, _) => SaveSettings();
        MinimizeToTrayCheck.Unchecked += (_, _) => SaveSettings();
    }

    private void InitializeTray()
    {
        _trayMenu.Items.Add("Show", null, (_, _) => ShowFromTray());
        _trayMenu.Items.Add("Start translation", null, (_, _) =>
        {
            if (!_translationRunning) Dispatcher.Invoke(StartTranslation);
        });
        _trayMenu.Items.Add("Stop translation", null, (_, _) =>
        {
            if (_translationRunning) Dispatcher.Invoke(StopTranslation);
        });
        _trayMenu.Items.Add(new Forms.ToolStripSeparator());
        _trayMenu.Items.Add("Exit", null, (_, _) =>
        {
            Dispatcher.Invoke(ExitApplication);
        });
        _trayIcon = new Forms.NotifyIcon
        {
            Text = "G29 to WASD Translator",
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = _trayMenu
        };
        _trayIcon.DoubleClick += (_, _) => ShowFromTray();
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void StartTranslation()
    {
        if (_translationRunning) return;
        _translationRunning = true;
        _translationThread = new Thread(TranslationLoop)
        {
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };
        _translationThread.Start();
        StartStopButton.Content = "Stop Translation";
        RunningText.Text = "Running";
        RunningText.Foreground = new SolidColorBrush(WpfColor.FromRgb(55, 180, 110));
        Log.Write("Translation started");
    }

    private void StopTranslation()
    {
        if (!_translationRunning) return;
        _translationRunning = false;
        _translationThread?.Join(500);
        _keyboard.ReleaseAll();
        StartStopButton.Content = "Start Translation";
        RunningText.Text = "Stopped";
        RunningText.Foreground = new SolidColorBrush(WpfColor.FromRgb(170, 179, 189));
        ClearKeyHighlights();
        Log.Write("Translation stopped");
    }

    private void TranslationLoop()
    {
        while (_translationRunning)
        {
            var snapshot = _controller.Snapshot;
            if (!snapshot.Connected)
            {
                _keyboard.ReleaseAll();
                Thread.Sleep(5);
                continue;
            }

            var steering = ReadAxis(snapshot, _settings.SteeringAxis);
            var accelerator = ReadAxis(snapshot, _settings.AcceleratorAxis);
            var brake = ReadAxis(snapshot, _settings.BrakeAxis);

            if (_settings.InvertSteering)
                steering = (65535 - steering);
            var steeringPercent = (steering - _settings.SteeringCenter) / 32767.0;
            steeringPercent = Math.Clamp(steeringPercent, -1.0, 1.0);
            var deadzone = _settings.SteeringDeadzonePercent / 100.0;
            var threshold = _settings.SteeringThresholdPercent / 100.0;

            var steeringActivation = Math.Max(deadzone, threshold);
            var left = steeringPercent < -steeringActivation;
            var right = steeringPercent > steeringActivation;

            var acceleratorPercent = NormalizePedalPercent(accelerator, _settings.AcceleratorMin,
                _settings.AcceleratorMax, _settings.InvertAccelerator);
            var brakePercent = NormalizePedalPercent(brake, _settings.BrakeMin,
                _settings.BrakeMax, _settings.InvertBrake);
            var acceleratorHeld = acceleratorPercent >= _settings.AcceleratorThresholdPercent;
            var brakeHeld = brakePercent >= _settings.BrakeThresholdPercent;

            _keyboard.SetHeld(0x57, acceleratorHeld);
            _keyboard.SetHeld(0x53, brakeHeld);
            _keyboard.SetHeld(0x41, left);
            _keyboard.SetHeld(0x44, right);
            Thread.Sleep(5);
        }
        _keyboard.ReleaseAll();
    }

    private static int ReadAxis(ControllerSnapshot snapshot, AxisName axis)
    {
        return snapshot.Axes.TryGetValue(axis, out var value) ? value : 32768;
    }

    private void UiTimerTick(object? sender, EventArgs e)
    {
        var snapshot = _controller.Snapshot;
        if (_pedalCalibration)
            UpdatePedalCalibration(snapshot);

        if (snapshot.Connected)
        {
            StatusDot.Fill = new SolidColorBrush(WpfColor.FromRgb(55, 180, 110));
            StatusText.Text = $"Connected: {snapshot.DeviceName}";
            RawAxisValue.Text = "Raw axes: " + string.Join(", ",
                snapshot.Axes.Select(a => $"{a.Key}={a.Value}"));
        }
        else
        {
            StatusDot.Fill = new SolidColorBrush(WpfColor.FromRgb(224, 90, 90));
            StatusText.Text = "G29 disconnected - waiting for reconnection";
        }

        var steering = ReadAxis(snapshot, _settings.SteeringAxis);
        if (_settings.InvertSteering) steering = 65535 - steering;
        var steeringPercent = (steering - _settings.SteeringCenter) / 32767.0;
        steeringPercent = Math.Clamp(steeringPercent, -1.0, 1.0);
        UpdateSteeringMarker(steeringPercent);
        SteeringValue.Text = $"{Math.Round(steeringPercent * 100):0}%";

        var accelerator = ReadAxis(snapshot, _settings.AcceleratorAxis);
        var acceleratorPercent = NormalizePedalPercent(accelerator, _settings.AcceleratorMin,
            _settings.AcceleratorMax, _settings.InvertAccelerator);
        AcceleratorBar.Value = acceleratorPercent;
        AcceleratorValue.Text = $"{acceleratorPercent:0}%";

        var brake = ReadAxis(snapshot, _settings.BrakeAxis);
        var brakePercent = NormalizePedalPercent(brake, _settings.BrakeMin,
            _settings.BrakeMax, _settings.InvertBrake);
        BrakeBar.Value = brakePercent;
        BrakeValue.Text = $"{brakePercent:0}%";

        if (_translationRunning)
        {
            var deadzone = _settings.SteeringDeadzonePercent / 100.0;
            var threshold = _settings.SteeringThresholdPercent / 100.0;
            var steeringThreshold = Math.Max(deadzone, threshold);
            HighlightKey(KeyA, steeringPercent < -steeringThreshold);
            HighlightKey(KeyD, steeringPercent > steeringThreshold);
            HighlightKey(KeyW, acceleratorPercent >= _settings.AcceleratorThresholdPercent);
            HighlightKey(KeyS, brakePercent >= _settings.BrakeThresholdPercent);
        }
    }

    private void UpdateSteeringMarker(double position)
    {
        var width = Math.Max(0, ((Grid)SteeringMarker.Parent).ActualWidth);
        var left = width * (position + 1.0) / 2.0 - SteeringMarker.Width / 2.0;
        SteeringMarker.Margin = new Thickness(Math.Clamp(left, 0, width - SteeringMarker.Width), 0, 0, 0);
    }

    private void HighlightKey(Border key, bool active)
    {
        key.Background = active
            ? new SolidColorBrush(WpfColor.FromRgb(38, 132, 255))
            : new SolidColorBrush(WpfColor.FromRgb(203, 211, 220));
    }

    private void ClearKeyHighlights()
    {
        foreach (var key in new[] { KeyW, KeyA, KeyS, KeyD })
            key.Background = new SolidColorBrush(WpfColor.FromRgb(203, 211, 220));
    }

    private void UpdateSettingLabels()
    {
        if (!DeadzoneText.IsKeyboardFocusWithin)
            DeadzoneText.Text = $"{DeadzoneSlider.Value:0}%";
        if (!SteeringThresholdText.IsKeyboardFocusWithin)
            SteeringThresholdText.Text = $"{SteeringThresholdSlider.Value:0}%";
        if (!AcceleratorThresholdText.IsKeyboardFocusWithin)
            AcceleratorThresholdText.Text = $"{AcceleratorThresholdSlider.Value:0}%";
        if (!BrakeThresholdText.IsKeyboardFocusWithin)
            BrakeThresholdText.Text = $"{BrakeThresholdSlider.Value:0}%";
    }

    private void ThresholdText_LostFocus(object sender, RoutedEventArgs e)
    {
        ApplyThresholdText((System.Windows.Controls.TextBox)sender);
    }

    private void ThresholdText_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            ApplyThresholdText((System.Windows.Controls.TextBox)sender);
            e.Handled = true;
        }
    }

    private void ApplyThresholdText(System.Windows.Controls.TextBox textBox)
    {
        var text = textBox.Text.Trim().TrimEnd('%');
        if (!double.TryParse(text, out var value))
        {
            UpdateSettingLabels();
            return;
        }

        if (textBox == DeadzoneText)
            DeadzoneSlider.Value = Math.Clamp(value, 0, 30);
        else if (textBox == SteeringThresholdText)
            SteeringThresholdSlider.Value = Math.Clamp(value, 1, 40);
        else if (textBox == AcceleratorThresholdText)
            AcceleratorThresholdSlider.Value = Math.Clamp(value, 0, 90);
        else if (textBox == BrakeThresholdText)
            BrakeThresholdSlider.Value = Math.Clamp(value, 0, 90);

        UpdateSettingLabels();
        SaveSettings();
    }

    private void SteeringPresetCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (SteeringPresetCombo.SelectedIndex < 0)
            return;

        _applyingSteeringPreset = true;
        switch (SteeringPresetCombo.SelectedIndex)
        {
            case 0:
                DeadzoneSlider.Value = 8;
                SteeringThresholdSlider.Value = 20;
                break;
            case 1:
                DeadzoneSlider.Value = 5;
                SteeringThresholdSlider.Value = 12;
                break;
            case 2:
                DeadzoneSlider.Value = 2;
                SteeringThresholdSlider.Value = 5;
                break;
        }
        _applyingSteeringPreset = false;
        UpdateSettingLabels();
        SaveSettings();
    }

    private void MarkCustomSteeringPreset()
    {
        if (!_applyingSteeringPreset && SteeringPresetCombo.SelectedIndex != 3)
            SteeringPresetCombo.SelectedIndex = 3;
    }

    private void ResetSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        DeadzoneSlider.Value = 5;
        SteeringThresholdSlider.Value = 12;
        AcceleratorThresholdSlider.Value = 8;
        BrakeThresholdSlider.Value = 8;
        SteeringPresetCombo.SelectedIndex = 1;
        UpdateSettingLabels();
        SaveSettings();
    }

    private void SaveSettings()
    {
        if (SteeringAxisCombo.SelectedItem != null)
            _settings.SteeringAxis = Enum.Parse<AxisName>(SteeringAxisCombo.SelectedItem.ToString()!);
        if (AcceleratorAxisCombo.SelectedItem != null)
            _settings.AcceleratorAxis = Enum.Parse<AxisName>(AcceleratorAxisCombo.SelectedItem.ToString()!);
        if (BrakeAxisCombo.SelectedItem != null)
            _settings.BrakeAxis = Enum.Parse<AxisName>(BrakeAxisCombo.SelectedItem.ToString()!);
        _settings.SteeringDeadzonePercent = (int)DeadzoneSlider.Value;
        _settings.SteeringThresholdPercent = (int)SteeringThresholdSlider.Value;
        _settings.AcceleratorThresholdPercent = (int)AcceleratorThresholdSlider.Value;
        _settings.BrakeThresholdPercent = (int)BrakeThresholdSlider.Value;
        _settings.InvertSteering = InvertSteeringCheck.IsChecked == true;
        _settings.InvertAccelerator = InvertAcceleratorCheck.IsChecked == true;
        _settings.InvertBrake = InvertBrakeCheck.IsChecked == true;
        _settings.LaunchAtStartup = StartupCheck.IsChecked == true;
        _settings.MinimizeToTray = MinimizeToTrayCheck.IsChecked == true;
        _settings.Save();
    }

    private void SetStartup(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run", writable: true)
                ?? Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            if (enabled)
                key?.SetValue("G29WASDTranslator", $"\"{Environment.ProcessPath}\"");
            else
                key?.DeleteValue("G29WASDTranslator", false);
        }
        catch (Exception ex)
        {
            Log.Write($"Startup setting failed: {ex.Message}");
        }
    }

    private void CalibrateButton_Click(object sender, RoutedEventArgs e)
    {
        var snapshot = _controller.Snapshot;
        var value = ReadAxis(snapshot, _settings.SteeringAxis);
        _settings.SteeringCenter = value;
        SaveSettings();
        Log.Write($"Steering center calibrated at {value}");
    }

    private void CalibratePedalsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_controller.Snapshot.Connected)
        {
            System.Windows.MessageBox.Show("Connect the G29 before calibrating pedals.", "G29 Translator",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _pedalCalibration = true;
        _pedalCalibrationStarted = DateTime.UtcNow;
        _acceleratorCalibrationMin = 65535;
        _acceleratorCalibrationMax = 0;
        _brakeCalibrationMin = 65535;
        _brakeCalibrationMax = 0;
        CalibratePedalsButton.Content = "Release and fully press both pedals";
        Log.Write("Pedal calibration started");
    }

    private void UpdatePedalCalibration(ControllerSnapshot snapshot)
    {
        if (!snapshot.Connected) return;

        var accelerator = ReadAxis(snapshot, _settings.AcceleratorAxis);
        var brake = ReadAxis(snapshot, _settings.BrakeAxis);
        _acceleratorCalibrationMin = Math.Min(_acceleratorCalibrationMin, accelerator);
        _acceleratorCalibrationMax = Math.Max(_acceleratorCalibrationMax, accelerator);
        _brakeCalibrationMin = Math.Min(_brakeCalibrationMin, brake);
        _brakeCalibrationMax = Math.Max(_brakeCalibrationMax, brake);

        var remaining = 8 - (DateTime.UtcNow - _pedalCalibrationStarted).TotalSeconds;
        CalibratePedalsButton.Content = remaining > 0
            ? $"Fully press both pedals: {Math.Ceiling(remaining):0}s"
            : "Processing pedal calibration...";

        if (remaining <= 0)
        {
            _pedalCalibration = false;
            if (_acceleratorCalibrationMax - _acceleratorCalibrationMin > 1000)
            {
                _settings.AcceleratorMin = _acceleratorCalibrationMin;
                _settings.AcceleratorMax = _acceleratorCalibrationMax;
            }
            if (_brakeCalibrationMax - _brakeCalibrationMin > 1000)
            {
                _settings.BrakeMin = _brakeCalibrationMin;
                _settings.BrakeMax = _brakeCalibrationMax;
            }
            SaveSettings();
            CalibratePedalsButton.Content = "Calibrate pedal ranges";
            Log.Write($"Pedal calibration saved: accelerator {_acceleratorCalibrationMin}-{_acceleratorCalibrationMax}, brake {_brakeCalibrationMin}-{_brakeCalibrationMax}");
        }
    }

    private static double NormalizePedalPercent(int raw, int minimum, int maximum, bool invert)
    {
        double percent;
        if (maximum - minimum >= 1000)
        {
            percent = (raw - minimum) / (double)(maximum - minimum);
        }
        else
        {
            percent = raw / 65535.0;
        }

        percent = Math.Clamp(percent, 0.0, 1.0);
        if (invert)
            percent = 1.0 - percent;
        return percent * 100.0;
    }

    private void StartStopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_translationRunning) StopTranslation(); else StartTranslation();
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        ExitApplication();
    }

    private void ExitApplication()
    {
        _allowClose = true;
        Close();
    }

    private void Window_StateChanged(object sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _settings.MinimizeToTray)
            Hide();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_allowClose && _settings.MinimizeToTray)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        EmergencyRelease();
        _translationRunning = false;
        _translationThread?.Join(500);
        _controller.Stop();
        _controller.Dispose();
        _keyboard.Dispose();
        _trayIcon.Visible = false;
        _trayIcon.Dispose();
        _trayMenu.Dispose();
    }

    private void EmergencyRelease()
    {
        if (_isStopping) return;
        _isStopping = true;
        try { _keyboard.ReleaseAll(); } catch { }
    }
}
