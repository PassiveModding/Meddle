using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.ImGuiNotification;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Havok.Animation.Rig;
using ImGuiNET;
using Meddle.Plugin.Models;
using Meddle.Plugin.Models.Composer;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Models.Structs;
using Meddle.Plugin.Services;
using Meddle.Plugin.Services.UI;
using Meddle.Plugin.Utils;
using Meddle.Utils.Constants;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Helpers;
using SharpGLTF.Transforms;
using SkiaSharp;

namespace Meddle.Plugin.UI;

public class DebugTab : ITab
{
    private readonly IClientState clientState;
    private readonly Configuration config;
    public MenuType MenuType => MenuType.Debug;
    private readonly SigUtil sigUtil;
    private readonly CommonUi commonUi;
    private readonly IGameGui gui;
    private readonly LayoutService layoutService;
    private readonly ParseService parseService;
    private readonly PbdHooks pbdHooks;
    private readonly INotificationManager notificationManager;
    private readonly SqPack sqPack;
    private readonly StainHooks stainHooks;
    private readonly IDataManager dataManager;
    private readonly ComposerFactory composerFactory;
    private string boneSearch = "";
    private readonly FileDialogManager fileDialog = new()
    {
        AddedWindowFlags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoDocking
    };
    private CancellationTokenSource? cancellationTokenSource;
    private Task? exportTask;
    

    private ICharacter? selectedCharacter;
    private enum BoneMode
    {
        Local,
        ModelPropagate,
        ModelNoPropagate,
        ModelRaw
    }
    
    private BoneMode boneModeInput = BoneMode.ModelPropagate;

    public DebugTab(Configuration config, SigUtil sigUtil, CommonUi commonUi, 
                    IGameGui gui, IClientState clientState, 
                    LayoutService layoutService,
                    ParseService parseService, PbdHooks pbdHooks,
                    INotificationManager notificationManager,
                    TextureCache textureCache,
                    ITextureProvider textureProvider,
                    SqPack sqPack,
                    StainHooks stainHooks,
                    IDataManager dataManager,
                    ComposerFactory composerFactory)
    {
        this.config = config;
        this.sigUtil = sigUtil;
        this.commonUi = commonUi;
        this.gui = gui;
        this.clientState = clientState;
        this.layoutService = layoutService;
        this.parseService = parseService;
        this.pbdHooks = pbdHooks;
        this.notificationManager = notificationManager;
        this.textureCache = textureCache;
        this.textureProvider = textureProvider;
        this.sqPack = sqPack;
        this.stainHooks = stainHooks;
        this.dataManager = dataManager;
        this.composerFactory = composerFactory;
    }

    public void Dispose()
    {
        // TODO release managed resources here
    }

    public string Name => "Debug";
    public int Order => 0;

