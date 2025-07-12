using System.Diagnostics;
using System.Text.Json;
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
using Meddle.Plugin.Models.Composer;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Models.Skeletons;
using Meddle.Plugin.Services;
using Meddle.Plugin.Services.UI;
using Meddle.Plugin.UI.Layout;
using Meddle.Plugin.UI.Windows;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Utils.Constants;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Helpers;
using Microsoft.Extensions.Logging;
using SharpGLTF.Scenes;
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
    private readonly CommonUi commonUi;
    private readonly SigUtil sigUtil;
    private readonly Configuration config;
    private readonly MdlMaterialWindowManager mdlMaterialWindowManager;
    private readonly ComposerFactory composerFactory;
    public MenuType MenuType => MenuType.Default;

    private readonly FileDialogManager fileDialog = new()
    {
        AddedWindowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking
    };
    
    private readonly ILogger<LiveCharacterTab> log;
    private readonly SqPack pack;
    private readonly ParseService parseService;
    private readonly PbdHooks pbd;
    private readonly Dictionary<nint, bool> selectedModels = new();
    private readonly TextureCache textureCache;
    private readonly ResolverService resolverService;
    private readonly StainHooks stainHooks;
    private readonly ITextureProvider textureProvider;
    private Task exportTask = Task.CompletedTask;
    private CancellationTokenSource cancelToken = new();
    private bool cacheHumanCustomizeData;

    private readonly Dictionary<Pointer<CSHuman>, (CustomizeData, CustomizeParameter)> humanCustomizeData = new();
    private ICharacter? selectedCharacter;
    private ExportProgress? progress;
    private bool requestedPopup;
    private Action? drawExportSettingsCallback;
    
    public LiveCharacterTab(
        ILogger<LiveCharacterTab> log,
        ITextureProvider textureProvider,
        ParseService parseService,
        TextureCache textureCache,
        ResolverService resolverService,
        StainHooks stainHooks,
        SqPack pack,
        PbdHooks pbd,
        CommonUi commonUi,
        SigUtil sigUtil,
        Configuration config,
        MdlMaterialWindowManager mdlMaterialWindowManager,
        ComposerFactory composerFactory)
    {
        this.log = log;
        this.textureProvider = textureProvider;
        this.parseService = parseService;
        this.textureCache = textureCache;
        this.resolverService = resolverService;
        this.stainHooks = stainHooks;
        this.pack = pack;
        this.pbd = pbd;
        this.commonUi = commonUi;
        this.sigUtil = sigUtil;
        this.config = config;
        this.mdlMaterialWindowManager = mdlMaterialWindowManager;
        this.composerFactory = composerFactory;
    }
    
    private bool IsDisposed { get; set; }

    public string Name => "Character";
    public int Order => (int) WindowOrder.Character;

    public void Draw()
    {
        LayoutWindow.DrawProgress(exportTask, progress, cancelToken);
        commonUi.DrawCharacterSelect(ref selectedCharacter);
        
        DrawSelectedCharacter();
        fileDialog.Draw();
        if (requestedPopup)
        {
            ImGui.OpenPopup("ExportSettingsPopup");
            requestedPopup = false;
        }
        
        if (drawExportSettingsCallback != null)
        {
            drawExportSettingsCallback();
        }
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
    
    private void DrawSelectedCharacter()
    {
        if (selectedCharacter == null)
        {
            ImGui.Text("No character selected");
            return;
        }

        var charPtr = (CSCharacter*)selectedCharacter.Address;
        DrawCharacter(charPtr, "Character");
    }
    
    private void DrawCharacter(CSCharacter* character, string name, int depth = 0)
    {
        if (depth > 3)
        {
            ImGui.Text("Bad things happened, too deep");
            return;
        }
        
        if (character == null)
        {
            ImGui.Text("Character is null");
            return;
        }

        var drawObject = character->GameObject.DrawObject;
        ImGui.Text(name);
        if (drawObject == null)
        {
            ImGui.Text("Draw object is null");
            return;
        }
        if (drawObject->GetObjectType() != ObjectType.CharacterBase)
        {
            ImGui.Text("Draw object is not a character base");
            return;
        }
        var cBase = (CSCharacterBase*)drawObject;
        var modelType = cBase->GetModelType();
        CustomizeData? customizeData;
        CustomizeParameter? customizeParams;
        GenderRace genderRace;
        if (modelType == CSCharacterBase.ModelType.Human)
        {
            DrawHumanCharacter((CSHuman*)cBase, out customizeData, out customizeParams, out genderRace);
            ExportButton("Export All Models With Attaches", () =>
            {
                var info = resolverService.ParseCharacter(character);
                if (info == null)
                {
                    throw new Exception("Failed to get character info from draw object");
                }

                if (customizeParams != null)
                {
                    info.CustomizeParameter = customizeParams;
                }

                if (customizeData != null)
                {
                    info.CustomizeData = customizeData;
                }

                return info;
            }, character->NameString.GetCharacterName(config, character->ObjectKind));
        }
        else
        {
            customizeData = null;
            customizeParams = null;
            genderRace = GenderRace.Unknown;
        }
        
        DrawDrawObject(drawObject, character->NameString.GetCharacterName(config, character->ObjectKind), customizeData, customizeParams, genderRace);

        try
        {
            if (character->Mount.MountObject != null)
            {
                ImGui.Separator();
                DrawCharacter(character->Mount.MountObject, "Mount", depth + 1);
            }

            if (character->CompanionData.CompanionObject != null)
            {
                ImGui.Separator();
                DrawCharacter(&character->CompanionData.CompanionObject->Character, "Companion", depth + 1);
            }

            if (character->OrnamentData.OrnamentObject != null)
            {
                ImGui.Separator();
                DrawCharacter(&character->OrnamentData.OrnamentObject->Character, "Ornament", depth + 1);
            }

            for (var weaponIdx = 0; weaponIdx < character->DrawData.WeaponData.Length; weaponIdx++)
            {
                var weaponData = character->DrawData.WeaponData[weaponIdx];
                if (weaponData.DrawObject != null)
                {
                    ImGui.Separator();
                    ImGui.Text($"Weapon {weaponIdx}");
                    DrawDrawObject(weaponData.DrawObject, $"{character->NameString.GetCharacterName(config, character->ObjectKind)}_Weapon", null, null, GenderRace.Unknown);
                }
            }
        }
        catch (Exception ex)
        {
            ImGui.Text($"Error: {ex.Message}");
        }
    }

    private void ExportButton(string text, Func<ParsedCharacterInfo> resolve, string name)
    {
        using var disabled = ImRaii.Disabled(!exportTask.IsCompleted);
        if (ImGui.Button(text))
        {
            requestedPopup = true;
            var parsedCharacterInfo = resolve();
            var configClone = config.ExportConfig.Clone();
            configClone.SetDefaultCloneOptions();
            drawExportSettingsCallback = () => DrawExportSettings(parsedCharacterInfo, name, configClone);
        }
    }
    
    private void ExportMenuItem(string text, Func<ParsedCharacterInfo> resolve, string name)
    {
        if (ImGui.MenuItem(text))
        {
            requestedPopup = true;
            var parsedCharacterInfo = resolve();
            var configClone = config.ExportConfig.Clone();
            configClone.SetDefaultCloneOptions();
            drawExportSettingsCallback = () => DrawExportSettings(parsedCharacterInfo, name, configClone);
        }
    }
    
    private void DrawExportSettings(ParsedCharacterInfo characterInfo, string name, Configuration.ExportConfiguration exportConfig)
    {
        if (!exportTask.IsCompleted)
        {
            log.LogWarning("Export task already running");
            return;
        }
        
        if (!ImGui.BeginPopup("ExportSettingsPopup", ImGuiWindowFlags.AlwaysAutoResize)) return;
        try
        {
            var exportFlags = UiUtil.ExportConfigDrawFlags.ShowExportPose |
                              UiUtil.ExportConfigDrawFlags.ShowSubmeshOptions;
            if (characterInfo.Models.Length == 1)
            {
                exportFlags |= UiUtil.ExportConfigDrawFlags.ShowUseDeformer;
            }
            
            if (UiUtil.DrawExportConfig(exportConfig, exportFlags))
            {
                config.ExportConfig.Apply(exportConfig);
                config.Save();
            }
            
            if (ImGui.Button("Export"))
            {
                var defaultName = $"Character-{name}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}";
                cancelToken = new CancellationTokenSource();
                fileDialog.SaveFolderDialog("Save Instances", defaultName,
                                            (result, path) =>
                                            {
                                                if (!result) return;
                                                exportTask = Task.Run(() =>
                                                {
                                                    Directory.CreateDirectory(path);
                                                    var characterBlob = JsonSerializer.Serialize(characterInfo, MaterialComposer.JsonOptions);
                                                    File.WriteAllText(Path.Combine(path, $"{name}_blob.json"), characterBlob);
                                                    var composer = composerFactory.CreateCharacterComposer(path, exportConfig, cancelToken.Token);
                                                    var scene = new SceneBuilder();
                                                    var characterRoot = new NodeBuilder($"Character-{name}");
                                                    scene.AddNode(characterRoot);
                                                    progress = new ExportProgress(characterInfo.Models.Length, "Character");
                                                    composer.Compose(characterInfo, scene, characterRoot, progress);
                                                    var modelRoot = scene.ToGltf2();
                                                    ExportUtil.SaveAsType(modelRoot, exportConfig.ExportType, path, name);
                                                    Process.Start("explorer.exe", path);
                                                });
                                            }, config.ExportDirectory);

                drawExportSettingsCallback = null;
                ImGui.CloseCurrentPopup();
            }
            
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                drawExportSettingsCallback = null;
                ImGui.CloseCurrentPopup();
            }
        }
        finally
        {
            ImGui.EndPopup();
        }
    }

    private void DrawDrawObject(DrawObject* drawObject, string name, CustomizeData? customizeData, CustomizeParameter? customizeParams, GenderRace genderRace)
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

        ExportButton("Export All Models", () =>
        {
            var info = resolverService.ParseDrawObject((DrawObject*)cBase);
            if (info == null)
            {
                throw new Exception("Failed to get character info from draw object");
            }

            if (customizeParams != null)
            {
                info.CustomizeParameter = customizeParams;
            }

            if (customizeData != null)
            {
                info.CustomizeData = customizeData;
            }

            return info;
        }, name);

        ImGui.SameLine();
        var currentSelectedModels = cBase->ModelsSpan.ToArray().Where(modelPtr =>
        {
            if (modelPtr == null) return false;
            return selectedModels.ContainsKey((nint)modelPtr.Value) && selectedModels[(nint)modelPtr.Value];
        }).ToArray();
        using (ImRaii.Disabled(currentSelectedModels.Length == 0))
        {
            var label = $"Export Selected Models ({currentSelectedModels.Length})";
            ExportButton(label, () =>
            {
                var colorTableTextures = parseService.ParseColorTableTextures(cBase);
                var models = new List<ParsedModelInfo>();
                customizeData ??= new CustomizeData();
                customizeParams ??= new CustomizeParameter();
                var skeleton = StructExtensions.GetParsedSkeleton(cBase);
                foreach (var currentSelectedModel in currentSelectedModels)
                {
                    var modelInfo = resolverService.ParseModel(cBase, currentSelectedModel.Value, colorTableTextures);
                    if (modelInfo != null)
                    {
                        models.Add(modelInfo);
                    }
                }

                var info = new ParsedCharacterInfo(models.ToArray(), skeleton, new ParsedAttach(), customizeData, customizeParams, genderRace);
                return info;
            }, name);
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

            DrawModel(cBase, modelPtr.Value, customizeParams, customizeData);
        }
    }
    
    private void DrawModel(Pointer<CharacterBase> cPtr, Pointer<CSModel> mPtr, CustomizeParameter? customizeParams,
                           CustomizeData? customizeData)
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
        var fileName = model->ModelResourceHandle->FileName.ParseString();
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
            var defaultFileName = Path.GetFileName(fileName);
            
            if (ImGui.MenuItem("Export as mdl"))
            {
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
                                          }, config.ExportDirectory);
            }

            ExportMenuItem("Export as gLTF", () =>
            {
                var info = resolverService.ParseDrawObject((DrawObject*)cBase);
                if (info == null)
                {
                    throw new Exception("Failed to get character info from draw object");
                }
                
                var colorTableTextures = parseService.ParseColorTableTextures(cBase);
                if (customizeParams != null)
                {
                    info.CustomizeParameter = customizeParams;
                }
                
                if (customizeData != null)
                {
                    info.CustomizeData = customizeData;
                }
                
                var modelData = resolverService.ParseModel(cBase, model, colorTableTextures);
                if (modelData == null)
                {
                    throw new Exception($"Failed to get model data for {fileName}");
                }
                
                return new ParsedCharacterInfo([modelData], info.Skeleton, new ParsedAttach(), info.CustomizeData, info.CustomizeParameter, info.GenderRace);
            }, $"{defaultFileName}");

            ImGui.EndPopup();
        }

        ImGui.TableSetColumnIndex(1);

        if (ImGui.CollapsingHeader($"[{model->SlotIndex}] {modelName}"))
        {
            UiUtil.Text($"Game File Name: {modelName}", modelName);
            UiUtil.Text($"File Name: {fileName}", fileName);
            ImGui.Text($"Slot Index: {model->SlotIndex}");
            var stain0 = stainHooks.GetStainFromCache((nint)cPtr.Value, model->SlotIndex, 0);
            var stain1 = stainHooks.GetStainFromCache((nint)cPtr.Value, model->SlotIndex, 1);
            if (stain0 != null)
            {
                ImGui.Text($"Stain 0: {stain0.Value.Stain.Name.ExtractText()} ({stain0.Value.Stain.RowId})");
                ImGui.SameLine();
                ImGui.ColorButton("##Stain0", stain0.Value.Color);
            }
            
            if (stain1 != null)
            {
                ImGui.Text($"Stain 1: {stain1.Value.Stain.Name.ExtractText()} ({stain1.Value.Stain.RowId})");
                ImGui.SameLine();
                ImGui.ColorButton("##Stain1", stain1.Value.Color);
            }

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
            
            using (var disableMatParam = ImRaii.Disabled(mdlMaterialWindowManager.HasWindow(mPtr.Value->ModelResourceHandle)))
            {
                // Note; since character materials are stored in model->Materials instead of model->ModelResourceHandle->Materials, 
                if (ImGui.Button("Open Material Window"))
                {
                    mdlMaterialWindowManager.AddMaterialWindow(model);
                }
            }

            var modelShapeAttributes = StructExtensions.ParseModelShapeAttributes(model);
            DrawShapeAttributeTable(modelShapeAttributes);

            for (var materialIdx = 0; materialIdx < model->MaterialsSpan.Length; materialIdx++)
            {
                var materialPtr = model->MaterialsSpan[materialIdx];
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

    private readonly Dictionary<string, ShpkFile> shpkCache = new();
    private void DrawConstantsTable(Pointer<CSMaterial> mtPtr)
    {
        if (mtPtr == null || mtPtr.Value == null)
        {
            return;
        }
        
        var material = mtPtr.Value;
        var materialParams = material->MaterialParameterCBuffer->TryGetBuffer<float>();
        var shpkName = material->MaterialResourceHandle->ShpkName;
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

        var orderedMaterialParams = shpk.MaterialParams.Select((x, idx) => (x, idx))
                                        .OrderBy(x => x.idx).ToArray();
        var availWidth = ImGui.GetContentRegionAvail().X;
        if (ImGui.BeginTable("MaterialParams", 6, 
                             ImGuiTableFlags.Borders |
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
                                   availWidth * 0.3f);
            ImGui.TableSetupColumn("Shader Defaults", ImGuiTableColumnFlags.WidthFixed,
                                   availWidth * 0.25f);
            ImGui.TableSetupColumn("Mtrl CBuf", ImGuiTableColumnFlags.WidthFixed,
                                   availWidth * 0.25f);
            ImGui.TableHeadersRow();

            foreach (var (materialParam, i) in orderedMaterialParams)
            {
                // TODO: This should exist on the in-memory shpk, would be nicer to parse it from there instead of loading the files.
                var shpkDefaults = shpk.MaterialParamDefaults
                                       .Skip(materialParam.ByteOffset / 4)
                                       .Take(materialParam.ByteSize / 4).ToArray();
                
                var constantBuffer = materialParams.Slice(materialParam.ByteOffset / 4,
                    materialParam.ByteSize / 4);

                var nameLookup = $"0x{materialParam.Id:X8}";
                if (Enum.IsDefined((MaterialConstant)materialParam.Id))
                {
                    nameLookup += $" ({(MaterialConstant)materialParam.Id})";
                }

                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.Text(i.ToString());
                ImGui.TableSetColumnIndex(1);
                ImGui.Text($"{materialParam.ByteOffset}");
                ImGui.TableSetColumnIndex(2);
                ImGui.Text($"{materialParam.ByteSize}");
                ImGui.TableSetColumnIndex(3);
                UiUtil.Text(nameLookup, nameLookup);
                ImGui.TableSetColumnIndex(4);
                var shpkDefaultString = string.Join(", ", shpkDefaults.Select(x => x.ToString("F2")));
                ImGui.Text(shpkDefaultString);
                ImGui.TableSetColumnIndex(5);
                var mtrlConstantBufferString = string.Join(", ", constantBuffer.ToArray().Select(x => x.ToString("F2")));
                ImGui.Text(mtrlConstantBufferString);
            }

            ImGui.EndTable();
        }
    }
    
    private void DrawMaterial(
        Pointer<CSCharacterBase> cPtr, Pointer<CSModel> mPtr, Pointer<CSMaterial> mtPtr, int materialIdx)
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
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(1);
            ImGui.Text("Material is null");
            return;
        }


        var cBase = cPtr.Value;
        var model = mPtr.Value;
        var material = mtPtr.Value;

        using var materialId = ImRaii.PushId($"{(nint)material}");
        var materialFileName = material->MaterialResourceHandle->FileName.ParseString();
        var materialName = model->ModelResourceHandle->GetMaterialFileNameBySlot((uint)materialIdx);

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
                fileDialog.SaveFileDialog("Save Material", "Material File{.mtrl}", Path.GetFileName(materialFileName), ".mtrl",
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
                                          }, config.ExportDirectory);
            }

            if (ImGui.MenuItem("Export raw textures as pngs"))
            {
                var textureBuffer = new Dictionary<string, SkTexture>();
                for (var i = 0; i < material->TexturesSpan.Length; i++)
                {
                    var textureEntry = material->TexturesSpan[i];
                    if (textureEntry.Texture == null)
                    {
                        continue;
                    }

                    if (i < material->MaterialResourceHandle->TextureCount)
                    {
                        var textureName = material->MaterialResourceHandle->TexturePath(i);
                        var gpuTex = DxHelper.ExportTextureResource(textureEntry.Texture->Texture);
                        var textureData = gpuTex.Resource.ToTexture();
                        textureBuffer[textureName] = textureData;
                    }
                }

                fileDialog.SaveFolderDialog("Save Textures", Path.GetFileNameWithoutExtension(materialFileName),
                                            (result, path) =>
                                            {
                                                if (!result) return;
                                                Directory.CreateDirectory(path);
                                                
                                                foreach (var (name, tex) in textureBuffer)
                                                {
                                                    var fileName = Path.GetFileNameWithoutExtension(name);
                                                    var filePath = Path.Combine(path, $"{fileName}.png");
                                                    using var memoryStream = new MemoryStream();
                                                    tex.Bitmap.Encode(memoryStream, SKEncodedImageFormat.Png, 100);
                                                    var textureBytes = memoryStream.ToArray();
                                                    File.WriteAllBytes(filePath, textureBytes);
                                                }
                                                Process.Start("explorer.exe", path);
                                            }, config.ExportDirectory);
            }


            ImGui.EndPopup();
        }

        ImGui.TableSetColumnIndex(1);
        if (ImGui.CollapsingHeader(materialName))
        {
            UiUtil.Text($"Material Ptr: {(nint)material:X8}", $"{(nint)material:X8}");
            UiUtil.Text($"Game File Name: {materialName}", materialName);
            UiUtil.Text($"File Name: {materialFileName}", materialFileName);
            ImGui.Text($"Material Index: {materialIdx}");
            ImGui.Text($"Texture Count: {material->TextureCount}");
            ImGui.Text($"Ref Count: {material->RefCount}");
            var shpkName = material->MaterialResourceHandle->ShpkName;
            UiUtil.Text($"Shader Package: {shpkName}", shpkName);
            ImGui.Text($"Shader Flags: 0x{material->ShaderFlags:X8}");

            var colorTableTexturePtr =
                cBase->ColorTableTexturesSpan[((int)model->SlotIndex * CSCharacterBase.MaterialsPerSlot) + materialIdx];
            if (colorTableTexturePtr != null && colorTableTexturePtr.Value != null &&
                ImGui.CollapsingHeader("Color Table"))
            {
                var colorTableTexture = colorTableTexturePtr.Value;
                var colorTable = parseService.ParseColorTableTexture(colorTableTexture);
                UiUtil.DrawColorTable(colorTable);
            }

            if (ImGui.CollapsingHeader("Constants"))
            {
                DrawConstantsTable(mtPtr);
            }

            // if (ImGui.CollapsingHeader("Keys"))
            // {
            //     DrawKeys(mtPtr);
            // }

            for (var texIdx = 0; texIdx < material->TextureCount; texIdx++)
            {
                var textureEntry = material->TexturesSpan[texIdx];
                DrawTexture(material, textureEntry, texIdx);
            }
        }
    }

    [Obsolete("Should not be used until shader package in-memory is updated", true)]
    private void DrawKeys(CSMaterial* material)
    {
        var constants = Names.GetConstants();
        var shaderPackage = material->MaterialResourceHandle->ShaderPackageResourceHandle->ShaderPackage;
        var shaderPackageKeys = ShaderPackageKeys.FromShaderPackage(shaderPackage);
        DrawKeyPairs(shaderPackageKeys->MaterialKeysSpan, shaderPackageKeys->MaterialValuesSpan, "Material (Shpk)");
        DrawKeyPairs(shaderPackageKeys->MaterialKeysSpan, material->ShaderKeyValuesSpan, "Material (Mtrl)");
        
        // DrawKeyPairs(shaderPackageKeys->SceneKeysSpan, shaderPackageKeys->SceneValuesSpan, "Scene");
        // var unknowns2Span = new Span<ShaderPackage.ConstantSamplerUnknown>(shaderPackage->Unknowns2, shaderPackage->Unk2Count);
        // DrawConstantSamplerUnknownSpan(shaderPackage->SamplersSpan, "Sampler");
        // DrawConstantSamplerUnknownSpan(shaderPackage->ConstantsSpan, "Constant");
        // DrawConstantSamplerUnknownSpan(shaderPackage->UnknownsSpan, "Unknown");
        // DrawConstantSamplerUnknownSpan(unknowns2Span, "Unknown2");
        
        return;

        // void DrawConstantSamplerUnknownSpan(Span<ShaderPackage.ConstantSamplerUnknown> samplers, string type)
        // {
        //     using var id = ImRaii.PushId(type);
        //     ImGui.Text($"{type} Keys");
        //     using var table = ImRaii.Table($"{type}Keys", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable);
        //     ImGui.TableSetupColumn("Index", ImGuiTableColumnFlags.WidthFixed, 80);
        //     ImGui.TableSetupColumn("Key", ImGuiTableColumnFlags.WidthFixed, 200);
        //     ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
        //     ImGui.TableHeadersRow();
        //     
        //     for (var i = 0; i < samplers.Length; i++)
        //     {
        //         var sampler = samplers[i];
        //         var keyString = $"0x{sampler.Id:X8}";
        //         var valueString = $"0x{sampler.CRC:X8}";
        //         
        //         ImGui.TableNextRow();
        //         ImGui.TableSetColumnIndex(0);
        //         ImGui.Text(i.ToString());
        //         ImGui.TableSetColumnIndex(1);
        //         ImGui.Text(keyString);
        //         ImGui.TableSetColumnIndex(2);
        //         ImGui.Text(valueString);
        //     }
        // }
        
        
        void DrawKeyPairs(Span<uint> keys, Span<uint> values, string type)
        {
            if (keys.Length != values.Length)
            {
                ImGui.Text("Key and value count mismatch");
                return;
            }
            
            using var id = ImRaii.PushId(type);
            
            ImGui.Text($"{type} Keys");
            
            using var table = ImRaii.Table($"{type}Keys", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.Resizable);
            ImGui.TableSetupColumn("Index", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Key", ImGuiTableColumnFlags.WidthFixed, 200);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
            
            ImGui.TableHeadersRow();
            
            for (int i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                var value = values[i];
                var match = constants.GetValueOrDefault(key);
                var valueMatch = constants.GetValueOrDefault(value);
                var keyString = match != null ? $"{match.Value} (0x{key:X8})" : $"0x{key:X8}";
                var valueString = valueMatch != null ? $"{valueMatch.Value} (0x{value:X8})" : $"0x{value:X8}";
                ImGui.TableNextRow();
                ImGui.TableSetColumnIndex(0);
                ImGui.Text(i.ToString());
                ImGui.TableSetColumnIndex(1);
                ImGui.Text(keyString);
                ImGui.TableSetColumnIndex(2);
                ImGui.Text(valueString);
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
            textureName = material->MaterialResourceHandle->TexturePath(texIdx);
        var textureFileName = textureEntry.Texture->FileName.ParseString();
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
                var gpuTex = DxHelper.ExportTextureResource(textureEntry.Texture->Texture);
                var textureData = gpuTex.Resource.ToTexture();
                using var memoryStream = new MemoryStream();
                textureData.Bitmap.Encode(memoryStream, SKEncodedImageFormat.Png, 100);
                var textureBytes = memoryStream.ToArray();
                
                fileDialog.SaveFileDialog("Save Texture", "PNG Image{.png}", defaultFileName, ".png",
                                          (result, path) =>
                                          {
                                                if (!result) return;
                                                File.WriteAllBytes(path, textureBytes);
                                          }, config.ExportDirectory);
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
                                          }, config.ExportDirectory);
            }

            ImGui.EndPopup();
        }

        ImGui.TableSetColumnIndex(1);
        if (ImGui.CollapsingHeader(textureName ?? textureFileName))
        {
            UiUtil.Text($"Game File Name: {textureName}", textureName);
            UiUtil.Text($"File Name: {textureFileName}", textureFileName);
            ImGui.Text($"Id: {textureEntry.Id}"); 
            ImGui.SameLine();
            ImGui.Text($"File Size: {textureEntry.Texture->FileSize}");
            ImGui.Text($"Size: {textureEntry.Texture->Texture->ActualWidth}x{textureEntry.Texture->Texture->ActualHeight}");
            ImGui.Text($"Depth: {textureEntry.Texture->Texture->Depth}");
            ImGui.SameLine();
            ImGui.Text($"Mip Levels: {textureEntry.Texture->Texture->MipLevel}");
            ImGui.SameLine();
            ImGui.Text($"Array Size: {textureEntry.Texture->Texture->ArraySize}");
            ImGui.Text($"Format: {(TexFile.TextureFormat)textureEntry.Texture->Texture->TextureFormat}");

            var availableWidth = ImGui.GetContentRegionAvail().X;
            float displayWidth = textureEntry.Texture->Texture->ActualWidth;
            float displayHeight = textureEntry.Texture->Texture->ActualHeight;
            if (displayWidth > availableWidth)
            {
                var ratio = availableWidth / displayWidth;
                displayWidth *= ratio;
                displayHeight *= ratio;
            }

            var wrap = textureCache.GetOrAdd($"{(nint)textureEntry.Texture->Texture}", () =>
            {
                var gpuTex = DxHelper.ExportTextureResource(textureEntry.Texture->Texture);
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
            var humanData = resolverService.ParseHuman((CSCharacterBase*)cBase);
            customizeData = humanData.customizeData;
            customizeParams = humanData.customizeParameter;
            genderRace = humanData.genderRace;
            humanCustomizeData[cBase] = (humanData.customizeData, humanData.customizeParameter);
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
