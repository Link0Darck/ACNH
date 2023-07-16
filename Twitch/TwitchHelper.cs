using NHSE.Core;
using NHSE.Villagers;
using SysBot.Base;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SysBot.ACNHOrders.Twitch
{
    public static class TwitchHelper
    {
        // Fonctions d'aide pour les commandes
        public static bool AddToWaitingList(string orderString, string display, string username, ulong id, bool sub, bool cat, out string msg)
        {
            if (!IsQueueable(orderString, id, out var msge))
            {
                msg = $"@{username} - {msge}";
                return false;
            }

            try
            {
                var cfg = Globals.Bot.Config;
                VillagerRequest? vr = null;

                // essayer d'avoir un villageois
                var result = VillagerOrderParser.ExtractVillagerName(orderString, out var res, out var san);
                if (result == VillagerOrderParser.VillagerRequestResult.InvalidVillagerRequested)
                {
                    msg = $"@{username} - {res} La commande n'a pas été acceptée.";
                    return false;
                }

                if (result == VillagerOrderParser.VillagerRequestResult.Success)
                {
                    if (!cfg.AllowVillagerInjection)
                    {
                        msg = $"@{username} - L'injection Villager est actuellement désactivée.";
                        return false;
                    }

                    orderString = san;
                    var replace = VillagerResources.GetVillager(res);
                    vr = new VillagerRequest(username, replace, 0, GameInfo.Strings.GetVillager(res));
                }

                var items = string.IsNullOrWhiteSpace(orderString) ? new Item[1] { new Item(Item.NONE) } : ItemParser.GetItemsFromUserInput(orderString, cfg.DropConfig, ItemDestination.FieldItemDropped);

                return InsertToQueue(items, vr, display, username, id, sub, cat, out msg);
            }
            catch (Exception e) 
            { 
                LogUtil.LogError($"{username}@{orderString}: {e.Message}", nameof(TwitchHelper)); 
                msg = $"@{username} {e.Message}";
                return false;
            }
        }

        public static bool AddToWaitingListPreset(string presetName, string display, string username, ulong id, bool sub, out string msg)
        {
            if (!IsQueueable(presetName, id, out var msge))
            {
                msg = $"@{username} - {msge}";
                return false;
            }

            try
            {
                var cfg = Globals.Bot.Config;
                VillagerRequest? vr = null;

                // essayer d'avoir un villageois
                var result = VillagerOrderParser.ExtractVillagerName(presetName, out var res, out var san);
                if (result == VillagerOrderParser.VillagerRequestResult.InvalidVillagerRequested)
                {
                    msg = $"@{username} - {res} La commande n'a pas été acceptée.";
                    return false;
                }

                if (result == VillagerOrderParser.VillagerRequestResult.Success)
                {
                    if (!cfg.AllowVillagerInjection)
                    {
                        msg = $"@{username} - L'injection Villager est actuellement désactivée.";
                        return false;
                    }

                    presetName = san;
                    var replace = VillagerResources.GetVillager(res);
                    vr = new VillagerRequest(username, replace, 0, GameInfo.Strings.GetVillager(res));
                }

                presetName = presetName.Trim();
                var preset = PresetLoader.GetPreset(cfg.OrderConfig, presetName);
                if (preset == null)
                {
                    msg = $"{username} - {presetName} n'est pas une présélection valide.";
                    return false;
                }

                return InsertToQueue(preset, vr, display, username, id, sub, true, out msg);
            }
            catch (Exception e)
            {
                LogUtil.LogError($"{username}@Preset:{presetName}: {e.Message}", nameof(TwitchHelper));
                msg = $"@{username} {e.Message}";
                return false;
            }
        }

        public static string ClearTrade(ulong userID)
        {
            QueueExtensions.GetPosition(userID, out var order);
            if (order == null)
                return "Désolé, vous n'êtes pas dans la file d'attente, ou votre commande est en cours d'exécution.";

            order.SkipRequested = true;
            return "Votre commande a été supprimée. Veuillez noter que vous ne pourrez pas vous réinscrire dans la file d'attente avant un certain temps.";
        }

        public static string ClearTrade(string userID)
        {
            if (!ulong.TryParse(userID, out var usrID))
                return $"{userID} n'est pas un u64 valide.";

            return ClearTrade(userID);
        }

        public static string GetPosition(ulong userID)
        {
            var position = QueueExtensions.GetPosition(userID, out var order);
            if (order == null)
                return "Désolé, vous n'êtes pas dans la file d'attente, ou votre commande est en cours d'exécution..";

            var message = $"Vous êtes dans la file d'attente des commandes. Position: {position}.";
            if (position > 1)
                message += $" Votre heure d'arrivée prévue est {QueueExtensions.GetETA(position)}.";

            return message;
        }

        public static string GetPresets(char prefix)
        {
            var presets = PresetLoader.GetPresets(Globals.Bot.Config.OrderConfig);

            if (presets.Length < 1)
                return "Il n'y a pas de préréglages disponibles";
            else
                return $"Les préréglages suivants sont disponibles: {string.Join(", ", presets)}. Entrez {prefix}preset [nom preset] pour en commander un!";
        }

        public static string Clean(ulong id, string username, TwitchConfig tcfg)
        {
            if (!tcfg.AllowDropViaTwitchChat)
            {
                LogUtil.LogInfo($"{username} tente de nettoyer des éléments, mais la configuration de Twitch ne permet pas actuellement les commandes de dépôt.", nameof(TwitchCrossBot));
                return string.Empty;
            }

            if (!GetDropAvailability(id, username, tcfg, out var error))
                return error;

            if (!Globals.Bot.Config.AllowClean)
                return "La fonctionnalité de nettoyage est actuellement désactivée.";
            
            Globals.Bot.CleanRequested = true;
            return "Une requête propre sera exécutée momentanément.";
        }

        public static string Drop(string message, ulong id, string username, TwitchConfig tcfg)
        {
            if (!tcfg.AllowDropViaTwitchChat)
            {
                LogUtil.LogInfo($"{username} tente de déposer des éléments, mais la configuration de Twitch ne permet pas actuellement les commandes de dépôt.", nameof(TwitchCrossBot));
                return string.Empty;
            }
            if (!GetDropAvailability(id, username, tcfg, out var error))
                return error;

            var cfg = Globals.Bot.Config;
            var items = ItemParser.GetItemsFromUserInput(message, cfg.DropConfig, cfg.DropConfig.UseLegacyDrop ? ItemDestination.PlayerDropped : ItemDestination.HeldItem);
            MultiItem.StackToMax(items);

            if (!InternalItemTool.CurrentInstance.IsSane(items, cfg.DropConfig))
                return $"Vous tentez de déposer des objets qui endommageront votre sauvegarde. Demande de dépôt non acceptée.";

            var MaxRequestCount = cfg.DropConfig.MaxDropCount;
            var ret = string.Empty;
            if (items.Count > MaxRequestCount)
            {
                ret += $"Les utilisateurs sont limités à {MaxRequestCount} articles par commande. Veuillez utiliser ce robot de manière responsable. ";
                items = items.Take(MaxRequestCount).ToArray();
            }

            var requestInfo = new ItemRequest(username, items);
            Globals.Bot.Injections.Enqueue(requestInfo);

            ret += $"Demande de dépôt d'un article{(requestInfo.Item.Count > 1 ? "s" : string.Empty)} sera exécuté momentanément.";
            return ret;
        }

        private static bool IsQueueable(string orderString, ulong id, out string msg)
        {
            if (!TwitchCrossBot.Bot.Config.AcceptingCommands || TwitchCrossBot.Bot.Config.SkipConsoleBotCreation)
            {
                msg = "Désolé, je n'accepte pas actuellement les demandes de file d'attente!";
                return false;
            }

            if (string.IsNullOrWhiteSpace(orderString))
            {
                msg = "Aucun texte de commande valide.";
                return false;
            }

            if (GlobalBan.IsBanned(id.ToString()))
            {
                msg = "Vous avez été banni pour abus. La commande n'a pas été acceptée.";
                return false;
            }

            msg = string.Empty;
            return true;
        }

        private static bool InsertToQueue(IReadOnlyCollection<Item> items, VillagerRequest? vr, string display, string username, ulong id, bool sub, bool cat, out string msg)
        {
            if (!InternalItemTool.CurrentInstance.IsSane(items, Globals.Bot.Config.DropConfig))
            {
                msg = $"@{username} - Vous tentez de commander des articles qui endommageront votre sauvegarde. Commande non acceptée.";
                return false;
            }

            var multiOrder = new MultiItem(items.ToArray(), cat, true, true);

            var tq = new TwitchQueue(multiOrder.ItemArray.Items, vr, display, id, sub);
            TwitchCrossBot.QueuePool.Add(tq);
            msg = $"@{username} - J'ai noté votre commande, maintenant chuchotez-moi un numéro aléatoire à 3 chiffres. Tapez simplement /w @{TwitchCrossBot.BotName.ToLower()} [Numéro à 3 chiffres] dans ce canal ! Votre commande ne sera pas placée dans la file d'attente tant que je n'aurai pas reçu votre chuchotement!";
            return true;
        }

        private static bool GetDropAvailability(ulong callerId, string callerName, TwitchConfig tcfg, out string error)
        {
            error = string.Empty;
            var cfg = Globals.Bot.Config;

            if (tcfg.IsSudo(callerName))
                return true;

            if (Globals.Bot.CurrentUserId == callerId)
                return true;

            if (!cfg.AllowDrop)
            {
                error = $"AllowDrop est actuellement réglé sur false dans la configuration principale.";
                return false;
            }
            else if (!cfg.DodoModeConfig.LimitedDodoRestoreOnlyMode)
            {
                error = $"Vous n'êtes autorisé à utiliser cette commande que lorsque vous êtes sur l'île pendant votre commande, et uniquement si vous avez oublié quelque chose dans votre commande.";
                return false;
            }

            return true;
        }
    }
}
