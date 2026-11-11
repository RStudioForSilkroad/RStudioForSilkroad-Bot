using System;
using System.IO;

// Where the program keeps its per-user data (settings, learned paths, phBot route exports).
static class AppPaths
{
    internal const string AppName = "Silkroad R Studio";

    static readonly string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static readonly string root = Path.Combine(localData, "SilkroadRStudio");
    // Folder used before the program was renamed; copied once on first start so settings are not lost.
    static readonly string legacyRoot = Path.Combine(localData, "Rikimaru", "MemorySelectV3");
    static bool migrationChecked;

    internal static string DataRoot
    {
        get
        {
            MigrateLegacyData();
            return root;
        }
    }

    static void MigrateLegacyData()
    {
        if (migrationChecked)
            return;
        migrationChecked = true;
        try
        {
            if (!Directory.Exists(root) && Directory.Exists(legacyRoot))
                CopyDirectory(legacyRoot, root);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), false);
        foreach (string directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }
}
