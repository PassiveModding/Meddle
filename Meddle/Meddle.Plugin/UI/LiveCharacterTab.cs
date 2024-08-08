using Dalamud.Game.ClientState.Objects.Types;
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
using Meddle.Plugin.Services;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
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
    private readonly ExportService exportService;
    private readonly ITextureProvider textureProvider;
    private readonly ILogger<LiveCharacterTab> log;
    private readonly IObjectTable objectTable;
    private readonly ParseService parseService;
    private readonly DXHelper dxHelper;
    private readonly TextureCache textureCache;
    private readonly SqPack pack;
    private readonly Configuration config;
    private readonly PluginState pluginState;
    private ICharacter? selectedCharacter;
    private readonly Dictionary<Pointer<CSModel>, bool> selectedModels = new();

    private readonly FileDialogManager fileDialog = new()
    {
        AddedWindowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking
    };

    public LiveCharacterTab(
        IObjectTable objectTable,
        IClientState clientState,
        ILogger<LiveCharacterTab> log,
        PluginState pluginState,
        ExportService exportService,
        ITextureProvider textureProvider,
        ParseService parseService,
        DXHelper dxHelper,
        TextureCache textureCache,
        SqPack pack,
        Configuration config)
    {
        this.log = log;
        this.pluginState = pluginState;
        this.exportService = exportService;
        this.textureProvider = textureProvider;
        this.parseService = parseService;
        this.dxHelper = dxHelper;
        this.textureCache = textureCache;
        this.pack = pack;
        this.config = config;
        this.objectTable = objectTable;
        this.clientState = clientState;
    }

    public string Name => "CharacterAlt";
    public int Order => 1;
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
            log.LogDebug("Disposing CharacterTabAlt");
            selectedModels.Clear();
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

        DrawSelectedCharacter();
        fileDialog.Draw();
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
            DrawCharacter(character->Mount.MountObject);
        }
        
        if (character->CompanionData.CompanionObject != null)
        {
            DrawDrawObject(character->CompanionData.CompanionObject->DrawObject);
        }
        
        if (character->OrnamentData.OrnamentObject != null)
        {
            DrawDrawObject(character->OrnamentData.OrnamentObject->DrawObject);
        }

        foreach (var weaponData in character->DrawData.WeaponData)
        {
            if (weaponData.DrawObject != null)
            {
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
        GenderRace genderRace = GenderRace.Unknown;
        if (modelType == CSCharacterBase.ModelType.Human)
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

            var skeleton = new Skeleton.Skeleton(cBase->Skeleton);
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
            return selectedModels.ContainsKey(modelPtr.Value) && selectedModels[modelPtr.Value];
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
                    if (!selectedModels.TryGetValue(modelPtr, out var isSelected) || !isSelected) continue;
                    var model = modelPtr.Value;
                    if (model == null) continue;
                    var modelData = parseService.HandleModelPtr(cBase, (int)model->SlotIndex, colorTableTextures);
                    if (modelData == null) continue;
                    models.Add(modelData);
                }

                var skeleton = new Skeleton.Skeleton(cBase->Skeleton);
                var cGroup = new CharacterGroup(customizeParams ?? new CustomizeParameter(), customizeData ?? new CustomizeData(), genderRace, models.ToArray(), skeleton, []);

                fileDialog.SaveFolderDialog("Save Model", "Character",
                    (result, path) =>
                    {
                        if (!result) return;
                        
                        Task.Run(() => { exportService.Export(cGroup, path); });
                    }, Plugin.TempDirectory);
            }
        }

        using var modelTable = ImRaii.Table("##Models", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable);
        ImGui.TableSetupColumn("Options", ImGuiTableColumnFlags.WidthFixed, 100);
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

    private void DrawModel(CSCharacterBase* cBase, CSModel* model, CustomizeParameter? customizeParams, CustomizeData? customizeData, GenderRace genderRace)
    {
        if (cBase == null)
        {
            return;
        }

        if (model == null || model->ModelResourceHandle == null)
        {
            return;
        }

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
            var selected = selectedModels.ContainsKey(model);
            if (ImGui.Checkbox("##Selected", ref selected))
            {
                if (selected)
                {
                    selectedModels[model] = true;
                }
                else
                {
                    selectedModels.Remove(model);
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
                        log.LogError("Failed to get model data from pack or disk for {FileName}", fileName);
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
                        var modelData = parseService.HandleModelPtr(cBase, (int)model->SlotIndex, colorTableTextures);
                        if (modelData == null)
                        {
                            log.LogError("Failed to get model data for {FileName}", fileName);
                            return;
                        }

                        var skeleton = new Skeleton.Skeleton(model->Skeleton);
                        var cGroup = new CharacterGroup(customizeParams ?? new CustomizeParameter(), customizeData ?? new CustomizeData(), genderRace, [modelData], skeleton, []);

                        
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
            var modelShapeAttributes = parseService.ParseModelShapeAttributes(model);
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

    private void DrawMaterial(CSCharacterBase* cBase, CSModel* model, CSMaterial* material, int materialIdx)
    {
        if (cBase == null)
        {
            return;
        }

        if (model == null)
        {
            return;
        }

        if (material == null || material->MaterialResourceHandle == null)
        {
            return;
        }

        using var materialId = ImRaii.PushId($"{(nint)material}");
        var materialFileName = material->MaterialResourceHandle->FileName.ToString();
        var materialName = model->ModelResourceHandle->GetMaterialFileName((uint)materialIdx);

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
                        log.LogError("Failed to get material data from pack or disk for {MaterialFileName}",
                            materialFileName);
                        return;
                    }

                    File.WriteAllBytes(path, data);
                });
            }
            
            if (ImGui.MenuItem("Export raw textures as pngs"))
            {
                var textureBuffer = new Dictionary<string, SKBitmap>();
                for (int i = 0; i < material->TexturesSpan.Length; i++)
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
                        log.LogError("Failed to get texture data from pack or disk for {TextureFileName}",
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

    private void DrawHumanCharacter(CSHuman* cBase, out CustomizeData customizeData, out CustomizeParameter customizeParams, out GenderRace genderRace)
    {
        var customizeCBuf = cBase->CustomizeParameterCBuffer->TryGetBuffer<Models.CustomizeParameter>()[0];
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

        if (ImGui.CollapsingHeader("Customize Options"))
        {
            UiUtil.DrawCustomizeParams(ref customizeParams);
            UiUtil.DrawCustomizeData(customizeData);
            ImGui.Text(genderRace.ToString());
        }
    }
}
