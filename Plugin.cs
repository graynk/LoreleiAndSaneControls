using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace LoreleiAndSaneControls;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInProcess("Lorelei and the Laser Eyes.exe")]
public class Plugin : BaseUnityPlugin
{
    private const string ConfirmMethodName = "OnPuzzleViewConfirm";
    private const string InputMethodName = "OnPuzzleViewSelectInput";
    private static readonly Dictionary<Action, bool> ButtonPressed = new();
    private static readonly Interact[] Maptubes = new Interact[5];
    private static Interact _mazeMap;
    private static Interact _forestMap;
    private static ManualLogSource _logger;
    private static Harmony _hi;
    private static string _currentlyOpenMapName = "";
    private static string _lastKnownMapName = "";

    private void Awake()
    {
        _logger = Logger;
        _logger.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded.");
        _hi = Harmony.CreateAndPatchAll(typeof(Plugin));
    }

    private void OnDestroy()
    {
        _hi?.UnpatchSelf();
    }

    [HarmonyPatch(typeof(LoreleiInput), "ReadInputState")]
    [HarmonyPrefix]
    private static bool ReadInput()
    {
        SGGameInput gameInput = SGFW.GameInput;

        ButtonPressed[Action.Map] = gameInput.GetButtonDown("Map");
        ButtonPressed[Action.Back] = gameInput.GetButtonDown("Back");
        ButtonPressed[Action.Prev] = gameInput.GetButtonDown("Prev");
        ButtonPressed[Action.Next] = gameInput.GetButtonDown("Next");
        ButtonPressed[Action.Interact] = gameInput.GetButtonDown("Interact");

        return true;
    }

    // Goes through EVERY interact object to find Map tubes (which normally are somewhere in the level on each floor)
    [HarmonyPatch(typeof(Lorelei), "Init")]
    [HarmonyPostfix]
    private static void StealMaptubesRefsIfAbsent()
    {
        var hasNullRefs = _mazeMap == null || _forestMap == null;
        foreach (Interact maptube in Maptubes)
            if (maptube == null)
            {
                hasNullRefs = true;
                break;
            }

        if (!hasNullRefs) return;

        var allInteractsInGame = FindObjectsByType<Interact>(FindObjectsSortMode.None);

        var found = 0;
        foreach (var interact in allInteractsInGame)
        {
            switch (interact.name)
            {
                case "Maptube_Cellar_Interact":
                    Maptubes[0] = interact;
                    break;
                case "Maptube_Floor01_Interact":
                    Maptubes[1] = interact;
                    break;
                case "Maptube_Floor02_Interact":
                    Maptubes[2] = interact;
                    break;
                case "Maptube_Floor03_Interact":
                    Maptubes[3] = interact;
                    break;
                case "Maptube_Loft_Interact":
                    Maptubes[4] = interact;
                    break;
                case "pfMazeposter01":
                    _mazeMap = interact;
                    break;
                case "Sign_Trail": 
                    _forestMap = interact; 
                    break;
                default:
                    continue;
            }
        }
    }

    private static Interact GetRequestedFloorMapInteract(string currentFloor, int offset)
    {
        var index = -1;

        for (var i = 0; i < Maptubes.Length; i++)
        {
            if (Maptubes[i]?.name == currentFloor)
            {
                index = i;
                break;
            }
        }

        var newFloorIndex = index + offset;
        if (index == -1 || newFloorIndex < 0 || newFloorIndex > Maptubes.Length) return null;

        return Maptubes[newFloorIndex];
    }


