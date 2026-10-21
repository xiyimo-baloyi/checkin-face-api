using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.Extensions.Logging;

namespace Africanideasfaceapi.Services.Monitoring;

public class CircuitLoggingHandler : CircuitHandler
{
    private readonly ILogger<CircuitLoggingHandler> _logger;

    public CircuitLoggingHandler(ILogger<CircuitLoggingHandler> logger)
    {
        _logger = logger;
    }

    public override Task OnConnectionUpAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Circuit {CircuitId} connected.", circuit.Id);
        return base.OnConnectionUpAsync(circuit, cancellationToken);
    }

    public override Task OnConnectionDownAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Circuit {CircuitId} disconnected.", circuit.Id);
        return base.OnConnectionDownAsync(circuit, cancellationToken);
    }

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Circuit {CircuitId} opened.", circuit.Id);
        return base.OnCircuitOpenedAsync(circuit, cancellationToken);
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Circuit {CircuitId} closed.", circuit.Id);
        return base.OnCircuitClosedAsync(circuit, cancellationToken);
    }
}
