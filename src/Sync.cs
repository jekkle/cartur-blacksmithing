using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;
using HarmonyLib;

namespace CarturBlacksmithing
{
    // The server's numbers apply to everyone, and a client on another version is told so. Same
    // design as Cartur's Craft From Containers (its Sync.cs): built on the game's own ZRpc, because
    // the ServerSync library most mods bundle states no licence and so cannot be reused. Only the
    // "1 - Server" section travels; key bindings stay the player's own.
    internal static class Sync
    {
        private const string VersionRpc = "CSMITH_Version";
        private const string SettingsRpc = "CSMITH_Settings";
        private const string MismatchRpc = "CSMITH_Mismatch";

        private static readonly HashSet<string> ServerOwned =
            new HashSet<string>(StringComparer.Ordinal) { Plugin.ServerSection };

        // ASCII unit separator: no setting value can contain it.
        private const char Field = '\u001f';

        private static bool _applied;
        private static readonly Dictionary<string, string> LocalValues = new Dictionary<string, string>(StringComparer.Ordinal);
        internal static string ServerModVersion;

        [HarmonyPatch(typeof(ZNet), "OnNewConnection")]
        private static class Handshake
        {
            private static void Postfix(ZNet __instance, ZNetPeer peer)
            {
                peer.m_rpc.Register<string>(VersionRpc, OnVersion);
                peer.m_rpc.Register<string>(SettingsRpc, OnSettings);
                peer.m_rpc.Register<string>(MismatchRpc, (rpc, v) => ServerModVersion = v);

                if (!__instance.IsServer())
                {
                    ServerModVersion = null;
                    peer.m_rpc.Invoke(VersionRpc, Plugin.Version);
                }
            }
        }

        private static void OnVersion(ZRpc rpc, string clientVersion)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer())
                return;

            if (clientVersion != Plugin.Version)
            {
                if (Plugin.EnforceVersion.Value)
                {
                    Plugin.Log.LogWarning($"Refusing a client on {Plugin.Name} {clientVersion}; this server runs {Plugin.Version}.");
                    rpc.Invoke(MismatchRpc, Plugin.Version);
                    rpc.Invoke("Error", (int)ZNet.ConnectionStatus.ErrorVersion);
                    return;
                }
                Plugin.Log.LogWarning($"A client is running {Plugin.Name} {clientVersion} against this server's {Plugin.Version}; let in because EnforceVersion is off.");
            }

            var text = new StringBuilder();
            foreach (ConfigEntryBase entry in Plugin.ServerOwnedEntries(ServerOwned))
                text.Append(entry.Definition.Key).Append(Field)
                    .Append(TomlTypeConverter.ConvertToString(entry.BoxedValue, entry.SettingType)).Append('\n');
            rpc.Invoke(SettingsRpc, text.ToString());
        }

        private static void OnSettings(ZRpc rpc, string payload)
        {
            if (ZNet.instance != null && ZNet.instance.IsServer())
                return;

            if (!_applied)
            {
                LocalValues.Clear();
                foreach (ConfigEntryBase entry in Plugin.ServerOwnedEntries(ServerOwned))
                    LocalValues[entry.Definition.Key] = TomlTypeConverter.ConvertToString(entry.BoxedValue, entry.SettingType);
                _applied = true;
            }

            int applied = 0;
            Plugin.WithoutSaving(() =>
            {
                foreach (string line in payload.Split('\n'))
                {
                    string[] parts = line.Split(Field);
                    ConfigEntryBase entry = parts.Length == 2 ? Plugin.FindEntry(Plugin.ServerSection, parts[0]) : null;
                    if (entry == null)
                        continue;
                    try
                    {
                        entry.BoxedValue = TomlTypeConverter.ConvertToValue(parts[1], entry.SettingType);
                        applied++;
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning($"Server sent a value for {parts[0]} this build cannot read: {e.Message}");
                    }
                }
            });
            Plugin.Log.LogInfo($"Using the server's blacksmithing settings ({applied}).");
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.Shutdown))]
        private static class RestoreLocal
        {
            private static void Postfix()
            {
                ServerModVersion = null;
                if (!_applied)
                    return;
                _applied = false;
                Plugin.WithoutSaving(() =>
                {
                    foreach (ConfigEntryBase entry in Plugin.ServerOwnedEntries(ServerOwned))
                        if (LocalValues.TryGetValue(entry.Definition.Key, out string stored))
                            try { entry.BoxedValue = TomlTypeConverter.ConvertToValue(stored, entry.SettingType); }
                            catch { /* the player's own file stays the source of truth */ }
                });
            }
        }

        // ShowConnectError's parameter is statusOverride, and the real status is often on ZNet -
        // see the same patch in Craft From Containers for why both are read.
        [HarmonyPatch(typeof(FejdStartup), "ShowConnectError")]
        private static class ExplainTheRejection
        {
            private static void Postfix(FejdStartup __instance, ZNet.ConnectionStatus statusOverride)
            {
                ZNet.ConnectionStatus status = statusOverride != ZNet.ConnectionStatus.None ? statusOverride : ZNet.GetConnectionStatus();
                if (status != ZNet.ConnectionStatus.ErrorVersion || ServerModVersion == null || __instance.m_connectionFailedError == null)
                    return;
                __instance.m_connectionFailedError.text =
                    $"{Plugin.Name}\nThis server runs version {ServerModVersion} and you have {Plugin.Version}. Install the matching version to join.";
            }
        }
    }
}
