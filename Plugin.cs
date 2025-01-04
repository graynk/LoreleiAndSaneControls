using System.Collections.Generic;
using System.Diagnostics.Contracts;
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
  private static ManualLogSource _logger;
  private static Harmony _hi;
  private static bool customButtonPressed = false;
  private static bool upButtonPressed = false;
  private static bool downButtonPressed = false;
  private static string currentMap = "";
  private static bool doOnce = false;
  private static bool upOnce = false;
  private static bool downOnce = false;
  private static readonly List<Interact> Maptubes = new(5);

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
  
  [HarmonyPatch(typeof(Lorelei), "Init")]
  [HarmonyPostfix]
  private static void StealMaptubesRefsIfAbsent()
  {
    // it _should_ be only 5, but just as a failsafe let's assume something went wrong and
    // there are actually more refs for some reason - still won't stop the logic from working
    if (Maptubes.Count >= 5)
    {
      return;
    }
    
    var allInteractsInGame = FindObjectsByType<Interact>(FindObjectsSortMode.None);
    foreach (var interact in allInteractsInGame)
    {
      if (interact.name.StartsWith("Maptube") && interact.name.EndsWith("_Interact"))
      {
        _logger.LogInfo($"{interact.name}");
        Maptubes.Add(interact);
      }
    }
  }
  
  [HarmonyPatch(typeof(LoreleiInput), "ReadInputState")]
  [HarmonyPrefix]
  private static bool ReadInput()
  {
    SGGameInput gameInput = SGFW.GameInput;
    if (currentMap != "" && gameInput.GetButton("Interact"))
    {
      currentMap = "";
    }
    if (gameInput.GetButton("Map"))
    {
      customButtonPressed = true;
    }
    else
    {
      customButtonPressed = false;
      doOnce = false;
    }
    if (gameInput.GetButton("Dialog_Up"))
    {
      upButtonPressed = true;
    }
    else
    {
      upButtonPressed = false;
      upOnce = false;
    }
    if (gameInput.GetButton("Dialog_Down"))
    {
      downButtonPressed = true;
    }
    else
    {
      downButtonPressed = false;
      downOnce = false;
    }
    if (gameInput.GetButton("Back"))
    {
      _logger.LogInfo($"Back is true, interact is {gameInput.GetButton("Interact")}");
      doOnce = false;
    }
    return true;
  }

  private static string GetAdjacentFloorName(string currentFloor, int offset)
  {
    _logger.LogInfo($"GetAdjacentFloorName({currentFloor}, {offset})");
    if (currentFloor == "Cellar")
    {
      if (offset == 0)
      {
        return "Cellar";
      }

      if (offset == 1)
      {
        return "Floor01";
      }
    }
    if (currentFloor == "Floor01")
    {
      if (offset == 0)
      {
        return "Floor01";
      }

      if (offset == 1)
      {
        return "Floor02";
      }

      if (offset == -1)
      {
        return "Cellar";
      }
    }
    if (currentFloor == "Floor02")
    {
      if (offset == 0)
      {
        return "Floor02";
      }

      if (offset == 1)
      {
        return "Floor03";
      }

      if (offset == -1)
      {
        return "Floor01";
      }
    }
    if (currentFloor == "Floor03")
    {
      if (offset == 0)
      {
        return "Floor03";
      }

      if (offset == 1)
      {
        return "Loft";
      }

      if (offset == -1)
      {
        return "Floor02";
      }
    }
    if (currentFloor == "Loft")
    {
      if (offset == 0)
      {
        return "Loft";
      }

      if (offset == -1)
      {
        return "Floor03";
      }
    }

    return "";
  }
  

  [HarmonyPatch(typeof(InteractHandler), "OnUpdate")]
  [HarmonyPrefix]
  private static bool InteractTapper()
  {
    var isMapOpen = currentMap != "";
    if (!doOnce && !upOnce && !downOnce && ((isMapOpen && (upButtonPressed || downButtonPressed)) || (!isMapOpen && customButtonPressed)))
    {
      _logger.LogInfo($"Interact tapped successfully {customButtonPressed} and {doOnce}");
      doOnce = true;
      _logger.LogInfo($"I have explicitly set doonce to fkin {doOnce}");
      
      var room = FuzzyGameState.LoreleiState.l_hCurrentLoreleiRoom;
      if (room == null)
      {
        _logger.LogInfo("room mutherfucker null");
        return true;
      }
      
      var greatSuccess = room.mapLink.GetMapAndRoomInstance(
        out FuzzyMap actualGodDamnMap, out FuzzyMap.Room alsoARoomButWhoCares);
      if (!greatSuccess || actualGodDamnMap == null)
      {
        _logger.LogInfo("this mutherfucker null");
        return true;
      }
      
      var currentFloor = currentMap == "" ? actualGodDamnMap.name.Substring("pfHotelmap".Length) : currentMap;
      var requestedFloor = currentFloor;
      
      _logger.LogInfo($"--------------");
      _logger.LogInfo($"{room.name}");
      _logger.LogInfo($"{actualGodDamnMap.name}");
      StealMaptubesRefsIfAbsent();
      
      if (upButtonPressed && !upOnce)
      {
        upOnce = true;
        requestedFloor = GetAdjacentFloorName(currentFloor, 1);
      } else if (downButtonPressed && !downOnce)
      {
        downOnce = true;
        requestedFloor = GetAdjacentFloorName(currentFloor, -1);
      } else if (customButtonPressed && !doOnce)
      {
        doOnce = true;
        requestedFloor = currentFloor;
      }

      if (requestedFloor == "")
      {
        _logger.LogInfo($"can't move further from {currentFloor}");
        return true;
      }
      Interact maptubeInteract = Maptubes.Find(maptube => maptube.name.Contains(requestedFloor));
      if (maptubeInteract == null)
      {
        _logger.LogInfo($"{currentFloor} not found");
        return true;
      }
      
      InteractHandler.Instance.ShowDialog(maptubeInteract, "", false);
      currentMap = requestedFloor;
      return false;
    }

    return true;
  }

  [HarmonyPatch(typeof(PadLockLogic), ConfirmMethodName)]
  [HarmonyPatch(typeof(KeyCabinetPadLock), ConfirmMethodName)]
  [HarmonyPatch(typeof(LetterLockLogic), ConfirmMethodName)]
  [HarmonyPatch(typeof(RomanPadLockLogic), ConfirmMethodName)]
  // [HarmonyPatch(typeof(MapTubeLogic), ConfirmMethodName)]
  [HarmonyPrefix]
  private static bool AlwaysPushConfirmButton(ref ConfirmButtonDuplicate ___m_hConfirmButton)
  {
    ___m_hConfirmButton.bIsPushed = true;
    return true;
  }

  [HarmonyPatch(typeof(PadLockLogic), InputMethodName)]
  [HarmonyPatch(typeof(KeyCabinetPadLock), InputMethodName)]
  [HarmonyPatch(typeof(LetterLockLogic), InputMethodName)]
  [HarmonyPatch(typeof(RomanPadLockLogic), InputMethodName)]
  // [HarmonyPatch(typeof(MapTubeLogic), InputMethodName)]
  [HarmonyPrefix]
  private static bool TreatVerticalDirectionAsInput(
    ref PadLockLogic __instance,
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
    if (nInput is not (SGMenuNavigator.INPUTDIR.UP or SGMenuNavigator.INPUTDIR.DOWN))
    {
      return true;
    }
    
    ReactPreset rotationPreset = __instance.wheelSpinPreset;
    if (rotationPreset == null)
    {
      return false;
    }
    
    // If the input is DOWN - invert the rotation animation
    if (nInput is SGMenuNavigator.INPUTDIR.DOWN)
    {
      rotationPreset = Instantiate(rotationPreset);
      // The animation seems to be pretty simplistic - just 3 keyframes on X axis: 0, 0.67, 1
      AnimationCurve rotation = rotationPreset.hReactEvents[0].hReact.rotationX;
      Keyframe[] originalKeys = rotation.keys;
      Keyframe[] invertedKeys = new Keyframe[originalKeys.Length];
      
      for (var index = 0; index < originalKeys.Length; ++index)
      {
        invertedKeys[index] = new Keyframe(originalKeys[index].time, -originalKeys[index].value);
      }
      rotation.SetKeys(invertedKeys);
    }

    var selectedButtonIndex = ___m_hWheelButtons.FindIndex(wheelButton => wheelButton.hSelectableObject == hSelection);
    WheelButton selectedButton = ___m_hWheelButtons[selectedButtonIndex];
    if (!___m_hReactHandler.IsEventsFinished(selectedButton.hGameObject))
    {
      return false;
    }

    // If the input is UP or DOWN and all previous events have finished - rotate the wheel
    ___m_bSnapWheelRotation = true;
    ___m_hReactHandler.RequestEvents(selectedButton.hGameObject, rotationPreset, 0.0f, false, false);
    
    // Some weird shaking code from the original function
    if (selectedButtonIndex < ___m_hWheelButtons.Count / 2)
    {
      RequestPadLockShake(___m_hReactHandler, __instance.leftSideShakeParent, ___m_hLeftShakePresets,
        ref ___m_nLastLeftShakeIndex);
    }
    else
    {
      RequestPadLockShake(___m_hReactHandler, __instance.rightSideShakeParent, ___m_hRightShakePresets,
        ref ___m_nLastRightShakeIndex);
    }

    return false;
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
    int index = Random.Range(0, hPresets.Count);
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
    public int nCurrentStep = -1;
    public int nInitialStep = -1;
    public GameObject hGameObject;
    public SelectableObject hSelectableObject;
  }
}