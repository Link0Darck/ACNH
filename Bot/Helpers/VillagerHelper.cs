﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NHSE.Core;
using NHSE.Villagers;
using SysBot.Base;
using ACNHMobileSpawner;
using System.Threading;

namespace SysBot.ACNHOrders
{
    public class VillagerHelper
    {
        const int villagerFlagStart = 0x1267c;
        const int villagerFlagSizeBytes = 0x200;
        public const int ExpectedHouseSize = VillagerHouse2.SIZE * 10;

        private readonly IConsoleConnectionAsync? Connection;
        private readonly CrossBot? Bot;
        private readonly string[]? OriginalVillagers;

        private readonly List<VillagerHouse2>? VillagerHouses;
        private readonly List<Villager2>? VillagerShells;

        public int LastInjectedIndex { get; set; } = 0;
        public string LastVillagers { get; private set; } = "Aucun villageois n'est encore chargé.";

        public VillagerHelper(CrossBot bot, VillagerHouse2[] houses, Villager2[] shells)
        {
            Bot = bot;
            Connection = Bot.Connection;

            VillagerHouses = new List<VillagerHouse2>(houses);
            VillagerShells = new List<Villager2>(shells);

            OriginalVillagers = new string[VillagerShells.Count];
            for (int i = 0; i < VillagerShells.Count; ++i)
                OriginalVillagers[i] = shells[i].InternalName;

            UpdateVillagerList();
        }

        public VillagerHelper() { }

        public static VillagerHelper Empty => new();

        public static async Task<VillagerHelper> GenerateHelper(CrossBot bot, CancellationToken token)
        {
            LogUtil.LogInfo("Récupération des données des villageois, veuillez patienter...", bot.Config.IP);
            // maisons
            var housesData = await bot.Connection.ReadBytesAsync((uint)OffsetHelper.VillagerHouseAddress, ExpectedHouseSize, token).ConfigureAwait(false);
            var houses = new VillagerHouse2[10];
            for (int i = 0; i < 10; ++i)
                houses[i] = new VillagerHouse2(housesData.Skip(i * VillagerHouse2.SIZE).Take(VillagerHouse2.SIZE).ToArray());

            // coquilles de villageois
            var villagers = await GetVillagerShells(bot.Connection, true, token).ConfigureAwait(false);

            LogUtil.LogInfo("Données Villager chargées...", bot.Config.IP);
            return new VillagerHelper(bot, houses, villagers);
        }

        public static async Task<Villager2[]> GetVillagerShells(IConsoleConnectionAsync connection, bool log, CancellationToken token)
        {
            // coquilles de villageois
            var villagers = new Villager2[10];
            for (int i = 0; i < 10; ++i)
            {
                var villagerBytes = await connection.ReadBytesAsync((uint)(OffsetHelper.VillagerAddress + (uint)(i * Villager2.SIZE)), 0x3, token).ConfigureAwait(false);
                villagers[i] = new Villager2(villagerBytes);

                if (log && villagers[i].Species != (byte)VillagerSpecies.non)
                    LogUtil.LogInfo($"Trouvé villageois : {GameInfo.Strings.GetVillager(villagers[i].InternalName)}", nameof(VillagerHelper));
            }
            return villagers;
        }

        public async Task<Dictionary<int, string>?> UpdateVillagers(CancellationToken token)
        {
            if (Bot == null || VillagerShells == null || OriginalVillagers == null)
                return null;

            VillagerShells.Clear();
            VillagerShells.AddRange(await GetVillagerShells(Bot.Connection, false, token).ConfigureAwait(false));
            UpdateVillagerList();

            var lostVillagers = new Dictionary<int, string>();
            for (int i = 0; i < OriginalVillagers.Length; ++i)
                if (VillagerShells[i].Species == (byte)VillagerSpecies.non && OriginalVillagers[i] != VillagerShells[i].InternalName)
                    lostVillagers.Add(i, OriginalVillagers[i]);
            return lostVillagers;
        }

