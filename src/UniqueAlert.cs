using System;
using System.Collections.Generic;
using System.Linq;
using System.Media;
using System.Windows.Forms;

// Tells the user when a unique monster (rank 3) appears near the character. Each monster is announced once while it stays in range.
sealed class UniqueAlert
{
    readonly HashSet<uint> announced = new HashSet<uint>();

    // Names of unique monsters that were not announced yet; forgets the ones that left, so a respawn is announced again.
    internal List<string> Observe(IEnumerable<Monster> monsters)
    {
        var uniques = monsters == null ? new List<Monster>() : monsters.Where(m => m != null && m.Alive && m.Rank == 3).ToList();
        announced.IntersectWith(uniques.Select(m => m.Uid));
        var fresh = new List<string>();
        foreach (var m in uniques)
            if (announced.Add(m.Uid))
                fresh.Add(string.IsNullOrEmpty(m.Name) ? "Unique (model " + m.Model + ")" : m.Name);
        return fresh;
    }

    internal void Reset()
    {
        announced.Clear();
    }
}

sealed partial class MemorySelectV3Form
{
    readonly CheckBox uniqueAlertBox = new CheckBox();
    readonly UniqueAlert uniqueAlert = new UniqueAlert();

    void CheckUniques()
    {
        if (!uniqueAlertBox.Checked)
        {
            uniqueAlert.Reset();
            return;
        }
        var names = uniqueAlert.Observe(snapshot.Monsters);
        if (names.Count == 0)
            return;
        string text = "Unique yakında: " + string.Join(", ", names);
        Log(text);
        status.Text = text;
        SystemSounds.Exclamation.Play();
    }
}
