using System.Text.Json;
using System.Text.Json.Serialization;

namespace HM27.Core;

/// <summary>База игры: файл game/data/hm27-data.json, собирается скриптом data/build.py.</summary>
public sealed class GameData
{
    [JsonPropertyName("format")] public int Format { get; set; }
    [JsonPropertyName("season")] public int Season { get; set; }
    [JsonPropertyName("built")] public string Built { get; set; }
    [JsonPropertyName("data_date")] public string DataDate { get; set; }
    [JsonPropertyName("source")] public string Source { get; set; }
    [JsonPropertyName("league")] public LeagueInfo League { get; set; }
    [JsonPropertyName("teams")] public List<TeamData> Teams { get; set; } = new();
    [JsonPropertyName("players")] public List<PlayerData> Players { get; set; } = new();
    /// <summary>Модель игрока: источники, возрастные кривые по группам (data/history.py).</summary>
    [JsonPropertyName("model")] public ModelInfo Model { get; set; } = new();

    [JsonIgnore] public Dictionary<int, PlayerData> PlayerById { get; private set; }
    [JsonIgnore] public Dictionary<string, TeamData> TeamByAbbr { get; private set; }

    public static GameData Parse(string json)
    {
        var d = JsonSerializer.Deserialize<GameData>(json) ?? throw new InvalidDataException("пустая база");
        d.Index();
        return d;
    }

    public static GameData Load(string path) => Parse(File.ReadAllText(path));

    void Index()
    {
        PlayerById = Players.ToDictionary(p => p.Id);
        foreach (var p in Players)
            if (p.A is { Count: > 0 }) p.Roles = HM27.Core.Roles.For(p);   // роли — из характеристик модели игрока
        TeamByAbbr = Teams.ToDictionary(t => t.Abbr);
    }

    public IEnumerable<PlayerData> Roster(string abbr) => Players.Where(p => p.Team == abbr);
}

public sealed class ModelInfo
{
    [JsonPropertyName("built")] public string Built { get; set; }
    [JsonPropertyName("xg")] public string Xg { get; set; }
    /// <summary>Группа → возраст → изменение за сезон (ед. шкалы 1–20).</summary>
    [JsonPropertyName("curves")] public Dictionary<string, Dictionary<string, double>> Curves { get; set; } = new();
    /// <summary>КА из характеристик по позициям (F, D, G): data/ca_formula.py.</summary>
    [JsonPropertyName("ca")] public Dictionary<string, CaFormula> Ca { get; set; }
}

/// <summary>КА = a + b·s + c·max(0, s − s0)², s = Σ w_k·(характеристика_k − 11).</summary>
public sealed class CaFormula
{
    [JsonPropertyName("w")] public Dictionary<string, double> W { get; set; } = new();
    [JsonPropertyName("a")] public double A { get; set; }
    [JsonPropertyName("b")] public double B { get; set; }
    [JsonPropertyName("c")] public double C { get; set; }
    [JsonPropertyName("s0")] public double S0 { get; set; }
    [JsonPropertyName("r2")] public double R2 { get; set; }
    [JsonPropertyName("err")] public double Err { get; set; }

    public double S(Func<string, double> attr) => W.Sum(kv => kv.Value * (attr(kv.Key) - 11));
    public double Of(double s) => A + B * s + C * Math.Pow(Math.Max(0, s - S0), 2);
    /// <summary>Сколько КА даёт +1 к характеристике key в точке s.</summary>
    public double Slope(string key, double s) => W.GetValueOrDefault(key) * (B + 2 * C * Math.Max(0, s - S0));
}

public sealed class LeagueInfo
{
    [JsonPropertyName("goals_pg")] public double GoalsPerGame { get; set; } = 3.05;
}

