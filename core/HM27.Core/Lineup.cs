namespace HM27.Core;

/// <summary>
/// Состав на матч: 4 тройки (ЛН, Ц, ПН), 3 пары защиты, основной и запасной вратарь, 2 звена большинства.
/// Минуты по звеньям — как у тренеров НХЛ; сумма по каждой позиции — 60 минут.
/// </summary>
public sealed class Lineup
{
    public List<int[]> F { get; set; } = new();
    public List<int[]> D { get; set; } = new();
    public int G1 { get; set; }
    public int G2 { get; set; }
    public int[] Pp1 { get; set; } = new int[5];
    public int[] Pp2 { get; set; } = new int[5];
    /// <summary>Звенья меньшинства: 2 нападающих + 2 защитника.</summary>
    public int[] Pk1 { get; set; } = new int[4];
    public int[] Pk2 { get; set; } = new int[4];
    /// <summary>Темп игры: −1 закрытый (−7% голов у обеих команд), 0 обычный, +1 открытый (+7%).</summary>
    public int Tempo { get; set; }
    /// <summary>Снимать вратаря раньше обычного, когда проигрываем 1–2 шайбы в концовке.</summary>
    public bool EarlyPull { get; set; }
    public double[] FMin { get; set; } = { 20, 17, 14, 9 };
    public double[] DMin { get; set; } = { 24, 20, 16 };
    /// <summary>Во втором матче за два дня в воротах запасной.</summary>
    public bool BackupOnB2B { get; set; } = true;

    public static readonly string[] FSlotNames = { "ЛН", "Ц", "ПН" };
    public static readonly string[] DSlotNames = { "ЛЗ", "ПЗ" };

    public Lineup Clone() => new()
    {
        F = F.Select(a => (int[])a.Clone()).ToList(),
        D = D.Select(a => (int[])a.Clone()).ToList(),
        G1 = G1, G2 = G2, Pp1 = (int[])Pp1.Clone(), Pp2 = (int[])Pp2.Clone(), Pk1 = (int[])Pk1.Clone(), Pk2 = (int[])Pk2.Clone(),
        Tempo = Tempo, EarlyPull = EarlyPull,
        FMin = (double[])FMin.Clone(), DMin = (double[])DMin.Clone(), BackupOnB2B = BackupOnB2B,
    };

    public IEnumerable<int> Skaters => F.SelectMany(x => x).Concat(D.SelectMany(x => x)).Where(id => id != 0);
    public IEnumerable<int> All => Skaters.Append(G1).Append(G2).Where(id => id != 0);

    /// <summary>Минуты игрока в этом составе (сумма по всем его местам, без спецбригад).</summary>
    public double MinutesOf(int id)
    {
        double m = 0;
        for (int i = 0; i < F.Count; i++) if (F[i].Contains(id)) m += FMin[i];
        for (int i = 0; i < D.Count; i++) if (D[i].Contains(id)) m += DMin[i];
        return m;
    }

    /// <summary>Номер звена (0–3 тройки, 10–12 пары) — для передач «внутри звена».</summary>
    public int UnitOf(int id)
    {
        for (int i = 0; i < F.Count; i++) if (F[i].Contains(id)) return i;
        for (int i = 0; i < D.Count; i++) if (D[i].Contains(id)) return 10 + i;
        return -1;
    }

    /// <summary>
    /// Состав из данных клуба с проверкой: только свои, здоровые, без повторов; пустые места
    /// добираются лучшими свободными по позиции. Так получается состав по умолчанию у всех 32 клубов.
    /// </summary>
    public static Lineup FromTeam(GameData data, TeamData team, ISet<int> unavailable)
    {
        var l = new Lineup();
        var used = new HashSet<int>();
        bool Ok(int id) => id != 0 && data.PlayerById.TryGetValue(id, out var p) && p.Team == team.Abbr && !unavailable.Contains(id) && !used.Contains(id);

        for (int i = 0; i < 4; i++)
        {
            var src = i < team.Lines.F.Count ? team.Lines.F[i] : new List<int>();
            var line = new int[3];
            for (int s = 0; s < 3; s++)
                if (s < src.Count && Ok(src[s]) && !data.PlayerById[src[s]].IsGoalie) { line[s] = src[s]; used.Add(src[s]); }
            l.F.Add(line);
        }
        for (int i = 0; i < 3; i++)
        {
            var src = i < team.Lines.D.Count ? team.Lines.D[i] : new List<int>();
            var pair = new int[2];
            for (int s = 0; s < 2; s++)
                if (s < src.Count && Ok(src[s]) && !data.PlayerById[src[s]].IsGoalie) { pair[s] = src[s]; used.Add(src[s]); }
            l.D.Add(pair);
        }
        var gs = team.Lines.G.Where(Ok).ToList();
        foreach (var g in gs) used.Add(g);
        l.G1 = gs.Count > 0 ? gs[0] : 0;
        l.G2 = gs.Count > 1 ? gs[1] : 0;
        l.Fill(data, team.Abbr, unavailable);
        l.SwapInStronger(data, team.Abbr, unavailable);
        l.Pp1 = ValidPp(data, team.Lines.Pp1, l);
        l.Pp2 = ValidPp(data, team.Lines.Pp2, l);
        l.FixPowerPlay(data);
        return l;
    }

