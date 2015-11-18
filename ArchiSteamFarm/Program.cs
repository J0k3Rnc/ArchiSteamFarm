﻿/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015 Łukasz "JustArchi" Domeradzki
 Contact: JustArchi@JustArchi.net

 Licensed under the Apache License, Version 2.0 (the "License");
 you may not use this file except in compliance with the License.
 You may obtain a copy of the License at

 http://www.apache.org/licenses/LICENSE-2.0
					
 Unless required by applicable law or agreed to in writing, software
 distributed under the License is distributed on an "AS IS" BASIS,
 WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 See the License for the specific language governing permissions and
 limitations under the License.

*/

using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace ArchiSteamFarm {
	internal static class Program {
		internal enum EUserInputType {
			Login,
			Password,
			SteamGuard,
			SteamParentalPIN,
			TwoFactorAuthentication,
		}

		private const string LatestGithubReleaseURL = "https://api.github.com/repos/JustArchi/ArchiSteamFarm/releases/latest";

		internal const ulong ArchiSCFarmGroup = 103582791440160998;
		internal const string ConfigDirectoryPath = "config";

		private static readonly SemaphoreSlim SteamSemaphore = new SemaphoreSlim(1);
		private static readonly ManualResetEvent ShutdownResetEvent = new ManualResetEvent(false);
		private static readonly Assembly Assembly = Assembly.GetExecutingAssembly();
		private static readonly string ExecutablePath = Assembly.Location;
		private static readonly AssemblyName AssemblyName = Assembly.GetName();
		//private static readonly string ExeName = AssemblyName.Name + ".exe";
		private static readonly string Version = AssemblyName.Version.ToString();

		internal static readonly object ConsoleLock = new object();

		private static async Task CheckForUpdate() {
			JObject response = await Utilities.UrlToJObject(LatestGithubReleaseURL).ConfigureAwait(false);
			if (response == null) {
				return;
			}

			string remoteVersion = response["tag_name"].ToString();
			if (string.IsNullOrEmpty(remoteVersion)) {
				return;
			}

			string localVersion = Version;

			Logging.LogGenericNotice("", "Local version: " + localVersion);
			Logging.LogGenericNotice("", "Remote version: " + remoteVersion);

			int comparisonResult = localVersion.CompareTo(remoteVersion);
			if (comparisonResult < 0) {
				Logging.LogGenericNotice("", "New version is available!");
				Logging.LogGenericNotice("", "Consider updating yourself!");
				await Utilities.SleepAsync(5000).ConfigureAwait(false);
			} else if (comparisonResult > 0) {
				Logging.LogGenericNotice("", "You're currently using pre-release version!");
				Logging.LogGenericNotice("", "Be careful!");
			}
		}

		internal static async Task Exit(int exitCode = 0) {
			await Bot.ShutdownAllBots().ConfigureAwait(false);
			Environment.Exit(exitCode);
		}

		internal static async Task Restart() {
			await Bot.ShutdownAllBots().ConfigureAwait(false);
			System.Diagnostics.Process.Start(ExecutablePath);
			Environment.Exit(0);
		}

		internal static async Task LimitSteamRequestsAsync() {
			await SteamSemaphore.WaitAsync().ConfigureAwait(false);
			await Utilities.SleepAsync(5 * 1000).ConfigureAwait(false); // We must add some delay to not get caught by Steam anty-DoS
			SteamSemaphore.Release();
		}

		internal static string GetUserInput(string botLogin, EUserInputType userInputType) {
			string result;
			lock (ConsoleLock) {
				switch (userInputType) {
					case EUserInputType.Login:
						Console.Write("<" + botLogin + "> Please enter your login: ");
						break;
					case EUserInputType.Password:
						Console.Write("<" + botLogin + "> Please enter your password: ");
						break;
					case EUserInputType.SteamGuard:
						Console.Write("<" + botLogin + "> Please enter the auth code sent to your email: ");
						break;
					case EUserInputType.SteamParentalPIN:
						Console.Write("<" + botLogin + "> Please enter steam parental PIN: ");
						break;
					case EUserInputType.TwoFactorAuthentication:
						Console.Write("<" + botLogin + "> Please enter your 2 factor auth code from your authenticator app: ");
						break;
				}
				result = Console.ReadLine();
				Console.Clear(); // For security purposes
			}
			result = result.Trim(); // Get rid of all whitespace characters
			return result;
		}

		internal static async void OnBotShutdown(Bot bot) {
			if (Bot.GetRunningBotsCount() == 0) {
				Logging.LogGenericInfo("Main", "No bots are running, exiting");
				await Utilities.SleepAsync(5000).ConfigureAwait(false); // This might be the only message user gets, consider giving him some time
				ShutdownResetEvent.Set();
			}
		}

		private static void Main(string[] args) {
			Logging.LogGenericInfo("Main", "Archi's Steam Farm, version " + Version);

			Task.Run(async () => await CheckForUpdate().ConfigureAwait(false)).Wait();

			// Allow loading configs from source tree if it's a debug build
			if (Debugging.IsDebugBuild) {
				for (var i = 0; i < 4; i++) {
					Directory.SetCurrentDirectory("..");
					if (Directory.Exists(ConfigDirectoryPath)) {
						break;
					}
				}
			}

			if (!Directory.Exists(ConfigDirectoryPath)) {
				Logging.LogGenericError("Main", "Config directory doesn't exist!");
				Console.ReadLine();
				Task.Run(async () => await Exit(1).ConfigureAwait(false)).Wait();
			}

			foreach (var configFile in Directory.EnumerateFiles(ConfigDirectoryPath, "*.xml")) {
				string botName = Path.GetFileNameWithoutExtension(configFile);
				Bot bot = new Bot(botName);
				if (!bot.Enabled) {
					Logging.LogGenericInfo(botName, "Not starting this instance because it's disabled in config file");
				}
			}

			// Check if we got any bots running
			OnBotShutdown(null);

			ShutdownResetEvent.WaitOne();
		}
	}
}