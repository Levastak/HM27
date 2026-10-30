using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Godot;
using HM27.Core;

namespace HM27.UI;

/// <summary>Гиперссылки на игроков и клубы: подсветка при наведении, щелчок — карточка.</summary>
public static class Links
{
    public static Label Link(string text, Action onClick, int size = 15, Color? color = null, Font font = null, HorizontalAlignment align = HorizontalAlignment.Left)
    {
        var l = Ui.L(text, size, color ?? Ui.Text, font, align);
        var baseCol = color ?? Ui.Text;
        l.MouseFilter = Control.MouseFilterEnum.Stop;
        l.MouseDefaultCursorShape = Control.CursorShape.PointingHand;
        l.SetMeta("link", true);
        l.MouseEntered += () => l.AddThemeColorOverride("font_color", Ui.AccentBright);
        l.MouseExited += () => l.AddThemeColorOverride("font_color", baseCol);
        l.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
            {
                l.AcceptEvent();
                onClick();
            }
        };
        return l;
    }

    /// <summary>Карточка игрока: внутри интерфейса клуба — страница, на экране матча — всплывающая карточка.</summary>
    public static void OpenPlayer(Node from, PlayerData p)
    {
        if (Shell.I != null) Shell.I.Go("player", null, p.Id.ToString());
        else if (Main.I?.Career != null) PlayerCard.Show(from, p, Main.I.Career);
    }

    public static void OpenClub(string abbr)
    {
        if (Shell.I != null) Shell.I.Go("club", null, abbr);
        else Main.I?.ShowHub("club", abbr);
    }

    public static Label Player(PlayerData p, string text = null, int size = 14, Color? color = null, Font font = null)
    {
        Label l = null;
        l = Link(text ?? p.NameRu, () => OpenPlayer(l, p), size, color, font);
        return l;
    }

    public static Label Player(Career c, int id, string text = null, int size = 14, Color? color = null, Font font = null) =>
        c.Data.PlayerById.TryGetValue(id, out var p) ? Player(p, text, size, color, font) : Ui.L(text ?? "—", size, color ?? Ui.Muted, font);

    /// <summary>Ссылка на игрока внутри текста BBCode (для Rich).</summary>
    public static string Bb(int id, string name) => $"[url=p:{id}]{Esc(name)}[/url]";
    public static string Esc(string s) => s?.Replace("[", "[lb]") ?? "";

    /// <summary>
    /// Текст с ссылками на игроков (BBCode, [url=p:id]): щелчок — карточка игрока, курсор-рука над именем.
    /// Для ленты матча и других мест, где имена стоят внутри фразы.
    /// </summary>
    public static RichTextLabel Rich(Node owner, string bb, int size, Color color, Font font = null)
    {
        var l = new RichTextLabel
        {
            BbcodeEnabled = true, FitContent = true, ScrollActive = false, AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SelectionEnabled = false, MetaUnderlined = false, MouseFilter = Control.MouseFilterEnum.Pass,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        l.AddThemeFontOverride("normal_font", font ?? Ui.Regular);
        l.AddThemeFontOverride("bold_font", Ui.Semi);
        l.AddThemeFontSizeOverride("normal_font_size", size);
        l.AddThemeFontSizeOverride("bold_font_size", size);
        l.AddThemeColorOverride("default_color", color);
        l.Text = bb;
        l.MetaClicked += meta =>
        {
            string m = meta.AsString();
            if (m.StartsWith("p:") && int.TryParse(m[2..], out var id) && Main.I?.Career?.Data.PlayerById.TryGetValue(id, out var p) == true) OpenPlayer(owner, p);
        };
        l.MetaHoverStarted += _ => l.MetaUnderlined = true;
        l.MetaHoverEnded += _ => l.MetaUnderlined = false;
        return l;
    }

    public static Label Club(TeamData t, string text = null, int size = 14, Color? color = null, Font font = null) =>
        Link(text ?? t.Name, () => OpenClub(t.Abbr), size, color, font);

    /// <summary>Строка из ссылок через разделитель (звенья, авторы гола).</summary>
    public static Control Row(IEnumerable<Control> items, string sep = " — ", int size = 12, Color? sepColor = null)
    {
        // перенос по строкам: длинный список имён не раздвигает страницу вбок
        var h = new HFlowContainer();
        h.AddThemeConstantOverride("h_separation", 0);
        h.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        bool first = true;
        foreach (var it in items)
        {
            if (!first) h.AddChild(Ui.L(sep, size, sepColor ?? Ui.Dim));
            h.AddChild(it); first = false;
        }
        return h;
    }
}

