using Discord.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{
    public class BanModule : ModuleBase<SocketCommandContext>
    {
        [Command("unBan")]
        [Summary("unbans a user by their long number id.")]
        [RequireSudo]
        public async Task UnBanAsync(string id)
        {
            if (GlobalBan.IsBanned(id))
            {
                GlobalBan.UnBan(id);
                await ReplyAsync($"{id} a été débanné des abus.").ConfigureAwait(false);
            }
            else
            {
                await ReplyAsync($"{id} n'a pas pu être trouvé dans la liste des interdictions.").ConfigureAwait(false);
            }
        }

        [Command("ban")]
        [Summary("bans a user by their long number id.")]
        [RequireSudo]
        public async Task BanAsync(string id)
        {
            if (GlobalBan.IsBanned(id))
            {
                await ReplyAsync($"{id} est déjà interdit d'abus").ConfigureAwait(false);
            }
            else
            {
                GlobalBan.Ban(id);
                await ReplyAsync($"{id} a été banni pour abus.").ConfigureAwait(false);
            }
        }

        [Command("checkBan")]
        [Summary("checks a user's ban state by their long number id.")]
        [RequireSudo]
        public async Task CheckBanAsync(string id) => await ReplyAsync(GlobalBan.IsBanned(id) ? $"{id} est banni pour abus" : $"{id} n'est pas banni pour abus").ConfigureAwait(false);
        
    }
}
