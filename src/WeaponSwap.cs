using System;
using System.Collections.Generic;
using System.Linq;

// Some skills can only be used with one kind of weapon. A Wizard / Cleric needs the Wizard's two-handed staff (type 11) for the Wizard spells and
// the Cleric's staff (type 15) for the Cleric's buffs and heals, so the character has to put the other weapon on before each of them.
// The tables come from the game (tools/game-data/build_data.py): skill-weapons.json (the weapon type a skill needs, by skill id) and
// weapon-types.json (the weapon type of each weapon item).
static class WeaponRules
{
    // The slot of the equipped weapon; the bag begins at slot 13.
    internal const int WeaponSlot = 6;
    internal const int NoChange = -1, NoWeapon = -2;

    static readonly Dictionary<int, int> skillWeapon = Invert(V3Data.Load<Dictionary<string, List<int>>>("skill-weapons.json"));
    static readonly Dictionary<uint, int> itemWeapon = Invert(V3Data.Load<Dictionary<string, List<int>>>("weapon-types.json"))
        .ToDictionary(kv => (uint)kv.Key, kv => kv.Value);

    static Dictionary<int, int> Invert(Dictionary<string, List<int>> groups)
    {
        var result = new Dictionary<int, int>();
        foreach (var group in groups)
            foreach (int id in group.Value)
                result[id] = int.Parse(group.Key);
        return result;
    }

    // The weapon type the skill needs; 0 when any weapon (or none) will do.
    internal static int RequiredFor(int skillId)
    {
        int type;
        return skillWeapon.TryGetValue(skillId, out type) ? type : 0;
    }

    // The weapon type of an item model; 0 when it is not a weapon.
    internal static int TypeOf(uint model)
    {
        int type;
        return itemWeapon.TryGetValue(model, out type) ? type : 0;
    }

    internal static string TypeName(int type)
    {
        switch (type)
        {
            case 10: return "Warlock asası";
            case 11: return "Wizard asası";
            case 12: return "Crossbow";
            case 13: return "Dagger";
            case 14: return "Harp";
            case 15: return "Cleric asası";
            default: return "silah tipi " + type;
        }
    }

    // What to do before using the skill: NoChange (nothing to do: no weapon needed, or the right one is on), NoWeapon (the right weapon is not in
    // the bag), or the bag slot whose weapon is to be put on. Among several suitable weapons the one with the highest model number is taken (within a
    // weapon family a higher number is a higher degree).
    internal static int SlotToEquip(int skillId, uint equippedModel, IEnumerable<KeyValuePair<int, uint>> bagWeapons)
    {
        int needed = RequiredFor(skillId);
        if (needed == 0 || TypeOf(equippedModel) == needed)
            return NoChange;
        var best = bagWeapons.Where(w => w.Key >= 13 && TypeOf(w.Value) == needed).OrderByDescending(w => w.Value).Select(w => w.Key).ToList();
        return best.Count == 0 ? NoWeapon : best[0];
    }
}

sealed partial class MemorySelectV3Form
{
    // After a weapon change is sent the client needs a moment: the next skill waits until then.
    long weaponWaitUntil, weaponGiveUpUntil;
    int weaponTries;
    int weaponLastSkill;

    // DORMANT: nothing calls this since skills are only cast by their hotbar key (the rows carry no skill id any more). The weapon rules above and the
    // item-move packet stay for a later key-based swap. When another weapon is needed, the change is sent and false is returned.
    bool WeaponReady(int skillId, long now)
    {
        if (!rotation.Options.WeaponSwap || skillId <= 0 || snapshot == null || !snapshot.InventoryKnown || WeaponRules.RequiredFor(skillId) == 0)
            return true;
        int slot = WeaponRules.SlotToEquip(skillId, snapshot.WeaponModel, snapshot.BagWeapons);
        if (slot == WeaponRules.NoChange)
        {
            weaponTries = 0;
            return true;
        }
        if (now < weaponGiveUpUntil)
            return true;
        if (now < weaponWaitUntil)
        {
            rotation.DelayLast(weaponWaitUntil);
            return false;
        }
        int needed = WeaponRules.RequiredFor(skillId);
        if (slot == WeaponRules.NoWeapon || weaponTries >= 3)
        {
            // Nothing suitable to put on (or the change did not take): the skill is tried as it is, and the cause is said once a minute.
            weaponGiveUpUntil = now + 60000;
            weaponTries = 0;
            Log("Skill #" + skillId + " için " + WeaponRules.TypeName(needed) + " gerekiyor; " + (slot == WeaponRules.NoWeapon ?
                "çantada böyle bir silah yok." : "silah değişimi olmadı."));
            return true;
        }
        if (!CommandsAllowed())
            return true;
        try
        {
            if (sender == null || !sender.MoveItem(slot, WeaponRules.WeaponSlot, 1))
            {
                weaponTries++;
                return true;
            }
        }
        catch (System.IO.IOException e)
        {
            Log("Silah değişimi gönderilemedi · " + e.Message);
            weaponTries++;
            return true;
        }
        weaponTries++;
        weaponWaitUntil = now + 1800;
        weaponLastSkill = skillId;
        Log("Silah değişiyor: " + WeaponRules.TypeName(needed) + " takılıyor (skill #" + skillId + " için).");
        rotation.DelayLast(weaponWaitUntil);
        return false;
    }
}

// How a skill is timed and whether it needs a target, from the game's skill table (skill-timing.json). Only "needs a target" is still used (a buff
// line made of a skill without a target is a buff); no skill is sent as a packet any more, the key is pressed.
sealed class SkillTiming
{
    internal int PrepareMs, CastMs, ActionMs, ReuseMs;
    internal bool NeedsTarget;

    static Dictionary<int, SkillTiming> table;

    static Dictionary<int, SkillTiming> Table
    {
        get
        {
            if (table == null)
            {
                var loaded = new Dictionary<int, SkillTiming>();
                foreach (var pair in V3Data.Load<Dictionary<string, int[]>>("skill-timing.json"))
                    loaded[int.Parse(pair.Key)] = new SkillTiming { PrepareMs = pair.Value[0], CastMs = pair.Value[1], ActionMs = pair.Value[2],
                        ReuseMs = pair.Value[3], NeedsTarget = pair.Value[4] != 0 };
                table = loaded;
            }
            return table;
        }
    }

    // The skill's timing; null for a skill the table does not know (it is then used the old way).
    internal static SkillTiming Of(int skillId)
    {
        SkillTiming timing;
        return Table.TryGetValue(skillId, out timing) ? timing : null;
    }
}
