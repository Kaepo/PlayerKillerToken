using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ItemManager;
using ServerSync;
using Random = UnityEngine.Random;

namespace PlayerKillerToken
{
	[BepInPlugin(ModGUID, ModName, ModVersion)]
	public class PlayerKillerToken : BaseUnityPlugin
	{
		internal const string ModName = "PlayerKillerToken";
		internal const string ModVersion = "1.0.0";
		internal const string Author = "kaepo";
		private const string ModGUID = Author + "." + ModName;
		private static string ConfigFileName = ModGUID + ".cfg";
		private static string ConfigFileFullPath = Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;

		internal static string ConnectionError = "";

		private readonly Harmony _harmony = new(ModGUID);

		public static readonly ManualLogSource PlayerKillerTokenLogger = BepInEx.Logging.Logger.CreateLogSource(ModName);

		private static readonly ConfigSync ConfigSync = new(ModGUID) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

		public void Awake()
		{
			_serverConfigLocked = config("General", "Force Server Config", true, "Force Server Config");
			_ = ConfigSync.AddLockingConfigEntry(_serverConfigLocked);

			Item PKToken = new("player_killer_token", "Player_Killer_Token");
			PKToken.Name.English("Player Killer Token"); // You can use this to fix the display name in code
			PKToken.Description.English("A commemerative token of your Viking valor.");

			Assembly assembly = Assembly.GetExecutingAssembly();
			_harmony.PatchAll(assembly);
			SetupWatcher();
		}

		private void OnDestroy()
		{
			Config.Save();
		}

		private void SetupWatcher()
		{
			FileSystemWatcher watcher = new(Paths.ConfigPath, ConfigFileName);
			watcher.Changed += ReadConfigValues;
			watcher.Created += ReadConfigValues;
			watcher.Renamed += ReadConfigValues;
			watcher.IncludeSubdirectories = true;
			watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
			watcher.EnableRaisingEvents = true;
		}

		private void ReadConfigValues(object sender, FileSystemEventArgs e)
		{
			if (!File.Exists(ConfigFileFullPath)) return;
			try
			{
				PlayerKillerTokenLogger.LogDebug("ReadConfigValues called");
				Config.Reload();
			}
			catch
			{
				PlayerKillerTokenLogger.LogError($"There was an issue loading your {ConfigFileName}");
				PlayerKillerTokenLogger.LogError("Please check your config entries for spelling and format!");
			}
		}


		#region ConfigOptions

		private static ConfigEntry<bool>? _serverConfigLocked;

		private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
		{
			ConfigDescription extendedDescription = new(description.Description + (synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]"), description.AcceptableValues, description.Tags);
			ConfigEntry<T> configEntry = Config.Bind(group, name, value, extendedDescription);
			//var configEntry = Config.Bind(group, name, value, description);

			SyncedConfigEntry<T> syncedConfigEntry = ConfigSync.AddConfigEntry(configEntry);
			syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

			return configEntry;
		}

		private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true)
		{
			return config(group, name, value, new ConfigDescription(description), synchronizedSetting);
		}

		private class ConfigurationManagerAttributes
		{
			public bool? Browsable = false;
		}

		#endregion

		[HarmonyPatch(typeof(Player), nameof(Player.Awake))]
		private class AddRPCToPlayer
		{
			private static void Postfix(Player __instance)
			{
				__instance.m_nview.Register<int>("PlayerKillerToken GrantToken", (_, count) =>
				{
					__instance.m_inventory.AddItem(ObjectDB.instance.GetItemPrefab("Player_Killer_Token"), count);
				});
			}
		}

		[HarmonyPatch(typeof(Player), nameof(Player.OnDeath))]
		private class RPCOnPlayerDeath
		{
			private static void Postfix(Player __instance)
			{
				if (!__instance.m_knownTexts.ContainsKey("PlayerKillerToken DeathList"))
				{
					__instance.m_knownTexts["PlayerKillerToken DeathList"] = "";
				}

				List<Player> nearbyPlayers = new();
				Player.GetPlayersInRange(__instance.transform.position, 25f, nearbyPlayers);
				if (nearbyPlayers.Count > 1)
				{
					string[] deaths = __instance.m_knownTexts["PlayerKillerToken DeathList"].Split(',');

					int firstIndex = 0;
					foreach (string death in deaths)
					{
						int.TryParse(death, out int day);
						if (day < EnvMan.instance.GetCurrentDay() - 5)
						{
							++firstIndex;
						}
						else
						{
							break;
						}
					}
					deaths = deaths.Skip(firstIndex).Concat(new[] { EnvMan.instance.GetCurrentDay().ToString() }).ToArray();

					__instance.m_knownTexts["PlayerKillerToken DeathList"] = string.Join(",", deaths);

					int[] tokenCounts = { 10, 8, 6, 4, 2, 1 };
					int tokenCount = deaths.Length > tokenCounts.Length ? Random.Range(0, 2) : tokenCounts[deaths.Length - 1];
					
					foreach (Player p in nearbyPlayers)
					{
						if (p != __instance && tokenCount > 0)
						{
							p.m_nview.InvokeRPC("PlayerKillerToken GrantToken", tokenCount);
						}
					}
				}
			}
		}
	}
}
