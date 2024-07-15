using System.Diagnostics;

namespace Meddle.UI.Util;

public class SkeletonUtil
{
    public static string ParseHavokInput(byte[] data)
    {
        File.WriteAllBytes("./data/input.pap", data);
        var program = Process.Start("./data/NotAssetCc.exe", new[] {"./data/input.pap", "./data/output.pap"});
        program.WaitForExit();
        var parseResult = File.ReadAllText("./data/output.pap");
        return parseResult;
    }
}
