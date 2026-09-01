using System.Reflection;
using System.Text.Json;
using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace LoreleiAndSaneControls.Patcher;

internal sealed class InputManagerAssetFile : IDisposable
{
    private const string ClassDatabaseResourceName =
        "LoreleiAndSaneControls.Patcher.Resources.classdata.tpk";

    private readonly AssetsManager _manager;
    private readonly AssetsFileInstance _assetsFile;
    private readonly string _classDatabasePath;
    private bool _disposed;

    private InputManagerAssetFile(
        AssetsManager manager,
        AssetsFileInstance assetsFile,
        AssetFileInfo info,
        AssetTypeValueField field,
        string classDatabasePath)
    {
        _manager = manager;
        _assetsFile = assetsFile;
        Info = info;
        Field = field;
        _classDatabasePath = classDatabasePath;
    }

    public AssetFileInfo Info { get; }

    public AssetTypeValueField Field { get; }

    public static InputManagerAssetFile Open(string assetsPath, string managedDirectory)
    {
        EnsureRequiredPathsExist(assetsPath, managedDirectory);

        var manager = new AssetsManager();
        var classDatabasePath = ExtractClassDatabase();

        try
        {
            manager.LoadClassPackage(classDatabasePath);
            var assetsFile = manager.LoadAssetsFile(assetsPath, false);
            manager.LoadClassDatabaseFromPackage(assetsFile.file.Metadata.UnityVersion);
            manager.MonoTempGenerator = new MonoCecilTempGenerator(managedDirectory);

            var matches = FindInputManagers(manager, assetsFile);
            if (matches.Count == 0)
            {
                throw new InvalidDataException("Could not find the Rewired.InputManager MonoBehaviour.");
            }

            if (matches.Count > 1)
            {
                throw new InvalidDataException(
                    $"Found {matches.Count} Rewired InputManager MonoBehaviours; refusing to guess.");
            }

            var match = matches[0];
            return new InputManagerAssetFile(
                manager,
                assetsFile,
                match.Info,
                match.Field,
                classDatabasePath);
        }
        catch
        {
            manager.UnloadAllAssetsFiles(true);
            File.Delete(classDatabasePath);
            throw;
        }
    }

    public void Write(string outputPath)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Info.SetNewData(Field);

