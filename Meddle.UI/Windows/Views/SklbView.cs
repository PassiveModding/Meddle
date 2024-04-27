using ImGuiNET;
using Meddle.Utils.Files;

namespace Meddle.UI.Windows.Views;

public class SklbView : IView
{
    private readonly SklbFile file;
    private readonly HexView hexView;

    public SklbView(SklbFile file)
    {
        this.file = file;
        this.hexView = new(file.RawData);
    }
    
    public void Draw()
    {
        ImGui.Text($"Version: {file.Header.Version} [{(uint)file.Header.Version:X8}]");
        ImGui.Text($"Old Header: {file.Header.OldHeader}");
        
        if (ImGui.CollapsingHeader("Raw Data"))
        {
            hexView.DrawHexDump();
        }
    }
}
