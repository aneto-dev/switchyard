namespace Switchyard.Ordering.Application.Ports;

public interface IOrderingUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
