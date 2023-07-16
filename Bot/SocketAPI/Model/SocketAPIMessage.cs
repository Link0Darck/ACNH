namespace SocketAPI 
{
	/// <summary>
	/// Représente une réponse sérialisable à retourner au client.
	/// </summary>
	public class SocketAPIMessage
	{
		public SocketAPIMessage() {}

		public SocketAPIMessage(object? value, string? error)
		{
			this.value = value;
			this.error = error;
		}

		/// <summary>
		/// Décrit si la demande s'est terminée avec succès ou non.
		/// </summary>
		public string status { 
			get 
			{ 
				if (this.error != null)
					return "error";
				else
					return "okay";
			}

			private set {}
		}

		/// <summary>
		/// L'identifiant unique de la demande associée.
		/// </summary>
		public string? id { get; set; }

		/// <summary>
		/// Décrit le type de réponse, c'est-à-dire l'événement ou la réponse.
		/// Propriété enveloppante utilisée à des fins d'encodage.
		/// </summary>
		public string? _type 
		{ 
			get { return this.type?.ToString().ToLower(); }
			private set {}
		}

		/// <summary>
		/// Décrit le type de réponse, c'est-à-dire l'événement ou la réponse. 
		/// </summary>
		public SocketAPIMessageType? type;

		/// <summary>
		/// Si une erreur se produit lors du traitement de la demande du client, cette propriété contient le message d'erreur.
		/// </summary>
		public string? error { get; set; }

		/// <summary>
		/// Le corps réel de la réponse, le cas échéant.
		/// </summary>
		public object? value { get; set; }

		/// <returns>
		/// Cet objet `System.Text.Json.JsonSerializer.Serialize`'d.
		/// </returns>
		public string? Serialize()
		{
			return System.Text.Json.JsonSerializer.Serialize(this);
		}

		/// <summary>
		/// Crée une `SocketAPIResponse` remplie de l'objet de valeur fourni.
		/// </summary>
		public static SocketAPIMessage FromValue(object? value)
		{
			return new(value, null);
		}

		/// <summary>
		/// Crée un `SocketAPIResponse` rempli avec le message d'erreur fourni.
		/// </summary>
		public static SocketAPIMessage FromError(string errorMessage)
		{
			return new(null, errorMessage);
		}

		public override string ToString()
		{
			return $"SocketAPI.SocketAPIMessage (id: {this.id}) - status: {this.status}, type: {this.type}, value: {this.value}, error: {this.error}";
		}
	}
}