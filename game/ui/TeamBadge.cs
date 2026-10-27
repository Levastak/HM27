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
    readonly Queue<(string key, string url)> _queue = new();
    HttpRequest _http;
    string _current;
    /// <summary>Загружено: ключ — аббревиатура клуба или «p:id» для фото игрока.</summary>
    public event Action<string> Loaded;

    public override void _Ready()
    {
        I = this;
        DirAccess.MakeDirRecursiveAbsolute("user://logos");
        DirAccess.MakeDirRecursiveAbsolute("user://photos");
        _http = new HttpRequest { Timeout = 15 };
        AddChild(_http);
        _http.RequestCompleted += OnDone;
    }

    public Texture2D Get(TeamData t) => Fetch(t.Abbr, t.Logo);

    /// <summary>Фото игрока (assets.nhle.com), кэш в папке пользователя. Пока нет — null.</summary>
    public Texture2D Photo(PlayerData p) =>
        string.IsNullOrEmpty(p.Head) ? null : Fetch($"p:{p.Id}", $"https://assets.nhle.com/mugs/nhl/{p.Head}.png");

    static string PathOf(string key) => key.StartsWith("p:") ? $"user://photos/{key[2..]}.png" : $"user://logos/{key}.svg";

    Texture2D Fetch(string key, string url)
    {
        if (_tex.TryGetValue(key, out var tex)) return tex;
        var path = PathOf(key);
        if (FileAccess.FileExists(path))
        {
            tex = Decode(key, FileAccess.GetFileAsBytes(path));
            if (tex != null) { _tex[key] = tex; return tex; }
        }
        if (!_pending.Contains(key) && !string.IsNullOrEmpty(url))
        {
            _pending.Add(key);
            _queue.Enqueue((key, url));
            Pump();
        }
        return null;
    }

    void Pump()
    {
        if (_current != null || _queue.Count == 0) return;
        var (key, url) = _queue.Dequeue();
        _current = key;
        if (_http.Request(url) != Error.Ok) { _current = null; Pump(); }
    }

    void OnDone(long result, long code, string[] headers, byte[] body)
    {
        var key = _current; _current = null;
        if (result == (long)HttpRequest.Result.Success && code == 200 && body.Length > 100)
        {
            var tex = Decode(key, body);
            if (tex != null)
            {
                using (var f = FileAccess.Open(PathOf(key), FileAccess.ModeFlags.Write)) f?.StoreBuffer(body);
                _tex[key] = tex;
                Loaded?.Invoke(key);
            }
        }
        Pump();
    }

    static Texture2D Decode(string key, byte[] data)
    {
        var img = new Image();
        var err = key.StartsWith("p:") ? img.LoadPngFromBuffer(data) : img.LoadSvgFromString(System.Text.Encoding.UTF8.GetString(data), 4.0f);
        if (err != Error.Ok) return null;
        img.GenerateMipmaps();
        return ImageTexture.CreateFromImage(img);
    }
}

/// <summary>Фото игрока в рамке; пока не скачалось — силуэт.</summary>
public partial class PlayerPhoto : Control
{
    readonly PlayerData _p;
    readonly Color _bg;
    Texture2D _tex;
    public PlayerPhoto() { }
    public PlayerPhoto(PlayerData p, int size, Color bg) { _p = p; _bg = bg; CustomMinimumSize = new Vector2(size, size); MouseFilter = MouseFilterEnum.Ignore; ClipContents = true; }

    public override void _Ready()
    {
        _tex = LogoCache.I?.Photo(_p);
        if (LogoCache.I != null) LogoCache.I.Loaded += OnLoaded;
    }
    public override void _ExitTree() { if (LogoCache.I != null) LogoCache.I.Loaded -= OnLoaded; }
    void OnLoaded(string key) { if (key == $"p:{_p.Id}") { _tex = LogoCache.I.Photo(_p); QueueRedraw(); } }

    public override void _Draw()
    {
        var r = new Rect2(Vector2.Zero, Size);
        DrawRect(r, _bg);
        if (_tex != null)
        {
            var ts = _tex.GetSize();
            float k = Math.Max(Size.X / ts.X, Size.Y / ts.Y);
            var sz = ts * k;
            DrawTextureRect(_tex, new Rect2((Size - sz) / 2, sz), false);
        }
        else
        {
            var c = new Color(1, 1, 1, 0.18f);
            DrawCircle(new Vector2(Size.X / 2, Size.Y * 0.38f), Size.X * 0.17f, c);
            // плечи: полукруг, обрезанный рамкой
            var pts = new System.Collections.Generic.List<Vector2>();
            float cx = Size.X / 2, cy = Size.Y, rr = Size.X * 0.36f;
            for (int i = 0; i <= 24; i++) { float a = Mathf.Pi + Mathf.Pi * i / 24f; pts.Add(new Vector2(cx + rr * Mathf.Cos(a), cy + rr * 0.9f * Mathf.Sin(a) + rr * 0.15f)); }
            DrawColoredPolygon(pts.ToArray(), c);
        }
        DrawRect(r, new Color(1, 1, 1, 0.12f), false, 1);
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