    // Deals with map opening / switching floors on a map. Also closes the inventory
    [HarmonyPatch(typeof(InteractHandler), "OnUpdate")]
    [HarmonyPrefix]
    private static bool HandleCustomInputInUpdateLoop(ref int ___m_nActiveDialogSelectedOption, ref bool bInteractButton)
    {
        var isMapOpen = _currentlyOpenMapName != "";
        // Map was closed
        if (isMapOpen && ButtonPressed[Action.Interact])
        {
            _currentlyOpenMapName = "";
            return true;
        }
        
        // Do nothing on puzzle views
        if (SGFW.GameLogic.LevelLogic.IsPuzzleViewActive() || SGFW.GameLogic.LevelLogic.IsPuzzleViewPending())
        {
            return true;
        }
        
        // Makes back button behave like a regular interact button to back out of regular interacts
        // Back button takes priority over map button
        // In case of inventory this _also_ depends on patch making IsExitItemSelected return true and GetSelectedItem return exit item
        // And in case of ByteSeyes it makes it press the exit node also.
        if (ButtonPressed[Action.Back] && InteractHandler.Instance.GetActiveDialog() != null)
        {
            if (InteractHandler.Instance.IsActiveDialogComputer() || ConsoleLogic.Instance.IsRunning()) return true;
            if (ContemplateLogic.Instance.IsMainUIActive()) ___m_nActiveDialogSelectedOption = 0;
            bInteractButton = true;
            return true;
        }

        // Don't let map be open on active dialogs
        if (!isMapOpen && InteractHandler.Instance.GetActiveDialog() != null) return true;

        var mapButtonPressed = ButtonPressed[Action.Map];
        var prevButtonPressed = ButtonPressed[Action.Prev];
        var nextButtonPressed = ButtonPressed[Action.Next];
        // If we're not requesting a map and not switching between floors - return early 
        if ((isMapOpen || !mapButtonPressed) && (!isMapOpen || (!prevButtonPressed && !nextButtonPressed))) return true;

        RoomParent room = FuzzyGameState.LoreleiState.l_hCurrentLoreleiRoom;
        if (room == null)
        {
            _logger.LogInfo("room instance is null");
            return true;
        }
        
        StealMaptubesRefsIfAbsent();

        _ = room.mapLink.GetMapAndRoomInstance(out FuzzyMap mapRef, out _);
        if (mapRef == null)
        {
            switch (room.roomName)
            {
                case "SGT_G_R_REDMAZE_NAME" or "SGT_COMMON_S_ROOM_QUIZ_CLUB":
                    InteractHandler.Instance.ShowDialog(_mazeMap, "", false);
                    return false;
                case "SGT_G_R_FOREST_NAME":
                    InteractHandler.Instance.ShowDialog(_forestMap, "", false);
                    return false;
            }

            if (_lastKnownMapName == "")
            {
                _lastKnownMapName = "Maptube_Floor01_Interact";
            }
            _currentlyOpenMapName = _lastKnownMapName;
        }

        var currentFloor = _currentlyOpenMapName == ""
            ? $"Maptube_{mapRef.name.Substring("pfHotelmap".Length)}_Interact"
            : _currentlyOpenMapName;

        
        var offset = 0;
        if (prevButtonPressed)
            offset = -1;
        else if (nextButtonPressed) offset = 1;

        Interact requestedFloorMap = GetRequestedFloorMapInteract(currentFloor, offset);
        if (requestedFloorMap == null)
        {
            _logger.LogInfo($"offset {offset} from {currentFloor} not found");
            return true;
        }

        
        var mapDialog = requestedFloorMap.hDialogs.Find(dialog => dialog.sDesc == "ViewMap");
        if (mapDialog == null || !mapDialog.IsRequiredFulfilled(InteractHandler.Instance))
        {
            _logger.LogInfo($"The map tube {requestedFloorMap.name} has not been solved yet");
            return true;
        }

        InteractHandler.Instance.ShowDialog(requestedFloorMap, "", false);
        _currentlyOpenMapName = requestedFloorMap.name;
        _lastKnownMapName = requestedFloorMap.name;
        return false;
    }

    // This just makes OnUpdate for every puzzle return true, which in Lorelei terms means "close that, we're done"
    [HarmonyPatch(typeof(PuzzleView), "OnUpdate")]
    [HarmonyPostfix]
    private static void BackOutOfAPuzzle(ref bool __result)
    {
        if (ButtonPressed[Action.Back])
        {
            __result = true;
        }
    }
    
    // Closes inventory on "Back" action when Inventory is in "Inspect" mode
    [HarmonyPatch(typeof(UIElement_Inventory), "IsExitItemSelected")]
    [HarmonyPrefix]
    private static bool BackOutOfABagInInspectMode(ref bool __result)
    {
        if (ButtonPressed[Action.Back])
        {
            __result = true;
            return false;
        }

        return true;
    }
    
    // Closes inventory on "Back" action when Inventory is in "Use" mode
    [HarmonyPatch(typeof(UIElement_Inventory), "GetSelectedItem")]
    [HarmonyPrefix]
    private static bool BackOutOfABagInUseMode(ref InventoryItem __result)
    {
        if (ButtonPressed[Action.Back])
        {
            // I hope the index for the exit item never stops being zero, because I can't be bothered to search for it through reflection
            __result = InteractHandler.Instance.GetItemByIndex(0);
            return false;
        }

        return true;
    }
    
    // Closes ByteSeyes on "Back" action
    [HarmonyPatch(typeof(MiniGameOS), "OnUpdate")]
    [HarmonyPrefix]
    private static bool BackOutOfByteSeyesState(ref ByteSeyesState ___m_nState)
    {
        if (___m_nState == ByteSeyesState.Menu && ButtonPressed[Action.Back])
        {
            ___m_nState = ByteSeyesState.RunMenuItem;
            return true;
        }

        return true;
    }
    
