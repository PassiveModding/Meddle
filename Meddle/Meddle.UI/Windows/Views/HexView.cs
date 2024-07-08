using System.Text;
using ImGuiNET;

namespace Meddle.UI.Windows.Views;

public class HexView(ReadOnlySpan<byte> data)
{
    private readonly byte[] data = data.ToArray();

    private int offset;
    private int linesToDraw = 100;
    private int sliceLength = 16;
    private int shift = 0;
    public void DrawHexDump()
    {
        var span = this.data.AsSpan();
        ImGui.SliderInt("Slice Length", ref sliceLength, 8, 32);
        var sliceCount = span.Length / sliceLength;
        var maxOffset = Math.Max(sliceCount - linesToDraw, 0);
        ImGui.SliderInt("Offset", ref offset, 0, maxOffset); 
        // +/- for offset since theres a lot of lines
        ImGui.SameLine();
        if (ImGui.ArrowButton("##left", ImGuiDir.Left))
        {
            offset = Math.Max(offset - 1, 0);
        }
        ImGui.SameLine();
        if (ImGui.ArrowButton("##right", ImGuiDir.Right))
        {
            offset = Math.Min(offset + 1, maxOffset);
        }
        // lines to draw slider
        ImGui.SliderInt("Lines to Draw", ref linesToDraw, 100, 1000);
        ImGui.SliderInt("Shift", ref shift, 0, sliceLength - 1);
        
        for (var i = offset; i < offset + linesToDraw; i++)
        {
            if (i >= sliceCount)
            {
                break;
            }
            //var slice = span.Slice(i * sliceLength, sliceLength);
            var slice = span.Slice(i * sliceLength + shift, sliceLength);
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
            
            var sliceOffset = i * sliceLength;
            ImGui.TextUnformatted($"{sliceOffset:X8} | {sliceStr} | {textStr}");
            
        }
    }
}
