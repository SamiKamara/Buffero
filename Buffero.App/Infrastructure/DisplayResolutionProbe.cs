using System.Windows.Forms;

namespace Buffero.App.Infrastructure;

internal static class DisplayResolutionProbe
{
    public static bool TryGetPrimaryScreenResolution(out int width, out int height)
    {
        var screen = Screen.PrimaryScreen;
        if (screen is null || screen.Bounds.Width < 2 || screen.Bounds.Height < 2)
        {
            width = 0;
            height = 0;
            return false;
        }

        width = screen.Bounds.Width;
        height = screen.Bounds.Height;
        return true;
    }
}
