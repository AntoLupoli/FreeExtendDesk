namespace FreeExtend.Protocol;

/// <summary>Dati ricevuti o da inviare non conformi al protocollo: la connessione va chiusa.</summary>
public sealed class ProtocolException(string message) : Exception(message);
