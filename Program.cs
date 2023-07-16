using System;
using System.IO;
using System.Threading.Tasks;
using System.Text.Json;
using System.Threading;

namespace SysBot.ACNHOrders
{
    internal static class Program
    {
        private const string DefaultConfigPath = "config.json";
        private const string DefaultTwitchPath = "twitch.json";
		private const string DefaultSocketServerAPIPath = "server.json";

        private static async Task Main(string[] args)
        {
            string configPath;

			Console.WriteLine("Démarrage...");
            if (args.Length > 0) 
            {
                if (args.Length > 1) 
                {
                    Console.WriteLine("Trop d'arguments fournis et seront ignorés.");
                    configPath = DefaultConfigPath;
                }
                else {
                    configPath = args[0];
                }
            }
            else {
                configPath = DefaultConfigPath;
            }

            if (!File.Exists(configPath))
            {
                CreateConfigQuit(configPath);
                return;
            }

            if (!File.Exists(DefaultTwitchPath))
                SaveConfig(new TwitchConfig(), DefaultTwitchPath);

			if (!File.Exists(DefaultSocketServerAPIPath))
				SaveConfig(new SocketAPI.SocketAPIServerConfig(), DefaultSocketServerAPIPath);

			var json = File.ReadAllText(configPath);
            var config = JsonSerializer.Deserialize<CrossBotConfig>(json);
            if (config == null)
            {
                Console.WriteLine("Échec de la désérialisation du fichier de configuration.");
                WaitKeyExit();
                return;
            }

            json = File.ReadAllText(DefaultTwitchPath);
            var twitchConfig = JsonSerializer.Deserialize<TwitchConfig>(json);
            if (twitchConfig == null)
            {
                Console.WriteLine("Échec de la désérialisation du fichier de configuration de twitch.");
                WaitKeyExit();
                return;
            }

			json = File.ReadAllText(DefaultSocketServerAPIPath);
			var serverConfig = JsonSerializer.Deserialize<SocketAPI.SocketAPIServerConfig>(json);
            if (serverConfig == null)
            {
				Console.WriteLine("Échec de la désérialisation du fichier de configuration du serveur API Socket.");
				WaitKeyExit();
				return;
            }

			SaveConfig(config, configPath);
            SaveConfig(twitchConfig, DefaultTwitchPath);
			SaveConfig(serverConfig, DefaultSocketServerAPIPath);
            
			SocketAPI.SocketAPIServer server = SocketAPI.SocketAPIServer.shared;
			_ = server.Start(serverConfig);

			await BotRunner.RunFrom(config, CancellationToken.None, twitchConfig).ConfigureAwait(false);

			WaitKeyExit();
        }

        private static void SaveConfig<T>(T config, string path)
        {
            var options = new JsonSerializerOptions {WriteIndented = true};
            var json = JsonSerializer.Serialize(config, options);
            File.WriteAllText(path, json);
        }

        private static void CreateConfigQuit(string configPath)
        {
            SaveConfig(new CrossBotConfig {IP = "192.168.0.1", Port = 6000}, configPath);
            Console.WriteLine("Fichier de configuration vide créé. Veuillez le configurer et redémarrer le programme.");
            WaitKeyExit();
        }

        private static void WaitKeyExit()
        {
            Console.WriteLine("Appuyez sur n'importe quelle touche pour quitter.");
            Console.ReadKey();
        }
    }
}