using SysBot.ACNHOrders;
using NHSE.Core;
using System;
using TwitchLib.Client;

namespace SysBot.ACNHOrders.Twitch
{
    public class TwitchOrderRequest<T> : IACNHOrderNotifier<T> where T : Item, new()
    {
        public T[] Order { get; }
        public VillagerRequest? VillagerOrder { get; }
        public ulong UserGuid { get; }
        public ulong OrderID { get; }
        public string VillagerName { get; }
        public bool SkipRequested { get; set; } = false;
        public Action<CrossBot>? OnFinish { private get; set; }
        public string Trader { get; }
        private TwitchClient Client { get; }
        private string Channel { get; }
        private TwitchConfig Settings { get; }
        private int Password { get; }

        public TwitchOrderRequest(T[] order, ulong user, ulong orderId, string trader, string villagerName, TwitchClient client, string channel, TwitchConfig settings, int pass, VillagerRequest? vil)
        {
            UserGuid = user;
            OrderID = orderId;
            Trader = trader;
            Order = order;
            VillagerName = villagerName;
            VillagerOrder = vil;
            Client = client;
            Channel = channel;
            Settings = settings;
            Password = pass;
        }

        public void OrderCancelled(CrossBot routine, string msg, bool faulted)
        {
            OnFinish?.Invoke(routine);
            SendMessage($"@{Trader} - {msg}", Settings.OrderCanceledDestination);
        }

        public void OrderInitializing(CrossBot routine, string msg)
        {
            msg = SanitizeForTwitch(msg);
            SendMessage($"@{Trader} - Votre commande commence, veuillez vous assurer que votre inventaire est vide, puis allez parler à Orville et restez sur l'écran de saisie du code Dodo. Je vous enverrai le lien Dodo sous peu. {msg}", Settings.OrderStartDestination);
        }

        public void OrderReady(CrossBot routine, string msg, string dodo)
        {
            msg = SanitizeForTwitch(msg);
            if (Settings.OrderWaitDestination == TwitchMessageDestination.Channel)
                SendMessage($"Je t'attends @{Trader}! {msg}. Entrez le numéro sur lequel vous m'avez chuchoté. https://berichan.github.io/GetDodoCode/?hash={SimpleEncrypt.SimpleEncryptToBase64(dodo, Password).MakeWebSafe()} pour obtenir votre code dodo. Cliquez sur ce lien, pas un vieux lien ou celui de quelqu'un d'autre.", Settings.OrderWaitDestination);
            else if (Settings.OrderWaitDestination == TwitchMessageDestination.Whisper)
                SendMessage($"Je t'attends @{Trader}! {msg}. Votre code Dodo est {dodo}", Settings.OrderWaitDestination);
        }

        public void OrderFinished(CrossBot routine, string msg)
        {
            OnFinish?.Invoke(routine);
            SendMessage($"@{Trader} - Votre commande est complète, Merci pour votre commande ! {msg}", Settings.OrderFinishDestination);
        }

        public void SendNotification(CrossBot routine, string msg)
        {
            if (msg.StartsWith("Arrivée du visiteur"))
                return;
            msg = SanitizeForTwitch(msg);
            SendMessage($"@{Trader} - {msg}", Settings.NotifyDestination);
        }

        private void SendMessage(string message, TwitchMessageDestination dest)
        {
            switch (dest)
            {
                case TwitchMessageDestination.Channel:
                    Client.SendMessage(Channel, message);
                    break;
                case TwitchMessageDestination.Whisper:
                    Client.SendWhisper(Trader, message);
                    break;
            }
        }

        public static string SanitizeForTwitch(string msg)
        {
            return msg.Replace("**", string.Empty);
        }
    }
}
