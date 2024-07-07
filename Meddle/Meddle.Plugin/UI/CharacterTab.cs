using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Memory;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.Interop;
using ImGuiNET;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Materials;
using Meddle.Utils.Models;
using Meddle.Utils.Skeletons.Havok;
using CSCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using CustomizeParameter = FFXIVClientStructs.FFXIV.Shader.CustomizeParameter;

namespace Meddle.Plugin.UI;

public unsafe partial class CharacterTab : ITab
{
    public string Name => "Character";
    public int Order => 0;
    
    private readonly IClientState clientState;
    private readonly IFramework framework;
    private readonly IPluginLog log;
    private readonly InteropService interopService;
    private readonly ExportUtil exportUtil;
    private readonly IObjectTable objectTable;
    private readonly IDataManager dataManager;
    private readonly ITextureProvider textureProvider;
    private readonly PbdFile pbdFile;
    private Task? exportTask;

    public CharacterTab(
        IObjectTable objectTable,
        IClientState clientState,
        IFramework framework,
        IPluginLog log,
        InteropService interopService,
        ExportUtil exportUtil,
        IDataManager dataManager, ITextureProvider textureProvider)
    {
        this.log = log;
        this.interopService = interopService;
        this.exportUtil = exportUtil;
        this.objectTable = objectTable;
        this.clientState = clientState;
        this.framework = framework;
        this.dataManager = dataManager;
        this.textureProvider = textureProvider;
        // chara/xls/bonedeformer/human.pbd
        var pbdData = dataManager.GameData.GetFile("chara/xls/bonedeformer/human.pbd");
        if (pbdData == null)
        {
            throw new Exception("Failed to load human.pbd");
        }
        pbdFile = new PbdFile(pbdData.DataSpan);
    }

    private ICharacter? SelectedCharacter { get; set; }

    public void Draw()
    {
        if (!interopService.IsResolved) return;
        DrawObjectPicker();
    }

    private void DrawObjectPicker()
    {
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

        if (SelectedCharacter != null)
        {
            var canParse = exportTask == null || exportTask.IsCompleted;
            if (exportTask == null)
            {
                exportTask = Task.Run(() => ParseCharacter(SelectedCharacter));
            }
            else
            {
                if (exportTask.IsFaulted)
                {
                    var exception = exportTask.Exception;
                    ImGui.Text($"Failed to parse character: {exception?.Message}");
                }
                
                ImGui.BeginDisabled(!canParse);
                if (ImGui.Button("Parse"))
                {  
                    exportTask = Task.Run(() => ParseCharacter(SelectedCharacter));
                }
                ImGui.EndDisabled();
            }
        }
        else
        {
            ImGui.TextWrapped("No character selected");
        }
        
        DrawCharacterGroup();
    }
    
    private ExportUtil.CharacterGroup? characterGroup;

    private void DrawSkeleton(Skeleton.Skeleton skeleton)
    {
        ImGui.Indent();
        foreach (var partialSkeleton in skeleton.PartialSkeletons)
        {
            if (ImGui.CollapsingHeader($"Partial Skeleton Connected At {partialSkeleton.ConnectedBoneIndex}"))
            {
                ImGui.Indent();
                var hkSkeleton = partialSkeleton.HkSkeleton;
                if (hkSkeleton == null)
                {
                    continue;
                }

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
                ImGui.Unindent();
            }
        }
        ImGui.Unindent();
    }
    
    private void DrawCharacterGroup()
    {
        if (characterGroup == null)
        {
            ImGui.Text("No character group");
            return;
        }
        
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
        } 
        finally
        {
            ImGui.Columns(1);
        }
        
        WrapCanParse(() =>
        {
            if (ImGui.Button($"Export All##{characterGroup.GetHashCode()}"))
            {
                exportTask = Task.Run(() => exportUtil.Export(characterGroup, pbdFile));
            }

            if (ImGui.Button($"Export Raw Textures##{characterGroup.GetHashCode()}"))
            {
                exportTask = Task.Run(() => exportUtil.ExportRawTextures(characterGroup));
            }
        });

