﻿using Discord;
using NHSE.Core;
using System.Net;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{
    public static class NetUtil
    {
        private static readonly WebClient webClient = new WebClient();

        public static async Task<byte[]> DownloadFromUrlAsync(string url)
        {
            return await webClient.DownloadDataTaskAsync(url).ConfigureAwait(false);
        }

        public static byte[] DownloadFromUrlSync(string url)
        {
            return webClient.DownloadData(url);
        }

        public static async Task<Download<Item[]>> DownloadNHIAsync(IAttachment att)
        {
            var result = new Download<Item[]> { SanitizedFileName = Format.Sanitize(att.Filename) };
            if ((att.Size > MultiItem.MaxOrder * Item.SIZE) || att.Size < Item.SIZE)
            {
                result.ErrorMessage = $"{result.SanitizedFileName}: Taille non valide.";
                return result;
            }

            string url = att.Url;

            // Téléchargez la ressource et chargez les octets dans un tampon.
            var buffer = await DownloadFromUrlAsync(url).ConfigureAwait(false);
            var items = Item.GetArray(buffer);
            if (items == null)
            {
                result.ErrorMessage = $"{result.SanitizedFileName}: Pièce jointe nhi non valide.";
                return result;
            }

            result.Data = items;
            result.Success = true;
            return result;
        }
    }

    public sealed class Download<T> where T : class
    {
        public bool Success;
        public T? Data;
        public string? SanitizedFileName;
        public string? ErrorMessage;
    }
}