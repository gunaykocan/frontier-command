namespace Game.Domain.Rules;

public sealed class DomainRuleException(string message) : InvalidOperationException(message);
