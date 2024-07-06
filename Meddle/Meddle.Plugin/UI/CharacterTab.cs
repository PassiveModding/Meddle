using System.Numerics;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using ImGuiNET;
using Meddle.Plugin.Utils;
using Meddle.Utils;
using Meddle.Utils.Export;
using Meddle.Utils.Files;
using Meddle.Utils.Materials;
using Meddle.Utils.Models;
using Meddle.Utils.Skeletons;
using CSCharacter = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using CustomizeParameter = FFXIVClientStructs.FFXIV.Shader.CustomizeParameter;

namespace Meddle.Plugin.UI;

public unsafe partial class CharacterTab : ITab
{
    public string Name => "Character";
    public int Order => 0;
    
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
            // login/char creator produces "invalid" characters but are still usable I guess
            objects = objectTable.OfType<ICharacter>()
                                 .Where(obj => IsValidCharacter(obj))
                                 .OrderBy(c => GetDistanceToLocalPlayer(c).LengthSquared())
                                 .ToArray();
        }

        SelectedCharacter ??= objects.FirstOrDefault() ?? clientState.LocalPlayer;

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
        
        DrawCharacterGroup();
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

    private ExportUtil.CharacterGroup? characterGroup;
    
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
                exportTask = Task.Run(() => ExportUtil.Export(characterGroup));
            }

            if (ImGui.Button($"Export Raw Textures##{characterGroup.GetHashCode()}"))
            {
                exportTask = Task.Run(() => ExportUtil.ExportRawTextures(characterGroup));
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
                    exportTask = Task.Run(() => ExportUtil.Export(group));
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
                            exportTask = Task.Run(() => ExportUtil.Export(group));
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
    }
    
    private Dictionary<string, bool> SelectedModels = new();
    
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
        var skeleton = new Skeleton(human->Skeleton);
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
            
            characterGroup = new ExportUtil.CharacterGroup(
                new Meddle.Utils.Export.CustomizeParameter(customizeParams), 
                customize, 
                mdlGroups.ToArray(),
                skeleton);
        });
    }
    
    private void DrawMdlGroup(Model.MdlGroup mdlGroup)
    {
        ImGui.Text($"Path: {mdlGroup.Path}");
        ImGui.Text($"Mtrl Files: {mdlGroup.MtrlFiles.Length}");
        try
        {
            ImGui.Indent();
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
            UIUtil.DrawColorTable(mtrlGroup.MtrlFile);
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
            ExportUtil.ExportTexture(textureImage.Bitmap.Bitmap, path);
        }
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
