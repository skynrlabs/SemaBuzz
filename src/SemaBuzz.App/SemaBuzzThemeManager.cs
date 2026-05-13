using System.Windows;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;

namespace SemaBuzz.App;

/// <summary>
/// Manages runtime theme switching.  Replaces brush resources in
/// <see cref="Application.Current.Resources"/> so every control that uses
/// <c>{DynamicResource ...}</c> automatically picks up the new palette, then
/// updates the DWM chrome colour on every open window.
/// </summary>
internal static class SemaBuzzThemeManager
{
    //  Theme palette definition

    private record struct ThemeDef(
        Color Background,
        Color Surface,
        Color Border,
        Color Accent,
        Color AccentDim,
        Color AccentGlow,
        Color Dead,
        Color Header,
        Func<Brush> WindowBackground,   // factory  creates a fresh, unfrozen brush
        int DwmCaption,                 // COLORREF 0x00BBGGRR
        int DwmText,
        int DwmBorder)
    {
        /// <summary>
        /// Optional override for text drawn ON the header (navy bar).
        /// When left at default (transparent) the normal <see cref="Accent"/> colour is used.
        /// </summary>
        public Color HeaderText { get; init; }
    }

    //  Palettes

    // Helper shorthands
    private static Color C(byte r, byte g, byte b) => Color.FromRgb(r, g, b);
    private static Color A(byte a, byte r, byte g, byte b) => Color.FromArgb(a, r, g, b);