public sealed class TeamData
{
    [JsonPropertyName("abbr")] public string Abbr { get; set; }
    /// <summary>Город по-русски (из базы).</summary>
    [JsonPropertyName("city")] public string CityRu { get; set; }
    /// <summary>Название по-русски (из базы).</summary>
    [JsonPropertyName("name")] public string NameRuData { get; set; }
    [JsonPropertyName("name_en")] public string NameEn { get; set; }
    /// <summary>Город для подписей — по-английски или по-русски (настройка Names.English).</summary>
    [JsonIgnore] public string City => Names.English ? Names.CityEn(Abbr, NameEn) : CityRu;
    /// <summary>Название для подписей — по-английски или по-русски (настройка Names.English).</summary>
    [JsonIgnore] public string Name => Names.English ? (NameEn ?? NameRuData) : NameRuData;
    [JsonPropertyName("conf")] public string Conference { get; set; }
    [JsonPropertyName("div")] public string Division { get; set; }
    [JsonPropertyName("colors")] public List<string> Colors { get; set; } = new();
    [JsonPropertyName("logo")] public string Logo { get; set; }
    /// <summary>Лог-рейтинг атаки (модель 4.4): + — забивает больше среднего.</summary>
    [JsonPropertyName("att")] public double Att { get; set; }
    /// <summary>Лог-рейтинг обороны: + — пропускает больше среднего (как в model.py).</summary>
    [JsonPropertyName("dfn")] public double Dfn { get; set; }
    [JsonPropertyName("lines")] public LinesData Lines { get; set; } = new();
    [JsonPropertyName("injured")] public List<int> Injured { get; set; } = new();
    /// <summary>История клуба: основание, арена, Кубки Стэнли и Президентские кубки (data/clubs.json).</summary>
    [JsonPropertyName("hist")] public TeamHistory Hist { get; set; }
    [JsonPropertyName("record")] public RecordData Record { get; set; }
}

public sealed class LinesData
{
    [JsonPropertyName("f")] public List<List<int>> F { get; set; } = new();
    [JsonPropertyName("d")] public List<List<int>> D { get; set; } = new();
    [JsonPropertyName("g")] public List<int> G { get; set; } = new();
    [JsonPropertyName("pp1")] public List<int> Pp1 { get; set; } = new();
    [JsonPropertyName("pp2")] public List<int> Pp2 { get; set; } = new();
}

public sealed class RecordData
{
    [JsonPropertyName("gp")] public int Gp { get; set; }
    [JsonPropertyName("w")] public int W { get; set; }
    [JsonPropertyName("l")] public int L { get; set; }
    [JsonPropertyName("otl")] public int Otl { get; set; }
    [JsonPropertyName("pts")] public int Pts { get; set; }
}

public sealed class CareerFacts
{
    [JsonPropertyName("gp")] public int Gp { get; set; }
    [JsonPropertyName("po")] public int Po { get; set; }
    [JsonPropertyName("sea")] public int Seasons { get; set; }
    [JsonPropertyName("clubs")] public int Clubs { get; set; }
    [JsonPropertyName("club_sea")] public int ClubSeasons { get; set; }
    [JsonPropertyName("miss")] public int MissLong { get; set; }
    [JsonPropertyName("span")] public int Span { get; set; }
    [JsonPropertyName("fights")] public double Fights { get; set; }
    /// <summary>Вспыльчивость по данным (z): частота и доля «эмоциональных» удалений — грубость, неспортивное поведение, драки. Больше — горячее.</summary>
    [JsonPropertyName("temper")] public double Temper { get; set; }
    [JsonPropertyName("aw")] public List<string> Awards { get; set; } = new();
    [JsonPropertyName("pick")] public int? Pick { get; set; }
    [JsonPropertyName("draft")] public int? DraftYear { get; set; }
    /// <summary>Капитан (C) или ассистент (A) в сезоне 2026-27.</summary>
    [JsonPropertyName("cap")] public string Cap { get; set; }
    /// <summary>Номинации клуба на Мастертон (упорство, самоотдача) за последние сезоны.</summary>
    [JsonPropertyName("masterton")] public int Masterton { get; set; }
    /// <summary>Номинации клуба на Кинг Клэнси (лидерство, работа с обществом) за последние сезоны.</summary>
    [JsonPropertyName("clancy")] public int Clancy { get; set; }
    /// <summary>Сезонов капитаном («С») с 2010-11 по 2025-26 в любых клубах.</summary>
    [JsonPropertyName("cap_sea")] public int CapSeasons { get; set; }
    /// <summary>Mark Messier Leadership Award (лидерство).</summary>
    [JsonPropertyName("messier")] public int Messier { get; set; }
    /// <summary>Lady Byng (честная игра).</summary>
    [JsonPropertyName("byng")] public int Byng { get; set; }
    /// <summary>Продлевал контракт заметно ниже рынка, чтобы остаться в клубе.</summary>
    [JsonPropertyName("discount")] public int Discount { get; set; }
    /// <summary>Публично просил обмен.</summary>
    [JsonPropertyName("trade_req")] public int TradeRequests { get; set; }
    /// <summary>Доля сыгранных матчей регулярки в завершённых сезонах (от трёх сезонов): «железный человек».</summary>
    [JsonPropertyName("gp82")] public double Gp82 { get; set; }
    /// <summary>Вся карьера в НХЛ — в одном клубе.</summary>
    [JsonPropertyName("one_club")] public bool OneClub { get; set; }
}

