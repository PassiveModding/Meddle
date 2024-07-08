using ImGuiNET;
using Meddle.Utils.Files.SqPack;

namespace Meddle.UI.Windows.Views;

public class DefaultView(IndexHashTableEntry hash, SqPackFile file, string? message = null) : IView
{
    private readonly HexView hexView = new(file.RawData);
    private readonly IndexHashTableEntry hash = hash;

    public void Draw()
    {
        if (message != null)
        {
            ImGui.Text(message);
        }
        
        ImGui.Text($"Hash: {hash.Hash:X8}");
        var dataSize = file.RawData.Length;
        ImGui.Text($"Data Size: {dataSize}");
        ImGui.Text($"Data Offset: {hash.Offset:X8}");
        hexView.DrawHexDump();
    }
}
