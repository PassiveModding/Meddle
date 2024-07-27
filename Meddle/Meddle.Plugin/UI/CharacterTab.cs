using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Interop;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Services;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.Structs.Material;
using Meddle.Utils.Materials;
using Meddle.Utils.Models;
using Microsoft.Extensions.Logging;
using CSCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using CustomizeParameter = Meddle.Utils.Export.CustomizeParameter;

namespace Meddle.Plugin.UI;

public unsafe class CharacterTab : ITab
{
    private readonly Dictionary<string, Channel> channelCache = new();

    private readonly IClientState clientState;
    private readonly ExportUtil exportUtil;
    private readonly ILogger<CharacterTab> log;
    private readonly IObjectTable objectTable;
    private readonly ParseUtil parseUtil;
    private readonly PluginState pluginState;

    private readonly Dictionary<string, TextureImage> textureCache = new();
    private readonly ITextureProvider textureProvider;

    private CharacterGroup? characterGroup;
    private Task? exportTask;
    private CharacterGroup? selectedSetGroup;

    public CharacterTab(
        IObjectTable objectTable,
        IClientState clientState,
        IFramework framework,
        ILogger<CharacterTab> log,
        PluginState pluginState,
        ExportUtil exportUtil,
        IDataManager dataManager,
        ITextureProvider textureProvider,
        ParseUtil parseUtil)
    {
        this.log = log;
        this.pluginState = pluginState;
        this.exportUtil = exportUtil;
        this.parseUtil = parseUtil;
        this.exportUtil.OnLogEvent += HandleLogEvent;
        this.parseUtil.OnLogEvent += HandleLogEvent;
        this.objectTable = objectTable;
        this.clientState = clientState;
        this.textureProvider = textureProvider;
    }

    private (LogLevel level, string message)? LastLog { get; set; }
    private void HandleLogEvent(LogLevel level, string message)
    {
        LastLog = (level, message);
    }
    
    private ICharacter? SelectedCharacter { get; set; }

    private bool ExportTaskIncomplete => exportTask?.IsCompleted == false;
    public string Name => "Character";
    public int Order => 0;
    public bool DisplayTab => true;

    public void Draw()
    {
        if (!pluginState.InteropResolved)
        {
            ImGui.Text("Waiting for game data...");
            return;
        }

        DrawObjectPicker();
    }


    private bool IsDisposed { get; set; }
    public void Dispose()
    {
        if (!IsDisposed)
        {
            log.LogInformation("Disposing CharacterTab");
            exportUtil.OnLogEvent -= HandleLogEvent;
            foreach (var (_, textureImage) in textureCache)
            {
                textureImage.Wrap.Dispose();
            }
            textureCache.Clear();
            IsDisposed = true;
        }
    }

