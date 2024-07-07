using System.Numerics;
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

    public TestingTab(IObjectTable objectTable, IClientState clientState, SqPack pack)
    {
        this.objectTable = objectTable;
        this.clientState = clientState;
        this.pack = pack;
    }
    
    public void Dispose()
    {
        shpkCache.Clear();
        mtrlConstantCache.Clear();
        materialCache.Clear();
    }

    public string Name => "Testing";
    public int Order => 3;
    
    private ICharacter? SelectedCharacter { get; set; }
    
    public void Draw()
    {
        ICharacter[] objects;
        if (clientState.LocalPlayer != null)
        {
            objects = objectTable.OfType<ICharacter>()
                                 .Where(obj => obj.IsValid() && obj.IsValidCharacter())
                                 .OrderBy(c => clientState.GetDistanceToLocalPlayer(c).LengthSquared())
                                 .ToArray();
        }
        else
        {
            // login/char creator produces "invalid" characters but are still usable I guess
            objects = objectTable.OfType<ICharacter>()
                                 .Where(obj =>  obj.IsValidCharacter())
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
                            
                            ImGui.PushID(mtrlFileName);
                            try
                            {
                                if (ImGui.CollapsingHeader(mtrlFileName))
                                {
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

                                    ImGui.Columns(7);
                                    ImGui.Text("ID");
                                    ImGui.SetColumnWidth(0, availWidth * 0.05f);
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
                                            var defaults = shpk.MaterialParamDefaults
                                                               .Skip((int)materialParam.ByteOffset / 4)
                                                               .Take((int)materialParam.ByteSize / 4).ToArray();
                                            string nameLookup = $"0x{materialParam.Id:X8}";
                                            if (Enum.IsDefined((MaterialConstant)materialParam.Id))
                                            {
                                                nameLookup += $" ({(MaterialConstant)materialParam.Id})";
                                            }

                                            var defaultString = defaults.Length switch
                                            {
                                                0 => "None",
                                                _ => string.Join(", ", defaults.Select(x => $"{x}"))
                                            };

                                            var mtrlDefault = mtrlConstants.Skip((int)materialParam.ByteOffset / 4)
                                                                           .Take((int)materialParam.ByteSize / 4)
                                                                           .ToArray();
                                            var mtrlDefaultString = mtrlDefault.Length switch
                                            {
                                                0 => "None",
                                                _ => string.Join(", ", mtrlDefault.Select(x => $"{x}"))
                                            };
                                            
                                            bool matchesDefault = defaults.SequenceEqual(mtrlDefault);

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
                                            ImGui.Text(defaultString);
                                            ImGui.NextColumn();
                                            if (!matchesDefault)
                                            {
                                                ImGui.TextColored(new Vector4(1, 0, 0, 1), mtrlDefaultString);
                                            }
                                            else
                                            {
                                                ImGui.Text(mtrlDefaultString);
                                            }

                                            ImGui.NextColumn();

                                            var currentVal =
                                                materialParams.Slice(materialParam.ByteOffset / 4,
                                                                     materialParam.ByteSize / 4);
                                            
                                            // if current val doesn't match mtrlDefault, highlight
                                            var changed = !currentVal.SequenceEqual(mtrlDefault);
                                            if (changed)
                                            {
                                                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0, 0, 1));
                                            }
                                            
                                            if (ImGui.Button($"Restore##{materialParam.GetHashCode()}"))
                                            {
                                                mtrlDefault.CopyTo(currentVal);
                                            }

                                            for (var j = 0; j < currentVal.Length; j++)
                                            {
                                                ImGui.SameLine();
                                                // force width
                                                ImGui.SetNextItemWidth(availWidth * 0.1f);
                                                ImGui.InputFloat($"##{materialParam.GetHashCode()}_{j}",
                                                                 ref currentVal[j], 0.01f, 0.1f, "%.2f");
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
                                    
                                    ImGui.Columns(1);
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
}
