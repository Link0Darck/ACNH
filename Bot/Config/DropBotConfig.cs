using System;
using NHSE.Core;

namespace SysBot.ACNHOrders
{
    [Serializable]
    public class DropBotConfig : IConfigItem
    {
        private int _maxDropCount = 7;
        private bool _wrapItems = false;

        /// <summary> When enabled, bot picks up items when idle for <see cref="NoActivitySeconds"/> seconds. </summary>
        public bool AutoClean { get; set; }

        /// <summary> Quand <see cref="AutoClean"/> est activé, le robot ramasse les objets après un certain temps d'inactivité.. </summary>
        public int NoActivitySeconds { get; set; } = 60;

        /// <summary> Nombre de fois qu'une action de ramassage d'un article doit être effectuée. </summary>
        public int MaxDropCount
        {
            get => _maxDropCount;
            set => _maxDropCount = Math.Max(1, Math.Min(40, value));
        }

        /// <summary> Nombre de fois qu'une action de ramassage d'un article doit être effectuée. </summary>
        public int PickupCount { get; set; } = 5;

        /// <summary> Devons-nous injecter notre demande de dépôt à l'ensemble de l'inventaire ? </summary>
        public bool UseLegacyDrop { get; set; } = false;

        /// <summary>
        /// Enveloppe tous les objets générés par le bot afin que l'option "Drop" soit la première option lors de l'interaction avec l'objet dans l'inventaire du joueur.
        ///  </summary>
        /// <remarks>
        /// N'emballe pas les recettes de bricolage. Le dépôt de l'héritage est nécessaire pour emballer les articles.
        /// </remarks>
        public bool WrapAllItems // n'emballe pas les recettes de bricolage
        {
            get => UseLegacyDrop && _wrapItems;
            set => _wrapItems = value;
        }

        /// <summary> Papier d'emballage à appliquer lorsque <see cref="WrapAllItems"/> est vrai. </summary>
        public ItemWrappingPaper WrappingPaper { get; set; } = ItemWrappingPaper.Black;

        /// <summary> Devrions-nous autoriser le dépôt d'articles cassés tels que des kits de tente, des boîtes aux lettres, des objets non ramassés, etc. </summary>
        public bool SkipDropCheck { get; set; } = false;
    }
}
