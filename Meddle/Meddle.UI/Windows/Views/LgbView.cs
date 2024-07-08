using System.Runtime.CompilerServices;
using ImGuiNET;
using Meddle.Utils;
using Meddle.Utils.Files;

namespace Meddle.UI.Windows.Views;

public class LgbView : IView
{
    private readonly LgbFile lgbFile;

    public LgbView(LgbFile lgbFile)
    {
        this.lgbFile = lgbFile;
    }
    
    public void Draw()
    {
        ImGui.Text($"Groups: {lgbFile.Header.GroupCount}");
        
        var reader = new SpanBinaryReader(lgbFile.RawData);
        for (var i = 0; i < lgbFile.Header.GroupCount; i++)
        {
            ImGui.PushID(i);
            var layer = lgbFile.Groups[i];
            var nameOffset = Unsafe.SizeOf<LgbFile.HeaderData>() + lgbFile.GroupOffsets[i] + layer.Header.NameOffset;
            var name = reader.ReadString((int)nameOffset);
            if (ImGui.CollapsingHeader($"Layer {i}: {name}"))
            {
                ImGui.Text($"Layer Set Count: {layer.ReferencedList.LayerSetCount}");
                ImGui.Text($"Instance Object Count: {layer.InstanceObjects.Length}");
                ImGui.Text($"Layer Set Reference Count: {layer.ReferencedList.LayerSetCount}");
                ImGui.Text($"OB Set Reference Count: {layer.Header.ObSetReferencedListCount}");
                ImGui.Text($"OB Set Enable Reference Count: {layer.Header.ObSetEnableReferencedListCount}");

                if (layer.InstanceObjects.Length > 0)
                {
                    ImGui.Indent();
                    if (ImGui.CollapsingHeader("Instance Objects"))
                    {
                        ImGui.Indent();
                        for (var j = 0; j < layer.InstanceObjects.Length; j++)
                        {
                            var instanceObject = layer.InstanceObjects[j];
                            if (ImGui.CollapsingHeader($"Instance Object {j}: {instanceObject.Type}"))
                            {
                                ImGui.Text($"Type: {instanceObject.Type}");
                                ImGui.Text($"Position: {instanceObject.Translation}");
                                ImGui.Text($"Rotation: {instanceObject.Rotation}");
                                ImGui.Text($"Scale: {instanceObject.Scale}");

                                if (instanceObject.Type == LgbFile.LayerEntryType.BG)
                                {
                                    var paths = LgbFileExtensions.GetBgInstanceObject(lgbFile, i, j);
                                    ImGui.Text(paths.ModelPath);
                                    ImGui.Text(paths.CollisionPath);
                                }
                            }
                        }
                        ImGui.Unindent();
                    }
                    ImGui.Unindent();
                }
            }
            ImGui.PopID();
        }
    }
}
