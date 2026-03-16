/*
using HarmonyLib;

namespace LotsOfItems.Patches;

[HarmonyPatch(typeof(Activity))]
static class ActivityFixPatch
{
    [HarmonyPatch(nameof(Activity.SetPower), [typeof(bool)])]
    [HarmonyPrefix]
    static bool PreventQBonusSignActivation(ref bool ___powered, bool val, UnityEngine.Animator ___bonusQSign)
    {
        // if bonusQSign actually exists, then the Activity should go fine
        if (___bonusQSign) return true;
        ___powered = val; // Otherwise, just completely ignore the bonus sign
        return false;
    }
}
*/