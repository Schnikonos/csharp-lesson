using Lesson.DTOs;
using Lesson.Services;
using Microsoft.AspNetCore.Mvc;

namespace Lesson.Controllers;

// -----------------------------------------------------------------------------
// C# NOTE: [ApiController] adds several conveniences automatically:
//   1. Model validation: if the request body fails Data Annotations, ASP.NET Core
//      returns HTTP 400 with a ValidationProblemDetails payload — you do NOT need
//      to check ModelState.IsValid manually (unlike classic MVC controllers).
//   2. [FromBody] inference: complex types in action parameters are assumed to
//      come from the request body without needing an explicit [FromBody] attribute.
//   3. ProblemDetails responses for standard error codes.
//
// Java parallel:
//   @RestController  →  [ApiController] + inheriting ControllerBase
//   @RequestMapping  →  [Route] on the class
//
// ControllerBase: base class for API controllers (no view support).
// Controller:     base class that adds Razor view support — not needed for APIs.
// -----------------------------------------------------------------------------

[ApiController]
[Route("[controller]")]   // [controller] is a token replaced by "accounts" (class name minus "Controller")
public class AccountController : ControllerBase
{
    // -----------------------------------------------------------------------------
    // C# NOTE: Constructor injection is the standard DI pattern — identical to
    // Spring's @Autowired constructor injection.
    //
    // The "primary constructor" syntax (C# 12) lets you write it more concisely:
    //   public class AccountController(IAccountService accountService) : ControllerBase
    // We use the explicit form here for clarity.
    // -----------------------------------------------------------------------------
    private readonly IAccountService _accountService;

    public AccountController(IAccountService accountService)
    {
        _accountService = accountService;
    }

    // GET /accounts
    // -----------------------------------------------------------------------------
    // C# NOTE: Return type options — three common patterns:
    //
    //   1. ActionResult<T>  (used here for GET all)
    //      Allows returning either T directly or any IActionResult (Ok, NotFound…).
    //      ASP.NET Core automatically wraps T in a 200 OK response.
    //      Best for actions that always succeed.
    //
    //   2. IActionResult  (used here for GET by id)
    //      Fully flexible — you decide the status code explicitly each time.
    //      Best when the action has multiple possible HTTP outcomes.
    //
    //   3. T directly (e.g. IEnumerable<AccountResponse>)
    //      Simplest, always HTTP 200. No way to return other status codes.
    //
    // Java parallel:
    //   ResponseEntity<T>  →  ActionResult<T> / IActionResult
    //   @GetMapping        →  [HttpGet]
    //   @PostMapping       →  [HttpPost]
    // -----------------------------------------------------------------------------

    [HttpGet]
    [ProducesResponseType<IEnumerable<AccountResponse>>(StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<AccountResponse>> GetAll()
    {
        return Ok(_accountService.GetAll());
    }

    // GET /accounts/{id}
    [HttpGet("{id:guid}")]  // ":guid" is a route constraint — only matches valid GUIDs
    [ProducesResponseType<AccountResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public IActionResult GetById(Guid id)
    {
        var account = _accountService.GetById(id);

        // C# NOTE: "is null" is the preferred null check over "== null" because
        // it cannot be overridden by a custom equality operator.
        if (account is null)
            return NotFound();  // HTTP 404 — NotFound() is a helper on ControllerBase

        return Ok(account);
    }

    // POST /accounts
    [HttpPost]
    [ProducesResponseType<AccountResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<AccountResponse> Create([FromBody] CreateAccountRequest request)
    {
        var created = _accountService.Create(request);

        // CreatedAtAction returns HTTP 201 with a Location header pointing to the
        // GET /accounts/{id} endpoint.
        //
        // Java parallel:
        //   ResponseEntity.created(uri).body(dto)
        //   → CreatedAtAction(nameof(GetById), new { id = created.Id }, created)
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }
}
