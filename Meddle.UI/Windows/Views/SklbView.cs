using ImGuiNET;
using Meddle.Utils.Files;

namespace Meddle.UI.Windows.Views;

public class SklbView : IView
{
    private readonly SklbFile file;
    private readonly HexView hexView;
    private readonly Configuration configuration;

    public SklbView(SklbFile file, Configuration configuration)
    {
        this.file = file;
        this.hexView = new(file.RawData);
        this.configuration = configuration;
    }
    
    private Task<string>? parseTask;

    private void RunParse()
    {
        parseTask = Task.Run(async () =>
        {                
            var tempPath = Path.GetTempFileName();
            File.WriteAllBytes(tempPath, file.Skeleton.ToArray());

            using var message = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{configuration.InteropPort}/parsesklb");
            using var content = new StringContent(tempPath);
            message.Content = content;
            using var client = new HttpClient();
            var response = await client.SendAsync(message);
            return await response.Content.ReadAsStringAsync();
        });
    }
    
    public void Draw()
    {
        ImGui.Text($"Version: {file.Header.Version} [{(uint)file.Header.Version:X8}]");
        ImGui.Text($"Old Header: {file.Header.OldHeader}");

        if (ImGui.CollapsingHeader("Havok"))
        {
            if (ImGui.Button("Parse"))
            {
                RunParse();
            }

            if (parseTask?.IsFaulted == true)
            {
                ImGui.Text($"Error: {parseTask.Exception?.Message}");
            }
            else if (parseTask?.IsCompleted == true)
            {
                ImGui.TextUnformatted(parseTask.Result);
            }
            else if (parseTask?.IsCompleted == false)
            {
                ImGui.Text("Parsing...");
            }
        }

        if (ImGui.CollapsingHeader("Raw Data"))
        {
            hexView.DrawHexDump();
        }
    }
}
