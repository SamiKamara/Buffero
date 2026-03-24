using System.Runtime.InteropServices;
using SharpGen.Runtime;

namespace Buffero.App.Infrastructure;

internal static class HResultExtensions
{
    public static void ThrowIfFailed(this int hresult)
    {
        if (hresult < 0)
        {
            Marshal.ThrowExceptionForHR(hresult);
        }
    }

    public static void ThrowIfFailed(this Result result)
    {
        ThrowIfFailed(result.Code);
    }
}
