using System.Reflection;
using System.Text.Json;

namespace LoreleiAndSaneControls.Patcher;

internal sealed record PatchDefinition(
    int ActionCategoryId,
    IReadOnlyList<PatchAction> Actions,
    ControllerMapSelector JoystickMap,
    ControllerMapSelector KeyboardMap,
    IReadOnlyList<RemovedControllerMapping> RemovedJoystickMappings)
{
    private const string PatchResourceName =
        "LoreleiAndSaneControls.Patcher.Resources.control_scheme_patch.json";

    public static PatchDefinition Load(string? path = null)
    {
        using var stream = path is null
            ? Assembly.GetExecutingAssembly().GetManifestResourceStream(PatchResourceName)
                ?? throw new InvalidOperationException("The embedded control scheme patch is missing.")
            : File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);

        var root = document.RootElement;
        var schema = root.GetProperty("$schema").GetString();
        if (!string.Equals(schema, "lorelei-control-scheme-patch/v1", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Unsupported control scheme patch schema: {schema}");
        }

        var actions = root.GetProperty("actions")
            .EnumerateArray()
            .Select(ReadAction)
            .ToArray();

        if (actions.Select(action => action.Id).Distinct().Count() != actions.Length
            || actions.Select(action => action.Name).Distinct(StringComparer.Ordinal).Count() != actions.Length)
        {
            throw new InvalidDataException("The patch contains duplicate action IDs or names.");
        }

        var joystickMap = root.GetProperty("joystickMap");
        var removedMappings = joystickMap.GetProperty("remove")
            .EnumerateArray()
            .Select(element => new RemovedControllerMapping(
                element.GetProperty("actionId").GetInt32(),
                element.GetProperty("action").GetString()
                    ?? throw new InvalidDataException("A removed mapping has no action name."),
                element.GetProperty("elementIdentifierId").GetInt32()))
            .ToArray();

        return new PatchDefinition(
            root.GetProperty("actionCategoryId").GetInt32(),
            actions,
            ReadSelector(joystickMap.GetProperty("match")),
            ReadSelector(root.GetProperty("keyboardMap").GetProperty("match")),
            removedMappings);
    }

    private static PatchAction ReadAction(JsonElement element)
    {
        var keyboard = element.GetProperty("keyboard");
        var joystick = element.GetProperty("joystick");
        return new PatchAction(
            element.GetProperty("id").GetInt32(),
            element.GetProperty("name").GetString()
                ?? throw new InvalidDataException("A patch action has no name."),
            keyboard.GetProperty("keyCode").GetInt32(),
            joystick.GetProperty("elementIdentifierId").GetInt32());
    }

    private static ControllerMapSelector ReadSelector(JsonElement element)
    {
        return new ControllerMapSelector(
            element.GetProperty("categoryId").GetInt32(),
            element.GetProperty("layoutId").GetInt32(),
            element.TryGetProperty("hardwareGuid", out var hardwareGuid)
                ? hardwareGuid.GetString()
                : null);
    }
}

internal sealed record PatchAction(
    int Id,
    string Name,
    int KeyboardKeyCode,
    int JoystickElementIdentifierId);

internal sealed record ControllerMapSelector(
    int CategoryId,
    int LayoutId,
    string? HardwareGuid);

internal sealed record RemovedControllerMapping(
    int ActionId,
    string ActionName,
    int ElementIdentifierId);
