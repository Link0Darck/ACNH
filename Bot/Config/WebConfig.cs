using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SysBot.ACNHOrders
{
    [Serializable]
    public class WebConfig
    {
        /// <summary> Point de terminaison HTTP ou HTTPS </summary>
        public string URIEndpoint { get; set; } = string.Empty;

        /// <summary> L'Auth ID </summary>
        public string AuthID { get; set; } = string.Empty;

        /// <summary> Le jeton d'authentification ou le mot de passe </summary>
        public string AuthTokenOrString { get; set; } = string.Empty;
    }
}
