using System;

[AttributeUsage(AttributeTargets.Class)]
/// <summary>
/// Marque la classe ou la structure comme étant un `SocketAPIController`, ou sinon simplement un conteneur approprié de `SocketAPIEndpoint`s.  
/// </summary>
public class SocketAPIController: Attribute {}