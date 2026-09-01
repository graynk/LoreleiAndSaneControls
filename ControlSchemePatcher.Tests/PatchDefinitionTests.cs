using LoreleiAndSaneControls.Patcher;
using Xunit;

namespace ControlSchemePatcher.Tests;

public sealed class PatchDefinitionTests
{
    [Fact]
    public void EmbeddedPatchContainsOnlyTheExpectedModActionsAndBindings()
    {
        var patch = PatchDefinition.Load();

        Assert.Equal(0, patch.ActionCategoryId);
        Assert.Equal(
            [
                new PatchAction(95, "Map", 109, 14),
                new PatchAction(96, "Back", 8, 5),
                new PatchAction(97, "Prev", 113, 10),
                new PatchAction(98, "Next", 101, 12)
            ],
            patch.Actions);
        Assert.Equal(7, patch.RemovedJoystickMappings.Count);
        Assert.All(
            patch.RemovedJoystickMappings,
            mapping => Assert.Contains(mapping.ActionName, new[] { "Interact", "Dialog_Confirm", "Menu" }));
    }

    [Fact]
    public void LoadsPatchFromAJsonFile()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Reference", "control_scheme_patch.json");
        var expected = PatchDefinition.Load();
        var actual = PatchDefinition.Load(path);

        Assert.Equal(expected.ActionCategoryId, actual.ActionCategoryId);
        Assert.Equal(expected.Actions.ToArray(), actual.Actions.ToArray());
        Assert.Equal(expected.JoystickMap, actual.JoystickMap);
        Assert.Equal(expected.KeyboardMap, actual.KeyboardMap);
        Assert.Equal(expected.RemovedJoystickMappings.ToArray(), actual.RemovedJoystickMappings.ToArray());
    }
}
