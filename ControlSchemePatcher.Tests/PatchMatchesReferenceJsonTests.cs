using System.Text.Json.Nodes;
using LoreleiAndSaneControls.Patcher;
using Xunit;

namespace ControlSchemePatcher.Tests;

public sealed class PatchMatchesReferenceJsonTests
{
    [Fact]
    public void PatchesTheOriginalBinaryAsset()
    {
        var referenceDirectory = Path.Combine(AppContext.BaseDirectory, "Reference");
        RequireLocalFiles(
            Path.Combine(referenceDirectory, "globalgamemanagers.assets"),
            Path.Combine(referenceDirectory, "Managed", "Assembly-CSharp.dll"),
            Path.Combine(referenceDirectory, "Managed", "Rewired_Core.dll"));

        var gameRoot = Path.Combine(Path.GetTempPath(), $"lorelei-tests-{Guid.NewGuid():N}");
        var dataDirectory = Path.Combine(gameRoot, "Lorelei and the Laser Eyes_Data");
        var managedDirectory = Path.Combine(dataDirectory, "Managed");
        Directory.CreateDirectory(managedDirectory);

        try
        {
            var assetsPath = Path.Combine(dataDirectory, "globalgamemanagers.assets");
            File.Copy(Path.Combine(referenceDirectory, "globalgamemanagers.assets"), assetsPath);

            foreach (var assemblyPath in Directory.GetFiles(Path.Combine(referenceDirectory, "Managed"), "*.dll"))
            {
                File.Copy(assemblyPath, Path.Combine(managedDirectory, Path.GetFileName(assemblyPath)));
            }

            var originalBytes = File.ReadAllBytes(assetsPath);
            var patchResult = Program.Patch(gameRoot);
            Assert.True(patchResult.Changed);
            Assert.Equal(originalBytes, File.ReadAllBytes(patchResult.BackupPath!));

            var extracted = Program.Extract(gameRoot);
            var expected = Load("InputManager-modified.json");
            var actual = JsonNode.Parse(File.ReadAllText(extracted.OutputPath));
            Assert.True(SemanticallyEqual(expected, actual));

            Assert.False(Program.Patch(gameRoot).Changed);
        }
        finally
        {
            Directory.Delete(gameRoot, recursive: true);
        }
    }

    [Fact]
    public void EmbeddedPatchDescribesEverySemanticDifferenceAndNothingElse()
    {
        RequireLocalFiles(Path.Combine(AppContext.BaseDirectory, "Reference", "InputManager-original.json"));

        var original = Load("InputManager-original.json");
        var modified = Load("InputManager-modified.json");
        var patch = PatchDefinition.Load();

        var originalActions = GetArray(original, "_userData", "actions", "Array");
        var modifiedActions = GetArray(modified, "_userData", "actions", "Array");
        var addedActions = modifiedActions
            .Where(candidate => !originalActions.Any(originalAction => JsonNode.DeepEquals(candidate, originalAction)))
            .Select(ActionIdentity)
            .ToArray();
        Assert.Equal(patch.Actions.Select(action => (action.Id, action.Name)), addedActions);
        RemoveWhere(modifiedActions, node => patch.Actions.Any(action => ActionIdentity(node) == (action.Id, action.Name)));

        var originalCategory = GetArray(
            original,
            "_userData",
            "actionCategoryMap",
            "list",
            "Array")[0]!["actionIds"]!["Array"]!.AsArray();
        var modifiedCategory = GetArray(
            modified,
            "_userData",
            "actionCategoryMap",
            "list",
            "Array")[0]!["actionIds"]!["Array"]!.AsArray();
        var addedCategoryIds = modifiedCategory
            .Select(node => node!.GetValue<int>())
            .Except(originalCategory.Select(node => node!.GetValue<int>()))
            .ToArray();
        Assert.Equal(patch.Actions.Select(action => action.Id), addedCategoryIds);
        RemoveWhere(modifiedCategory, node => patch.Actions.Any(action => node!.GetValue<int>() == action.Id));

        var originalJoystick = ControllerMappings(original, "joystickMaps");
        var modifiedJoystick = ControllerMappings(modified, "joystickMaps");
        Assert.Equal(
            patch.RemovedJoystickMappings.Select(mapping =>
                (mapping.ActionId, mapping.ElementIdentifierId, KeyCode: 0)),
            Difference(originalJoystick, modifiedJoystick).Select(MappingIdentity));
        Assert.Equal(
            patch.Actions.Select(action =>
                (action.Id, action.JoystickElementIdentifierId, KeyCode: 0)),
            Difference(modifiedJoystick, originalJoystick).Select(MappingIdentity));
        RemoveWhere(
            originalJoystick,
            node => patch.RemovedJoystickMappings.Any(mapping =>
                MappingIdentity(node) == (mapping.ActionId, mapping.ElementIdentifierId, 0)));
        RemoveWhere(
            modifiedJoystick,
            node => patch.Actions.Any(action =>
                MappingIdentity(node) == (action.Id, action.JoystickElementIdentifierId, 0)));

        var originalKeyboard = ControllerMappings(original, "keyboardMaps");
        var modifiedKeyboard = ControllerMappings(modified, "keyboardMaps");
        Assert.Empty(Difference(originalKeyboard, modifiedKeyboard));
        Assert.Equal(
            patch.Actions.Select(action => (action.Id, ElementId: -1, action.KeyboardKeyCode)),
            Difference(modifiedKeyboard, originalKeyboard).Select(MappingIdentity));
        RemoveWhere(
            modifiedKeyboard,
            node => patch.Actions.Any(action =>
                MappingIdentity(node) == (action.Id, -1, action.KeyboardKeyCode)));

        Assert.True(SemanticallyEqual(original, modified),
            "The reference JSON files contain an unrelated semantic difference.");
    }

