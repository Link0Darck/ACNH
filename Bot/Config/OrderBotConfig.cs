using System;
using System.Collections.Generic;
using NHSE.Core;

namespace SysBot.ACNHOrders
{
    [Serializable]
    public class OrderBotConfig
    {
        private int _maxQueueCount = 50;
        private int _timeAllowed = 180;
        private int _waitForArriverTime = 60;

        /// <summary> Nombre de personnes autorisées dans la file d'attente avant que le robot n'arrête d'accepter les demandes. N'accepte pas plus de 99 (environ 8 heures). </summary>
        public int MaxQueueCount
        {
            get => _maxQueueCount;
            set => _maxQueueCount = Math.Max(1, Math.Min(99, value));
        }

        /// <summary> Durée maximale en secondes avant qu'un utilisateur ne soit expulsé de votre île pour éviter les rôdeurs. Le minimum est de 2 minutes (120 secondes). </summary>
        public int UserTimeAllowed 
        { 
            get => _timeAllowed; 
            set => _timeAllowed = Math.Max(120, value); 
        }

        /// <summary> Durée maximale d'attente jusqu'à ce qu'ils ne se présentent plus et que le robot redémarre, en secondes. </summary>
        public int WaitForArriverTime
        {
            get => _waitForArriverTime;
            set => _waitForArriverTime = Math.Max(45, value);
        }

        /// <summary> Message à envoyer à la fin de la chaîne de fin de commande </summary>
        public string CompleteOrderMessage { get; set; } = "Passez une bonne journée !";

        /// <summary> Si certaines des entrées sont mangées en parlant à Orville, devons-nous essayer de lui parler encore une fois ? </summary>
        public bool RetryFetchDodoOnFail { get; set; } = true;

        /// <summary> Devons-nous inclure les identifiants dans les échos et les réponses aux commandes ? </summary>
        public bool ShowIDs { get; set; } = false;

        /// <summary> Le bot doit-il envoyer un ping au propriétaire du bot lorsqu'il détecte un compte alternatif ? </summary>
        public bool PingOnAbuseDetection { get; set; } = true;

        /// <summary> Définissez ce paramètre à un nombre supérieur à 0 si vous souhaitez interdire aux gens d'arriver ou de partir à l'heure. </summary>
        public int PenaltyBanCount { get; set; } = 0;

        public int PositionCommandCooldown { get; set; } = -1;

        /// <summary> Dossier de préréglages qui peuvent être commandés en utilisant $preset [nom de fichier]. </summary>
        public string NHIPresetsDirectory { get; set; } = "presets";

        /// <summary> Envoyer les messages des commandes qui commencent/arrivent dans les canaux d'écho </summary>
        public List<ulong> EchoArrivingLeavingChannels { get; set; } = new();
    }
}