    private static readonly Dictionary<SemaBuzzThemeId, ThemeDef> Palettes = new()
    {
        //  Obsidian Amber (default)
        [SemaBuzzThemeId.Obsidian] = new ThemeDef(
            Background: C(0x12, 0x12, 0x12),
            Surface: C(0x1E, 0x1E, 0x1E),
            Border: C(0x2A, 0x2A, 0x2A),
            Accent: C(0xFF, 0xB3, 0x00),      // amber
            AccentDim: A(0x80, 0xFF, 0xB3, 0x00),
            AccentGlow: A(0x33, 0xFF, 0xB3, 0x00),
            Dead: C(0x44, 0x44, 0x44),
            Header: C(0x0D, 0x0D, 0x0D),
            WindowBackground: () => new SolidColorBrush(C(0x12, 0x12, 0x12)),
            DwmCaption: 0x00121212,
            DwmText: 0x0000B3FF,   // #FFB300  COLORREF (0x00BBGGRR)
            DwmBorder: 0x002A2A2A),

        //  Neon Pink / Purple gradient
        [SemaBuzzThemeId.Neon] = new ThemeDef(
            Background: C(0x0D, 0x0B, 0x14),
            Surface: C(0x1A, 0x10, 0x28),
            Border: C(0x3D, 0x2B, 0x5E),
            Accent: C(0xE0, 0x40, 0xFB),      // neon magenta-pink
            AccentDim: A(0x80, 0xE0, 0x40, 0xFB),
            AccentGlow: A(0x33, 0xE0, 0x40, 0xFB),
            Dead: C(0x4A, 0x30, 0x60),
            Header: C(0x08, 0x06, 0x0F),
            WindowBackground: () => new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(C(0x0D, 0x0B, 0x14), 0.0),
                    new GradientStop(C(0x18, 0x0A, 0x2E), 0.5),
                    new GradientStop(C(0x0D, 0x0B, 0x14), 1.0),
                },
                new Point(0, 0), new Point(1, 1)),
            DwmCaption: 0x00140B0D,   // #0D0B14  COLORREF
            DwmText: 0x00FB40E0,   // #E040FB  COLORREF
            DwmBorder: 0x005E2B3D),  // #3D2B5E  COLORREF

        //  Matrix CRT Green / Black
        [SemaBuzzThemeId.Matrix] = new ThemeDef(
            Background: C(0x00, 0x00, 0x00),
            Surface: C(0x00, 0x0D, 0x00),
            Border: C(0x00, 0x28, 0x00),
            Accent: C(0x00, 0xFF, 0x41),      // phosphor green
            AccentDim: A(0x80, 0x00, 0xFF, 0x41),
            AccentGlow: A(0x33, 0x00, 0xFF, 0x41),
            Dead: C(0x15, 0x40, 0x15),
            Header: C(0x00, 0x00, 0x00),
            WindowBackground: () => new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(C(0x00, 0x00, 0x00), 0.0),
                    new GradientStop(C(0x00, 0x07, 0x00), 0.5),
                    new GradientStop(C(0x00, 0x00, 0x00), 1.0),
                },
                new Point(0, 0), new Point(0, 1)),
            DwmCaption: 0x00000000,
            DwmText: 0x0041FF00,   // #00FF41  COLORREF
            DwmBorder: 0x00002800),  // #002800  COLORREF

        //  Blood Moon (deep crimson)
        [SemaBuzzThemeId.BloodMoon] = new ThemeDef(
            Background: C(0x0F, 0x00, 0x00),
            Surface: C(0x1C, 0x00, 0x00),
            Border: C(0x3A, 0x05, 0x05),
            Accent: C(0xFF, 0x17, 0x44),      // vivid red
            AccentDim: A(0x80, 0xFF, 0x17, 0x44),
            AccentGlow: A(0x33, 0xFF, 0x17, 0x44),
            Dead: C(0x5A, 0x10, 0x10),
            Header: C(0x0A, 0x00, 0x00),
            WindowBackground: () => new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(C(0x0F, 0x00, 0x00), 0.0),
                    new GradientStop(C(0x1A, 0x00, 0x00), 0.5),
                    new GradientStop(C(0x0F, 0x00, 0x00), 1.0),
                },
                new Point(0, 0), new Point(0, 1)),
            DwmCaption: 0x0000000F,   // #0F0000  COLORREF
            DwmText: 0x004417FF,   // #FF1744  COLORREF
            DwmBorder: 0x0005053A),  // #3A0505  COLORREF

        //  Arctic (icy cyan)
        [SemaBuzzThemeId.Arctic] = new ThemeDef(
            Background: C(0x06, 0x0B, 0x14),
            Surface: C(0x0D, 0x16, 0x26),
            Border: C(0x1E, 0x3A, 0x5A),
            Accent: C(0x00, 0xE5, 0xFF),      // icy cyan
            AccentDim: A(0x80, 0x00, 0xE5, 0xFF),
            AccentGlow: A(0x33, 0x00, 0xE5, 0xFF),
            Dead: C(0x1A, 0x3A, 0x50),
            Header: C(0x04, 0x08, 0x10),
            WindowBackground: () => new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(C(0x06, 0x0B, 0x14), 0.0),
                    new GradientStop(C(0x0A, 0x14, 0x22), 0.5),
                    new GradientStop(C(0x06, 0x0B, 0x14), 1.0),
                },
                new Point(0, 0), new Point(0, 1)),
            DwmCaption: 0x00140B06,   // #060B14  COLORREF
            DwmText: 0x00FFE500,   // #00E5FF  COLORREF
            DwmBorder: 0x005A3A1E),  // #1E3A5A  COLORREF

        //  Sepia (old gold / warm dark)
        [SemaBuzzThemeId.Sepia] = new ThemeDef(
            Background: C(0x1A, 0x12, 0x00),
            Surface: C(0x26, 0x1A, 0x00),
            Border: C(0x3D, 0x2E, 0x00),
            Accent: C(0xD4, 0xA0, 0x17),      // old gold
            AccentDim: A(0x80, 0xD4, 0xA0, 0x17),
            AccentGlow: A(0x33, 0xD4, 0xA0, 0x17),
            Dead: C(0x4A, 0x3A, 0x10),
            Header: C(0x12, 0x0D, 0x00),
            WindowBackground: () => new SolidColorBrush(C(0x1A, 0x12, 0x00)),
            DwmCaption: 0x0000121A,   // #1A1200  COLORREF
            DwmText: 0x0017A0D4,   // #D4A017  COLORREF
            DwmBorder: 0x00002E3D),  // #3D2E00  COLORREF

        //  Midnight (electric blue)
        [SemaBuzzThemeId.Midnight] = new ThemeDef(
            Background: C(0x00, 0x08, 0x14),
            Surface: C(0x00, 0x10, 0x2A),
            Border: C(0x00, 0x1E, 0x4A),
            Accent: C(0x29, 0x79, 0xFF),      // electric blue
            AccentDim: A(0x80, 0x29, 0x79, 0xFF),
            AccentGlow: A(0x33, 0x29, 0x79, 0xFF),
            Dead: C(0x0A, 0x1A, 0x40),
            Header: C(0x00, 0x05, 0x0E),
            WindowBackground: () => new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(C(0x00, 0x08, 0x14), 0.0),
                    new GradientStop(C(0x00, 0x10, 0x20), 0.5),
                    new GradientStop(C(0x00, 0x08, 0x14), 1.0),
                },
                new Point(0, 0), new Point(0, 1)),
            DwmCaption: 0x00140800,   // #000814  COLORREF
            DwmText: 0x00FF7929,   // #2979FF  COLORREF
            DwmBorder: 0x004A1E00),  // #001E4A  COLORREF

        //  Sunset (deep orange)
        [SemaBuzzThemeId.Sunset] = new ThemeDef(
            Background: C(0x14, 0x08, 0x00),
            Surface: C(0x1F, 0x0E, 0x00),
            Border: C(0x3D, 0x20, 0x00),
            Accent: C(0xFF, 0x6D, 0x00),      // deep orange
            AccentDim: A(0x80, 0xFF, 0x6D, 0x00),
            AccentGlow: A(0x33, 0xFF, 0x6D, 0x00),
            Dead: C(0x5A, 0x2E, 0x00),
            Header: C(0x0E, 0x05, 0x00),
            WindowBackground: () => new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(C(0x14, 0x08, 0x00), 0.0),
                    new GradientStop(C(0x1A, 0x0A, 0x00), 0.5),
                    new GradientStop(C(0x14, 0x08, 0x00), 1.0),
                },
                new Point(0, 0), new Point(0, 1)),
            DwmCaption: 0x00000814,   // #140800  COLORREF
            DwmText: 0x00006DFF,   // #FF6D00  COLORREF
            DwmBorder: 0x0000203D),  // #3D2000  COLORREF

        //  Rose (hot pink / deep rose)
        [SemaBuzzThemeId.Rose] = new ThemeDef(
            Background: C(0x14, 0x00, 0x10),
            Surface: C(0x20, 0x00, 0x18),
            Border: C(0x42, 0x00, 0x30),
            Accent: C(0xF5, 0x00, 0x57),      // deep rose
            AccentDim: A(0x80, 0xF5, 0x00, 0x57),
            AccentGlow: A(0x33, 0xF5, 0x00, 0x57),
            Dead: C(0x5A, 0x00, 0x40),
            Header: C(0x0E, 0x00, 0x0C),
            WindowBackground: () => new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(C(0x14, 0x00, 0x10), 0.0),
                    new GradientStop(C(0x1C, 0x00, 0x16), 0.5),
                    new GradientStop(C(0x14, 0x00, 0x10), 1.0),
                },
                new Point(0, 0), new Point(0, 1)),
            DwmCaption: 0x00100014,   // #140010  COLORREF
            DwmText: 0x005700F5,   // #F50057  COLORREF
            DwmBorder: 0x00300042),  // #420030  COLORREF

        //  Violet (electric purple)
        [SemaBuzzThemeId.Violet] = new ThemeDef(
            Background: C(0x0A, 0x00, 0x14),
            Surface: C(0x12, 0x00, 0x20),
            Border: C(0x2A, 0x00, 0x50),
            Accent: C(0xAA, 0x00, 0xFF),      // electric violet
            AccentDim: A(0x80, 0xAA, 0x00, 0xFF),
            AccentGlow: A(0x33, 0xAA, 0x00, 0xFF),
            Dead: C(0x3A, 0x00, 0x60),
            Header: C(0x06, 0x00, 0x10),
            WindowBackground: () => new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(C(0x0A, 0x00, 0x14), 0.0),
                    new GradientStop(C(0x12, 0x00, 0x22), 0.5),
                    new GradientStop(C(0x0A, 0x00, 0x14), 1.0),
                },
                new Point(0, 0), new Point(0, 1)),
            DwmCaption: 0x0014000A,   // #0A0014  COLORREF
            DwmText: 0x00FF00AA,   // #AA00FF  COLORREF
            DwmBorder: 0x0050002A),  // #2A0050  COLORREF

        //  Emerald (rich green)
        [SemaBuzzThemeId.Emerald] = new ThemeDef(
            Background: C(0x00, 0x12, 0x09),
            Surface: C(0x00, 0x1A, 0x0E),
            Border: C(0x00, 0x3D, 0x22),
            Accent: C(0x00, 0xC8, 0x53),      // rich emerald
            AccentDim: A(0x80, 0x00, 0xC8, 0x53),
            AccentGlow: A(0x33, 0x00, 0xC8, 0x53),
            Dead: C(0x0A, 0x40, 0x20),
            Header: C(0x00, 0x0D, 0x06),
            WindowBackground: () => new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(C(0x00, 0x12, 0x09), 0.0),
                    new GradientStop(C(0x00, 0x18, 0x0C), 0.5),
                    new GradientStop(C(0x00, 0x12, 0x09), 1.0),
                },
                new Point(0, 0), new Point(0, 1)),
            DwmCaption: 0x00091200,   // #001209  COLORREF
            DwmText: 0x0053C800,   // #00C853  COLORREF
            DwmBorder: 0x00223D00),  // #003D22  COLORREF

        //  Steel (blue-grey / industrial)
        [SemaBuzzThemeId.Steel] = new ThemeDef(
            Background: C(0x0A, 0x0D, 0x10),
            Surface: C(0x11, 0x16, 0x20),
            Border: C(0x20, 0x2A, 0x35),
            Accent: C(0x78, 0x90, 0x9C),      // blue-grey steel
            AccentDim: A(0x80, 0x78, 0x90, 0x9C),
            AccentGlow: A(0x33, 0x78, 0x90, 0x9C),
            Dead: C(0x2A, 0x35, 0x40),
            Header: C(0x06, 0x08, 0x0A),
            WindowBackground: () => new SolidColorBrush(C(0x0A, 0x0D, 0x10)),
            DwmCaption: 0x00100D0A,   // #0A0D10  COLORREF
            DwmText: 0x009C9078,   // #78909C  COLORREF
            DwmBorder: 0x00352A20),  // #202A35  COLORREF

        //  Chrome (dark grey · Google blue)
        [SemaBuzzThemeId.Chrome] = new ThemeDef(
            Background: C(0x20, 0x21, 0x24),
            Surface: C(0x29, 0x2A, 0x2D),
            Border: C(0x3C, 0x40, 0x43),
            Accent: C(0x8A, 0xB4, 0xF8),      // Chrome blue
            AccentDim: A(0x80, 0x8A, 0xB4, 0xF8),
            AccentGlow: A(0x33, 0x8A, 0xB4, 0xF8),
            Dead: C(0x5F, 0x63, 0x68),
            Header: C(0x1A, 0x1B, 0x1E),
            WindowBackground: () => new SolidColorBrush(C(0x20, 0x21, 0x24)),
            DwmCaption: 0x001E1B1A,   // #1A1B1E  COLORREF
            DwmText: 0x00F8B48A,   // #8AB4F8  COLORREF
            DwmBorder: 0x0043403C),  // #3C4043  COLORREF

        //  Muted Terminal (dark teal · ICQ green)
        [SemaBuzzThemeId.MutedTerminal] = new ThemeDef(
            Background: C(0x1A, 0x2B, 0x2B),      // dark teal-grey
            Surface: C(0x22, 0x38, 0x38),      // slightly lighter teal-grey
            Border: C(0x3A, 0x56, 0x56),       // muted teal border
            Accent: C(0x8D, 0xC2, 0x00),      // ICQ yellow-green
            AccentDim: A(0x80, 0x8D, 0xC2, 0x00),
            AccentGlow: A(0x33, 0x8D, 0xC2, 0x00),
            Dead: C(0x3A, 0x56, 0x56),
            Header: C(0x0F, 0x1A, 0x1A),      // very dark teal header
            WindowBackground: () => new SolidColorBrush(C(0x1A, 0x2B, 0x2B)),
            DwmCaption: 0x001A1A0F,   // #0F1A1A  COLORREF
            DwmText: 0x0000C28D,   // #8DC200  COLORREF (ICQ yellow-green)
            DwmBorder: 0x0056563A),  // #3A5656  COLORREF

        //  Daylight (cool grey-blue · deep ocean blue)  — free tier
        [SemaBuzzThemeId.Daylight] = new ThemeDef(
            Background: C(0xE8, 0xED, 0xF2),      // light cool grey-blue
            Surface: C(0xD5, 0xDC, 0xE5),      // slightly darker panel
            Border: C(0xAA, 0xB8, 0xC5),      // clear but soft border
            Accent: C(0x1B, 0x6C, 0xA8),      // deep ocean blue
            AccentDim: A(0x80, 0x1B, 0x6C, 0xA8),
            AccentGlow: A(0x33, 0x1B, 0x6C, 0xA8),
            Dead: C(0x8F, 0xA0, 0xAF),      // muted slate
            Header: C(0xC5, 0xD0, 0xDA),      // soft blue-grey header
            WindowBackground: () => new SolidColorBrush(C(0xE8, 0xED, 0xF2)),
            DwmCaption: 0x00DAD0C5,   // #C5D0DA  COLORREF
            DwmText: 0x00A86C1B,   // #1B6CA8  COLORREF
            DwmBorder: 0x00C5B8AA),  // #AAB8C5  COLORREF

        //  Parchment (warm cream · deep rust)  — PRO
        [SemaBuzzThemeId.Parchment] = new ThemeDef(
            Background: C(0xED, 0xE5, 0xD8),      // warm parchment cream
            Surface: C(0xE0, 0xD5, 0xC4),      // slightly deeper warm panel
            Border: C(0xBF, 0xAA, 0x8A),      // warm tan border
            Accent: C(0x7A, 0x3B, 0x1E),      // deep rust / terracotta
            AccentDim: A(0x80, 0x7A, 0x3B, 0x1E),
            AccentGlow: A(0x33, 0x7A, 0x3B, 0x1E),
            Dead: C(0xA8, 0x90, 0x70),      // muted warm grey
            Header: C(0xD5, 0xC8, 0xB0),      // warm sandy header
            WindowBackground: () => new SolidColorBrush(C(0xED, 0xE5, 0xD8)),
            DwmCaption: 0x00B0C8D5,   // #D5C8B0  COLORREF
            DwmText: 0x001E3B7A,   // #7A3B1E  COLORREF
            DwmBorder: 0x008AAABF),  // #BFAA8A  COLORREF

        //  Sage (soft sage-green · deep forest)  — PRO
        [SemaBuzzThemeId.Sage] = new ThemeDef(
            Background: C(0xDF, 0xEB, 0xDF),      // soft sage green
            Surface: C(0xCE, 0xDE, 0xCE),      // slightly deeper sage panel
            Border: C(0xA3, 0xC2, 0xA3),      // muted leaf border
            Accent: C(0x1B, 0x5E, 0x37),      // deep forest green
            AccentDim: A(0x80, 0x1B, 0x5E, 0x37),
            AccentGlow: A(0x33, 0x1B, 0x5E, 0x37),
            Dead: C(0x7D, 0xA8, 0x7D),      // muted mid-green
            Header: C(0xC4, 0xD8, 0xC4),      // pale sage header
            WindowBackground: () => new SolidColorBrush(C(0xDF, 0xEB, 0xDF)),
            DwmCaption: 0x00D8D8C4,   // #C4D8C4  COLORREF
            DwmText: 0x00375E1B,   // #1B5E37  COLORREF
            DwmBorder: 0x00A3C2A3),  // #A3C2A3  COLORREF

        //  Lavender (soft violet · deep indigo)  — PRO
        [SemaBuzzThemeId.Lavender] = new ThemeDef(
            Background: C(0xEA, 0xE5, 0xF2),      // soft lavender
            Surface: C(0xDB, 0xD3, 0xE8),      // slightly deeper lavender panel
            Border: C(0xB8, 0xA8, 0xD5),      // muted violet border
            Accent: C(0x44, 0x27, 0x99),      // deep indigo
            AccentDim: A(0x80, 0x44, 0x27, 0x99),
            AccentGlow: A(0x33, 0x44, 0x27, 0x99),
            Dead: C(0x96, 0x88, 0xBB),      // muted slate-violet
            Header: C(0xD0, 0xC7, 0xE4),      // pale lavender header
            WindowBackground: () => new SolidColorBrush(C(0xEA, 0xE5, 0xF2)),
            DwmCaption: 0x00E4C7D0,   // #D0C7E4  COLORREF
            DwmText: 0x00992744,   // #442799  COLORREF
            DwmBorder: 0x00D5A8B8),  // #B8A8D5  COLORREF

        //  Sand (warm tan · deep teal)  — PRO
        [SemaBuzzThemeId.Sand] = new ThemeDef(
            Background: C(0xEB, 0xE0, 0xCB),      // warm sandy tan
            Surface: C(0xDE, 0xD0, 0xB5),      // slightly deeper sand panel
            Border: C(0xC5, 0xAF, 0x8A),      // warm sand border
            Accent: C(0x2C, 0x60, 0x60),      // deep teal
            AccentDim: A(0x80, 0x2C, 0x60, 0x60),
            AccentGlow: A(0x33, 0x2C, 0x60, 0x60),
            Dead: C(0xA8, 0x90, 0x70),      // muted warm grey
            Header: C(0xD5, 0xC5, 0xA5),      // sandy header
            WindowBackground: () => new SolidColorBrush(C(0xEB, 0xE0, 0xCB)),
            DwmCaption: 0x00A5C5D5,   // #D5C5A5  COLORREF
            DwmText: 0x0060602C,   // #2C6060  COLORREF
            DwmBorder: 0x008AAFC5),  // #C5AF8A  COLORREF

        //  Forest (earth brown / turquoise)
        [SemaBuzzThemeId.Forest] = new ThemeDef(
            Background: C(0x13, 0x0D, 0x07),
            Surface: C(0x1F, 0x12, 0x08),
            Border: C(0x4D, 0x2E, 0x12),
            Accent: C(0x00, 0xC8, 0xA0),      // turquoise stone
            AccentDim: A(0x80, 0x00, 0xC8, 0xA0),
            AccentGlow: A(0x33, 0x00, 0xC8, 0xA0),
            Dead: C(0x3A, 0x22, 0x10),
            Header: C(0x0D, 0x08, 0x04),
            WindowBackground: () => new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(C(0x13, 0x0D, 0x07), 0.0),
                    new GradientStop(C(0x1C, 0x10, 0x06), 0.5),
                    new GradientStop(C(0x13, 0x0D, 0x07), 1.0),
                },
                new Point(0, 0), new Point(0, 1)),
            DwmCaption: 0x00070D13,   // #130D07  COLORREF
            DwmText: 0x00A0C800,   // #00C8A0  COLORREF
            DwmBorder: 0x00122E4D),  // #4D2E12  COLORREF

    };

    //  State

    public static SemaBuzzThemeId Current { get; private set; } = SemaBuzzThemeId.Obsidian;

    /// <summary>The accent <see cref="Color"/> of the currently active theme.</summary>
    public static Color AccentColor => Palettes[Current].Accent;

    /// <summary>Fired on the UI thread after every theme switch.</summary>
    public static event Action? ThemeChanged;

    //  Apply

    /// <summary>
    /// Switch the application to <paramref name="theme"/>, updating all
    /// DynamicResource bindings and DWM chrome on every open window.
    /// Safe to call on the UI thread at any time.
    /// </summary>
    public static void Apply(SemaBuzzThemeId theme)
    {
        Current = theme;
        var p = Palettes[theme];
        var res = Application.Current.Resources;

        // Brush resources  replaced wholesale so DynamicResource picks them up
        res["ObsidianBrush"] = Solid(p.Background);
        res["ObsidianSurfaceBrush"] = Solid(p.Surface);
        res["ObsidianBorderBrush"] = Solid(p.Border);
        res["AmberBrush"] = Solid(p.Accent);
        res["AmberDimBrush"] = Solid(p.AccentDim);
        res["AmberGlowBrush"] = Solid(p.AccentGlow);
        res["WireDeadBrush"] = Solid(p.Dead);
        res["WindowHeaderBrush"] = Solid(p.Header);
        res["WindowHeaderDimBrush"] = Solid(Color.FromArgb(0x66, p.Header.R, p.Header.G, p.Header.B));

        // Header text — white on dark headers (e.g. Win95 navy), else same as Accent
        var headerText = p.HeaderText.A == 0 ? p.Accent : p.HeaderText;
        res["AmberHeaderBrush"] = Solid(headerText);
        res["AmberHeaderColor"] = headerText;

        // Color resources (used by a few explicit Color references)
        res["ObsidianColor"] = p.Background;
        res["ObsidianSurfaceColor"] = p.Surface;
        res["ObsidianBorderColor"] = p.Border;
        res["AmberColor"] = p.Accent;
        res["AmberDimColor"] = p.AccentDim;
        res["AmberGlowColor"] = p.AccentGlow;
        res["WireDeadColor"] = p.Dead;

        // Window background  may be a LinearGradientBrush for gradient themes
        res["WindowBackgroundBrush"] = p.WindowBackground();

        // DWM chrome  apply to every currently-open window
        foreach (Window w in Application.Current.Windows)
            SemaBuzzTheme.Apply(w, p.DwmCaption, p.DwmText, p.DwmBorder);

        ThemeChanged?.Invoke();
    }

    /// <summary>
    /// Apply only the DWM chrome (caption/text/border colors) of the current
    /// theme to a single window  use this in dialog <c>OnSourceInitialized</c>
    /// overrides so newly-opened windows match the active theme.
    /// </summary>
    public static void ApplyChrome(Window window)
    {
        var p = Palettes[Current];
        SemaBuzzTheme.Apply(window, p.DwmCaption, p.DwmText, p.DwmBorder);
    }

    //  Helpers

    private static SolidColorBrush Solid(Color c) => new(c);
}
