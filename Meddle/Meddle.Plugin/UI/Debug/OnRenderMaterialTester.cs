using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Meddle.Plugin.Services;
using Meddle.Plugin.Utils;

namespace Meddle.Plugin.UI.Debug;

public class OnRenderMaterialTester : IService
{
    private readonly CommonUi commonUi;
    private readonly TextureCache textureCache;
    private readonly ITextureProvider textureProvider;
    private ICharacter? selectedCharacter;

    public OnRenderMaterialTester(CommonUi commonUi, TextureCache textureCache, ITextureProvider textureProvider)
    {
        this.commonUi = commonUi;
        this.textureCache = textureCache;
        this.textureProvider = textureProvider;
    }

    public unsafe void Draw()
    {
        commonUi.DrawCharacterSelect(ref selectedCharacter, CharacterValidationFlags.IsVisible);

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
        for (var i = 0; i < human->ModelsSpan.Length; i++)
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

            using var modelId = ImRaii.PushId($"model_{i}");
            if (ImGui.CollapsingHeader($"Model {i} - {model.Value->ModelResourceHandle->FileName.ToString()}"))
            {
                using var modelIndent = ImRaii.PushIndent();
                for (var j = 0; j < model.Value->MaterialsSpan.Length; j++)
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
                    var materialFileName = material.Value->MaterialResourceHandle->FileName.ToString();
                    if (ImGui.CollapsingHeader($"Material {j} - {materialFileName}"))
                    {
                        using var materialIndent = ImRaii.PushIndent();
                        var materialShpk = material.Value->MaterialResourceHandle->ShpkName.ToString();
                        ImGui.Text($"Material SHPK: {materialShpk}");

                        var output = OnRenderMaterialUtil.ResolveHumanOnRenderMaterial(human, model, (uint)j);
                        DrawOutput(output);
                    }
                }
            }
        }
    }

    private void DrawOutput(OnRenderMaterialOutput output)
    {
        var serialized = System.Text.Json.JsonSerializer.Serialize(output, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
        ImGui.TextWrapped(serialized);

        var decalTexture = output.DecalTexture;
        if (decalTexture != null)
        {
            var wrap = textureCache.GetOrAdd($"{decalTexture.GetHashCode()}", () =>
            {
                using var bitmap = decalTexture.Bitmap;
                var textureData = bitmap.GetPixelSpan();
                return textureProvider.CreateFromRaw(
                    RawImageSpecification.Rgba32(decalTexture.Width, decalTexture.Height), textureData,
                    $"Meddle_Decal_{decalTexture.GetHashCode()}");
            });
            var availableWidth = ImGui.GetContentRegionAvail().X;
            float displayWidth = decalTexture.Width;
            float displayHeight = decalTexture.Height;
            if (displayWidth > availableWidth)
            {
                var ratio = availableWidth / displayWidth;
                displayWidth *= ratio;
                displayHeight *= ratio;
            }
            ImGui.Image(wrap.Handle, new Vector2(displayWidth, displayHeight));
        }
    }
}
