using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using System.Text.RegularExpressions;

namespace SocketAPI {
	/// <summary>
	/// Agit comme un serveur d'API, acceptant les demandes et y répondant via TCP/IP.
	/// </summary>
	public sealed class SocketAPIServer
	{
		/// <summary>
		/// Utile pour l'arrêt en douceur de TcpListener.
		/// </summary>
		private CancellationTokenSource tcpListenerCancellationSource = new();

		/// <summary>
		/// Fournit un alias pour le jeton d'annulation.
		/// </summary>
		private CancellationToken tcpListenerCancellationToken 
		{ 
			get { return tcpListenerCancellationSource.Token; }
			set { }
		}

		/// <summary>
		/// L'écouteur TCP utilisé pour écouter les connexions entrantes.
		/// </summary>
		private TcpListener? listener;

		/// <summary>
		/// Conserve une liste de points d'extrémité appelables.
		/// </summary>
		private Dictionary<string, Delegate> apiEndpoints = new();

		/// <summary>
		/// Conserve la liste des clients connectés à qui diffuser des événements.
		/// </summary>
		private ConcurrentBag<TcpClient> clients = new();

		private SocketAPIServer() {}


		private static SocketAPIServer? _shared;

		/// <summary>
		///	L'instance singleton du `SocketAPIServer`.
		/// </summary>
		public static SocketAPIServer shared
		{
			get 
			{  
				if (_shared == null)
					_shared = new();
				return _shared;
			}
			private set { }
		}

		/// <summary>
		/// Commence à écouter les connexions entrantes sur le port configuré.
		/// </summary>
		public async Task Start(SocketAPIServerConfig config)
		{
			if (!config.Enabled)
				return;

			if (!config.LogsEnabled)
				Logger.disableLogs();

			int eps = RegisterEndpoints();
			Logger.LogInfo($"n. de points d'extrémité enregistrés : {eps}");

			listener = new(IPAddress.Any, config.Port);

			try 
			{
				listener.Start();
			}
			catch(SocketException ex)
			{
				Logger.LogError($"Le serveur Socket API n'a pas réussi à démarrer: {ex.Message}");
				return;
			}

			Logger.LogInfo($"Serveur Socket API écoutant sur le port {config.Port}.");

			tcpListenerCancellationToken.ThrowIfCancellationRequested();
			tcpListenerCancellationToken.Register(listener.Stop);

			while(!tcpListenerCancellationToken.IsCancellationRequested)
			{
				try
				{
					TcpClient client = await listener.AcceptTcpClientAsync();
					clients.Add(client);

					IPEndPoint? clientEP = client.Client.RemoteEndPoint as IPEndPoint;
					Logger.LogInfo($"Un client connecté ! IP: {clientEP?.Address}, sur le port : {clientEP?.Port}");

					HandleTcpClient(client);
				}
				catch(OperationCanceledException) when (tcpListenerCancellationToken.IsCancellationRequested)
				{
					Logger.LogInfo("Le serveur API a été fermé.", true);
					while(!clients.IsEmpty)
						clients.TryTake(out _);
				}
				catch(Exception ex)
				{
					Logger.LogError($"Une erreur s'est produite sur le serveur API de Soket.: {ex.Message}", true);
				}
			}
		}

		/// <summary>
		/// En présence d'un TcpClient connecté, ce callback gère la communication et l'arrêt en douceur.
		/// </summary>
		private async void HandleTcpClient(TcpClient client)
		{
			NetworkStream stream = client.GetStream();

			while (true)
			{
				byte[] buffer = new byte[client.ReceiveBufferSize];
				int bytesRead = await stream.ReadAsync(buffer, 0, client.ReceiveBufferSize, tcpListenerCancellationToken);

				if (bytesRead == 0)
				{
					Logger.LogInfo("Un client distant a fermé la connexion.");
					break;
				}

				string rawMessage = Encoding.UTF8.GetString(buffer);
				rawMessage = Regex.Replace(rawMessage, @"\r\n?|\n|\0", "");
				
				SocketAPIRequest? request = SocketAPIProtocol.DecodeMessage(rawMessage);

				if (request == null)
				{
					this.SendResponse(client, SocketAPIMessage.FromError("Une erreur s'est produite lors de l'analyse JSON de la requête fournie."));
					continue;
				}

				SocketAPIMessage? message = this.InvokeEndpoint(request!.endpoint!, request?.args);

				if (message == null)
					message = SocketAPIMessage.FromError("Le point de terminaison fourni n'a pas été trouvé.");

				message.id = request!.id;

				this.SendResponse(client, message);
			}
		}

