using System;

namespace SocketAPI
{
	[Serializable]
	public sealed class SocketAPIRequest
	{
		/// <summary>
		/// L'identifiant unique de la demande.
		/// </summary>
		public string? id { get; set; }

		/// <summary>
		/// Représente le nom du point de terminaison à exécuter à distance et à partir duquel récupérer le résultat.
		/// </summary>
		public string? endpoint { get; set; }

		/// <summary>
		/// La chaîne d'arguments formatée en JSON à transmettre au point de terminaison.
		/// </summary>
		public string? args { get; set; }

		public SocketAPIRequest() {}

		public override string ToString()
		{
			return $"SocketAPI.SocketAPIRequest (id: {this.id}) - endpoint: {this.endpoint}, args: {this.args}";
		}
	}
}