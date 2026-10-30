using BepInEx;
using HarmonyLib;
using Jotunn.Utils;

namespace MyMod
{
    [BepInPlugin(ModInfo.Guid, ModInfo.Name, ModInfo.Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        void Awake()
        {
            new Harmony(ModInfo.Guid).PatchAll();
            Logger.LogInfo($"{ModInfo.Name} {ModInfo.Version} loaded");
        }
    }
}
