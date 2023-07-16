using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Discord.Net;
using NHSE.Core;
using System.Threading.Tasks;
using System.Linq;
using System;
using System.Collections.Concurrent;

namespace SysBot.ACNHOrders
{
    public static class QueueExtensions
    {
        const int ArriveTime = 90;
        const int SetupTime = 95;

        public static async Task AddToQueueAsync(this SocketCommandContext Context, OrderRequest<Item> itemReq, string player, SocketUser trader)
        {
            IUserMessage test;
            try
            {
                const string helper = "Je vous ai ajouté à la file d'attente ! Je vous enverrai un message ici lorsque votre commande sera prête.";
                test = await trader.SendMessageAsync(helper).ConfigureAwait(false);
            }
            catch (HttpException ex)
            {
                await Context.Channel.SendMessageAsync($"{ex.HttpCode}: {ex.Reason}!").ConfigureAwait(false);
                var noAccessMsg = Context.User == trader ? "Vous devez activer les messages privés pour qu'ils soient mis en file d'attente !" : $"{player} doit activer les messages privés pour qu'ils soient mis en file d'attente !";
                await Context.Channel.SendMessageAsync(noAccessMsg).ConfigureAwait(false);
                return;
            }

            // Essayez d'ajouter
            var result = AttemptAddToQueue(itemReq, trader.Mention, trader.Username, out var msg);

            // Notifier dans le canal
            await Context.Channel.SendMessageAsync(msg).ConfigureAwait(false);
            // Notifier en PM pour refléter ce qui est dit dans le canal.
            await trader.SendMessageAsync(msg).ConfigureAwait(false);

            // Nettoyage
            if (result)
            {
                // Supprimez le message d'adhésion de l'utilisateur pour des raisons de confidentialité
                if (!Context.IsPrivate)
                    await Context.Message.DeleteAsync(RequestOptions.Default).ConfigureAwait(false);
            }
            else
            {
                // Supprimez notre "Je vous ajoute !", et envoyez le même message que celui que nous avons envoyé au canal général.
                await test.DeleteAsync().ConfigureAwait(false);
            }
        }

        public static bool AddToQueueSync(IACNHOrderNotifier<Item> itemReq, string playerMention, string playerNameId, out string msg)
        {
            var result = AttemptAddToQueue(itemReq, playerMention, playerNameId, out var msge);
            msg = msge;

            return result;
        }

        // this sucks
        private static bool AttemptAddToQueue(IACNHOrderNotifier<Item> itemReq, string traderMention, string traderDispName, out string msg)
        {
            var orders = Globals.Hub.Orders;
            var orderArray = orders.ToArray();
            var order = Array.Find(orderArray, x => x.UserGuid == itemReq.UserGuid);
            if (order != null)
            {
                if (!order.SkipRequested)
                    msg = $"{traderMention} - Désolé, vous êtes déjà dans la file d'attente.";
                else
                    msg = $"{traderMention} - Vous avez été récemment retiré de la file d'attente. Veuillez attendre un peu avant d'essayer d'entrer à nouveau dans la file d'attente.";
                return false;
            }

            if(Globals.Bot.CurrentUserName == traderDispName)
            {
                msg = $"{traderMention} - Impossible de mettre votre commande en file d'attente car elle est en cours de traitement. Veuillez attendre quelques secondes pour que la file d'attente se vide si vous l'avez déjà complétée.";
                return false;
            }

            var position = orderArray.Length + 1;
            var idToken = Globals.Bot.Config.OrderConfig.ShowIDs ? $" (ID {itemReq.OrderID})" : string.Empty;
            msg = $"{traderMention} - Je vous ai ajouté à la file d'attente des commandes {idToken}. Votre position est : **{position}**";

            if (position > 1)
                msg += $". Votre heure d'arrivée prévue est {GetETA(position)}";
            else
                msg += ". Votre commande commencera après la fin de la commande en cours !";

            if (itemReq.VillagerOrder != null)
                msg += $". {GameInfo.Strings.GetVillager(itemReq.VillagerOrder.GameName)} vous attendront sur l'île. Assurez-vous de pouvoir les récupérer dans le délai de la commande.";

            Globals.Hub.Orders.Enqueue(itemReq);

            return true;
        }

        public static int GetPosition(ulong id, out OrderRequest<Item>? order)
        {
            var orders = Globals.Hub.Orders;
            var orderArray = orders.ToArray().Where(x => !x.SkipRequested).ToArray();
            var orderFound = Array.Find(orderArray, x => x.UserGuid == id);
            if (orderFound != null && !orderFound.SkipRequested)
            {
                if (orderFound is OrderRequest<Item> oreq)
                {
                    order = oreq;
                    return Array.IndexOf(orderArray, orderFound) + 1;
                }
            }

            order = null;
            return -1;
        }

        public static string GetETA(int pos)
        {
            int minSeconds = ArriveTime + SetupTime + Globals.Bot.Config.OrderConfig.UserTimeAllowed + Globals.Bot.Config.OrderConfig.WaitForArriverTime;
            int addSeconds = ArriveTime + Globals.Bot.Config.OrderConfig.UserTimeAllowed + Globals.Bot.Config.OrderConfig.WaitForArriverTime;
            var timeSpan = TimeSpan.FromSeconds(minSeconds + (addSeconds * (pos-1)));
            if (timeSpan.Hours > 0)
                return string.Format("{0:D2}h:{1:D2}m:{2:D2}s", timeSpan.Hours, timeSpan.Minutes, timeSpan.Seconds);
            else
                return string.Format("{0:D2}m:{1:D2}s", timeSpan.Minutes, timeSpan.Seconds);
        }

        private static ulong ID = 0;
        private static object IDAccessor = new();
        public static ulong GetNextID()
        {
            lock(IDAccessor)
            {
                return ID++;
            }
        }

        public static void ClearQueue<T>(this ConcurrentQueue<T> queue)
        {
            T item; // erreur d'exécution bizarre
#pragma warning disable CS8600
            while (queue.TryDequeue(out item)) { } // ne rien faire
#pragma warning restore CS8600
        }

        public static string GetQueueString()
        {
            var orders = Globals.Hub.Orders;
            var orderArray = orders.ToArray().Where(x => !x.SkipRequested).ToArray();
            string orderString = string.Empty;
            foreach (var ord in orderArray)
                orderString += $"{ord.VillagerName} \r\n";

            return orderString;
        }
    }
}
