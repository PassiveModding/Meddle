using System.Text.Json;

namespace Meddle.UI;

public class Configuration
{
    public string GameDirectory { get; set; } = string.Empty;
    public int WindowX { get; set; }
    public int WindowY { get; set; }
    public int InteropPort { get; set; }
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public int DisplayScale { get; set; }
    public int FpsLimit { get; set; }
    public bool AssetCcResolve { get; set; }
    
    public static Configuration Load()
    {
        if (!File.Exists(Path.Combine(Program.DataDirectory, "config.json")))
        {
            return new Configuration
            {
                WindowX = 100,
                WindowY = 100,
                WindowWidth = 1280,
                WindowHeight = 720,
                DisplayScale = 1,
                FpsLimit = 60,
                InteropPort = 5000,
                AssetCcResolve = false
            };
        }
        
        return JsonSerializer.Deserialize<Configuration>(File.ReadAllText(Path.Combine(Program.DataDirectory, "config.json")))!;
    }
    
    public void Save()
    {
        File.WriteAllText(Path.Combine(Program.DataDirectory, "config.json"), JsonSerializer.Serialize(this));
    }
}
