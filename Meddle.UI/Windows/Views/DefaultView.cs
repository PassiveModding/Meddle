using System.Numerics;
using System.Text;
using ImGuiNET;
using Meddle.Utils;
using Meddle.Utils.Files;
using Meddle.Utils.Files.SqPack;
using OtterTex;

namespace Meddle.UI.Windows.Views;

public class DefaultView : IView
{
    public DefaultView(IndexHashTableEntry hash, SqPackFile file)
    {
        this.hash = hash;
        this.file = file;
    }

    private readonly IndexHashTableEntry hash;
    private readonly SqPackFile file;

    public void Draw()
    {
        ImGui.Text($"Hash: {hash.Hash:X8}");
        var dataSize = file.RawData.Length;
        ImGui.Text($"Data Size: {dataSize}");
        const int sliceLength = 16;
        var sliceCount = dataSize / sliceLength;
        for (var i = 0; i < sliceCount; i++)
        {
            if (i > 100)
            {
                ImGui.Text("...");
                break;
            }
            var slice = file.RawData.Slice(i * sliceLength, sliceLength);
            var sliceStr = new StringBuilder();
            var textStr = new StringBuilder();
            for (var j = 0; j < slice.Length; j++)
            {
                var element = slice[j];
                sliceStr.Append(element.ToString("X2"));
                if (j < slice.Length - 1)
                {
                    sliceStr.Append(" ");
                }
                
                var c = (char)element;
                if (char.IsLetterOrDigit(c) || char.IsPunctuation(c))
                {
                    textStr.Append(c);
                }
                else if (char.IsWhiteSpace(c))
                {
                    textStr.Append(' ');
                }
                else
                {
                    textStr.Append('.');
                }
                textStr.Append(' ');
            }
            
            var offset = i * sliceLength;
            ImGui.TextUnformatted($"{offset:X8} | {sliceStr} | {textStr}");
        }
    }
}
