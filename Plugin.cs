using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace NavalBuffetMod
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;

        public class ContainerConfig
        {
            public ConfigEntry<bool> Replenishable;
            public ConfigEntry<float> Radius;
        }

        public static Dictionary<string, ContainerConfig> Configs = new Dictionary<string, ContainerConfig>();

        private void Awake()
        {
            Log = Logger;
            
            string[] targets = { "MunitionsContainer1", "MunitionsPallet1", "NavalSupplyContainer1", "NavalPallet1" };
            
            foreach (var target in targets)
            {
                Configs[target] = new ContainerConfig
                {
                    Replenishable = Config.Bind(target, "Replenishable", true, "Whether the container can be used multiple times without despawning."),
                    Radius = Config.Bind(target, "Radius", 1000f, "Resupply radius.")
                };
            }

            Harmony harmony = new Harmony(PluginInfo.PLUGIN_GUID);
            harmony.PatchAll();
            
            Log.LogInfo("Harmony patches applied successfully. Configuration initialized.");
        }
    }

    [HarmonyPatch(typeof(Rearmer), "Start")]
    public class Rearmer_Start_Patch
    {
        static void Postfix(Rearmer __instance)
        {
            try
            {
                if (__instance == null || __instance.gameObject == null) return;
                
                string objName = __instance.gameObject.name;
                string matchedTarget = null;
                
                foreach (var target in Plugin.Configs.Keys)
                {
                    if (objName.Contains(target))
                    {
                        matchedTarget = target;
                        break;
                    }
                }

                if (matchedTarget != null)
                {
                    var traverse = Traverse.Create(__instance);
                    bool origSingleUse = traverse.Field<bool>("singleUse").Value;
                    float origRange = traverse.Field<float>("range").Value;
                    
                    Plugin.Log.LogInfo($"[Wiretap] Found TARGET Rearmer on GameObject: '{objName}' | original singleUse: {origSingleUse} | original range: {origRange}");

                    var config = Plugin.Configs[matchedTarget];
                    traverse.Field("singleUse").SetValue(!config.Replenishable.Value);
                    traverse.Field("range").SetValue(config.Radius.Value);
                    
                    Plugin.Log.LogInfo($"[NavalBuffetMod] Success! {objName} is now set to singleUse: {traverse.Field<bool>("singleUse").Value}, range: {traverse.Field<float>("range").Value}");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[NavalBuffetMod] Error in Rearmer Start Patch: {ex}");
            }
        }
    }

    public static class PluginInfo
    {
        public const string PLUGIN_GUID = "com.user.navalbuffetmod";
        public const string PLUGIN_NAME = "NavalBuffetMod";
        public const string PLUGIN_VERSION = "1.1.0";
    }
}
