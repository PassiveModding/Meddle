using System.Numerics;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.Interop;
using Dalamud.Bindings.ImGui;
using Meddle.Plugin.Models;
using Meddle.Plugin.Utils;
using Meddle.Utils.Constants;

namespace Meddle.Plugin.UI.Windows;

public class MdlMaterialWindow : Window
{
    private readonly MdlMaterialWindowManager windowManager;
    private readonly Pointer<ModelResourceHandle> model;
    private readonly Pointer<Model> model1;
    public readonly string Id;

    public unsafe MdlMaterialWindow(MdlMaterialWindowManager windowManager, Pointer<ModelResourceHandle> model) :
        base("Material Editor")
    {
        this.windowManager = windowManager;
        this.model = model;
        Id = $"{(nint)this.model.Value:X8}";
        WindowName = $"Material Editor {model.Value->FileName.ParseString()}###{Id}";
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 350),
            MaximumSize = new Vector2(1200, 1000)
        };
    }

    public unsafe MdlMaterialWindow(MdlMaterialWindowManager windowManager, Pointer<Model> model) :
        base("Material Editor")
    {
        this.windowManager = windowManager;
        this.model1 = model;
        Id = $"{(nint)model1.Value:X8}";
        WindowName = $"Material Editor {model.Value->ModelResourceHandle->FileName.ParseString()}###{Id}";
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(375, 350),
            MaximumSize = new Vector2(1200, 1000)
        };
    }


    public override void OnClose()
    {
        windowManager.RemoveMaterialWindow(this);
        base.OnClose();
    }

    public override void Draw()
    {
        if (model1 != null)
        {
            DrawModel(model1);
        }
        else
        {
            DrawModel(model);
        }
    }
    private unsafe void DrawModel(Pointer<Model> modelPtr)
    {
        if (modelPtr == null || modelPtr.Value == null)
        {
            ImGui.Text("Model data is null.");
            return;
        }

        var modelName = modelPtr.Value->ModelResourceHandle->FileName.ParseString();
        using var modelId = ImRaii.PushId(modelName);

        ImGui.Text($"Model: {modelName}");

        var materials = modelPtr.Value->MaterialsSpan;
        ImGui.Text($"Material Count: {materials.Length}");

        for (var i = 0; i < materials.Length; i++)
        {
            var material = materials[i];
            if (material == null || material.Value == null || material.Value->MaterialResourceHandle == null) continue;
            using var materialId = ImRaii.PushId(i);
            var shpkName = material.Value->MaterialResourceHandle->ShpkName;
            using var materialNode = ImRaii.TreeNode($"[{shpkName}]Material {i}: {material.Value->MaterialResourceHandle->FileName.ParseString()}");
            if (!materialNode.Success) continue;
            DrawMtrl(material.Value->MaterialResourceHandle);
            // DrawMtrlTextures(material.Value->MaterialResourceHandle);
            // using var materialParamNode = ImRaii.TreeNode("Material Parameters");
            // if (materialParamNode.Success)
            // {
            //     DrawMtrl(material.Value->MaterialResourceHandle);
            // }
            //
            // using var materialTexturesNode = ImRaii.TreeNode("Material Textures");
            // if (materialTexturesNode.Success)
            // {
            //     DrawMtrlTextures(material.Value->MaterialResourceHandle);
            // }
        }
    }

    private unsafe void DrawModel(Pointer<ModelResourceHandle> modelPtr)
    {
        if (modelPtr == null || modelPtr.Value == null)
        {
            ImGui.Text("Model data is null.");
            return;
        }

        var modelName = modelPtr.Value->FileName.ParseString();
        using var modelId = ImRaii.PushId(modelName);

        ImGui.Text($"Model: {modelName}");

        var materials = StructExtensions.GetMaterials(modelPtr.Value);
        ImGui.Text($"Material Count: {materials.Length}");

        for (var i = 0; i < materials.Length; i++)
        {
            var material = materials[i];
            if (material == null || material.Value == null) continue;
            using var materialId = ImRaii.PushId(i);
            var shpkName = material.Value->ShpkName;
            using var materialNode = ImRaii.TreeNode($"[{shpkName}]Material {i}: {material.Value->FileName.ParseString()}");
            if (!materialNode.Success) continue;
            DrawMtrl(material);
        }
    }

    private readonly Dictionary<nint, float[]> materialParamsCache = new();
    private readonly Dictionary<nint, (uint Key, uint Value)[]> materialKeysCache = new();
    private readonly Dictionary<nint, Dictionary<uint, string>> materialKeysEditors = new();

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

        var shpkName = mtrlPtr.Value->ShpkName;
        var shpkPath = $"shader/sm5/shpk/{shpkName}";
        var shpk = windowManager.GetShpkFile(shpkPath);

        ImGui.Text($"Shader Package: {shpkName}");

        if (ImGui.CollapsingHeader("Material Parameters",
                                   ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.AllowItemOverlap))
        {
            var materialParams = material->MaterialParameterCBuffer->TryGetBuffer<float>();
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
        if (ImGui.CollapsingHeader("Material Textures",
                                   ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.AllowItemOverlap))
        {

            var textures = mtrlPtr.Value->TexturesSpan;
            if (textures.Length == 0)
            {
                ImGui.Text("No textures.");
                return;
            }
            
            var availWidth = ImGui.GetContentRegionAvail().X;
            using var table = ImRaii.Table("MaterialTextures", 2, ImGuiTableFlags.Borders |
                                                                  ImGuiTableFlags.RowBg |
                                                                  ImGuiTableFlags.Hideable |
                                                                  ImGuiTableFlags.Resizable);
            
            ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, availWidth * 0.05f);
            ImGui.TableSetupColumn("Path", ImGuiTableColumnFlags.WidthFixed, availWidth * 0.2f);
            ImGui.TableHeadersRow();
            
            for (var i = 0; i < textures.Length; i++)
            {
                var texture = textures[i];
                if (texture.TextureResourceHandle == null) continue;
                using var textureId = ImRaii.PushId(i);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(i.ToString());
                ImGui.TableNextColumn();
                var texturePath = texture.TextureResourceHandle->FileName.ParseString();
                ImGui.Text(texturePath);
            }
        }
        if (ImGui.CollapsingHeader("Material Keys",
                                   ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.AllowItemOverlap))
        {
            Span<uint> shaderValues = mtrlPtr.Value->Material->ShaderKeyValuesSpan;
            if (shaderValues.Length == 0)
            {
                ImGui.Text("No shader keys.");
                return;
            }
            
            var availWidth = ImGui.GetContentRegionAvail().X;
            using var table = ImRaii.Table("MaterialKeys", 2, ImGuiTableFlags.Borders |
                                                                  ImGuiTableFlags.RowBg |
                                                                  ImGuiTableFlags.Hideable |
                                                                  ImGuiTableFlags.Resizable);
            
            ImGui.TableSetupColumn("Key", ImGuiTableColumnFlags.WidthFixed, availWidth * 0.05f);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthFixed, availWidth * 0.2f);
            ImGui.TableHeadersRow();
            
            // set cache if not already set
            if (!materialKeysCache.TryGetValue((nint)mtrlPtr.Value, out var materialKeys))
            {
                var keyColl = new List<(uint, uint)>();
                for (int i = 0; i < shpk.MaterialKeys.Length; i++)
                {
                    var key = shpk.MaterialKeys[i];
                    var value = shaderValues[i];
                    keyColl.Add((key.Id, value));
                }
                
                materialKeys = keyColl.ToArray();
                materialKeysCache[(nint)mtrlPtr.Value] = materialKeys;
            }

            for (int i = 0; i < shpk.MaterialKeys.Length; i++)
            {
                var key = shpk.MaterialKeys[i];
                var value = shaderValues[i];
                using var keyId = ImRaii.PushId(i);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(Names.TryResolveName(key.Id));
                ImGui.TableNextColumn();
                ImGui.Text(Names.TryResolveName(value));
                
                // option to set value
                ImGui.SameLine();
                if (!materialKeysEditors.TryGetValue((nint)mtrlPtr.Value, out var keyEditor))
                {
                    keyEditor = new Dictionary<uint, string>();
                    materialKeysEditors[(nint)mtrlPtr.Value] = keyEditor;
                }
                
                if (!keyEditor.TryGetValue(key.Id, out var currentValue))
                {
                    currentValue = $"{value:X8}";
                    keyEditor[key.Id] = currentValue;
                }
                
                ImGui.SetNextItemWidth(availWidth * 0.1f);
                if (ImGui.InputText($"##{key.Id}", ref currentValue, 64))
                {
                    keyEditor[key.Id] = currentValue;
                }
                ImGui.SameLine();
                if (ImGui.Button($"Set##{key.Id}"))
                {
                    if (uint.TryParse(currentValue, System.Globalization.NumberStyles.HexNumber,
                                      null, out var newValue))
                    {
                        shaderValues[i] = newValue;
                    }
                    else
                    {
                        ImGui.TextColored(new Vector4(1, 0, 0, 1), "Invalid value format.");
                    }
                }
                
                ImGui.SameLine();
                if (ImGui.Button($"Restore##{key.Id}"))
                {
                    var cacheValue = materialKeys.FirstOrDefault(x => x.Key == key.Id);
                    if (cacheValue.Key != 0)
                    {
                        shaderValues[i] = cacheValue.Value;
                        keyEditor[key.Id] = $"{cacheValue.Value:X8}";
                    }
                    else
                    {
                        ImGui.TextColored(new Vector4(1, 0, 0, 1), "No cached value found.");
                    }
                }
            }
        }
    }
}