    // Closes ByteSeyes on "Back" action
    [HarmonyPatch(typeof(MiniGameOS), "ExecuteMenuItem")]
    [HarmonyPrefix]
    private static bool BackOutOfByteSeyesUI(ref MiniGameOS.MenuItems nMenuItem)
    {
        if (ButtonPressed[Action.Back])
        {
            nMenuItem = MiniGameOS.MenuItems.PowerOff;
            return true;
        }

        return true;
    }
    
    // Closes the list of Photographic memories on "Back" action
    [HarmonyPatch(typeof(UIElement_PhotographicMemory), "OnUpdate")]
    [HarmonyPrefix]
    private static bool BackOutOfMemories(ref UIElement_PhotographicMemory.MemoryAction __result)
    {
        if (ButtonPressed[Action.Back])
        {
            __result = UIElement_PhotographicMemory.MemoryAction.Back;
            return false;
        }

        return true;
    }

    // For padlocks converts what would normally be an "interact" action (e.g. on a dial) to a "check solution" action
    [HarmonyPatch(typeof(PadLockLogic), ConfirmMethodName)]
    [HarmonyPatch(typeof(KeyCabinetPadLock), ConfirmMethodName)]
    [HarmonyPatch(typeof(LetterLockLogic), ConfirmMethodName)]
    [HarmonyPatch(typeof(RomanPadLockLogic), ConfirmMethodName)]
    [HarmonyPatch(typeof(MapTubeLogic), ConfirmMethodName)]
    [HarmonyPatch(typeof(VerticalPadLockLogic), ConfirmMethodName)]
    [HarmonyPatch(typeof(VerticalRotatedPadLockLogic), ConfirmMethodName)]
    [HarmonyPrefix]
    private static bool AlwaysPushConfirmButton(ref ConfirmButtonDuplicate ___m_hConfirmButton)
    {
        ___m_hConfirmButton.bIsPushed = true;
        return true;
    }

    // For padlocks turn UP/DOWN actions into "rotate the dial" action
    [HarmonyPatch(typeof(PadLockLogic), InputMethodName)]
    [HarmonyPatch(typeof(KeyCabinetPadLock), InputMethodName)]
    [HarmonyPatch(typeof(LetterLockLogic), InputMethodName)]
    [HarmonyPatch(typeof(RomanPadLockLogic), InputMethodName)]
    [HarmonyPrefix]
    private static bool TreatVerticalDirectionAsInput(
        ref ReactPreset ___wheelSpinPreset,
        ref GameObject ___leftSideShakeParent,
        ref GameObject ___rightSideShakeParent,
        ref bool ___m_bSnapWheelRotation,
        ref int ___m_nLastLeftShakeIndex,
        ref int ___m_nLastRightShakeIndex,
        ref List<ReactPreset> ___m_hLeftShakePresets,
        ref List<ReactPreset> ___m_hRightShakePresets,
        ref List<WheelButton> ___m_hWheelButtons,
        ref ReactHandler ___m_hReactHandler,
        SGMenuNavigator.INPUTDIR nInput,
        SelectableObject hSelection = null
    )
    {
        var letOriginalRun = HandleWheelRotation(
            ___wheelSpinPreset,
            ref ___m_bSnapWheelRotation,
            ___m_hWheelButtons,
            ___m_hReactHandler,
            nInput,
            SGMenuNavigator.INPUTDIR.UP,
            SGMenuNavigator.INPUTDIR.DOWN,
            out int selectedButtonIndex,
            hSelection);
        if (letOriginalRun)
        {
            return true;
        }

        // Some weird shaking code from the original function
        if (selectedButtonIndex < ___m_hWheelButtons.Count / 2)
            RequestPadLockShake(___m_hReactHandler, ___leftSideShakeParent, ___m_hLeftShakePresets,
                ref ___m_nLastLeftShakeIndex);
        else
            RequestPadLockShake(___m_hReactHandler, ___rightSideShakeParent, ___m_hRightShakePresets,
                ref ___m_nLastRightShakeIndex);

        return false;
    }

