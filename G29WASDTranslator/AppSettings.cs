using System.IO;
using System.Text.Json;

namespace G29WASDTranslator;

public sealed class AppSettings
{
    public int SettingsVersion { get; set; } = 3;
    public AxisName SteeringAxis { get; set; } = AxisName.X;
    public AxisName AcceleratorAxis { get; set; } = AxisName.Y;
    public AxisName BrakeAxis { get; set; } = AxisName.RotationZ;
    public int SteeringDeadzonePercent { get; set; } = 5;
    public int SteeringThresholdPercent { get; set; } = 12;
    public int AcceleratorThresholdPercent { get; set; } = 8;
    public int BrakeThresholdPercent { get; set; } = 8;
    public bool InvertSteering { get; set; }
    public bool InvertAccelerator { get; set; } = true;
    public bool InvertBrake { get; set; } = true;
    public bool LaunchAtStartup { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public int SteeringCenter { get; set; } = 32768;
    public int AcceleratorMin { get; set; }
    public int AcceleratorMax { get; set; }
    public int BrakeMin { get; set; }
    public int BrakeMax { get; set; }

    private static string SettingsDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "G29WASDTranslator");

    public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
                if (settings.SettingsVersion < 3)
                {
                    settings.SettingsVersion = 3;
                    settings.AcceleratorAxis = AxisName.Y;
                    settings.BrakeAxis = AxisName.RotationZ;
                    settings.InvertAccelerator = true;
                    settings.InvertBrake = true;
                    settings.AcceleratorMin = 0;
                    settings.AcceleratorMax = 0;
                    settings.BrakeMin = 0;
                    settings.BrakeMax = 0;
                    settings.Save();
                }
                return settings;
            }
        }
        catch
        {
        }
        return new AppSettings();
    }

    public void Save()
    {
        Directory.CreateDirectory(SettingsDirectory);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
