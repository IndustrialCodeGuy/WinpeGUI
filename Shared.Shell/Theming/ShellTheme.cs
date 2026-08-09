namespace Shared.Shell.Theming;

// =====================================================================
//  SHELL THEME: CENTRAL COLOR SOURCE OF TRUTH
// =====================================================================
//
// Purpose:
// - Shared single-theme palette for taskbar, Explorer chrome, and shared
//   controls.
// - Keeps taskbar buttons and Explorer toolbar buttons visually aligned.
// =====================================================================

public static class ShellTheme
{
    public static bool DarkMode { get; private set; }

    public static void SetDarkMode(bool enabled)
    {
        DarkMode = enabled;
    }

    public static void ConfigureFromArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i]?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(arg))
                continue;

            if (arg.Equals("--dark", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--dark-mode", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--darkmode", StringComparison.OrdinalIgnoreCase))
            {
                SetDarkMode(true);
                continue;
            }

            if (arg.Equals("--light", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--light-mode", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("--lightmode", StringComparison.OrdinalIgnoreCase))
            {
                SetDarkMode(false);
                continue;
            }

            if ((arg.Equals("--theme", StringComparison.OrdinalIgnoreCase) ||
                arg.Equals("/theme", StringComparison.OrdinalIgnoreCase)) &&
                i + 1 < args.Length)
            {
                string value = args[++i]?.Trim() ?? string.Empty;
                if (value.Equals("dark", StringComparison.OrdinalIgnoreCase))
                    SetDarkMode(true);
                else if (value.Equals("light", StringComparison.OrdinalIgnoreCase))
                    SetDarkMode(false);
            }
        }
    }

    public static string ThemeArgs => DarkMode ? " --dark" : string.Empty;

    // ---------------- Fixed palette colors ----------------
    private static readonly Color ButtonFocusedColor = Color.FromArgb(255, 170, 170, 170);
    private static readonly Color ButtonHoveredColor = Color.FromArgb(255, 189, 189, 189);
    private static readonly Color ButtonPressedColor = Color.FromArgb(255, 150, 150, 150);
    private static readonly Color ItemSelectedBackLightColor = Color.FromArgb(255, 204, 228, 247);
    private static readonly Color ItemHoverBackLightColor = Color.FromArgb(255, 224, 238, 249);
    private static readonly Color ItemSelectedBorderLightColor = Color.FromArgb(255, 0, 114, 203);
    private static readonly Color ItemSelectedBackDarkColor = Color.FromArgb(255, 152, 198, 230);   // #98C6E6
    private static readonly Color ItemHoverBackDarkColor = Color.FromArgb(255, 148, 184, 208);      // #94B8D0
    private static readonly Color ItemSelectedBorderDarkColor = Color.FromArgb(255, 0, 114, 203);   // #0072CB
    // ---------------- Shell surface colors ----------------
    public static Color WindowBack => DarkMode ? SystemColors.ControlDarkDark : SystemColors.ControlLight;
    public static Color TaskbarBack => DarkMode ? SystemColors.ControlDarkDark : SystemColors.ControlLight;
    public static Color TaskbarTopBorder => DarkMode ? Color.Black : SystemColors.ControlDark;

    // ---------------- Explorer button colors ----------------
    public static Color ButtonDefault => WindowBack;
    public static Color ButtonFocused => ButtonFocusedColor;
    public static Color ButtonHovered => ButtonHoveredColor;
    public static Color ButtonPressed => ButtonPressedColor;
    public static Color ButtonBorder => WindowBack;
    public static Color ButtonBorderHot => WindowBack;

    // ---------------- Taskbar button colors ----------------
    public static Color TaskbarButtonDefault => TaskbarBack;
    public static Color TaskbarButtonFocused => ButtonFocusedColor;
    public static Color TaskbarButtonHovered => ButtonHoveredColor;
    public static Color TaskbarButtonPressed => ButtonPressedColor;

    // ---------------- Text and content colors ----------------
    public static Color TextColor => SystemColors.WindowText;
    public static Color ContentBack => DarkMode ? SystemColors.ControlDark : SystemColors.ControlLightLight;

    // ---------------- List/tree item colors ----------------
    public static Color ContentBorder => DarkMode ? SystemColors.ControlDarkDark : SystemColors.ControlDark;

    // ---------------- Hovered/Selected item colors ----------------
    public static Color ItemSelectedBack => DarkMode ? ItemSelectedBackDarkColor : ItemSelectedBackLightColor;
    public static Color ItemHoverBack => DarkMode ? ItemHoverBackDarkColor : ItemHoverBackLightColor;
    public static Color ItemSelectedBorder => DarkMode ? ItemSelectedBorderDarkColor : ItemSelectedBorderLightColor;
    public static Color ItemSelectedText => SystemColors.WindowText;

    // Cut and hidden items use the same muted/ghosted treatment, matching Explorer.
    public static Color ItemCutText => ItemSelectedBorder;
    public static Color MutedText => TaskbarButtonPressed;
}
