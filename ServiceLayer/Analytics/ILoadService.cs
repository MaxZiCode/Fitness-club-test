using ErrorOr;

namespace ServiceLayer.Analytics;

public interface ILoadService
{
    Task<ErrorOr<LoadReport>> GetLoadAsync(LoadQuery query, CancellationToken cancellationToken = default);
}
