namespace Game.Application.Common;

public sealed class MatchConcurrencyException(string message, Exception innerException)
    : Exception(message, innerException);
