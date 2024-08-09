﻿using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Common.Math;
using FFXIVClientStructs.Interop;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Structs;
using Meddle.Plugin.Services;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files.SqPack;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using CSCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using CSCharacterBase = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CharacterBase;
using CSHuman = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.Human;
using CustomizeParameter = Meddle.Utils.Export.CustomizeParameter;
using CSMaterial = FFXIVClientStructs.FFXIV.Client.Graphics.Render.Material;
using CSModel = FFXIVClientStructs.FFXIV.Client.Graphics.Render.Model;

namespace Meddle.Plugin.UI;

public unsafe class LiveCharacterTab : ITab
{
    private readonly IClientState clientState;
    private readonly Configuration config;
    private readonly DXHelper dxHelper;
    private readonly ExportService exportService;

    private readonly FileDialogManager fileDialog = new()
    {
        AddedWindowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking
    };

    private readonly ILogger<LiveCharacterTab> log;
    private readonly IObjectTable objectTable;
    private readonly SqPack pack;
    private readonly ParseService parseService;
    private readonly PbdHooks pbd;
    private readonly Dictionary<nint, bool> selectedModels = new();
    private readonly TextureCache textureCache;
    private readonly ITextureProvider textureProvider;
    private bool cacheHumanCustomizeData;

    private readonly Dictionary<Pointer<CSHuman>, (CustomizeData, CustomizeParameter)> humanCustomizeData = new();
    private ICharacter? selectedCharacter;

    public LiveCharacterTab(
        IObjectTable objectTable,
        IClientState clientState,
        ILogger<LiveCharacterTab> log,
        ExportService exportService,
        ITextureProvider textureProvider,
        ParseService parseService,
        DXHelper dxHelper,
        TextureCache textureCache,
        SqPack pack,
        PbdHooks pbd,
        Configuration config)
    {
        this.log = log;
        this.exportService = exportService;
        this.textureProvider = textureProvider;
        this.parseService = parseService;
        this.dxHelper = dxHelper;
        this.textureCache = textureCache;
        this.pack = pack;
        this.pbd = pbd;
        this.config = config;
        this.objectTable = objectTable;
        this.clientState = clientState;
    }


    private bool IsDisposed { get; set; }

    public string Name => "CharacterAlt";
    public int Order => 1;
    public bool DisplayTab => true;

    public void Draw()
    {
        DrawObjectPicker();
        DrawSelectedCharacter();
        fileDialog.Draw();
    }

    public void Dispose()
    {
        if (!IsDisposed)
        {
            log.LogDebug("Disposing CharacterTabAlt");
            selectedModels.Clear();
            humanCustomizeData.Clear();
            IsDisposed = true;
        }
    }

    private void DrawObjectPicker()
    {
        // Warning text:
        ImGui.TextWrapped("NOTE: Exported models use a rudimentary approximation of the games pixel shaders, " +
                          "they will likely not match 1:1 to the in-game appearance.");

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

        selectedCharacter ??= objects.FirstOrDefault() ?? clientState.LocalPlayer;

        ImGui.Text("Select Character");
        var preview = selectedCharacter != null
                          ? clientState.GetCharacterDisplayText(selectedCharacter, config.PlayerNameOverride)
                          : "None";
        using (var combo = ImRaii.Combo("##Character", preview))
        {
            if (combo)
            {
                foreach (var character in objects)
                {
                    if (ImGui.Selectable(clientState.GetCharacterDisplayText(character, config.PlayerNameOverride)))
                    {
                        selectedCharacter = character;
                    }
                }
            }
        }
    }

    private void DrawSelectedCharacter()
    {
        if (selectedCharacter == null)
        {
            ImGui.Text("No character selected");
            return;
        }

        var charPtr = (CSCharacter*)selectedCharacter.Address;
        DrawCharacter(charPtr);
    }

