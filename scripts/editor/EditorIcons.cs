using System.Globalization;
using Godot;

/// The editor's icons, drawn from small SVG outlines (24 by 24, in the style of Lucide) the first time each is asked
/// for. They are white unless a color is given, so buttons can tint them with their theme's icon colors.
public static class EditorIcons
{
    static readonly Dictionary<string, string> Shapes = new()
    {
        ["outline"] = "<path d='M21 12h-8M21 6H8M21 18h-8M3 6v4c0 1.1.9 2 2 2h3M3 10v6c0 1.1.9 2 2 2h3'/>",
        ["recipes"] = "<path d='M12 2 2 7l10 5 10-5-10-5zM2 17l10 5 10-5M2 12l10 5 10-5'/>",
        ["blocks"] = "<path d='M21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73l7 4a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16zM3.3 7 12 12l8.7-5M12 22V12'/>",
        ["play"] = "<path d='M6 3 20 12 6 21Z' fill='currentColor'/>",
        ["stop"] = "<rect x='5' y='5' width='14' height='14' rx='2' fill='currentColor'/>",
        ["restart"] = "<path d='M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8M3 3v5h5'/>",
        ["save"] = "<path d='M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2zM17 21v-8H7v8M7 3v5h8'/>",
        ["undo"] = "<path d='M3 7v6h6M21 17a9 9 0 0 0-9-9 9 9 0 0 0-6 2.3L3 13'/>",
        ["redo"] = "<path d='M21 7v6h-6M3 17a9 9 0 0 1 9-9 9 9 0 0 1 6 2.3l3 2.7'/>",
        ["plus"] = "<path d='M12 5v14M5 12h14'/>",
        ["trash"] = "<path d='M3 6h18M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2'/>",
        ["rename"] = "<path d='M17 3a2.85 2.83 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5Z'/>",
        ["duplicate"] = "<rect x='8' y='8' width='14' height='14' rx='2'/><path d='M4 16c-1.1 0-2-.9-2-2V4c0-1.1.9-2 2-2h10c1.1 0 2 .9 2 2'/>",
        ["warning"] = "<path d='M10.29 3.86 1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0zM12 9v4M12 17h.01'/>",
        ["error"] = "<circle cx='12' cy='12' r='10'/><path d='m15 9-6 6M9 9l6 6'/>",
        ["ok"] = "<circle cx='12' cy='12' r='10'/><path d='m8 12 3 3 5-6'/>",
        ["check"] = "<path d='M20 6 9 17l-5-5'/>",
        ["close"] = "<path d='M18 6 6 18M6 6l12 12'/>",
        ["chevron_down"] = "<path d='m6 9 6 6 6-6'/>",
        ["chevron_right"] = "<path d='m9 18 6-6-6-6'/>",
        ["up"] = "<path d='m18 15-6-6-6 6'/>",
        ["down"] = "<path d='m6 9 6 6 6-6'/>",
        ["search"] = "<circle cx='11' cy='11' r='8'/><path d='m21 21-4.3-4.3'/>",
        ["more"] = "<circle cx='5' cy='12' r='1'/><circle cx='12' cy='12' r='1'/><circle cx='19' cy='12' r='1'/>",
        ["back"] = "<path d='m12 19-7-7 7-7M19 12H5'/>",
        ["note"] = "<path d='M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z'/>",
        ["book"] = "<path d='M4 19.5A2.5 2.5 0 0 1 6.5 17H20M6.5 2H20v20H6.5A2.5 2.5 0 0 1 4 19.5v-15A2.5 2.5 0 0 1 6.5 2z'/>",
        ["game"] = "<path d='M21 4h-7M10 4H3M21 12h-9M8 12H3M21 20h-5M12 20H3M14 2v4M8 10v4M16 18v4'/>",
        ["table"] = "<rect x='3' y='3' width='18' height='18' rx='2'/><path d='M3 9h18M9 21V9'/>",
        ["zone"] = "<rect x='4' y='4' width='16' height='16' rx='2' stroke-dasharray='3 3'/>",
        ["deck"] = "<rect x='8' y='2' width='12' height='16' rx='2'/><path d='M4 7v13a2 2 0 0 0 2 2h9'/>",
        ["players"] = "<path d='M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2M22 21v-2a4 4 0 0 0-3-3.87M16 3.13a4 4 0 0 1 0 7.75'/><circle cx='9' cy='7' r='4'/>",
        ["variables"] = "<path d='M8 21s-4-3-4-9 4-9 4-9M16 3s4 3 4 9-4 9-4 9M15 9l-6 6M9 9l6 6'/>",
        ["actions"] = "<path d='m9 9 5 12 1.8-5.2L21 14ZM7.2 2.2 8 5.1M5.1 8l-2.9-.8M14 4.1 12 6M6 12l-1.9 2'/>",
        ["turn"] = "<path d='m17 2 4 4-4 4M3 11v-1a4 4 0 0 1 4-4h14M7 22l-4-4 4-4M21 13v1a4 4 0 0 1-4 4H3'/>",
        ["triggers"] = "<path d='M13 2 3 14h9l-1 8 10-12h-9l1-8z'/>",
        ["conditions"] = "<path d='M16 3h5v5M8 3H3v5M12 22v-8.3a4 4 0 0 0-1.17-2.87L3 3M15 9l6-6'/>",
        ["scoring"] = "<path d='M3 3v18h18M18 17V9M13 17V5M8 17v-3'/>",
        ["melds"] = "<rect x='3' y='3' width='7' height='7' rx='1'/><rect x='14' y='3' width='7' height='7' rx='1'/><rect x='14' y='14' width='7' height='7' rx='1'/><rect x='3' y='14' width='7' height='7' rx='1'/>",
        ["file"] = "<path d='M8 3H7a2 2 0 0 0-2 2v5a2 2 0 0 1-2 2 2 2 0 0 1 2 2v5c0 1.1.9 2 2 2h1M16 21h1a2 2 0 0 0 2-2v-5c0-1.1.9-2 2-2a2 2 0 0 1-2-2V5a2 2 0 0 0-2-2h-1'/>",
        ["panel_left"] = "<rect x='3' y='3' width='18' height='18' rx='2'/><path d='M9 3v18'/>",
        ["panel_right"] = "<rect x='3' y='3' width='18' height='18' rx='2'/><path d='M15 3v18'/>",
        ["panel_bottom"] = "<rect x='3' y='3' width='18' height='18' rx='2'/><path d='M3 15h18'/>",
        ["function"] = "<path d='M9 17c2 0 2.8-1 2.8-2.8V10c0-2 1-3.3 3.2-3M9 11.2h5.7'/><rect x='3' y='3' width='18' height='18' rx='2'/>",
    };

