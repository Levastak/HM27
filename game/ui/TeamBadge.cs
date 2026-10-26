using System;
using System.Collections.Generic;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>
/// Эмблемы клубов: при первом запуске скачиваются с сайта НХЛ (SVG) и кэшируются в папке пользователя.
/// Эмблемы — товарные знаки лиги и клубов; в сборку игры они не входят. Пока эмблемы нет — круг в цветах клуба.
/// </summary>
public partial class LogoCache : Node
{
    public static LogoCache I { get; private set; }
    readonly Dictionary<string, Texture2D> _tex = new();
    readonly HashSet<string> _pending = new();
    readonly Queue<(string abbr, string url)> _queue = new();
    HttpRequest _http;
    string _current;
    public event Action<string> Loaded;

    public override void _Ready()
    {
        I = this;
        DirAccess.MakeDirRecursiveAbsolute("user://logos");
        _http = new HttpRequest { Timeout = 15 };
        AddChild(_http);
        _http.RequestCompleted += OnDone;
    }

    public Texture2D Get(TeamData t)
    {
        if (_tex.TryGetValue(t.Abbr, out var tex)) return tex;
        var path = $"user://logos/{t.Abbr}.svg";
        if (FileAccess.FileExists(path))
        {
            tex = FromSvg(FileAccess.GetFileAsString(path));
            if (tex != null) { _tex[t.Abbr] = tex; return tex; }
        }
        if (!_pending.Contains(t.Abbr) && !string.IsNullOrEmpty(t.Logo))
        {
            _pending.Add(t.Abbr);
            _queue.Enqueue((t.Abbr, t.Logo));
            Pump();
        }
        return null;
    }

    void Pump()
    {
        if (_current != null || _queue.Count == 0) return;
        var (abbr, url) = _queue.Dequeue();
        _current = abbr;
        if (_http.Request(url) != Error.Ok) { _current = null; Pump(); }
    }

    void OnDone(long result, long code, string[] headers, byte[] body)
    {
        var abbr = _current; _current = null;
        if (result == (long)HttpRequest.Result.Success && code == 200 && body.Length > 100)
        {
            var svg = System.Text.Encoding.UTF8.GetString(body);
            var tex = FromSvg(svg);
            if (tex != null)
            {
                using (var f = FileAccess.Open($"user://logos/{abbr}.svg", FileAccess.ModeFlags.Write)) f?.StoreString(svg);
                _tex[abbr] = tex;
                Loaded?.Invoke(abbr);
            }
        }
        Pump();
    }

    static Texture2D FromSvg(string svg)
    {
        var img = new Image();
        if (img.LoadSvgFromString(svg, 4.0f) != Error.Ok) return null;
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }
}

/// <summary>Эмблема клуба заданного размера; сама обновляется, когда эмблема скачается.</summary>
public partial class TeamBadge : Control
{
    readonly TeamData _team;
    Texture2D _tex;

    public TeamBadge(TeamData team, int size)
    {
        _team = team;
        CustomMinimumSize = new Vector2(size, size);
        MouseFilter = MouseFilterEnum.Ignore;
    }

    public TeamBadge() { }

    public override void _Ready()
    {
        _tex = LogoCache.I?.Get(_team);
        if (LogoCache.I != null) LogoCache.I.Loaded += OnLoaded;
    }

    public override void _ExitTree()
    {
        if (LogoCache.I != null) LogoCache.I.Loaded -= OnLoaded;
    }

    void OnLoaded(string abbr)
    {
        if (abbr != _team.Abbr) return;
        _tex = LogoCache.I.Get(_team);
        QueueRedraw();
    }

    public override void _Draw()
    {
        var s = Size;
        float d = Math.Min(s.X, s.Y);
        var rect = new Rect2((s.X - d) / 2, (s.Y - d) / 2, d, d);
        if (_tex != null)
        {
            var ts = _tex.GetSize();
            float k = d / Math.Max(ts.X, ts.Y);
            var sz = ts * k;
            DrawTextureRect(_tex, new Rect2(rect.Position + (new Vector2(d, d) - sz) / 2, sz), false);
            return;
        }
        var c1 = new Color(_team.Colors.Count > 0 ? _team.Colors[0] : "#444444");
        var c2 = new Color(_team.Colors.Count > 1 ? _team.Colors[1] : "#888888");
        DrawCircle(rect.GetCenter(), d / 2, c2);
        DrawCircle(rect.GetCenter(), d / 2 - Math.Max(2, d * 0.06f), c1);
        int fs = (int)(d * 0.3f);
        var font = Ui.Black;
        var w = font.GetStringSize(_team.Abbr, HorizontalAlignment.Left, -1, fs).X;
        DrawString(font, new Vector2(rect.GetCenter().X - w / 2, rect.GetCenter().Y + fs * 0.36f), _team.Abbr, HorizontalAlignment.Left, -1, fs, Ui.ReadableOn(c1));
    }
}
