using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Meddle.Plugin.Models;
using Meddle.Plugin.Services;
using Meddle.Plugin.Services.UI;
using Meddle.Plugin.Utils;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.UI;

public class TestFunctionTab : ITab
{
    private readonly ILogger<TestFunctionTab> log;
    private readonly Configuration config;
    private readonly CommonUi commonUi;
    private readonly TextureCache textureCache;
    private readonly ITextureProvider textureProvider;
    private ICharacter? selectedCharacter;
    private OnRenderMaterialOutput? output;
    public string Name => "Test Functions";
    public int Order => 100;
    public MenuType MenuType => MenuType.Debug;
    
    public TestFunctionTab(
        ILogger<TestFunctionTab> log,
        Configuration config,
        CommonUi commonUi, TextureCache textureCache, ITextureProvider textureProvider)
    {
        this.log = log;
        this.config = config;
        this.commonUi = commonUi;
        this.textureCache = textureCache;
        this.textureProvider = textureProvider;
    }
    
    public unsafe void Draw()
    {
        commonUi.DrawCharacterSelect(ref selectedCharacter, ObjectUtil.ValidationFlags.IsVisible);
        if (output != null)
        {
            var serialized = System.Text.Json.JsonSerializer.Serialize(output, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            ImGui.TextWrapped(serialized);
            if (output.DecalTexture != null)
            {
                var wrap = textureCache.GetOrAdd($"{output.DecalTexture.GetHashCode()}", () =>
                {
                    var textureData = output.DecalTexture.Bitmap.GetPixelSpan();
                    var wrap = textureProvider.CreateFromRaw(
                        RawImageSpecification.Rgba32(output.DecalTexture.Width, output.DecalTexture.Height), textureData,
                        $"Meddle_Decal_{output.DecalTexture.GetHashCode()}");
                    return wrap;
                });
                var availableWidth = ImGui.GetContentRegionAvail().X;
                float displayWidth = output.DecalTexture.Width;
                float displayHeight = output.DecalTexture.Height;
                if (displayWidth > availableWidth)
                {
                    var ratio = availableWidth / displayWidth;
                    displayWidth *= ratio;
                    displayHeight *= ratio;
                }
                ImGui.Image(wrap.Handle, new Vector2(displayWidth, displayHeight));
            }
            
            if (ImGui.Button("Clear Output"))
            {
                output = null;
            }
        }
        
        if (selectedCharacter == null)
        {
            ImGui.Text("No character selected");
            return;
        }
        
        var character = (Character*)selectedCharacter.Address;
        var drawObject = character->GameObject.DrawObject;
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
            
        var cBase = (CharacterBase*)drawObject;
        var modelType = cBase->GetModelType();
        if (modelType != CharacterBase.ModelType.Human)
        {
            ImGui.Text("Model is not human");
            return;
        }
            
            
        var human = (Human*)cBase;
        for (int i = 0; i < human->ModelsSpan.Length; i++)
        {
            var model = human->ModelsSpan[i];
            if (model == null || model.Value == null)
            {
                ImGui.Text($"Model {i} is null");
                continue;
            }
            
            if (model.Value->ModelResourceHandle == null)
            {
                ImGui.Text($"Model {i} resource handle is null");
                continue;
            }
            
            using var id = ImRaii.PushId($"model_{i}");
            using var modelNode = ImRaii.TreeNode($"Model {i} - {model.Value->ModelResourceHandle->FileName.ToString()}");
            if (modelNode.Success)
            {
                for (int j = 0; j < model.Value->MaterialsSpan.Length; j++)
                {
                    var material = model.Value->MaterialsSpan[j];
                    if (material == null || material.Value == null)
                    {
                        ImGui.Text($"Material {j} is null");
                        continue;
                    }
                    
                    if (material.Value->MaterialResourceHandle == null)
                    {
                        ImGui.Text($"Material {j} resource handle is null");
                        continue;
                    }
                    
                    using var matId = ImRaii.PushId($"material_{j}");
                    using var materialNode = ImRaii.TreeNode($"Material {j} - {material.Value->MaterialResourceHandle->FileName.ToString()}");
                    if (materialNode.Success)
                    {
                        var materialShpk = material.Value->MaterialResourceHandle->ShpkName.ToString();
                        ImGui.Text($"Material SHPK: {materialShpk}");
                        if (ImGui.Button("Resolve"))
                        {
                            var resolveOutput = OnRenderMaterialUtil.ResolveHumanOnRenderMaterial(human, model, (uint)j);
                            output = resolveOutput;
                        }
                    }
                }
            }
        }
    }
    
    
    public void Dispose()
    {
        // TODO release managed resources here
    }
}