    private void DrawObjectPicker()
    {
        // Warning text:
        ImGui.TextWrapped("Meddle allows you to export character data. Select a character to begin.");
        ImGui.Separator();
        ImGui.TextWrapped("Warning: This plugin is experimental and may not work as expected.");
        ImGui.TextWrapped(
            "Exported models use a rudimentary approximation of the games pixel shaders, they will likely not match 1:1 to the in-game appearance.");

        if (LastLog != null)
        {
            ImGui.TextColored(LastLog.Value.level switch
            {
                LogLevel.Warning => new Vector4(1.0f, 1.0f, 0.0f, 1.0f),
                LogLevel.Error => new Vector4(1.0f, 0.0f, 0.0f, 1.0f),
                _ => new Vector4(1.0f, 1.0f, 1.0f, 1.0f)
            }, LastLog.Value.message);
        }
        
        ICharacter[] objects;
        if (clientState.LocalPlayer != null)
        {
            objects = objectTable.OfType<ICharacter>()
                                 .Where(obj => obj.IsValid() && obj.IsValidCharacterBase())
                                 .OrderBy(c => clientState.GetDistanceToLocalPlayer(c).LengthSquared())
                                 .ToArray();
        }
        else
        {
            // login/char creator produces "invalid" characters but are still usable I guess
            objects = objectTable.OfType<ICharacter>()
                                 .Where(obj => obj.IsValidHuman())
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

        if (exportTask is {IsFaulted: true})
        {
            ImGui.Text("An Error Occured: " + exportTask.Exception?.Message);
        }

        if (SelectedCharacter != null)
        {
            ImGui.BeginDisabled(ExportTaskIncomplete);
            if (ImGui.Button("Parse"))
            {
                exportTask = ParseCharacter(SelectedCharacter);
            }

            ImGui.EndDisabled();
        }
        else
        {
            ImGui.TextWrapped("No character selected");
        }


        if (characterGroup == null)
        {
            ImGui.Text("Parse a character to view data");
            return;
        }

        DrawCharacterGroup();
    }

    private void DrawSkeleton(Skeleton.Skeleton skeleton)
    {
        ImGui.Indent();
        foreach (var partialSkeleton in skeleton.PartialSkeletons)
        {
            ImGui.PushID(partialSkeleton.GetHashCode());
            if (ImGui.CollapsingHeader(
                    $"[{partialSkeleton.ConnectedBoneIndex}] {partialSkeleton.HandlePath ?? "Unknown"}"))
            {
                ImGui.Indent();
                var hkSkeleton = partialSkeleton.HkSkeleton;
                if (hkSkeleton == null)
                {
                    ImGui.Text("No skeleton data");
                }
                else
                {
                    ImGui.Text($"ConnectedBoneIdx: {partialSkeleton.ConnectedBoneIndex}");
                    ImGui.Columns(3);
                    ImGui.Text("Bone Names");
                    ImGui.NextColumn();
                    ImGui.Text("Bone Parents");
                    ImGui.NextColumn();
                    ImGui.Text("Transform");
                    ImGui.NextColumn();
                    for (var i = 0; i < hkSkeleton.BoneNames.Count; i++)
                    {
                        ImGui.Text(hkSkeleton.BoneNames[i]);
                        ImGui.NextColumn();
                        ImGui.Text($"{hkSkeleton.BoneParents[i]}");
                        ImGui.NextColumn();
                        var transform = hkSkeleton.ReferencePose[i].AffineTransform;
                        ImGui.Text($"Scale: {transform.Scale}");
                        ImGui.Text($"Rotation: {transform.Rotation}");
                        ImGui.Text($"Translation: {transform.Translation}");
                        ImGui.NextColumn();
                    }

                    ImGui.Columns(1);
                }

                ImGui.Unindent();
            }

            ImGui.PopID();
        }

        ImGui.Unindent();
    }

    private void DrawCharacterGroup()
    {
        ImGui.PushID(characterGroup!.GetHashCode());
        var availWidth = ImGui.GetContentRegionAvail().X;
        ImGui.Columns(2, "Customize", true);
        try
        {
            ImGui.SetColumnWidth(0, availWidth * 0.8f);
            ImGui.Text("Customize Parameters");
            ImGui.NextColumn();
            ImGui.Text("Customize Data");
            ImGui.NextColumn();
            ImGui.Separator();
            // draw customizeparams
            var customizeParams = characterGroup.CustomizeParams;
            UIUtil.DrawCustomizeParams(ref customizeParams);
            ImGui.NextColumn();
            // draw customize data

            var customizeData = characterGroup.CustomizeData;
            UIUtil.DrawCustomizeData(customizeData);
            ImGui.Text(characterGroup.GenderRace.ToString());

            ImGui.NextColumn();
        } finally
        {
            ImGui.Columns(1);
        }
        
        ImGui.Separator();

        DrawExportOptions();

        if (ImGui.CollapsingHeader("Skeletons"))
        {
            DrawSkeleton(characterGroup.Skeleton);
        }

        foreach (var mdlGroup in characterGroup.MdlGroups)
        {
            ImGui.PushID(mdlGroup.GetHashCode());
            if (ImGui.CollapsingHeader(mdlGroup.Path))
            {
                ImGui.Indent();
                DrawMdlGroup(mdlGroup);
                ImGui.Unindent();
            }

            ImGui.PopID();
        }

        if (characterGroup.AttachedModelGroups.Length > 0)
        {
            ImGui.Separator();
            foreach (var attachedModelGroup in characterGroup.AttachedModelGroups)
            {
                foreach (var mdlGroup in attachedModelGroup.MdlGroups)
                {
                    ImGui.PushID(mdlGroup.GetHashCode());
                    if (ImGui.CollapsingHeader(mdlGroup.Path))
                    {
                        ImGui.Indent();
                        DrawMdlGroup(mdlGroup);
                        ImGui.Unindent();
                    }

                    ImGui.PopID();
                }
            }
        }

        ImGui.PopID();
    }

    private void DrawExportOptions()
    {
        if (characterGroup == null) return;
        var availWidth = ImGui.GetContentRegionAvail().X;
        
        ImGui.BeginDisabled(ExportTaskIncomplete);
        if (ImGui.Button("Export All"))
        {
            exportTask = Task.Run(() =>
            {
                try
                {
                    exportUtil.Export(characterGroup);
                }
                catch (Exception e)
                {
                    log.LogError(e, "Failed to export character");
                    throw;
                }
            });
        }

        if (ImGui.Button("Export Raw Textures"))
        {
            exportTask = Task.Run(() =>
            {
                try
                {
                    exportUtil.ExportRawTextures(characterGroup);
                }
                catch (Exception e)
                {
                    log.LogError(e, "Failed to export raw textures");
                    throw;
                }
            });
        }

        ImGui.EndDisabled();

        if (selectedSetGroup == null) return;
        if (ImGui.CollapsingHeader("Export Individual"))
        {
            ImGui.BeginDisabled(ExportTaskIncomplete);
            if (ImGui.Button($"Export Selected Models##{characterGroup.GetHashCode()}"))
            {
                var group = characterGroup with
                {
                    MdlGroups = selectedSetGroup.MdlGroups,
                    AttachedModelGroups = selectedSetGroup.AttachedModelGroups
                };
                exportTask = Task.Run(() =>
                {
                    log.LogInformation("Exporting selected models");
                    try
                    {
                        exportUtil.Export(group);
                    }
                    catch (Exception e)
                    {
                        log.LogError(e, "Failed to export selected models");
                        throw;
                    }
                });
            }

            ImGui.EndDisabled();

            ImGui.Columns(2, "ExportIndividual", true);
            try
            {
                // set size
                ImGui.SetColumnWidth(0, availWidth * 0.2f);
                ImGui.Text("Export");
                ImGui.NextColumn();
                ImGui.Text("Path");
                ImGui.NextColumn();
                foreach (var mdlGroup in characterGroup.MdlGroups)
                {
                    ImGui.PushID(mdlGroup.GetHashCode());
                    ImGui.BeginDisabled(ExportTaskIncomplete);
                    if (ImGui.Button("Export"))
                    {
                        var group = characterGroup with
                        {
                            MdlGroups = [mdlGroup],
                            AttachedModelGroups = []
                        };
                        exportTask = Task.Run(() =>
                        {
                            try
                            {
                                exportUtil.Export(group);
                            }
                            catch (Exception e)
                            {
                                log.LogError(e, "Failed to export mdl group");
                                throw;
                            }
                        });
                    }

                    ImGui.EndDisabled();

                    var selected = selectedSetGroup.MdlGroups.Contains(mdlGroup);

                    ImGui.SameLine();
                    if (ImGui.Checkbox($"##{mdlGroup.GetHashCode()}", ref selected))
                    {
                        // if selected, make sure mdlgroup is in selectedSetGroup
                        if (selected)
                        {
                            selectedSetGroup = selectedSetGroup with
                            {
                                MdlGroups = selectedSetGroup.MdlGroups.Append(mdlGroup).ToArray()
                            };
                        }
                        else
                        {
                            selectedSetGroup = selectedSetGroup with
                            {
                                MdlGroups = selectedSetGroup.MdlGroups.Where(m => m != mdlGroup).ToArray()
                            };
                        }
                    }

                    ImGui.NextColumn();
                    ImGui.Text(mdlGroup.Path);
                    ImGui.NextColumn();
                    ImGui.PopID();
                }

                ImGui.Separator();
                foreach (var attachedModelGroup in characterGroup.AttachedModelGroups)
                {
                    ImGui.PushID(attachedModelGroup.GetHashCode());
                    ImGui.BeginDisabled(ExportTaskIncomplete);
                    if (ImGui.Button("Export"))
                    {
                        var group = characterGroup with
                        {
                            AttachedModelGroups = [attachedModelGroup],
                            MdlGroups = []
                        };
                        exportTask = Task.Run(() =>
                        {
                            try
                            {
                                exportUtil.Export(group);
                            }
                            catch (Exception e)
                            {
                                log.LogError(e, "Failed to export attached model group");
                                throw;
                            }
                        });
                    }

                    ImGui.EndDisabled();

                    ImGui.SameLine();

                    var selected = selectedSetGroup.AttachedModelGroups.Contains(attachedModelGroup);
                    if (ImGui.Checkbox($"##{attachedModelGroup.GetHashCode()}", ref selected))
                    {
                        if (selected)
                        {
                            selectedSetGroup = selectedSetGroup with
                            {
                                AttachedModelGroups = selectedSetGroup.AttachedModelGroups.Append(attachedModelGroup)
                                                                      .ToArray()
                            };
                        }
                        else
                        {
                            selectedSetGroup = selectedSetGroup with
                            {
                                AttachedModelGroups = selectedSetGroup.AttachedModelGroups
                                                                      .Where(m => m != attachedModelGroup).ToArray()
                            };
                        }
                    }

                    ImGui.NextColumn();
                    foreach (var attachMdl in attachedModelGroup.MdlGroups)
                    {
                        ImGui.Text(attachMdl.Path);
                    }

                    ImGui.NextColumn();
                    ImGui.PopID();
                }
            } finally
            {
                ImGui.Columns(1);
            }
        }
    }

    private Task ParseCharacter(ICharacter character)
    {
        if (!exportTask?.IsCompleted ?? false)
        {
            return exportTask;
        }

        var charPtr = (CSCharacter*)character.Address;
        var drawObject = charPtr->GameObject.DrawObject;
        if (drawObject == null)
        {
            return Task.CompletedTask;
        }

        var objectType = drawObject->Object.GetObjectType();
        if (objectType != ObjectType.CharacterBase)
            throw new InvalidOperationException($"Object type is not CharacterBase: {objectType}");

        var modelType = ((CharacterBase*)drawObject)->GetModelType();
        var characterBase = (CharacterBase*)drawObject;
        CustomizeParameter customizeParams;
        CustomizeData customizeData;
        GenderRace genderRace;
        if (modelType == CharacterBase.ModelType.Human)
        {
            var human = (Human*)drawObject;
            var customizeCBuf = human->CustomizeParameterCBuffer->TryGetBuffer<Models.CustomizeParameter>()[0];
            customizeParams = new CustomizeParameter
            {
                SkinColor = customizeCBuf.SkinColor,
                MuscleTone = customizeCBuf.MuscleTone,
                SkinFresnelValue0 = customizeCBuf.SkinFresnelValue0,
                LipColor = customizeCBuf.LipColor,
                MainColor = customizeCBuf.MainColor,
                FacePaintUVMultiplier = customizeCBuf.FacePaintUVMultiplier,
                HairFresnelValue0 = customizeCBuf.HairFresnelValue0,
                MeshColor = customizeCBuf.MeshColor,
                FacePaintUVOffset = customizeCBuf.FacePaintUVOffset,
                LeftColor = customizeCBuf.LeftColor,
                RightColor = customizeCBuf.RightColor,
                OptionColor = customizeCBuf.OptionColor
            };
            customizeData = new CustomizeData
            {
                LipStick = human->Customize.Lipstick,
                Highlights = human->Customize.Highlights
            };
            genderRace = (GenderRace)human->RaceSexId;
        }
        else
        {
            customizeParams = new CustomizeParameter();
            customizeData = new CustomizeData();
            genderRace = GenderRace.Unknown;
        }

        var colorTableTextures = parseUtil.ParseColorTableTextures(characterBase);

        var attachDict = new Dictionary<Pointer<CharacterBase>, Dictionary<int, ColorTable>>();
        if (charPtr->Mount.MountObject != null)
        {
            var mountDrawObject = charPtr->Mount.MountObject->GameObject.DrawObject;
            if (mountDrawObject != null && mountDrawObject->Object.GetObjectType() == ObjectType.CharacterBase)
            {
                var mountBase = (CharacterBase*)mountDrawObject;
                var mountColorTableTextures = parseUtil.ParseColorTableTextures(mountBase);
                attachDict[mountBase] = mountColorTableTextures;
            }
        }

        if (charPtr->OrnamentData.OrnamentObject != null)
        {
            var ornamentDrawObject = charPtr->OrnamentData.OrnamentObject->DrawObject;
            if (ornamentDrawObject != null && ornamentDrawObject->Object.GetObjectType() == ObjectType.CharacterBase)
            {
                var ornamentBase = (CharacterBase*)ornamentDrawObject;
                var ornamentColorTableTextures = parseUtil.ParseColorTableTextures(ornamentBase);
                attachDict[ornamentBase] = ornamentColorTableTextures;
            }
        }

        if (charPtr->DrawData.IsWeaponHidden == false)
        {
            var weaponDataSpan = charPtr->DrawData.WeaponData;
            foreach (var weaponData in weaponDataSpan)
            {
                var draw = weaponData.DrawObject;
                if (draw == null)
                {
                    continue;
                }

                if (draw->Object.GetObjectType() != ObjectType.CharacterBase)
                {
                    continue;
                }

                var weaponBase = (CharacterBase*)draw;
                var weaponColorTableTextures = parseUtil.ParseColorTableTextures(weaponBase);
                attachDict[weaponBase] = weaponColorTableTextures;
            }
        }

        // begin background work
        try
        {
            characterGroup = parseUtil.HandleCharacterGroup(characterBase, colorTableTextures, attachDict,
                                                            customizeParams, customizeData, genderRace);
            selectedSetGroup = characterGroup;
        }
        catch (Exception e)
        {
            log.LogError(e, "Failed to parse character");
            throw;
        }

        return Task.CompletedTask;
    }

    private void DrawMdlGroup(MdlFileGroup mdlGroup)
    {
        ImGui.Text($"Path: {mdlGroup.Path}");
        ImGui.Text($"Mtrl Files: {mdlGroup.MtrlFiles.Length}");

        if (mdlGroup.ShapeAttributeGroup != null && ImGui.CollapsingHeader("Shape/Attribute Masks"))
        {
            var enabledShapes = Model.GetEnabledValues(mdlGroup.ShapeAttributeGroup.EnabledShapeMask,
                                                       mdlGroup.ShapeAttributeGroup.ShapeMasks).ToArray();
            var enabledAttributes = Model.GetEnabledValues(mdlGroup.ShapeAttributeGroup.EnabledAttributeMask,
                                                           mdlGroup.ShapeAttributeGroup.AttributeMasks).ToArray();

            ImGui.Text("Shapes");
            ImGui.Columns(2);
            ImGui.Text("Name");
            ImGui.NextColumn();
            ImGui.Text("Enabled");
            ImGui.NextColumn();

            foreach (var shape in mdlGroup.ShapeAttributeGroup.ShapeMasks)
            {
                ImGui.Text($"[{shape.id}] {shape.name}");
                ImGui.NextColumn();
                ImGui.Text(enabledShapes.Contains(shape.name) ? "Yes" : "No");
                ImGui.NextColumn();
            }

            ImGui.Columns(1);

            ImGui.Text("Attributes");
            ImGui.Columns(2);
            ImGui.Text("Name");
            ImGui.NextColumn();
            ImGui.Text("Enabled");
            ImGui.NextColumn();

            foreach (var attribute in mdlGroup.ShapeAttributeGroup.AttributeMasks)
            {
                ImGui.Text($"[{attribute.id}] {attribute.name}");
                ImGui.NextColumn();
                ImGui.Text(enabledAttributes.Contains(attribute.name) ? "Yes" : "No");
                ImGui.NextColumn();
            }

            ImGui.Columns(1);
        }

        foreach (var mtrlGroup in mdlGroup.MtrlFiles)
        {
            ImGui.PushID(mtrlGroup.GetHashCode());
            if (ImGui.CollapsingHeader($"{mtrlGroup.Path}"))
            {
                try
                {
                    ImGui.Indent();
                    DrawMtrlGroup(mtrlGroup);
                } finally
                {
                    ImGui.Unindent();
                }
            }

            ImGui.PopID();
        }
    }

    private void DrawMtrlGroup(MtrlFileGroup mtrlGroup)
    {
        ImGui.Text($"Path: {mtrlGroup.Path}");
        ImGui.Text($"Shpk Path: {mtrlGroup.ShpkPath}");
        ImGui.Text($"Tex Files: {mtrlGroup.TexFiles.Length}");

        if (ImGui.CollapsingHeader($"Constants##{mtrlGroup.GetHashCode()}"))
        {
            try
            {
                ImGui.Columns(4);
                ImGui.Text("ID");
                ImGui.NextColumn();
                ImGui.Text("Offset");
                ImGui.NextColumn();
                ImGui.Text("Size");
                ImGui.NextColumn();
                ImGui.Text("Values");
                ImGui.NextColumn();

                foreach (var constant in mtrlGroup.MtrlFile.Constants)
                {
                    var index = constant.ValueOffset / 4;
                    var count = constant.ValueSize / 4;
                    var buf = new List<byte>(128);
                    for (var j = 0; j < count; j++)
                    {
                        var value = mtrlGroup.MtrlFile.ShaderValues[index + j];
                        var bytes = BitConverter.GetBytes(value);
                        buf.AddRange(bytes);
                    }

                    // display as floats
                    var floats = MemoryMarshal.Cast<byte, float>(buf.ToArray());
                    ImGui.Text($"0x{constant.ConstantId:X4}");
                    // if has named value in MaterialConstant enum, display
                    if (Enum.IsDefined(typeof(MaterialConstant), constant.ConstantId))
                    {
                        ImGui.SameLine();
                        ImGui.Text($"({(MaterialConstant)constant.ConstantId})");
                    }

                    ImGui.NextColumn();
                    ImGui.Text($"{constant.ValueOffset:X4}");
                    ImGui.NextColumn();
                    ImGui.Text($"{count}");
                    ImGui.NextColumn();
                    ImGui.Text(string.Join(", ", floats.ToArray()));
                    ImGui.NextColumn();
                }
            } finally
            {
                ImGui.Columns(1);
            }
        }

        if (ImGui.CollapsingHeader($"Shader Keys##{mtrlGroup.GetHashCode()}"))
        {
            var keys = mtrlGroup.MtrlFile.ShaderKeys;
            ImGui.Columns(2);
            ImGui.Text("Category");
            ImGui.NextColumn();
            ImGui.Text("Value");
            ImGui.NextColumn();

            foreach (var key in keys)
            {
                ImGui.Text($"0x{key.Category:X8}");
                if (Enum.IsDefined(typeof(ShaderCategory), key.Category))
                {
                    ImGui.SameLine();
                    ImGui.Text($"({(ShaderCategory)key.Category})");
                }

                ImGui.NextColumn();
                ImGui.Text($"0x{key.Value:X8}");

                var shaderCategory = (ShaderCategory)key.Category;
                switch (shaderCategory)
                {
                    case ShaderCategory.CategoryHairType:
                        ImGui.SameLine();
                        ImGui.Text($"({(HairType)key.Value})");
                        break;
                    case ShaderCategory.CategorySkinType:
                        ImGui.SameLine();
                        ImGui.Text($"({(SkinType)key.Value})");
                        break;
                    case ShaderCategory.CategoryFlowMapType:
                        ImGui.SameLine();
                        ImGui.Text($"({(FlowType)key.Value})");
                        break;
                    case ShaderCategory.CategoryTextureType:
                        ImGui.SameLine();
                        ImGui.Text($"({(TextureMode)key.Value})");
                        break;
                    case ShaderCategory.CategorySpecularType:
                        ImGui.SameLine();
                        ImGui.Text($"({(SpecularMode)key.Value})");
                        break;
                }

                ImGui.NextColumn();
            }

            ImGui.Columns(1);
        }

        if (ImGui.CollapsingHeader($"Color Table##{mtrlGroup.GetHashCode()}"))
        {
            UIUtil.DrawColorTable(mtrlGroup.MtrlFile);
        }

        foreach (var texGroup in mtrlGroup.TexFiles)
        {
            if (ImGui.CollapsingHeader($"{texGroup.MtrlPath}##{texGroup.GetHashCode()}"))
            {
                DrawTexGroup(texGroup);
            }
        }
    }

    private void DrawTexGroup(TexResourceGroup texGroup)
    {
        ImGui.Text($"Path: {texGroup.MtrlPath}");
        if (texGroup.MtrlPath != texGroup.ResourcePath)
        {
            ImGui.Text($"Resource Path: {texGroup.ResourcePath}");
        }
        ImGui.PushID(texGroup.GetHashCode());
        try
        {
            DrawTexFile(texGroup.MtrlPath, texGroup.Resource);
        } finally
        {
            ImGui.PopID();
        }
    }

    private void DrawTexFile(string path, TextureResource file)
    {
        ImGui.Text($"Width: {file.Width}");
        ImGui.Text($"Height: {file.Height}");
        //ImGui.Text($"Depth: {file}");
        ImGui.Text($"Mipmaps: {file.MipLevels}");

        // select channels
        if (!channelCache.TryGetValue(path, out var channels))
        {
            channels = Channel.Rgb;
            channelCache[path] = channels;
        }

        var channelsInt = (int)channels;
        var changed = false;
        ImGui.Text("Channels");
        if (ImGui.CheckboxFlags($"Red##{path}", ref channelsInt, (int)Channel.Red))
        {
            changed = true;
        }

        ImGui.SameLine();
        if (ImGui.CheckboxFlags($"Green##{path}", ref channelsInt, (int)Channel.Green))
        {
            changed = true;
        }

        ImGui.SameLine();
        if (ImGui.CheckboxFlags($"Blue##{path}", ref channelsInt, (int)Channel.Blue))
        {
            changed = true;
        }

        ImGui.SameLine();
        if (ImGui.CheckboxFlags($"Alpha##{path}", ref channelsInt, (int)Channel.Alpha))
        {
            changed = true;
        }

        channels = (Channel)channelsInt;
        channelCache[path] = channels;

        if (changed)
        {
            textureCache.Remove(path);
        }

        if (!textureCache.TryGetValue(path, out var textureImage))
        {
            var texture = new Texture(file, path, null, null, null);
            var bitmap = texture.ToTexture();

            // remove channels
            if (channels != Channel.All)
            {
                for (var x = 0; x < bitmap.Width; x++)
                for (var y = 0; y < bitmap.Height; y++)
                {
                    var pixel = bitmap[x, y];
                    var newPixel = new Vector4();
                    if (channels.HasFlag(Channel.Red))
                    {
                        newPixel.X = pixel.Red / 255f;
                    }

                    if (channels.HasFlag(Channel.Green))
                    {
                        newPixel.Y = pixel.Green / 255f;
                    }

                    if (channels.HasFlag(Channel.Blue))
                    {
                        newPixel.Z = pixel.Blue / 255f;
                    }

                    if (channels.HasFlag(Channel.Alpha))
                    {
                        newPixel.W = pixel.Alpha / 255f;
                    }
                    else
                    {
                        newPixel.W = 1f;
                    }

                    // if only alpha, set rgb to alpha and alpha to 1
                    if (channels == Channel.Alpha)
                    {
                        newPixel.X = newPixel.W;
                        newPixel.Y = newPixel.W;
                        newPixel.Z = newPixel.W;
                        newPixel.W = 1f;
                    }

                    bitmap[x, y] = newPixel.ToSkColor();
                }
            }

            var pixelSpan = bitmap.Bitmap.GetPixelSpan();
            var pixelsCopy = new byte[pixelSpan.Length];
            pixelSpan.CopyTo(pixelsCopy);
            var wrap = textureProvider.CreateFromRaw(
                RawImageSpecification.Rgba32(file.Width, file.Height), pixelsCopy,
                "Meddle.Texture");

            textureImage = new TextureImage(bitmap, wrap);
            textureCache[path] = textureImage;
        }

        var availableWidth = ImGui.GetContentRegionAvail().X;
        float displayWidth;
        float displayHeight;
        if (file.Width > availableWidth)
        {
            displayWidth = availableWidth;
            displayHeight = file.Height * (displayWidth / file.Width);
        }
        else
        {
            displayWidth = file.Width;
            displayHeight = file.Height;
        }

        ImGui.Image(textureImage.Wrap.ImGuiHandle, new Vector2(displayWidth, displayHeight));

        if (ImGui.Button("Export as .png"))
        {
            exportUtil.ExportTexture(textureImage.Bitmap.Bitmap, path);
        }
    }

    private enum Channel
    {
        Red = 1,
        Green = 2,
        Blue = 4,
        Alpha = 8,
        Rgb = Red | Green | Blue,
        All = Red | Green | Blue | Alpha
    }

    private record TextureImage(SKTexture Bitmap, IDalamudTextureWrap Wrap);
}
