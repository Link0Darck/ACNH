using System;
using System.Threading;
using System.Threading.Tasks;
using ACNHMobileSpawner;
using Discord.Commands;
using SysBot.Base;

namespace SysBot.ACNHOrders
{
    // ReSharper désactivé une fois UnusedType.Global
    public class ControlModule : ModuleBase<SocketCommandContext>
    {
        [Command("detach")]
        [Summary("Detaches the virtual controller so the operator can use their own handheld controller temporarily.")]
        [RequireSudo]
        public async Task DetachAsync()
        {
            await ReplyAsync("Une demande de détachement du contrôleur sera exécutée momentanément.").ConfigureAwait(false);
            var bot = Globals.Bot;
            await bot.Connection.SendAsync(SwitchCommand.DetachController(), CancellationToken.None).ConfigureAwait(false);
        }

        [Command("toggleRequests")]
        [Summary("Toggles accepting drop requests.")]
        [RequireSudo]
        public async Task ToggleRequestsAsync()
        {
            bool value = (Globals.Bot.Config.AcceptingCommands ^= true);
            await ReplyAsync($"Accepter les demandes de dépôt: {value}.").ConfigureAwait(false);
        }

        [Command("toggleMashB")]
        [Summary("Toggle whether or not the bot should mash the B button to ensure all dialogue is processed. Only works in dodo restore mode.")]
        [RequireSudo]
        public async Task ToggleMashB()
        {
            Globals.Bot.Config.DodoModeConfig.MashB = !Globals.Bot.Config.DodoModeConfig.MashB;
            await ReplyAsync($"Mash B réglé sur : {Globals.Bot.Config.DodoModeConfig.MashB}.").ConfigureAwait(false);
        }

        [Command("toggleRefresh")]
        [Summary("Toggle whether or not the bot should refresh the map. Only works in dodo restore mode.")]
        public async Task ToggleRefresh()
        {
            Globals.Bot.Config.DodoModeConfig.RefreshMap = !Globals.Bot.Config.DodoModeConfig.RefreshMap;
            await ReplyAsync($"RefreshMap défini sur : {Globals.Bot.Config.DodoModeConfig.RefreshMap}.").ConfigureAwait(false);
        }

        [Command("newDodo")]
        [Alias("restartGame", "restart")]
        [Summary("Tells the bot to restart the game and fetch a new dodo code. Only works in dodo restore mode.")]
        [RequireSudo]
        public async Task FetchNewDodo()
        {
            Globals.Bot.RestoreRestartRequested = true;
            await ReplyAsync($"Envoi d'une demande de récupération d'un nouveau code dodo.").ConfigureAwait(false);
        }

        [Command("timer")]
        [Alias("timedDodo", "delayDodo")]
        [Summary("Tells the bot to restart the game after a delay and fetch a new dodo code. Only works in dodo restore mode.")]
        [RequireSudo]
        public async Task DelayFetchNewDodo(int timeDelayMinutes)
        {
            _ = Task.Run(async () =>
              {
                  await Task.Delay(timeDelayMinutes * 60_000, CancellationToken.None).ConfigureAwait(false);
                  Globals.Bot.RestoreRestartRequested = true;
                  await ReplyAsync($"Je vais bientôt récupérer un nouveau code de dodo.").ConfigureAwait(false);
              }, CancellationToken.None).ConfigureAwait(false);
            await ReplyAsync($"Envoi d'une demande de récupération d'un nouveau code dodo après {timeDelayMinutes} minutes.").ConfigureAwait(false);
        }

        [Command("speak")]
        [Alias("talk", "say")]
        [Summary("Tells the bot to speak during times when people are on the island.")]
        [RequireSudo]
        public async Task SpeakAsync([Remainder] string request)
        {
            var saneString = request.Length > (int)OffsetHelper.ChatBufferSize ? request.Substring(0, (int)OffsetHelper.ChatBufferSize) : request;
            Globals.Bot.Speaks.Enqueue(new SpeakRequest(Context.User.Username, saneString));
            await ReplyAsync($"Je dirai `{saneString}` prochainement.").ConfigureAwait(false);
        }

        [Command("setScreenOn")]
        [Alias("screenOn", "scrOn")]
        [Summary("Turns the screen on")]
        [RequireSudo]
        public async Task SetScreenOnAsync()
        {
            await SetScreen(true).ConfigureAwait(false);
        }

        [Command("setScreenOff")]
        [Alias("screenOff", "scrOff")]
        [Summary("Turns the screen off")]
        [RequireSudo]
        public async Task SetScreenOffAsync()
        {
            await SetScreen(false).ConfigureAwait(false);
        }

        [Command("kill")]
        [Alias("sudoku", "exit")]
        [Summary("Kills the bot")]
        [RequireSudo]
        public async Task KillBotAsync()
        {
            await ReplyAsync($"Au revoir {Context.User.Mention}, se souvenir de moi.").ConfigureAwait(false);
            Environment.Exit(0);
        }

        private async Task SetScreen(bool on)
        {
            var bot = Globals.Bot;
                
            await bot.SetScreenCheck(on, CancellationToken.None, true).ConfigureAwait(false);
            await ReplyAsync("État de l'écran réglé sur : " + (on ? "On" : "Off")).ConfigureAwait(false);
        }
    }
}
