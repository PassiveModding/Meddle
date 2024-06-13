using System.Text.RegularExpressions;

namespace Meddle.UI;

public static class PathUtil
{
    public static string Resolve(string mdlPath, string mtrlPath)
    {
        var mtrlPathRegex = new Regex(@"[a-z]\d{4}");
        var mtrlPathMatches = mtrlPathRegex.Matches(mtrlPath);
        if (mtrlPathMatches.Count != 2)
        {
            throw new Exception($"Invalid mdl path {mdlPath} -> {mtrlPath}");
        }

        if (mdlPath.StartsWith("chara/human/"))
        {
            var characterCode = mtrlPathMatches[0].Value;
            var subcategory = mtrlPathMatches[1].Value;
            
            var subCategoryName = subcategory[0] switch
            {
                'b' => "body",
                'f' => "face",
                'h' => "hair",
                't' => "tail",
                'z' => "zear",
                _ => throw new Exception($"Unknown subcategory {subcategory} for {mdlPath} -> {mtrlPath}")
            };
            //       chara/human/c0101/obj/hair/h0109/material/v0001/mt_c0101h0109_hir_a.mtrl
            return $"chara/human/{characterCode}/obj/{subCategoryName}/{subcategory}/material{mtrlPath}";
        }

        if (mdlPath.StartsWith("chara/weapon/"))
        {
            var weaponCode = mtrlPathMatches[0].Value;
            var subcategory = mtrlPathMatches[1].Value;
            
            var subCategoryName = subcategory[0] switch
            {
                'b' => "body",
                _ => throw new Exception($"Unknown subcategory {subcategory} for {mdlPath} -> {mtrlPath}")
            };

            return $"chara/weapon/{weaponCode}/obj/{subCategoryName}/{subcategory}/material{mtrlPath}";
        }

        if (mdlPath.StartsWith("chara/monster/"))
        {
            var monsterCode = mtrlPathMatches[0].Value;
            var subcategory = mtrlPathMatches[1].Value;
            
            var subCategoryName = subcategory[0] switch
            {
                'b' => "body",
                _ => throw new Exception($"Unknown subcategory {subcategory} for {mdlPath} -> {mtrlPath}")
            };
            
            return $"chara/monster/{monsterCode}/obj/{subCategoryName}/{subcategory}/material{mtrlPath}";
        }

        if (mdlPath.StartsWith("chara/equipment/"))
        {
            var characterCode = mtrlPathMatches[0].Value;
            var equipmentCode = mtrlPathMatches[1].Value;
            if (equipmentCode.StartsWith('e'))
            {
                return $"chara/equipment/{equipmentCode}/material{mtrlPath}";
            }

            var subCategoryName = equipmentCode[0] switch
            {
                'b' => "body",
                'f' => "face",
                'h' => "hair",
                't' => "tail",
                _ => throw new Exception($"Unknown subcategory {equipmentCode} for {mdlPath} -> {mtrlPath}")
            };
                
            return $"chara/human/{characterCode}/obj/{subCategoryName}/{equipmentCode}/material{mtrlPath}";
        }

        if (mdlPath.StartsWith("chara/demihuman/"))
        {
            var demiHumanCode = mtrlPathMatches[0].Value;
            var subcategory = mtrlPathMatches[1].Value;
            
            var subCategoryName = subcategory[0] switch
            {
                'e' => "equipment",
                _ => throw new Exception($"Unknown subcategory {subcategory} for {mdlPath} -> {mtrlPath}")
            };
            
            return $"chara/demihuman/{demiHumanCode}/obj/{subCategoryName}/{subcategory}/material{mtrlPath}";
        }
        
        throw new Exception($"Unsupported mdl path {mdlPath} -> {mtrlPath}");
    }
}
