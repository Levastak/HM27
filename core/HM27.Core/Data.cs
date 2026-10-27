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
        TeamByAbbr = Teams.ToDictionary(t => t.Abbr);
    }

    public IEnumerable<PlayerData> Roster(string abbr) => Players.Where(p => p.Team == abbr);
}

public sealed class LeagueInfo
{
    [JsonPropertyName("goals_pg")] public double GoalsPerGame { get; set; } = 3.05;
}

public sealed class TeamData
{
    [JsonPropertyName("abbr")] public string Abbr { get; set; }
    [JsonPropertyName("city")] public string City { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("name_en")] public string NameEn { get; set; }
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

public sealed class PlayerData
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("name_ru")] public string NameRu { get; set; }
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

    [JsonIgnore] public bool IsGoalie => Pos == "G";
    [JsonIgnore] public bool IsDefense => Pos == "D";
    [JsonIgnore] public bool IsForward => Pos is "C" or "LW" or "RW";
    [JsonIgnore] public int Ovr => R.TryGetValue("ovr", out var v) ? v : 50;
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