public sealed class RoleFit
{
    [JsonPropertyName("n")] public string Name { get; set; }
    [JsonPropertyName("v")] public int V { get; set; }
}

public sealed class PlayerData
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("name_ru")] public string NameRuData { get; set; }
    /// <summary>Имя для подписей: по-английски (как в НХЛ) или по-русски — настройка Names.English.</summary>
    [JsonIgnore] public string NameRu => Names.English ? Name : (NameRuData ?? Name);
    [JsonPropertyName("team")] public string Team { get; set; }
    /// <summary>C, LW, RW, D, G.</summary>
    [JsonPropertyName("pos")] public string Pos { get; set; }
    [JsonPropertyName("num")] public int? Num { get; set; }
    [JsonPropertyName("h")] public int? Height { get; set; }
    [JsonPropertyName("w")] public int? Weight { get; set; }
    [JsonPropertyName("born")] public string Born { get; set; }
    [JsonPropertyName("country")] public string Country { get; set; }
    /// <summary>Путь к фото на assets.nhle.com/mugs/nhl/{head}.png (фото не входят в игру, скачиваются при показе).</summary>
    [JsonPropertyName("head")] public string Head { get; set; }
    /// <summary>Личный xG за матч (полевые).</summary>
    [JsonPropertyName("ixg_pg")] public double IxgPg { get; set; }
    /// <summary>Личный xG за 60 минут, гармонизированный по времени (используется движком).</summary>
    [JsonPropertyName("ixg60")] public double Ixg60 { get; set; }
    /// <summary>Среднее игровое время в реальном сезоне, минут.</summary>
    [JsonPropertyName("toi")] public double Toi { get; set; }
    /// <summary>Реализация: голы / xG, сжатая к 1.</summary>
    [JsonPropertyName("fin")] public double Fin { get; set; } = 1.0;
    /// <summary>Передачи за матч, сжатые к среднему позиции.</summary>
    [JsonPropertyName("apg")] public double Apg { get; set; }
    /// <summary>Вратари: сейвы сверх ожидаемого на единицу xG против (+ — лучше среднего).</summary>
    [JsonPropertyName("gsax")] public double Gsax { get; set; }
    [JsonPropertyName("r")] public Dictionary<string, int> R { get; set; } = new();
    /// <summary>Реальная статистика НХЛ в текущем сезоне на дату базы: gp, g, a, pts, pm, toi; у вратарей gp, w, l, otl, sv, gaa.</summary>
    [JsonPropertyName("stats")] public Dictionary<string, double> Stats { get; set; } = new();
    /// <summary>Сырые показатели для характеристик (за 60 минут, доли, км/ч) — показываются в аналитике.</summary>
    [JsonPropertyName("x")] public Dictionary<string, double?> X { get; set; } = new();
    /// <summary>Пригодность к ролям (1–99), по убыванию.</summary>
    [JsonPropertyName("roles")] public List<RoleFit> Roles { get; set; } = new();
    /// <summary>Характеристики-оценки: нет данных (нет трекинга EDGE, мало вбрасываний и т. п.).</summary>
    [JsonPropertyName("est")] public List<string> Est { get; set; } = new();
    public double Xv(string k) => X != null && X.TryGetValue(k, out var v) && v.HasValue ? v.Value : double.NaN;

    /// <summary>Модель игрока (формат 3): уровень 1–20 по данным и его погрешность.</summary>
    [JsonPropertyName("lvl")] public double[] Lvl { get; set; }
    /// <summary>Характеристики 1–20 по данным: ключ → [значение, погрешность] (см. <see cref="Attrs"/>).</summary>
    [JsonPropertyName("a")] public Dictionary<string, double[]> A { get; set; } = new();
    /// <summary>Факты карьеры для скрытых качеств: матчи, драфт, награды, клубы, пропуски, драки.</summary>
    [JsonPropertyName("fx")] public CareerFacts Fx { get; set; } = new();
    /// <summary>Особенности стиля (коды Traits.All), до трёх: по данным 2022–2026, лучшие 5% своей группы.</summary>
    [JsonPropertyName("tr")] public List<string> Tr { get; set; }
    /// <summary>Х-фактор: характеристика, в которой игрок среди лучших 1% лиги (код характеристики) или null.</summary>
    [JsonPropertyName("xf")] public string Xf { get; set; }
    /// <summary>Реальный сезон 2025-26 (MoneyPuck): gp, g, a — для сверки сезонов игры с реальностью (HM27.Check).</summary>
    [JsonPropertyName("real")] public Dictionary<string, int> Real { get; set; }
    /// <summary>Биография: место рождения, драфт, награды, путь до НХЛ, сезоны в НХЛ, сборная (страница игрока НХЛ).</summary>
    [JsonPropertyName("bio")] public PlayerBio Bio { get; set; }

    /// <summary>Уровень по данным (если модели нет — из старой оценки ovr).</summary>
    [JsonIgnore] public double Level => Lvl is { Length: > 0 } ? Lvl[0] : Math.Clamp(R.GetValueOrDefault("ovr", 50) / 5.0, 1, 20);
    [JsonIgnore] public double LevelSe => Lvl is { Length: > 1 } ? Lvl[1] : 3.0;
    /// <summary>Текущие способности (КА) 1–200 по данным, как в FM: линейно по силе; средний игрок НХЛ ≈ 120, лучший ≈ 190.</summary>
    [JsonPropertyName("ca")] public double[] CaData { get; set; }
    [JsonIgnore] public double CaEst => CaData is { Length: > 0 } ? CaData[0] : 120 + (Level - 11) * PlayerModel.CA_PER_LVL;
    [JsonIgnore] public double CaSe => CaData is { Length: > 1 } ? CaData[1] : 18;
    /// <summary>Характеристика по данным; если нет — уровень игрока (с большой погрешностью).</summary>
    public double AttrEst(string k) => A != null && A.TryGetValue(k, out var v) && v.Length > 0 ? v[0] : Level;
    public double AttrSe(string k) => A != null && A.TryGetValue(k, out var v) && v.Length > 1 ? v[1] : 3.0;

    [JsonIgnore] public bool IsGoalie => Pos == "G";
    [JsonIgnore] public bool IsDefense => Pos == "D";
    [JsonIgnore] public bool IsForward => Pos is "C" or "LW" or "RW";
    /// <summary>
    /// Общая оценка 0–100 по нашей модели (КА из данных): звёзды = Ovr / 20, как везде в игре (КА 80 → 0, КА 180 → 5 звёзд).
    /// Старый рейтинг «Сводки» (r.ovr) больше нигде не показывается — он расходился с КА.
    /// </summary>
    [JsonIgnore] public int Ovr => (int)Math.Round(Math.Clamp(CaEst - 80, 1, 100));
    [JsonIgnore] public int OvrOld => R.TryGetValue("ovr", out var v) ? v : 50;
    public int Rating(string key) => R.TryGetValue(key, out var v) ? v : 50;

    public int? Age(DateOnly on)
    {
        if (!DateOnly.TryParse(Born, out var b)) return null;
        int a = on.Year - b.Year;
        if (on < b.AddYears(a)) a--;
        return a;
    }

    /// <summary>Фамилия по-русски для коротких подписей.</summary>
    [JsonIgnore] public string LastRu
    {
        get
        {
            var n = NameRu ?? Name ?? "";
            var i = n.IndexOf(' ');
            return i > 0 ? n[(i + 1)..] : n;
        }
    }
}

