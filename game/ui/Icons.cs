using System.Collections.Generic;
using Godot;

namespace HM27.UI;

/// <summary>Иконки Lucide (ISC): SVG из res://ui/icons, перекрашенные в белый (цвет — через Modulate).</summary>
public static class Icons
{
    static readonly Dictionary<string, Texture2D> Cache = new();

    public static Texture2D Get(string name, bool filled = false)
    {
        string key = name + (filled ? ":f" : "");
        if (Cache.TryGetValue(key, out var t)) return t;
        var path = $"res://ui/icons/{name}.svg";
        if (!FileAccess.FileExists(path)) return null;
        var svg = FileAccess.GetFileAsString(path).Replace("currentColor", "#ffffff");
        // комментарий с лицензией перед <svg> не все сборки ThorVG переваривают (у Льва не было значка «Скаутинга») — убираем; лицензия — LICENSE-lucide.txt
        int open = svg.IndexOf("<svg", System.StringComparison.Ordinal);
        if (open > 0) svg = svg[open..];
        if (filled) svg = svg.Replace("fill=\"none\"", "fill=\"#ffffff\"");
        var img = new Image();
        if (img.LoadSvgFromString(svg, 3.0f) != Error.Ok) return null;
        img.GenerateMipmaps();
        t = ImageTexture.CreateFromImage(img);
        Cache[key] = t;
        return t;
    }
}
