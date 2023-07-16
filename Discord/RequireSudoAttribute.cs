using System;
using System.Threading.Tasks;
using Discord.Commands;
using Discord.WebSocket;

namespace SysBot.ACNHOrders
{
    public sealed class RequireSudoAttribute : PreconditionAttribute
    {
        // Remplacer la méthode CheckPermissions
        public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            var mgr = Globals.Bot.Config;
            if (mgr.CanUseSudo(context.User.Id) || context.User.Id == Globals.Self.Owner || mgr.IgnoreAllPermissions)
                return Task.FromResult(PreconditionResult.FromSuccess());

            // Vérifier si cet utilisateur est un utilisateur de la Guilde, qui est le seul contexte où les rôles existent.
            if (context.User is not SocketGuildUser gUser)
                return Task.FromResult(PreconditionResult.FromError("Vous devez être dans une guilde pour exécuter cette commande."));

            if (mgr.CanUseSudo(gUser.Id))
                return Task.FromResult(PreconditionResult.FromSuccess());

            // Puisque ce n'était pas le cas, échouez
            return Task.FromResult(PreconditionResult.FromError("Vous n'êtes pas autorisé à exécuter cette commande."));
        }
    }
}