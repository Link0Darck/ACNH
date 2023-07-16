using System;

[System.AttributeUsage(System.AttributeTargets.Method)]
/// <summary>
/// Marque une méthode en tant que point de terminaison SocketAPIServer, joignable par des clients distants.
/// La méthode attribuée doit :
/// - être statique
/// - avoir un type de retour de type `object?`.
/// - avoir un seul paramètre de type `string`.
/// </summary>
public class SocketAPIEndpoint: Attribute {}