/// <summary>Тексты для биографий: трофеи, турниры, лиги, клубы, города — по-русски.</summary>
public static class BioText
{
    public static string Season(long s) => $"{s / 10000}-{(s % 10000) % 100:00}";
    public static int EndYear(long s) => (int)(s % 10000);
    /// <summary>Сезон игры (20262027): с него начинается альтернативная история.</summary>
    public static long GameSeason(GameData d) => (long)d.Season * 10000 + d.Season + 1;
    /// <summary>Реальные сезоны НХЛ до старта игры; текущий реальный сезон не показываем — в игре он свой.</summary>
    public static List<List<System.Text.Json.JsonElement>> Past(GameData d, PlayerBio b) =>
        b == null ? new() : b.Nhl.Where(r => r[0].GetInt64() < GameSeason(d)).ToList();

    public static (string name, string note) Trophy(string en) => en switch
    {
        "Stanley Cup" => ("Кубок Стэнли", null),
        "Hart Memorial Trophy" => ("Харт Трофи", "самый ценный игрок"),
        "Ted Lindsay Award" => ("Тед Линдсей Эворд", "лучший по версии игроков"),
        "Art Ross Trophy" => ("Арт Росс Трофи", "лучший бомбардир"),
        "Maurice “Rocket” Richard Trophy" or "Maurice \"Rocket\" Richard Trophy" => ("Морис Ришар Трофи", "лучший снайпер"),
        "Conn Smythe Trophy" => ("Конн Смайт Трофи", "самый ценный игрок плей-офф"),
        "Calder Memorial Trophy" => ("Колдер Трофи", "лучший новичок"),
        "James Norris Memorial Trophy" => ("Джеймс Норрис Трофи", "лучший защитник"),
        "Vezina Trophy" => ("Везина Трофи", "лучший вратарь"),
        "William M. Jennings Trophy" => ("Уильям Дженнингс Трофи", "меньше всех пропущенных"),
        "Frank J. Selke Trophy" => ("Фрэнк Селки Трофи", "лучший нападающий в обороне"),
        "Lady Byng Memorial Trophy" => ("Леди Бинг Трофи", "благородство"),
        "Mark Messier NHL Leadership Award" => ("Приз Марка Мессье", "лидерство"),
        "Bill Masterton Memorial Trophy" => ("Билл Мастертон Трофи", "преданность хоккею"),
        "King Clancy Memorial Trophy" => ("Кинг Клэнси Трофи", "лидерство и помощь людям"),
        "E.J. McGuire Award of Excellence" => ("Приз И. Дж. Макгуайра", "лучший проспект драфта"),
        "NHL Foundation Player Award" => ("Приз фонда НХЛ", "помощь людям"),
        _ => (en, null),
    };

    public static string Intl(string k) => k switch
    {
        "og" => "Олимпиада", "wc" => "чемпионат мира", "wjc" => "молодёжный ЧМ", "u18" => "юниорский ЧМ", "u17" => "турнир до 17 лет",
        "wcup" => "Кубок мира", "4n" => "Турнир четырёх наций", _ => k,
    };

