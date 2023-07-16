using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.Net;
using NHSE.Core;
using SysBot.Base;

namespace SysBot.ACNHOrders
{
    // ReSharper désactivé une fois UnusedType.Global
    public class DropModule : ModuleBase<SocketCommandContext>
    {
        private static int MaxRequestCount => Globals.Bot.Config.DropConfig.MaxDropCount;

        [Command("clean")]
        [Summary("Picks up items around the bot.")]
        public async Task RequestCleanAsync()
        {
            if (!await GetDropAvailability().ConfigureAwait(false))
                return;

            if (!Globals.Bot.Config.AllowClean)
            {
                await ReplyAsync("La fonctionnalité de nettoyage est actuellement désactivée.").ConfigureAwait(false);
                return;
            }
            Globals.Bot.CleanRequested = true;
            await ReplyAsync("Une requête propre sera exécutée momentanément.").ConfigureAwait(false);
        }

        [Command("code")]
        [Alias("dodo")]
        [Summary("Prints the Dodo Code for the island.")]
        [RequireSudo]
        public async Task RequestDodoCodeAsync()
        {
            var draw = Globals.Bot.DodoImageDrawer;
            var txt = $"Code Dodo pour {Globals.Bot.TownName}: {Globals.Bot.DodoCode}.";
            if (draw != null )
            {
                var path = draw.GetProcessedDodoImagePath();
                if (path != null)
                {
                    await Context.Channel.SendFileAsync(path, txt);
                    return;
                }
            }
            
            await ReplyAsync(txt).ConfigureAwait(false);
        }

