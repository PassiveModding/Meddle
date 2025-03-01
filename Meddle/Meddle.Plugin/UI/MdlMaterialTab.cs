using System.Numerics;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.Interop;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Services.UI;
using Meddle.Plugin.Utils;
using Meddle.Utils.Constants;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;

namespace Meddle.Plugin.UI;

public class MdlMaterialTab : ITab
{
    private readonly SqPack pack;
    private static Pointer<ModelResourceHandle> ModelPtr { get; set; }
    private readonly Dictionary<string, ShpkFile> shpkCache = new();
    
    public MenuType MenuType => MenuType.Debug;
    
    public int Order => 3;
    
    public string Name => "Model Material Parameters";

    public MdlMaterialTab(SqPack pack)
    {
        this.pack = pack;
    }
    
    public static void SetModel(Pointer<ModelResourceHandle> modelPtr)
    {
        ModelPtr = modelPtr;
    }

    public unsafe void Draw()
    {
        if (ModelPtr == null || ModelPtr.Value == null)
        {
            ImGui.Text("No model selected.");
            return;
        }

        var modelName = ModelPtr.Value->FileName.ParseString();
        using var modelId = ImRaii.PushId(modelName);
        
        ImGui.Text($"Model: {modelName}");
        
        var materials = StructExtensions.GetMaterials(ModelPtr.Value);
        ImGui.Text($"Material Count: {materials.Length}");

        for (var i = 0; i < materials.Length; i++)
        {
            var material = materials[i];
            if (material == null || material.Value == null) continue;
            using var materialId = ImRaii.PushId(i);
            var shpkName = material.Value->ShpkNameString;
            using var materialNode = ImRaii.TreeNode($"[{shpkName}]Material {i}: {material.Value->FileName.ParseString()}");
            if (!materialNode.Success) continue;
            DrawMtrl(material);
        }
    }
    
    private Dictionary<nint, float[]> materialParamsCache = new();
    
