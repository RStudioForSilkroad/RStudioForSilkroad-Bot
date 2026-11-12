using System;
using System.Collections.Generic;
using System.Linq;

// One line of the skill list: a hotbar key and when to press it.
//   Saldırı      pressed in turn while a target is selected. Seconds = this skill's own cooldown (0 = none).
//                Target limits it to one monster class (Hepsi = any); MinMobs = only when at least that many monsters are close.
//   Buff         pressed when due, no target needed. With a Skill ID the bot looks at the character's active buffs and presses
//                the key only when that buff is gone; without one it repeats every Seconds (0 = once per run).
//   Acil         emergency buff: pressed when HP falls below Percent, or when at least MinMobs monsters crowd the character.
//   İyileştirme  pressed when HP falls below Percent; Seconds = cooldown
//   Mana         pressed when MP falls below Percent; Seconds = cooldown
// A line with a Skill ID and no key is used by its id (the client is told to use that skill; no key, no foreground window needed);
// a line with a key presses the key.
public sealed class SkillSlot
{
    public bool On = true;
    public string Key = "1";
    public string Kind = "Saldırı";
    public int Seconds;
    public int Percent = 50;
    public string Name = "";
    public int SkillId;
    public string Target = "Hepsi";
    public int MinMobs;
}

// Options that apply to the whole list (the phBot attack / buff tab check boxes that make sense for this bot).
public sealed class SkillOptions
{
    public bool BuffWhileAttacking = true, Rearm = true, InOrder = true, Fallback = true, BuffOnly;
}

public sealed class SkillProfile
{
    public string Name = "";
    public List<SkillSlot> Slots = new List<SkillSlot>();
    public SkillOptions Options = new SkillOptions();
}

// What the rotation needs to know about the character right now.
sealed class SkillContext
{
    internal int Hp = 100, Mp = 100, Attackers;
    internal bool StatsKnown = true, InCombat;
    internal HashSet<uint> Buffs;
}

// Windows keyboard messages for a window that is not in front: WM_KEYDOWN / WM_KEYUP with the scan code in lParam.
static class KeyMessage
{
    internal const uint Down = 0x100, Up = 0x101;

    internal static uint LParam(ushort scan, bool up)
    {
        uint value = 1u | ((uint)scan << 16);
        return up ? value | (1u << 30) | (1u << 31) : value;
    }

    internal static IntPtr Param(ushort scan, bool up)
    {
        return new IntPtr(unchecked((int)LParam(scan, up)));
    }

    internal static bool Post(IntPtr window, ushort vk, ushort scan, bool up)
    {
        return window != IntPtr.Zero && Native.PostMessage(window, up ? Up : Down, new UIntPtr(vk), Param(scan, up));
    }
}

sealed class SkillRotation
{
    internal const string Attack_ = "Saldırı", Buff_ = "Buff", Heal_ = "İyileştirme", Mana_ = "Mana", Emergency_ = "Acil";
    internal static readonly string[] Kinds = { Attack_, Buff_, Emergency_, Heal_, Mana_ };
    internal const string AnyTarget = "Hepsi";
    // Returned instead of a key for a line that has a Skill ID and no key: the caller uses LastSkillId.
    internal const ushort IdOnly = 0xFFFF;
    internal static readonly string[] Targets = { AnyTarget, "General", "Champion", "Giant", "General Party", "Champion Party",
        "Giant Party", "Unique", "Diğer" };

    sealed class Entry
    {
        internal ushort Key;
        internal string Kind, Target;
        internal int Seconds, Percent, SkillId, MinMobs, Failures;
        internal long Due;
    }

    readonly List<Entry> entries = new List<Entry>();
    int attackIndex;

    // The skill id and kind of the line the last call returned a key (or IdOnly) for.
    internal int LastSkillId { get; private set; }
    internal string LastKind { get; private set; }

    ushort Chosen(Entry e)
    {
        LastSkillId = e.SkillId;
        LastKind = e.Kind;
        return e.Key;
    }

    internal bool HasIdOnly
    {
        get { return entries.Any(e => e.Key == IdOnly); }
    }
    SkillOptions options = new SkillOptions();

    internal bool HasAttack
    {
        get { return entries.Any(e => e.Kind == Attack_); }
    }

    internal SkillOptions Options
    {
        get { return options; }
    }

    // The virtual-key code of a hotbar key name (1-9, 0, A-Z, F1-F12), or 0 when the name is not usable.
    internal static ushort ParseKey(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        string t = text.Trim().ToUpperInvariant();
        if (t.Length == 1 && ((t[0] >= '0' && t[0] <= '9') || (t[0] >= 'A' && t[0] <= 'Z')))
            return t[0];
        int number;
        if (t.Length >= 2 && t.Length <= 3 && t[0] == 'F' && int.TryParse(t.Substring(1), out number) && number >= 1 && number <= 12)
            return (ushort)(0x70 + number - 1);
        return 0;
    }

