using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Interop;
using ImGuiNET;
using Meddle.Plugin.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using CSCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using Material = FFXIVClientStructs.FFXIV.Client.Graphics.Render.Material;

namespace Meddle.Plugin.UI;

public class TestingTab : ITab
{
    private readonly IObjectTable objectTable;
    private readonly IClientState clientState;
    private readonly SqPack pack;
    private readonly Configuration config;
    public string Name => "Testing";
    public int Order => 3;
    public bool DisplayTab => config.ShowAdvanced;

    public TestingTab(IObjectTable objectTable, IClientState clientState, SqPack pack, Configuration config)
    {
        this.objectTable = objectTable;
        this.clientState = clientState;
        this.pack = pack;
        this.config = config;
    }
    
    public void Dispose()
    {
        shpkCache.Clear();
        mtrlConstantCache.Clear();
        materialCache.Clear();
    }

    
    private ICharacter? SelectedCharacter { get; set; }
    
    public void Draw()
    {
        ICharacter[] objects;
        if (clientState.LocalPlayer != null)
        {
            objects = objectTable.OfType<ICharacter>()
                                 .Where(obj => obj.IsValid() && obj.IsValidHuman())
                                 .OrderBy(c => clientState.GetDistanceToLocalPlayer(c).LengthSquared())
                                 .ToArray();
        }
        else
        {
            // login/char creator produces "invalid" characters but are still usable I guess
            objects = objectTable.OfType<ICharacter>()
                                 .Where(obj =>  obj.IsValidHuman())
                                 .OrderBy(c => clientState.GetDistanceToLocalPlayer(c).LengthSquared())
                                 .ToArray();
        }

        SelectedCharacter ??= objects.FirstOrDefault() ?? clientState.LocalPlayer;

        ImGui.Text("Select Character");
        var preview = SelectedCharacter != null ? clientState.GetCharacterDisplayText(SelectedCharacter) : "None";
        using (var combo = ImRaii.Combo("##Character", preview))
        {
            if (combo)
            {
                foreach (var character in objects)
                {
                    if (ImGui.Selectable(clientState.GetCharacterDisplayText(character)))
                    {
                        SelectedCharacter = character;
                    }
                }
            }
        }
        
        if (SelectedCharacter != null)
        {
            DrawCharacter(SelectedCharacter);
        }
    }
    
    private Dictionary<string, ShpkFile> shpkCache = new();
    private Dictionary<string, MtrlFile> mtrlCache = new();
    private Dictionary<string, float[]> mtrlConstantCache = new();
    private Dictionary<string, Pointer<Material>> materialCache = new();
    
    // only show values that are different from the shader default
    private bool onlyShowChanged;
    
