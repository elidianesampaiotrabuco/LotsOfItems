using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using LotsOfItems.Plugin;
using UnityEngine;

namespace LotsOfItems.Patches;

[HarmonyPatch]
internal static class FieldTripRelatedPatch
{
    readonly internal static Dictionary<Items, BusPassInteraction> busPasses = []; // Main dictionary for this class
    readonly internal static HashSet<Items> doesNotRequireFieldTripActiveItems = [];

    [HarmonyPatch(typeof(ItemAcceptor), nameof(ItemAcceptor.ItemFits))]
    [HarmonyPostfix]
    static void RegisterLastUsedItem(Items item, ref bool __result)
    {
        if (__result)
        {
            lastUsedPass = item;
            if (!doesNotRequireFieldTripActiveItems.Contains(item)) // If this item doesn't require a field trip, there's no need for this check
                __result = !Singleton<CoreGameManager>.Instance.tripPlayed && Singleton<CoreGameManager>.Instance.tripAvailable;
        }
    }

    // ********** BALDI INTERACTION PATCH ************
    [HarmonyPatch(typeof(FieldTripEntranceRoomFunction), nameof(FieldTripEntranceRoomFunction.StartFieldTrip))]
    [HarmonyPrefix]
    static bool CheckForAdditionalBusPasses(FieldTripEntranceRoomFunction __instance, PlayerManager player, ref bool ___unlocked, AudioManager ___baldiAudioManager, SoundObject ___audNoPass)
    {
        if (___unlocked) // If the FieldTrip already unlocked, no additional check is needed
            return true;

        Items itemInHand = player.itm.items[player.itm.selectedItem].itemType; // Try prioritizing the item in-hand
        if (itemInHand == Items.BusPass) // If the original bus pass is in the hand, there's no need for custom checks then
            return true;

        // If the item in hand is not a bus pass, search for one inside the inv
        if (!busPasses.ContainsKey(itemInHand))
        {
            bool hasBusPass = false;

            int loopingIdx = player.itm.selectedItem;
            for (int i = 0; i <= player.itm.maxItem; i++) // Try to get a custom bus pass in the inventory
            {
                loopingIdx = (loopingIdx + 1) % player.itm.maxItem;
                itemInHand = player.itm.items[loopingIdx].itemType;
                if (busPasses.ContainsKey(itemInHand))
                {
                    hasBusPass = true;
                    break;
                }
            }
            if (!hasBusPass)
                return true; // Baldi behaves like normal and won't allow the player's entrance
        }

        // *** Actual Baldi Interaction ***

        SoundObject[] speeches = null;
        var interaction = busPasses[itemInHand];
        BusPassInteraction.TripToken tripToken = interaction.baldiInteraction?.Invoke(player) ?? null;
        if (tripToken == null) // If there's no interaction set for Baldi, then this bus pass is not for him!
            return true;

        if (tripToken.acceptsBusPass)
        {
            if (!___unlocked)
                player.itm.Remove(itemInHand);

            Singleton<BaseGameManager>.Instance.CallSpecialManagerFunction(1, __instance.gameObject);
            ___unlocked = true;
            return false;
        }
        else if (tripToken.customRefusalAudio != null) // If the bus pass wasn't accepted and there's a custom refusal audio, override it for that
            speeches = tripToken.customRefusalAudio;

        ___baldiAudioManager.FlushQueue(true);

        if (speeches != null)
            ___baldiAudioManager.QueueRandomAudio(speeches);
        else
            ___baldiAudioManager.QueueAudio(___audNoPass);

        return false;
    }

    // ************** JOHNNY INTERACTION PATCH **************

    [HarmonyPatch(typeof(StoreRoomFunction), nameof(StoreRoomFunction.OnPlayerEnter))]
    [HarmonyPostfix]
    static void AlwaysEnableHotspot(Transform ___johnnyHotspot) =>
        ___johnnyHotspot.gameObject.SetActive(!Singleton<CoreGameManager>.Instance.tripPlayed); // Ignore the tripAvailable part


    [HarmonyPatch(typeof(StoreRoomFunction), nameof(StoreRoomFunction.GivenBusPass))]
    [HarmonyTranspiler]
    static IEnumerable<CodeInstruction> ChangeSequencerConstructor(IEnumerable<CodeInstruction> i) =>
    new CodeMatcher(i)
    .End()
    .MatchBack(
        false,
        new CodeMatch(CodeInstruction.Call(typeof(StoreRoomFunction), "BusPassSequencer"))
    )
    .SetInstruction(CodeInstruction.Call(typeof(FieldTripRelatedPatch), "AlternativeBusPassSequencer")) // Changes the IEnumerator

    .InstructionEnumeration();

