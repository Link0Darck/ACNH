using System;
using System.Collections.Generic;
using System.Linq;
using SysBot.Base;

namespace SysBot.ACNHOrders
{
    [Serializable]
    public sealed record CrossBotConfig : SwitchConnectionConfig
    {
        #region Discord

        /// <summary> Lorsqu'il est activé, le robot accepte les commandes des utilisateurs via Discord.. </summary>
        public bool AcceptingCommands { get; set; } = true;

        /// <summary> Statut Discord personnalisé pour jouer un jeu. </summary>
        public string Name { get; set; } = "CrossBot";

        /// <summary> Jeton de connexion du robot. </summary>
        public string Token { get; set; } = "DISCORD_TOKEN";

        /// <summary> Préfixe de commande du robot. </summary>
        public string Prefix { get; set; } = "$";

        /// <summary> Les utilisateurs ayant ce rôle sont autorisés à interagir avec le bot. Si "@everyone", tout le monde peut interagir. </summary>
        public string RoleUseBot { get; set; } = "@everyone";

        // Numéros 64 bits sur la liste blanche de certains canaux/utilisateurs pour les autorisations
        public List<ulong> Channels { get; set; } = new();
        public List<ulong> Users { get; set; } = new();
        public List<ulong> Sudo { get; set; } = new();

        public List<ulong> LoggingChannels { get; set; } = new();

        // Devrions-nous ignorer toutes les permissions pour les commandes et autoriser les conversations entre robots ? Ceci ne devrait être utilisé que pour le débogage/les applications qui se superposent au bot acnh via discord.
        public bool IgnoreAllPermissions { get; set; } = false;

        #endregion

        #region Features

        /// <summary> Ne crée pas de bots au démarrage du programme ; utile pour tester les intégrations. </summary>
        public bool SkipConsoleBotCreation { get; set; }

        /// <summary> Lorsqu'il est activé, le bot n'autorise pas les modifications de la RAM si les métadonnées de l'élément du lecteur sont invalides. </summary>
        /// <remarks> Ne le désactivez qu'en dernier recours, si vous avez corrompu les métadonnées de votre élément par d'autres moyens. </remarks>
        public bool RequireValidInventoryMetadata { get; set; } = true;

        /// <summary> Lorsqu'elle sera activée, les joueurs seront autorisés à utiliser la commande de dépôt en mode dodorestore, ou sur l'île en mode ordre. </summary>
        public bool AllowDrop { get; set; } = true;

        public DropBotConfig DropConfig { get; set; } = new();

        public OrderBotConfig OrderConfig { get; set; } = new();

        public DodoRestoreConfig DodoModeConfig { get; set; } = new();

        /// <summary> Lorsqu'il est activé, les utilisateurs de Discord peuvent demander au robot de ramasser des objets (spamming Y un <see cref="DropBotConfig.PickupCount"/> temps). </summary>
        public bool AllowClean { get; set; }

        /// <summary> Permet de faire appel à des personnes pour utiliser la commande $lookup </summary>
        public bool AllowLookup { get; set; }

        /// <summary> Le nom de fichier à utiliser lors de la sauvegarde des ancres de position et de rotation. </summary>
        public string AnchorFilename { get; set; } = "Anchors.bin";

        /// <summary> S'il faut arrêter la boucle principale et permettre à l'utilisateur de mettre à jour ses ancrages. </summary>
        public bool ForceUpdateAnchors { get; set; } = false;

        public int MapPlaceX { get; set; } = -1;
        public int MapPlaceY { get; set; } = -1;

        /// <summary> Combien d'octets à tirer à la fois. Lower = plus lent mais moins susceptible de se planter </summary>
        public int MapPullChunkSize { get; set; } = 4096;

        /// <summary> Si les Canaux sont remplis, est-ce que tout ce qui n'est pas valide (comme les conversations générales) doit être supprimé ? Ne s'applique pas à sudo </summary>
        public bool DeleteNonCommands { get; set; } = false;

        /// <summary> Temps d'attente supplémentaire entre les pressions sur les touches du dodo talk en millisecondes </summary>
        public int DialogueButtonPressExtraDelay { get; set; } = 0;

        /// <summary> Temps d'attente supplémentaire avant le redémarrage du jeu. Peut être utile si vous devez attendre la roue de "vérification si le jeu peut être joué". </summary>
        public int RestartGameWait { get; set; } = 0;

        /// <summary> Combien de temps supplémentaire, le cas échéant, devons-nous attendre pendant qu'orville se connecte à l'Internet, en millisecondes ? </summary>
        public int ExtraTimeConnectionWait { get; set; } = 1000;

        /// <summary> Combien de temps supplémentaire, le cas échéant, devons-nous attendre après avoir tenté de franchir la porte de l'aéroport ? </summary>
        public int ExtraTimeEnterAirportWait{ get; set; } = 0;

        /// <summary> Devrions-nous vérifier le décalage de texte instantané pour voir si nous sommes toujours en dialogue, et si oui, devrions-nous continuer à appuyer sur B ? </summary>
        public bool AttemptMitigateDialogueWarping { get; set; } = false;

        /// <summary> Ne devrions-nous pas utiliser le texte instantané ? </summary>
        public bool LegacyDodoCodeRetrieval { get; set; } = false;

        /// <summary> Doit-on geler les textos instantanés ? </summary>
        public bool ExperimentalFreezeDodoCodeRetrieval { get; set; } = false;

        /// <summary> Faut-il mettre l'écran en veille quand on ne fait rien ? (nécessite sys-botbase >= 1.72) </summary>
        public bool ExperimentalSleepScreenOnIdle { get; set; } = false;

        /// <summary> Devrions-nous autoriser l'injection de villageois ? </summary>
        public bool AllowVillagerInjection { get; set; } = true;

        /// <summary> Devrions-nous effacer les noms des îles et des arrivées sur l'écran des arrivées ? </summary>
        public bool HideArrivalNames { get; set; } = false;

        /// <summary> Caractère à utiliser pour être placé sur les noms de dodo et d'arrivée, écrit dans blocker.txt et à utiliser comme source de texte dans le logiciel de streaming. </summary>
        public string BlockerEmoji { get; set; } = "\u2764";

        /// <summary> Répertoire NHL à utiliser par $loadLayer </summary>
        public string FieldLayerNHLDirectory { get; set; } = "nhl";

        /// <summary> Devrions-nous autoriser les hackers/abusers connus à utiliser le robot de commande (liste construite par la communauté) ? </summary>
        public bool AllowKnownAbusers { get; set; } = false;

        /// <summary> Doit-on appuyer une fois sur le haut avant de commencer le jeu ? Ce n'est pas garanti pour éviter la mise à jour, mais le robot fera de son mieux. </summary>
        public bool AvoidSystemUpdate { get; set; } = true;

        /// <summary> Fonctionnalité expérimentale de SignalR </summary>
        public WebConfig SignalrConfig { get; set; } = new();

        #endregion

        public bool CanUseCommandUser(ulong authorId) => Users.Count == 0 || Users.Contains(authorId);
        public bool CanUseCommandChannel(ulong channelId) => Channels.Count == 0 || Channels.Contains(channelId);
        public bool CanUseSudo(ulong userId) => Sudo.Contains(userId);

        public bool GetHasRole(string roleName, IEnumerable<string> roles)
        {
            return roleName switch
            {
                nameof(RoleUseBot) => roles.Contains(RoleUseBot),
                _ => throw new ArgumentException($"{roleName} n'est pas un type de rôle valide.", nameof(roleName)),
            };
        }
    }
}
