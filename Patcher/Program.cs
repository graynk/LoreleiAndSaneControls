using System.Text.Json;

namespace LoreleiAndSaneControls.Patcher;

internal static class Program
{
    internal const int Success = 0;
    internal const int Failed = 1;
    internal const int InvalidArguments = 2;

    private static int Main(string[] args)
    {
        return Run(args, Console.In, Console.Out, Console.Error);
    }

    internal static int Run(
        IReadOnlyList<string> args,
        TextReader input,
        TextWriter output,
        TextWriter error,
        string? gameRoot = null)
    {
        gameRoot ??= AppContext.BaseDirectory;

        var patchPath = args.Count == 2 && args[0] == "--patch" ? args[1] : null;
        if (args.Count == 0 || patchPath is not null)
        {
            output.WriteLine("This will patch the game's control scheme for Lorelei and the Sane Controls.");
            if (patchPath is not null)
            {
                output.WriteLine($"Patch: {patchPath}");
            }

            output.WriteLine("A backup of globalgamemanagers.assets will be created.");
            if (!Confirm(input, output))
            {
                return Success;
            }

            try
            {
                var result = Patch(gameRoot, patchPath);
                if (result.Changed)
                {
                    output.WriteLine($"Patched {result.AssetsPath}");
                    output.WriteLine($"Backup: {result.BackupPath}");
                }
                else
                {
                    output.WriteLine("The control scheme is already patched. No files were changed.");
                }

                return Success;
            }
            catch (Exception exception)
            {
                error.WriteLine($"Patching failed: {exception.Message}");
                return Failed;
            }
        }

        if (args.Count == 1 && args[0] == "extract")
        {
            output.WriteLine("This will extract the Rewired InputManager control scheme from globalgamemanagers.assets.");
            output.WriteLine("No game files will be changed.");
            if (!Confirm(input, output))
            {
                return Success;
            }

            try
            {
                var result = Extract(gameRoot);
                output.WriteLine($"Extracted PathID {result.PathId} to {result.OutputPath}");
                return Success;
            }
            catch (Exception exception)
            {
                error.WriteLine($"Extraction failed: {exception.Message}");
                return Failed;
            }
        }

        error.WriteLine("Usage: control_scheme_patcher [--patch <file.json>]");
        return InvalidArguments;
    }

    internal static (bool Changed, string AssetsPath, string? BackupPath) Patch(
        string gameRoot,
        string? patchPath = null)
    {
        var paths = FindGameFiles(gameRoot);
        var patch = new ControlSchemePatch(PatchDefinition.Load(patchPath));
        var backupPath = paths.AssetsPath + ".lorelei-backup";
        var temporaryPath = paths.AssetsPath + $".lorelei-{Guid.NewGuid():N}.tmp";

        try
        {
            using (var asset = InputManagerAssetFile.Open(paths.AssetsPath, paths.ManagedDirectory))
            {
                if (!patch.Apply(asset.Field))
                {
                    return (false, paths.AssetsPath, null);
                }

                if (File.Exists(backupPath))
                {
                    throw new IOException($"Backup already exists: {backupPath}. Move it before patching again.");
                }

                asset.Write(temporaryPath);
            }

            using (var asset = InputManagerAssetFile.Open(temporaryPath, paths.ManagedDirectory))
            {
                if (patch.Apply(asset.Field))
                {
                    throw new InvalidDataException("The patched asset failed validation.");
                }
            }

            File.Replace(temporaryPath, paths.AssetsPath, backupPath, ignoreMetadataErrors: true);
            return (true, paths.AssetsPath, backupPath);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    internal static (long PathId, string OutputPath) Extract(string gameRoot)
    {
        var paths = FindGameFiles(gameRoot);
        using var asset = InputManagerAssetFile.Open(paths.AssetsPath, paths.ManagedDirectory);
        var outputPath = Path.Combine(paths.DataDirectory, "InputManager-globalgamemanagers.assets.json");

        using var stream = File.Create(outputPath);
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
        asset.WriteJson(writer);
        return (asset.Info.PathId, outputPath);
    }

    internal static (string DataDirectory, string AssetsPath, string ManagedDirectory) FindGameFiles(
        string gameRoot)
    {
        var root = Path.GetFullPath(gameRoot);
        var candidates = new List<string>
        {
            Path.Combine(root, "Lorelei and the Laser Eyes_Data"),
            root
        };

        var matches = candidates
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => File.Exists(Path.Combine(path, "globalgamemanagers.assets")))
            .ToList();

        if (matches.Count == 0)
        {
            throw new DirectoryNotFoundException(
                "Could not find the game's data directory. Put control_scheme_patcher in the game root.");
        }

        if (matches.Count != 1)
        {
            throw new InvalidDataException("Found more than one globalgamemanagers.assets; refusing to guess.");
        }

        var dataDirectory = matches[0];
        return (
            dataDirectory,
            Path.Combine(dataDirectory, "globalgamemanagers.assets"),
            Path.Combine(dataDirectory, "Managed"));
    }

    private static bool Confirm(TextReader input, TextWriter output)
    {
        output.Write("Continue? [y/N] ");
        if (string.Equals(input.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        output.WriteLine("Cancelled.");
        return false;
    }
}