    // For vertical locks convert LEFT/RIGHT action into "rotate the dial" action
    [HarmonyPatch(typeof(MapTubeLogic), InputMethodName)]
    [HarmonyPatch(typeof(VerticalPadLockLogic), InputMethodName)]
    [HarmonyPatch(typeof(VerticalRotatedPadLockLogic), InputMethodName)]
    [HarmonyPrefix]
    private static bool TreatHorizontalDirectionAsInput(
        ref ReactPreset ___wheelSpinPreset,
        ref bool ___m_bSnapWheelRotation,
        ref List<WheelButton> ___m_hWheelButtons,
        ref ReactHandler ___m_hReactHandler,
        SGMenuNavigator.INPUTDIR nInput,
        SelectableObject hSelection = null
    )
    {
        return HandleWheelRotation(
            ___wheelSpinPreset,
            ref ___m_bSnapWheelRotation,
            ___m_hWheelButtons,
            ___m_hReactHandler,
            nInput,
            SGMenuNavigator.INPUTDIR.LEFT,
            SGMenuNavigator.INPUTDIR.RIGHT,
            out _,
            hSelection);
    }

    // Generalized function to rotate a wheel dial in a puzzle. Inverts the direction if necessary
    private static bool HandleWheelRotation(
        ReactPreset rotationPreset,
        ref bool ___m_bSnapWheelRotation,
        List<WheelButton> ___m_hWheelButtons,
        ReactHandler ___m_hReactHandler,
        SGMenuNavigator.INPUTDIR nInput,
        SGMenuNavigator.INPUTDIR incDirection,
        SGMenuNavigator.INPUTDIR decDirection,
        out int selectedButtonIndex,
        SelectableObject hSelection = null
    )
    {
        if (nInput != incDirection && nInput != decDirection)
        {
            selectedButtonIndex = -1;
            return true;
        }

        if (rotationPreset == null)
        {
            selectedButtonIndex = -1;
            return true;
        }

        // If the input counts down - invert the rotation animation
        if (nInput == decDirection)
        {
            rotationPreset = InvertRotationPreset(rotationPreset);
        }

        selectedButtonIndex =
            ___m_hWheelButtons.FindIndex(wheelButton => wheelButton.hSelectableObject == hSelection);
        var selectedButton = ___m_hWheelButtons[selectedButtonIndex];
        if (!___m_hReactHandler.IsEventsFinished(selectedButton.hGameObject))
        {
            return true;
        }

        // If the input counts up or down and all previous events have finished - rotate the wheel
        ___m_bSnapWheelRotation = true;
        ___m_hReactHandler.RequestEvents(selectedButton.hGameObject, rotationPreset, 0.0f, false, false);
        return false;
    }

    // Checks which axis have animation keys and inverts them
    private static ReactPreset InvertRotationPreset(ReactPreset rotationPreset)
    {
        rotationPreset = Instantiate(rotationPreset);
        var reactEvent = rotationPreset.hReactEvents[0].hReact;
        AnimationCurve[] rotations = [reactEvent.rotationX, reactEvent.rotationY, reactEvent.rotationZ];
        // The animation seems to be pretty simplistic - just 3 keyframes on 1 axis: 0, 0.67, 1
        foreach (var rotation in rotations)
        {
            if (rotation.keys.Length == 0) continue;

            var originalKeys = rotation.keys;
            var invertedKeys = new Keyframe[originalKeys.Length];

            for (var index = 0; index < originalKeys.Length; ++index)
                invertedKeys[index] = new Keyframe(originalKeys[index].time, -originalKeys[index].value);

            rotation.SetKeys(invertedKeys);
        }

        return rotationPreset;
    }

    // A copy-paste from PadLockLogic to avoid Traverse-related shenanigans (which don't work well with ref arguments)
    private static void RequestPadLockShake(
        ReactHandler reactHandler,
        GameObject hGameObject,
        List<ReactPreset> hPresets,
        ref int nLastIndex)
    {
        if (hGameObject == null || hPresets.Count == 0)
            return;
        var index = Random.Range(0, hPresets.Count);
        while (index == nLastIndex && hPresets.Count > 1)
            index = Random.Range(0, hPresets.Count);
        reactHandler.RequestEvents(hGameObject, hPresets[index], 0.0f, false, true);
        nLastIndex = index;
    }

    // These two classes are a copy of protected classes from PadLockLogic.
    // I'm not sure how to avoid copy-pasting them, but they're small enough, and it seems to work, so whatever
    // Weirdly enough, there seems to be a copy of these in all relevant padlocks, with different constants.
    private class ConfirmButtonDuplicate
    {
        public bool bIsPushed;
        public GameObject hGameObject;
        public SelectableObject hSelectableObject;
    }

    private class WheelButton
    {
        public const float STEPANGLE = 36f;
        public GameObject hGameObject;
        public SelectableObject hSelectableObject;
        public int nCurrentStep = -1;
        public int nInitialStep = -1;
    }

    private enum Action
    {
        Prev,
        Next,
        Map,
        Back,
        Interact
    }
    
    private enum ByteSeyesState
    {
        Off,
        Boot,
        Menu,
        RunMenuItem,
        RunMenuItemComplete,
    }
}