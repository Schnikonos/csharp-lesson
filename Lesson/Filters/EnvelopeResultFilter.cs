using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Text.Json;

namespace Lesson.Filters;

/// <summary>
/// Lesson 06-C — IResultFilter: wraps the action result.
///
/// IResultFilter has two hooks:
///   OnResultExecuting  — before the result is written to the response
///   OnResultExecuted   — after the response has been written
///
/// This implementation wraps every successful ObjectResult in a standard
/// envelope:  { data: ..., meta: { timestamp, version } }
///
/// Java parallel: ResponseBodyAdvice&lt;T&gt;.beforeBodyWrite() — wraps the
/// return value before Jackson serialises it.
/// </summary>
public class EnvelopeResultFilter : IResultFilter
{
    public void OnResultExecuting(ResultExecutingContext context)
    {
        // Only wrap successful JSON responses — leave errors untouched
        if (context.Result is ObjectResult { StatusCode: null or >= 200 and < 300 } objectResult)
        {
            context.Result = new ObjectResult(new
            {
                data = objectResult.Value,
                meta = new { timestamp = DateTime.UtcNow, version = "06-C" }
            })
            {
                StatusCode = objectResult.StatusCode
            };
        }
    }

    public void OnResultExecuted(ResultExecutedContext context)
    {
        // Nothing extra needed for this lesson; available for post-write logging.
    }
}
