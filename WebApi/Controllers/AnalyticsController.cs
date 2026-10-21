using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ServiceLayer.Analytics;
using WebApi.Analytics;

namespace WebApi.Controllers;

[ApiController]
[Authorize(Roles = AdminRole)]
[Route("analytics")]
public sealed class AnalyticsController(ILoadService loadService) : BaseApiController
{
    [HttpGet("load")]
    public async Task<IActionResult> GetLoadAsync(
        [FromQuery] LoadRequest request,
        CancellationToken cancellationToken)
    {
        var result = await loadService.GetLoadAsync(request.ToQuery(), cancellationToken);
        return result.Match(report => Ok(LoadResponse.FromReport(report)), HandleErrors);
    }
}
