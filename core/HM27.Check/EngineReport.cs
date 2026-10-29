using System.Text.Json;
using HM27.Core;

/// <summary>Сверка движка по сменам с целями НХЛ (data/engine_targets.json): dotnet run -- база.json engine [сезонов]</summary>
static class EngineReport
{
    public static void Run(GameData data, int seasons)
    {
        var tp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(Environment.GetCommandLineArgs().Length > 1 ? Environment.GetCommandLineArgs()[1] : "game/data/hm27-data.json"))!, "..", "..", "data", "engine_targets.json");
        if (!File.Exists(tp)) tp = "data/engine_targets.json";
        var tg = JsonDocument.Parse(File.ReadAllText(tp)).RootElement;
        var D = ShiftEngine.Diag = new ShiftEngine.Diagnostics();
        var all = new List<Career>();
        for (int s = 0; s < seasons; s++)
        {
            var c = Career.New(data, "MIN", 5000 + s);
            // только регулярка
            while (c.Phase == Phase.Regular) c.SimDay();
            all.Add(c);
        }
        int games = D.Games, tgames = 2 * games;
        Console.WriteLine($"Матчей: {games}");
        Console.WriteLine($"{"состав",-11} {"мин/к",7} {"(НХЛ)",7} {"г60",6} {"(НХЛ)",6} {"поп60",6} {"(НХЛ)",6} {"блок",5} {"(НХЛ)",5} {"мимо",5} {"(НХЛ)",5} {"сх%",5} {"(НХЛ)",5}");
        foreach (var k in new[] { "5v5", "pp", "pk", "4v4", "3v3", "en_against", "en_for" })
        {
            var t = tg.GetProperty("states").GetProperty(k);
            double sec = D.Sec.GetValueOrDefault(k), g = D.Goals.GetValueOrDefault(k), at = D.Att.GetValueOrDefault(k), sog = D.Sog.GetValueOrDefault(k);
            Console.WriteLine($"{k,-11} {sec / tgames / 60,7:F2} {t.GetProperty("min_per_team_game").GetDouble(),7:F2} {(sec > 0 ? 3600 * g / sec : 0),6:F2} {t.GetProperty("gf60").GetDouble(),6:F2} " +
                $"{(sec > 0 ? 3600 * at / sec : 0),6:F1} {t.GetProperty("cf60").GetDouble(),6:F1} {(at > 0 ? D.Blk.GetValueOrDefault(k) / at : 0),5:F3} {t.GetProperty("block_share").GetDouble(),5:F3} " +
                $"{(at > 0 ? D.Miss.GetValueOrDefault(k) / at : 0),5:F3} {t.GetProperty("miss_share").GetDouble(),5:F3} {(sog > 0 ? g / sog : 0),5:F3} {t.GetProperty("sh_pct").GetDouble(),5:F3}");
        }
        var res = all.SelectMany(c => c.Games.Where(g => !g.Playoff && g.Played)).Select(g => g.Result).ToList();
        double gpg = res.Average(r => r.HomeGoals + r.AwayGoals - (r.End == "SO" ? 1 : 0));
        Console.WriteLine($"Голов за матч {gpg:F3} (НХЛ {tg.GetProperty("goals_per_game").GetDouble():F3})");
        Console.WriteLine($"Большинств на команду {D.PpEntries / (double)tgames:F2} (НХЛ {tg.GetProperty("pp_per_team_game").GetDouble():F2}), реализация {D.Goals.GetValueOrDefault("pp") / Math.Max(1, D.PpEntries):P1} (НХЛ {tg.GetProperty("pp_pct").GetDouble():P1})");
        double otShare = res.Count(r => r.End != "REG") / (double)res.Count, soShare = res.Count(r => r.End == "SO") / (double)Math.Max(1, res.Count(r => r.End != "REG"));
        Console.WriteLine($"ОТ {otShare:P1} (НХЛ {tg.GetProperty("ot_share").GetDouble():P1}), из них буллиты {soShare:P1} (НХЛ {tg.GetProperty("so_share_of_ot").GetDouble():P1})");
        var noSo = res.Where(r => r.End != "SO").ToList();
        Console.WriteLine($"Победы хозяев без буллитов {noSo.Count(r => r.HomeGoals > r.AwayGoals) / (double)noSo.Count:P1} (НХЛ {tg.GetProperty("home_win_excl_so").GetDouble():P1})");
        var reg = res.Where(r => r.End == "REG").ToList();
        var marg = tg.GetProperty("reg_margin");
        Console.WriteLine("Разница в основное время: " + string.Join("  ", Enumerable.Range(1, 6).Select(m => $"{m}: {reg.Count(r => Math.Min(6, Math.Abs(r.HomeGoals - r.AwayGoals)) == m) / (double)reg.Count:P1} ({marg.GetProperty(m.ToString()).GetDouble():P1})")));
        Console.WriteLine($"Броски в створ на команду {res.Average(r => (r.HomeShots + r.AwayShots) / 2.0):F1}");
        // эффект счёта
        var se = tg.GetProperty("score_effects_5v5");
        Console.WriteLine("Эффект счёта (доля попыток, 5×5): " + string.Join("  ", Enumerable.Range(-3, 7).Select(df =>
        {
            double f = D.ScoreSecAtt.GetValueOrDefault(df), a = D.ScoreSecAtt.GetValueOrDefault(-df);
            return $"{df:+0;-0;0}: {f / Math.Max(1, f + a):P1} ({se.GetProperty(df.ToString()).GetProperty("cf_share").GetDouble():P1})";
        })));
        Console.WriteLine($"Добивания: {D.Rebounds} попыток, реализация {D.RebGoals / Math.Max(1.0, D.Rebounds):P1} (НХЛ 10,7%)");
        // голы по периодам и передачи
        var goals = res.SelectMany(r => r.Goals.Where(e => e.Kind != "so")).ToList();
        var gp = tg.GetProperty("goals_by_period");
        Console.WriteLine("Голы по периодам: " + string.Join("  ", new[] { 1, 2, 3, 4 }.Select(p => $"{p}: {goals.Count(e => Math.Min(4, e.Period) == p) / (double)res.Count:F2} ({gp.GetProperty(p.ToString()).GetDouble():F2})")));
        var asst = tg.GetProperty("assists");
        Console.WriteLine("Передач на гол: " + string.Join("  ", new[] { 0, 1, 2 }.Select(n => $"{n}: {goals.Count(e => (e.A1 != 0 ? 1 : 0) + (e.A2 != 0 ? 1 : 0) == n) / (double)goals.Count:P1} ({asst.GetProperty(n.ToString()).GetDouble():P1})")));
        // минуты по рангу в составе и смены (по протоколам своих матчей)
        var mine = all.SelectMany(c => c.Games.Where(g => g.Played && !g.Playoff && g.Result.Box.Count > 0).Select(g => (c, g))).ToList();
        foreach (var (pos, n) in new[] { ("F", 12), ("D", 6) })
        {
            var ranks = new double[n]; int cnt = 0; double shifts = 0, toiAll = 0; int pl = 0;
            foreach (var (c, g) in mine)
                foreach (var dressed in new[] { g.Result.HomeDressed, g.Result.AwayDressed })
                {
                    var tois = dressed.Where(id => data.PlayerById.TryGetValue(id, out var p) && !p.IsGoalie && (pos == "D") == p.IsDefense && g.Result.Box.ContainsKey(id))
                        .Select(id => g.Result.Box[id]).ToList();
                    if (tois.Count < n) continue;
                    var sorted = tois.Select(b => b.Toi / 60.0).OrderByDescending(x => x).ToList();
                    for (int i = 0; i < n; i++) ranks[i] += sorted[i];
                    cnt++;
                    foreach (var b in tois) { shifts += b.Shifts; toiAll += b.Toi; pl++; }
                }
            var real = tg.GetProperty("toi_by_rank").GetProperty(pos).EnumerateArray().Select(x => x.GetDouble()).ToList();
            Console.WriteLine($"Минуты {pos}: " + string.Join(" ", Enumerable.Range(0, n).Select(i => $"{ranks[i] / Math.Max(1, cnt):F1}({real[i]:F1})")));
            Console.WriteLine($"Смены {pos}: {shifts / Math.Max(1, pl):F1} за матч (НХЛ {tg.GetProperty("shifts_per_game").GetProperty(pos).GetDouble():F1}), средняя {toiAll / Math.Max(1, shifts):F1} с (НХЛ {tg.GetProperty("shift_sec").GetProperty(pos).GetProperty("mean").GetDouble():F1})");
        }
        // распределение игроков за сезон
        var sp = tg.GetProperty("season_players");
        double R(string k) => sp.EnumerateObject().Average(x => x.Value.GetProperty(k).GetDouble());
        var rows = all.Select(c =>
        {
            var g = c.Skaters.Values.Select(x => x.G).OrderByDescending(x => x).ToList();
            var p = c.Skaters.Values.Select(x => x.Pts).OrderByDescending(x => x).ToList();
            return (g1: g[0], g10: g.Take(10).Average(), g30: g.Count(x => x >= 30), g40: g.Count(x => x >= 40), g50: g.Count(x => x >= 50),
                    p1: p[0], p10: p.Take(10).Average(), p60: p.Count(x => x >= 60), p80: p.Count(x => x >= 80), p100: p.Count(x => x >= 100));
        }).ToList();
        Console.WriteLine($"Игроки за сезон (игра / НХЛ 2022-26): лидер по голам {rows.Average(x => x.g1):F0}/{R("goals_top1"):F0}, топ-10 {rows.Average(x => x.g10):F1}/{R("goals_top10"):F1}, " +
            $"30+ {rows.Average(x => x.g30):F0}/{R("g30"):F0}, 40+ {rows.Average(x => x.g40):F0}/{R("g40"):F0}, 50+ {rows.Average(x => x.g50):F1}/{R("g50"):F1}; " +
            $"лидер по очкам {rows.Average(x => x.p1):F0}/{R("pts_top1"):F0}, топ-10 {rows.Average(x => x.p10):F1}/{R("pts_top10"):F1}, 60+ {rows.Average(x => x.p60):F0}/{R("p60"):F0}, 80+ {rows.Average(x => x.p80):F0}/{R("p80"):F0}, 100+ {rows.Average(x => x.p100):F1}/{R("p100"):F1}");
        var sogRows = all.Select(c =>
        {
            var v = c.Skaters.Select(kv => (s: kv.Value.S, d: data.PlayerById[kv.Key].IsDefense)).ToList();
            var all2 = v.Select(x => x.s).OrderByDescending(x => x).ToList(); var dd = v.Where(x => x.d).Select(x => x.s).OrderByDescending(x => x).ToList();
            return (t1: all2[0], t10: all2.Take(10).Average(), n250: all2.Count(x => x >= 250), n200: all2.Count(x => x >= 200), d1: dd[0], d5: dd.Take(5).Average());
        }).ToList();
        Console.WriteLine($"Броски в створ за сезон (игра / НХЛ 2023-26): лидер {sogRows.Average(x => x.t1):F0}/359, топ-10 {sogRows.Average(x => x.t10):F0}/305, 250+ {sogRows.Average(x => x.n250):F0}/18, 200+ {sogRows.Average(x => x.n200):F0}/62, " +
            $"защитник-лидер {sogRows.Average(x => x.d1):F0}/275, топ-5 защитников {sogRows.Average(x => x.d5):F0}/239");
        Console.WriteLine("Снятый вратарь (сек на матч / голы за / против на матч; НХЛ: −1: 24,8/0,058/0,163, −2: 23,9/0,053/0,171, −3: 7,6/0,02/0,041): " +
            string.Join("  ", new[] { -1, -2, -3 }.Select(df => $"{df}: {D.PullSec.GetValueOrDefault(df) / games:F1}/{D.PullGf.GetValueOrDefault(df) / games:F3}/{D.PullGa.GetValueOrDefault(df) / games:F3}")));
        Console.WriteLine("Доля защитников (НХЛ: голы 14,8%, 1-я передача 26,4%, 2-я 36,7%): " + string.Join("  ", new[] { "g", "a1", "a2" }.Select(k =>
            $"{k}: {D.RoleShare.GetValueOrDefault(k + "_D") / Math.Max(1, D.RoleShare.GetValueOrDefault(k + "_D") + D.RoleShare.GetValueOrDefault(k + "_F")):P1}")));
        // 5×5 на льду по рангу минут в клубе (как в протоколах НХЛ 2024-26: нападающие тройками, защитники парами)
        var realF = new[] { (2.88, 2.55), (2.66, 2.51), (2.34, 2.45), (2.17, 2.46) }; var realD = new[] { (2.60, 2.48), (2.53, 2.48), (2.43, 2.52), (2.22, 2.58) };
        foreach (var (pos, size, real) in new[] { ("F", 3, realF), ("D", 2, realD) })
        {
            var b = new double[4, 3];
            foreach (var team in data.Teams)
            {
                var ids = D.Es5Sec.Keys.Where(id => data.PlayerById.TryGetValue(id, out var p) && p.Team == team.Abbr && !p.IsGoalie && (pos == "D") == p.IsDefense)
                    .OrderByDescending(id => D.Es5Sec[id]).ToList();
                for (int i = 0; i < ids.Count; i++)
                {
                    int k = i / size; if (k > 3) { if (pos == "D") k = 3; else continue; }
                    b[k, 0] += D.Es5Sec[ids[i]]; b[k, 1] += D.Es5Gf.GetValueOrDefault(ids[i]); b[k, 2] += D.Es5Ga.GetValueOrDefault(ids[i]);
                }
            }
            Console.WriteLine($"5×5 на льду, {pos} по рангу: " + string.Join("  ", Enumerable.Range(0, 4).Select(k => $"{3600 * b[k, 1] / b[k, 0]:F2}/{3600 * b[k, 2] / b[k, 0]:F2} ({real[k].Item1:F2}/{real[k].Item2:F2})")));
        }
        // лучшие бомбардиры: игра против реального сезона, IPP (доля очков от голов команды при нём на льду)
        Console.WriteLine("Лучшие по очкам (среднее за сезон в игре; реальный 2025-26 на 82 матча):");
        var agg = all.SelectMany(c => c.Skaters).GroupBy(kv => kv.Key).Select(g => (id: g.Key, g: g.Average(x => (double)x.Value.G), a: g.Average(x => (double)x.Value.A),
            gp: g.Average(x => (double)x.Value.Gp), toi: g.Sum(x => (double)x.Value.Toi) / Math.Max(1, g.Sum(x => x.Value.Gp)) / 60)).OrderByDescending(x => x.g + x.a).Take(15).ToList();
        foreach (var x in agg)
        {
            var p = data.PlayerById[x.id];
            double ongf = D.OnGf.GetValueOrDefault(x.id) / seasons;
            string real = p.Real != null && p.Real.GetValueOrDefault("gp") > 0 ? $"{82.0 * p.Real["g"] / p.Real["gp"]:F0}+{82.0 * p.Real["a"] / p.Real["gp"]:F0}" : "—";
            Console.WriteLine($"  {p.NameRu,-24} {p.Team} {x.g:F0}+{x.a:F0}={x.g + x.a:F0} за {x.gp:F0} м., {x.toi:F1} мин, IPP {(x.g + x.a) / Math.Max(1, ongf):P0}; реально {real}");
        }
        TraitReport(data, all, D, tp);
        var fo = FaceoffMirror(data, all);
        Console.WriteLine($"Вбрасывания: {fo.perGame:F1} за матч (НХЛ 56,3); у центров (300+): разброс % {fo.sd:F3} (НХЛ 0,051), r с реальным % {fo.r:F2}, средняя разница {fo.diff:+0.000}");
        Console.WriteLine($"За матч (игра / НХЛ): силовые {all.Sum(c => c.Skaters.Values.Sum(x => (double)x.Hit)) / games:F1}/43,6, отборы {all.Sum(c => c.Skaters.Values.Sum(x => (double)x.Tk)) / games:F1}/11,9, потери {all.Sum(c => c.Skaters.Values.Sum(x => (double)x.Gv)) / games:F1}/23,4");
        // таблица
        var pts = all.Select(c => c.Table.Values.Select(r => (double)r.Pts).OrderByDescending(x => x).ToList()).ToList();
        Console.WriteLine($"Таблица: лидер {pts.Average(p => p[0]):F0}, последний {pts.Average(p => p[^1]):F0}, разброс {pts.Average(p => Sd(p)):F1} (НХЛ: 112–130, 46–60, 13–18,6)");
        ShiftEngine.Diag = null;
    }

    /// <summary>Особенности в игре: отношение «обладатели / остальные» по тому же показателю, что в реальных протоколах.</summary>
    public static List<(string key, double real, double sim, int n)> TraitSignatures(GameData data, List<Career> all, ShiftEngine.Diagnostics D, string targetsPath)
    {
        var res = new List<(string, double, double, int)>();
        var tt = Path.Combine(Path.GetDirectoryName(targetsPath)!, "trait_targets.json");
        if (!File.Exists(tt)) return res;
        var tg = JsonDocument.Parse(File.ReadAllText(tt)).RootElement;
        var minDen = new Dictionary<string, double> { ["unb"] = 120, ["sog"] = 80, ["att"] = 160 };
        foreach (var row in tg.EnumerateObject())
        {
            if (!row.Value.TryGetProperty("trait", out _)) continue;
            var v = row.Value; string tr = v.GetProperty("trait").GetString(), grp = v.GetProperty("group").GetString(), num = v.GetProperty("num").GetString(), den = v.GetProperty("den").GetString();
            double[] have = new double[2], other = new double[2]; int n = 0;
            var players = data.Players.Where(p => !p.IsGoalie && p.IsDefense == (grp == "D")).ToList();
            if (den == "60")
            {
                // за 60 минут на льду: попытки или блоки за сезоны игры
                var hv = new List<double>(); var ov = new List<double>();
                foreach (var p in players)
                {
                    double toi = all.Sum(c => (double)(c.Skaters.GetValueOrDefault(p.Id)?.Toi ?? 0)); if (toi < 600 * 60) continue;
                    double cnt = all.Sum(c => (double)(num == "sa_60" ? c.Skaters.GetValueOrDefault(p.Id)?.Att ?? 0 : c.Skaters.GetValueOrDefault(p.Id)?.Blk ?? 0));
                    (p.Tr != null && p.Tr.Contains(tr) ? hv : ov).Add(cnt / toi * 3600);
                }
                if (hv.Count >= 3) res.Add((row.Name, v.GetProperty("ratio").GetDouble(), hv.Average() / ov.Average(), hv.Count));
                continue;
            }
            foreach (var p in players)
            {
                if (!D.Sig.TryGetValue(p.Id, out var sg) || sg.GetValueOrDefault(den) < minDen[den]) continue;
                bool h = p.Tr != null && p.Tr.Contains(tr);
                var b = h ? have : other; b[0] += sg.GetValueOrDefault(num); b[1] += sg.GetValueOrDefault(den); if (h) n++;
            }
            if (n >= 3 && have[1] > 0 && other[1] > 0 && other[0] > 0) res.Add((row.Name, v.GetProperty("ratio").GetDouble(), have[0] / have[1] / (other[0] / other[1]), n));
        }
        return res;
    }

    /// <summary>Вбрасывания: сколько за матч, разброс процента у центров и совпадение с реальным процентом (база, fo_pct).</summary>
    public static (double perGame, double sd, double r, double diff) FaceoffMirror(GameData data, List<Career> all)
    {
        int games = all.Sum(c => c.Games.Count(g => g.Played && !g.Playoff));
        double total = all.Sum(c => c.Skaters.Values.Sum(x => (double)x.Fow));
        var rows = new List<(double sim, double real)>();
        foreach (var p in data.Players.Where(p => p.Pos == "C" && p.X != null && p.X.GetValueOrDefault("fo_pct") != null))
        {
            double w = all.Sum(c => (double)(c.Skaters.GetValueOrDefault(p.Id)?.Fow ?? 0)), n = w + all.Sum(c => (double)(c.Skaters.GetValueOrDefault(p.Id)?.Fol ?? 0));
            if (n >= 300 * all.Count / 1.0 && p.X.TryGetValue("fo_n", out var fn) && fn >= 300) rows.Add((w / n, p.X["fo_pct"].Value));
        }
        double m = rows.Average(x => x.sim), sd = Math.Sqrt(rows.Average(x => (x.sim - m) * (x.sim - m)));
        double mr = rows.Average(x => x.real), cov = rows.Average(x => (x.sim - m) * (x.real - mr)), sdr = Math.Sqrt(rows.Average(x => (x.real - mr) * (x.real - mr)));
        return (total / games, sd, cov / (sd * sdr), rows.Average(x => x.sim - x.real));
    }

    static void TraitReport(GameData data, List<Career> all, ShiftEngine.Diagnostics D, string tp)
    {
        var rows = TraitSignatures(data, all, D, tp);
        Console.WriteLine("Особенности в игре (обладатели / остальные; игра — НХЛ): " + string.Join("  ", rows.Select(r => $"{r.key} ×{r.sim:F2}—×{r.real:F2}")));
        var f = Finishing(data, D);
        var real = Real(tp);
        Console.WriteLine($"Реализация 5×5 (регрессия по оценкам из базы; игра — НХЛ): завершение {f.fin:+0.000}—{real?.fin:+0.000}, дальний бросок издали {f.lng:+0.000}—{real?.lng:+0.000}, топ-3% по завершению ×{f.top3:F2}—×{real?.top3:F2}");
    }

    public static (double fin, double lng, double top3)? Real(string tp)
    {
        var tt = Path.Combine(Path.GetDirectoryName(tp)!, "trait_targets.json");
        if (!File.Exists(tt)) return null;
        var f = JsonDocument.Parse(File.ReadAllText(tt)).RootElement.GetProperty("finishing");
        return (f.GetProperty("fin").GetDouble(), f.GetProperty("lng_far").GetDouble(), f.GetProperty("top3_ratio").GetDouble());
    }

    /// <summary>Та же пуассоновская регрессия, что в data/trait_targets.py: голы ~ зона + защитник + завершение + дальний бросок (издали).</summary>
    public static (double fin, double lng, double top3) Finishing(GameData data, ShiftEngine.Diagnostics D)
    {
        var rows = new List<(int id, double[] x, double y, double n)>();
        foreach (var (id, sg) in D.Sig)
        {
            if (!data.PlayerById.TryGetValue(id, out var p) || p.IsGoalie || p.A == null || !p.A.ContainsKey("fin") || !p.A.ContainsKey("lng")) continue;
            double fin = p.AttrEst("fin") - 11, lng = p.AttrEst("lng") - 11;
            string[] zs = { "close", "mid", "far" };
            for (int zi = 0; zi < 3; zi++)
            {
                double n = sg.GetValueOrDefault(zs[zi] + "_n5"); if (n <= 0) continue;
                rows.Add((id, new[] { 1, zi == 1 ? 1 : 0, zi == 2 ? 1 : 0, p.IsDefense ? 1 : 0, fin, zi == 2 ? lng : 0 }, sg.GetValueOrDefault(zs[zi] + "_g5"), n));
            }
        }
        int k = 6; var b = new double[k];
        b[0] = Math.Log(rows.Sum(r => r.y) / rows.Sum(r => r.n));
        for (int it = 0; it < 60; it++)
        {
            var M = new double[k, k + 1];
            foreach (var r in rows)
            {
                double eta = 0; for (int j = 0; j < k; j++) eta += r.x[j] * b[j];
                double mu = r.n * Math.Exp(eta), z = eta + (r.y - mu) / Math.Max(mu, 1e-9);
                for (int i = 0; i < k; i++) { for (int j = 0; j < k; j++) M[i, j] += mu * r.x[i] * r.x[j]; M[i, k] += mu * r.x[i] * z; }
            }
            for (int i = 0; i < k; i++) M[i, i] += 1e-6;
            // решение методом Гаусса
            for (int c = 0; c < k; c++)
            {
                int piv = c; for (int i = c + 1; i < k; i++) if (Math.Abs(M[i, c]) > Math.Abs(M[piv, c])) piv = i;
                for (int j = 0; j <= k; j++) (M[c, j], M[piv, j]) = (M[piv, j], M[c, j]);
                for (int i = 0; i < k; i++) if (i != c) { double f = M[i, c] / M[c, c]; for (int j = c; j <= k; j++) M[i, j] -= f * M[c, j]; }
            }
            for (int i = 0; i < k; i++) b[i] = M[i, k] / M[i, i];
        }
        var fins = rows.Select(r => data.PlayerById[r.id].AttrEst("fin")).ToList();
        var uniq = rows.Select(r => r.id).Distinct().Select(id => data.PlayerById[id].AttrEst("fin")).OrderBy(x => x).ToList();
        double thr = uniq[(int)(0.97 * (uniq.Count - 1))];
        double ys = 0, ms = 0;
        for (int i = 0; i < rows.Count; i++)
            if (fins[i] >= thr) { double eta = 0; for (int j = 0; j < k; j++) eta += rows[i].x[j] * b[j]; ys += rows[i].y; ms += rows[i].n * Math.Exp(eta); }
        return (b[4], b[5], ys / Math.Max(1e-9, ms));
    }

    static double Sd(List<double> v) { double m = v.Average(); return Math.Sqrt(v.Average(x => (x - m) * (x - m))); }
}