    // An error message for the first unusable line, or null.
    internal static string Validate(IList<SkillSlot> slots)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            if (!s.On)
                continue;
            bool noKey = string.IsNullOrWhiteSpace(s.Key);
            if (noKey && s.SkillId <= 0)
                return (i + 1) + ". skill: tuş yaz ya da skill’i listeden seçip Skill ID ver (tuşsuz kullanılır).";
            if (!noKey && ParseKey(s.Key) == 0)
                return (i + 1) + ". skill: tuş geçersiz (1-9, 0, A-Z veya F1-F12).";
            if (!Kinds.Contains(s.Kind))
                return (i + 1) + ". skill: tür seçilmedi.";
            if (s.Seconds < 0 || s.Seconds > 3600)
                return (i + 1) + ". skill: süre 0-3600 sn olmalı.";
            // An emergency line may live on the crowd condition alone (percent 0); every other percent line needs 1-99.
            bool crowdOnly = s.Kind == Emergency_ && s.MinMobs > 0;
            if ((s.Kind == Heal_ || s.Kind == Mana_ || s.Kind == Emergency_) && (s.Percent < (crowdOnly ? 0 : 1) || s.Percent > 99))
                return (i + 1) + ". skill: yüzde " + (crowdOnly ? "0" : "1") + "-99 olmalı.";
            if (s.SkillId < 0 || s.SkillId > 99999999)
                return (i + 1) + ". skill: Skill ID geçersiz.";
            if (s.MinMobs < 0 || s.MinMobs > 50)
                return (i + 1) + ". skill: en az mob sayısı 0-50 olmalı.";
            if (s.Kind == Attack_ && !string.IsNullOrEmpty(s.Target) && !Targets.Contains(s.Target))
                return (i + 1) + ". skill: hedef türü tanınmıyor.";
        }
        return null;
    }

    internal void Load(IEnumerable<SkillSlot> slots)
    {
        Load(slots, null);
    }

    internal void Load(IEnumerable<SkillSlot> slots, SkillOptions skillOptions)
    {
        entries.Clear();
        attackIndex = 0;
        options = skillOptions ?? new SkillOptions();
        foreach (var s in slots.Where(s => s.On))
        {
            ushort key = ParseKey(s.Key);
            if (key == 0 && s.SkillId > 0 && string.IsNullOrWhiteSpace(s.Key))
                key = IdOnly;
            if (key != 0 && Kinds.Contains(s.Kind))
                entries.Add(new Entry
                {
                    Key = key, Kind = s.Kind, Seconds = Math.Max(0, s.Seconds), Percent = s.Percent, SkillId = Math.Max(0, s.SkillId),
                    Target = string.IsNullOrEmpty(s.Target) ? AnyTarget : s.Target, MinMobs = Math.Max(0, s.MinMobs)
                });
        }
    }

    // All keys of the enabled lines, for the check against the start/stop shortcut.
    internal List<ushort> Keys()
    {
        return entries.Where(e => e.Key != IdOnly).Select(e => e.Key).Distinct().ToList();
    }

    // After a revival or a change of area the buffs are gone: every buff is due again.
    internal void Rearm()
    {
        foreach (var e in entries)
        {
            e.Due = 0;
            e.Failures = 0;
        }
    }

    // A new target starts the attack list from its first line again (phBot: "Cast skills in order").
    internal void NewTarget()
    {
        if (options.InOrder)
            attackIndex = 0;
    }

    internal ushort Support(long now, int hpPercent, int mpPercent, bool statsKnown)
    {
        return Support(now, new SkillContext { Hp = hpPercent, Mp = mpPercent, StatsKnown = statsKnown });
    }

    // Emergency, then heal, then mana, then buffs: the key to press now without a target, or 0.
    internal ushort Support(long now, SkillContext c)
    {
        foreach (var e in entries)
        {
            if (e.Kind != Emergency_ || now < e.Due)
                continue;
            bool low = c.StatsKnown && e.Percent > 0 && c.Hp < e.Percent, crowded = e.MinMobs > 0 && c.Attackers >= e.MinMobs;
            if (!low && !crowded)
                continue;
            if (e.SkillId > 0 && c.Buffs != null && c.Buffs.Contains((uint)e.SkillId))
                continue;
            e.Due = now + Math.Max(2, e.Seconds) * 1000L;
            return Chosen(e);
        }
        foreach (string kind in new[] { Heal_, Mana_ })
            foreach (var e in entries)
            {
                if (e.Kind != kind || now < e.Due)
                    continue;
                if (kind == Heal_ && !(c.StatsKnown && c.Hp < e.Percent))
                    continue;
                if (kind == Mana_ && !(c.StatsKnown && c.Mp < e.Percent))
                    continue;
                e.Due = now + Math.Max(2, e.Seconds) * 1000L;
                return Chosen(e);
            }
        foreach (var e in entries)
        {
            if (e.Kind != Buff_ || now < e.Due || (c.InCombat && !options.BuffWhileAttacking))
                continue;
            if (e.SkillId > 0 && c.Buffs != null)
            {
                // The character's own buff list says whether it is still on. A key that keeps failing (no mana, skill not learned)
                // is retried every 3 s five times, then left alone for 30 s.
                if (c.Buffs.Contains((uint)e.SkillId))
                {
                    e.Failures = 0;
                    continue;
                }
                e.Failures++;
                e.Due = now + (e.Failures % 5 == 0 ? 30000 : 3000);
            }
            else
                e.Due = e.Seconds == 0 ? long.MaxValue : now + e.Seconds * 1000L;
            return Chosen(e);
        }
        return 0;
    }

    internal ushort NextAttack(long now)
    {
        return NextAttack(now, null, 99);
    }

    // The next attack key in turn whose own cooldown is over, or 0. className is the target's class (General, Giant ...);
    // near is the number of monsters close to the character. Lines made for the target's class come first; lines for "Hepsi" are the
    // general list and are used when the class has no line of its own (or, with the fallback option, when none of them is ready).
    internal ushort NextAttack(long now, string className, int near)
    {
        bool specific = className != null && entries.Any(e => e.Kind == Attack_ && e.Target == className);
        if (specific)
        {
            ushort key = Pick(now, near, e => e.Target == className);
            if (key != 0 || !options.Fallback)
                return key;
        }
        return Pick(now, near, e => e.Target == AnyTarget);
    }

    ushort Pick(long now, int near, Func<Entry, bool> inList)
    {
        int count = entries.Count;
        for (int step = 0; step < count; step++)
        {
            int index = (attackIndex + step) % count;
            var e = entries[index];
            if (e.Kind != Attack_ || now < e.Due || !inList(e) || near < e.MinMobs)
                continue;
            e.Due = now + e.Seconds * 1000L;
            attackIndex = (index + 1) % count;
            return Chosen(e);
        }
        return 0;
    }

    // Ready-made lists. All lines are off: the keys are placeholders the user sets to the hotbar before turning a line on.
    internal static readonly string[] TemplateNames = { "Savunma buff seti", "Alan hasarı + tek hedef", "Yalnız buff / iyileştirme" };

    internal static List<SkillSlot> Template(string name)
    {
        if (name == TemplateNames[0])
            return new List<SkillSlot>
            {
                new SkillSlot { On = false, Key = "5", Kind = Buff_, Name = "Savunma buff 1", Seconds = 180 },
                new SkillSlot { On = false, Key = "6", Kind = Buff_, Name = "Savunma buff 2", Seconds = 180 },
                new SkillSlot { On = false, Key = "7", Kind = Emergency_, Name = "Acil kalkan", Percent = 35, Seconds = 30 },
                new SkillSlot { On = false, Key = "8", Kind = Heal_, Name = "İyileştirme", Percent = 55, Seconds = 4 },
                new SkillSlot { On = false, Key = "9", Kind = Mana_, Name = "Mana", Percent = 35, Seconds = 4 }
            };
        if (name == TemplateNames[1])
            return new List<SkillSlot>
            {
                new SkillSlot { On = false, Key = "1", Kind = Attack_, Name = "Ana saldırı" },
                new SkillSlot { On = false, Key = "2", Kind = Attack_, Name = "Alan hasarı", MinMobs = 3, Seconds = 6 },
                new SkillSlot { On = false, Key = "3", Kind = Attack_, Name = "Dev için güçlü", Target = "Giant", Seconds = 8 },
                new SkillSlot { On = false, Key = "4", Kind = Attack_, Name = "Unique için güçlü", Target = "Unique", Seconds = 8 }
            };
        if (name == TemplateNames[2])
            return new List<SkillSlot>
            {
                new SkillSlot { On = false, Key = "5", Kind = Buff_, Name = "Buff 1", Seconds = 180 },
                new SkillSlot { On = false, Key = "6", Kind = Buff_, Name = "Buff 2", Seconds = 180 },
                new SkillSlot { On = false, Key = "8", Kind = Heal_, Name = "İyileştirme", Percent = 60, Seconds = 4 }
            };
        return new List<SkillSlot>();
    }
}
