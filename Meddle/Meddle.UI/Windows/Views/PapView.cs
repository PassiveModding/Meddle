using ImGuiNET;
using Meddle.UI.Util;
using Meddle.Utils.Files;

namespace Meddle.UI.Windows.Views;

public class PapView : IView
{
    private readonly PapFile papFile;
    private readonly HexView havokHexView;
    private readonly HexView footerHexView;
    private readonly HexView hexView;

    public PapView(PapFile papFile)
    {
        this.papFile = papFile;
        this.havokHexView = new HexView(papFile.HavokData);
        this.footerHexView = new HexView(papFile.FooterData);
        this.hexView = new HexView(papFile.RawData);
    }

    private string? parsed;
    public void Draw()
    {
        ImGui.Text($"Variant: {papFile.FileHeader.Variant}");
        ImGui.Text($"Model ID: {papFile.FileHeader.ModelId}");
        ImGui.Text($"Model Type: {papFile.FileHeader.ModelType}");
        
        ImGui.Text($"Loaded {papFile.Animations.Length} animations");
        foreach (var anim in papFile.Animations)
        {
            if (ImGui.TreeNode($"{anim.GetName}##{anim.GetHashCode()}"))
            {
                ImGui.Text($"Type: {anim.Type}");
                ImGui.Text($"Havok Index: {anim.HavokIndex}");
                ImGui.Text($"Is Face: {anim.IsFace}");
                ImGui.TreePop();
            }
        }

        if (ImGui.Button("Parse"))
        {
            parsed = SkeletonUtil.ParseHavokInput(papFile.HavokData.ToArray());
        }
        
        if (ImGui.CollapsingHeader("XML") && parsed != null)
        {
            ImGui.TextUnformatted(parsed);
        }

        if (ImGui.CollapsingHeader("Havok Data"))
        {
            havokHexView.DrawHexDump();
        }
        
        if (ImGui.CollapsingHeader("Footer Data"))
        {
            footerHexView.DrawHexDump();
        }
        
        if (ImGui.CollapsingHeader("Raw Data"))
        {
            hexView.DrawHexDump();
        }
    }
}
