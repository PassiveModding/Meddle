using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.Interop;
using Meddle.Plugin.Models.Layout;
using Meddle.Plugin.Utils;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using Meddle.Utils.Helpers;
using Microsoft.Extensions.Logging;

namespace Meddle.Plugin.Services;

public class ResolverService : IService
{
    private readonly ILogger<ResolverService> logger;
    private readonly LayoutService layoutService;
    private readonly SqPack pack;
    private readonly IFramework framework;
    private readonly PbdHooks pbdHooks;

    public ResolverService(
        ILogger<ResolverService> logger, 
        LayoutService layoutService,
        SqPack pack,
        IFramework framework,
        PbdHooks pbdHooks)
    {
        this.logger = logger;
        this.layoutService = layoutService;
        this.pack = pack;
        this.framework = framework;
        this.pbdHooks = pbdHooks;
    }
    
    
    public void ResolveInstances(params ParsedInstance[] instances)
    {
        framework.RunOnTick(() =>
        {
            foreach (var instance in instances)
            {
                ResolveInstance(instance);
            }
        }).GetAwaiter().GetResult();
    }
    
    public static bool IsCharacterKind(ObjectKind kind)
    {
        return kind switch
        {
            ObjectKind.Pc => true,
            ObjectKind.Mount => true,
            ObjectKind.Companion => true,
            ObjectKind.Retainer => true,
            ObjectKind.BattleNpc => true,
            ObjectKind.EventNpc => true,
            ObjectKind.Ornament => true,
            _ => false
        };
    }

    private unsafe void ResolveParsedCharacterInstance(ParsedCharacterInstance characterInstance)
    {
        var objects = layoutService.ParseObjects();
        // check to ensure the character instance is still valid
        if (objects.Any(o => o.Id == characterInstance.Id))
        {
            if (characterInstance.IdType == ParsedCharacterInstance.ParsedCharacterInstanceIdType.CharacterBase)
            {
                var cBase = (CharacterBase*)characterInstance.Id;
                var characterInfo = ParseMaterialUtil.ParseDrawObject(&cBase->DrawObject, pbdHooks);
                characterInstance.CharacterInfo = characterInfo;
            }
            else
            {
                var gameObject = (GameObject*)characterInstance.Id;
                if (IsCharacterKind(gameObject->ObjectKind))
                {
                    var characterInfo = ParseCharacter((Character*)gameObject);
                    characterInstance.CharacterInfo = characterInfo;
                }
                else
                {
                    var characterInfo = ParseMaterialUtil.ParseDrawObject(gameObject->DrawObject, pbdHooks);
                    characterInstance.CharacterInfo = characterInfo;
                }
            }
        }
        else
        {
            logger.LogWarning("Character instance {Id} no longer exists", characterInstance.Id);
        }
    }
    
    private void ResolveParsedTerrainInstance(ParsedTerrainInstance terrainInstance)
    {
        var path = terrainInstance.Path;
        var teraPath = $"{terrainInstance.Path.GamePath}/bgplate/terrain.tera";
        var teraResource = pack.GetFile(teraPath);
        
        if (teraResource == null)
        {
            logger.LogWarning("Failed to load terrain.tera for {Path}", path);
            return;
        }
        
        var terrain = new TeraFile(teraResource.Value.file.RawData);
        terrainInstance.Data = new ParsedTerrainInstanceData(terrain);
    }
    
    private void ResolveInstance(ParsedInstance instance)
    {
        if (instance is ParsedCharacterInstance {IsResolved: false} characterInstance)
        {
            ResolveParsedCharacterInstance(characterInstance);
        }

        if (instance is ParsedTerrainInstance {IsResolved: false} terrainInstance)
        {
            ResolveParsedTerrainInstance(terrainInstance);
        }

        if (instance is ParsedSharedInstance sharedInstance)
        {
            foreach (var child in sharedInstance.Children)
            {
                ResolveInstance(child);
            }
        }
    }
    
    /// <summary>
    /// Used for terrain
    /// </summary>
    public ParsedModelInfo? ParseModelFromPath(string path)
    {
        var modelResource = pack.GetFile(path);
        if (modelResource == null)
        {
            logger.LogWarning("Failed to load model file: {Path}", path);
            return null;
        }

        var modelData = modelResource.Value.file.RawData;
        var mdlFile = new MdlFile(modelData);
        var materials = new List<ParsedMaterialInfo>();
        var mtrlNames = mdlFile.GetMaterialNames().Select(x => x.Value)
                               .ToArray();
        foreach (var mtrlName in mtrlNames)
        {
            if (mtrlName.StartsWith('/')) throw new InvalidOperationException("Cannot resolve relative paths");
            
            var mtrlResource = pack.GetFile(mtrlName);
            if (mtrlResource == null)
            {
                logger.LogWarning("Failed to load material file: {Path}", mtrlName);
                continue;
            }
            
            var mtrlData = mtrlResource.Value.file.RawData;
            var mtrlFile = new MtrlFile(mtrlData);
            var shaderName = mtrlFile.GetShaderPackageName();
            var colorTable = mtrlFile.GetColorTable();
            
            var textures = new List<ParsedTextureInfo>();
            var textureNames = mtrlFile.GetTexturePaths().Select(x => x.Value)
                                       .ToArray();
            for (var texIdx = 0; texIdx < textureNames.Length; texIdx++)
            {
                var texName = textureNames[texIdx];
                var texResource = pack.GetFile(texName);
                if (texResource == null)
                {
                    logger.LogWarning("Failed to load texture file: {Path}", texName);
                    continue;
                }
                
                var texData = texResource.Value.file.RawData;
                var texFile = new TexFile(texData);
                var texRes = texFile.ToResource();
                var texInfo = new ParsedTextureInfo(texName, texName, texRes);
                textures.Add(texInfo);
            }

            var materialInfo = new ParsedMaterialInfo(mtrlName, mtrlName, shaderName, null, colorTable, textures.ToArray());
            
            materials.Add(materialInfo);
        }

        var modelInfo = new ParsedModelInfo(path, path, null, null, materials.ToArray(), null, null);
        return modelInfo;
    }

    public ParsedCharacterInfo? ParseDrawObject(Pointer<DrawObject> drawObject)
    {
        return ParseMaterialUtil.ParseDrawObject(drawObject, pbdHooks);
    }
    
    public unsafe ParsedCharacterInfo? ParseCharacter(Character* character)
    {
        if (character == null)
        {
            return null;
        }
        
        var drawObject = character->DrawObject;
        if (drawObject == null)
        {
            return null;
        }
        
        var characterInfo = ParseMaterialUtil.ParseDrawObject(drawObject, pbdHooks);
        if (characterInfo == null)
        {
            return null;
        }
        
        var attaches = new List<ParsedCharacterInfo>();
        var mountInfo = ParseCharacter(character->Mount.MountObject);
        if (mountInfo != null)
        {
            attaches.Add(mountInfo);
        }
        
        var ornamentInfo = ParseCharacter((Character*)character->OrnamentData.OrnamentObject);
        if (ornamentInfo != null)
        {
            attaches.Add(ornamentInfo);
        }

        foreach (var weapon in character->DrawData.WeaponData)
        {
            var weaponInfo = ParseMaterialUtil.ParseDrawObject(weapon.DrawObject, pbdHooks);
            if (weaponInfo != null)
            {
                attaches.Add(weaponInfo);
            }
        }

        characterInfo.Attaches = attaches.ToArray();
        
        return characterInfo;
    }
}
