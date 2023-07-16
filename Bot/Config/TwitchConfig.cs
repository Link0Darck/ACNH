using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SysBot.ACNHOrders
{
    [Serializable]
    public class TwitchConfig
    {
        private const string Startup = nameof(Startup);
        private const string Operation = nameof(Operation);
        private const string Messages = nameof(Messages);
        public override string ToString() => "Paramètres d'intégration de Twitch";

        // Démarrage

        ///<summary>Jeton de connexion Bot</summary>
        public string Token { get; set; } = string.Empty;

        ///<summary>Nom d'utilisateur du bot</summary>
        public string Username { get; set; } = string.Empty;

        ///<summary>Canal pour l'envoi de messages</summary>
        public string Channel { get; set; } = string.Empty;

        ///<summary>Préfixe de commande du robot</summary>
        public char CommandPrefix { get; set; } = '$';

        // Messagerie

        ///<summary>Empêchez le robot d'envoyer des messages si X messages ont été envoyés au cours des Y dernières secondes.</summary>
        public int ThrottleMessages { get; set; } = 100;

        ///<summary>Empêchez le robot d'envoyer des messages si X messages ont été envoyés au cours des Y dernières secondes.</summary>
        public double ThrottleSeconds { get; set; } = 30;

        ///<summary>Empêche le robot d'envoyer des chuchotements si X messages ont été envoyés au cours des Y dernières secondes.</summary>
        public int ThrottleWhispers { get; set; } = 100;

        ///<summary>Empêche le robot d'envoyer des chuchotements si X messages ont été envoyés au cours des Y dernières secondes.</summary>
        public double ThrottleWhispersSeconds { get; set; } = 60;

        // Opération

        ///<summary>Noms d'utilisateur Sudo</summary>
        public string SudoList { get; set; } = string.Empty;

        ///<summary>Les utilisateurs ayant ces noms d'utilisateur ne peuvent pas utiliser le robot..</summary>
        public string UserBlacklist { get; set; } = string.Empty;

        ///<summary>Lorsqu'il est activé, le bot traitera les commandes envoyées au canal.</summary>
        public bool AllowCommandsViaChannel { get; set; } = true;

        ///<summary>Lorsqu'il est activé, le robot permet aux utilisateurs d'envoyer des commandes par chuchotement (sans passer par le mode lent).</summary>
        public bool AllowCommandsViaWhisper { get; set; }

        // Destinations des messages

        ///<summary>Détermine l'endroit où les notifications génériques sont envoyées.</summary>
        public TwitchMessageDestination NotifyDestination { get; set; } = TwitchMessageDestination.Channel;

        ///<summary>Détermine l'endroit où les notifications de démarrage sont envoyées.</summary>
        public TwitchMessageDestination OrderStartDestination { get; set; } = TwitchMessageDestination.Channel;

        ///<summary>Détermine l'endroit où les notifications d'attente sont envoyées. Ne peut pas être public</summary>
        public TwitchMessageDestination OrderWaitDestination { get; set; } = TwitchMessageDestination.Channel;

        ///<summary>Détermine l'endroit où les notifications d'arrivée sont envoyées.</summary>
        public TwitchMessageDestination OrderFinishDestination { get; set; }

        ///<summary>Détermine l'endroit où les notifications d'annulation sont envoyées.</summary>
        public TwitchMessageDestination OrderCanceledDestination { get; set; } = TwitchMessageDestination.Channel;

        ///<summary>Détermine l'endroit où les notifications d'arrivée sont envoyées.</summary>
        public TwitchMessageDestination UserDefinedCommandsDestination { get; set; }

        ///<summary>Détermine l'endroit où les notifications d'annulation sont envoyées.</summary>
        public TwitchMessageDestination UserDefinedSubOnlyCommandsDestination { get; set; }

        // Commandes

        public bool AllowDropViaTwitchChat { get; set; } = false;

        /// <summary> Dictionnaire des commandes définies par l'utilisateur</summary>
        public Dictionary<string, string> UserDefinitedCommands { get; set; } = 
            new Dictionary<string, string>() { 
                { "island", "Le code dodo pour {islandname} est {dodo}. Il y a actuellement {vcount} visiteurs sur {islandname}." }, 
                { "islandlist", "Les personnes suivantes sont sur {islandname}: {visitorlist}." },
                { "villagers", "Les villageois suivants peuvent être adoptés sur {islandname}: {villagerlist}." },
                { "custom", "Bonjour, @{user}!" }
            };

        public Dictionary<string, string> UserDefinedSubOnlyCommands { get; set; } =
            new Dictionary<string, string>() {
                { "subdodo", "Le code dodo pour {islandname} est {dodo}. Il y a actuellement {vcount} visiteurs sur {islandname}." },
                { "sub", "Bonjour, @{user}! Merci d'être un abonné!" }
            };

        public bool IsSudo(string username)
        {
            var sudos = SudoList.Split(new[] { ",", ", ", " " }, StringSplitOptions.RemoveEmptyEntries);
            return sudos.Contains(username);
        }
    }

    public enum TwitchMessageDestination
    {
        Disabled,
        Channel,
        Whisper,
    }
}