/// <summary>История клуба (для карточки клуба).</summary>
public sealed class TeamHistory
{
    [JsonPropertyName("arena")] public string Arena { get; set; }
    [JsonPropertyName("capacity")] public int Capacity { get; set; }
    [JsonPropertyName("founded")] public int Founded { get; set; }
    [JsonPropertyName("joined")] public int? Joined { get; set; }
    /// <summary>Годы побед в Кубке Стэнли (год финала).</summary>
    [JsonPropertyName("cups")] public List<int> Cups { get; set; } = new();
    /// <summary>Президентский кубок (год окончания сезона).</summary>
    [JsonPropertyName("presidents")] public List<int> Presidents { get; set; } = new();
    /// <summary>Прежние названия и переезды.</summary>
    [JsonPropertyName("former")] public string Former { get; set; }
}

/// <summary>Биография игрока со страницы НХЛ. Сезоны — восьмизначные (20252026).</summary>
public sealed class PlayerBio
{
    [JsonPropertyName("city")] public string City { get; set; }
    [JsonPropertyName("prov")] public string Prov { get; set; }
    /// <summary>Награды: [название (англ.), [сезоны]].</summary>
    [JsonPropertyName("aw")] public List<List<JsonElement>> Awards { get; set; } = new();
    /// <summary>Регулярка НХЛ по сезонам: полевые [сезон, клуб, И, Г, П, О, Штр, +/−]; вратари [сезон, клуб, И, В, КН, %ОБ, «на ноль»].</summary>
    [JsonPropertyName("nhl")] public List<List<JsonElement>> Nhl { get; set; } = new();
    /// <summary>Путь до НХЛ: [сезон, лига, клуб, И, О].</summary>
    [JsonPropertyName("pre")] public List<List<JsonElement>> Pre { get; set; } = new();
    /// <summary>Сезоны в других лигах после дебюта в НХЛ (АХЛ, Европа): [сезон, лига, клуб, И, О].</summary>
    [JsonPropertyName("minor")] public List<List<JsonElement>> Minor { get; set; } = new();
    /// <summary>Сборная: турнир (wc, wjc, u18, u17, og, wcup, 4n) → сезоны.</summary>
    [JsonPropertyName("intl")] public Dictionary<string, List<int>> Intl { get; set; } = new();
    /// <summary>Драфт: [год, клуб, раунд, общий номер].</summary>
    [JsonPropertyName("draft")] public List<JsonElement> Draft { get; set; }
    /// <summary>Плей-офф за карьеру: полевые [И, Г, П, О]; вратари [И, В, КН, %ОБ].</summary>
    [JsonPropertyName("po")] public List<double> Po { get; set; }
    [JsonPropertyName("hof")] public int Hof { get; set; }
    [JsonPropertyName("top100")] public int Top100 { get; set; }
}

