using Discord;
using Discord.WebSocket;
using NHSE.Core;
using System;
using System.Linq;

namespace SysBot.ACNHOrders
{
    public class OrderRequest<T> : IACNHOrderNotifier<T> where T : Item, new()
    {
        public MultiItem ItemOrderData { get; }
        public ulong UserGuid { get; }
        public ulong OrderID { get; }
        public string VillagerName { get; }
        private SocketUser Trader { get; }
        private ISocketMessageChannel CommandSentChannel { get; }
        public Action<CrossBot>? OnFinish { private get; set; }
        public T[] Order { get; } // C'est stupide mais je ne peux plus travailler sur cette partie.
        public VillagerRequest? VillagerOrder { get; }
        public bool SkipRequested { get; set; } = false;

        public OrderRequest(MultiItem data, T[] order, ulong user, ulong orderId, SocketUser trader, ISocketMessageChannel commandSentChannel, VillagerRequest? vil)
        {
            ItemOrderData = data;
            UserGuid = user;
            OrderID = orderId;
            Trader = trader;
            CommandSentChannel = commandSentChannel;
            Order = order;
            VillagerName = trader.Username;
            VillagerOrder = vil;
        }

        public void OrderCancelled(CrossBot routine, string msg, bool faulted)
        {
            OnFinish?.Invoke(routine);
            Trader.SendMessageAsync($"Oups ! Quelque chose s'est produit avec votre commande: {msg}");
            if (!faulted)
                CommandSentChannel.SendMessageAsync($"{Trader.Mention} - Votre commande a été annulée : {msg}");
        }

        public void OrderInitializing(CrossBot routine, string msg)
        {
            Trader.SendMessageAsync($"Votre commande commence, veuillez **vous assurer que votre inventaire est __vide__**, puis allez parler à Orville et restez sur l'écran de saisie du code Dodo. Je vous enverrai le code Dodo sous peu. {msg}");
        }

        public void OrderReady(CrossBot routine, string msg, string dodo)
        {
            Trader.SendMessageAsync($"Je t'attends. {Trader.Username}! {msg}. Votre code Dodo est **{dodo}**");
        }

        public void OrderFinished(CrossBot routine, string msg)
        {
            OnFinish?.Invoke(routine);
            Trader.SendMessageAsync($"Votre commande est complète, Merci pour votre commande! {msg}");
        }

        public void SendNotification(CrossBot routine, string msg)
        {
            Trader.SendMessageAsync(msg);
        }
    }
}
