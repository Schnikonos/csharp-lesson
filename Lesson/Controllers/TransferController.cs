using Lesson.Models;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

/// <summary>
/// Lesson 07-A — Error Handling &amp; Validation basics.
///
/// Demonstrates:
///   • try/catch in a controller action → mapped to IActionResult error responses
///   • ModelState validation with Data Annotations (automatic via [ApiController])
///   • Returning standard HTTP error responses (400, 404, 500)
///
/// [ApiController] automatically returns a 400 ValidationProblemDetails when
/// ModelState is invalid — you do NOT need an explicit if (!ModelState.IsValid) check.
///
/// Java parallel:
///   try/catch → @ExceptionHandler (before a global handler existed)
///   [ApiController] validation → @Valid + MethodArgumentNotValidException auto-handled
///                                by DefaultHandlerExceptionResolver
/// </summary>
[ApiController]
[Route("transfers")]
public class TransferController : ControllerBase
{
    private static readonly List<CreateTransferRequest> _transfers = [];

    // POST /transfers — validated automatically by [ApiController] + Data Annotations
    [HttpPost]
    public IActionResult Create([FromBody] CreateTransferRequest request)
    {
        // ModelState is already checked by [ApiController] before we reach here.
        // If invalid, a 400 ValidationProblemDetails is returned automatically.

        try
        {
            if (request.FromAccount == request.ToAccount)
                return BadRequest(new { error = "Source and destination accounts must differ." });

            _transfers.Add(request);
            return CreatedAtAction(nameof(GetById), new { id = _transfers.Count - 1 }, request);
        }
        catch (Exception ex)
        {
            // Manual catch — returns a plain 500 (Lesson 07-B will replace this with a global handler)
            return StatusCode(500, new { error = "An unexpected error occurred.", detail = ex.Message });
        }
    }

    // GET /transfers/{id}
    [HttpGet("{id:int}")]
    public IActionResult GetById(int id)
    {
        if (id < 0 || id >= _transfers.Count)
            return NotFound(new { error = $"Transfer {id} not found." });

        return Ok(_transfers[id]);
    }

    // DELETE /transfers/reset — test helper
    [HttpDelete("reset")]
    public IActionResult Reset()
    {
        _transfers.Clear();
        return NoContent();
    }

    // GET /transfers/simulate-error — forces an unhandled exception to demonstrate try/catch
    [HttpGet("simulate-error")]
    public IActionResult SimulateError()
    {
        try
        {
            throw new InvalidOperationException("Simulated domain error.");
        }
        catch (InvalidOperationException ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
