using System.Diagnostics;
using Meddle.Utils.Skeletons.Havok;
using Meddle.Utils.Skeletons.Havok.Models;

namespace Meddle.UI.Util;

public class SkeletonUtil
{
    public static string ParseHavokInput(byte[] data)
    {
        if (Program.Configuration.AssetCcResolve)
        {
            return ParseHavokInputCc(data);
        }
        else
        {
            return ParseHavokInputInterop(data);
        }
    }
    
    public static (string, HavokSkeleton) ProcessHavokInput(byte[] data)
    {
        if (Program.Configuration.AssetCcResolve)
        {
            var str = ParseHavokInputCc(data);
            var skeleton = HavokCCUtils.ParseHavokXml(str);
            return (str, skeleton);
        }
        else
        {
            var str = ParseHavokInputInterop(data);
            var skeleton = HavokUtils.ParseHavokXml(str);
            return (str, skeleton);
        }
    }
    
    public static string ParseHavokInputCc(byte[] data)
    {
        File.WriteAllBytes("./data/input.pap", data);
        var program = Process.Start("./data/NotAssetCc.exe", new[] {"./data/input.pap", "./data/output.pap"});
        program.WaitForExit();
        var parseResult = File.ReadAllText("./data/output.pap");
        return parseResult;
    }
    
    public static string ParseHavokInputInterop(byte[] data)
    {        
        var tempPath = Path.GetTempFileName();
        File.WriteAllBytes(tempPath, data);

        using var message = new HttpRequestMessage(HttpMethod.Post, $"http://localhost:{Program.Configuration.InteropPort}/parsesklb");
        using var content = new StringContent(tempPath);
        message.Content = content;
        using var client = new HttpClient();
        var response = client.SendAsync(message).GetAwaiter().GetResult();
        var result = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        return result;
    }
}