    public static string League(string code) => code switch
    {
        "KHL" => "КХЛ", "MHL" => "МХЛ", "VHL" => "ВХЛ", "AHL" => "АХЛ", "Russia" => "чемпионат России", "Russia-Jr." => "юниорская лига России",
        "Russia2" => "высшая лига России", "Russia3" => "первая лига России", "Czechia" => "Экстралига (Чехия)", "Czech" => "Экстралига (Чехия)",
        "Slovakia" => "Экстралига (Словакия)", "SHL" => "SHL (Швеция)", "Allsvenskan" => "Аллсвенскан (Швеция)", "HockeyAllsvenskan" => "Аллсвенскан (Швеция)",
        "J20 SuperElit" or "J20 Nationell" or "Swe-Jr." => "молодёжная лига Швеции", "Liiga" => "Лиига (Финляндия)", "Mestis" => "Местис (Финляндия)",
        "U20 SM-sarja" or "Jr. A SM-liiga" => "молодёжная лига Финляндии", "NL" => "Национальная лига (Швейцария)", "SL" => "Швейцарская лига",
        "DEL" => "DEL (Германия)", "ICEHL" or "EBEL" => "ICEHL (Австрия)", "NCAA" => "NCAA (студенты США)", "USHL" => "USHL (юниоры США)",
        "USNTDP" or "NTDP" => "сборная США до 18 лет", "OHL" => "OHL (юниоры Канады)", "WHL" => "WHL (юниоры Канады)", "QMJHL" => "QMJHL (юниоры Канады)",
        "ECHL" => "ECHL", _ => code,
    };

    static readonly (string en, string ru)[] Clubs =
    {
        ("SKA-1946", "СКА-1946"), ("SKA-Neva", "СКА-Нева"), ("SKA", "СКА"), ("CSKA", "ЦСКА"), ("Krasnaya Armiya", "«Красная Армия»"),
        ("Loko", "«Локо»"), ("Lokomotiv", "«Локомотив»"), ("Ak Bars", "«Ак Барс»"), ("Irbis", "«Ирбис»"), ("Avangard", "«Авангард»"),
        ("Omskie Yastreby", "«Омские Ястребы»"), ("Metallurg Magnitogorsk", "«Металлург» (Магнитогорск)"), ("Stalnye Lisy", "«Стальные Лисы»"),
        ("Metallurg Novokuznetsk", "«Металлург» (Новокузнецк)"), ("Novokuznetsk", "«Металлург» (Новокузнецк)"), ("Kuznetskie Medvedi", "«Кузнецкие Медведи»"),
        ("Salavat Yulaev", "«Салават Юлаев»"), ("Tolpar", "«Толпар»"), ("Dynamo Moskva", "«Динамо» (Москва)"), ("Dinamo Moskva", "«Динамо» (Москва)"),
        ("MHC Dynamo", "МХК «Динамо»"), ("Spartak", "«Спартак»"), ("Traktor", "«Трактор»"), ("Belye Medvedi", "«Белые Медведи»"),
        ("Torpedo", "«Торпедо»"), ("Chaika", "«Чайка»"), ("Sibir", "«Сибирь»"), ("Sibirskie Snaipery", "«Сибирские Снайперы»"),
        ("Avtomobilist", "«Автомобилист»"), ("Severstal", "«Северсталь»"), ("Almaz", "«Алмаз»"), ("Neftekhimik", "«Нефтехимик»"),
        ("Reaktor", "«Реактор»"), ("Amur", "«Амур»"), ("Admiral", "«Адмирал»"), ("Vityaz", "«Витязь»"), ("Barys", "«Барыс»"),
        ("Dinamo Minsk", "«Динамо» (Минск)"), ("Kunlun", "«Куньлунь»"), ("Jokerit", "«Йокерит»"), ("Lada", "«Лада»"), ("Sochi", "«Сочи»"),
        ("Yugra", "«Югра»"), ("Atlant", "«Атлант»"), ("Dynamo St. Petersburg", "«Динамо» (Санкт-Петербург)"), ("Khimik", "«Химик»"),
        ("Molot-Prikamye", "«Молот-Прикамье»"), ("Rubin", "«Рубин»"), ("Zvezda", "«Звезда»"), ("Kristall", "«Кристалл»"),
    };

    public static string Club(string en)
    {
        if (Names.English) return en;
        foreach (var (k, ru) in Clubs) if (en.StartsWith(k, StringComparison.OrdinalIgnoreCase)) return ru;
        return en;
    }

