using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.DependencyInjection;
using SysBot.Base;

namespace SysBot.ACNHOrders
{
    public sealed class SysCord
    {
        private readonly DiscordSocketClient _client;
        private readonly CrossBot Bot;
        public ulong Owner = ulong.MaxValue;
        public bool Ready = false;

        // Conservez le CommandService et le conteneur DI pour les utiliser avec les commandes.
        // Ces deux types nécessitent l'installation du package Discord.Net.Commands.
        private readonly CommandService _commands;
        private readonly IServiceProvider _services;

        public SysCord(CrossBot bot)
        {
            Bot = bot;
            _client = new DiscordSocketClient(new DiscordSocketConfig
            {
                // Quelle quantité d'exploitation forestière voulez-vous voir ?
                LogLevel = LogSeverity.Info,

                // Si vous ou un autre service doit faire quelque chose avec les messages
                // (par exemple, vérifier les réactions, vérifier le contenu des messages édités/supprimés),
                // vous devez définir le MessageCacheSize. Vous pouvez ajuster ce nombre selon vos besoins.
                //MessageCacheSize = 50,
            });

            _commands = new CommandService(new CommandServiceConfig
            {
                // Encore une fois, niveau du journal :
                LogLevel = LogSeverity.Info,

                // Cela permet d'exécuter les commandes sur le pool de threads de la tâche plutôt que sur le thread de lecture de la websocket.
                // Cela permet de s'assurer que les logiques longues ne bloquent pas la connexion websocket.
                DefaultRunMode = RunMode.Sync,

                // Il y a quelques propriétés supplémentaires que vous pouvez définir,
                // par exemple, les commandes insensibles à la casse.
                CaseSensitiveCommands = false,
            });

            // Souscrivez le gestionnaire de journalisation à la fois au client et au CommandService.
            _client.Log += Log;
            _commands.Log += Log;

            // Configurez votre conteneur DI.
            _services = ConfigureServices();
        }

        // Si un service nécessite le client, ou le CommandService, ou autre chose que vous avez sous la main,
        // passez-les comme paramètres dans cette méthode si nécessaire.
        // Si cette méthode devient assez longue, vous pouvez la séparer dans un autre fichier en utilisant des partiels.
        private static IServiceProvider ConfigureServices()
        {
            var map = new ServiceCollection();//.AddSingleton(new SomeServiceClass());

            // Lorsque tous les services requis sont dans la collection, construisez le conteneur.
            // Astuce : Il y a une surcharge qui prend un bool 'validateScopes' pour s'assurer que // vous n'avez pas fait d'erreur dans votre graphe de dépendances.
            // que vous n'avez pas fait d'erreur dans votre graphe de dépendances.
            return map.BuildServiceProvider();
        }

        // Exemple d'un gestionnaire de journalisation. Il peut être réutilisé par les addons
        // qui demandent un Func<LogMessage, Task>.

        private static Task Log(LogMessage msg)
        {
            Console.ForegroundColor = msg.Severity switch
            {
                LogSeverity.Critical => ConsoleColor.Red,
                LogSeverity.Error => ConsoleColor.Red,

                LogSeverity.Warning => ConsoleColor.Yellow,
                LogSeverity.Info => ConsoleColor.White,

                LogSeverity.Verbose => ConsoleColor.DarkGray,
                LogSeverity.Debug => ConsoleColor.DarkGray,
                _ => Console.ForegroundColor
            };

            var text = $"[{msg.Severity,8}] {msg.Source}: {msg.Message} {msg.Exception}";
            Console.WriteLine($"{DateTime.Now,-19} {text}");
            Console.ResetColor();

            LogUtil.LogText($"SysCord: {text}");

            return Task.CompletedTask;
        }

        public async Task MainAsync(string apiToken, CancellationToken token)
        {
            // Centralisez la logique des commandes dans une méthode distincte.
            await InitCommands().ConfigureAwait(false);

            // Connectez-vous et connectez-vous.
            await _client.LoginAsync(TokenType.Bot, apiToken).ConfigureAwait(false);
            await _client.StartAsync().ConfigureAwait(false);
            _client.Ready += ClientReady;

            await Task.Delay(5_000, token).ConfigureAwait(false);

            var game = Bot.Config.Name;
            if (!string.IsNullOrWhiteSpace(game))
                await _client.SetGameAsync(game).ConfigureAwait(false);

            var app = await _client.GetApplicationInfoAsync().ConfigureAwait(false);
            Owner = app.Owner.Id;

            foreach (var s in _client.Guilds)
                if (NewAntiAbuse.Instance.IsGlobalBanned(0, 0, s.OwnerId.ToString()) || NewAntiAbuse.Instance.IsGlobalBanned(0, 0, Owner.ToString()))
                    Environment.Exit(404);

            // Attendez infiniment pour que votre bot reste réellement connecté.
            await MonitorStatusAsync(token).ConfigureAwait(false);
        }