    private void DrawCharacter(CSCharacter* character)
    {
        if (character == null)
        {
            ImGui.Text("Character is null");
            return;
        }

        var drawObject = character->GameObject.DrawObject;
        DrawDrawObject(drawObject);

        if (character->Mount.MountObject != null)
        {
            ImGui.Separator();
            ImGui.Text("Mount");
            DrawCharacter(character->Mount.MountObject);
        }

        if (character->CompanionData.CompanionObject != null)
        {
            ImGui.Separator();
            ImGui.Text("Companion");
            DrawCharacter(&character->CompanionData.CompanionObject->Character);
        }

        if (character->OrnamentData.OrnamentObject != null)
        {
            ImGui.Separator();
            ImGui.Text("Ornament");
            DrawCharacter(&character->OrnamentData.OrnamentObject->Character);
        }

        for (var weaponIdx = 0; weaponIdx < character->DrawData.WeaponData.Length; weaponIdx++)
        {
            var weaponData = character->DrawData.WeaponData[weaponIdx];
            if (weaponData.DrawObject != null)
            {
                ImGui.Separator();
                ImGui.Text($"Weapon {weaponIdx}");
                DrawDrawObject(weaponData.DrawObject);
            }
        }
    }

    private void DrawDrawObject(DrawObject* drawObject)
    {
        if (drawObject == null)
        {
            ImGui.Text("Draw object is null");
            return;
        }

        var objectType = drawObject->Object.GetObjectType();
        if (objectType != ObjectType.CharacterBase)
        {
            ImGui.Text($"Draw object is not a character base ({objectType})");
            return;
        }

        using var drawObjectId = ImRaii.PushId($"{(nint)drawObject}");
        var cBase = (CSCharacterBase*)drawObject;
        var modelType = cBase->GetModelType();
        CustomizeParameter? customizeParams = null;
        CustomizeData? customizeData = null;
        var genderRace = GenderRace.Unknown;
        if (modelType == CharacterBase.ModelType.Human)
        {
            DrawHumanCharacter((CSHuman*)cBase, out customizeData, out customizeParams, out genderRace);
        }

        if (ImGui.Button("Export All Models"))
        {
            var colorTableTextures = parseService.ParseColorTableTextures(cBase);
            var models = new List<MdlFileGroup>();
            foreach (var modelPtr in cBase->ModelsSpan)
            {
                if (modelPtr == null) continue;
                var model = modelPtr.Value;
                if (model == null) continue;
                var modelData = parseService.HandleModelPtr(cBase, (int)model->SlotIndex, colorTableTextures);
                if (modelData == null) continue;
                models.Add(modelData);
            }

            var skeleton = StructExtensions.GetParsedSkeleton(cBase);
            var cGroup = new CharacterGroup(customizeParams ?? new CustomizeParameter(),
                                            customizeData ?? new CustomizeData(),
                                            genderRace, models.ToArray(),
                                            skeleton, []);
            fileDialog.SaveFolderDialog("Save Model", "Character",
                                        (result, path) =>
                                        {
                                            if (!result) return;

                                            Task.Run(() => { exportService.Export(cGroup, path); });
                                        }, Plugin.TempDirectory);
        }

        ImGui.SameLine();
        var selectedModelCount = cBase->ModelsSpan.ToArray().Count(modelPtr =>
        {
            if (modelPtr == null) return false;
            return selectedModels.ContainsKey((nint)modelPtr.Value) && selectedModels[(nint)modelPtr.Value];
        });
        using (var disable = ImRaii.Disabled(selectedModelCount == 0))
        {
            if (ImGui.Button($"Export Selected Models ({selectedModelCount})") && selectedModelCount > 0)
            {
                var colorTableTextures = parseService.ParseColorTableTextures(cBase);
                var models = new List<MdlFileGroup>();
                foreach (var modelPtr in cBase->ModelsSpan)
                {
                    if (modelPtr == null) continue;
                    if (!selectedModels.TryGetValue((nint)modelPtr.Value, out var isSelected) || !isSelected) continue;
                    var model = modelPtr.Value;
                    if (model == null) continue;
                    var modelData = parseService.HandleModelPtr(cBase, (int)model->SlotIndex, colorTableTextures);
                    if (modelData == null) continue;
                    models.Add(modelData);
                }

                var skeleton = StructExtensions.GetParsedSkeleton(cBase);
                var cGroup = new CharacterGroup(customizeParams ?? new CustomizeParameter(),
                                                customizeData ?? new CustomizeData(), genderRace, models.ToArray(),
                                                skeleton, []);

                fileDialog.SaveFolderDialog("Save Model", "Character",
                                            (result, path) =>
                                            {
                                                if (!result) return;

                                                Task.Run(() => { exportService.Export(cGroup, path); });
                                            }, Plugin.TempDirectory);
            }
        }

        using var modelTable = ImRaii.Table("##Models", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable);
        ImGui.TableSetupColumn("Options", ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableSetupColumn("Character Data", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (var modelPtr in cBase->ModelsSpan)
        {
            if (modelPtr == null)
            {
                continue;
            }

            DrawModel(cBase, modelPtr.Value, customizeParams, customizeData, genderRace);
        }
    }

    private void DrawModel(
        Pointer<CharacterBase> cPtr, Pointer<CSModel> mPtr, CustomizeParameter? customizeParams,
        CustomizeData? customizeData, GenderRace genderRace)
    {
        if (cPtr == null || cPtr.Value == null)
        {
            return;
        }

        if (mPtr == null || mPtr.Value == null || mPtr.Value->ModelResourceHandle == null)
        {
            return;
        }

        var cBase = cPtr.Value;
        var model = mPtr.Value;
        using var modelId = ImRaii.PushId($"{(nint)model}");
        ImGui.TableNextRow();
        var fileName = model->ModelResourceHandle->FileName.ToString();
        var modelName = cBase->ResolveMdlPath(model->SlotIndex);

        ImGui.TableSetColumnIndex(0);
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            if (ImGui.Button(FontAwesomeIcon.FileExport.ToIconString()))
            {
                ImGui.OpenPopup("ExportModelPopup");
            }

            ImGui.SameLine();
            var selected = selectedModels.ContainsKey((nint)model);
            if (ImGui.Checkbox("##Selected", ref selected))
            {
                if (selected)
                {
                    selectedModels[(nint)model] = true;
                }
                else
                {
                    selectedModels.Remove((nint)model);
                }
            }
        }

        // popup for export options
        if (ImGui.BeginPopupContextItem("ExportModelPopup"))
        {
            if (ImGui.MenuItem("Export as mdl"))
            {
                var defaultFileName = Path.GetFileName(fileName);
                fileDialog.SaveFileDialog("Save Model", "Model File{.mdl}", defaultFileName, ".mdl",
                                          (result, path) =>
                                          {
                                              if (!result) return;
                                              var data = pack.GetFileOrReadFromDisk(fileName);
                                              if (data == null)
                                              {
                                                  log.LogError(
                                                      "Failed to get model data from pack or disk for {FileName}",
                                                      fileName);
                                                  return;
                                              }

                                              File.WriteAllBytes(path, data);
                                          });
            }

            if (ImGui.MenuItem("Export as glTF"))
            {
                var folderName = Path.GetFileNameWithoutExtension(fileName);
                fileDialog.SaveFolderDialog("Save Model", folderName,
                                            (result, path) =>
                                            {
                                                if (!result) return;
                                                var colorTableTextures = parseService.ParseColorTableTextures(cBase);
                                                var modelData =
                                                    parseService.HandleModelPtr(
                                                        cBase, (int)model->SlotIndex, colorTableTextures);
                                                if (modelData == null)
                                                {
                                                    log.LogError("Failed to get model data for {FileName}", fileName);
                                                    return;
                                                }

                                                var skeleton = StructExtensions.GetParsedSkeleton(model);
                                                var cGroup = new CharacterGroup(
                                                    customizeParams ?? new CustomizeParameter(),
                                                    customizeData ?? new CustomizeData(), genderRace, [modelData],
                                                    skeleton, []);


                                                Task.Run(() => { exportService.Export(cGroup, path); });
                                            }, Plugin.TempDirectory);
            }

            ImGui.EndPopup();
        }

        ImGui.TableSetColumnIndex(1);

        if (ImGui.CollapsingHeader($"[{model->SlotIndex}] {modelName}"))
        {
            UiUtil.Text($"Game File Name: {modelName}", modelName);
            UiUtil.Text($"File Name: {fileName}", fileName);
            ImGui.Text($"Slot Index: {model->SlotIndex}");
            UiUtil.Text($"Skeleton Ptr: {(nint)model->Skeleton:X8}", $"{(nint)model->Skeleton:X8}");
            var deformerInfo = pbd.TryGetDeformer((nint)cBase, model->SlotIndex);
            if (deformerInfo != null)
            {
                ImGui.Text(
                    $"Deformer Id: {(GenderRace)deformerInfo.Value.DeformerId} ({deformerInfo.Value.DeformerId})");
                ImGui.Text($"RaceSex Id: {(GenderRace)deformerInfo.Value.RaceSexId} ({deformerInfo.Value.RaceSexId})");
                ImGui.Text($"Pbd Path: {deformerInfo.Value.PbdPath}");
            }
            else
            {
                ImGui.Text("No deformer info found");
            }

            var modelShapeAttributes = StructExtensions.ParseModelShapeAttributes(model);
            DrawShapeAttributeTable(modelShapeAttributes);

            for (var materialIdx = 0; materialIdx < model->MaterialsSpan.Length; materialIdx++)
            {
                var materialPtr = model->MaterialsSpan[materialIdx];
                if (materialPtr == null || materialPtr.Value == null)
                {
                    continue;
                }

                DrawMaterial(cBase, model, materialPtr.Value, materialIdx);
            }
        }
    }

    private void DrawShapeAttributeTable(Model.ShapeAttributeGroup shapeAttributeGroup)
    {
        if (shapeAttributeGroup.AttributeMasks.Length == 0 && shapeAttributeGroup.ShapeMasks.Length == 0)
        {
            return;
        }

        var enabledShapes = Model.GetEnabledValues(shapeAttributeGroup.EnabledShapeMask,
                                                   shapeAttributeGroup.ShapeMasks).ToArray();
        var enabledAttributes = Model.GetEnabledValues(shapeAttributeGroup.EnabledAttributeMask,
                                                       shapeAttributeGroup.AttributeMasks).ToArray();

        if (ImGui.BeginTable("ShapeAttributeTable", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Enabled", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            foreach (var shape in shapeAttributeGroup.ShapeMasks)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.Text("Shape");
                ImGui.TableSetColumnIndex(1);
                ImGui.Text($"[{shape.id}] {shape.name}");
                ImGui.TableSetColumnIndex(2);
                ImGui.Text(enabledShapes.Contains(shape.name) ? "Yes" : "No");
            }

            foreach (var attribute in shapeAttributeGroup.AttributeMasks)
            {
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.Text("Attribute");
                ImGui.TableSetColumnIndex(1);
                ImGui.Text($"[{attribute.id}] {attribute.name}");
                ImGui.TableSetColumnIndex(2);
                ImGui.Text(enabledAttributes.Contains(attribute.name) ? "Yes" : "No");
            }

            ImGui.EndTable();
        }
    }

    private void DrawMaterial(
        Pointer<CharacterBase> cPtr, Pointer<CSModel> mPtr, Pointer<CSMaterial> mtPtr, int materialIdx)
    {
        if (cPtr == null || cPtr.Value == null)
        {
            return;
        }


        if (mPtr == null || mPtr.Value == null || mPtr.Value->ModelResourceHandle == null)
        {
            return;
        }

        if (mtPtr == null || mtPtr.Value == null || mtPtr.Value->MaterialResourceHandle == null)
        {
            return;
        }


        var cBase = cPtr.Value;
        var model = mPtr.Value;
        var material = mtPtr.Value;

        using var materialId = ImRaii.PushId($"{(nint)material}");
        var materialFileName = material->MaterialResourceHandle->FileName.ToString();
        var materialName = ((ModelResourceHandle*)model->ModelResourceHandle)->GetMaterialFileName((uint)materialIdx);

        // in same row as model export button, draw button for export material
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            if (ImGui.Button(FontAwesomeIcon.FileExport.ToIconString()))
            {
                ImGui.OpenPopup("ExportMaterialPopup");
            }
        }

        // popup for export options
        if (ImGui.BeginPopupContextItem("ExportMaterialPopup"))
        {
            if (ImGui.MenuItem("Export as mtrl"))
            {
                var defaultFileName = Path.GetFileName(materialName);
                fileDialog.SaveFileDialog("Save Material", "Material File{.mtrl}", defaultFileName, ".mtrl",
                                          (result, path) =>
                                          {
                                              if (!result) return;
                                              var data = pack.GetFileOrReadFromDisk(materialFileName);
                                              if (data == null)
                                              {
                                                  log.LogError(
                                                      "Failed to get material data from pack or disk for {MaterialFileName}",
                                                      materialFileName);
                                                  return;
                                              }

                                              File.WriteAllBytes(path, data);
                                          });
            }

            if (ImGui.MenuItem("Export raw textures as pngs"))
            {
                var textureBuffer = new Dictionary<string, SKBitmap>();
                for (var i = 0; i < material->TexturesSpan.Length; i++)
                {
                    var textureEntry = material->TexturesSpan[i];
                    if (textureEntry.Texture == null)
                    {
                        continue;
                    }

                    if (i < material->MaterialResourceHandle->TextureCount)
                    {
                        var textureName = material->MaterialResourceHandle->TexturePathString(i);
                        var gpuTex = dxHelper.ExportTextureResource(textureEntry.Texture->Texture);
                        var textureData = gpuTex.Resource.ToBitmap();
                        textureBuffer[textureName] = textureData;
                    }
                }

                var materialNameNoExt = Path.GetFileNameWithoutExtension(materialFileName);
                fileDialog.SaveFolderDialog("Save Textures", materialNameNoExt,
                                            (result, path) =>
                                            {
                                                if (!result) return;
                                                Directory.CreateDirectory(path);

                                                foreach (var (name, texture) in textureBuffer)
                                                {
                                                    var fileName = Path.GetFileNameWithoutExtension(name);
                                                    var filePath = Path.Combine(path, $"{fileName}.png");
                                                    using var str = new SKDynamicMemoryWStream();
                                                    texture.Encode(str, SKEncodedImageFormat.Png, 100);
                                                    var imageData = str.DetachAsData().AsSpan();
                                                    File.WriteAllBytes(filePath, imageData.ToArray());
                                                }
                                            }, Plugin.TempDirectory);
            }


            ImGui.EndPopup();
        }

        ImGui.TableSetColumnIndex(1);
        if (ImGui.CollapsingHeader(materialName))
        {
            ImGui.Text($"Game File Name: {materialName}");
            ImGui.Text($"File Name: {materialFileName}");
            ImGui.Text($"Material Index: {materialIdx}");
            ImGui.Text($"Texture Count: {material->TextureCount}");
            var shpkName = material->MaterialResourceHandle->ShpkNameString;
            ImGui.Text($"Shader Package: {shpkName}");

            var colorTableTexturePtr =
                cBase->ColorTableTexturesSpan[((int)model->SlotIndex * CSCharacterBase.MaterialsPerSlot) + materialIdx];
            if (colorTableTexturePtr != null && colorTableTexturePtr.Value != null &&
                ImGui.CollapsingHeader("Color Table"))
            {
                var colorTableTexture = colorTableTexturePtr.Value;
                var colorTable = parseService.ParseColorTableTexture(colorTableTexture);
                UiUtil.DrawColorTable(colorTable);
            }

            for (var texIdx = 0; texIdx < material->TextureCount; texIdx++)
            {
                var textureEntry = material->TexturesSpan[texIdx];
                DrawTexture(material, textureEntry, texIdx);
            }
        }
    }