    static readonly Dictionary<string, string> Cities = new()
    {
        ["Moscow"] = "Москва", ["Moskva"] = "Москва", ["St. Petersburg"] = "Санкт-Петербург", ["Saint Petersburg"] = "Санкт-Петербург", ["Leningrad"] = "Ленинград",
        ["Novokuznetsk"] = "Новокузнецк", ["Ufa"] = "Уфа", ["Kazan"] = "Казань", ["Chelyabinsk"] = "Челябинск", ["Yaroslavl"] = "Ярославль",
        ["Omsk"] = "Омск", ["Magnitogorsk"] = "Магнитогорск", ["Nizhny Novgorod"] = "Нижний Новгород", ["Togliatti"] = "Тольятти", ["Tolyatti"] = "Тольятти",
        ["Cherepovets"] = "Череповец", ["Yekaterinburg"] = "Екатеринбург", ["Novosibirsk"] = "Новосибирск", ["Khabarovsk"] = "Хабаровск",
        ["Krasnoyarsk"] = "Красноярск", ["Perm"] = "Пермь", ["Samara"] = "Самара", ["Voskresensk"] = "Воскресенск", ["Elektrostal"] = "Электросталь",
        ["Tver"] = "Тверь", ["Tyumen"] = "Тюмень", ["Kurgan"] = "Курган", ["Kemerovo"] = "Кемерово", ["Surgut"] = "Сургут",
        ["Nizhnekamsk"] = "Нижнекамск", ["Vladivostok"] = "Владивосток", ["Minsk"] = "Минск", ["Riga"] = "Рига", ["Kyiv"] = "Киев", ["Kiev"] = "Киев",
        ["Almaty"] = "Алматы", ["Astana"] = "Астана", ["Saratov"] = "Саратов", ["Penza"] = "Пенза", ["Kirov"] = "Киров", ["Izhevsk"] = "Ижевск",
        ["Toronto"] = "Торонто", ["Montreal"] = "Монреаль", ["Vancouver"] = "Ванкувер", ["Calgary"] = "Калгари", ["Edmonton"] = "Эдмонтон",
        ["Ottawa"] = "Оттава", ["Winnipeg"] = "Виннипег", ["Quebec City"] = "Квебек", ["Halifax"] = "Галифакс", ["Regina"] = "Реджайна", ["Saskatoon"] = "Саскатун",
        ["Stockholm"] = "Стокгольм", ["Gothenburg"] = "Гётеборг", ["Helsinki"] = "Хельсинки", ["Turku"] = "Турку", ["Tampere"] = "Тампере",
        ["Prague"] = "Прага", ["Praha"] = "Прага", ["Bratislava"] = "Братислава", ["Zurich"] = "Цюрих", ["Bern"] = "Берн", ["Berlin"] = "Берлин",
        ["Boston"] = "Бостон", ["Chicago"] = "Чикаго", ["Detroit"] = "Детройт", ["Buffalo"] = "Баффало", ["Minneapolis"] = "Миннеаполис",
        ["St. Louis"] = "Сент-Луис", ["Pittsburgh"] = "Питтсбург", ["Philadelphia"] = "Филадельфия", ["New York"] = "Нью-Йорк", ["Dallas"] = "Даллас",
        ["Scottsdale"] = "Скоттсдейл", ["Los Angeles"] = "Лос-Анджелес", ["Denver"] = "Денвер", ["Richmond Hill"] = "Ричмонд-Хилл", ["Burnaby"] = "Бернаби",
    };

    public static string City(string en) => en == null ? null : Cities.TryGetValue(en, out var ru) ? ru : en;

    /// <summary>«Эдмонтон» по коду клуба базы, иначе название как в данных (бывшие клубы — «Аризона Койотис» и т.п.).</summary>
    public static string NhlTeam(GameData d, string abbrOrName) => abbrOrName switch
    {
        null => "—",
        "Arizona Coyotes" => "«Аризона Койотис»", "Phoenix Coyotes" => "«Финикс Койотис»", "Atlanta Thrashers" => "«Атланта Трэшерз»",
        _ => d.TeamByAbbr.TryGetValue(abbrOrName, out var t) ? $"«{t.City}»" : abbrOrName,
    };

