using System.Numerics;
using Dalamud.Interface.Utility;

namespace Meddle.Plugin.UI.Layout;

public partial class LayoutWindow
{
        private unsafe bool WorldToScreenFallback(Vector3 worldPos, out Vector2 screenPos, out bool inView)
    {
        var currentCamera = sigUtil.GetCamera();

        if (!currentCamera->WorldToScreen(worldPos, out var sPos))
        {
            screenPos = Vector2.Zero;
            inView = false;
            return false;
        }

        screenPos = sPos;
        inView = true;
        return true;
    }

    public unsafe bool WorldToScreen(Vector3 worldPos, out Vector2 screenPos, out bool inView)
    {
        var device = sigUtil.GetDevice();
        var control = sigUtil.GetControl();

        // Read current ViewProjectionMatrix plus game window size
        var windowPos = ImGuiHelpers.MainViewport.Pos;

        Matrix4x4 viewProjectionMatrix;
        if (control->LocalPlayer != null)
        {
            viewProjectionMatrix = control->ViewProjectionMatrix;
        }
        else
        {
            var fallbackResult = WorldToScreenFallback(worldPos, out screenPos, out inView);
            if (!fallbackResult)
            {
                screenPos = Vector2.Zero;
                inView = false;
                return false;
            }

            return true;
        }

        float width = device->Width;
        float height = device->Height;

        var pCoords = Vector4.Transform(new Vector4(worldPos, 1.0f), viewProjectionMatrix);
        var inFront = pCoords.W > 0.0f;

        if (Math.Abs(pCoords.W) < float.Epsilon)
        {
            screenPos = Vector2.Zero;
            inView = false;
            return false;
        }

        pCoords *= MathF.Abs(1.0f / pCoords.W);
        screenPos = new Vector2(pCoords.X, pCoords.Y);

        screenPos.X = (0.5f * width * (screenPos.X + 1f)) + windowPos.X;
        screenPos.Y = (0.5f * height * (1f - screenPos.Y)) + windowPos.Y;

        inView = inFront &&
                 screenPos.X > windowPos.X && screenPos.X < windowPos.X + width &&
                 screenPos.Y > windowPos.Y && screenPos.Y < windowPos.Y + height;

        return inFront;
    }
}
