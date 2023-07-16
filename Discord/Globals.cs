using System;
using System.Linq;
using System.Threading.Tasks;
using Discord.Commands;
using Discord.WebSocket;

namespace SysBot.ACNHOrders
{
    public static class Globals
    {
        public static SysCord Self { get; set; } = default!;
        public static CrossBot Bot { get; set; } = default!;
        public static QueueHub Hub { get; set; } = default!;
    }

    public sealed class RequireQueueRoleAttribute : PreconditionAttribute
    {
        // Créer un champ pour stocker le nom spécifié
        private readonly string _name;

        // Créez un constructeur pour que le nom puisse être spécifié.
        public RequireQueueRoleAttribute(string name) => _name = name;

        public override Task<PreconditionResult> CheckPermissionsAsync(ICommandContext context, CommandInfo command, IServiceProvider services)
        {
            var mgr = Globals.Bot.Config;
            if (mgr.CanUseSudo(context.User.Id) || Globals.Self.Owner == context.User.Id || mgr.IgnoreAllPermissions)
                return Task.FromResult(PreconditionResult.FromSuccess());

            // Vérifier si cet utilisateur est un utilisateur de la Guilde, qui est le seul contexte où les rôles existent.
            if (context.User is not SocketGuildUser gUser)
                return Task.FromResult(PreconditionResult.FromError("Vous devez être dans une guilde pour exécuter cette commande."));

            if (!mgr.AcceptingCommands)
                return Task.FromResult(PreconditionResult.FromError("Désolé, je n'accepte pas de commandes pour le moment!"));

            bool hasRole = mgr.GetHasRole(_name, gUser.Roles.Select(z => z.Name));
            if (!hasRole)
                return Task.FromResult(PreconditionResult.FromError("Vous n'avez pas le rôle requis pour exécuter cette commande."));

            return Task.FromResult(PreconditionResult.FromSuccess());
        }
    }
}