    public unsafe void DrawCharacter(ICharacter character)
    {
        ImGui.Checkbox("Only Show Changed", ref onlyShowChanged);
        
        if (ImGui.Button("Restore all defaults"))
        {
            // iterate all material pointers, check if valid and restore from mtrlConstantCache
            foreach (var (_, mtrlPtr) in materialCache)
            {
                if (mtrlPtr == null)
                    continue;
                
                var material = mtrlPtr.Value;
                if (material == null)
                    continue;
                
                var materialParams = material->MaterialParameterCBuffer->TryGetBuffer<float>();
                var mtrlFileName = material->MaterialResourceHandle->ResourceHandle.FileName.ToString();
                if (mtrlConstantCache.TryGetValue(mtrlFileName, out var mtrlConstants))
                {
                    mtrlConstants.CopyTo(materialParams);
                }
            }
        }
        
        var charPtr = (CSCharacter*)character.Address;
        var human = (Human*)charPtr->GameObject.DrawObject;

        foreach (var mdlPtr in human->ModelsSpan)
        {
            if (mdlPtr == null)
                continue;

            var model = mdlPtr.Value;
            if (model == null)
                continue;
            var availWidth = ImGui.GetContentRegionAvail().X;
            var mdlFileName = model->ModelResourceHandle->ResourceHandle.FileName.ToString();
            ImGui.PushID(mdlFileName);
            try
            {
                if (ImGui.CollapsingHeader(mdlFileName))
                {
                    ImGui.Indent();
                    try
                    {
                        foreach (var mtrlPtr in model->MaterialsSpan)
                        {
                            if (mtrlPtr == null)
                                continue;

                            var material = mtrlPtr.Value;
                            if (material == null)
                                continue;

                            var mtrlFileName = material->MaterialResourceHandle->ResourceHandle.FileName.ToString();
                            
                            if (!materialCache.TryGetValue(mtrlFileName, out var materialPtr))
                            {
                                materialCache[mtrlFileName] = material;
                            }
                            else
                            {
                                material = materialPtr;
                            }

                            if (material == null)
                            {
                                materialCache.Remove(mtrlFileName);
                                continue;
                            }
                            
                            ImGui.PushID(mtrlFileName);
                            try
                            {
                                if (ImGui.CollapsingHeader(mtrlFileName))
                                {
                                    if (!mtrlCache.TryGetValue(mtrlFileName, out var mtrl))
                                    {
                                        var mtrlData = pack.GetFile(mtrlFileName);
                                        if (mtrlData != null)
                                        {
                                            mtrl = new MtrlFile(mtrlData.Value.file.RawData);
                                            mtrlCache[mtrlFileName] = mtrl;
                                        }
                                        else
                                        {
                                            throw new Exception($"Failed to load {mtrlFileName}");
                                        }
                                    }

                                    var shpkName = material->MaterialResourceHandle->ShpkNameString;
                                    var shpkPath = $"shader/sm5/shpk/{shpkName}";
                                    if (!shpkCache.TryGetValue(shpkPath, out var shpk))
                                    {
                                        var shpkData = pack.GetFile(shpkPath);
                                        if (shpkData != null)
                                        {
                                            shpk = new ShpkFile(shpkData.Value.file.RawData);
                                            shpkCache[shpkPath] = shpk;
                                        }
                                        else
                                        {
                                            throw new Exception($"Failed to load {shpkPath}");
                                        }
                                    }
                                    
                                    ImGui.Text($"Shader Flags: 0x{material->ShaderFlags:X8}");
                                    ImGui.Text($"Shader Package: {shpkName}");

                                    var materialParams = material->MaterialParameterCBuffer->TryGetBuffer<float>();
                                    if (!mtrlConstantCache.TryGetValue(mtrlFileName, out var mtrlConstants))
                                    {
                                        // make a copy of the materialParams
                                        mtrlConstants = new float[materialParams.Length];
                                        materialParams.ToArray().CopyTo(mtrlConstants.AsSpan());
                                        mtrlConstantCache[mtrlFileName] = mtrlConstants;
                                    }
                                    
                                    var orderedMaterialParams = shpk.MaterialParams.Select((x, idx) => (x, idx)).OrderBy(x => x.idx).ToArray();
                                    
                                    if (ImGui.BeginTable("MaterialParams", 8, ImGuiTableFlags.Borders | 
                                                                              ImGuiTableFlags.RowBg | 
                                                                              ImGuiTableFlags.Hideable |
                                                                              ImGuiTableFlags.Resizable))
                                    {
                                        // Set up column headers
                                        ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, availWidth * 0.05f);
                                        ImGui.TableSetupColumn("Offset", ImGuiTableColumnFlags.WidthFixed, availWidth * 0.05f);
                                        ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthFixed, availWidth * 0.05f);
                                        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, availWidth * 0.2f);
                                        ImGui.TableSetupColumn("Shader Defaults", ImGuiTableColumnFlags.WidthFixed, availWidth * 0.12f);
                                        ImGui.TableSetupColumn("Mtrl Defaults", ImGuiTableColumnFlags.WidthFixed, availWidth * 0.12f);
                                        ImGui.TableSetupColumn("Mtrl CBuf", ImGuiTableColumnFlags.WidthFixed, availWidth * 0.1f);
                                        ImGui.TableSetupColumn("Edit", ImGuiTableColumnFlags.WidthFixed);

                                        ImGui.TableHeadersRow();

                                        foreach (var (materialParam, i) in orderedMaterialParams)
                                        {
                                            ImGui.PushID($"{materialParam.GetHashCode()}_{i}_{shpkName}");
                                            try
                                            {
                                                var shpkDefaults = shpk.MaterialParamDefaults
                                                                       .Skip(materialParam.ByteOffset / 4)
                                                                       .Take(materialParam.ByteSize / 4).ToArray();

                                                var cbufCache = mtrlConstants.Skip(materialParam.ByteOffset / 4)
                                                                             .Take(materialParam.ByteSize / 4)
                                                                             .ToArray();

                                                string nameLookup = $"0x{materialParam.Id:X8}";
                                                if (Enum.IsDefined((MaterialConstant)materialParam.Id))
                                                {
                                                    nameLookup += $" ({(MaterialConstant)materialParam.Id})";
                                                }

                                                bool matchesDefault = shpkDefaults.SequenceEqual(cbufCache);
                                                if (onlyShowChanged && matchesDefault)
                                                {
                                                    continue;
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
                                                var shpkDefaultString = string.Join(", ", shpkDefaults.Select(x => x.ToString("F2")));
                                                ImGui.Text(shpkDefaultString);
                                                ImGui.TableNextColumn();
                                                if (mtrl.Constants.Any(x => x.ConstantId == materialParam.Id))
                                                {
                                                    var constant = mtrl.Constants.First(x => x.ConstantId == materialParam.Id);
                                                    var buf = new List<byte>();
                                                    for (var j = 0; j < constant.ValueSize / 4; j++)
                                                    {
                                                        var value = mtrl.ShaderValues[(constant.ValueOffset / 4) + j];
                                                        var bytes = BitConverter.GetBytes(value);
                                                        buf.AddRange(bytes);
                                                    }

                                                    var mtrlDefaults = MemoryMarshal.Cast<byte, float>(buf.ToArray())
                                                        .ToArray();
                                                    var mtrlDefaultString = string.Join(", ", mtrlDefaults.Select(x => x.ToString("F2")));
                                                    ImGui.Text(mtrlDefaultString);
                                                }
                                                ImGui.TableNextColumn();
                                                var mtrlCbufString = string.Join(", ", cbufCache.Select(x => x.ToString("F2")));
                                                if (!matchesDefault)
                                                {
                                                    ImGui.TextColored(new Vector4(1, 0, 0, 1), mtrlCbufString);
                                                }
                                                else
                                                {
                                                    ImGui.Text(mtrlCbufString);
                                                }
                                                ImGui.TableNextColumn();

                                                var currentVal = materialParams.Slice(materialParam.ByteOffset / 4,
                                                                                      materialParam.ByteSize / 4);

                                                var changed = !currentVal.SequenceEqual(cbufCache);
                                                if (changed)
                                                {
                                                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0, 0, 1));
                                                }

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
                                                    var v4 = new Vector4(currentVal[0], currentVal[1], currentVal[2], currentVal[3]);
                                                    ImGui.InputFloat4($"##{materialParam.GetHashCode()}", ref v4);
                                                    currentVal[0] = v4.X;
                                                    currentVal[1] = v4.Y;
                                                    currentVal[2] = v4.Z;
                                                    currentVal[3] = v4.W;
                                                }
                                                else
                                                {
                                                    var step = GetStepForConstantId((MaterialConstant)materialParam.Id);
                                                    for (var j = 0; j < currentVal.Length; j++)
                                                    {
                                                        if (j > 0)
                                                        {
                                                            ImGui.SameLine();
                                                        }
                                                        
                                                        ImGui.InputFloat($"##{materialParam.GetHashCode()}_{j}",
                                                                         ref currentVal[j], step.Item1, step.Item2, "%.2f");
                                                    }
                                                }
                                                
                                                ImGui.SameLine();
                                                if (ImGui.Button($"Restore##{materialParam.GetHashCode()}"))
                                                {
                                                    cbufCache.CopyTo(currentVal);
                                                }
                                                
                                                /*for (var j = 0; j < currentVal.Length; j++)
                                                {
                                                    ImGui.SameLine();
                                                    ImGui.SetNextItemWidth(availWidth * 0.1f);
                                                    var step = GetStepForConstantId((MaterialConstant)materialParam.Id);
                                                    //ImGui.InputFloat($"##{materialParam.GetHashCode()}_{j}",
                                                    //                 ref currentVal[j], step.Item1, step.Item2, "%.2f");
                                                    if (currentVal.Length == 2)
                                                    {
                                                        var v2 = new Vector2(currentVal[0], currentVal[1]);
                                                        
                                                    }
                                                    else
                                                    {
                                                        ImGui.InputFloat($"##{materialParam.GetHashCode()}_{j}",
                                                                         ref currentVal[j], step.Item1, step.Item2, "%.2f");
                                                    }
                                                }*/

                                                if (changed)
                                                {
                                                    ImGui.PopStyleColor();
                                                }

                                            }
                                            finally
                                            {
                                                ImGui.PopID();
                                            }
                                        }

                                        ImGui.EndTable();
                                    }

                                    
                                    
                                    /*ImGui.Columns(8);
                                    ImGui.SetColumnWidth(0, availWidth * 0.05f);
                                    ImGui.Text("ID");
                                    ImGui.NextColumn();
                                    ImGui.SetColumnWidth(1, availWidth * 0.05f);
                                    ImGui.Text("Offset");
                                    ImGui.NextColumn();
                                    ImGui.SetColumnWidth(2, availWidth * 0.05f);
                                    ImGui.Text("Size");
                                    ImGui.NextColumn();
                                    ImGui.SetColumnWidth(3, availWidth * 0.2f);
                                    ImGui.Text("Name");
                                    ImGui.NextColumn();
                                    ImGui.SetColumnWidth(4, availWidth * 0.12f);
                                    ImGui.Text("Shader Defaults");
                                    ImGui.NextColumn();
                                    ImGui.SetColumnWidth(4, availWidth * 0.12f);
                                    ImGui.Text("Mtrl Defaults");
                                    ImGui.NextColumn();
                                    ImGui.SetColumnWidth(5, availWidth * 0.1f);
                                    ImGui.Text("Mtrl CBuf");
                                    ImGui.NextColumn();
                                    ImGui.Text("Edit");
                                    ImGui.NextColumn();
                                    ImGui.Separator();
                                    
                                    foreach (var (materialParam, i) in orderedMaterialParams)
                                    {
                                        ImGui.PushID($"{materialParam.GetHashCode()}_{i}_{shpkName}");
                                        try
                                        {
                                            var shpkDefaults = shpk.MaterialParamDefaults
                                                               .Skip(materialParam.ByteOffset / 4)
                                                               .Take(materialParam.ByteSize / 4).ToArray();

                                            var cbufCache = mtrlConstants.Skip(materialParam.ByteOffset / 4)
                                                                         .Take(materialParam.ByteSize / 4)
                                                                         .ToArray();
                                            
                                            string nameLookup = $"0x{materialParam.Id:X8}";
                                            if (Enum.IsDefined((MaterialConstant)materialParam.Id))
                                            {
                                                nameLookup += $" ({(MaterialConstant)materialParam.Id})";
                                            }
                                            
                                            bool matchesDefault = shpkDefaults.SequenceEqual(cbufCache);
                                            if (onlyShowChanged && matchesDefault)
                                            {
                                                continue;
                                            }

                                            ImGui.Text(i.ToString());
                                            ImGui.NextColumn();
                                            ImGui.Text($"{materialParam.ByteOffset}");
                                            ImGui.NextColumn();
                                            ImGui.Text($"{materialParam.ByteSize}");
                                            ImGui.NextColumn();
                                            ImGui.Text(nameLookup);
                                            // if click, copy name
                                            if (ImGui.IsItemClicked())
                                            {
                                                ImGui.SetClipboardText(nameLookup);
                                            }
                                            ImGui.NextColumn();
                                            var shpkDefaultString = string.Join(", ", shpkDefaults.Select(x => x.ToString("F2")));
                                            var mtrlCbufString = string.Join(", ", cbufCache.Select(x => x.ToString("F2")));
                                            ImGui.Text(shpkDefaultString);
                                            ImGui.NextColumn();
                                            if (mtrl.Constants.Any(x => x.ConstantId == materialParam.Id))
                                            {
                                                var constant = mtrl.Constants.First(x => x.ConstantId == materialParam.Id);
                                                var buf = new List<byte>();
                                                for (var j = 0; j < constant.ValueSize / 4; j++)
                                                {
                                                    var value = mtrl.ShaderValues[(constant.ValueOffset / 4) + j];
                                                    var bytes = BitConverter.GetBytes(value);
                                                    buf.AddRange(bytes);
                                                }

                                                var mtrlDefaults = MemoryMarshal.Cast<byte, float>(buf.ToArray())
                                                    .ToArray();
                                                var mtrlDefaultString = string.Join(", ", mtrlDefaults.Select(x => x.ToString("F2")));
                                                ImGui.Text(mtrlDefaultString);
                                            }
                                            ImGui.NextColumn();
                                            
                                            
                                            if (!matchesDefault)
                                            {
                                                ImGui.TextColored(new Vector4(1, 0, 0, 1), mtrlCbufString);
                                            }
                                            else
                                            {
                                                ImGui.Text(mtrlCbufString);
                                            }

                                            ImGui.NextColumn();

                                            var currentVal =
                                                materialParams.Slice(materialParam.ByteOffset / 4,
                                                                     materialParam.ByteSize / 4);
                                            
                                            // if current val doesn't match mtrlDefault, highlight
                                            var changed = !currentVal.SequenceEqual(cbufCache);
                                            if (changed)
                                            {
                                                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0, 0, 1));
                                            }
                                            
                                            if (ImGui.Button($"Restore##{materialParam.GetHashCode()}"))
                                            {
                                                cbufCache.CopyTo(currentVal);
                                            }

                                            for (var j = 0; j < currentVal.Length; j++)
                                            {
                                                ImGui.SameLine();
                                                // force width
                                                ImGui.SetNextItemWidth(availWidth * 0.1f);
                                                var step = GetStepForConstantId((MaterialConstant)materialParam.Id);
                                                ImGui.InputFloat($"##{materialParam.GetHashCode()}_{j}",
                                                                 ref currentVal[j], step.Item1, step.Item2, "%.2f");
                                            }
                                            
                                            if (changed)
                                            {
                                                ImGui.PopStyleColor();
                                            }

                                            ImGui.NextColumn();
                                        }
                                        finally
                                        {
                                            ImGui.PopID();
                                        }
                                    }
                                    
                                    ImGui.Columns(1);*/
                                }
                            } finally
                            {
                                ImGui.PopID();
                            }
                        }
                    } finally
                    {
                        ImGui.Unindent();
                    }
                }
            }
            finally
            {
                ImGui.PopID();
            }
        }
    }
    
    

    private (float, float) GetStepForConstantId(MaterialConstant constant)
    {
        return constant switch {
            MaterialConstant.g_ShaderID => (1.0f, 1.0f),
            MaterialConstant.g_SphereMapIndex => (1.0f, 1.0f),
            MaterialConstant.g_TileIndex => (1.0f, 1.0f),
            MaterialConstant.g_TextureMipBias => (1.0f, 1.0f),
            _ => (0.01f, 0.1f)
        };
    }
}