    /// <summary>Отрезки сезонов: 2014–2016 (свёртка подряд идущих сезонов одного клуба).</summary>
    public static List<(string league, string club, int from, int to, int gp)> Spans(IEnumerable<List<JsonElement>> rows)
    {
        var res = new List<(string, string, int, int, int)>();
        foreach (var r in rows)
        {
            long s = r[0].GetInt64(); string lg = r[1].GetString(), club = Club(r[2].GetString()); int gp = r[3].GetInt32();
            int y0 = (int)(s / 10000), y1 = EndYear(s);
            if (res.Count > 0 && res[^1].Item1 == lg && res[^1].Item2 == club && res[^1].Item4 == y0) res[^1] = (lg, club, res[^1].Item3, y1, res[^1].Item5 + gp);
            else res.Add((lg, club, y0, y1, gp));
        }
        return res;
    }
}

/// <summary>Виджеты биографии игрока и истории клуба.</summary>
public static class BioWidgets
{
    static Control Line(string key, Control value)
    {
        var h = Ui.H(10);
        h.AddChild(Ui.L(key, 12, Ui.Dim).Also(l => l.CustomMinimumSize = new Vector2(118, 0)));
        h.AddChild(Ui.Expand(value));
        return h;
    }

    static Label Text(string s, Color? c = null) => Ui.L(s, 13, c ?? Ui.Text).Also(l => { l.AutowrapMode = TextServer.AutowrapMode.WordSmart; l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill; });

