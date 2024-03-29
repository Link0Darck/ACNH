﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;

namespace SysBot.ACNHOrders
{
    // ReSharper désactivé une fois UnusedType.Global
    public class HelpModule : ModuleBase<SocketCommandContext>
    {
        private readonly CommandService _service;

        public HelpModule(CommandService service)
        {
            _service = service;
        }

        [Command("help")]
        [Summary("Lists available commands.")]
        [RequireSudo]
        public async Task HelpAsync()
        {
            var builder = new EmbedBuilder
            {
                Color = new Color(114, 137, 218),
                Description = "Voici les commandes que vous pouvez utiliser :"
            };

            var app = await Context.Client.GetApplicationInfoAsync().ConfigureAwait(false);
            var owner = app.Owner.Id;
            var uid = Context.User.Id;

            foreach (var module in _service.Modules)
            {
                string? description = null;
                HashSet<string> mentioned = new();
                foreach (var cmd in module.Commands)
                {
                    var name = cmd.Name;
                    if (mentioned.Contains(name))
                        continue;
                    if (cmd.Attributes.Any(z => z is RequireOwnerAttribute) && owner != uid)
                        continue;
                    if (cmd.Attributes.Any(z => z is RequireSudoAttribute) && !Globals.Bot.Config.CanUseSudo(uid))
                        continue;

                    mentioned.Add(name);
                    var result = await cmd.CheckPreconditionsAsync(Context).ConfigureAwait(false);
                    if (result.IsSuccess)
                        description += $"{cmd.Aliases[0]}\n";
                }
                if (string.IsNullOrWhiteSpace(description))
                    continue;

                builder.AddField(x =>
                {
                    x.Name = module.Name;
                    x.Value = description;
                    x.IsInline = false;
                });
            }

            await ReplyAsync("L'aide est arrivée !", false, builder.Build()).ConfigureAwait(false);
        }

        [Command("help")]
        [Summary("Lists information about a specific command.")]
        public async Task HelpAsync([Summary("The command you want help for")] string command)
        {
            var result = _service.Search(Context, command);

            if (!result.IsSuccess)
            {
                await ReplyAsync($"Désolé, je n'ai pas pu trouver une commande comme **{command}**.").ConfigureAwait(false);
                return;
            }

            var builder = new EmbedBuilder
            {
                Color = new Color(114, 137, 218),
                Description = $"Voici quelques commandes comme **{command}**:"
            };

            foreach (var match in result.Commands)
            {
                var cmd = match.Command;

                builder.AddField(x =>
                {
                    x.Name = string.Join(", ", cmd.Aliases);
                    x.Value = GetCommandSummary(cmd);
                    x.IsInline = false;
                });
            }

            await ReplyAsync("L'aide est arrivée !", false, builder.Build()).ConfigureAwait(false);
        }

        private static string GetCommandSummary(CommandInfo cmd)
        {
            return $"Résumé : {cmd.Summary}\nParamètres: {GetParameterSummary(cmd.Parameters)}";
        }

        private static string GetParameterSummary(IReadOnlyList<ParameterInfo> p)
        {
            if (p.Count == 0)
                return "Aucun";
            return $"{p.Count}\n- " + string.Join("\n- ", p.Select(GetParameterSummary));
        }

        private static string GetParameterSummary(ParameterInfo z)
        {
            var result = z.Name;
            if (!string.IsNullOrWhiteSpace(z.Summary))
                result += $" ({z.Summary})";
            return result;
        }
    }
}
