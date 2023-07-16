# Serveur API Socket

Le serveur n'est qu'un `TCPListener` assez simple. Il accepte des clients indéfiniment et garde une référence forte en mémoire afin de pouvoir diffuser un message à tous les clients connectés (i.e. île écrasée, arrivée et départ).

L'échange de messages est basé sur JSON - les requêtes des clients doivent être conformes à `SocketAPIRequest`, les réponses du serveur et les événements sont conformes à `SocketAPIMessage`.

## Configuration

Le serveur est livré avec son propre fichier de configuration : `server.json`. Il contient les propriétés configurables suivantes :

```javascript
{
  "Enabled": true, // WLe serveur et toutes ses fonctionnalités doivent être activés ou désactivés, `false` par défaut.
  "LogsEnabled": true, // Si les logs doivent être écrits dans la console, `true` par défaut.
  "Port": 5201 // Le port sur lequel le serveur écoutera les clients TCP, défini à 5201 par défaut.
}
```

## `SocketAPIRequest`

Requests are of the form:

```javascript
{
  "id":123, // La réponse sera renvoyée par la réponse.
  "endpoint":"endpointName", // Le nom du point de terminaison distant à exécuter.
  "args":"{\"myArg\":123}" // Objet d'arguments formaté en JSON, il sera transmis en tant que chaîne de caractères au point de terminaison dont la responsabilité sera également de le désérialiser au type d'entrée attendu.
}
```

## `SocketAPIMessage`

Et des réponses de la forme :

```javascript
{
  "id":123, // Comme dans la demande du client.
  "value":"{}", // objet ou null.
  "error":"message", // Si une erreur a été déclenchée par le point de terminaison, elle contient le message d'erreur.
  "status":"okay or error", // Contient soit "okay" soit "error".
  "_type":"event or response" // Contient soit "response" soit "event".
}
```

Il est également de la responsabilité du serveur de charger et de garder la trace des points de terminaison - ceci est fait via Reflection, **une fois** et de manière asynchrone (en supposant que la méthode `SocketAPIServer.Start()` n'est pas attendue), au démarrage. Le serveur recherche les classes marquées avec le `SocketAPIController` dans l'assemblage `SysBot.ACNHOrders` - ceci a été fait pour réduire encore plus le nombre de méthodes à explorer -, et ensuite les méthodes marquées avec le `SocketAPIEndpoint` qui :

1. Ont un seul paramètre de type `string`,
2. Sont statiques,
3. Ont un type de retour de type `object`.

Un exemple est fourni dans [`Bot/SocketAPI/ExampleEndpoint.cs`] (https://github.com/Fehniix/SysBot.ACNHOrders/blob/main/Bot/SocketAPI/EndpointExample.cs).

Une chaîne de requête client est pré-validée par `SocketAPIProtocol.DecodeMessage`. La chaîne doit être conforme à la norme JSON RFC 8259, elle doit pouvoir être désérialisée en un objet `SocketAPIRequest` et l'attribut `endpoint` ne doit pas être `null`. Les arguments inclus dans la requête sont censés être déjà formatés en JSON, c'est-à-dire :

```json
"args":"{\"myArg\":123}"
```
## Client implementations

- TypeScript, NodeJS: [Fehniix/sysbot-net-api](https://github.com/Fehniix/sysbot-net-api)