namespace Sim.Core;

public sealed class CommandRejectedException(string reason) : Exception(reason);
