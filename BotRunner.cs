using System;
using System.Threading;
using System.Threading.Tasks;
using SysBot.Base;
using SysBot.ACNHOrders.Twitch;
using SysBot.ACNHOrders.Signalr;

namespace SysBot.ACNHOrders
{
    public static class BotRunner
    {
        public static async Task RunFrom(CrossBotConfig config, CancellationToken cancel, TwitchConfig? tConfig = null)
        {
            // Configurer la journalisation pour la fenêtre de la console
            LogUtil.Forwarders.Add(Logger);
            static void Logger(string msg, string identity) => Console.WriteLine(GetMessage(msg, identity));
            static string GetMessage(string msg, string identity) => $"> [{DateTime.Now:hh:mm:ss}] - {identity}: {msg}";

            var bot = new CrossBot(config);

            var sys = new SysCord(bot);

            Globals.Self = sys;
            Globals.Bot = bot;
            Globals.Hub = QueueHub.CurrentInstance;
            GlobalBan.UpdateConfiguration(config);

            bot.Log("Démarrage du discord.");
#pragma warning disable 4014
            Task.Run(() => sys.MainAsync(config.Token, cancel), cancel);
#pragma warning restore 4014


            if (tConfig != null && !string.IsNullOrWhiteSpace(tConfig.Token))
            {
                bot.Log("Démarrer Twitch.");
                var _ = new TwitchCrossBot(tConfig, bot);
            }

            if (!string.IsNullOrWhiteSpace(config.SignalrConfig.URIEndpoint))
            {
                bot.Log("Web de départ.");
                var _ = new SignalrCrossBot(config.SignalrConfig, bot);
            }

            if (config.SkipConsoleBotCreation)
            {
                await Task.Delay(-1, cancel).ConfigureAwait(false);
                return;
            }

            bot.Log("Boucle de démarrage du robot.");

            var task = bot.RunAsync(cancel);
            await task.ConfigureAwait(false);

            if (task.IsFaulted)
            {
                if (task.Exception == null)
                {
                    bot.Log("Le bot s'est arrêté en raison d'une erreur inconnue.");
                }
                else
                {
                    bot.Log("Le bot s'est terminé en raison d'une erreur :");
                    foreach (var ex in task.Exception.InnerExceptions)
                    {
                        bot.Log(ex.Message);
                        var st = ex.StackTrace;
                        if (st != null)
                            bot.Log(st);
                    }
                }
            }
            else
            {
                bot.Log("Le robot est terminé.");
            }
        }
    }
}