    static int[] ValidPp(GameData data, List<int> src, Lineup l)
    {
        var dressed = l.Skaters.ToHashSet();
        var r = new int[5];
        int k = 0;
        foreach (var id in src) if (dressed.Contains(id) && k < 5 && !r.Contains(id)) r[k++] = id;
        return r;
    }

    /// <summary>
    /// Здоровый игрок заметно сильнее (КА на 15+) слабейшего в составе той же группы (нападающие / защитники) — выходит вместо него.
    /// Тренер НХЛ может посадить игрока, но такой разрыв в звеньях из источника почти всегда — пропуск в данных, а не решение.
    /// </summary>
    public void SwapInStronger(GameData data, string abbr, ISet<int> unavailable)
    {
        for (int guard = 0; guard < 6; guard++)
        {
            var dressed = All.ToHashSet();
            bool changed = false;
            foreach (bool def in new[] { false, true })
            {
                var lines = def ? D : F;
                var slots = lines.SelectMany((u, i) => u.Select((id, k) => (i, k, id))).Where(x => x.id != 0 && data.PlayerById.ContainsKey(x.id)).ToList();
                if (slots.Count == 0) continue;
                var weakest = slots.OrderBy(x => data.PlayerById[x.id].CaEst).First();
                var bench = data.Roster(abbr).Where(p => !p.IsGoalie && p.IsDefense == def && !unavailable.Contains(p.Id) && !dressed.Contains(p.Id))
                    .OrderByDescending(p => p.CaEst).FirstOrDefault();
                if (bench != null && bench.CaEst >= data.PlayerById[weakest.id].CaEst + 15) { lines[weakest.i][weakest.k] = bench.Id; changed = true; }
            }
            if (!changed) break;
        }
    }

    /// <summary>Добрать пустые места лучшими свободными игроками клуба.</summary>
    public void Fill(GameData data, string abbr, ISet<int> unavailable)
    {
        var used = All.ToHashSet();
        var pool = data.Roster(abbr).Where(p => !unavailable.Contains(p.Id) && !used.Contains(p.Id)).OrderByDescending(p => p.CaEst).ToList();
        // лучший по силе (КА) с поправкой за непривычное место: центр на краю −4, крайний в центре −8, защитник в нападении и наоборот −30.
        // Раньше брали «своих по позиции» по старому рейтингу — и при неполных звеньях в источнике сильнейший центр мог не попасть в состав
        int Take(Func<PlayerData, double> penalty)
        {
            var p = pool.Where(x => !x.IsGoalie).OrderByDescending(x => x.CaEst - penalty(x)).FirstOrDefault();
            if (p == null) return 0;
            pool.Remove(p);
            return p.Id;
        }
        for (int i = 0; i < F.Count; i++)
            for (int s = 0; s < 3; s++)
                if (F[i][s] == 0)
                    F[i][s] = Take(p => p.IsDefense ? 30 : s == 1 ? (p.Pos == "C" ? 0 : 8) : (p.Pos == "C" ? 4 : 0));
        for (int i = 0; i < D.Count; i++)
            for (int s = 0; s < 2; s++)
                if (D[i][s] == 0)
                    D[i][s] = Take(p => p.IsDefense ? 0 : 30);
        if (G1 == 0) G1 = TakeGoalie();
        if (G2 == 0) G2 = TakeGoalie();

        int TakeGoalie()
        {
            var g = pool.Where(p => p.IsGoalie).OrderByDescending(p => p.Gsax).FirstOrDefault();
            if (g == null) return 0;
            pool.Remove(g);
            return g.Id;
        }
    }

