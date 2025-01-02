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
  private static ManualLogSource _logger;
  private static Harmony _hi;

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

  [HarmonyPatch(typeof(PadLockLogic), ConfirmMethodName)]
  [HarmonyPatch(typeof(KeyCabinetPadLock), ConfirmMethodName)]
  [HarmonyPatch(typeof(LetterLockLogic), ConfirmMethodName)]
  [HarmonyPatch(typeof(RomanPadLockLogic), ConfirmMethodName)]
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