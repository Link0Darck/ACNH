using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Discord.Commands;

namespace SysBot.ACNHOrders
{
    public class MapModule : ModuleBase<SocketCommandContext>
    {
        [Command("loadLayer")]
        [Summary("Changes the current refresher layer to a new .nhl field item layer")]
        [RequireSudo]
        public async Task SetFieldLayerAsync(string filename)
        {
            var bot = Globals.Bot;

            if (!bot.Config.DodoModeConfig.LimitedDodoRestoreOnlyMode)
            {
                await ReplyAsync($"Cette commande ne peut être utilisée qu'en mode de restauration dodo avec la carte de rafraîchissement définie sur true.").ConfigureAwait(false);
                return;
            }

            var bytes = bot.ExternalMap.GetNHL(filename);

            if (bytes == null)
            {
                await ReplyAsync($"Fichier {filename} n'existe pas ou n'a pas la bonne extension .nhl").ConfigureAwait(false);
                return;
            }

            var req = new MapOverrideRequest(Context.User.Username, bytes, filename);
            bot.MapOverrides.Enqueue(req);

            await ReplyAsync($"Couche de rafraîchissement de la carte définie sur : {Path.GetFileNameWithoutExtension(filename)}.").ConfigureAwait(false);
        }
    }
}