        if (ImGui.CollapsingHeader("Export Individual"))
        {
            WrapCanParse(() =>
            {
                var selectedCount = SelectedModels.Count(x => characterGroup.MdlGroups.Any(y => y.Path == x.Key && x.Value));
                if (ImGui.Button($"Export {selectedCount} Selected Models##{characterGroup.GetHashCode()}") && selectedCount > 0)
                {
                    var selectedModels = characterGroup.MdlGroups
                                            .Where(
                                                x => SelectedModels.TryGetValue(x.Path, out var selected) && selected)
                                            .ToArray();
                    var group = characterGroup with {MdlGroups = selectedModels};
                    exportTask = Task.Run(() => exportUtil.Export(group, pbdFile));
                }
            });
            
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
                    WrapCanParse(() =>
                    {
                        if (ImGui.Button("Export"))
                        {
                            var group = characterGroup with {MdlGroups = [mdlGroup]};
                            exportTask = Task.Run(() => exportUtil.Export(group, pbdFile));
                        }
                    });
            
                    if (!SelectedModels.TryGetValue(mdlGroup.Path, out var selected))
                    {
                        selected = false;
                        SelectedModels[mdlGroup.Path] = selected;
                    }
                    
                    ImGui.SameLine();
                    if (ImGui.Checkbox($"##{mdlGroup.GetHashCode()}", ref selected))
                    {
                        SelectedModels[mdlGroup.Path] = selected;
                    }
            
                    ImGui.NextColumn();
                    ImGui.Text(mdlGroup.Path);
                    ImGui.NextColumn();
                    ImGui.PopID();
                }
            } 
            finally
            {
                ImGui.Columns(1);
            }
        }
        
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
    }
    
    private Dictionary<string, bool> SelectedModels = new();
    private Dictionary<string, bool> SelectedAttachedModels = new();
    
    private void WrapCanParse(Action action)
    {
        var canParse = exportTask == null || exportTask.IsCompleted;
        ImGui.BeginDisabled(!canParse);
        try
        {
            action();
        } finally
        {
            ImGui.EndDisabled();
        }
    }

    private List<HavokXml> ParseSkeletons(Human* human)
    {
        var skeletonResourceHandles =
            new Span<Pointer<SkeletonResourceHandle>>(human->Skeleton->SkeletonResourceHandles,
                                                      human->Skeleton->PartialSkeletonCount);
        var skeletons = new List<HavokXml>();
        foreach (var skeletonPtr in skeletonResourceHandles)
        {
            var skeletonResourceHandle = skeletonPtr.Value;
            if (skeletonResourceHandle == null)
            {
                continue;
            }

            var skeletonFileResource =
                dataManager.GameData.GetFile(skeletonResourceHandle->ResourceHandle.FileName.ToString());
            if (skeletonFileResource == null)
            {
                continue;
            }

            var sklbFile = new SklbFile(skeletonFileResource.DataSpan);
            var tempFile = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(tempFile, sklbFile.Skeleton.ToArray());
                var hkXml = framework.RunOnTick(() =>
                {
                    var xml = HkUtil.HkxToXml(tempFile);
                    return xml;
                }).GetAwaiter().GetResult();
                var havokXml = new HavokXml(hkXml);

                skeletons.Add(havokXml);
            } finally
            {
                File.Delete(tempFile);
            }
        }

        return skeletons;
    }

    private Dictionary<string, MaterialResourceHandle.ColorTableRow[]> ColorTableRows = new();

    private Model.MdlGroup? HandleModelPtr(FFXIVClientStructs.FFXIV.Client.Graphics.Render.Model* model)
    {
        if (model == null)
            return null;
        
        var mdlFileName = model->ModelResourceHandle->ResourceHandle.FileName.ToString();
        var mdlFileResource = dataManager.GameData.GetFile(mdlFileName);
        if (mdlFileResource == null)
        {
            return null;
        }

        var shapesMask = model->EnabledShapeKeyIndexMask;
        var shapes = new List<(string, short)>();
        foreach (var shape in model->ModelResourceHandle->Shapes)
        {
            shapes.Add((MemoryHelper.ReadStringNullTerminated((nint)shape.Item1.Value), shape.Item2));
        }
        
        var attributeMask = model->EnabledAttributeIndexMask;
        var attributes = new List<(string, short)>();
        foreach (var attribute in model->ModelResourceHandle->Attributes)
        {
            attributes.Add((MemoryHelper.ReadStringNullTerminated((nint)attribute.Item1.Value), attribute.Item2));
        }
        var shapeAttributeGroup = new Model.ShapeAttributeGroup(shapesMask, attributeMask, shapes.ToArray(), attributes.ToArray());
        
        var mdlFile = new MdlFile(mdlFileResource.DataSpan);
        var mtrlGroups = new List<Material.MtrlGroup>();

        for (var j = 0; j < model->MaterialsSpan.Length; j++)
        {
            var materialPtr = model->MaterialsSpan[j];
            var material = materialPtr.Value;
            if (material == null)
            {
                continue;
            }

            var tableLive = material->MaterialResourceHandle->ColorTableSpan;
            var mtrlFileName = material->MaterialResourceHandle->ResourceHandle.FileName.ToString();
            ColorTableRows[mtrlFileName] = tableLive.ToArray();
            var shader = material->MaterialResourceHandle->ShpkNameString;

            var mtrlFileResource = dataManager.GameData.GetFile(mtrlFileName);
            if (mtrlFileResource == null)
            {
                continue;
            }

            var mtrlFile = new MtrlFile(mtrlFileResource.DataSpan);

            var shpkFileResource = dataManager.GameData.GetFile($"shader/sm5/shpk/{shader}");
            if (shpkFileResource == null)
            {
                continue;
            }

            var shpkFile = new ShpkFile(shpkFileResource.DataSpan);

            var texGroups = new List<Texture.TexGroup>();

            var textureNames = mtrlFile.GetTexturePaths().Select(x => x.Value).ToArray();
            foreach (var textureName in textureNames)
            {
                var texFileResource = dataManager.GameData.GetFile(textureName);
                if (texFileResource == null)
                {
                    continue;
                }

                var texFile = new TexFile(texFileResource.DataSpan);
                texGroups.Add(new Texture.TexGroup(textureName, texFile));
            }

            mtrlGroups.Add(
                new Material.MtrlGroup(mtrlFileName, mtrlFile, shader, shpkFile, texGroups.ToArray()));
        }

        return new Model.MdlGroup(mdlFileName, mdlFile, mtrlGroups.ToArray(), shapeAttributeGroup);
    }
    
    private ExportUtil.AttachedModelGroup HandleAttachGroup(CharacterBase* characterBase)
    {
        var attach = new Meddle.Plugin.Skeleton.Attach(characterBase->Attach);
        var models = new List<Model.MdlGroup>();
        var skeleton = new Skeleton.Skeleton(characterBase->Skeleton);
        foreach (var modelPtr in characterBase->ModelsSpan)
        {
            var model = modelPtr.Value;
            if (model == null)
            {
                continue;
            }
                        
            var mdlGroup = HandleModelPtr(model);
            if (mdlGroup != null)
            {
                models.Add(mdlGroup);
            }
        }

        var attachGroup = new ExportUtil.AttachedModelGroup(attach, models.ToArray(), skeleton);
        return attachGroup;
    }
    
    private void ParseCharacter(ICharacter character)
    {            
        var charPtr = (CSCharacter*)character.Address;
        var drawObject = charPtr->GameObject.DrawObject;
        if (drawObject == null)
        {
            return;
        }
        
        var objectType = drawObject->Object.GetObjectType();
        if (objectType != ObjectType.CharacterBase)
            throw new InvalidOperationException($"Object type is not CharacterBase: {objectType}");
        
        var modelType = ((CharacterBase*)drawObject)->GetModelType();
        var characterBase = (CharacterBase*)drawObject;
        Meddle.Utils.Export.CustomizeParameter customizeParams;
        CustomizeData customizeData;
        GenderRace genderRace;
        if (modelType == CharacterBase.ModelType.Human)
        {
            var human = (Human*)drawObject;
            var customizeCBuf = human->CustomizeParameterCBuffer->TryGetBuffer<CustomizeParameter>()[0];
            customizeParams = new Meddle.Utils.Export.CustomizeParameter
            {
                SkinColor = customizeCBuf.SkinColor,
                SkinFresnelValue0 = customizeCBuf.SkinFresnelValue0,
                LipColor = customizeCBuf.LipColor,
                MainColor = customizeCBuf.MainColor,
                HairFresnelValue0 = customizeCBuf.HairFresnelValue0,
                MeshColor = customizeCBuf.MeshColor,
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
            customizeParams = new Meddle.Utils.Export.CustomizeParameter();
            customizeData = new CustomizeData();
            genderRace = GenderRace.Unknown;
        }
        
        var skeleton = new Skeleton.Skeleton(characterBase->Skeleton);
        var mdlGroups = new List<Model.MdlGroup>();
        foreach (var modelPtr in characterBase->ModelsSpan)
        {
            var model = modelPtr.Value;
            if (model == null)
            {
                continue;
            }
            
            var mdlGroup = HandleModelPtr(model);
            if (mdlGroup != null)
            {
                mdlGroups.Add(mdlGroup);
            }
        }
        
        var attachGroups = new List<ExportUtil.AttachedModelGroup>();
        // TODO: Mount/ornament/weapon
        if (charPtr->Mount.MountObject != null)
        {
            var mountDrawObject = charPtr->Mount.MountObject->GameObject.DrawObject;
            if (mountDrawObject != null && mountDrawObject->Object.GetObjectType() == ObjectType.CharacterBase)
            {
                var mountBase = (CharacterBase*)mountDrawObject;
                var attachGroup = HandleAttachGroup(mountBase);
                attachGroups.Add(attachGroup);
            }
        }
        
        if (charPtr->OrnamentData.OrnamentObject != null)
        {
            var ornamentDrawObject = charPtr->OrnamentData.OrnamentObject->DrawObject;
            if (ornamentDrawObject != null && ornamentDrawObject->Object.GetObjectType() == ObjectType.CharacterBase)
            {
                var ornamentBase = (CharacterBase*)ornamentDrawObject;
                var attachGroup = HandleAttachGroup(ornamentBase);
                attachGroups.Add(attachGroup);
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
                var attachGroup = HandleAttachGroup(weaponBase);
                attachGroups.Add(attachGroup);
            }
        }
        
        characterGroup = new ExportUtil.CharacterGroup(
            customizeParams, 
            customizeData, 
            genderRace,
            mdlGroups.ToArray(),
            skeleton,
            attachGroups.ToArray());
    }
    
    private void DrawMdlGroup(Model.MdlGroup mdlGroup)
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
    
    private void DrawMtrlGroup(Material.MtrlGroup mtrlGroup)
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
            } 
            finally
            {
                ImGui.Columns(1);
            }
        }

        if (ImGui.CollapsingHeader($"Shader Keys##{mtrlGroup.GetHashCode()}"))
        {
            var keys = mtrlGroup.MtrlFile.ShaderKeys;
            ImGui.Text("Keys");
            ImGui.Columns(2);
            ImGui.Text("Category");
            ImGui.NextColumn();
            ImGui.Text("Value");
            ImGui.NextColumn();
            
            for (var i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
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
        
        if (ImGui.CollapsingHeader($"Shader Values##{mtrlGroup.GetHashCode()}"))
        {
            var keys = mtrlGroup.MtrlFile.ShaderValues;
            ImGui.Text("Values");
            ImGui.Columns(2);
            ImGui.Text("Index");
            ImGui.NextColumn();
            ImGui.Text("Value");
            ImGui.NextColumn();
            
            for (var i = 0; i < keys.Length; i++)
            {
                var key = keys[i];
                ImGui.Text($"{i}");
                ImGui.NextColumn();
                ImGui.Text($"0x{key:X8}");
                ImGui.NextColumn();
            }
            
            ImGui.Columns(1);
        }
        
        if (ImGui.CollapsingHeader($"Color Table##{mtrlGroup.GetHashCode()}"))
        {
            UIUtil.DrawColorTable(mtrlGroup.MtrlFile);
            if (ColorTableRows.TryGetValue(mtrlGroup.Path, out var rows))
            {
                UIUtil.DrawColorTable(rows);
            }
        }
        
        foreach (var texGroup in mtrlGroup.TexFiles)
        {
            if (ImGui.CollapsingHeader($"{texGroup.Path}##{texGroup.GetHashCode()}"))
            {
                DrawTexGroup(texGroup);
            }
        }
    }
    
    private void DrawTexGroup(Texture.TexGroup texGroup)
    {
        ImGui.Text($"Path: {texGroup.Path}");
        ImGui.PushID(texGroup.GetHashCode());
        try
        {
            DrawTexFile(texGroup.Path, texGroup.TexFile);
        } finally
        {
            ImGui.PopID();
        }
    }

    private enum Channel : int
    {
        Red = 1,
        Green = 2,
        Blue = 4,
        Alpha = 8,
        Rgb = Red | Green | Blue,
        All = Red | Green | Blue | Alpha
    }
    
    private readonly Dictionary<string, TextureImage> textureCache = new();
    private record TextureImage(Texture Texture, SKTexture Bitmap, IDalamudTextureWrap Wrap);
    private readonly Dictionary<string, Channel> channelCache = new();
    private void DrawTexFile(string path, TexFile file)
    {
        ImGui.Text($"Width: {file.Header.Width}");
        ImGui.Text($"Height: {file.Header.Height}");
        ImGui.Text($"Depth: {file.Header.Depth}");
        ImGui.Text($"Mipmaps: {file.Header.CalculatedMips}");
        
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
                RawImageSpecification.Rgba32(file.Header.Width, file.Header.Height), pixelsCopy,
                "Meddle.Texture");
            
            textureImage = new TextureImage(texture, bitmap, wrap);
            textureCache[path] = textureImage;
        }
        
        var availableWidth = ImGui.GetContentRegionAvail().X;
        float displayWidth;
        float displayHeight;
        if (file.Header.Width > availableWidth)
        {
            displayWidth = availableWidth;
            displayHeight = file.Header.Height * (displayWidth / file.Header.Width);
        }
        else
        {
            displayWidth = file.Header.Width;
            displayHeight = file.Header.Height;
        }
        
        ImGui.Image(textureImage.Wrap.ImGuiHandle, new Vector2(displayWidth, displayHeight));

        if (ImGui.Button($"Export as .png"))
        {
            exportUtil.ExportTexture(textureImage.Bitmap.Bitmap, path);
        }
    }
    
    
    public void Dispose() 
    { 
        foreach (var (_, textureImage) in textureCache)
        {
            textureImage.Wrap.Dispose();
        }
    }
}