        [Command("sendDodo")]
        [Alias("sd", "send")]
        [Summary("Prints the Dodo Code for the island. Only works in dodo restore mode.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task RequestRestoreLoopDodoAsync()
        {
            if (!Globals.Bot.Config.DodoModeConfig.AllowSendDodo && !Globals.Bot.Config.CanUseSudo(Context.User.Id) && Globals.Self.Owner != Context.User.Id)
                return;
            if (!Globals.Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode)
                return;
            try
            {
                await Context.User.SendMessageAsync($"Code Dodo pour {Globals.Bot.TownName}: {Globals.Bot.DodoCode}.").ConfigureAwait(false);
            }
            catch (HttpException ex)
            {
                await ReplyAsync($"{ex.Message}: Les messages privés doivent être ouverts pour utiliser cette commande. Je ne divulguerai pas le code Dodo dans ce canal !");
                return;
            }

            var reaction = Globals.Bot.Config.DodoModeConfig.SuccessfulDodoCodeSendReaction;
            if (!string.IsNullOrWhiteSpace(reaction))
            {
                try
                {
                    IEmote emote = reaction.StartsWith("<") ? Emote.Parse(reaction) : new Emoji(reaction);
                    await Context.Message.AddReactionAsync(emote);
                }
                catch 
                {
                    LogUtil.LogError($"Impossible d'analyser {reaction} comme emote, ou les permissions nécessaires ne sont pas données à ce bot.", "Config");
                }
            }
        }

        private const string DropItemSummary =
            "Demande au robot de déposer un objet en fonction des données fournies par l'utilisateur. " +
            "Mode hexagonal : ID des éléments (en hexadécimal) ; demandez-en plusieurs en mettant des espaces entre les éléments. " +
            "Mode texte : Noms des éléments ; demandez-en plusieurs en mettant des virgules entre les éléments. Pour analyser une autre langue, indiquez d'abord le code de la langue et une virgule, puis les éléments.";

        [Command("drop")]
        [Alias("dropItem")]
        [Summary("Drops a custom item (or items).")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task RequestDropAsync([Summary(DropItemSummary)][Remainder]string request)
        {
            var cfg = Globals.Bot.Config;
            var items = ItemParser.GetItemsFromUserInput(request, cfg.DropConfig, cfg.DropConfig.UseLegacyDrop ? ItemDestination.PlayerDropped : ItemDestination.HeldItem);

            MultiItem.StackToMax(items);
            await DropItems(items).ConfigureAwait(false);
        }

        private const string DropDIYSummary =
            "Demande au robot d'élaborer une recette de bricolage à partir des données fournies par l'utilisateur." +
            "Mode hexagonal : ID des recettes de bricolage (en hexagone) ; demandez-en plusieurs en mettant des espaces entre les éléments." +
            "Mode texte : DIY Recipe Noms des articles ; demandez-en plusieurs en mettant des virgules entre les articles. Pour analyser une autre langue, indiquez d'abord le code de la langue et une virgule, puis les éléments.";

        [Command("dropDIY")]
        [Alias("diy")]
        [Summary("Drops a DIY recipe with the requested recipe ID(s).")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task RequestDropDIYAsync([Summary(DropDIYSummary)][Remainder]string recipeIDs)
        {
            var items = ItemParser.GetDIYsFromUserInput(recipeIDs);
            await DropItems(items).ConfigureAwait(false);
        }

        [Command("setTurnips")]
        [Alias("turnips")]
        [Summary("Sets all the week's turnips (minus Sunday) to a certain value.")]
        [RequireSudo]
        public async Task RequestTurnipSetAsync(int value)
        {
            var bot = Globals.Bot;
            bot.StonkRequests.Enqueue(new TurnipRequest(Context.User.Username, value)
            {
                OnFinish = success =>
                {
                    var reply = success
                        ? $"Toutes les valeurs du navet ont été réglées avec succès sur {value}!"
                        : "Défaillance catastrophique.";
                    Task.Run(async () => await ReplyAsync($"{Context.User.Mention}: {reply}").ConfigureAwait(false));
                }
            });
            await ReplyAsync($"Mise en file d'attente de toutes les valeurs de navet à régler sur {value}.");
        }

        [Command("setTurnipsMax")]
        [Alias("turnipsMax", "stonks")]
        [Summary("Sets all the week's turnips (minus Sunday) to 999,999,999")]
        [RequireSudo]
        public async Task RequestTurnipMaxSetAsync() => await RequestTurnipSetAsync(999999999);

        private async Task DropItems(IReadOnlyCollection<Item> items)
        {
            if (!await GetDropAvailability().ConfigureAwait(false))
                return;

            if (!InternalItemTool.CurrentInstance.IsSane(items, Globals.Bot.Config.DropConfig))
            {
                await ReplyAsync($"{Context.User.Mention} - Vous tentez de déposer des objets qui endommageront votre sauvegarde. Demande de dépôt non acceptée.");
                return;
            }

            if (items.Count > MaxRequestCount)
            {
                var clamped = $"Les utilisateurs sont limités à {MaxRequestCount} articles par commande. Veuillez utiliser ce robot de manière responsable.";
                await ReplyAsync(clamped).ConfigureAwait(false);
                items = items.Take(MaxRequestCount).ToArray();
            }

            var requestInfo = new ItemRequest(Context.User.Username, items);
            Globals.Bot.Injections.Enqueue(requestInfo);

            var msg = $"Demande de dépôt d'un article {(requestInfo.Item.Count > 1 ? "s" : string.Empty)} sera exécuté momentanément.";
            await ReplyAsync(msg).ConfigureAwait(false);
        }

        private async Task<bool> GetDropAvailability()
        {
            var cfg = Globals.Bot.Config;

            if (cfg.CanUseSudo(Context.User.Id) || Globals.Self.Owner == Context.User.Id)
                return true;

            if (Globals.Bot.CurrentUserId == Context.User.Id)
                return true;

            if (!cfg.AllowDrop)
            {
                await ReplyAsync($"AllowDrop est actuellement réglé sur false.");
                return false;
            }
            else if (!cfg.DodoModeConfig.LimitedDodoRestoreOnlyMode)
            {
                await ReplyAsync($"{Context.User.Mention} - Vous n'êtes autorisé à utiliser cette commande que sur l'île, pendant votre commande, et seulement si vous avez oublié quelque chose dans votre commande.");
                return false;
            }

            return true;
        }
    }
}
