using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Interop;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Services.UI;
using Meddle.Plugin.Utils;
using Meddle.Utils.Constants;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using CSCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using Material = FFXIVClientStructs.FFXIV.Client.Graphics.Render.Material;

namespace Meddle.Plugin.UI;

public class MaterialParameterTab : ITab
{
    public MenuType MenuType => MenuType.Debug;
    private readonly Dictionary<string, Pointer<Material>> materialCache = new();
    private readonly Dictionary<string, MtrlFile> mtrlCache = new();
    private readonly Dictionary<string, float[]> mtrlConstantCache = new();
    private readonly CommonUi commonUi;
    private readonly Configuration config;
    private readonly SqPack pack;

    private readonly Dictionary<string, ShpkFile> shpkCache = new();
    private Vector4[]? customizeParameters;
    private Pointer<Human> lastHuman;

    // only show values that are different from the shader default
    private bool onlyShowChanged;

    public MaterialParameterTab(CommonUi commonUi, Configuration config, SqPack pack)
    {
        this.commonUi = commonUi;
        this.config = config;
        this.pack = pack;
    }

    private ICharacter? selectedCharacter;
    public string Name => "Material Parameters";
    public int Order => 3;

    public void Dispose()
    {
        shpkCache.Clear();
        mtrlConstantCache.Clear();
        materialCache.Clear();
    }

    public void Draw()
    {
        ImGui.TextColored(new Vector4(1, 0, 0, 1),
                          "Warning: This tab is for advanced users only. Modifying material parameters may cause crashes or other issues.");

       commonUi.DrawCharacterSelect(ref selectedCharacter);

       if (ImGui.Button("Clear Cache"))
       {
           shpkCache.Clear();
           mtrlConstantCache.Clear();
           materialCache.Clear();
       }

       if (selectedCharacter != null)
        {
            DrawCharacter(selectedCharacter);
        }
    }

    public unsafe void DrawCustomizeParams(CharacterBase* cbase)
    {
        if (cbase->GetModelType() != CharacterBase.ModelType.Human) return;
        var human = (Human*)cbase;

        if (ImGui.CollapsingHeader("Customize Parameters"))
        {
            if (human != lastHuman)
            {
                if (lastHuman != null && lastHuman.Value != null)
                {
                    // restore defaults
                    var lastParams = lastHuman.Value->CustomizeParameterCBuffer->TryGetBuffer<Vector4>();
                    customizeParameters.CopyTo(lastParams);
                }

                customizeParameters = null;
                lastHuman = human;
            }
            
            var parameter = human->CustomizeParameterCBuffer->TryGetBuffer<Vector4>();
            if (customizeParameters == null)
            {
                var initParams = new Vector4[parameter.Length];
                for (var i = 0; i < parameter.Length; i++)
                {
                    var p = parameter[i];
                    initParams[i] = new Vector4(p.X, p.Y, p.Z, p.W);
                }

                customizeParameters = initParams;
            }

            if (ImGui.Button("Restore all defaults"))
            {
                customizeParameters.CopyTo(parameter);
            }

            ImGui.ColorEdit4("Skin Color", ref parameter[0]);
            ImGui.SameLine();
            // restore
            if (ImGui.Button("Restore##SkinColor"))
            {
                parameter[0] = customizeParameters[0];
            }

            ImGui.ColorEdit4("Skin Fresnel", ref parameter[1]);
            ImGui.SameLine();
            if (ImGui.Button("Restore##SkinFresnel"))
            {
                parameter[1] = customizeParameters[1];
            }

            ImGui.ColorEdit4("Lip Color", ref parameter[2]);
            ImGui.SameLine();
            if (ImGui.Button("Restore##LipColor"))
            {
                parameter[2] = customizeParameters[2];
            }

            ImGui.ColorEdit4("Main Color", ref parameter[3]);
            ImGui.SameLine();
            if (ImGui.Button("Restore##MainColor"))
            {
                parameter[3] = customizeParameters[3];
            }

            ImGui.ColorEdit4("Hair Fresnel", ref parameter[4]);
            ImGui.SameLine();
            if (ImGui.Button("Restore##HairFresnel"))
            {
                parameter[4] = customizeParameters[4];
            }

            ImGui.ColorEdit4("Mesh Color", ref parameter[5]);
            ImGui.SameLine();
            if (ImGui.Button("Restore##MeshColor"))
            {
                parameter[5] = customizeParameters[5];
            }

            ImGui.ColorEdit4("Left Color", ref parameter[6]);
            ImGui.SameLine();
            if (ImGui.Button("Restore##LeftColor"))
            {
                parameter[6] = customizeParameters[6];
            }

            ImGui.ColorEdit4("Right Color", ref parameter[7]);
            ImGui.SameLine();
            if (ImGui.Button("Restore##RightColor"))
            {
                parameter[7] = customizeParameters[7];
            }

            ImGui.ColorEdit4("Option Color", ref parameter[8]);
            ImGui.SameLine();
            if (ImGui.Button("Restore##OptionColor"))
            {
                parameter[8] = customizeParameters[8];
            }
        }
    }