/// <summary>Язык имён игроков и названий клубов (настройка игры; остальной текст — по-русски).</summary>
public static class Names
{
    public static bool English = true;

    static readonly Dictionary<string, string> Cities = new()
    {
        ["ANA"] = "Anaheim", ["BOS"] = "Boston", ["BUF"] = "Buffalo", ["CGY"] = "Calgary", ["CAR"] = "Carolina", ["CHI"] = "Chicago",
        ["COL"] = "Colorado", ["CBJ"] = "Columbus", ["DAL"] = "Dallas", ["DET"] = "Detroit", ["EDM"] = "Edmonton", ["FLA"] = "Florida",
        ["LAK"] = "Los Angeles", ["MIN"] = "Minnesota", ["MTL"] = "Montreal", ["NSH"] = "Nashville", ["NJD"] = "New Jersey",
        ["NYI"] = "NY Islanders", ["NYR"] = "NY Rangers", ["OTT"] = "Ottawa", ["PHI"] = "Philadelphia", ["PIT"] = "Pittsburgh",
        ["SJS"] = "San Jose", ["SEA"] = "Seattle", ["STL"] = "St. Louis", ["TBL"] = "Tampa Bay", ["TOR"] = "Toronto", ["UTA"] = "Utah",
        ["VAN"] = "Vancouver", ["VGK"] = "Vegas", ["WSH"] = "Washington", ["WPG"] = "Winnipeg",
    };

    /// <summary>Короткое имя клуба по-английски («Edmonton», «NY Rangers»).</summary>
    public static string CityEn(string abbr, string nameEn) => abbr != null && Cities.TryGetValue(abbr, out var c) ? c : nameEn ?? abbr;
}