    static readonly Dictionary<string, Texture2D> cache = [];

    /// The icon, 16 pixels across (times size), in the color given (white if none).
    public static Texture2D Icon(string name, Color? color = null, float size = 1)
    {
        Color tint = color ?? Colors.White;
        string key = $"{name} {tint.ToHtml()} {size}";
        if (!cache.TryGetValue(key, out Texture2D? icon))
        {
            string shape = Shapes[name].Replace("currentColor", "#" + tint.ToHtml(false));
            string opacity = tint.A.ToString(CultureInfo.InvariantCulture);
            string svg = "<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16' viewBox='0 0 24 24' fill='none' "
                + $"stroke='#{tint.ToHtml(false)}' stroke-opacity='{opacity}' stroke-width='2' stroke-linecap='round' stroke-linejoin='round'>{shape}</svg>";
            cache[key] = icon = Texture(svg, size);
        }
        return icon;
    }

    /// A check box, ticked or not, in the editor's colors.
    public static Texture2D CheckBox(bool ticked)
    {
        string key = $"check_box {ticked}";
        if (!cache.TryGetValue(key, out Texture2D? icon))
        {
            string inside = ticked
                ? $"<rect x='1.5' y='1.5' width='13' height='13' rx='3' fill='#{EditorTheme.Accent.ToHtml(false)}'/><path d='m4.5 8 2.5 2.5 4.5-5' fill='none' stroke='#ffffff' stroke-width='1.6' stroke-linecap='round' stroke-linejoin='round'/>"
                : "<rect x='1.5' y='1.5' width='13' height='13' rx='3' fill='#2a2a2a' stroke='#6b6b6b'/>";
            cache[key] = icon = Texture($"<svg xmlns='http://www.w3.org/2000/svg' width='16' height='16' viewBox='0 0 16 16'>{inside}</svg>", 1);
        }
        return icon;
    }

    static Texture2D Texture(string svg, float size)
    {
        Image image = new();
        image.LoadSvgFromString(svg, size);
        return ImageTexture.CreateFromImage(image);
    }
}
