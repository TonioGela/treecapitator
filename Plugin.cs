using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace Treecapitator
{
    [BepInPlugin(ModInfo.Guid, ModInfo.Name, ModInfo.Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        const string ItemName = "Treecapitator";
        // Hits from the axe carry one of these tool tiers: they beat every tree's m_minToolTier and tell the tree's owner (maybe another client) what to do.
        // Tier: drop the wood straight away and break the stump. AreaTier: let the log fall, break the stump.
        const short Tier = 1000;
        const short AreaTier = 1001;

        static ConfigEntry<float> areaRadius;

        void Awake()
        {
            areaRadius = Config.Bind("Area mode", "Radius", 20f, "The secondary attack fells every tree and breaks every stump within this radius, in meters (the player is about 2m tall)");
            new Harmony(ModInfo.Guid).PatchAll();
            PrefabManager.OnVanillaPrefabsAvailable += AddItem;
            Logger.LogInfo($"{ModInfo.Name} {ModInfo.Version} loaded");
        }

        void AddItem()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= AddItem;
            var config = new ItemConfig
            {
                Name = ItemName,
                Description = "Fells any tree, stump included, in a single swing.",
                CraftingStation = CraftingStations.Workbench,
            };
            config.AddRequirement("Wood", 4);
            config.AddRequirement("Stone", 2);
            var item = new CustomItem(ItemName, "AxeStone", config);
            // Area mode needs a secondary attack: borrow the bronze axe's if the stone axe has none
            if (!item.ItemDrop.m_itemData.HaveSecondaryAttack())
                item.ItemDrop.m_itemData.m_shared.m_secondaryAttack = PrefabManager.Cache.GetPrefab<ItemDrop>("AxeBronze").m_itemData.m_shared.m_secondaryAttack.Clone();
            ItemManager.Instance.AddItem(item);
        }

        static bool inArea;
        static readonly AccessTools.FieldRef<Humanoid, bool> isSecondary = AccessTools.FieldRefAccess<Humanoid, bool>("m_currentAttackIsSecondary");

        // Runs on the attacker's client, before the hit is sent to the target's owner. Only trees, logs and stumps get the boost, not creatures or buildings.
        static void Boost(Component target, HitData hit)
        {
            if (inArea || hit.GetAttacker() != Player.m_localPlayer || Player.m_localPlayer.GetCurrentWeapon()?.m_shared.m_name != ItemName) return;
            hit.m_damage.m_chop = 1e6f;
            hit.m_toolTier = Tier;
            if (!isSecondary(Player.m_localPlayer)) return;
            hit.m_toolTier = AreaTier;
            inArea = true; // the Damage calls below come back through these patches
            try
            {
                var center = Player.m_localPlayer.transform.position;
                // ponytail: scans every loaded tree and destructible, fine for one swing; use Physics.OverlapSphere if it ever stutters
                foreach (var tree in Object.FindObjectsOfType<TreeBase>())
                    if (tree != target && Vector3.Distance(tree.transform.position, center) <= areaRadius.Value) tree.Damage(AreaHit(hit, tree, center));
                foreach (var stump in Object.FindObjectsOfType<Destructible>())
                    if (stump != target && IsStump(stump) && Vector3.Distance(stump.transform.position, center) <= areaRadius.Value) stump.Damage(AreaHit(hit, stump, center));
            }
            finally { inArea = false; }
        }

        // Pointing away from the player, so the logs fall outwards
        static HitData AreaHit(HitData hit, Component target, Vector3 center)
        {
            var copy = hit.Clone();
            copy.m_point = target.transform.position + Vector3.up;
            copy.m_dir = Vector3.ProjectOnPlane(target.transform.position - center, Vector3.up).normalized;
            return copy;
        }

        // Vanilla stump prefabs are all named *stub* (e.g. stubbe, Beech_Stub)
        static bool IsStump(Destructible d) => d.m_destructibleType == DestructibleType.Tree || d.name.ToLower().Contains("stub");

        [HarmonyPatch(typeof(TreeBase), nameof(TreeBase.Damage))]
        static class TreeDamage { static void Prefix(TreeBase __instance, HitData hit) => Boost(__instance, hit); }

        [HarmonyPatch(typeof(TreeLog), nameof(TreeLog.Damage))]
        static class LogDamage { static void Prefix(TreeLog __instance, HitData hit) => Boost(__instance, hit); }

        // Stumps left by other axes
        [HarmonyPatch(typeof(Destructible), nameof(Destructible.Damage))]
        static class StumpDamage
        {
            static void Prefix(Destructible __instance, HitData hit)
            {
                if (IsStump(__instance)) Boost(__instance, hit);
            }
        }

        // The Treecapitator hit being handled by the tree's owner, so SpawnLog below can see it
        static HitData capitating;

        [HarmonyPatch(typeof(TreeBase), "RPC_Damage")]
        static class TreeRpcDamage
        {
            static void Prefix(HitData hit) => capitating = hit.m_toolTier == Tier || hit.m_toolTier == AreaTier ? hit : null;
            static void Postfix() => capitating = null;
        }

        // Break the stump, then either let the log fall (area mode) or drop all the wood the log would eventually give
        [HarmonyPatch(typeof(TreeBase), "SpawnLog")]
        static class TreeSpawnLog
        {
            static bool Prefix(TreeBase __instance)
            {
                if (capitating == null) return true;
                var t = __instance.transform;
                if (__instance.m_stubPrefab)
                {
                    var stub = Object.Instantiate(__instance.m_stubPrefab, t.position, t.rotation);
                    stub.GetComponent<ZNetView>().SetLocalScale(t.localScale);
                    var destructible = stub.GetComponent<Destructible>();
                    if (destructible) destructible.Destroy(capitating);
                    else ZNetScene.instance.Destroy(stub);
                }
                if (capitating.m_toolTier == AreaTier)
                {
                    __instance.m_stubPrefab = null; // this tree instance is destroyed right after, so vanilla SpawnLog just skips the stump
                    return true;
                }
                DropLog(__instance.m_logPrefab, t.position);
                return false;
            }

            // A log drops its own items, then splits into m_subLogPoints smaller logs, which do the same
            static void DropLog(GameObject prefab, Vector3 pos)
            {
                var log = prefab ? prefab.GetComponent<TreeLog>() : null;
                if (!log) return;
                foreach (var drop in log.m_dropWhenDestroyed.GetDropList())
                {
                    var offset = Random.insideUnitCircle;
                    Object.Instantiate(drop, pos + new Vector3(offset.x, 1f, offset.y), Quaternion.Euler(0f, Random.Range(0, 360), 0f));
                }
                foreach (var _ in log.m_subLogPoints) DropLog(log.m_subLogPrefab, pos);
            }
        }
    }
}
