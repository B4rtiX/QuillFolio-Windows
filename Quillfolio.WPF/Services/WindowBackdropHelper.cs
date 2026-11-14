using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using Quillfolio.Core.Models;
using Quillfolio.Core.Services;

namespace Quillfolio.WPF.Services;

public static class WindowBackdropHelper
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR = 34;
    private const int DWMWA_CAPTION_COLOR = 35;
    private const int DWMWCP_ROUND = 2;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    public static void ApplyModernWindowChrome(Window window)
    {
        window.SourceInitialized += (_, _) => ApplyDwmToWindow(window);
    }

    public static bool IsEffectiveDarkMode(AppThemeMode mode) => mode switch
    {
        AppThemeMode.Dark => true,
        AppThemeMode.Oled => true,
        AppThemeMode.System => IsWindowsDarkThemeEnabled(),
        _ => false
    };

    public static bool IsWindowsDarkThemeEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            if (key?.GetValue("AppsUseLightTheme") is int val)
            {
                return val == 0;
            }
        }
        catch { }
        return false;
    }

    public static void ApplyAppTheme(AppThemeMode mode, string? deskThemeKey = null)
    {
        var effective = mode == AppThemeMode.System
            ? (IsWindowsDarkThemeEnabled() ? AppThemeMode.Dark : AppThemeMode.Light)
            : mode;

        if (effective == AppThemeMode.Oled)
        {
            SetBrush("AppBackgroundBrush", "#000000");
            SetBrush("DeskBackgroundBrush", "#000000");
            SetBrush("SidebarBackgroundBrush", "#070709");
            SetBrush("CardBackgroundBrush", "#0E0E12");
            SetBrush("GlassSurfaceBrush", "#EB0B0B0F");
            SetBrush("ControlSurfaceBrush", "#15151B");
            SetBrush("HoverSurfaceBrush", "#1F1F28");
            SetBrush("PressedSurfaceBrush", "#292936");
            SetBrush("PillTrackBrush", "#131318");
            SetBrush("BorderBrush", "#24242E");
            SetBrush("SubtleBorderBrush", "#343442");
            SetBrush("IslandBorderBrush", "#262632");
            SetBrush("PrimaryBrush", "#8B5CF6");
            SetBrush("PrimaryHoverBrush", "#7C3AED");
            SetBrush("PrimaryPressedBrush", "#6D28D9");
            SetBrush("TextPrimaryBrush", "#F8FAFC");
            SetBrush("TextSecondaryBrush", "#94A3B8");
            SetBrush("ToolbarForegroundBrush", "#CBD5E1");
            SetBrush("TableHeaderBrush", "#1B1B24");
            SetBrush("TableHeaderTextBrush", "#FFFFFF");
            SetBrush("ActiveItemBrush", "#241942");
            SetBrush("ActiveBorderBrush", "#7C3AED");
            SetBrush("SelectionTintBrush", "#22183E");
        }
        else if (effective == AppThemeMode.Dark)
        {
            string deskHex = deskThemeKey == "DeepStudio" ? "#0B0F19" : "#0F1523";
            SetBrush("AppBackgroundBrush", "#111827");
            SetBrush("DeskBackgroundBrush", deskHex);
            SetBrush("SidebarBackgroundBrush", "#151D2E");
            SetBrush("CardBackgroundBrush", "#1C2538");
            SetBrush("GlassSurfaceBrush", "#EB182133");
            SetBrush("ControlSurfaceBrush", "#242E45");
            SetBrush("HoverSurfaceBrush", "#2D3954");
            SetBrush("PressedSurfaceBrush", "#364566");
            SetBrush("PillTrackBrush", "#161E2E");
            SetBrush("BorderBrush", "#2D3952");
            SetBrush("SubtleBorderBrush", "#3D4C6B");
            SetBrush("IslandBorderBrush", "#323F5C");
            SetBrush("PrimaryBrush", "#8B5CF6");
            SetBrush("PrimaryHoverBrush", "#7C3AED");
            SetBrush("PrimaryPressedBrush", "#6D28D9");
            SetBrush("TextPrimaryBrush", "#F1F5F9");
            SetBrush("TextSecondaryBrush", "#94A3B8");
            SetBrush("ToolbarForegroundBrush", "#CBD5E1");
            SetBrush("TableHeaderBrush", "#1E293B");
            SetBrush("TableHeaderTextBrush", "#FFFFFF");
            SetBrush("ActiveItemBrush", "#2A204B");
            SetBrush("ActiveBorderBrush", "#8B5CF6");
            SetBrush("SelectionTintBrush", "#251E42");
        }
        else
        {
            string deskHex = (deskThemeKey ?? DataStore.Instance.DeskTheme) switch
            {
                "WarmSand" => "#F2ECE4",
                "SlateMist" => "#E2E8F0",
                "DeepStudio" => "#1B2230",
                _ => "#EAEEF5"
            };
            SetBrush("AppBackgroundBrush", "#F4F6FB");
            SetBrush("DeskBackgroundBrush", deskHex);
            SetBrush("SidebarBackgroundBrush", "#FAFBFE");
            SetBrush("CardBackgroundBrush", "#FFFFFF");
            SetBrush("GlassSurfaceBrush", "#F2FFFFFF");
            SetBrush("ControlSurfaceBrush", "#FFFFFF");
            SetBrush("HoverSurfaceBrush", "#F1F5F9");
            SetBrush("PressedSurfaceBrush", "#E2E8F0");
            SetBrush("PillTrackBrush", "#F1F5F9");
            SetBrush("BorderBrush", "#E2E8F0");
            SetBrush("SubtleBorderBrush", "#CBD5E1");
            SetBrush("IslandBorderBrush", "#D6DFEC");
            SetBrush("PrimaryBrush", "#7C3AED");
            SetBrush("PrimaryHoverBrush", "#6D28D9");
            SetBrush("PrimaryPressedBrush", "#5B21B6");
            SetBrush("TextPrimaryBrush", "#0F172A");
            SetBrush("TextSecondaryBrush", "#64748B");
            SetBrush("ToolbarForegroundBrush", "#475569");
            SetBrush("TableHeaderBrush", "#1E293B");
            SetBrush("TableHeaderTextBrush", "#FFFFFF");
            SetBrush("ActiveItemBrush", "#F5F3FF");
            SetBrush("ActiveBorderBrush", "#C4B5FD");
            SetBrush("SelectionTintBrush", "#F5F3FF");
        }

        if (Application.Current?.Windows != null)
        {
            foreach (Window w in Application.Current.Windows)
            {
                ApplyDwmToWindow(w, effective);
            }
        }
    }

    public static void ApplyDeskTheme(string? themeKey)
    {
        ApplyAppTheme(DataStore.Instance.AppTheme, themeKey);
    }

    private static void ApplyDwmToWindow(Window window, AppThemeMode? resolvedMode = null)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            var effective = resolvedMode ?? (DataStore.Instance.AppTheme == AppThemeMode.System
                ? (IsWindowsDarkThemeEnabled() ? AppThemeMode.Dark : AppThemeMode.Light)
                : DataStore.Instance.AppTheme);

            int cornerPref = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));

            int darkFlag = effective == AppThemeMode.Light ? 0 : 1;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkFlag, sizeof(int));

            int captionColor = effective switch
            {
                AppThemeMode.Oled => 0x00000000,   // #000000
                AppThemeMode.Dark => 0x0023150F,   // 0x00BBGGRR for #0F1523
                _ => 0x00FBF6F4                    // #F4F6FB
            };
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref captionColor, sizeof(int));

            int borderColor = effective switch
            {
                AppThemeMode.Oled => 0x002E2424,
                AppThemeMode.Dark => 0x0052392D,
                _ => 0x00F0E8E2
            };
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref borderColor, sizeof(int));
        }
        catch { }
    }

    private static void SetBrush(string key, string hex)
    {
        try
        {
            var color = (Color)ColorConverter.ConvertFromString(hex);
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            if (Application.Current != null)
            {
                Application.Current.Resources[key] = brush;
            }
        }
        catch { }
    }
}