    /// <summary>Звенья большинства: если неполные — лучшие по атаке (4 нападающих + защитник; второе — следующие).</summary>
    public void FixPowerPlay(GameData data)
    {
        var dressed = Skaters.Select(id => data.PlayerById[id]).ToList();
        int[] Build(IEnumerable<int> exclude)
        {
            var ex = exclude.ToHashSet();
            var f = dressed.Where(p => !p.IsDefense && !ex.Contains(p.Id)).OrderByDescending(p => p.Ixg60 + p.Apg).Take(4);
            var d = dressed.Where(p => p.IsDefense && !ex.Contains(p.Id)).OrderByDescending(p => p.Apg + p.Ixg60).Take(1);
            return f.Concat(d).Select(p => p.Id).Concat(Enumerable.Repeat(0, 5)).Take(5).ToArray();
        }
        if (Pp1.Count(x => x != 0) < 5) Pp1 = Build(Array.Empty<int>());
        if (Pp2.Count(x => x != 0) < 5) Pp2 = Build(Pp1);
        // меньшинство: лучшие в обороне (2 нападающих + 2 защитника)
        int[] Pk(IEnumerable<int> exclude)
        {
            var ex = exclude.ToHashSet();
            double D(PlayerData p) => p.Rating("dfn") + 0.5 * p.Rating("blk") + 0.3 * p.Rating("tka");
            var f = dressed.Where(p => !p.IsDefense && !ex.Contains(p.Id)).OrderByDescending(D).Take(2);
            var d = dressed.Where(p => p.IsDefense && !ex.Contains(p.Id)).OrderByDescending(D).Take(2);
            return f.Concat(d).Select(p => p.Id).Concat(Enumerable.Repeat(0, 4)).Take(4).ToArray();
        }
        var dressedIds = dressed.Select(p => p.Id).ToHashSet();
        Pk1 = (Pk1 ?? new int[4]).Select(x => dressedIds.Contains(x) ? x : 0).ToArray();
        Pk2 = (Pk2 ?? new int[4]).Select(x => dressedIds.Contains(x) ? x : 0).ToArray();
        if (Pk1.Length != 4 || Pk1.Count(x => x != 0) < 4) Pk1 = Pk(Array.Empty<int>());
        if (Pk2.Length != 4 || Pk2.Count(x => x != 0) < 4) Pk2 = Pk(Pk1);
    }

    /// <summary>Полностью автоматический состав: лучшие по общему рейтингу на свои позиции.</summary>
    public static Lineup Auto(GameData data, string abbr, ISet<int> unavailable)
    {
        var l = new Lineup();
        for (int i = 0; i < 4; i++) l.F.Add(new int[3]);
        for (int i = 0; i < 3; i++) l.D.Add(new int[2]);
        l.Fill(data, abbr, unavailable);
        l.FixPowerPlay(data);
        return l;
    }

    /// <summary>Убрать недоступных (травма) и добрать замену.</summary>
    public void Repair(GameData data, string abbr, ISet<int> unavailable)
    {
        for (int i = 0; i < F.Count; i++) for (int s = 0; s < 3; s++) if (unavailable.Contains(F[i][s]) || !OnTeam(F[i][s])) F[i][s] = 0;
        for (int i = 0; i < D.Count; i++) for (int s = 0; s < 2; s++) if (unavailable.Contains(D[i][s]) || !OnTeam(D[i][s])) D[i][s] = 0;
        if (unavailable.Contains(G1) || !OnTeam(G1)) G1 = 0;
        if (unavailable.Contains(G2) || !OnTeam(G2)) G2 = 0;
        if (G1 == 0 && G2 != 0) { G1 = G2; G2 = 0; }
        Fill(data, abbr, unavailable);
        var dressed = Skaters.ToHashSet();
        Pp1 = Pp1.Select(x => dressed.Contains(x) ? x : 0).ToArray();
        Pp2 = Pp2.Select(x => dressed.Contains(x) ? x : 0).ToArray();
        FixPowerPlay(data);

        bool OnTeam(int id) => id != 0 && data.PlayerById.TryGetValue(id, out var p) && p.Team == abbr;
    }

    /// <summary>Поменять местами только в звеньях равных составов и в воротах (спецбригады не трогаем).</summary>
    public void SwapEven(int a, int b)
    {
        int Map(int x) => x == a ? b : x == b ? a : x;
        foreach (var line in F) for (int s = 0; s < line.Length; s++) line[s] = Map(line[s]);
        foreach (var pair in D) for (int s = 0; s < pair.Length; s++) pair[s] = Map(pair[s]);
        G1 = Map(G1); G2 = Map(G2);
    }

    /// <summary>Поставить игрока в спецбригаду на место slot (если он уже в ней — поменять местами).</summary>
    public static void SetUnit(int[] unit, int slot, int id)
    {
        int at = Array.IndexOf(unit, id);
        if (at >= 0) unit[at] = unit[slot];
        unit[slot] = id;
    }

    /// <summary>Поменять местами двух игроков (в составе или игрока состава с запасным).</summary>
    public void Swap(int a, int b)
    {
        int Map(int x) => x == a ? b : x == b ? a : x;
        foreach (var line in F) for (int s = 0; s < line.Length; s++) line[s] = Map(line[s]);
        foreach (var pair in D) for (int s = 0; s < pair.Length; s++) pair[s] = Map(pair[s]);
        for (int s = 0; s < 5; s++) { Pp1[s] = Map(Pp1[s]); Pp2[s] = Map(Pp2[s]); }
        for (int s = 0; s < Pk1.Length; s++) Pk1[s] = Map(Pk1[s]);
        for (int s = 0; s < Pk2.Length; s++) Pk2[s] = Map(Pk2[s]);
        G1 = Map(G1); G2 = Map(G2);
    }
}
