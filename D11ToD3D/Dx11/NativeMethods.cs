using System.Runtime.InteropServices;
using System;

namespace D11ToD3D.Dx11;

public static class NativeMethods
{
    [DllImport("user32.dll", SetLastError = false)]
    public static extern IntPtr GetDesktopWindow();
}