		/// <summary>
		/// Envoie au client fourni le message donné de type `Response`.
		/// </summary>
		public void SendResponse(TcpClient client, SocketAPIMessage message)
		{
			message.type = SocketAPIMessageType.Response;
			this.SendMessage(client, message);
		}

		/// <summary>
		/// Envoie au client fourni le message donné de type `Event`.
		/// </summary>
		public void SendEvent(TcpClient client, SocketAPIMessage message)
		{
			message.type = SocketAPIMessageType.Event;
			this.SendMessage(client, message);
		}

		/// <summary>
		/// Étant donné un message, cette méthode l'envoie à tous les clients actuellement connectés en parallèle, encodé comme un événement.
		/// </summary>
		public async void BroadcastEvent(SocketAPIMessage message)
		{
			foreach(TcpClient client in clients)
			{
				if (client.Connected)
					await Task.Run(() => SendEvent(client, message));
			}
		}

		/// <summary>
		/// Encode un message et l'envoie à un client.
		/// </summary>
		private async void SendMessage(TcpClient toClient, SocketAPIMessage message)
		{
			byte[] wBuff = Encoding.UTF8.GetBytes(SocketAPIProtocol.EncodeMessage(message)!);
			try
			{
				await toClient.GetStream().WriteAsync(wBuff, 0, wBuff.Length, tcpListenerCancellationToken);
			}
			catch(Exception ex)
			{
				Logger.LogError($"Une erreur s'est produite lors de l'envoi d'un message à un client: {ex.Message}");
				toClient.Close();
			}
		}

		/// <summary>
		/// Arrête l'exécution du serveur.
		/// </summary>
		public void Stop()
		{
			listener?.Server.Close();
			tcpListenerCancellationSource.Cancel();
		}

		/// <summary>
		/// Enregistre un point de terminaison de l'API par son nom.
		/// </summary>
		/// <param name="name">Le nom du point de terminaison utilisé pour invoquer le gestionnaire fourni.</param>
		/// <param name="handler">Le gestionnaire responsable de la génération d'une réponse.</param>
		/// <returns></returns>
		private bool RegisterEndpoint(string name, Func<string, object?> handler)
		{
			if (apiEndpoints.ContainsKey(name))
				return false;

			apiEndpoints.Add(name, handler);

			return true;
		}

		/// <summary>
		/// Charge toutes les classes marquées comme `SocketAPIController` et les méthodes respectives marquées `SocketAPIEndpoint`.
		/// </summary>
		/// <remarks>
		/// Les méthodes marquées SocketAPIEndpoint 
		/// </remarks>
		/// <returns>Le nombre de méthodes enregistrées avec succès.</returns>
		private int RegisterEndpoints()
		{
			var endpoints = AppDomain.CurrentDomain.GetAssemblies()
								.Where(a => a.FullName?.Contains("SysBot.ACNHOrders") ?? false)
								.SelectMany(a => a.GetTypes())
								.Where(t => t.IsClass && t.GetCustomAttributes(typeof(SocketAPIController), true).Count() > 0)
								.SelectMany(c => c.GetMethods())
								.Where(m => m.GetCustomAttributes(typeof(SocketAPIEndpoint), true).Count() > 0)
								.Where(m => m.GetParameters().Count() == 1 &&
											m.IsStatic &&
											m.GetParameters()[0].ParameterType == typeof(string) &&
											m.ReturnType == typeof(object));

			foreach (var endpoint in endpoints)
				RegisterEndpoint(endpoint.Name, (Func<string, object?>)endpoint.CreateDelegate(typeof(Func<string, object?>)));

			return endpoints.Count();
		}

		/// <summary>
		/// Appelle le point de terminaison enregistré via le nom du point de terminaison, en lui fournissant des arguments codés en JSON.
		/// </summary>
		/// <param name="endpointName">Le nom du point de terminaison enregistré. Sensible à la casse !</param>
		/// <param name="jsonArgs">Les arguments à fournir au point de terminaison, encodés au format JSON.</param>
		/// <returns>Une réponse formatée en JSON. `null` si le point de terminaison n'a pas été trouvé.</returns>
		private SocketAPIMessage? InvokeEndpoint(string endpointName, string? jsonArgs)
		{
			if (!apiEndpoints.ContainsKey(endpointName))
				return SocketAPIMessage.FromError("Le point de terminaison fourni n'a pas été trouvé.");

			try
			{
				object? rawResponse = (object?)apiEndpoints[endpointName].Method.Invoke(null, new[] { jsonArgs });
				return SocketAPIMessage.FromValue(rawResponse);
			}
			catch(Exception ex)
			{
				return SocketAPIMessage.FromError(ex.InnerException?.Message ?? "Une exception générique a été levée.");
			}
		}
	}
}