    static IEnumerator AlternativeBusPassSequencer(StoreRoomFunction store)
    {
        bool tripAvailable = Singleton<CoreGameManager>.Instance.tripAvailable; // Will evaluate whether Johnny should really accept the (custom) bus pass or not

        var player = Singleton<CoreGameManager>.Instance.GetPlayer(0);
        Items savedLastUsedPass = lastUsedPass; // Use the last item used for Johnny, to keep record for the interaction
        bool hasCustomBusPass = busPasses.TryGetValue(savedLastUsedPass, out var interaction);

        // Attempts to get a token, otherwise null
        BusPassInteraction.JohnnyToken token = hasCustomBusPass ? interaction.johnnyInteraction.Invoke(player) : null;

        // If the interaction mutes this or if the bus pass isn't customized and there's no trip available, mute johnny
        if (!tripAvailable || (hasCustomBusPass && token.muteJohnnysFieldTripSpeech))
            store.johnnyAudioManager.FlushQueue(true); // Prevent johnny from loving field trips lol

        // Yield returns from the og BusPassSequencer
        yield return null;
        while (store.johnnyAudioManager.QueuedAudioIsPlaying)
        {
            yield return null;
        }

        // Try to get the last bus pass used to be sure it is a custom one
        if (!hasCustomBusPass)
        {
            if (tripAvailable)
            {
                // Immediately calls the field trip thing since johnny's item acceptor should only accept BusPasses in theory
                Singleton<BaseGameManager>.Instance.CallSpecialManagerFunction(3, store.gameObject);
                store.johnnyAudioManager.QueueAudio(store.audTripReturn);
            }
            else
                store.johnnyHotspot.gameObject.SetActive(true); // guarantee the hotspot is still active

            yield break;
        }

        if (!token.acceptsBusPass && tripAvailable) // he can't just not accept a bus pass if there's no trip available
        {
            // If the interaction state is not a full kick out of the level, then just simply refuse
            if (token.interactionState != BusPassInteraction.JohnnyToken.JohnnyAction.KickOutOfLevel || Singleton<BaseGameManager>.Instance is not PitstopGameManager pitStopMan)
            {
                // Usually this should never happen since you're just wasting a pass
                yield break;
            }
            pitStopMan.StartCoroutine(pitStopMan.FieldTripTransition(false, false));
            store.johnnyAudioManager.QueueRandomAudio(token.customRefusalAudio);

            while (store.johnnyAudioManager.QueuedAudioIsPlaying)
            {
                yield return null; // Expel from the store!
            }
            Singleton<CoreGameManager>.Instance.audMan.PlaySingle(LotOfItemsPlugin.assetMan.Get<SoundObject>("audBump"));

            Vector3 ogPos = player.transform.position,
            elevatorPos = pitStopMan.Ec.Elevators[0].transform.position;
            float t = 0;
            while (true)
            {
                player.plm.Entity.Teleport(Vector3.Lerp(ogPos, elevatorPos, t));
                if (t > 1f)
                    break;
                t += Time.deltaTime * pitStopMan.Ec.EnvironmentTimeScale * 50f;
                yield return null;
            }

            player.plm.Entity.Teleport(elevatorPos);


            yield break;
        }

        // Custom interaction if available
        if (token.customJohnnyBusPassAudio != null)
            store.johnnyAudioManager.QueueRandomAudio(token.customJohnnyBusPassAudio);
        else if (tripAvailable)
            store.johnnyAudioManager.QueueAudio(store.audTripReturn);

        // If it is a custom reward
        if (token.interactionState == BusPassInteraction.JohnnyToken.JohnnyAction.GiveCustomReward)
        {
            store.johnnyHotspot.gameObject.SetActive(true); // Prevent the hotspot from disabling itself since custom rewards aren't usually from field trips
            token.customRewardAction?.Invoke(player);
            yield break;
        }

        if (tripAvailable) // will only reward field trip loot if there was a field trip
            Singleton<BaseGameManager>.Instance.CallSpecialManagerFunction(3, store.gameObject);
        else
            store.johnnyHotspot.gameObject.SetActive(true);
        yield break;
    }

    [HarmonyPatch(typeof(StoreRoomFunction), nameof(StoreRoomFunction.GivenBusPass))]
    [HarmonyPrefix]
    static bool AllowBusPassEntrance() =>
        busPasses.TryGetValue(lastUsedPass, out var interaction) && interaction.johnnyInteraction != null;
    // If the custom bus pass has a johnny interaction, it can go


    static Items lastUsedPass = Items.None;

}

internal readonly struct BusPassInteraction(Func<PlayerManager, BusPassInteraction.TripToken> baldiInteraction = null, Func<PlayerManager, BusPassInteraction.JohnnyToken> johnnyInteraction = null)
{
    public readonly Func<PlayerManager, TripToken> baldiInteraction = baldiInteraction;
    public readonly Func<PlayerManager, JohnnyToken> johnnyInteraction = johnnyInteraction;

    // Fields for specific tokens
    public class TripToken
    {
        public bool acceptsBusPass = false;
        public SoundObject[] customRefusalAudio = null;
    }
    public class JohnnyToken : TripToken
    {
        public enum JohnnyAction
        {
            RefusePass = 0,
            KickOutOfLevel = 1,
            GiveCustomReward = 2
        }
        public JohnnyAction interactionState;
        public SoundObject[] customJohnnyBusPassAudio = null;
        public Action<PlayerManager> customRewardAction = null;
        public bool muteJohnnysFieldTripSpeech = false;
        public bool specificForFieldTrip = true;
    }

}