    private readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        IncludeFields = true
    };

    private string constantSearch = "";
    
    public void Draw()
    {
        fileDialog.Draw();
        
        if (ImGui.CollapsingHeader("View Skeleton"))
        {
            DrawSelectedCharacter();
        }

        if (ImGui.CollapsingHeader("Config Json"))
        {
            var cofigJson = JsonSerializer.Serialize(config, jsonOptions);
            ImGui.TextWrapped(cofigJson);
        }

        if (ImGui.CollapsingHeader("EnvLighting"))
        {
            ParseEnvLight();
        }

        if (ImGui.CollapsingHeader("Constants"))
        {
            if (ImGui.CollapsingHeader("Constant Cache"))
            { 
                if (ImGui.InputText("##ConstantSearch", ref constantSearch, 100))
                {
                    constantSearch = constantSearch.ToLower();
                }

                var constants = Names.GetConstants().ToArray();
                if (!string.IsNullOrEmpty(constantSearch))
                {
                    constants = constants.Where(x =>
                         {
                             if (x.Value.Value.Contains(constantSearch, StringComparison.CurrentCultureIgnoreCase))
                             {
                                 return true;
                             }
                             
                             if (x.Key.ToString().Contains(constantSearch, StringComparison.CurrentCultureIgnoreCase))
                             {
                                 return true;
                             }
                             
                             var hexKey = $"0x{x.Key:X8}";
                             if (hexKey.Contains(constantSearch, StringComparison.CurrentCultureIgnoreCase))
                             {
                                 return true;
                             }
                              
                             return false;
                         })
                        .ToArray();
                }
                
                using var table = ImRaii.Table("##ConstantCache", 3, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit);
                
                // foreach (var (key, value) in constants)
                // {
                //     if (value is Names.StubName stubName)
                //     {
                //         ImGui.Text($"{key}: {stubName.Value} (stubName)");
                //     }
                //     else if (value is Names.Name name)
                //     {
                //         ImGui.Text($"{key}: {name.Value}");
                //     }
                // }
                
                ImGui.TableSetupColumn("Key");
                ImGui.TableSetupColumn("Hex Key");
                ImGui.TableSetupColumn("Value");
                ImGui.TableHeadersRow();
                
                foreach (var (key, value) in constants)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(key.ToString());
                    ImGui.TableNextColumn();
                    var hexKey = $"0x{key:X8}";
                    ImGui.Text(hexKey);
                    ImGui.TableNextColumn();
                    if (value is Names.StubName stubName)
                    {
                        ImGui.Text($"{stubName.Value} (stubName)");
                    }
                    else if (value is Names.Name name)
                    {
                        ImGui.Text(name.Value);
                    }
                }
            }


            var buf = string.Join("\n", MaterialComposer.FailedConstants);
            ImGui.InputTextMultiline("Failed Constants", ref buf, 100000, new Vector2(0, 0), ImGuiInputTextFlags.ReadOnly);
        }

        if (ImGui.CollapsingHeader("Object Table"))
        {
            DrawObjectTable();
        }
        
        if (ImGui.CollapsingHeader("Addresses"))
        {
            DrawAddresses();
        }

        if (ImGui.CollapsingHeader("Cache Info"))
        {
            DrawCacheInfo();
        }

        if (ImGui.CollapsingHeader("Stain Info"))
        {
            DrawStainInfo();
        }

        if (ImGui.CollapsingHeader("PBD Info"))
        {
            using var table = ImRaii.Table("##PbdInfo", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable);
            ImGui.TableSetupColumn("Human", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Slot", ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableSetupColumn("DeformerId", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("RaceSexId", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("PbdPath");
            ImGui.TableHeadersRow();
            foreach (var cachedDeformer in pbdHooks.GetDeformerCache())
            {
                foreach (var deformer in cachedDeformer.Value)
                {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text($"{cachedDeformer.Key:X8}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{deformer.Key}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{deformer.Value.DeformerId}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{deformer.Value.RaceSexId}");
                    ImGui.TableNextColumn();
                    ImGui.Text($"{deformer.Value.PbdPath}");
                }
            }
        }

        if (ImGui.CollapsingHeader("Terrain Pointers"))
        {
            DrawTerrainPointers();
        }
        
        if (ImGui.CollapsingHeader("File Export"))
        {
            DrawFileExportUi();
        }
    }
    
    private unsafe void DrawTerrainPointers()
    {
        var world = sigUtil.GetLayoutWorld();

        if (world == null || world->ActiveLayout == null) return;
        foreach (var layer in world->ActiveLayout->Layers)
        {
            UiUtil.Text($"Layer: {layer.Item1} - {(nint)layer.Item2.Value:X8}", $"{(nint)layer.Item2.Value:X8}");
            var layerPtr = layer.Item2.Value;
            if (layerPtr == null) continue;
            foreach (var instance in layerPtr->Instances)
            {
                UiUtil.Text($"Instance: {instance.Item1} - {(nint)instance.Item2.Value:X8}", $"{(nint)instance.Item2.Value:X8}");
            }
        }

        foreach (var terrain in world->ActiveLayout->Terrains)
        {
            UiUtil.Text($"Terrain: {terrain.Item1} - {(nint)terrain.Item2.Value:X8}", $"{(nint)terrain.Item2.Value:X8}");
            var terrainPtr = terrain.Item2.Value;
            if (terrainPtr == null) continue;
            UiUtil.Text($"GfxTerrain: {(nint)terrainPtr->GfxTerrain:X8}", $"{(nint)terrainPtr->GfxTerrain:X8}");
        }
    }

    private string exportPathInput = "";
    public void DrawFileExportUi()
    {
        using var indent = ImRaii.PushIndent();
        ImGui.Text("Export Path");
        ImGui.SameLine();
        ImGui.InputText("##ExportPath", ref exportPathInput, 100);
        if (ImGui.Button("Export"))
        {
            // var data = sqPack.GetFile(path);
            // if (data == null)
            // {
            //     notificationManager.AddNotification(new Notification
            //     {
            //         Content = $"File not found: {path}",
            //         Type = NotificationType.Error
            //     });
            //     return;
            // }
            //
            // var outPath = Path.Combine(config.ExportDirectory, Path.GetFileName(path));
            // File.WriteAllBytes(outPath, data.Value.file.RawData.ToArray());
            cancellationTokenSource = new CancellationTokenSource();
            var pathFileName = Path.GetFileNameWithoutExtension(exportPathInput);
            var defaultName = $"Export-{pathFileName}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}";
            fileDialog.SaveFolderDialog("Save File", defaultName,
                            (result, exportPath) =>
                            {
                                if (!result) return;
                                var data = sqPack.GetFile(exportPathInput);
                                if (data == null)
                                {
                                    notificationManager.AddNotification(new Notification
                                    {
                                        Content = $"File not found: {exportPathInput}",
                                        Type = NotificationType.Error
                                    });
                                    return;
                                }
                                
                                var outPath = Path.Combine(exportPath, Path.GetFileName(exportPathInput));
                                Directory.CreateDirectory(exportPath);
                                File.WriteAllBytes(outPath, data.Value.file.RawData.ToArray());
                                Process.Start("explorer.exe", exportPath);
                            }, config.ExportDirectory);
        }

        using (var disabled = ImRaii.Disabled(exportTask is {IsCompleted: false} || !exportPathInput.EndsWith(".mdl")))
        {
            if (ImGui.Button("Export Model"))
            {
                cancellationTokenSource = new CancellationTokenSource();
                var configClone = config.ExportConfig.Clone();
                var pathFileName = Path.GetFileNameWithoutExtension(exportPathInput);
                var defaultName = $"Export-{pathFileName}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}";
                var stubInstance = new ParsedBgPartsInstance(0, true, new Transform(AffineTransform.Identity), exportPathInput);
                fileDialog.SaveFolderDialog("Save Instances", defaultName,
                                            (result, exportPath) =>
                                            {
                                                if (!result) return;
                                                exportTask = Task.Run(() =>
                                                {
                                                    var composer = composerFactory.CreateComposer(exportPath,
                                                                                                  configClone,
                                                                                                  cancellationTokenSource.Token);
                                                    var progress = new ExportProgress(1, "Instances");
                                                    composer.Compose([stubInstance], progress);
                                                    Process.Start("explorer.exe", exportPath);
                                                }, cancellationTokenSource.Token);
                                            }, config.ExportDirectory);
            }
        }
        
        using (var disabled = ImRaii.Disabled(exportTask is {IsCompleted: false} || !exportPathInput.EndsWith(".tex")))
        {
            if (ImGui.Button("Export Texture"))
            {
                cancellationTokenSource = new CancellationTokenSource();
                var configClone = config.ExportConfig.Clone();
                var pathFileName = Path.GetFileNameWithoutExtension(exportPathInput);
                var defaultName = $"Export-{pathFileName}-{DateTime.Now:yyyy-MM-dd-HH-mm-ss}";
                fileDialog.SaveFolderDialog("Save Texture", defaultName,
                                            (result, exportPath) =>
                                            {
                                                if (!result) return;
                                                exportTask = Task.Run(() =>
                                                {
                                                    var file = sqPack.GetFile(exportPathInput);
                                                    if (file == null)
                                                    {
                                                        notificationManager.AddNotification(new Notification
                                                        {
                                                            Content = $"File not found: {exportPathInput}",
                                                            Type = NotificationType.Error
                                                        });
                                                        return;
                                                    }
                                                    
                                                    var outPath = Path.Combine(exportPath, Path.GetFileName(exportPathInput));
                                                    
                                                    Directory.CreateDirectory(exportPath);
                                                    var buf = file.Value.file.RawData.ToArray();
                                                    File.WriteAllBytes(outPath, buf);
                                                    
                                                    // Convert to png
                                                    var tex = new TexFile(buf);
                                                    var texture = tex.ToResource().ToTexture();
                                                    using var memoryStream = new MemoryStream();
                                                    texture.Bitmap.Encode(memoryStream, SKEncodedImageFormat.Png, 100);
                                                    var textureBytes = memoryStream.ToArray();
                                                    File.WriteAllBytes(Path.ChangeExtension(outPath, ".png"), textureBytes);
                                                    
                                                    Process.Start("explorer.exe", exportPath);
                                                }, cancellationTokenSource.Token);
                                            }, config.ExportDirectory);
            }
        }

        if (exportPathInput.EndsWith(".tex"))
        {
            // draw the texture
            var availableWidth = ImGui.GetContentRegionAvail().X;

            var tex = textureProvider.GetFromGame(exportPathInput);
            var wrap = tex.GetWrapOrEmpty();
            ImGui.Image(wrap.ImGuiHandle, new Vector2(availableWidth, availableWidth * wrap.Height / wrap.Width));
        }
    }
    private readonly TextureCache textureCache;
    private readonly ITextureProvider textureProvider;
    
    private void DrawStainInfo()
    {
        foreach (var (key, stain) in stainHooks.StainDict)
        {
            using var id = ImRaii.PushId(key.ToString());
            ImGui.Text($"Stain: {key}, {stain.Name}");
            var color = StainHooks.GetStainColor(stain);
            ImGui.SameLine();
            ImGui.ColorButton("Color", color);
        }
    }

    private unsafe void DrawObjectTable()
    {
        using var indent = ImRaii.PushIndent();
        var objectTable = sigUtil.GetGameObjectManager();
        for (int i = 0; i < objectTable->Objects.GameObjectIdSorted.Length; i++)
        {
            var objPtr = objectTable->Objects.GameObjectIdSorted[i];
            if (objPtr == null || objPtr.Value == null)
            {
                continue;
            }
            var obj = objPtr.Value;
            
            var kind = obj->GetObjectKind();
            if (ImGui.CollapsingHeader($"[{i}|{(nint)obj:X8}] {kind} - {obj->NameString} {(obj->DrawObject != null ? $"Visible: {obj->DrawObject->IsVisible}" : "")}"))
            {
                UiUtil.Text($"Address: {(nint)obj:X8}", $"{(nint)obj:X8}");
                ImGui.Text($"Name: {obj->NameString}");
                ImGui.Text($"Type: {kind}");
                var drawObject = obj->DrawObject;
                if (drawObject != null)
                {
                    var drawObjectType = drawObject->GetObjectType();
                    UiUtil.Text($"DrawObject Address: {(nint)drawObject:X8}", $"{(nint)drawObject:X8}");
                    ImGui.Text($"DrawObject Type: {drawObjectType}");
                    ImGui.Text($"DrawObject Position: {drawObject->Position}");
                    ImGui.Text($"DrawObject Rotation: {drawObject->Rotation}");
                    ImGui.Text($"DrawObject Scale: {drawObject->Scale}");
                    if (drawObjectType == ObjectType.CharacterBase)
                    {
                        using var cbaseIndent = ImRaii.PushIndent();
                        var cBase = (CharacterBase*)drawObject;
                        DrawCharacterBase(cBase, "CharacterBase");
                    }
                }
            }
        }
    }

    private unsafe void DrawAddresses()
    {
        var housingManager = sigUtil.GetHousingManager();
        var currentTerritory = housingManager->CurrentTerritory;
        var layoutWorld = sigUtil.GetLayoutWorld();
        var activeLayout = layoutWorld->ActiveLayout;
        
        UiUtil.Text($"HousingManager: {(nint)housingManager:X8}", $"{(nint)housingManager:X8}");
        UiUtil.Text($"CurrentTerritory: {(nint)currentTerritory:X8}", $"{(nint)currentTerritory:X8}");
        UiUtil.Text($"LayoutWorld: {(nint)layoutWorld:X8}", $"{(nint)layoutWorld:X8}");
        UiUtil.Text($"ActiveLayout: {(nint)activeLayout:X8}", $"{(nint)activeLayout:X8}");
    }

    private void DrawCacheInfo()
    {
        if (ImGui.Button("Clear Caches"))
        {
            parseService.ClearCaches();
        }
        
        using var table = ImRaii.Table("##CacheInfo", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit);
        ImGui.TableSetupColumn("Type");
        ImGui.TableSetupColumn("Path");
        ImGui.TableHeadersRow();
        
        foreach (var (path, _) in parseService.ShpkCache)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("Shpk");
            ImGui.TableNextColumn();
            ImGui.Text(path);
        }

        foreach (var (path, _) in parseService.MtrlCache)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("Mtrl");
            ImGui.TableNextColumn();
            ImGui.Text(path);
        }
        
        foreach (var (path, _) in parseService.MdlCache)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("Mdl");
            ImGui.TableNextColumn();
            ImGui.Text(path);
        }

        foreach (var (path, _) in parseService.TexCache)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.Text("Tex");
            ImGui.TableNextColumn();
            ImGui.Text(path);
        }
    }

    private unsafe void DrawSelectedCharacter()
    {
        using var indent = ImRaii.PushIndent();
        commonUi.DrawCharacterSelect(ref selectedCharacter);
        if (selectedCharacter == null)
        {
            ImGui.Text("No characters found");
            return;
        }

        // player address
        ImGui.Text($"Address: {selectedCharacter.Address:X8}");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Click to copy");
        }
        if (ImGui.IsItemClicked())
        {
            ImGui.SetClipboardText($"{selectedCharacter.Address:X8}");
        }


        var character = (Character*)selectedCharacter.Address;
        if (character == null)
        {
            ImGui.Text("Character is null");
            return;
        }

        var drawObject = character->DrawObject;
        if (drawObject == null)
        {
            ImGui.Text("DrawObject is null");
            return;
        }

        ImGui.Text($"DrawObject Address: {(nint)drawObject:X8}");

        var objectType = drawObject->GetObjectType();
        ImGui.Text($"Object Type: {objectType}");
        if (objectType != ObjectType.CharacterBase)
        {
            return;
        }

        var cBase = (CharacterBase*)drawObject;
        DrawCharacterBase(cBase, "Character");

        var ornament = character->OrnamentData.OrnamentObject;
        if (ornament != null)
        {
            var drawOrnament = ornament->DrawObject;
            if (drawOrnament != null)
            {
                ImGui.Text($"Ornament DrawObject Address: {(nint)drawOrnament:X8}");
                var ornamentType = drawOrnament->GetObjectType();
                ImGui.Text($"Ornament Object Type: {ornamentType}");
                if (ornamentType == ObjectType.CharacterBase)
                {
                    var ornamentBase = (CharacterBase*)drawOrnament;
                    DrawCharacterBase(ornamentBase, "Ornament");
                }
            }
        }
        
        var mount = character->Mount.MountObject;
        if (mount != null)
        {
            var drawMount = mount->DrawObject;
            if (drawMount != null)
            {
                ImGui.Text($"Mount DrawObject Address: {(nint)drawMount:X8}");
                var mountType = drawMount->GetObjectType();
                ImGui.Text($"Mount Object Type: {mountType}");
                if (mountType == ObjectType.CharacterBase)
                {
                    var mountBase = (CharacterBase*)drawMount;
                    DrawCharacterBase(mountBase, "Mount");
                }
            }
        }
        
        var companion = character->CompanionData.CompanionObject;
        if (companion != null)
        {
            var drawCompanion = companion->DrawObject;
            if (drawCompanion != null)
            {
                ImGui.Text($"Companion DrawObject Address: {(nint)drawCompanion:X8}");
                var companionType = drawCompanion->GetObjectType();
                ImGui.Text($"Companion Object Type: {companionType}");
                if (companionType == ObjectType.CharacterBase)
                {
                    var companionBase = (CharacterBase*)drawCompanion;
                    DrawCharacterBase(companionBase, "Companion");
                }
            }
        }

        var weapons = character->DrawData.WeaponData;
        for (int i = 0; i < weapons.Length; i++)
        {
            var weapon = weapons[i];
            if (weapon.DrawObject != null)
            {
                ImGui.Text($"Weapon {i} DrawObject Address: {(nint)weapon.DrawObject:X8}");
                var weaponType = weapon.DrawObject->GetObjectType();
                ImGui.Text($"Weapon {i} Object Type: {weaponType}");
                if (weaponType == ObjectType.CharacterBase)
                {
                    var weaponBase = (CharacterBase*)weapon.DrawObject;
                    DrawCharacterBase(weaponBase, $"Weapon {i}");
                }
            }
        }
    }

    private unsafe void DrawCharacterBase(CharacterBase* cBase, string name)
    {
        if (cBase == null)
        {
            ImGui.Text($"{name} CharacterBase is null");
            return;
        }
        using var id = ImRaii.PushId($"{(nint)cBase:X8}");
        if (ImGui.CollapsingHeader(name))
        {
            ImGui.Text($"Visible: {cBase->IsVisible}");
            ImGui.Text($"ModelType: {cBase->GetModelType()}");

            if (cBase->GetModelType() == CharacterBase.ModelType.Human)
            {
                var human = (Human*)cBase;
                ImGui.Text($"RaceSexId: {human->RaceSexId}");
                ImGui.Text($"HairId: {human->HairId}");
                ImGui.Text($"FaceId: {human->FaceId}");
                ImGui.Text($"TailEarId: {human->TailEarId}");
                ImGui.Text($"FurId: {human->FurId}");
                
                ImGui.Text($"Highlights: {human->Customize.Highlights}");
                ImGui.Text($"Lipstick: {human->Customize.Lipstick}");
            }
            
            var skeleton = cBase->Skeleton;
            if (skeleton == null)
            {
                ImGui.Text($"{name} Skeleton is null");
            }
            else
            {
                ImGui.Text($"Skeleton: {(nint)cBase->Skeleton:X8}");
                ImGui.Text($"Partial Skeleton Count: {cBase->Skeleton->PartialSkeletonCount}");
                using var skeletonIndent = ImRaii.PushIndent();
                if (ImGui.CollapsingHeader("Draw Bones"))
                {
                    // boneMode
                    ImGui.Text("Bone Mode");
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(200);
                    using (var combo = ImRaii.Combo("##BoneMode", boneModeInput.ToString()))
                    {
                        if (combo.Success)
                        {
                            foreach (BoneMode mode in Enum.GetValues(typeof(BoneMode)))
                            {
                                if (ImGui.Selectable(mode.ToString(), mode == boneModeInput))
                                {
                                    boneModeInput = mode;
                                }
                            }
                        }
                    }
                    
                    ImGui.Text("Bone Search");
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(200);
                    ImGui.InputText("##BoneSearch", ref boneSearch, 100);
                    
                    // imgui select partial skeleton by index
                    for (int i = 0; i < skeleton->PartialSkeletonCount; i++)
                    {
                        using var pskeletonIndent = ImRaii.PushIndent();
                        var partialSkeleton = skeleton->PartialSkeletons[i];
                        var handle = partialSkeleton.SkeletonResourceHandle;
                        if (handle == null) continue;
                        var path = handle->FileName.ParseString();
                        if (ImGui.CollapsingHeader($"Partial Skeleton {i}: {path}"))
                        {
                            var ex = (PartialSkeletonEx*)(&partialSkeleton);
                            var boneCount = ex->BoneCount;
                            ImGui.Text($"Partial Skeleton Bone Count: {boneCount}");
                            using var boneTable = ImRaii.Table($"##BoneTable{i}", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable);
                            ImGui.TableSetupColumn("Bone", ImGuiTableColumnFlags.WidthStretch);
                            ImGui.TableSetupColumn("Parent", ImGuiTableColumnFlags.WidthStretch);
                            ImGui.TableSetupColumn("Translation", ImGuiTableColumnFlags.WidthStretch);
                            ImGui.TableSetupColumn("Rotation", ImGuiTableColumnFlags.WidthStretch);
                            ImGui.TableSetupColumn("Scale", ImGuiTableColumnFlags.WidthStretch);
                            ImGui.TableHeadersRow();
                            DrawBoneTransformsOnScreen(partialSkeleton, boneModeInput);
                        }
                    }
                }
            }
           

            if (ImGui.CollapsingHeader("Draw Models"))
            {
                using var skeletonIndent = ImRaii.PushIndent();
                DrawModels(cBase);
            }
        }
    }

    private unsafe void DrawModels(CharacterBase* cBase)
    {
        var models = cBase->ModelsSpan;
        for (var i = 0; i < models.Length; i++)
        {
            var modelPtr = models[i];
            if (ImGui.CollapsingHeader($"Model {i} - {((nint)modelPtr.Value):X8}") && modelPtr.Value != null)
            {
                try
                {
                    using var modelIndent = ImRaii.PushIndent();
                    using var modelId = ImRaii.PushId(i);
                    var model = modelPtr.Value;
                    UiUtil.Text($"Model: {(nint)model:X8}", $"{(nint)model:X8}");
                    ImGui.Text($"Name: {model->ModelResourceHandle->FileName.ToString()}");
                    ImGui.Text($"Material Count: {model->MaterialsSpan.Length}");
                    ImGui.Text($"Bone Count: {model->BoneCount}");
                
                    if (ImGui.CollapsingHeader("Materials"))
                    {
                        try
                        {
                            using var materialIndent = ImRaii.PushIndent();
                            for (var materialIdx = 0; materialIdx < model->MaterialsSpan.Length; materialIdx++)
                            {
                                var material = model->MaterialsSpan[materialIdx];
                                if (ImGui.CollapsingHeader($"Material {materialIdx} - {(nint)material.Value:X8}") && material.Value != null)
                                {
                                    try
                                    {
                                        using var materialId = ImRaii.PushId(materialIdx);
                                        UiUtil.Text($"Material: {(nint)material.Value:X8}", $"{(nint)material.Value:X8}");
                                        UiUtil.Text($"Material Resource: {(nint)material.Value->MaterialResourceHandle:X8}", $"{(nint)material.Value->MaterialResourceHandle:X8}");
                                        ImGui.Text($"Texture Count: {material.Value->TextureCount}");

                                        for (var j = 0; j < material.Value->TextureCount; j++)
                                        {
                                            try
                                            {
                                                using var textureIndent = ImRaii.PushIndent();
                                                var texture = material.Value->Textures[j];
                                                if (ImGui.CollapsingHeader($"Texture {j} - {(nint)texture.Texture:X8}") && texture.Texture != null)
                                                {
                                                    try
                                                    {
                                                        using var textureId = ImRaii.PushId(j);
                                                        UiUtil.Text($"Texture: {(nint)texture.Texture:X8}", $"{(nint)texture.Texture:X8}");
                                                        ImGui.Text($"Id: {texture.Id}");
                                                        ImGui.Text($"SamplerFlags: {texture.SamplerFlags}");
                                                        ImGui.Text($"Texture: {texture.Texture->FileName.ToString()}");
                                                    }
                                                    catch (Exception e)
                                                    {
                                                        ImGui.Text($"Error: {e.Message}");
                                                    }
                                                }
                                            }
                                            catch (Exception e)
                                            {
                                                ImGui.Text($"Error: {e.Message}");
                                            }
                                        }
                                    }
                                    catch (Exception e)
                                    {
                                        ImGui.Text($"Error: {e.Message}");
                                    }
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            ImGui.Text($"Error: {e.Message}");
                        }
                    }
                }
                catch (Exception e)
                {
                    ImGui.Text($"Error: {e.Message}");
                }
            }
        }
    }

    private unsafe void DrawBoneTransformsOnScreen(PartialSkeleton partialSkeleton, BoneMode boneMode)
    {
        var rootPos = partialSkeleton.Skeleton->Transform;
        var rootTransform = new Transform(rootPos);
        var ex = (PartialSkeletonEx*)(&partialSkeleton);
        var pose = partialSkeleton.GetHavokPose(0);
        for (var i = 0; i < ex->BoneCount; i++)
        {
            var bone = pose->Skeleton->Bones[i];
            if (!string.IsNullOrEmpty(boneSearch) && bone.Name.String != null && !bone.Name.String.Contains(boneSearch))
            {
                continue;
            }
            
            var t = boneMode switch
            {
                BoneMode.Local => new Transform(pose->LocalPose[i]),
                BoneMode.ModelPropagate => new Transform(*pose->AccessBoneModelSpace(i, hkaPose.PropagateOrNot.Propagate)),
                BoneMode.ModelNoPropagate => new Transform(*pose->AccessBoneModelSpace(i, hkaPose.PropagateOrNot.DontPropagate)),
                BoneMode.ModelRaw => new Transform(pose->ModelPose[i]),
                _ => new Transform(pose->ModelPose[i])
            };

            var modelTransform = boneMode == BoneMode.Local ? new Transform(pose->ModelPose[i]) : t;
            var worldMatrix = modelTransform.AffineTransform.Matrix * rootTransform.AffineTransform.Matrix;
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text($"[{i}] {bone.Name.String}");
            var dotColorRgb = new Vector4(1, 1, 1, 0.5f);
            if (ImGui.IsItemHovered())
            {
                dotColorRgb = new Vector4(1, 0, 0, 0.5f);
            }
            
            ImGui.TableSetColumnIndex(1);
            var parentIndex = pose->Skeleton->ParentIndices[i];
            ImGui.Text(parentIndex.ToString());
            ImGui.TableSetColumnIndex(2);
            ImGui.Text($"{t.Translation:F3}");
            ImGui.TableSetColumnIndex(3);
            ImGui.Text($"X:{t.Rotation.X:F2} Y:{t.Rotation.Y:F2} " +
                       $"Z:{t.Rotation.Z:F2} W:{t.Rotation.W:F2}");
            ImGui.TableSetColumnIndex(4);
            ImGui.Text($"{t.Scale:F3}");
            
            if (gui.WorldToScreen(worldMatrix.Translation, out var screenPos))
            {
                var dotColor = ImGui.GetColorU32(dotColorRgb);
                ImGui.GetBackgroundDrawList().AddCircleFilled(screenPos, 5, dotColor);
            }
        }
    }
    
    private unsafe void ParseEnvLight()
    {
        var envMan = EnvManagerEx.Instance();
        if (envMan == null) throw new InvalidOperationException("EnvManagerEx is null");
        var envState = envMan->EnvState;
        var lighting = envState.Lighting;
        
        var sunCol = lighting.SunLightColor;
        ImGui.ColorButton("Sunlight Color", new Vector4(sunCol.Red, sunCol.Green, sunCol.Blue, 1.0f));
        
        var moonCol = lighting.MoonLightColor;
        ImGui.ColorButton("Moonlight Color", new Vector4(moonCol.Red, moonCol.Green, moonCol.Blue, 1.0f));
        
        var ambientCol = lighting.Ambient;
        ImGui.ColorButton("Ambient Color", new Vector4(ambientCol.Red, ambientCol.Green, ambientCol.Blue, 1.0f));
    }
}
