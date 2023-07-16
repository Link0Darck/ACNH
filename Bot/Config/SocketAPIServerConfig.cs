using System;

namespace SocketAPI
{
	[Serializable]
	public class SocketAPIServerConfig
	{
		/// <summary>
		/// Si le serveur API de socket doit être activé ou non.
		/// </summary>
		public bool Enabled { get; set; } = false;

		/// <summary>
		/// Si les journaux relatifs au serveur API de socket doivent être écrits dans la console.
		/// </summary>
		public bool LogsEnabled { get; set; } = true;

		/// <summary>
		/// Le port réseau sur lequel le serveur de socket écoute les connexions entrantes.
		/// </summary>
		public ushort Port { get; set; } = 5201;
	}
}