    public unsafe void DrawCharacter(ICharacter character)
    {
        ImGui.Checkbox("Only Show Changed", ref onlyShowChanged);

        var charPtr = (CSCharacter*)character.Address;
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
                var mtrlFileName = material->MaterialResourceHandle->ResourceHandle.FileName.ParseString();
                if (mtrlConstantCache.TryGetValue(mtrlFileName, out var mtrlConstants))
                {
                    mtrlConstants.CopyTo(materialParams);
                }
            }
        }

        var cBase = (CharacterBase*)charPtr->GameObject.DrawObject;
        DrawCustomizeParams(cBase);

        foreach (var mdlPtr in cBase->ModelsSpan)
        {
            if (mdlPtr == null)
                continue;

            var model = mdlPtr.Value;
            if (model == null)
                continue;
            var availWidth = ImGui.GetContentRegionAvail().X;
            var mdlFileName = model->ModelResourceHandle->ResourceHandle.FileName.ParseString();
            //ImGui.PushID(mdlFileName);
            using var modelId = ImRaii.PushId(mdlFileName);

            if (ImGui.CollapsingHeader(mdlFileName))
            {
                using var modelIndent = ImRaii.PushIndent();
                foreach (var mtrlPtr in model->MaterialsSpan)
                {
                    if (mtrlPtr == null)
                        continue;

                    var material = mtrlPtr.Value;
                    if (material == null)
                        continue;

                    var mtrlFileName = material->MaterialResourceHandle->ResourceHandle.FileName.ParseString();

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

                    using var mtrlId = ImRaii.PushId(mtrlFileName);
                    if (ImGui.CollapsingHeader(mtrlFileName))
                    {
                        if (!mtrlCache.TryGetValue(mtrlFileName, out var mtrl))
                        {
                            var mtrlData = pack.GetFileOrReadFromDisk(mtrlFileName);
                            if (mtrlData != null)
                            {
                                mtrl = new MtrlFile(mtrlData);
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

                        var orderedMaterialParams = shpk.MaterialParams.Select((x, idx) => (x, idx))
                                                        .OrderBy(x => x.idx).ToArray();

                        if (ImGui.BeginTable("MaterialParams", 8, ImGuiTableFlags.Borders |
                                                                  ImGuiTableFlags.RowBg |
                                                                  ImGuiTableFlags.Hideable |
                                                                  ImGuiTableFlags.Resizable))
                        {
                            // Set up column headers
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
                            ImGui.TableSetupColumn("Mtrl Defaults", ImGuiTableColumnFlags.WidthFixed,
                                                   availWidth * 0.12f);
                            ImGui.TableSetupColumn("Mtrl CBuf", ImGuiTableColumnFlags.WidthFixed,
                                                   availWidth * 0.1f);
                            ImGui.TableSetupColumn("Edit", ImGuiTableColumnFlags.WidthFixed);

                            ImGui.TableHeadersRow();

                            foreach (var (materialParam, i) in orderedMaterialParams)
                            {
                                using var paramId = ImRaii.PushId($"{materialParam.GetHashCode()}_{i}_{shpkName}");

                                var shpkDefaults = shpk.MaterialParamDefaults
                                                       .Skip(materialParam.ByteOffset / 4)
                                                       .Take(materialParam.ByteSize / 4).ToArray();

                                var cbufCache = mtrlConstants.Skip(materialParam.ByteOffset / 4)
                                                             .Take(materialParam.ByteSize / 4)
                                                             .ToArray();

                                var nameLookup = $"0x{materialParam.Id:X8}";
                                if (Enum.IsDefined((MaterialConstant)materialParam.Id))
                                {
                                    nameLookup += $" ({(MaterialConstant)materialParam.Id})";
                                }

                                var matchesDefault = shpkDefaults.SequenceEqual(cbufCache);
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
                                var shpkDefaultString =
                                    string.Join(", ", shpkDefaults.Select(x => x.ToString("F2")));
                                ImGui.Text(shpkDefaultString);
                                ImGui.TableNextColumn();
                                if (mtrl.Constants.Any(x => x.ConstantId == materialParam.Id))
                                {
                                    var constant =
                                        mtrl.Constants.First(x => x.ConstantId == materialParam.Id);
                                    var buf = new List<byte>();
                                    for (var j = 0; j < constant.ValueSize / 4; j++)
                                    {
                                        var value = mtrl.ShaderValues[(constant.ValueOffset / 4) + j];
                                        var bytes = BitConverter.GetBytes(value);
                                        buf.AddRange(bytes);
                                    }

                                    var mtrlDefaults = MemoryMarshal.Cast<byte, float>(buf.ToArray())
                                                                    .ToArray();
                                    var mtrlDefaultString =
                                        string.Join(", ", mtrlDefaults.Select(x => x.ToString("F2")));
                                    ImGui.Text(mtrlDefaultString);
                                }

                                ImGui.TableNextColumn();
                                var mtrlCbufString =
                                    string.Join(", ", cbufCache.Select(x => x.ToString("F2")));
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
                                    var step = GetStepForConstantId((MaterialConstant)materialParam.Id);
                                    for (var j = 0; j < currentVal.Length; j++)
                                    {
                                        if (j > 0)
                                        {
                                            ImGui.SameLine();
                                        }

                                        ImGui.InputFloat($"##{materialParam.GetHashCode()}_{j}",
                                                         ref currentVal[j], step.Item1, step.Item2,
                                                         "%.2f");
                                    }
                                }

                                ImGui.SameLine();
                                if (ImGui.Button($"Restore##{materialParam.GetHashCode()}"))
                                {
                                    cbufCache.CopyTo(currentVal);
                                }
                            }

                            ImGui.EndTable();
                        }
                    }
                }
            }
        }
    }


    private static (float, float) GetStepForConstantId(MaterialConstant constant)
        {
            return constant switch
            {
                MaterialConstant.g_ShaderID => (1.0f, 1.0f),
                MaterialConstant.g_SphereMapIndex => (1.0f, 1.0f),
                MaterialConstant.g_TileIndex => (1.0f, 1.0f),
                MaterialConstant.g_TextureMipBias => (1.0f, 1.0f),
                _ => (0.01f, 0.1f)
            };
        }
    }