        public async Task<bool> InjectVillager(VillagerRequest vr, CancellationToken token)
        {
            var vd = vr.Item;
            var index = vr.Index;

            if (VillagerHouses == null || VillagerShells == null || Connection == null || Bot == null)
                return false;

            VillagerHouse2? house = VillagerHouses.Find(x => x.NPC1 == (sbyte)index);

            if (house == null)
            {
                LogUtil.LogInfo($"Maison {index} demandé mais qui n'existe pas. Injection dans la première maison disponible", Bot.Config.IP);
                for (int i = 0; i < VillagerHouses.Count; ++i)
                {
                    if (VillagerHouses[i].HouseStatus == 0)
                    {
                        house = VillagerHouses[i];
                        break;
                    }
                }
            }

            if (house == null)
            {
                LogUtil.LogInfo($"Aucune maison trouvée à injecter.", Bot.Config.IP);
                return false;
            }

            var houseIndex = VillagerHouses.IndexOf(house);

            var villagerToInject = (byte[])vd.Villager.Clone();
            var villagerAsVillager = new Villager2(villagerToInject);

            LogUtil.LogInfo($"Début de l'injection pour : {GameInfo.Strings.GetVillager(villagerAsVillager.InternalName)}", Bot.Config.IP);

            // mise à jour déménagement hors de l'état
            villagerAsVillager.MoveType = 0;
            villagerAsVillager.MovingOut = true;
            ushort[] flags = villagerAsVillager.GetEventFlagsSave();
            flags[05] = 1; // flag 5 = Achèvement du déménagement
            flags[09] = 0; // flag 9 = Maison abandonnée
            flags[24] = 0; // flag 24 = Forcer le déplacement
            villagerAsVillager.SetEventFlagsSave(flags);
            villagerAsVillager.CatchPhrase = GameInfo.Strings.GetVillagerDefaultPhrase(villagerAsVillager.InternalName);

            var houseToInject = (byte[])vd.House.Clone();
            var houseAsHouse = new VillagerHouse2(houseToInject)
            {
                HouseStatus = house.HouseStatus == 0 ? 2 : house.HouseStatus,
                NPC1 = house.NPC1 == -1 ? (sbyte)index : house.NPC1,
                NPC2 = house.NPC2
            };
            
            // injecter un villageois
            await Connection.WriteBytesAsync(villagerAsVillager.Data, (uint)OffsetHelper.VillagerAddress + ((uint)index * Villager2.SIZE), token).ConfigureAwait(false);
            await Connection.WriteBytesAsync(villagerAsVillager.Data, (uint)OffsetHelper.VillagerAddress + ((uint)index * Villager2.SIZE) + (uint)OffsetHelper.BackupSaveDiff, token).ConfigureAwait(false);

            // maison d'injection
            await Connection.WriteBytesAsync(houseAsHouse.Data, (uint)OffsetHelper.VillagerHouseAddress + ((uint)houseIndex * VillagerHouse2.SIZE), token).ConfigureAwait(false);
            await Connection.WriteBytesAsync(houseAsHouse.Data, (uint)OffsetHelper.VillagerHouseAddress + ((uint)houseIndex * VillagerHouse2.SIZE) + (uint)OffsetHelper.BackupSaveDiff, token).ConfigureAwait(false);

            LogUtil.LogInfo($"Injection du Villager terminée.", Bot.Config.IP);
            vr.OnFinish?.Invoke(true);
            return true;
        }

        private void UpdateVillagerList()
        {
            if (VillagerShells == null)
                return;
            string newVillagers = string.Empty;
            foreach (var v in VillagerShells)
                newVillagers += $"{GameInfo.Strings.GetVillager(v.InternalName)}, ";
            LastVillagers = newVillagers.TrimEnd(' ').TrimEnd(',');
        }
    }
}