    private void DrawTexture(CSMaterial* material, CSMaterial.TextureEntry textureEntry, int texIdx)
    {
        if (textureEntry.Texture == null)
        {
            return;
        }

        using var textureId = ImRaii.PushId($"{(nint)textureEntry.Texture}");
        string? textureName = null;
        if (texIdx < material->MaterialResourceHandle->TextureCount)
            textureName = material->MaterialResourceHandle->TexturePathString(texIdx);
        var textureFileName = textureEntry.Texture->FileName.ToString();
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            if (ImGui.Button(FontAwesomeIcon.FileExport.ToIconString()))
            {
                ImGui.OpenPopup("ExportTexturePopup");
            }
        }

        // popup for export options
        if (ImGui.BeginPopupContextItem("ExportTexturePopup"))
        {
            if (ImGui.MenuItem("Export as png"))
            {
                var defaultFileName = Path.GetFileName(textureFileName);
                defaultFileName = Path.ChangeExtension(defaultFileName, ".png");
                var gpuTex = dxHelper.ExportTextureResource(textureEntry.Texture->Texture);
                var textureData = gpuTex.Resource.ToBitmap();

                fileDialog.SaveFileDialog("Save Texture", "PNG Image{.png}", defaultFileName, ".png",
                                          (result, path) =>
                                          {
                                              if (!result) return;
                                              using var str = new SKDynamicMemoryWStream();
                                              textureData.Encode(str, SKEncodedImageFormat.Png, 100);
                                              var imageData = str.DetachAsData().AsSpan();
                                              File.WriteAllBytes(path, imageData.ToArray());
                                          }, Plugin.TempDirectory);
            }

            if (ImGui.MenuItem("Export as tex"))
            {
                var defaultFileName = Path.GetFileName(textureFileName);
                fileDialog.SaveFileDialog("Save Texture", "TEX File{.tex}", defaultFileName, ".tex",
                                          (result, path) =>
                                          {
                                              if (!result) return;
                                              var data = pack.GetFileOrReadFromDisk(textureFileName);
                                              if (data == null)
                                              {
                                                  log.LogError(
                                                      "Failed to get texture data from pack or disk for {TextureFileName}",
                                                      textureFileName);
                                                  return;
                                              }

                                              File.WriteAllBytes(path, data);
                                          }, Plugin.TempDirectory);
            }

            ImGui.EndPopup();
        }