    private static JsonNode Load(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Reference", fileName);
        return JsonNode.Parse(File.ReadAllText(path))
            ?? throw new InvalidDataException($"Could not parse {path}.");
    }

    private static void RequireLocalFiles(params string[] paths)
    {
        foreach (var path in paths)
        {
            Assert.True(File.Exists(path), $"Missing local test fixture: {path}");
        }
    }

    private static JsonArray GetArray(JsonNode root, params string[] path)
    {
        JsonNode? current = root;
        foreach (var part in path)
        {
            current = current?[part];
        }

        return current?.AsArray()
            ?? throw new InvalidDataException($"JSON array {string.Join('.', path)} was not found.");
    }

    private static JsonArray ControllerMappings(JsonNode root, string mapName)
    {
        return GetArray(root, "_userData", mapName, "Array")[0]!["actionElementMaps"]!["Array"]!.AsArray();
    }

    private static (int Id, string Name) ActionIdentity(JsonNode? node)
    {
        return (
            node!["_id"]!.GetValue<int>(),
            node["_name"]!.GetValue<string>());
    }

    private static (int ActionId, int ElementId, int KeyCode) MappingIdentity(JsonNode? node)
    {
        return (
            node!["_actionId"]!.GetValue<int>(),
            node["_elementIdentifierId"]!.GetValue<int>(),
            node["_keyboardKeyCode"]!.GetValue<int>());
    }

    private static IEnumerable<JsonNode?> Difference(JsonArray left, JsonArray right)
    {
        return left.Where(candidate => !right.Any(other => JsonNode.DeepEquals(candidate, other)));
    }

    private static void RemoveWhere(JsonArray array, Func<JsonNode?, bool> predicate)
    {
        for (var index = array.Count - 1; index >= 0; index--)
        {
            if (predicate(array[index]))
            {
                array.RemoveAt(index);
            }
        }
    }

    private static bool SemanticallyEqual(JsonNode? left, JsonNode? right)
    {
        if (left is JsonValue leftValue && right is JsonValue rightValue)
        {
            if (leftValue.TryGetValue<double>(out var leftNumber)
                && rightValue.TryGetValue<double>(out var rightNumber))
            {
                return leftNumber.Equals(rightNumber);
            }

            return JsonNode.DeepEquals(left, right);
        }

        if (left is JsonObject leftObject && right is JsonObject rightObject)
        {
            return leftObject.Count == rightObject.Count
                && leftObject.All(property =>
                    rightObject.TryGetPropertyValue(property.Key, out var rightChild)
                    && SemanticallyEqual(property.Value, rightChild));
        }

        if (left is JsonArray leftArray && right is JsonArray rightArray)
        {
            return leftArray.Count == rightArray.Count
                && leftArray.Zip(rightArray).All(pair => SemanticallyEqual(pair.First, pair.Second));
        }

        return left is null && right is null;
    }
}