    /// <summary>Биография и достижения: рождение, драфт, путь до НХЛ, НХЛ в цифрах, трофеи, сборная.</summary>
    public static Control Biography(Career c, PlayerData p)
    {
        var b = p.Bio;
        var v = Ui.V(8);
        if (b == null) { v.AddChild(Ui.L("Нет данных", 13, Ui.Muted)); return v; }
        var born = DateOnly.TryParse(p.Born, out var bd) ? $"{bd.Day} {Ui.MonthsGen[bd.Month - 1]} {bd.Year}" : "—";
        var place = string.Join(", ", new[] { BioText.City(b.City), PlayerCard.Country(p.Country) }.Where(x => !string.IsNullOrEmpty(x)));
        v.AddChild(Line("Родился", Text($"{born}{(place.Length > 0 ? " · " + place : "")}")));
        if (b.Draft is { Count: 4 })
        {
            int y = b.Draft[0].GetInt32(), rd = b.Draft[2].GetInt32(), n = b.Draft[3].GetInt32();
            string ab = b.Draft[1].GetString();
            var h = Ui.H(0, Ui.L($"{y} · {rd}-й раунд, {n}-й номер · ", 13, Ui.Text));
            if (ab != null && c.Data.TeamByAbbr.TryGetValue(ab, out var dt)) h.AddChild(Links.Club(dt, $"«{dt.City}»", 13));
            else h.AddChild(Ui.L(BioText.NhlTeam(c.Data, ab), 13, Ui.Text));
            v.AddChild(Line("Драфт", h));
        }
        else v.AddChild(Line("Драфт", Text("не выбран — пришёл в НХЛ свободным агентом", Ui.Muted)));

        // путь до НХЛ
        var pre = BioText.Spans(b.Pre);
        if (pre.Count > 0)
            v.AddChild(Line("До НХЛ", Text(string.Join(" → ", pre.Select(x => $"{x.club} ({BioText.League(x.league)}, {(x.from == x.to - 1 ? $"{x.from}-{x.to % 100:00}" : $"{x.from}–{x.to}")})")))));

        // НХЛ в цифрах
        var nhl = BioText.Past(c.Data, b);
        if (nhl.Count > 0)
        {
            bool g = p.IsGoalie;
            int gp = nhl.Sum(r => r[2].GetInt32());
            var clubs = new List<string>();
            foreach (var r in nhl) { var t = BioText.NhlTeam(c.Data, r[1].GetString()); if (clubs.Count == 0 || clubs[^1] != t) clubs.Add(t); }
            string first = BioText.Season(nhl[0][0].GetInt64());
            string tot = g ? $"{gp} {Ui.Plural(gp, "матч", "матча", "матчей")}, {nhl.Sum(r => r[3].GetInt32())} побед, на ноль — {nhl.Sum(r => r[6].GetInt32())}"
                           : $"{gp} {Ui.Plural(gp, "матч", "матча", "матчей")}, {nhl.Sum(r => r[5].GetInt32())} очков ({nhl.Sum(r => r[3].GetInt32())} + {nhl.Sum(r => r[4].GetInt32())})";
            v.AddChild(Line("В НХЛ", Text($"с сезона {first} · {tot}")));
            v.AddChild(Line("Клубы НХЛ", Text(string.Join(" → ", clubs))));
            if (b.Po is { Count: 4 } && b.Po[0] > 0)
                v.AddChild(Line("Плей-офф", Text(g ? $"{b.Po[0]:0} матчей, {b.Po[1]:0} побед, % отражённых {b.Po[3]:.000}" : $"{b.Po[0]:0} матчей, {b.Po[3]:0} очков ({b.Po[1]:0} + {b.Po[2]:0})")));
        }
        var minor = BioText.Spans(b.Minor).Where(x => x.gp >= 5).ToList();
        if (minor.Count > 0)
            v.AddChild(Line("Другие лиги", Text(string.Join(", ", minor.Select(x => $"{x.club} ({BioText.League(x.league)}, {x.from}–{x.to})")), Ui.Muted)));

        // сборная
        if (b.Intl.Count > 0)
        {
            var order = new[] { "og", "4n", "wcup", "wc", "wjc", "u18", "u17" };
            var parts = order.Where(b.Intl.ContainsKey).Select(k =>
            {
                var ys = b.Intl[k].Select(s => BioText.EndYear(s)).Distinct().OrderBy(x => x).ToList();
                return $"{BioText.Intl(k)} ({string.Join(", ", ys)})";
            });
            v.AddChild(Line("Сборная", Text($"{PlayerCard.Country(p.Country)}: {string.Join("; ", parts)}")));
        }
        // капитанство
        var f = p.Fx;
        if (f != null && (f.Cap != null || f.CapSeasons > 0))
            v.AddChild(Line("Капитанство", Text(string.Join("; ", new[] {
                f.Cap == "C" ? "капитан клуба" : f.Cap == "A" ? "ассистент капитана" : null,
                f.CapSeasons > 0 ? $"капитан в {f.CapSeasons} {Ui.Plural(f.CapSeasons, "сезоне", "сезонах", "сезонах")} с 2010 года" : null }.Where(x => x != null)))));
        return v;
    }

    /// <summary>Достижения: Кубки Стэнли, трофеи НХЛ (с годами), Зал славы — как витрина FM.</summary>
    public static Control Honours(PlayerData p)
    {
        var v = Ui.V(0);
        var b = p.Bio;
        var rows = new List<(string name, string note, List<int> years, bool cup)>();
        if (b != null)
            foreach (var a in b.Awards)
            {
                var (name, note) = BioText.Trophy(a[0].GetString());
                var ys = a[1].EnumerateArray().Select(x => BioText.EndYear(x.GetInt64())).Distinct().OrderBy(x => x).ToList();
                rows.Add((name, note, ys, a[0].GetString() == "Stanley Cup"));
            }
        rows = rows.OrderByDescending(r => r.cup).ThenByDescending(r => r.years.Count).ToList();
        if (b?.Hof == 1) rows.Insert(0, ("Зал хоккейной славы", null, new List<int>(), false));
        if (b?.Top100 == 1) rows.Add(("100 величайших игроков НХЛ", null, new List<int>(), false));
        foreach (var r in rows)
        {
            var row = new PanelContainer();
            var sb = Ui.Box(r.cup ? new Color(Ui.Gold, 0.10f) : new Color(0, 0, 0, 0), 0, null, 12);
            sb.ContentMarginTop = sb.ContentMarginBottom = 6; sb.BorderColor = Ui.Line; sb.BorderWidthBottom = 1;
            row.AddThemeStyleboxOverride("panel", sb);
            var h = Ui.H(8);
            h.AddChild(Ui.Icon("trophy", 14, r.cup ? Ui.Gold : Ui.Muted));
            h.AddChild(Ui.Expand(Ui.V(0, Ui.L(r.years.Count > 1 ? $"{r.name} ×{r.years.Count}" : r.name, 13, Ui.Text, Ui.Semi),
                r.note == null ? new Control() : Ui.L(r.note, 11, Ui.Muted))));
            if (r.years.Count > 0) h.AddChild(Ui.L(string.Join(", ", r.years), 12, Ui.Muted));
            row.AddChild(h);
            v.AddChild(row);
        }
        if (rows.Count == 0) v.AddChild(Ui.Margin(Ui.L("Трофеев НХЛ пока нет", 13, Ui.Muted), 12, 8, 12, 8));
        return v;
    }