        using var writer = new AssetsFileWriter(outputPath);
        _assetsFile.file.Write(writer);
    }

    public void WriteJson(Utf8JsonWriter writer)
    {
        WriteJsonValue(writer, Field);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _manager.UnloadAllAssetsFiles(true);
        File.Delete(_classDatabasePath);
        _disposed = true;
    }

    private static List<InputManagerAsset> FindInputManagers(
        AssetsManager manager,
        AssetsFileInstance assetsFile)
    {
        var matches = new List<InputManagerAsset>();
        foreach (var info in assetsFile.file.GetAssetsOfType(AssetClassID.MonoBehaviour))
        {
            if (!IsRewiredInputManagerScript(manager, assetsFile, info))
            {
                continue;
            }

            AssetTypeValueField field;
            try
            {
                field = manager.GetBaseField(assetsFile, info);
            }
            catch (Exception exception)
            {
                throw new InvalidDataException(
                    "Could not deserialize the Rewired InputManager. " +
                    "Make sure every DLL from the game's Managed directory is present. " +
                    $"AssetsTools.NET reported: {exception.Message}",
                    exception);
            }

            if (!HasRewiredUserData(field))
            {
                throw new InvalidDataException(
                    "The Rewired InputManager does not contain the expected actions and controller maps.");
            }

            matches.Add(new InputManagerAsset(info, field));
        }

        return matches;
    }

    private static bool IsRewiredInputManagerScript(
        AssetsManager manager,
        AssetsFileInstance assetsFile,
        AssetFileInfo monoBehaviourInfo)
    {
        var scriptIndex = monoBehaviourInfo.GetScriptIndex(assetsFile.file);
        if (scriptIndex < 0 || scriptIndex >= assetsFile.file.Metadata.ScriptTypes.Count)
        {
            return false;
        }

        var scriptReference = assetsFile.file.Metadata.ScriptTypes[scriptIndex];
        if (scriptReference.FileId != 0)
        {
            return false;
        }

        var scriptInfo = assetsFile.file.GetAssetInfo(scriptReference.PathId);
        if (scriptInfo is null || scriptInfo.TypeId != (int)AssetClassID.MonoScript)
        {
            return false;
        }

        var script = manager.GetBaseField(assetsFile, scriptInfo);
        return string.Equals(script["m_ClassName"].AsString, "InputManager", StringComparison.Ordinal)
            && string.Equals(script["m_Namespace"].AsString, "Rewired", StringComparison.Ordinal);
    }

    private static bool HasRewiredUserData(AssetTypeValueField field)
    {
        var userData = field["_userData"];
        return !userData.IsDummy
            && !userData["actions"].IsDummy
            && !userData["joystickMaps"].IsDummy
            && !userData["keyboardMaps"].IsDummy;
    }

    private static void EnsureRequiredPathsExist(string assetsPath, string managedDirectory)
    {
        if (!File.Exists(assetsPath))
        {
            throw new FileNotFoundException("globalgamemanagers.assets was not found.", assetsPath);
        }

        if (!Directory.Exists(managedDirectory))
        {
            throw new DirectoryNotFoundException("The game's Managed directory was not found.");
        }

        EnsureManagedAssemblyExists(managedDirectory, "Assembly-CSharp.dll");
        EnsureManagedAssemblyExists(managedDirectory, "Rewired_Core.dll");
        EnsureManagedAssemblyExists(managedDirectory, "mscorlib.dll");
        EnsureManagedAssemblyExists(managedDirectory, "UnityEngine.dll");
        EnsureManagedAssemblyExists(managedDirectory, "UnityEngine.CoreModule.dll");
    }

    private static void EnsureManagedAssemblyExists(string managedDirectory, string fileName)
    {
        var path = Path.Combine(managedDirectory, fileName);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Managed/{fileName} was not found.", path);
        }
    }

    private static string ExtractClassDatabase()
    {
        using var resource = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream(ClassDatabaseResourceName)
            ?? throw new InvalidOperationException("The embedded Unity class database is missing.");

        var path = Path.Combine(Path.GetTempPath(), $"lorelei-classdata-{Guid.NewGuid():N}.tpk");
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        resource.CopyTo(file);
        return path;
    }

    private static void WriteJsonValue(Utf8JsonWriter writer, AssetTypeValueField field)
    {
        if (field.TemplateField.IsArray)
        {
            writer.WriteStartArray();
            if (field.TemplateField.ValueType == AssetValueType.ByteArray)
            {
                foreach (var value in field.AsByteArray)
                {
                    writer.WriteNumberValue(value);
                }
            }
            else
            {
                foreach (var child in field.Children)
                {
                    WriteJsonValue(writer, child);
                }
            }

            writer.WriteEndArray();
            return;
        }

        if (field.Value is null)
        {
            writer.WriteStartObject();
            foreach (var child in field.Children)
            {
                writer.WritePropertyName(child.FieldName);
                WriteJsonValue(writer, child);
            }

            writer.WriteEndObject();
            return;
        }

        switch (field.Value.ValueType)
        {
            case AssetValueType.Bool:
                writer.WriteBooleanValue(field.AsBool);
                break;
            case AssetValueType.Int8:
            case AssetValueType.Int16:
            case AssetValueType.Int32:
                writer.WriteNumberValue(field.AsInt);
                break;
            case AssetValueType.Int64:
                writer.WriteNumberValue(field.AsLong);
                break;
            case AssetValueType.UInt8:
            case AssetValueType.UInt16:
            case AssetValueType.UInt32:
                writer.WriteNumberValue(field.AsUInt);
                break;
            case AssetValueType.UInt64:
                writer.WriteNumberValue(field.AsULong);
                break;
            case AssetValueType.String:
                writer.WriteStringValue(field.AsString);
                break;
            case AssetValueType.Float:
                writer.WriteNumberValue(field.AsFloat);
                break;
            case AssetValueType.Double:
                writer.WriteNumberValue(field.AsDouble);
                break;
            default:
                throw new NotSupportedException($"Cannot export {field.Value.ValueType} as JSON.");
        }
    }

    private sealed record InputManagerAsset(AssetFileInfo Info, AssetTypeValueField Field);
}
