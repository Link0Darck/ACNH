using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.WebSocket;
using NHSE.Core;
using NHSE.Villagers;

namespace SysBot.ACNHOrders
{
    // ReSharper disable once UnusedType.Global
    public class OrderModule : ModuleBase<SocketCommandContext>
    {
        private static int MaxOrderCount => Globals.Bot.Config.OrderConfig.MaxQueueCount;
        private static Dictionary<ulong, DateTime> UserLastCommand = new();
        private static object commandSync = new();

        private const string OrderItemSummary =
            "Demande au robot d'ajouter la commande d'articles à la file d'attente avec les données fournies par l'utilisateur." +
            "Mode hexagonal : ID des éléments (en hexadécimal) ; demandez-en plusieurs en mettant des espaces entre les éléments." +
            "Mode texte : Noms des éléments ; demandez-en plusieurs en mettant des virgules entre les éléments. Pour analyser une autre langue, indiquez d'abord le code de la langue et une virgule, puis les éléments.";

        [Command("order")]
        [Summary(OrderItemSummary)]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task RequestOrderAsync([Summary(OrderItemSummary)][Remainder] string request)
        {
            var cfg = Globals.Bot.Config;
            VillagerRequest? vr = null;

            // essayer d'avoir un villageois
            var result = VillagerOrderParser.ExtractVillagerName(request, out var res, out var san);
            if (result == VillagerOrderParser.VillagerRequestResult.InvalidVillagerRequested)
            {
                await ReplyAsync($"{Context.User.Mention} - {res} La commande n'a pas été acceptée.");
                return;
            }

            if (result == VillagerOrderParser.VillagerRequestResult.Success)
            {
                if (!cfg.AllowVillagerInjection)
                {
                    await ReplyAsync($"{Context.User.Mention} - L'injection Villager est actuellement désactivée.");
                    return;
                }

                request = san;
                var replace = VillagerResources.GetVillager(res);
                vr = new VillagerRequest(Context.User.Username, replace, 0, GameInfo.Strings.GetVillager(res));
            }

            Item[]? items = null;

            var attachment = Context.Message.Attachments.FirstOrDefault();
            if (attachment != default)
            {
                var att = await NetUtil.DownloadNHIAsync(attachment).ConfigureAwait(false);
                if (!att.Success || !(att.Data is Item[] itemData))
                {
                    await ReplyAsync("Aucune pièce jointe NHI fournie!").ConfigureAwait(false);
                    return;
                }
                else
                {
                    items = itemData;
                }
            }

            if (items == null)
                items = string.IsNullOrWhiteSpace(request) ? new Item[1] { new Item(Item.NONE) } : ItemParser.GetItemsFromUserInput(request, cfg.DropConfig, ItemDestination.FieldItemDropped).ToArray();

            await AttemptToQueueRequest(items, Context.User, Context.Channel, vr).ConfigureAwait(false);
        }

