using AssetsTools.NET;
using AssetsTools.NET.Extra;

namespace LoreleiAndSaneControls.Patcher;

internal sealed class ControlSchemePatch
{
    private readonly PatchDefinition _definition;

    public ControlSchemePatch(PatchDefinition definition)
    {
        _definition = definition;
    }

    public bool Apply(AssetTypeValueField inputManager)
    {
        var userData = inputManager["_userData"];
        var actions = GetArray(userData, "actions");
        ValidateReferencedActions(actions);

        var changed = false;
        foreach (var action in _definition.Actions)
        {
            changed |= AddAction(actions, action);
        }

        var categoryActionIds = FindActionCategoryIds(userData);
        foreach (var action in _definition.Actions)
        {
            changed |= AddInteger(categoryActionIds, action.Id);
        }

        var joystickMap = FindControllerMap(
            GetArray(userData, "joystickMaps"),
            _definition.JoystickMap,
            "joystick");
        var joystickMappings = GetArray(joystickMap, "actionElementMaps");

        foreach (var mapping in _definition.RemovedJoystickMappings)
        {
            changed |= RemoveMapping(
                joystickMappings,
                mapping.ActionId,
                mapping.ElementIdentifierId,
                keyboardKeyCode: 0);
        }

        foreach (var action in _definition.Actions)
        {
            changed |= AddMapping(
                joystickMappings,
                action.Id,
                action.JoystickElementIdentifierId,
                keyboardKeyCode: 0);
        }

        var keyboardMap = FindControllerMap(
            GetArray(userData, "keyboardMaps"),
            _definition.KeyboardMap,
            "keyboard");
        var keyboardMappings = GetArray(keyboardMap, "actionElementMaps");
        foreach (var action in _definition.Actions)
        {
            changed |= AddMapping(
                keyboardMappings,
                action.Id,
                elementIdentifierId: -1,
                action.KeyboardKeyCode);
        }

        return changed;
    }