    /// <summary>Сезоны в НХЛ (регулярка), как таблица «Карьера» в FM.</summary>
    public static Control NhlCareer(Career c, PlayerData p)
    {
        var nhl = BioText.Past(c.Data, p.Bio);
        bool g = p.IsGoalie;
        bool playedNow = g ? c.Goalies.ContainsKey(p.Id) : c.Skaters.ContainsKey(p.Id);
        if (nhl.Count == 0 && !playedNow) return Ui.Margin(Ui.L("В НХЛ ещё не играл", 13, Ui.Muted), 12, 8, 12, 8);
        Control Num(string s, bool bold = false, Color? col = null) => Ui.L(s, 13, col ?? (bold ? Ui.Text : Ui.Muted), bold ? Ui.Semi : null, HorizontalAlignment.Right).Also(l => l.CustomMinimumSize = new Vector2(40, 0));
        var v = Ui.V(0);
        var head = Ui.H(6, Ui.L("Сезон", 13, Ui.Muted, Ui.Cond).Also(l => l.CustomMinimumSize = new Vector2(64, 0)), Ui.Expand(Ui.L("Клуб", 13, Ui.Muted, Ui.Cond)));
        foreach (var t in g ? new[] { "И", "В", "КН", "%ОБ", "Сух" } : new[] { "И", "Г", "П", "О", "Штр", "+/−" })
            head.AddChild(Ui.L(t, 13, Ui.Muted, Ui.Cond, HorizontalAlignment.Right).Also(l => l.CustomMinimumSize = new Vector2(40, 0)));
        v.AddChild(Ui.Margin(head, 12, 6, 12, 6));

        void Row(string season, Control club, Control[] nums, bool game)
        {
            var h = Ui.H(6);
            h.AddChild(Ui.L(season, 13, game ? Ui.Gold : Ui.Muted, game ? Ui.Semi : null).Also(l => l.CustomMinimumSize = new Vector2(64, 0)));
            h.AddChild(Ui.Expand(club));
            foreach (var n in nums) h.AddChild(n);
            var row = new PanelContainer();
            var sb = Ui.Box(game ? new Color(Ui.Gold, 0.07f) : new Color(0, 0, 0, 0), 0, null, 12);
            sb.ContentMarginTop = sb.ContentMarginBottom = 4; sb.BorderColor = Ui.Line; sb.BorderWidthTop = 1;
            row.AddThemeStyleboxOverride("panel", sb);
            row.AddChild(h);
            if (game) row.TooltipText = "Сезон в HM27: альтернативная история с начала сезона 2026-27";
            v.AddChild(row);
        }

        // сезон в игре — первым: дальше история идёт своим путём
        if (playedNow)
        {
            var tt = c.Team(p.Team);
            var club = Ui.H(6, new TeamBadge(tt, 16), Links.Club(tt, tt.City, 13));
            string sea = BioText.Season(BioText.GameSeason(c.Data));
            if (g)
            {
                var s = c.Goalies[p.Id];
                Row(sea, club, new[] { Num($"{s.Gp}"), Num($"{s.W}", true), Num($"{s.Gaa:0.00}"), Num(s.Sa > 0 ? $"{s.SvPct:.000}" : "—", true), Num($"{s.So}") }, true);
            }
            else
            {
                var s = c.Skaters[p.Id];
                Row(sea, club, new[] { Num($"{s.Gp}"), Num($"{s.G}"), Num($"{s.A}"), Num($"{s.Pts}", true), Num($"{s.Pim}"), Num(Ui.Signed(s.Pm, "0")) }, true);
            }
        }
        foreach (var r in nhl.AsEnumerable().Reverse())
        {
            string team = r[1].GetString();
            Control cell = team != null && c.Data.TeamByAbbr.TryGetValue(team, out var tt)
                ? Ui.H(6, new TeamBadge(tt, 16), Links.Club(tt, tt.City, 13)) : Ui.L(BioText.NhlTeam(c.Data, team), 13, Ui.Text);
            Control[] nums = g
                ? new[] { Num($"{r[2].GetInt32()}"), Num($"{r[3].GetInt32()}", true), Num($"{r[4].GetDouble():0.00}"), Num($"{r[5].GetDouble():.000}", true), Num($"{r[6].GetInt32()}") }
                : new[] { Num($"{r[2].GetInt32()}"), Num($"{r[3].GetInt32()}"), Num($"{r[4].GetInt32()}"), Num($"{r[5].GetInt32()}", true), Num($"{r[6].GetInt32()}"), Num(Ui.Signed(r[7].GetInt32(), "0")) };
            Row(BioText.Season(r[0].GetInt64()), cell, nums, false);
        }
        return v;
    }

