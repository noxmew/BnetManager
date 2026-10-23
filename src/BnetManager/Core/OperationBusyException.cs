namespace BnetManager.Core;

public sealed class OperationBusyException(string message) : InvalidOperationException(message) { }
