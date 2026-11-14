using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Quillfolio.WPF.Services;

public static class TouchKeyboardHelper
{
    private static readonly Guid ClsidUiHostNoLaunch = new("4CE576FA-83DC-4F88-951C-9D0782B4E376");

    [ComImport]
    [Guid("37C994E7-432B-4834-A2F7-DCE1F13B834B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITipInvocation
    {
        void Toggle(IntPtr hwnd);
    }

    public static void ShowTouchKeyboard(VisualElementTarget element)
    {
        try
        {
            var hwndSource = PresentationSource.FromVisual(element.Visual) as HwndSource;
            IntPtr hwnd = hwndSource?.Handle ?? IntPtr.Zero;
            if (hwnd == IntPtr.Zero) return;

            var type = Type.GetTypeFromCLSID(ClsidUiHostNoLaunch, throwOnError: false);
            if (type != null)
            {
                var tip = Activator.CreateInstance(type) as ITipInvocation;
                if (tip != null)
                {
                    try
                    {
                        tip.Toggle(hwnd);
                        return;
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(tip);
                    }
                }
            }
        }
        catch
        {
            // Fallback below
        }

        try
        {
            string commonFiles = Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles);
            string tabTip = Path.Combine(commonFiles, "microsoft shared", "ink", "TabTip.exe");
            if (File.Exists(tabTip))
            {
                Process.Start(new ProcessStartInfo(tabTip) { UseShellExecute = true });
            }
        }
        catch { }
    }

    public readonly record struct VisualElementTarget(System.Windows.Media.Visual Visual);
}
