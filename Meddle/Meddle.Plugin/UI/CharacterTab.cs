using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Shader;
using ImGuiNET;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Files.Structs.Material;
using Meddle.Utils.Materials;
using Meddle.Utils.Models;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SkiaSharp;
using CSCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;

namespace Meddle.Plugin.UI;

public unsafe partial class CharacterTab : ITab
{
    private readonly IClientState clientState;
    private readonly InteropService interopService;
    private readonly IPluginLog log;
    private readonly IObjectTable objectTable;
    private readonly IDataManager dataManager;
    private readonly ITextureProvider textureProvider;
    private Task? exportTask;

    public CharacterTab(
        IObjectTable objectTable,
        IClientState clientState,
        InteropService interopService,
        IPluginLog log,
        IDataManager dataManager, ITextureProvider textureProvider)
    {
        this.interopService = interopService;
        this.log = log;
        this.objectTable = objectTable;
        this.clientState = clientState;
        this.dataManager = dataManager;
        this.textureProvider = textureProvider;
    }

    private ICharacter? SelectedCharacter { get; set; }

    public string Name => "Character";

    public int Order => 0;

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
                                 .Where(obj => obj.IsValid() && IsValidCharacter(obj))
                                 .OrderBy(c => GetDistanceToLocalPlayer(c).LengthSquared())
                                 .ToArray();
        }
        else
        {
            objects = objectTable.OfType<ICharacter>()
                                 .Where(obj => IsValidCharacter(obj))
                                 .OrderBy(c => GetDistanceToLocalPlayer(c).LengthSquared())
                                 .ToArray();
        }

        if (SelectedCharacter == null)
        {
            SelectedCharacter = objects.FirstOrDefault() ?? clientState.LocalPlayer;
        }

        ImGui.Text("Select Character");
        var preview = SelectedCharacter != null ? GetCharacterDisplayText(SelectedCharacter) : "None";
        using (var combo = ImRaii.Combo("##Character", preview))
        {
            if (combo)
            {
                foreach (var character in objects)
                {
                    if (ImGui.Selectable(GetCharacterDisplayText(character)))
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
                ParseCharacter(SelectedCharacter);
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
                    ParseCharacter(SelectedCharacter);
                }
                ImGui.EndDisabled();
            }
        }
        else
        {
            ImGui.TextWrapped("No character selected");
        }
        
        DrawCache();
    }

    private Vector3 GetDistanceToLocalPlayer(IGameObject obj)
    {
        if (clientState.LocalPlayer is {Position: var charPos})
            return Vector3.Abs(obj.Position - charPos);
        return new Vector3(obj.YalmDistanceX, 0, obj.YalmDistanceZ);
    }

    private string GetCharacterDisplayText(ICharacter obj)
    {
        return
            $"{obj.Address:X8}:{obj.GameObjectId:X} - {obj.ObjectKind} - {(string.IsNullOrWhiteSpace(obj.Name.TextValue) ? "Unnamed" : obj.Name.TextValue)} - {GetDistanceToLocalPlayer(obj).Length():0.00}y";
    }

    private record CharacterGroup(CustomizeParameter CustomizeParams, CustomizeData CustomizeData, Model.MdlGroup[] MdlGroups);
    private CharacterGroup? characterGroup;
    
    private unsafe void DrawCache()
    {
        if (characterGroup == null)
        {
            return;
        }
        
        // draw customizeparams
        ImGui.Text("Customize Parameters");
        var skinColor = characterGroup.CustomizeParams.SkinColor.ToVector4();
        ImGui.ColorEdit4("Skin Color", ref skinColor);
        var skinFresnelValue = characterGroup.CustomizeParams.SkinFresnelValue0.ToVector4();
        ImGui.ColorEdit4("Skin Fresnel Value", ref skinFresnelValue);
        var lipColor = characterGroup.CustomizeParams.LipColor.ToVector4();
        ImGui.ColorEdit4("Lip Color", ref lipColor);
        var mainColor = characterGroup.CustomizeParams.MainColor.ToVector3();
        ImGui.ColorEdit3("Main Color", ref mainColor);
        var hairFresnelValue = characterGroup.CustomizeParams.HairFresnelValue0.ToVector3();
        ImGui.ColorEdit3("Hair Fresnel Value", ref hairFresnelValue);
        var meshColor = characterGroup.CustomizeParams.MeshColor.ToVector3();
        ImGui.ColorEdit3("Mesh Color", ref meshColor);
        var leftColor = characterGroup.CustomizeParams.LeftColor.ToVector4();
        ImGui.ColorEdit4("Left Color", ref leftColor);
        var rightColor = characterGroup.CustomizeParams.RightColor.ToVector4();
        ImGui.ColorEdit4("Right Color", ref rightColor);
        var optionColor = characterGroup.CustomizeParams.OptionColor.ToVector3();
        ImGui.ColorEdit3("Option Color", ref optionColor);
        
        // draw customize data
        ImGui.Text("Customize Data");
        var lipstick = characterGroup.CustomizeData.Lipstick;
        ImGui.Checkbox("Lipstick", ref lipstick);
        var highlights = characterGroup.CustomizeData.Highlights;
        ImGui.Checkbox("Highlights", ref highlights);

        
        var canParse = exportTask == null || exportTask.IsCompleted;
        ImGui.BeginDisabled(!canParse);
        if (ImGui.Button("Export All"))
        {
            Export(characterGroup);
        }

        if (ImGui.Button("Export Raw Textures"))
        {
            ExportRawTextures(characterGroup);
        }
        ImGui.EndDisabled();
        
        foreach (var mdlGroup in characterGroup.MdlGroups)
        {
            if (ImGui.CollapsingHeader($"{mdlGroup.Path}##{mdlGroup.GetHashCode()}"))
            {
                DrawMdlGroup(mdlGroup, characterGroup.CustomizeParams, characterGroup.CustomizeData);
            }
        }
    }

    private void ExportRawTextures(CharacterGroup characterGroup1)
    {
        exportTask = Task.Run(() =>
        {
            try
            {
                var folder = Path.Combine(Plugin.TempDirectory, "output", "textures");
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }
                else
                {
                    // delete and recreate
                    Directory.Delete(folder, true);
                    Directory.CreateDirectory(folder);
                }

                foreach (var mdlGroup in characterGroup1.MdlGroups)
                {
                    foreach (var mtrlGroup in mdlGroup.MtrlFiles)
                    {
                        foreach (var texGroup in mtrlGroup.TexFiles)
                        {
                            var outputPath = Path.Combine(folder, $"{Path.GetFileNameWithoutExtension(texGroup.Path)}.png");
                            var texture = new Texture(texGroup.TexFile, texGroup.Path, null, null, null);
                            var str = new SKDynamicMemoryWStream();
                            texture.ToTexture().Bitmap.Encode(str, SKEncodedImageFormat.Png, 100);

                            var data = str.DetachAsData().AsSpan();
                            File.WriteAllBytes(outputPath, data.ToArray());
                        }
                    }
                }
                Process.Start("explorer.exe", folder);
            }
            catch (Exception e)
            {
                log.Error(e, "Failed to export textures");
                throw;
            }
        });
    }

    private Vector3 Normalize(Vector3 v)
    {
        var length = v.Length();
        if (length == 0)
        {
            return v;
        }

        return v / length;
    }
    
    private Vector4 Normalize(Vector4 v)
    {
        var length = v.Length();
        if (length == 0)
        {
            return v;
        }

        return v / length;
    }
    
    private void ParseCharacter(ICharacter character)
    {            
        var charPtr = (CSCharacter*)character.Address;
        var human = (Human*)charPtr->GameObject.DrawObject;
        var customizeCBuf = human->CustomizeParameterCBuffer->TryGetBuffer<CustomizeParameter>();
        var customizeParams = customizeCBuf[0];
        customizeParams = new CustomizeParameter
        {
            SkinColor = Normalize(customizeParams.SkinColor),
            SkinFresnelValue0 = Normalize(customizeParams.SkinFresnelValue0),
            LipColor = Normalize(customizeParams.LipColor),
            MainColor = Normalize(customizeParams.MainColor),
            HairFresnelValue0 = Normalize(customizeParams.HairFresnelValue0),
            MeshColor = Normalize(customizeParams.MeshColor),
            LeftColor = Normalize(customizeParams.LeftColor),
            RightColor = Normalize(customizeParams.RightColor),
            OptionColor = Normalize(customizeParams.OptionColor)
        };
        var customize = human->Customize;
        exportTask = Task.Run(() =>
        {

            var mdlGroups = new List<Model.MdlGroup>();
            for (int i = 0; i < human->SlotCount; i++)
            {
                var model = human->Models[i];
                if (model == null)
                {
                    continue;
                }

                var mdlFileName = model->ModelResourceHandle->ResourceHandle.FileName.ToString();
                var mdlFileResource = dataManager.GameData.GetFile(mdlFileName);
                if (mdlFileResource == null)
                {
                    continue;
                }

                var mdlFile = new MdlFile(mdlFileResource.DataSpan);
                var mtrlGroups = new List<Material.MtrlGroup>();

                foreach (var materialPtr in model->MaterialsSpan)
                {
                    var material = materialPtr.Value;
                    if (material == null)
                    {
                        continue;
                    }

                    var mtrlFileName = material->MaterialResourceHandle->ResourceHandle.FileName.ToString();
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

                mdlGroups.Add(new Model.MdlGroup(mdlFileName, mdlFile, mtrlGroups.ToArray()));
            }
            
            characterGroup = new CharacterGroup(customizeParams, customize, mdlGroups.ToArray());
        });
    }

    private void Export(CharacterGroup characterGroup)
    {
        exportTask = Task.Run(() =>
        {
            try
            {
                var scene = new SceneBuilder();

                foreach (var mdlGroup in characterGroup.MdlGroups)
                {
                    if (mdlGroup.Path.Contains("b0003_top")) continue;
                    log.Information("Exporting {Path}", mdlGroup.Path);
                    var model = new Model(mdlGroup);
                    var materials = new List<MaterialBuilder>();
                    //Parallel.ForEach(mdlGroup.MtrlFiles, mtrlGroup =>
                    foreach (var mtrlGroup in mdlGroup.MtrlFiles)
                    {
                        log.Information("Exporting {Path}", mtrlGroup.Path);
                        var material = new Material(mtrlGroup);
                        var name =
                            $"{Path.GetFileNameWithoutExtension(material.HandlePath)}_{Path.GetFileNameWithoutExtension(material.ShaderPackageName)}";
                        var builder = material.ShaderPackageName switch
                        {
                            "bg.shpk" => MaterialUtility.BuildBg(material, name),
                            "bgprop.shpk" => MaterialUtility.BuildBgProp(material, name),
                            "character.shpk" => MaterialUtility.BuildCharacter(material, name),
                            "characterocclusion.shpk" => MaterialUtility.BuildCharacterOcclusion(material, name),
                            "characterlegacy.shpk" => MaterialUtility.BuildCharacterLegacy(material, name),
                            "charactertattoo.shpk" => MaterialUtility.BuildCharacterTattoo(
                                material, name, characterGroup.CustomizeParams, characterGroup.CustomizeData),
                            "hair.shpk" => MaterialUtility.BuildHair(material, name, characterGroup.CustomizeParams,
                                                                     characterGroup.CustomizeData),
                            "skin.shpk" => MaterialUtility.BuildSkin(material, name, characterGroup.CustomizeParams,
                                                                     characterGroup.CustomizeData),
                            "iris.shpk" => MaterialUtility.BuildIris(material, name, characterGroup.CustomizeParams,
                                                                     characterGroup.CustomizeData),
                            _ => MaterialUtility.BuildFallback(material, name)
                        };

                        materials.Add(builder);
                    }

                    var bones = Array.Empty<BoneNodeBuilder>();
                    var boneNodes = bones.Cast<NodeBuilder>().ToArray();
                    var meshes = ModelBuilder.BuildMeshes(model, materials, bones, null);
                    foreach (var mesh in meshes)
                    {
                        InstanceBuilder instance;
                        if (mesh.UseSkinning && boneNodes.Length > 0)
                        {
                            instance = scene.AddSkinnedMesh(mesh.Mesh, Matrix4x4.Identity, boneNodes);
                        }
                        else
                        {
                            instance = scene.AddRigidMesh(mesh.Mesh, Matrix4x4.Identity);
                        }

                        //ApplyMeshShapes(instance, model, mesh.Shapes);

                        // Remove subMeshes that are not enabled
                        if (mesh.Submesh != null)
                        {
                            // Reaper eye go away (might not be necessary since DT)
                            if (mesh.Submesh.Attributes.Contains("atr_eye_a"))
                            {
                                instance.Remove();
                            }
                            else if (!mesh.Submesh.Attributes.All(model.EnabledAttributes.Contains))
                            {
                                instance.Remove();
                            }
                        }
                    }
                }


                var sceneGraph = scene.ToGltf2();
                var outputPath = Path.Combine(Plugin.TempDirectory, "output", "model.mdl");
                var folder = Path.GetDirectoryName(outputPath) ?? "output";
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }
                else
                {
                    // delete and recreate
                    Directory.Delete(folder, true);
                    Directory.CreateDirectory(folder);
                }

                // replace extension with gltf
                outputPath = Path.ChangeExtension(outputPath, ".gltf");

                sceneGraph.SaveGLTF(outputPath);
                Process.Start("explorer.exe", folder);
            }
            catch (Exception e)
            {
                log.Error(e, "Failed to export character");
                throw;
            }
        });
    }
    
    private static void ApplyMeshShapes(InstanceBuilder builder, Model model, IReadOnlyList<string>? appliedShapes)
    {
        if (model.Shapes.Count == 0 || appliedShapes == null) return;

        // This will set the morphing value to 1 if the shape is enabled, 0 if not
        var shapes = model.Shapes
                          .Where(x => appliedShapes.Contains(x.Name))
                          .Select(x => (x, model.EnabledShapes.Contains(x.Name)));
        builder.Content.UseMorphing().SetValue(shapes.Select(x => x.Item2 ? 1f : 0).ToArray());
    }

    
    private void DrawMdlGroup(
        Model.MdlGroup mdlGroup, CustomizeParameter characterGroupCustomizeParams, CustomizeData characterGroupCustomizeData)
    {
        var canParse = exportTask == null || exportTask.IsCompleted;
        ImGui.BeginDisabled(!canParse);
        if (ImGui.Button("Export"))
        {
            var group = new CharacterGroup(characterGroupCustomizeParams, characterGroupCustomizeData, new[] {mdlGroup});
            Export(group);
        }
        ImGui.EndDisabled();
        
        ImGui.Text($"Path: {mdlGroup.Path}");
        ImGui.Text($"Mtrl Files: {mdlGroup.MtrlFiles.Length}");
        try
        {
            ImGui.Indent();
            foreach (var mtrlGroup in mdlGroup.MtrlFiles)
            {
                if (ImGui.CollapsingHeader($"{mtrlGroup.Path}##{mtrlGroup.GetHashCode()}"))
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
            }
        }
        finally
        {
            ImGui.Unindent();
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
                ImGui.NextColumn();
                ImGui.Text($"0x{key.Value:X8}");
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
            DrawColorTable(mtrlGroup.MtrlFile);
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
        DrawTexFile(texGroup.Path, texGroup.TexFile);
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
    
    private Dictionary<string, TextureImage> textureCache = new();
    private record TextureImage(Texture Texture, SKTexture Bitmap, IDalamudTextureWrap Wrap);
    private Dictionary<string, Channel> channelCache = new();
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
        
        ImGui.Text("Channels");
        var channelsInt = (int)channels;
        bool changed = ImGui.CheckboxFlags($"Red##{path}", ref channelsInt, (int)Channel.Red) ||
            ImGui.CheckboxFlags($"Green##{path}", ref channelsInt, (int)Channel.Green) ||
            ImGui.CheckboxFlags($"Blue##{path}", ref channelsInt, (int)Channel.Blue) ||
            ImGui.CheckboxFlags($"Alpha##{path}", ref channelsInt, (int)Channel.Alpha);
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
            
            
            var pixelsCopy = new byte[bitmap.Bitmap.GetPixelSpan().Length];
            bitmap.Bitmap.GetPixelSpan().CopyTo(pixelsCopy);
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

        if (ImGui.Button($"Export as .png##{path}"))
        {
            var outputPath = Path.Combine(Path.GetTempPath(), "Meddle.Export", "output", $"{Path.GetFileNameWithoutExtension(path)}.png");
            var folder = Path.GetDirectoryName(outputPath) ?? "output";
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            else
            {
                // delete and recreate
                Directory.Delete(folder, true);
                Directory.CreateDirectory(folder);
            }
            
            var str = new SKDynamicMemoryWStream();
            textureImage.Bitmap.Bitmap.Encode(str, SKEncodedImageFormat.Png, 100);

            var data = str.DetachAsData().AsSpan();
            File.WriteAllBytes(outputPath, data.ToArray());
            Process.Start("explorer.exe", folder);
        }
    }
    
    private void DrawColorTable(MtrlFile file)
    {
        ImGui.Text($"Color Table: {file.HasTable}");
        ImGui.Text($"Dye Table: {file.HasDyeTable}");
        ImGui.Text($"Extended Color Table: {file.LargeColorTable}");
        if (!file.HasTable)
        {
            return;
        }

        ImGui.Columns(9, "ColorTable", true);
        ImGui.Text("Row");
        ImGui.NextColumn();
        ImGui.Text("Diffuse");
        ImGui.NextColumn();
        ImGui.Text("Specular");
        ImGui.NextColumn();
        ImGui.Text("Emissive");
        ImGui.NextColumn();
        ImGui.Text("Material Repeat");
        ImGui.NextColumn();
        ImGui.Text("Material Skew");
        ImGui.NextColumn();
        ImGui.Text("Specular");
        ImGui.NextColumn();
        ImGui.Text("Gloss");
        ImGui.NextColumn();
        ImGui.Text("Tile Set");
        ImGui.NextColumn();

        for (var i = 0; i < (file.LargeColorTable ? ColorTable.NumRows : ColorTable.LegacyNumRows); i++)
        {
            if (file.LargeColorTable)
            {
                DrawRow(i, file);
            }
            else
            {
                DrawLegacyRow(i, file);
            }
        }

        ImGui.Columns(1);
    }

    private void DrawRow(int i, MtrlFile file)
    {
        ref var row = ref file.ColorTable.GetRow(i);
        ImGui.Text($"{i}");
        ImGui.NextColumn();
        ImGui.ColorButton($"##rowdiff", new Vector4(row.Diffuse, 1f), ImGuiColorEditFlags.NoAlpha);
        if (file.HasDyeTable)
        {
            ImGui.SameLine();
            var diff = file.ColorDyeTable[i].Diffuse;
            ImGui.Checkbox($"##rowdiff", ref diff);
        }

        ImGui.NextColumn();
        ImGui.ColorButton($"##rowspec", new Vector4(row.Specular, 1f), ImGuiColorEditFlags.NoAlpha);
        if (file.HasDyeTable)
        {
            ImGui.SameLine();
            var spec = file.ColorDyeTable[i].Specular;
            ImGui.Checkbox($"##rowspec", ref spec);
        }

        ImGui.NextColumn();
        ImGui.ColorButton($"##rowemm", new Vector4(row.Emissive, 1f), ImGuiColorEditFlags.NoAlpha);
        if (file.HasDyeTable)
        {
            ImGui.SameLine();
            var emm = file.ColorDyeTable[i].Emissive;
            ImGui.Checkbox($"##rowemm", ref emm);
        }

        ImGui.NextColumn();
        ImGui.Text($"{row.MaterialRepeat}");
        ImGui.NextColumn();
        ImGui.Text($"{row.MaterialSkew}");
        ImGui.NextColumn();
        if (file.HasDyeTable)
        {
            var specStr = file.ColorDyeTable[i].SpecularStrength;
            ImGui.Checkbox($"##rowspecstr", ref specStr);
            ImGui.SameLine();
        }

        ImGui.Text($"{row.SpecularStrength}");
        ImGui.NextColumn();
        if (file.HasDyeTable)
        {
            var gloss = file.ColorDyeTable[i].Gloss;
            ImGui.Checkbox($"##rowgloss", ref gloss);
            ImGui.SameLine();
        }

        ImGui.Text($"{row.GlossStrength}");
        ImGui.NextColumn();
        ImGui.Text($"{row.TileSet}");
        ImGui.NextColumn();
    }

    private void DrawLegacyRow(int i, MtrlFile file)
    {
        ref var row = ref file.ColorTable.GetLegacyRow(i);
        ImGui.Text($"{i}");
        ImGui.NextColumn();
        ImGui.ColorButton($"##rowdiff", new Vector4(row.Diffuse, 1f), ImGuiColorEditFlags.NoAlpha);
        if (file.HasDyeTable)
        {
            ImGui.SameLine();
            var diff = file.ColorDyeTable[i].Diffuse;
            ImGui.Checkbox($"##rowdiff", ref diff);
        }

        ImGui.NextColumn();
        ImGui.ColorButton($"##rowspec", new Vector4(row.Specular, 1f), ImGuiColorEditFlags.NoAlpha);
        if (file.HasDyeTable)
        {
            ImGui.SameLine();
            var spec = file.ColorDyeTable[i].Specular;
            ImGui.Checkbox($"##rowspec", ref spec);
        }

        ImGui.NextColumn();
        ImGui.ColorButton($"##rowemm", new Vector4(row.Emissive, 1f), ImGuiColorEditFlags.NoAlpha);
        if (file.HasDyeTable)
        {
            ImGui.SameLine();
            var emm = file.ColorDyeTable[i].Emissive;
            ImGui.Checkbox($"##rowemm", ref emm);
        }

        ImGui.NextColumn();
        ImGui.Text($"{row.MaterialRepeat}");
        ImGui.NextColumn();
        ImGui.Text($"{row.MaterialSkew}");
        ImGui.NextColumn();
        if (file.HasDyeTable)
        {
            var specStr = file.ColorDyeTable[i].SpecularStrength;
            ImGui.Checkbox($"##rowspecstr", ref specStr);
            ImGui.SameLine();
        }

        ImGui.Text($"{row.SpecularStrength}");
        ImGui.NextColumn();
        if (file.HasDyeTable)
        {
            var gloss = file.ColorDyeTable[i].Gloss;
            ImGui.Checkbox($"##rowgloss", ref gloss);
            ImGui.SameLine();
        }

        ImGui.Text($"{row.GlossStrength}");
        ImGui.NextColumn();
        ImGui.Text($"{row.TileSet}");
        ImGui.NextColumn();
    }
    
    public static bool IsValidCharacter(ICharacter obj)
    {
        var drawObject = ((CSCharacter*)obj.Address)->GameObject.DrawObject;
        if (drawObject == null)
            return false;
        if (drawObject->Object.GetObjectType() != ObjectType.CharacterBase)
            return false;
        if (((CharacterBase*)drawObject)->GetModelType() != CharacterBase.ModelType.Human)
            return false;

        if (!drawObject->IsVisible)
        {
            return false;
        }
        
        return true;
    }
    
    public void Dispose() 
    { 
        foreach (var (_, textureImage) in textureCache)
        {
            textureImage.Wrap.Dispose();
        }
    }
}
