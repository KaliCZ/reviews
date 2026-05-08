using Microsoft.AspNetCore.Mvc;
using Reviews.Shared;
using Temporalio.Client;

namespace Reviews.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HelloController(ITemporalClient temporal) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Post([FromBody] HelloRequest body)
    {
        var by = body.By <= 0 ? 1 : body.By;
        var handle = await temporal.StartWorkflowAsync(
            (IncrementCounterWorkflow wf) => wf.RunAsync(by),
            new(id: $"increment-{Guid.NewGuid():N}", taskQueue: IncrementCounterWorkflow.TaskQueue));

        var count = await handle.GetResultAsync();
        return Ok(new { message = "Incremented via Temporal", count });
    }
}

public record HelloRequest(int By);