        [Command("ordercat")]
        [Summary("Orders a catalogue of items created by an order tool such as ACNHMobileSpawner, does not duplicate any items.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task RequestCatalogueOrderAsync([Summary(OrderItemSummary)][Remainder] string request)
        {
            var cfg = Globals.Bot.Config;
            VillagerRequest? vr = null;

            // essayer d'avoir un villageois
            var result = VillagerOrderParser.ExtractVillagerName(request, out var res, out var san);
            if (result == VillagerOrderParser.VillagerRequestResult.InvalidVillagerRequested)
            {
                await ReplyAsync($"{Context.User.Mention} - {res} La commande n'a pas été acceptée.");
                return;
            }

            if (result == VillagerOrderParser.VillagerRequestResult.Success)
            {
                if (!cfg.AllowVillagerInjection)
                {
                    await ReplyAsync($"{Context.User.Mention} - L'injection Villager est actuellement désactivée.");
                    return;
                }

                request = san;
                var replace = VillagerResources.GetVillager(res);
                vr = new VillagerRequest(Context.User.Username, replace, 0, GameInfo.Strings.GetVillager(res));
            }

            var items = string.IsNullOrWhiteSpace(request) ? new Item[1] { new Item(Item.NONE) } : ItemParser.GetItemsFromUserInput(request, cfg.DropConfig, ItemDestination.FieldItemDropped);
            await AttemptToQueueRequest(items, Context.User, Context.Channel, vr, true).ConfigureAwait(false);
        }

        [Command("order")]
        [Summary("Requests the bot an order of items in the NHI format.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task RequestNHIOrderAsync()
        {
            var attachment = Context.Message.Attachments.FirstOrDefault();
            if (attachment == default)
            {
                await ReplyAsync("Pas de pièce jointe fournie!").ConfigureAwait(false);
                return;
            }

            var att = await NetUtil.DownloadNHIAsync(attachment).ConfigureAwait(false);
            if (!att.Success || !(att.Data is Item[] items))
            {
                await ReplyAsync("Aucune pièce jointe NHI fournie!").ConfigureAwait(false);
                return;
            }

            await AttemptToQueueRequest(items, Context.User, Context.Channel, null, true).ConfigureAwait(false);
        }

        [Command("preset")]
        [Summary("Requests the bot an order of a preset created by the bot host.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task RequestPresetOrderAsync([Remainder] string presetName)
        {
            var cfg = Globals.Bot.Config;
            VillagerRequest? vr = null;

            // essayer d'avoir un villageois
            var result = VillagerOrderParser.ExtractVillagerName(presetName, out var res, out var san);
            if (result == VillagerOrderParser.VillagerRequestResult.InvalidVillagerRequested)
            {
                await ReplyAsync($"{Context.User.Mention} - {res} La commande n'a pas été acceptée.");
                return;
            }

            if (result == VillagerOrderParser.VillagerRequestResult.Success)
            {
                if (!cfg.AllowVillagerInjection)
                {
                    await ReplyAsync($"{Context.User.Mention} - L'injection Villager est actuellement désactivée.");
                    return;
                }

                presetName = san;
                var replace = VillagerResources.GetVillager(res);
                vr = new VillagerRequest(Context.User.Username, replace, 0, GameInfo.Strings.GetVillager(res));
            }

            presetName = presetName.Trim();
            var preset = PresetLoader.GetPreset(cfg.OrderConfig, presetName);
            if (preset == null)
            {
                await ReplyAsync($"{Context.User.Mention} - {presetName} n'est pas une présélection valide.");
                return;
            }

            await AttemptToQueueRequest(preset, Context.User, Context.Channel, vr, true).ConfigureAwait(false);
        }

        [Command("queue")]
        [Alias("qs", "qp", "position")]
        [Summary("View your position in the queue.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task ViewQueuePositionAsync()
        {
            var cooldown = Globals.Bot.Config.OrderConfig.PositionCommandCooldown;
            if (!CanCommand(Context.User.Id, cooldown, true))
            {
                await ReplyAsync($"{Context.User.Mention} - Cette commande a un {cooldown} secondes d'attentes. Utilisez ce bot de manière responsable.").ConfigureAwait(false);
                return;
            }

            var position = QueueExtensions.GetPosition(Context.User.Id, out _);
            if (position < 0)
            {
                await ReplyAsync("Désolé, vous n'êtes pas dans la file d'attente, ou votre commande est en cours d'exécution.").ConfigureAwait(false);
                return;
            }

            var message = $"{Context.User.Mention} - Vous êtes dans la file d'attente des commandes. Position : {position}.";
            if (position > 1)
                message += $" Votre heure d'arrivée prévue est {QueueExtensions.GetETA(position)}.";
            else
                message += " Votre commande commencera après la fin de la commande en cours.!";

            await ReplyAsync(message).ConfigureAwait(false);
            await Context.Message.DeleteAsync().ConfigureAwait(false);
        }

        [Command("remove")]
        [Alias("qc", "delete", "removeMe", "cancel")]
        [Summary("Remove yourself from the queue.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task RemoveFromQueueAsync()
        {
            QueueExtensions.GetPosition(Context.User.Id, out var order);
            if (order == null)
            {
                await ReplyAsync($"{Context.User.Mention} - Désolé, vous n'êtes pas dans la file d'attente, ou votre commande est en cours d'exécution.").ConfigureAwait(false);
                return;
            }

            order.SkipRequested = true;
            await ReplyAsync($"{Context.User.Mention} - Votre commande a été supprimée. Veuillez noter que vous ne pourrez pas vous réinscrire dans la file d'attente avant un certain temps.").ConfigureAwait(false);
        }

        [Command("removeUser")]
        [Alias("rmu", "removeOther", "rmo")]
        [Summary("Remove someone from the queue.")]
        [RequireSudo]
        public async Task RemoveOtherFromQueueAsync(string identity)
        {
            if (ulong.TryParse(identity, out var res))
            {
                QueueExtensions.GetPosition(res, out var order);
                if (order == null)
                {
                    await ReplyAsync($"{identity} n'est pas un ulong valide dans la file d'attente.").ConfigureAwait(false);
                    return;
                }

                order.SkipRequested = true;
                await ReplyAsync($"{identity} ({order.VillagerName}) a été retiré de la file d'attente.").ConfigureAwait(false);
            }
            else
                await ReplyAsync($"{identity} n'est pas un u64 valide.").ConfigureAwait(false);
        }

        [Command("removeAlt")]
        [Alias("removeLog", "rmAlt")]
        [Summary("Removes an identity (name-id) from the local user-to-villager AntiAbuse database")]
        [RequireSudo]
        public async Task RemoveAltAsync([Remainder]string identity)
        {
            if (NewAntiAbuse.Instance.Remove(identity))
                await ReplyAsync($"{identity} a été supprimé de la base de données.").ConfigureAwait(false);
            else
                await ReplyAsync($"{identity} n'est pas une identité valide.").ConfigureAwait(false);
        }

        [Command("removeAltLegacy")]
        [Alias("removeLogLegacy", "rmAltLegacy")]
        [Summary("(Uses legacy database) Removes an identity (name-id) from the local user-to-villager AntiAbuse database")]
        [RequireSudo]
        public async Task RemoveLegacyAltAsync([Remainder] string identity)
        {
            if (LegacyAntiAbuse.CurrentInstance.Remove(identity))
                await ReplyAsync($"{identity} a été supprimé de la base de données.").ConfigureAwait(false);
            else
                await ReplyAsync($"{identity} n'est pas une identité valide.").ConfigureAwait(false);
        }

        [Command("visitorList")]
        [Alias("visitors")]
        [Summary("Print the list of visitors on the island (dodo restore mode only).")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task ShowVisitorList()
        {
            if (!Globals.Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode && Globals.Self.Owner != Context.User.Id)
            {
                await ReplyAsync($"{Context.User.Mention} - Vous ne pouvez visualiser les visiteurs qu'en mode de restauration dodo. Veuillez respecter la vie privée des autres commanditaires.");
                return;
            }

            await ReplyAsync(Globals.Bot.VisitorList.VisitorFormattedString);
        }

        [Command("checkState")]
        [Alias("checkDirtyState")]
        [Summary("Prints whether or not the bot will restart the game for the next order.")]
        [RequireSudo]
        public async Task ShowDirtyStateAsync()
        {
            if (Globals.Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode)
            {
                await ReplyAsync("Il n'y a pas d'état de commande dans le mode de restauration du dodo.");
                return;
            }

            await ReplyAsync($"État: {(Globals.Bot.GameIsDirty? "Mauvais" : "Bon")}").ConfigureAwait(false);
        }

        [Command("queueList")]
        [Alias("ql")]
        [Summary("DMs the user the current list of names in the queue.")]
        [RequireSudo]
        public async Task ShowQueueListAsync()
        {
            if (Globals.Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode)
            {
                await ReplyAsync("Il n'y a pas de file d'attente dans le mode de restauration du dodo.").ConfigureAwait(false);
                return;
            }

            try
            {
                await Context.User.SendMessageAsync($"Les utilisateurs suivants sont dans la file d'attente pour {Globals.Bot.TownName}: \r\n{QueueExtensions.GetQueueString()}").ConfigureAwait(false);
            }
            catch (Exception e)
            {
                await ReplyAsync($"{e.Message}: Vos DMs sont-ils ouverts?").ConfigureAwait(false);
            }
        }

        [Command("gameTime")]
        [Alias("gt")]
        [Summary("Prints the last checked (current) in-game time.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task GetGameTime()
        {
            var bot = Globals.Bot;
            var cooldown = bot.Config.OrderConfig.PositionCommandCooldown;
            if (!CanCommand(Context.User.Id, cooldown, true))
            {
                await ReplyAsync($"{Context.User.Mention} - Cette commande a un {cooldown} second cooldown. Utilisez ce bot de manière responsable.").ConfigureAwait(false);
                return;
            }

            if (Globals.Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode)
            {
                var nooksMessage = (bot.LastTimeState.Hour >= 22 || bot.LastTimeState.Hour < 8) ? "Nook's Cranny est fermé" : "Nook's Cranny devrait être ouvert.";
                await ReplyAsync($"L'heure actuelle du jeu est: {bot.LastTimeState} \r\n{nooksMessage}").ConfigureAwait(false);
                return;
            }

            await ReplyAsync($"La dernière commande a débuté à : {bot.LastTimeState}").ConfigureAwait(false);
            return;
        }

        private async Task AttemptToQueueRequest(IReadOnlyCollection<Item> items, SocketUser orderer, ISocketMessageChannel msgChannel, VillagerRequest? vr, bool catalogue = false)
        {
            if (!Globals.Bot.Config.AllowKnownAbusers && LegacyAntiAbuse.CurrentInstance.IsGlobalBanned(orderer.Id))
            {
                await ReplyAsync($"{Context.User.Mention} - Vous n'êtes pas autorisé à utiliser ce bot.");
                return;
            }

            if (Globals.Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode || Globals.Bot.Config.SkipConsoleBotCreation)
            {
                await ReplyAsync($"{Context.User.Mention} - Les commandes ne sont pas acceptées actuellement.");
                return;
            }

            if (GlobalBan.IsBanned(orderer.Id.ToString()))
            {
                await ReplyAsync($"{Context.User.Mention} - Vous avez été banni pour abus. La commande n'a pas été acceptée merci d'ouvrir un ticket dans <#902214397718569031>.");
                return;
            }

            var currentOrderCount = Globals.Hub.Orders.Count;
            if (currentOrderCount >= MaxOrderCount)
            {
                var requestLimit = $"La limite de la file d'attente a été atteinte, il y a actuellement {currentOrderCount} joueurs dans la file d'attente. Veuillez réessayer plus tard.";
                await ReplyAsync(requestLimit).ConfigureAwait(false);
                return;
            }

            if (!InternalItemTool.CurrentInstance.IsSane(items, Globals.Bot.Config.DropConfig))
            {
                await ReplyAsync($"{Context.User.Mention} - Vous tentez de commander des articles qui endommageront votre sauvegarde. Commande non acceptée.");
                return;
            }

            if (items.Count > MultiItem.MaxOrder)
            {
                var clamped = $"Les utilisateurs sont limités à {MultiItem.MaxOrder} articles par commande, vous avez demandé {items.Count}. Tous les articles dépassant la limite ont été supprimés.";
                await ReplyAsync(clamped).ConfigureAwait(false);
                items = items.Take(40).ToArray();
            }

            var multiOrder = new MultiItem(items.ToArray(), catalogue, true, true);
            var requestInfo = new OrderRequest<Item>(multiOrder, multiOrder.ItemArray.Items.ToArray(), orderer.Id, QueueExtensions.GetNextID(), orderer, msgChannel, vr);
            await Context.AddToQueueAsync(requestInfo, orderer.Username, orderer);
        }

        public static bool CanCommand(ulong id, int secondsCooldown, bool addIfNotAdded)
        {
            if (secondsCooldown < 0)
                return true;
            lock (commandSync)
            {
                if (UserLastCommand.ContainsKey(id))
                {
                    bool inCooldownPeriod = Math.Abs((DateTime.Now - UserLastCommand[id]).TotalSeconds) < secondsCooldown;
                    if (addIfNotAdded && !inCooldownPeriod)
                    {
                        UserLastCommand.Remove(id);
                        UserLastCommand.Add(id, DateTime.Now);
                    }
                    return !inCooldownPeriod;
                }
                else if (addIfNotAdded)
                {
                    UserLastCommand.Add(id, DateTime.Now);
                }
                return true;
            }
        }
    }

    public static class VillagerOrderParser
    {
        public enum VillagerRequestResult
        {
            NoVillagerRequested,
            InvalidVillagerRequested,
            Success
        }

        public static VillagerRequestResult ExtractVillagerName(string order, out string result, out string sanitizedOrder, string villagerFormat = "Villager:")
        {
            result = string.Empty;
            sanitizedOrder = string.Empty;
            var index = order.IndexOf(villagerFormat, StringComparison.InvariantCultureIgnoreCase);
            if (index < 0)
                return VillagerRequestResult.NoVillagerRequested;

            var internalName = order.Substring(index + villagerFormat.Length);
            var nameSearched = internalName;
            internalName = internalName.Trim();

            if (!VillagerResources.IsVillagerDataKnown(internalName))
                internalName = GameInfo.Strings.VillagerMap.FirstOrDefault(z => string.Equals(z.Value, internalName, StringComparison.InvariantCultureIgnoreCase)).Key;

            if (IsUnadoptable(nameSearched) || IsUnadoptable(internalName))
            {
                result = $"{nameSearched} n'est pas adoptable. La préparation de la commande requise pour ce villageois est inutile.";
                return VillagerRequestResult.InvalidVillagerRequested;
            }

            if (internalName == default)
            {
                result = $"{nameSearched} n'est pas un nom de villageois interne valide.";
                return VillagerRequestResult.InvalidVillagerRequested;
            }

            sanitizedOrder = order.Substring(0, index);
            result = internalName;
            return VillagerRequestResult.Success;
        }

        private static readonly List<string> UnadoptableVillagers = new()
        {
            "cbr18",
            "der10",
            "elp11",
            "gor11",
            "rbt20",
            "shp14",
            "alp",
            "alw",
            "bev",
            "bey",
            "boa",
            "boc",
            "bpt",
            "chm",
            "chy",
            "cml",
            "cmlb",
            "dga",
            "dgb",
            "doc",
            "dod",
            "fox",
            "fsl",
            "grf",
            "gsta",
            "gstb",
            "gul",
            "gul",
            "hgc",
            "hgh",
            "hgs",
            "kpg",
            "kpm",
            "kpp",
            "kps",
            "lom",
            "man",
            "mka",
            "mnc",
            "mnk",
            "mob",
            "mol",
            "otg",
            "otgb",
            "ott",
            "owl",
            "ows",
            "pck",
            "pge",
            "pgeb",
            "pkn",
            "plk",
            "plm",
            "plo",
            "poo",
            "poob",
            "pyn",
            "rcm",
            "rco",
            "rct",
            "rei",
            "seo",
            "skk",
            "slo",
            "spn",
            "sza",
            "szo",
            "tap",
            "tkka",
            "tkkb",
            "ttla",
            "ttlb",
            "tuk",
            "upa",
            "wrl",
            "xct"
        };

        public static bool IsUnadoptable(string? internalName) => UnadoptableVillagers.Contains(internalName == null ? string.Empty : internalName.Trim().ToLower());
    }
}
