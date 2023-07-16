using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{
    [Serializable]
    public class DodoRestoreConfig
    {
        /// <summary> N'autorisera pas les commandes, mais essaiera de récupérer un nouveau code dodo si la session en ligne se bloque.</summary>
        public bool LimitedDodoRestoreOnlyMode { get; set; }

        /// <summary> Où le code dodo nouvellement récupéré sera écrit après la restauration.</summary>
        public string DodoRestoreFilename { get; set; } = "Dodo.txt";

        /// <summary> Où le nombre de visiteurs sera écrit après la restauration.</summary>
        public string VisitorFilename { get; set; } = "Visitors.txt";

        /// <summary> Où la liste des noms des visiteurs sera écrite après la restauration.</summary>
        public string VisitorListFilename { get; set; } = "VisitorsList.txt";

        /// <summary> Où les noms des villageois seraient écrits.</summary>
        public string VillagerFilename { get; set; } = "Villagers.txt";

        /// <summary> Où le nom de la couche actuellement chargée est écrit</summary>
        public string LoadedNHLFilename { get; set; } = "LoadedLayerNHL.txt";

        /// <summary> Doit-on ou non minimiser la quantité de texte écrite dans les fichiers dodo/visitor ?</summary>
        public bool MinimizeDetails { get; set; } = false;

        /// <summary> Chaînes où le dodo code sera affiché sur la restauration </summary>
        public List<ulong> EchoDodoChannels { get; set; } = new();

        /// <summary> Canaux où les nouveaux arrivants seront affichés </summary>
        public List<ulong> EchoArrivalChannels { get; set; } = new();

        /// <summary> Les canaux où seront publiées les mises à jour de l'île (comme les cycles de la LNH) </summary>
        public List<ulong> EchoIslandUpdateChannels { get; set; } = new();

        /// <summary> Lorsqu'il est réglé sur vrai, le mode de restauration régénère également la carte. </summary>
        public bool RefreshMap { get; set; } = false;

        /// <summary> Lorsqu'il est défini sur true, le mode de restauration gèle également la carte. </summary>
        public bool FreezeMap { get; set; } = false;

        /// <summary> Lorsqu'il est réglé sur true, le mode de restauration rafraîchira également le terrain et l'altitude. (nécessite que l'option rafraîchir la carte soit définie sur true) </summary>
        public bool RefreshTerrainData { get; set; } = false;

        /// <summary> Lorsqu'elle est définie sur true, les nouvelles arrivées seront affichées dans tous les canaux en mode restauration.. </summary>
        public bool PostDodoCodeWithNewArrivals { get; set; } 

        /// <summary> Lorsqu'il est défini à true, le code dodo devient un statut de bot. </summary>
        public bool SetStatusAsDodoCode { get; set; }

        /// <summary> Si la valeur est false, senddodo ne fonctionnera pas (pour personne). </summary>
        public bool AllowSendDodo { get; set; } = true;

        /// <summary> Lorsqu'il a la valeur true, le bot réinjecte les villageois perdus tout en utilisant sa propre base de données de villageois. </summary>
        public bool ReinjectMovedOutVillagers { get; set; }

        /// <summary> Changez le pourcentage de la taille de la police du code dodo si vous utilisez le moteur de rendu du code dodo fantaisie. </summary>
        public float DodoFontPercentageSize { get; set; } = 100;

        /// <summary> Lorsqu'elle est définie à true, le bot va mash B lorsqu'il ne fait aucune des autres fonctionnalités de restauration. </summary>
        public bool MashB { get; set; }

        /// <summary> Si la valeur est supérieure à -1, le robot ira automatiquement chercher un nouveau dodo toutes les x minutes si l'île est vide. </summary>
        public int AutoNewDodoTimeMinutes { get; set; } = -1;

        /// <summary> Comment le robot devrait-il réagir si quelqu'un réussit à obtenir le code dodo ? </summary>
        public string SuccessfulDodoCodeSendReaction { get; set; } = "";

        /// <summary> Devrions-nous faire défiler les LNH dans l'annuaire de la LNH ? </summary>
        public bool CycleNHLs { get; set; } = false;

        /// <summary> Si l'option ci-dessus est définie comme vraie, à quelle fréquence devons-nous les parcourir ? </summary>
        public int CycleNHLMinutes { get; set; } = 1440;
    }
}