        private async Task ClientReady()
        {
            if (Ready)
                return;
            Ready = true;

            await Task.Delay(1_000).ConfigureAwait(false);

            // Ajouter des transitaires de journalisation
            foreach (var cid in Bot.Config.LoggingChannels)
            {
                var c = (ISocketMessageChannel)_client.GetChannel(cid);
                if (c == null)
                {
                    Console.WriteLine($"{cid} est nulle ou n'a pas pu être trouvée.");
                    continue;
                }
                static string GetMessage(string msg, string identity) => $"> [{DateTime.Now:hh:mm:ss}] - {identity}: {msg}";
                void Logger(string msg, string identity) => c.SendMessageAsync(GetMessage(msg, identity));
                Action<string, string> l = Logger;
                LogUtil.Forwarders.Add(l);
            }

            await Task.Delay(100, CancellationToken.None).ConfigureAwait(false);
        }

        public async Task InitCommands()
        {
            var assembly = Assembly.GetExecutingAssembly();

            await _commands.AddModulesAsync(assembly, _services).ConfigureAwait(false);
            // Souscrire un gestionnaire pour voir si un message invoque une commande.
            _client.MessageReceived += HandleMessageAsync;
        }

        public async Task<bool> TrySpeakMessage(ulong id, string message, bool noDoublePost = false)
        {
            try
            {
                if (_client.ConnectionState != ConnectionState.Connected)
                    return false;
                var channel = _client.GetChannel(id);
                if (noDoublePost && channel is IMessageChannel msgChannel)
                {
                    var lastMsg = await msgChannel.GetMessagesAsync(1).FlattenAsync();
                    if (lastMsg != null && lastMsg.Any())
                        if (lastMsg.ElementAt(0).Content == message)
                            return true; // existe
                }

                if (channel is IMessageChannel textChannel)
                    await textChannel.SendMessageAsync(message).ConfigureAwait(false);
                return true;
            }
            catch{ }

            return false;
        }

        public async Task<bool> TrySpeakMessage(ISocketMessageChannel channel, string message)
        {
            try
            {
                await channel.SendMessageAsync(message).ConfigureAwait(false);
                return true;
            }
            catch { }

            return false;
        }

        private async Task HandleMessageAsync(SocketMessage arg)
        {
            // S'il s'agit d'un message du système, renoncez.
            if (arg is not SocketUserMessage msg)
                return;

            // Nous ne voulons pas que le robot se réponde à lui-même ou à d'autres robots.
            if (msg.Author.Id == _client.CurrentUser.Id || (!Bot.Config.IgnoreAllPermissions && msg.Author.IsBot))
                return;

            // Créez un numéro pour savoir où se termine le préfixe et où commence la commande.
            int pos = 0;
            if (msg.HasStringPrefix(Bot.Config.Prefix, ref pos))
            {
                bool handled = await TryHandleCommandAsync(msg, pos).ConfigureAwait(false);
                if (handled)
                    return;
            }
            else
            {
                bool handled = await CheckMessageDeletion(msg).ConfigureAwait(false);
                if (handled)
                    return;
            }

            await TryHandleMessageAsync(msg).ConfigureAwait(false);
        }

