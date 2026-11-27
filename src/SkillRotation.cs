using System;
using System.Collections.Generic;
using System.Linq;

// One line of the skill list: a hotbar key and when to press it. A skill is only ever used by pressing its key (never sent as a packet).
//   Saldırı      pressed in turn while a target is selected. Seconds = this skill's own cooldown (0 = none).
//                Target limits it to one monster class (Hepsi = any); MinMobs = only when at least that many monsters are close.
//   Buff         pressed when due, no target needed. With a Skill ID (read only: the buff's id in the character's buff list) the bot presses
//                the key only when that buff is gone; without one it repeats every Seconds (0 = once per run).
//   Acil         emergency buff: pressed when HP falls below Percent, or when at least MinMobs monsters crowd the character.
//   İyileştirme  pressed when HP falls below Percent; Seconds = cooldown
//   Mana         pressed when MP falls below Percent; Seconds = cooldown
//   Parti buff   old kind that needed a packet: such a line is kept in the list but never used.
// A line without a usable key is never used (the run says how many there are).
public sealed class SkillSlot
{
    public bool On = true;
    public string Key = "1";
    public string Kind = "Saldırı";
    public int Seconds;
    public int Percent = 50;
    public string Name = "";
    // Read only: the id of the buff in the character's buff list. It decides when the key is pressed; nothing is ever sent by it.
    public int SkillId;
    public string Target = "Hepsi";
    public int MinMobs;
    // Kept for old settings (party buff lines); unused.
    public string Members = "";
}

// Options that apply to the whole list (the phBot attack / buff tab check boxes that make sense for this bot).
public sealed class SkillOptions
{
    public bool BuffWhileAttacking = true, Rearm = true, InOrder = true, Fallback = true, BuffOnly, WeaponSwap = true;
    // How much of a skill's prepare + cast time passes before the next skill is sent (percent; 100 = the whole time, lower = skills follow sooner; 40 was tried live).
    public int CastPacePercent = 40;
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
    internal const string Attack_ = "Saldırı", Buff_ = "Buff", Heal_ = "İyileştirme", Mana_ = "Mana", Emergency_ = "Acil", PartyBuff_ = "Parti buff";
    // The kinds that can be chosen. PartyBuff_ (it needed a packet) is only known so that old lines keep their kind; it is never executed.
    internal static readonly string[] Kinds = { Attack_, Buff_, Emergency_, Heal_, Mana_ };
    internal const string AnyTarget = "Hepsi";
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

    Entry lastEntry;

    ushort Chosen(Entry e)
    {
        lastEntry = e;
        return e.Key;
    }

    // The line just chosen cannot be used yet: it is offered again at `until`, and the try does not count as a failure.
    internal void DelayLast(long until)
    {
        if (lastEntry == null)
            return;
        lastEntry.Due = until;
        if (lastEntry.Kind == Buff_ && lastEntry.Failures > 0)
            lastEntry.Failures--;
    }

    // Enabled lines of the last Load that are never used: without a usable key, and old party buff lines.
    internal int KeylessLines { get; private set; }
    internal int InertPartyLines { get; private set; }
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

    // The key name for a pressed key (1-9, 0, A-Z, F1-F12; the number pad counts as the number row), or null when the key cannot be a hotbar key.
    internal static string NameOfKey(System.Windows.Forms.Keys key)
    {
        if (key >= System.Windows.Forms.Keys.D0 && key <= System.Windows.Forms.Keys.D9)
            return ((char)('0' + (key - System.Windows.Forms.Keys.D0))).ToString();
        if (key >= System.Windows.Forms.Keys.NumPad0 && key <= System.Windows.Forms.Keys.NumPad9)
            return ((char)('0' + (key - System.Windows.Forms.Keys.NumPad0))).ToString();
        if (key >= System.Windows.Forms.Keys.A && key <= System.Windows.Forms.Keys.Z)
            return ((char)('A' + (key - System.Windows.Forms.Keys.A))).ToString();
        if (key >= System.Windows.Forms.Keys.F1 && key <= System.Windows.Forms.Keys.F12)
            return "F" + (1 + (key - System.Windows.Forms.Keys.F1));
        return null;
    }

    // The first hotbar key (1-9, 0, then F1-F12) that no line uses yet, for a new line.
    internal static string NextFreeKey(IEnumerable<string> used)
    {
        var taken = new HashSet<ushort>(used.Select(ParseKey));
        foreach (string k in new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "0", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12" })
            if (!taken.Contains(ParseKey(k)))
                return k;
        return "1";
    }

    // True for a line that is never used: no usable key (a line with an empty key stays in the list; the run says how many there are).
    internal static bool Keyless(SkillSlot s)
    {
        return ParseKey(s.Key) == 0;
    }

    // An error message for the first unusable line, or null. A line with an empty key is not an error: it stays in the list and is never used.
    internal static string Validate(IList<SkillSlot> slots)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            if (!s.On)
                continue;
            bool noKey = string.IsNullOrWhiteSpace(s.Key);
            if (!noKey && ParseKey(s.Key) == 0)
                return (i + 1) + ". skill: tuş geçersiz (1-9, 0, A-Z veya F1-F12).";
            if (!Kinds.Contains(s.Kind) && s.Kind != PartyBuff_)
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
        KeylessLines = InertPartyLines = 0;
        options = skillOptions ?? new SkillOptions();
        foreach (var s in slots.Where(s => s.On))
        {
            if (s.Kind == PartyBuff_)
            {
                InertPartyLines++;
                continue;
            }
            ushort key = ParseKey(s.Key);
            if (key == 0)
            {
                KeylessLines++;
                continue;
            }
            // A skill that takes no target (an imbue, a self buff) in an attack line is a buff: it is renewed when it has run out.
            string kind = s.Kind;
            var timing = s.SkillId > 0 ? SkillTiming.Of(s.SkillId) : null;
            if (kind == Attack_ && timing != null && !timing.NeedsTarget)
                kind = Buff_;
            if (key != 0 && Kinds.Contains(kind))
                entries.Add(new Entry
                {
                    Key = key, Kind = kind, Seconds = Math.Max(0, s.Seconds), Percent = s.Percent, SkillId = Math.Max(0, s.SkillId),
                    Target = string.IsNullOrEmpty(s.Target) ? AnyTarget : s.Target, MinMobs = Math.Max(0, s.MinMobs)
                });
        }
    }

    // All keys of the enabled lines, for the check against the start/stop shortcut.
    internal List<ushort> Keys()
    {
        return entries.Select(e => e.Key).Distinct().ToList();
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
