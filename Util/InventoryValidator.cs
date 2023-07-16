using System;
using System.Collections.Generic;
using NHSE.Core;

namespace SysBot.ACNHOrders
{
    public static class InventoryValidator
    {
        private const int pocket = Item.SIZE * 20;
        private const int size = (pocket + 0x18) * 2;
        private const int shift = -0x18 - (Item.SIZE * 20);

        public static (uint, int) GetOffsetLength(uint slot1) => ((uint)((int)slot1 + shift), size);

        public static bool ValidateItemBinary(byte[] data)
        {
            // Vérifiez le nombre de slots déverrouillés -- attendez 0,10,20
            var bagCount = BitConverter.ToUInt32(data, pocket);
            if (bagCount > 20 || bagCount % 10 != 0) // pochette 21-39 pièces
                return false;

            var pocketCount = BitConverter.ToUInt32(data, pocket + 0x18 + pocket);
            if (pocketCount != 20) // Le nombre de poches 0-19 devrait être de 20.
                return false;

            // Vérifiez la liaison de la roue de l'élément -- attendez-vous à -1 ou [0,7].
            // Interdisez les liaisons en double !
            // Ne prenez pas la peine de vérifier que bind[i] (quand ! -1) n'est pas NONE à items[i]. Nous n'avons pas besoin de tout vérifier !
            var bound = new List<byte>();
            if (!ValidateBindList(data, pocket + 4, bound))
                return false;
            if (!ValidateBindList(data, pocket + 4 + (pocket + 0x18), bound))
                return false;

            return true;
        }

        private static bool ValidateBindList(byte[] data, int bindStart, ICollection<byte> bound)
        {
            for (int i = 0; i < 20; i++)
            {
                var bind = data[bindStart + i];
                if (bind == 0xFF)
                    continue;
                if (bind > 7)
                    return false;
                if (bound.Contains(bind))
                    return false;

                bound.Add(bind);
            }

            return true;
        }
    }
}