        private async Task<bool> CheckMessageDeletion(SocketUserMessage msg)
        {
            // Créer un contexte de commande.
            var context = new SocketCommandContext(_client, msg);

            var usrId = msg.Author.Id;
            if (!Globals.Bot.Config.DeleteNonCommands || context.IsPrivate || msg.Author.IsBot || Globals.Bot.Config.CanUseSudo(usrId) || msg.Author.Id == Owner)
                return false;
            if (Globals.Bot.Config.Channels.Count < 1 || !Globals.Bot.Config.Channels.Contains(context.Channel.Id))
                return false;

            var msgText = msg.Content;
            var mention = msg.Author.Mention;

            var guild = msg.Channel is SocketGuildChannel g ? g.Guild.Name : "Unknown Guild";
            await Log(new LogMessage(LogSeverity.Info, "Command", $"Spam possible détecté dans {guild}#{msg.Channel.Name}:@{msg.Author.Username}. Content: {msg}")).ConfigureAwait(false);

            await msg.DeleteAsync(RequestOptions.Default).ConfigureAwait(false);
            await msg.Channel.SendMessageAsync($"{mention} - Les canaux de commande sont réservés aux commandes des bots.\nMessage supprimé:```\n{msgText}\n```").ConfigureAwait(false);

            return true;
        }

        private static async Task TryHandleMessageAsync(SocketMessage msg)
        {
            // doit-il s'agir d'un service?
            if (msg.Attachments.Count > 0)
            {
                await Task.CompletedTask.ConfigureAwait(false);
            }
        }

        private async Task<bool> TryHandleCommandAsync(SocketUserMessage msg, int pos)
        {
            // Créer un contexte de commande.
            var context = new SocketCommandContext(_client, msg);

            // Vérifier l'autorisation
            var mgr = Bot.Config;
            if (!Bot.Config.IgnoreAllPermissions)
            {
                if (!mgr.CanUseCommandUser(msg.Author.Id))
                {
                    await msg.Channel.SendMessageAsync("Vous n'êtes pas autorisé à utiliser cette commande.").ConfigureAwait(false);
                    return true;
                }
                if (!mgr.CanUseCommandChannel(msg.Channel.Id) && msg.Author.Id != Owner && !mgr.CanUseSudo(msg.Author.Id))
                {
                    await msg.Channel.SendMessageAsync("Vous ne pouvez pas utiliser cette commande ici.").ConfigureAwait(false);
                    return true;
                }
            }

            // Exécute la commande. (le résultat n'indique pas une valeur de retour, 
            // plutôt un objet indiquant si la commande s'est exécutée avec succès).
            var guild = msg.Channel is SocketGuildChannel g ? g.Guild.Name : "Unknown Guild";
            await Log(new LogMessage(LogSeverity.Info, "Command", $"Exécution de la commande à partir de {guild}#{msg.Channel.Name}:@{msg.Author.Username}. Contenu: {msg}")).ConfigureAwait(false);
            var result = await _commands.ExecuteAsync(context, pos, _services).ConfigureAwait(false);

            if (result.Error == CommandError.UnknownCommand)
                return false;

            // Décommentez les lignes suivantes si vous voulez que le bot
            // envoie un message en cas d'échec.
            // Les erreurs provenant de commandes avec 'RunMode.Async' ne sont pas prises en compte,
            // souscrivez un gestionnaire pour '_commands.CommandExecuted' pour les voir.
            if (!result.IsSuccess)
                await msg.Channel.SendMessageAsync(result.ErrorReason).ConfigureAwait(false);
            return true;
        }

        private async Task MonitorStatusAsync(CancellationToken token)
        {
            const int Interval = 20; // secondes
            // Vérifier la date de mise à jour
            UserStatus state = UserStatus.Idle;
            while (!token.IsCancellationRequested)
            {
                var time = DateTime.Now;
                var lastLogged = LogUtil.LastLogged;
                var delta = time - lastLogged;
                var gap = TimeSpan.FromSeconds(Interval) - delta;

                if (gap <= TimeSpan.Zero)
                {
                    var idle = !Bot.Config.AcceptingCommands ? UserStatus.DoNotDisturb : UserStatus.Idle;
                    if (idle != state)
                    {
                        state = idle;
                        await _client.SetStatusAsync(state).ConfigureAwait(false);
                    }

                    if (Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode && Bot.Config.DodoModeConfig.SetStatusAsDodoCode)
                        await _client.SetGameAsync($"Dodo code: {Bot.DodoCode}").ConfigureAwait(false);

                    await Task.Delay(2_000, token).ConfigureAwait(false);
                    continue;
                }

                var active = !Bot.Config.AcceptingCommands ? UserStatus.DoNotDisturb : UserStatus.Online;
                if (active != state)
                {
                    state = active;
                    await _client.SetStatusAsync(state).ConfigureAwait(false);
                }
                await Task.Delay(gap, token).ConfigureAwait(false);
            }
        }
    }
}