    public unsafe void DrawMtrl(Pointer<MaterialResourceHandle> mtrlPtr)
    {
        if (mtrlPtr == null || mtrlPtr.Value == null)
        {
            ImGui.Text("No material selected.");
            return;
        }
        
        var material = mtrlPtr.Value->Material;
        if (material == null)
        {
            ImGui.Text("Material data is null.");
            return;
        }

        var materialParams = material->MaterialParameterCBuffer->TryGetBuffer<float>();

        var shpkName = mtrlPtr.Value->ShpkNameString;
        var shpkPath = $"shader/sm5/shpk/{shpkName}";
        if (!shpkCache.TryGetValue(shpkPath, out var shpk))
        {
            var shpkData = pack.GetFileOrReadFromDisk(shpkPath);
            if (shpkData != null)
            {
                shpk = new ShpkFile(shpkData);
                shpkCache[shpkPath] = shpk;
            }
            else
            {
                throw new Exception($"Failed to load {shpkPath}");
            }
        }
        
        ImGui.Text($"Shader Package: {shpkName}");
        var orderedMaterialParams = shpk.MaterialParams.Select((x, idx) => (x, idx))
                                        .OrderBy(x => x.idx).ToArray();

        var availWidth = ImGui.GetContentRegionAvail().X;
        using var table = ImRaii.Table("MaterialParams", 7, ImGuiTableFlags.Borders |
                                                            ImGuiTableFlags.RowBg |
                                                            ImGuiTableFlags.Hideable |
                                                            ImGuiTableFlags.Resizable);
        ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed,
                               availWidth * 0.05f);
        ImGui.TableSetupColumn("Offset", ImGuiTableColumnFlags.WidthFixed,
                               availWidth * 0.05f);
        ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed,
                               availWidth * 0.05f);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed,
                               availWidth * 0.2f);
        ImGui.TableSetupColumn("Shader Defaults", ImGuiTableColumnFlags.WidthFixed,
                               availWidth * 0.12f);
        ImGui.TableSetupColumn("Mtrl CBuf", ImGuiTableColumnFlags.WidthFixed,
                               availWidth * 0.1f);
        ImGui.TableSetupColumn("Edit", ImGuiTableColumnFlags.WidthFixed);

        ImGui.TableHeadersRow();
        
        var nameConstants = Names.GetConstants();
        if (!materialParamsCache.TryGetValue((nint)mtrlPtr.Value, out var materialParamsArray))
        {
            materialParamsArray = materialParams.ToArray();
            materialParamsCache[(nint)mtrlPtr.Value] = materialParamsArray;
        }
        
        foreach (var (materialParam, i) in orderedMaterialParams)
        {
            using var paramId = ImRaii.PushId($"{materialParam.GetHashCode()}_{i}_{shpkName}");

            var shpkDefaults = shpk.MaterialParamDefaults
                                   .Skip(materialParam.ByteOffset / 4)
                                   .Take(materialParam.ByteSize / 4).ToArray();

            var cbufCache = materialParamsArray.Skip(materialParam.ByteOffset / 4)
                                               .Take(materialParam.ByteSize / 4)
                                               .ToArray();

            string nameLookup;
            if (nameConstants.TryGetValue(materialParam.Id, out var crcPair))
            {
                nameLookup = $"{crcPair.Value} (0x{materialParam.Id:X8})";
            }
            else
            {
                nameLookup = $"0x{materialParam.Id:X8}";
            }
            
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text(i.ToString());
            ImGui.TableNextColumn();
            ImGui.Text($"{materialParam.ByteOffset}");
            ImGui.TableNextColumn();
            ImGui.Text($"{materialParam.ByteSize}");
            ImGui.TableNextColumn();
            ImGui.Text(nameLookup);
            if (ImGui.IsItemClicked())
            {
                ImGui.SetClipboardText(nameLookup);
            }

            ImGui.TableNextColumn();
            var shpkDefaultString =
                string.Join(", ", shpkDefaults.Select(x => x.ToString("F2")));
            ImGui.Text(shpkDefaultString);
            ImGui.TableNextColumn();

            var mtrlCbufString =
                string.Join(", ", cbufCache.Select(x => x.ToString("F2")));

            ImGui.Text(mtrlCbufString);
            ImGui.TableNextColumn();

            var currentVal = materialParams.Slice(materialParam.ByteOffset / 4,
                                                  materialParam.ByteSize / 4);

            var changed = !currentVal.SequenceEqual(cbufCache);
            using var style = ImRaii.PushColor(ImGuiCol.Text,
                                               changed
                                                   ? new Vector4(1, 0, 0, 1)
                                                   : new Vector4(1, 1, 1, 1));

            // if length 2, assume it's a vector2
            if (currentVal.Length == 2)
            {
                var v2 = new Vector2(currentVal[0], currentVal[1]);
                ImGui.InputFloat2($"##{materialParam.GetHashCode()}", ref v2);
                currentVal[0] = v2.X;
                currentVal[1] = v2.Y;
            }
            else if (currentVal.Length == 3)
            {
                var v3 = new Vector3(currentVal[0], currentVal[1], currentVal[2]);
                ImGui.InputFloat3($"##{materialParam.GetHashCode()}", ref v3);
                currentVal[0] = v3.X;
                currentVal[1] = v3.Y;
                currentVal[2] = v3.Z;
            }
            else if (currentVal.Length == 4)
            {
                var v4 = new Vector4(
                    currentVal[0], currentVal[1], currentVal[2], currentVal[3]);
                ImGui.InputFloat4($"##{materialParam.GetHashCode()}", ref v4);
                currentVal[0] = v4.X;
                currentVal[1] = v4.Y;
                currentVal[2] = v4.Z;
                currentVal[3] = v4.W;
            }
            else
            {
                for (var j = 0; j < currentVal.Length; j++)
                {
                    if (j > 0)
                    {
                        ImGui.SameLine();
                    }

                    ImGui.InputFloat($"##{materialParam.GetHashCode()}_{j}",
                                     ref currentVal[j], 0.1f, 1.0f, "%.2f");
                }
            }

            ImGui.SameLine();
            if (ImGui.Button($"Restore##{materialParam.GetHashCode()}"))
            {
                cbufCache.CopyTo(currentVal);
            }
        }
    }
    
    public void Dispose()
    {}
}
