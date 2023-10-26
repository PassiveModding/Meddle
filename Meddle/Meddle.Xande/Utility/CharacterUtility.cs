using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using Meddle.Xande.Models;
using Xande.Enums;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace Meddle.Xande.Utility;

public static class CharacterUtility
{
    public static unsafe CharacterResourceMap? GetCharacterInfo(ushort gameObjectId, IObjectTable objectTable)
    {
        var characters = objectTable.OfType<Character>();

        var match = characters.FirstOrDefault(x => x.ObjectIndex == gameObjectId);
        if (match == null || !match.IsValid())
        {
            return null;
        }

        //var gameObjectAddress = match.Address;
        var gameObject = (GameObject*) match.Address;
        //var character = (FFXIVClientStructs.FFXIV.Client.Game.Character.Character*)gameObjectAddress;
        var drawObject = gameObject->GetDrawObject();
        if (drawObject == null)
        {
            return null;
        }

        var model = (CharacterBase*) drawObject;
        var raceCode = model->GetModelType() == CharacterBase.ModelType.Human
            ? (GenderRace) ((Human*) model)->RaceSexId
            : GenderRace.Unknown;

        // model -> mtrl -> tex
        var output = new Dictionary<string, Dictionary<string, List<string>>>();

        for (var i = 0; i < model->SlotCount; i++)
        {
            var mdl = model->Models[i];
            if (mdl == null ||
                mdl->ModelResourceHandle == null) //|| mdl->ModelResourceHandle->Category != ResourceCategory.Chara)
            {
                continue;
            }

            var mdlHandle = mdl->ModelResourceHandle;
            //var modelName = mdlHandle->ResourceHandle.FileName.ToString();
            var modelName = GetResourceHandleFileName(&mdlHandle->ResourceHandle);
            if (modelName == null || output.ContainsKey(modelName)) continue;
            output[modelName] = new Dictionary<string, List<string>>();

            for (var j = 0; j < mdl->MaterialCount; j++)
            {
                var mtrl = mdl->Materials[j];
                if (mtrl == null)
                {
                    continue;
                }

                if (mtrl->MaterialResourceHandle == null)
                {
                    continue;
                }

                var mtrlResource = mtrl->MaterialResourceHandle;

                if (mtrlResource == null)
                {
                    continue;
                }

                //var mtrlName = mtrl->MaterialResourceHandle->ResourceHandle.FileName.ToString();
                var mtrlName = GetResourceHandleFileName(&mtrlResource->ResourceHandle);
                if (mtrlName == null || output[modelName].ContainsKey(mtrlName)) continue;
                output[modelName][mtrlName] = new List<string>();


                var shaderResource = mtrlResource->ShaderPackageResourceHandle;
                if (shaderResource != null)
                {
                    //var shaderName = shaderResource->ResourceHandle.FileName.ToString();
                    var shaderName = GetResourceHandleFileName(&shaderResource->ResourceHandle);

                    if (shaderName != null && !output[modelName][mtrlName].Contains(shaderName))
                    {
                        output[modelName][mtrlName].Add(shaderName);
                    }
                }

                for (var k = 0; k < mtrlResource->TexturesSpan.Length; k++)
                {
                    var tex = mtrlResource->TexturesSpan[k];
                    if (tex.TextureResourceHandle == null)
                    {
                        continue;
                    }

                    //var texName = tex.TextureResourceHandle->ResourceHandle.FileName.ToString();
                    var texName = GetResourceHandleFileName(&tex.TextureResourceHandle->ResourceHandle);

                    if (texName != null && !output[modelName][mtrlName].Contains(texName))
                    {
                        output[modelName][mtrlName].Add(texName);
                    }
                }
            }
        }

        var skeleton = model->Skeleton;
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

                var skeletonName = GetResourceHandleFileName(&handle->ResourceHandle);
                if (skeletonName == null || output.ContainsKey(skeletonName)) continue;
                output[skeletonName] = new Dictionary<string, List<string>>();

                if (partialSkeleton.SkeletonParameterResourceHandle == null) continue;
                var skeletonParameterName = GetResourceHandleFileName((ResourceHandle*) partialSkeleton.SkeletonParameterResourceHandle);
                if (skeletonParameterName != null && !output[skeletonName].ContainsKey(skeletonParameterName))
                {
                    output[skeletonName][skeletonParameterName] = new List<string>();
                }
            }
        }

        return new CharacterResourceMap(raceCode, output);
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