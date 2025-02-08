using System.Diagnostics;
using System.Reflection.Metadata;
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
using Meddle.Plugin.Models.Structs;
using Meddle.Plugin.Services;
using Meddle.Plugin.Services.UI;
using Meddle.Plugin.UI.Layout;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Utils.Constants;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Helpers;
using Microsoft.Extensions.Logging;
using SharpGLTF.Scenes;
using SharpGLTF.Schema2;
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
    private readonly Configuration config;
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
    private readonly ITextureProvider textureProvider;
    private Task exportTask = Task.CompletedTask;
    private CancellationTokenSource exportCancelTokenSource = new();
    private bool cacheHumanCustomizeData;

    private readonly Dictionary<Pointer<CSHuman>, (CustomizeData, CustomizeParameter)> humanCustomizeData = new();
    private ICharacter? selectedCharacter;

    public LiveCharacterTab(
        ILogger<LiveCharacterTab> log,
        ITextureProvider textureProvider,
        ParseService parseService,
        TextureCache textureCache,
        ResolverService resolverService,
        SqPack pack,
        PbdHooks pbd,
        CommonUi commonUi,
        Configuration config,
        ComposerFactory composerFactory)
    {
        this.log = log;
        this.textureProvider = textureProvider;
        this.parseService = parseService;
        this.textureCache = textureCache;
        this.resolverService = resolverService;
        this.pack = pack;
        this.pbd = pbd;
        this.commonUi = commonUi;
        this.config = config;
        this.composerFactory = composerFactory;
    }


    private readonly Dictionary<int, ProgressEvent> progressEvents = new();
    private void HandleProgressEvent(ProgressEvent progressEvent)
    {
        if (progressEvent.Progress == progressEvent.Total)
        {
            progressEvents.Remove(progressEvent.ContextHash);
        }
        else
        {
            progressEvents[progressEvent.ContextHash] = progressEvent;
        }
    }
    
    private bool IsDisposed { get; set; }

    public string Name => "Character";
    public int Order => (int) WindowOrder.Character;

    public void Draw()
    {
        // Warning text:
        ImGui.TextWrapped("NOTE: Exported models use a rudimentary approximation of the games pixel shaders, " +
                          "they will likely not match 1:1 to the in-game appearance.\n" +
                          "You can get a better result by using the Blender addon.");

        if (!exportTask.IsCompleted)
        {
            ImGui.Text("Exporting...");
            if (ImGui.Button("Cancel Export"))
            {
                exportCancelTokenSource.Cancel();
            }

            foreach (var progressEvent in progressEvents)
            {
                if (progressEvent.Value.Progress != progressEvent.Value.Total)
                {
                    ImGui.Text(progressEvent.Value.Name);
                    ImGui.ProgressBar(progressEvent.Value.Progress / (float)progressEvent.Value.Total, new Vector2(-1, 0), $"{progressEvent.Value.Progress}/{progressEvent.Value.Total}");
                }
            }
        }
        else
        {
            progressEvents.Clear();
        }
        
        commonUi.DrawCharacterSelect(ref selectedCharacter);
        
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

    private bool CanExport()
    {
        return exportTask.IsCompleted;
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
            using (ImRaii.Disabled(!CanExport()))
            {
                if (ImGui.Button("Export All Models With Attaches"))
                {
                    ExportAllModelsWithAttaches(character, customizeParams, customizeData);
                }
            }
        }
        else
        {
            customizeData = null;
            customizeParams = null;
            genderRace = GenderRace.Unknown;
        }
        
        DrawDrawObject(drawObject, customizeData, customizeParams, genderRace);

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
                    DrawDrawObject(weaponData.DrawObject, null, null, GenderRace.Unknown);
                }
            }
        }
        catch (Exception ex)
        {
            ImGui.Text($"Error: {ex.Message}");
        }
    }

    private void DrawDrawObject(DrawObject* drawObject, CustomizeData? customizeData, CustomizeParameter? customizeParams, GenderRace genderRace)
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
        using (ImRaii.Disabled(!CanExport()))
        {
            if (ImGui.Button("Export All Models"))
            {
                ExportAllModels(cBase, customizeParams, customizeData);
            }
        }

        ImGui.SameLine();
        var currentSelectedModels = cBase->ModelsSpan.ToArray().Where(modelPtr =>
        {
            if (modelPtr == null) return false;
            return selectedModels.ContainsKey((nint)modelPtr.Value) && selectedModels[(nint)modelPtr.Value];
        }).ToArray();
        using (ImRaii.Disabled(currentSelectedModels.Length == 0 || !CanExport()))
        {
            if (ImGui.Button($"Export Selected Models ({currentSelectedModels.Length})") && currentSelectedModels.Length > 0)
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
                
                var folder = $"Models-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}";
                fileDialog.SaveFolderDialog("Save Model", folder,
                                            (result, path) =>
                                            {
                                                // if (!result) return;
                                                // if (!CanExport()) return;
                                                // exportCancelTokenSource = new CancellationTokenSource();
                                                // exportTask = Task.Run(() =>
                                                // {
                                                //     var exportType = config.ExportType;
                                                //     var composer = composerFactory.CreateCharacterComposer(Path.Combine(path, "cache"), HandleProgressEvent, exportCancelTokenSource.Token);
                                                //     var scene = new SceneBuilder();
                                                //     var root = new NodeBuilder();
                                                //     composer.ComposeModels(models.ToArray(), genderRace, customizeParams, 
                                                //                            customizeData, skeleton, scene, root);
                                                //     scene.AddNode(root);
                                                //     var gltf = scene.ToGltf2();
                                                //     gltf.SaveAsType(exportType, path, "models");
                                                //     Process.Start("explorer.exe", path);
                                                // }, exportCancelTokenSource.Token);
                                            }, config.ExportDirectory);
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

            DrawModel(cBase, modelPtr.Value, customizeParams, customizeData);
        }
    }

    private void ExportAllModelsWithAttaches(CSCharacter* character, CustomizeParameter? customizeParams, CustomizeData? customizeData)
    {
        var info = resolverService.ParseCharacter(character);
        if (info == null)
        {
            log.LogError("Failed to get character info from draw object");
            return;
        }
        
        if (customizeParams != null)
        {
            info.CustomizeParameter = customizeParams;
        }
        
        if (customizeData != null)
        {
            info.CustomizeData = customizeData;
        }
        
        var folderName = $"Character-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}";
        fileDialog.SaveFolderDialog("Save Model", folderName,
                                    (result, path) =>
                                    {
                                        if (!result) return;
                                        if (!CanExport()) return;
                                        exportCancelTokenSource = new CancellationTokenSource();
                                        exportTask = Task.Run(() =>
                                        {
                                            // var exportType = config.ExportType;
                                            // var composer = composerFactory.CreateCharacterComposer(Path.Combine(path, "cache"), HandleProgressEvent, exportCancelTokenSource.Token);
                                            // var scene = new SceneBuilder();
                                            // var root = new NodeBuilder();
                                            // composer.ComposeCharacterInfo(info, null, scene, root);
                                            // scene.AddNode(root);
                                            // var gltf = scene.ToGltf2();
                                            // gltf.SaveAsType(exportType, path, "character");
                                            // Process.Start("explorer.exe", path);
                                        }, exportCancelTokenSource.Token);
                                    }, config.ExportDirectory);
    }
    
    private void ExportAllModels(CSCharacterBase* cBase, CustomizeParameter? customizeParams, CustomizeData? customizeData)
    {
        var info = resolverService.ParseDrawObject((DrawObject*)cBase);
        if (info == null)
        {
            log.LogError("Failed to get character info from draw object");
            return;
        }

        if (customizeParams != null)
        {
            info.CustomizeParameter = customizeParams;
        }
        
        if (customizeData != null)
        {
            info.CustomizeData = customizeData;
        }
        
        var folderName = $"Character-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}";
        fileDialog.SaveFolderDialog("Save Model", folderName,
                                    (result, path) =>
                                    {
                                        if (!result) return;
                                        if (!CanExport()) return;
                                        exportCancelTokenSource = new CancellationTokenSource();
                                        exportTask = Task.Run(() =>
                                        {
                                            // var exportType = config.ExportType;
                                            // var composer = composerFactory.CreateCharacterComposer(Path.Combine(path, "cache"), HandleProgressEvent, exportCancelTokenSource.Token);
                                            // var scene = new SceneBuilder();
                                            // var root = new NodeBuilder();
                                            // composer.ComposeCharacterInfo(info, null, scene, root);
                                            // scene.AddNode(root);
                                            // var gltf = scene.ToGltf2();
                                            // if (exportType.HasFlag(ExportType.GLTF))
                                            // {
                                            //     gltf.SaveGLTF(Path.Combine(path, "character.gltf"));
                                            // }
                                            //
                                            // if (exportType.HasFlag(ExportType.GLB))
                                            // {
                                            //     gltf.SaveGLB(Path.Combine(path, "character.glb"));
                                            // }
                                            //
                                            // if (exportType.HasFlag(ExportType.OBJ))
                                            // {
                                            //     gltf.SaveAsWavefront(Path.Combine(path, "character.obj"));
                                            // }
                                            // Process.Start("explorer.exe", path);
                                        }, exportCancelTokenSource.Token);
                                    }, config.ExportDirectory);
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

            using (ImRaii.Disabled(!CanExport()))
            {
                if (ImGui.MenuItem("Export as glTF"))
                {
                    var folderName = Path.GetFileNameWithoutExtension(fileName);
                    var characterInfo = resolverService.ParseDrawObject((DrawObject*)cBase);
                    if (characterInfo == null)
                    {
                        log.LogError("Failed to get character info from draw object");
                        return;
                    }

                    var colorTableTextures = parseService.ParseColorTableTextures(cBase);
                    if (customizeParams != null)
                    {
                        characterInfo.CustomizeParameter = customizeParams;
                    }

                    if (customizeData != null)
                    {
                        characterInfo.CustomizeData = customizeData;
                    }

                    var modelData = resolverService.ParseModel(cBase, model, colorTableTextures);
                    if (modelData == null)
                    {
                        log.LogError("Failed to get model data for {FileName}", fileName);
                        return;
                    }

                    fileDialog.SaveFolderDialog("Save Model", folderName,
                                                (result, path) =>
                                                {
                                                    if (!result) return;
                                                    if (!CanExport()) return;
                                                    exportCancelTokenSource = new CancellationTokenSource();
                                                    exportTask = Task.Run(() =>
                                                    {
                                                        // var exportType = config.ExportType;
                                                        // var composer =
                                                        //     composerFactory.CreateCharacterComposer(
                                                        //         Path.Combine(path, "cache"), HandleProgressEvent,
                                                        //         exportCancelTokenSource.Token);
                                                        // var scene = new SceneBuilder();
                                                        // var root = new NodeBuilder();
                                                        // composer.ComposeModels(
                                                        //     [modelData], characterInfo.GenderRace,
                                                        //     characterInfo.CustomizeParameter,
                                                        //     characterInfo.CustomizeData, characterInfo.Skeleton, scene,
                                                        //     root);
                                                        // scene.AddNode(root);
                                                        // var gltf = scene.ToGltf2();
                                                        // if (exportType.HasFlag(ExportType.GLTF))
                                                        // {
                                                        //     gltf.SaveGLTF(Path.Combine(path, "model.gltf"));
                                                        // }
                                                        //
                                                        // if (exportType.HasFlag(ExportType.GLB))
                                                        // {
                                                        //     gltf.SaveGLB(Path.Combine(path, "model.glb"));
                                                        // }
                                                        //
                                                        // if (exportType.HasFlag(ExportType.OBJ))
                                                        // {
                                                        //     gltf.SaveAsWavefront(Path.Combine(path, "model.obj"));
                                                        // }
                                                        // Process.Start("explorer.exe", path);
                                                    }, exportCancelTokenSource.Token);
                                                }, config.ExportDirectory);
                }
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

    private readonly Dictionary<string, ShpkFile> shpkCache = new();
    private void DrawConstantsTable(Pointer<CSMaterial> mtPtr)
    {
        if (mtPtr == null || mtPtr.Value == null)
        {
            return;
        }
        
        var material = mtPtr.Value;
        var materialParams = material->MaterialParameterCBuffer->TryGetBuffer<float>();
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
            return;
        }


        var cBase = cPtr.Value;
        var model = mPtr.Value;
        var material = mtPtr.Value;

        using var materialId = ImRaii.PushId($"{(nint)material}");
        var materialFileName = material->MaterialResourceHandle->FileName.ParseString();
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
                var textureBuffer = new Dictionary<string, SKTexture>();
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
                        var gpuTex = DXHelper.ExportTextureResource(textureEntry.Texture->Texture);
                        var textureData = gpuTex.Resource.ToTexture();
                        textureBuffer[textureName] = textureData;
                    }
                }

                var materialNameNoExt = Path.GetFileNameWithoutExtension(materialFileName);
                fileDialog.SaveFolderDialog("Save Textures", materialNameNoExt,
                                            (result, path) =>
                                            {
                                                // if (!result) return;
                                                // Directory.CreateDirectory(path);
                                                //
                                                // foreach (var (name, texture) in textureBuffer)
                                                // {
                                                //     var fileName = Path.GetFileNameWithoutExtension(name);
                                                //     var filePath = Path.Combine(path, $"{fileName}.png");
                                                //     DataProvider.SaveTextureToDisk(texture, filePath);
                                                // }
                                                // Process.Start("explorer.exe", path);
                                            }, config.ExportDirectory);
            }


            ImGui.EndPopup();
        }

        ImGui.TableSetColumnIndex(1);
        if (ImGui.CollapsingHeader(materialName))
        {
            UiUtil.Text($"Game File Name: {materialName}", materialName);
            UiUtil.Text($"File Name: {materialFileName}", materialFileName);
            ImGui.Text($"Material Index: {materialIdx}");
            ImGui.Text($"Texture Count: {material->TextureCount}");
            var shpkName = material->MaterialResourceHandle->ShpkNameString;
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
                var gpuTex = DXHelper.ExportTextureResource(textureEntry.Texture->Texture);
                var textureData = gpuTex.Resource.ToTexture();

                fileDialog.SaveFileDialog("Save Texture", "PNG Image{.png}", defaultFileName, ".png",
                                          (result, path) =>
                                          {
                                              // if (!result) return;
                                              // DataProvider.SaveTextureToDisk(textureData, path);
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
                var gpuTex = DXHelper.ExportTextureResource(textureEntry.Texture->Texture);
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
