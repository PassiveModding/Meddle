using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using Lumina.Models.Materials;
using Penumbra.Api;
using Xande;
using Xande.Enums;
using Character = Dalamud.Game.ClientState.Objects.Types.Character;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace Meddle.Xande.Utility;

public static class CharacterUtility
{
    public static string? ResolveGameObjectPath(string key, ushort selectedObjectObjectIndex, string? suspectedPath, DalamudPluginInterface pluginInterface)
    {
        string?[] gamePaths = Ipc.ReverseResolveGameObjectPath.Subscriber(pluginInterface)
            .Invoke(key, selectedObjectObjectIndex);
        
        var orderedPaths = gamePaths
            .Where(x => x != null)
            .OrderByDescending(x => x?.ComputeLd(suspectedPath ?? key))
            .ToArray();
        
        return orderedPaths.FirstOrDefault() ?? suspectedPath;
    }
    
    public static unsafe bool HasDrawObject(ushort gameObjectId, IObjectTable objectTable)
    {
        var characters = objectTable.OfType<Character>();

        var match = characters.FirstOrDefault(x => x.ObjectIndex == gameObjectId);
        if (match == null || !match.IsValid())
        {
            return false;
        }

        var gameObject = (GameObject*) match.Address;
        var drawObject = gameObject->GetDrawObject();
        if (drawObject == null)
        {
            return false;
        }

        return true;
    }
    
    public static unsafe Models.Character? GetCharacterInfo(ushort gameObjectId, IObjectTable objectTable, LuminaManager lumina)
    {
        var characters = objectTable.OfType<Character>();

        var match = characters.FirstOrDefault(x => x.ObjectIndex == gameObjectId);
        if (match == null || !match.IsValid())
        {
            return null;
        }

        //var gameObjectAddress = match.Address;
        var gameObject = (GameObject*) match.Address;
        var drawObject = gameObject->GetDrawObject();
        if (drawObject == null)
        {
            return null;
        }

        var character = (CharacterBase*) drawObject;
        var raceCode = character->GetModelType() == CharacterBase.ModelType.Human
            ? (GenderRace) ((Human*) character)->RaceSexId
            : GenderRace.Unknown;

        var characterData = new Models.Character
        {
            GenderRace = raceCode,
            SelectedObjectObjectIndex = gameObjectId
        };

        for (var i = 0; i < character->SlotCount; i++)
        {
            var model = character->Models[i];
            if (model == null || model->ModelResourceHandle == null || model->ModelResourceHandle->ResourceHandle.Type.Category != ResourceHandleType.HandleCategory.Chara)
            {
                continue;
            }

            var mdlHandle = model->ModelResourceHandle;
            //var modelName = mdlHandle->ResourceHandle.FileName.ToString();
            var activeModelPath = GetResourceHandleFileName(&mdlHandle->ResourceHandle);
            if (activeModelPath == null) continue;
            
            var luminaModel = ModelUtility.GetModel(lumina, activeModelPath);
            characterData.AddModel(activeModelPath);

            for (var j = 0; j < model->MaterialsSpan.Length; j++)
            {
                var drawMaterial = model->MaterialsSpan[j].Value;
                var luminaMaterial = luminaModel.Materials[j];
                
                luminaMaterial.Update(lumina.GameData);

                
                if (drawMaterial == null)
                {
                    continue;
                }

                if (drawMaterial->MaterialResourceHandle == null)
                {
                    continue;
                }

                var mtrlResource = drawMaterial->MaterialResourceHandle;
                if (mtrlResource == null)
                {
                    continue;
                }

                var activeMaterialPath = GetResourceHandleFileName(&mtrlResource->ResourceHandle);
                if (activeMaterialPath == null) continue;
                var luminaMaterialPath = luminaMaterial.ResolvedPath ?? luminaMaterial.MaterialPath;
                characterData.AddMaterial(activeModelPath, activeMaterialPath, luminaMaterialPath);

                var shaderResource = mtrlResource->ShaderPackageResourceHandle;
                if (shaderResource != null)
                {
                    var activeShaderPath = GetResourceHandleFileName(&shaderResource->ResourceHandle);
                    if (activeShaderPath != null)
                    {
                        characterData.AddShaderpack(activeModelPath, activeMaterialPath, activeShaderPath);
                    }
                }

                for (var k = 0; k < mtrlResource->TexturesSpan.Length; k++)
                {
                    var drawTexture = mtrlResource->TexturesSpan[k];

                    Texture? luminaTexture = null;
                    if (luminaMaterial.Textures != null && luminaMaterial.Textures.Length > k)
                    {
                        luminaTexture = luminaMaterial.Textures[k];
                    }

                    if (drawTexture.TextureResourceHandle == null)
                    {
                        continue;
                    }

                    var activeTexturePath = GetResourceHandleFileName(&drawTexture.TextureResourceHandle->ResourceHandle);
                    if (activeTexturePath != null)
                    {
                        var luminaTexturePath = luminaTexture?.TexturePath ?? string.Empty;
                        characterData.AddTexture(activeModelPath, activeMaterialPath, activeTexturePath, luminaTexturePath);
                    }
                }
            }
        }

        var skeleton = character->Skeleton;
        if (skeleton != null)
        {
            for (var i = 0; i < skeleton->PartialSkeletonCount; i++)
            {
                var partialSkeleton = skeleton->PartialSkeletons[i];
                var handle = partialSkeleton.SkeletonResourceHandle;
                if (handle == null)
                {
                    continue;
                }

                var activeSkeletonPath = GetResourceHandleFileName(&handle->ResourceHandle);
                if (activeSkeletonPath == null) continue;
                characterData.AddSkeleton(activeSkeletonPath);

                if (partialSkeleton.SkeletonParameterResourceHandle == null) continue;
                var skeletonParameterName = GetResourceHandleFileName((ResourceHandle*) partialSkeleton.SkeletonParameterResourceHandle);
                if (skeletonParameterName != null)
                {
                    characterData.AddSkeletonParameters(activeSkeletonPath, skeletonParameterName);
                }
            }
        }

        return characterData;
    }

    private static unsafe string? GetResourceHandleFileName(ResourceHandle* handle)
    {
        if (handle == null)
            return null;

        var name = handle->FileName.ToString();

        if (name[0] == '|')
        {
            var pos = name.IndexOf('|', 1);
            if (pos < 0)
                return string.Empty;

            name = name[(pos + 1)..];
        }
        
        if (Path.IsPathRooted(name))
        {
            name = name.Replace("/", "\\");
        }

        return name;
    }
}