using System.Numerics;
using Dalamud.Interface.Utility;
namespace Meddle.Plugin.UI.Layout;

public partial class LayoutWindow
{
    public unsafe bool WorldToScreen(Matrix4x4 viewProjectionMatrix, Vector3 worldPos, out Vector2 screenPos)
    {
        var device = sigUtil.GetDevice();
        float width = device->Width;
        float height = device->Height;
        
        var windowPos = ImGuiHelpers.MainViewport.Pos;
        
        var pCoords = Vector4.Transform(new Vector4(worldPos, 1f), viewProjectionMatrix);
        var inFront = pCoords.W > 0.0f;
        if (Math.Abs(pCoords.W) < float.Epsilon) {
            screenPos = Vector2.Zero;
            return false;
        }

        pCoords *= MathF.Abs(1.0f / pCoords.W);
        screenPos = new Vector2(pCoords.X, pCoords.Y);
        
        screenPos.X = (0.5f * width * (screenPos.X + 1f)) + windowPos.X;
        screenPos.Y = (0.5f * height * (1f - screenPos.Y)) + windowPos.Y;
        
        var inView = inFront &&
                     screenPos.X > windowPos.X && screenPos.X < windowPos.X + width &&
                     screenPos.Y > windowPos.Y && screenPos.Y < windowPos.Y + height;

        return inView;
        
        // return IsOnScreen(new Vector3(pCoords.X, pCoords.Y, pCoords.Z));
        //
        // static bool IsOnScreen(Vector3 pos) {
        //     return -1.0 <= pos.X && pos.X <= 1.0 && -1.0 <= pos.Y && pos.Y <= 1.0 && pos.Z <= 1.0 && 0.0 <= pos.Z;
        // }
    }
    

    public unsafe bool WorldToScreen(Vector3 worldPos, out Vector2 screenPos, out bool inView)
    {
        var camera = sigUtil.GetCamera();
        Matrix4x4 viewProjectionMatrix = camera->ViewMatrix * camera->RenderCamera->ProjectionMatrix;
        inView = WorldToScreen(viewProjectionMatrix, worldPos, out screenPos);
        return inView;
    }
}