    /// <summary>История клуба: основание, арена, Кубки Стэнли, Президентский кубок, прежние названия.</summary>
    public static Control ClubHistory(Career c, TeamData t)
    {
        var v = Ui.V(8);
        var h = t.Hist;
        if (h == null) { v.AddChild(Ui.L("Нет данных", 13, Ui.Muted)); return v; }
        v.AddChild(Line("Основан", Text(h.Joined is int j && j != h.Founded ? $"{h.Founded} · в НХЛ с {j}" : $"{h.Founded}")));
        if (!string.IsNullOrEmpty(h.Former)) v.AddChild(Line("История", Text(char.ToUpper(h.Former[0]) + h.Former[1..], Ui.Muted)));
        v.AddChild(Line("Арена", Text($"{h.Arena} · {h.Capacity:N0} мест".Replace(",", " ").Replace(" ", " "))));
        int now = c.Today.Year;
        var cups = h.Cups;
        v.AddChild(Line("Кубок Стэнли", Text(cups.Count == 0 ? "ещё не выигрывал" :
            $"{cups.Count} — {string.Join(", ", cups)} (последний {now - cups.Max()} {Ui.Plural(now - cups.Max(), "год", "года", "лет")} назад)", cups.Count > 0 ? Ui.Gold : Ui.Muted)));
        v.AddChild(Line("Президентский кубок", Text(h.Presidents.Count == 0 ? "нет" : $"{h.Presidents.Count} — {string.Join(", ", h.Presidents)}")));
        // карьера в HM27
        if (c.Champion == t.Abbr) v.AddChild(Line("В HM27", Text($"обладатель Кубка Стэнли {c.Season + 1}", Ui.Gold)));
        // символы клуба: лучшие по сыгранным матчам в НХЛ среди нынешнего состава
        var vets = c.Roster(t.Abbr).Where(p => p.Fx != null && p.Fx.ClubSeasons >= 5).OrderByDescending(p => p.Fx.ClubSeasons).Take(4).ToList();
        if (vets.Count > 0)
        {
            var row = Links.Row(vets.Select(p => (Control)Links.Player(p, $"{p.NameRu} ({p.Fx.ClubSeasons} {Ui.Plural(p.Fx.ClubSeasons, "сезон", "сезона", "сезонов")})", 13)), ", ", 13);
            v.AddChild(Line("Ветераны клуба", row));
        }
        return v;
    }
}
