using System;
using System.Linq;
using System.Threading.Tasks;
using Discord.Commands;
using NHSE.Core;
using NHSE.Villagers;

namespace SysBot.ACNHOrders
{
    // ReSharper désactivé une fois UnusedType.Global
    public class VillagerModule : ModuleBase<SocketCommandContext>
    {

        [Command("injectVillager"), Alias("iv")]
        [Summary("Injects a villager based on the internal name.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task InjectVillagerAsync(int index, string internalName) => await InjectVillagers(index, new string[1] { internalName });
        

        [Command("injectVillager"), Alias("iv")]
        [Summary("Injects a villager based on the internal name.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task InjectVillagerAsync(string internalName) => await InjectVillagerAsync(0, internalName).ConfigureAwait(false);

        [Command("multiVillager"), Alias("mvi", "injectVillagerMulti", "superUltraInjectionGiveMeMoreVillagers")]
        [Summary("Injects multiple villagers based on the internal names.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task InjectVillagerMultiAsync([Remainder]string names) => await InjectVillagers(0, names.Split(new string[2] { ",", " ", }, StringSplitOptions.RemoveEmptyEntries));

        private async Task InjectVillagers(int startIndex, string[] villagerNames)
        {
            if (!Globals.Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode)
            {
                await ReplyAsync($"{Context.User.Mention} - Les villageois ne peuvent pas être injectés en mode commande.").ConfigureAwait(false);
                return;
            }

            if (!Globals.Bot.Config.AllowVillagerInjection)
            {
                await ReplyAsync($"{Context.User.Mention} - L'injection Villager est actuellement désactivée.").ConfigureAwait(false);
                return;
            }

            var bot = Globals.Bot;
            int index = startIndex;
            int count = villagerNames.Length;

            if (count < 1)
            {
                await ReplyAsync($"{Context.User.Mention} - Aucun nom de villageois dans le commandement").ConfigureAwait(false);
                return;
            }

            foreach (var nameLookup in villagerNames)
            {
                var internalName = nameLookup;
                var nameSearched = internalName;

                if (!VillagerResources.IsVillagerDataKnown(internalName))
                    internalName = GameInfo.Strings.VillagerMap.FirstOrDefault(z => string.Equals(z.Value, internalName, StringComparison.InvariantCultureIgnoreCase)).Key;

                if (internalName == default)
                {
                    await ReplyAsync($"{Context.User.Mention} - {nameSearched} n'est pas un nom de villageois interne valide.");
                    return;
                }

                if (index > byte.MaxValue || index < 0)
                {
                    await ReplyAsync($"{Context.User.Mention} - {index} n'est pas un indice valide");
                    return;
                }

                int slot = index;

                var replace = VillagerResources.GetVillager(internalName);
                var user = Context.User;
                var mention = Context.User.Mention;

                var extraMsg = string.Empty;
                if (VillagerOrderParser.IsUnadoptable(internalName))
                    extraMsg += " Veuillez noter que vous ne pourrez pas adopter ce villageois..";

                var request = new VillagerRequest(Context.User.Username, replace, (byte)index, GameInfo.Strings.GetVillager(internalName))
                {
                    OnFinish = success =>
                    {
                        var reply = success
                            ? $"{nameSearched} a été injecté par le bot à Index {slot}. S'il vous plaît, allez leur parler !{extraMsg}"
                            : "Échec de l'injection du villageois. Veuillez dire au propriétaire du bot de regarder les logs !";
                        Task.Run(async () => await ReplyAsync($"{mention}: {reply}").ConfigureAwait(false));
                    }
                };

                bot.VillagerInjections.Enqueue(request);

                index = (index + 1) % 10;
            }

            var addMsg = count > 1 ? $"Demande d'injection de villageois pour {count} Les villageois ont" : "La demande d'injection du villageois a";
            var msg = $"{Context.User.Mention}: {addMsg} a été ajouté à la file d'attente et sera injecté dans un instant. Je vous répondrai dès que cela sera terminé.";
            await ReplyAsync(msg).ConfigureAwait(false);
        }

        [Command("villagers"), Alias("vl", "villagerList")]
        [Summary("Prints the list of villagers currently on the island.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task GetVillagerListAsync()
        {
            if (!Globals.Bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode)
            {
                await ReplyAsync($"{Context.User.Mention} - Les villageois de l'île peuvent être remplacés en les ajoutant à votre commande..");
                return;
            }

            await ReplyAsync($"Les villageois suivants sont sur {Globals.Bot.TownName}: {Globals.Bot.Villagers.LastVillagers}.").ConfigureAwait(false);
        }
        

        [Command("villagerName")]
        [Alias("vn", "nv", "name")]
        [Summary("Gets the internal name of a villager.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task GetVillagerInternalNameAsync([Summary("Language code to search with")] string language, [Summary("Villager name")][Remainder] string villagerName)
        {
            var strings = GameInfo.GetStrings(language);
            await ReplyVillagerName(strings, villagerName).ConfigureAwait(false);
        }

        [Command("villagerName")]
        [Alias("vn", "nv", "name")]
        [Summary("Gets the internal name of a villager.")]
        [RequireQueueRole(nameof(Globals.Bot.Config.RoleUseBot))]
        public async Task GetVillagerInternalNameAsync([Summary("Villager name")][Remainder] string villagerName)
        {
            var strings = GameInfo.Strings;
            await ReplyVillagerName(strings, villagerName).ConfigureAwait(false);
        }

        private async Task ReplyVillagerName(GameStrings strings, string villagerName)
        {
            if (!Globals.Bot.Config.AllowLookup)
            {
                await ReplyAsync($"{Context.User.Mention} - Code de langue pour la recherche.");
                return;
            }

            var map = strings.VillagerMap;
            var result = map.FirstOrDefault(z => string.Equals(villagerName, z.Value.Replace(" ", string.Empty), StringComparison.InvariantCultureIgnoreCase));
            if (string.IsNullOrWhiteSpace(result.Key))
            {
                await ReplyAsync($"Aucun villageois trouvé de nom {villagerName}.").ConfigureAwait(false);
                return;
            }
            await ReplyAsync($"{villagerName}={result.Key}").ConfigureAwait(false);
        }
    }
}