        ImGui.TableSetColumnIndex(1);
        if (ImGui.CollapsingHeader(textureName ?? textureFileName))
        {
            UiUtil.Text($"Game File Name: {textureName}", textureName);
            UiUtil.Text($"File Name: {textureFileName}", textureFileName);
            ImGui.Text($"Id: {textureEntry.Id}");

            var availableWidth = ImGui.GetContentRegionAvail().X;
            float displayWidth = textureEntry.Texture->Texture->Width;
            float displayHeight = textureEntry.Texture->Texture->Height;
            if (displayWidth > availableWidth)
            {
                var ratio = availableWidth / displayWidth;
                displayWidth *= ratio;
                displayHeight *= ratio;
            }

            var wrap = textureCache.GetOrAdd($"{(nint)textureEntry.Texture->Texture}", () =>
            {
                var gpuTex = dxHelper.ExportTextureResource(textureEntry.Texture->Texture);
                var textureData = gpuTex.Resource.ToBitmap().GetPixelSpan();
                var wrap = textureProvider.CreateFromRaw(
                    RawImageSpecification.Rgba32(gpuTex.Resource.Width, gpuTex.Resource.Height), textureData,
                    $"Meddle_{(nint)textureEntry.Texture->Texture}_{textureFileName}");
                return wrap;
            });

            ImGui.Image(wrap.ImGuiHandle, new Vector2(displayWidth, displayHeight));
        }
    }

    private void DrawHumanCharacter(
        CSHuman* cBase, out CustomizeData customizeData, out CustomizeParameter customizeParams,
        out GenderRace genderRace)
    {
        if (cacheHumanCustomizeData && humanCustomizeData.TryGetValue(cBase, out var data))
        {
            customizeData = data.Item1;
            customizeParams = data.Item2;
            genderRace = (GenderRace)cBase->RaceSexId;
        }
        else
        {
            var customizeCBuf = cBase->CustomizeParameterCBuffer->TryGetBuffer<Models.Structs.CustomizeParameter>()[0];
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
                LipStick = cBase->Customize.Lipstick,
                Highlights = cBase->Customize.Highlights
            };
            genderRace = (GenderRace)cBase->RaceSexId;
            humanCustomizeData[cBase] = (customizeData, customizeParams);
        }

        if (ImGui.CollapsingHeader("Customize Options"))
        {
            if (ImGui.Checkbox("Cache Human Customize Data", ref cacheHumanCustomizeData))
            {
                humanCustomizeData.Clear();
            }

            var width = ImGui.GetContentRegionAvail().X;
            using var disable = ImRaii.Disabled(!cacheHumanCustomizeData);
            using var table = ImRaii.Table("##CustomizeTable", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable);
            ImGui.TableSetupColumn("Params", ImGuiTableColumnFlags.WidthFixed, width * 0.75f);
            ImGui.TableSetupColumn("Data");
            ImGui.TableHeadersRow();

            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            UiUtil.DrawCustomizeParams(ref customizeParams);
            ImGui.TableSetColumnIndex(1);
            UiUtil.DrawCustomizeData(customizeData);
            ImGui.Text(genderRace.ToString());
        }
    }
}
