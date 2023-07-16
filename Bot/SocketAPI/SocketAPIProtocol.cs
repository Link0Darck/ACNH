using System.Text.Json;

namespace SocketAPI
{
	public sealed class SocketAPIProtocol
	{
		/// <summary>
		/// Cette méthode renvoie une instance de `SocketAPIRequest` à partir d'un message entrant au format JSON.
		/// Retourne `null` si le message d'entrée est un JSON invalide ou si `endpoint` est manquant.
		/// </summary>
		public static SocketAPIRequest? DecodeMessage(string message)
		{
			try 
			{
				SocketAPIRequest? request = JsonSerializer.Deserialize<SocketAPIRequest>(message);
				
				if (request == null) 
					return null;

				if (request!.endpoint == null)
					return null;

				return request;
			} 
			catch(System.Exception ex)
			{
				Logger.LogError($"Impossible de désérialiser la demande entrante ({message}). Erreur: {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// Compte tenu du type de message et de la chaîne d'entrée, cette méthode renvoie un message codé prêt à être envoyé à un client.
		/// Le message codé en JSON se termine par "\0\0".
		/// N'envoyez pas de messages d'une longueur supérieure à 2^16 octets (ou à la taille de tampon TCP par défaut de votre système d'exploitation) ! Les messages seraient fragmentés par le TCP.
		/// </summary>
		public static string? EncodeMessage(SocketAPIMessage message)
		{
			try
			{
				return JsonSerializer.Serialize(message) + "\0\0";
			}
			catch (System.Exception ex)
			{
				Logger.LogError($"Impossible de sérialiser le message sortant ({message}). Erreur: {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// Étant donné le message d'entrée, cette méthode récupère le type de message conformément à la spécification du protocole (1).
		/// </summary>
		private static SocketAPIMessageType GetMessageTypeFromMessage(string type)
		{
			return (SocketAPIMessageType)System.Enum.Parse(typeof(SocketAPIMessageType), type, true);
		}
	}
}