    private void ValidateReferencedActions(AssetTypeValueField actions)
    {
        foreach (var removed in _definition.RemovedJoystickMappings)
        {
            var matches = actions.Children.Where(field => field["_id"].AsInt == removed.ActionId).ToList();
            if (matches.Count != 1
                || !string.Equals(matches[0]["_name"].AsString, removed.ActionName, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Action ID {removed.ActionId} is not the expected {removed.ActionName} action.");
            }
        }
    }

    private bool AddAction(AssetTypeValueField actions, PatchAction action)
    {
        var sameId = actions.Children.Where(field => field["_id"].AsInt == action.Id).ToList();
        var sameName = actions.Children.Where(field => string.Equals(
            field["_name"].AsString,
            action.Name,
            StringComparison.Ordinal)).ToList();

        if (sameId.Count == 0 && sameName.Count == 0)
        {
            var field = ValueBuilder.DefaultValueFieldFromArrayTemplate(actions);
            field["_id"].AsInt = action.Id;
            field["_name"].AsString = action.Name;
            field["_type"].AsInt = 1;
            field["_descriptiveName"].AsString = action.Name;
            field["_positiveDescriptiveName"].AsString = string.Empty;
            field["_negativeDescriptiveName"].AsString = string.Empty;
            field["_behaviorId"].AsInt = 0;
            field["_userAssignable"].AsInt = 1;
            field["_categoryId"].AsInt = _definition.ActionCategoryId;
            actions.Children.Add(field);
            return true;
        }

        if (sameId.Count != 1 || sameName.Count != 1 || !ReferenceEquals(sameId[0], sameName[0]))
        {
            throw new InvalidDataException(
                $"Action ID {action.Id} or name {action.Name} is already used by another action.");
        }

        if (!IsAction(sameId[0], action))
        {
            throw new InvalidDataException($"Existing action {action.Name} does not match the patch definition.");
        }

        return false;
    }

    private bool IsAction(AssetTypeValueField field, PatchAction action)
    {
        return field["_id"].AsInt == action.Id
            && string.Equals(field["_name"].AsString, action.Name, StringComparison.Ordinal)
            && field["_type"].AsInt == 1
            && string.Equals(field["_descriptiveName"].AsString, action.Name, StringComparison.Ordinal)
            && string.IsNullOrEmpty(field["_positiveDescriptiveName"].AsString)
            && string.IsNullOrEmpty(field["_negativeDescriptiveName"].AsString)
            && field["_behaviorId"].AsInt == 0
            && field["_userAssignable"].AsInt == 1
            && field["_categoryId"].AsInt == _definition.ActionCategoryId;
    }

    private AssetTypeValueField FindActionCategoryIds(AssetTypeValueField userData)
    {
        var categories = userData["actionCategoryMap"]["list"]["Array"];
        if (categories.IsDummy)
        {
            throw new InvalidDataException("The Rewired action category map is missing.");
        }

        var matches = categories.Children
            .Where(field => field["categoryId"].AsInt == _definition.ActionCategoryId)
            .ToList();
        if (matches.Count != 1)
        {
            throw new InvalidDataException(
                $"Expected exactly one action category {_definition.ActionCategoryId}, found {matches.Count}.");
        }

        return matches[0]["actionIds"]["Array"];
    }

    private static bool AddInteger(AssetTypeValueField array, int value)
    {
        var count = array.Children.Count(field => field.AsInt == value);
        if (count > 1)
        {
            throw new InvalidDataException($"Value {value} is duplicated in an action category.");
        }

        if (count == 1)
        {
            return false;
        }

        var field = ValueBuilder.DefaultValueFieldFromArrayTemplate(array);
        field.AsInt = value;
        array.Children.Add(field);
        return true;
    }

    private static AssetTypeValueField FindControllerMap(
        AssetTypeValueField maps,
        ControllerMapSelector selector,
        string mapType)
    {
        var matches = maps.Children.Where(field =>
            field["categoryId"].AsInt == selector.CategoryId
            && field["layoutId"].AsInt == selector.LayoutId
            && (selector.HardwareGuid is null
                || string.Equals(
                    field["hardwareGuidString"].AsString,
                    selector.HardwareGuid,
                    StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (matches.Count != 1)
        {
            throw new InvalidDataException(
                $"Expected exactly one matching {mapType} map, found {matches.Count}.");
        }

        return matches[0];
    }

    private static bool RemoveMapping(
        AssetTypeValueField mappings,
        int actionId,
        int elementIdentifierId,
        int keyboardKeyCode)
    {
        var changed = false;
        for (var index = mappings.Children.Count - 1; index >= 0; index--)
        {
            if (!IsMapping(mappings.Children[index], actionId, elementIdentifierId, keyboardKeyCode))
            {
                continue;
            }

            mappings.Children.RemoveAt(index);
            changed = true;
        }

        return changed;
    }

    private static bool AddMapping(
        AssetTypeValueField mappings,
        int actionId,
        int elementIdentifierId,
        int keyboardKeyCode)
    {
        var count = CountMappings(mappings, actionId, elementIdentifierId, keyboardKeyCode);
        if (count > 1)
        {
            throw new InvalidDataException(
                $"Action {actionId} has duplicate bindings for element {elementIdentifierId}.");
        }

        if (count == 1)
        {
            return false;
        }

        var sameSlot = mappings.Children.Any(field =>
            field["_actionId"].AsInt == actionId
            && field["_elementIdentifierId"].AsInt == elementIdentifierId
            && field["_keyboardKeyCode"].AsInt == keyboardKeyCode);
        if (sameSlot)
        {
            throw new InvalidDataException(
                $"Action {actionId} has a conflicting binding for element {elementIdentifierId}.");
        }

        var mapping = ValueBuilder.DefaultValueFieldFromArrayTemplate(mappings);
        mapping["_actionCategoryId"].AsInt = 0;
        mapping["_actionId"].AsInt = actionId;
        mapping["_elementType"].AsInt = 1;
        mapping["_elementIdentifierId"].AsInt = elementIdentifierId;
        mapping["_axisRange"].AsInt = 0;
        mapping["_invert"].AsInt = 0;
        mapping["_axisContribution"].AsInt = 0;
        mapping["_keyboardKeyCode"].AsInt = keyboardKeyCode;
        mapping["_modifierKey1"].AsInt = 0;
        mapping["_modifierKey2"].AsInt = 0;
        mapping["_modifierKey3"].AsInt = 0;
        mappings.Children.Add(mapping);
        return true;
    }

    private static int CountMappings(
        AssetTypeValueField mappings,
        int actionId,
        int elementIdentifierId,
        int keyboardKeyCode)
    {
        return mappings.Children.Count(field =>
            IsMapping(field, actionId, elementIdentifierId, keyboardKeyCode));
    }

    private static bool IsMapping(
        AssetTypeValueField field,
        int actionId,
        int elementIdentifierId,
        int keyboardKeyCode)
    {
        return field["_actionCategoryId"].AsInt == 0
            && field["_actionId"].AsInt == actionId
            && field["_elementType"].AsInt == 1
            && field["_elementIdentifierId"].AsInt == elementIdentifierId
            && field["_axisRange"].AsInt == 0
            && field["_invert"].AsInt == 0
            && field["_axisContribution"].AsInt == 0
            && field["_keyboardKeyCode"].AsInt == keyboardKeyCode
            && field["_modifierKey1"].AsInt == 0
            && field["_modifierKey2"].AsInt == 0
            && field["_modifierKey3"].AsInt == 0;
    }

    private static AssetTypeValueField GetArray(AssetTypeValueField parent, string fieldName)
    {
        var array = parent[fieldName]["Array"];
        if (array.IsDummy)
        {
            throw new InvalidDataException($"The Rewired {fieldName} array is missing.");
        }

        return array;
    }
}
