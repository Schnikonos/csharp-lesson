# Lesson 06-A — Middleware Basics

> **Branch:** `lesson/06-middleware/a-basic`
> **Prerequisites:** Lesson 05-C (Advanced LINQ)

---

## What you will learn

| Topic | C# ASP.NET Core | Java parallel |
|---|---|---|
| `IMiddleware` | preferred DI-managed middleware contract | `OncePerRequestFilter` |
| `RequestDelegate next` | call the next component in the pipeline | `filterChain.doFilter()` |
| Middleware ordering | registration order in `Program.cs` controls execution | `FilterRegistrationBean.setOrder()` |
| Request/response logging | log before/after `next(context)` | `CommonsRequestLoggingFilter` |
| Response header injection | add header before `next()` | `OncePerRequestFilter` — `response.setHeader()` |

---

## 1. The Middleware Pipeline

ASP.NET Core processes every HTTP request through a **pipeline** of middleware components.
Each component can:
- Run code **before** the next component (inbound)
- Call `await next(context)` to pass control forward
- Run code **after** the next component returns (outbound)

```
Request ?  [ResponseHeaderMiddleware] ? [RequestLoggingMiddleware] ? [Router] ? Controller
Response ?                           ?                            ?          ?
```

Registration order in `Program.cs` determines pipeline order.  
Middleware registered **first** wraps everything registered after it.

---

## 2. IMiddleware vs Convention-Based Middleware

| Approach | Lifetime | DI injection |
|---|---|---|
| `IMiddleware` | managed by DI container | full constructor injection ? |
| Convention-based (`Invoke(HttpContext)`) | instantiated once at startup | only singleton-safe services in constructor |

`IMiddleware` is the modern, recommended approach because it integrates cleanly with the DI container.

---

## 3. RequestLoggingMiddleware

```csharp
public class RequestLoggingMiddleware(ILogger<RequestLoggingMiddleware> logger) : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var sw = Stopwatch.StartNew();
        logger.LogInformation("? {Method} {Path}", context.Request.Method, context.Request.Path);

        await next(context);   // pass to next middleware / endpoint

        sw.Stop();
        logger.LogInformation("? {Method} {Path} {StatusCode} ({Elapsed}ms)",
            context.Request.Method, context.Request.Path,
            context.Response.StatusCode, sw.ElapsedMilliseconds);
    }
}
```

**Java parallel:** `OncePerRequestFilter.doFilterInternal()` — call `filterChain.doFilter()`,
then log after it returns.

---

## 4. ResponseHeaderMiddleware

```csharp
public class ResponseHeaderMiddleware : IMiddleware
{
    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        context.Response.Headers["X-Powered-By"] = "ASP.NET Core 10 Lesson 06";
        await next(context);
        // response body has already started streaming — do not write body here
    }
}
```

Headers **must** be set before `next()` is called (or before the response body starts writing).

---

## 5. Registration in Program.cs

```csharp
// Register as transient so DI manages the lifetime
builder.Services.AddTransient<RequestLoggingMiddleware>();
builder.Services.AddTransient<ResponseHeaderMiddleware>();

// Add to pipeline — ORDER MATTERS
app.UseMiddleware<ResponseHeaderMiddleware>();   // outermost wrapper
app.UseMiddleware<RequestLoggingMiddleware>();   // logs every request that reaches it
```

**Java parallel:**
```java
@Bean
public FilterRegistrationBean<RequestLoggingFilter> loggingFilter() {
    var reg = new FilterRegistrationBean<>(new RequestLoggingFilter());
    reg.setOrder(1);
    return reg;
}
```

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/middleware/ping` | Returns `{ message: "pong" }` — used to verify header injection |
| `GET` | `/middleware/slow` | 10 ms delay — verifies elapsed-time logging |

---

## Project Structure (new / changed files)

```
Lesson/
  Middleware/
    RequestLoggingMiddleware.cs  NEW  IMiddleware — logs method, path, status, elapsed
    ResponseHeaderMiddleware.cs  NEW  IMiddleware — injects X-Powered-By header
  Controllers/
    MiddlewareDemoController.cs  NEW  /middleware/ping + /middleware/slow
  Program.cs                          + middleware DI registrations + UseMiddleware calls
Lesson.Tests/
  MiddlewareBasicTests.cs        NEW  7 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~MiddlewareBasicTests"
# 7 tests — all pass
```

| Test | What it verifies |
|---|---|
| `Ping_ResponseContainsXPoweredByHeader` | Header is present |
| `Ping_XPoweredByHeader_ContainsExpectedValue` | Header value contains "ASP.NET Core" |
| `AnyEndpoint_ResponseContainsXPoweredByHeader` | Header applies to all routes |
| `Ping_Returns200_MiddlewareDoesNotBreakPipeline` | Middleware is transparent |
| `Ping_ResponseBody_IsCorrect` | Body is unchanged by middleware |
| `Slow_Returns200_AfterDelay` | Middleware works with delayed responses |
| `UnknownRoute_Returns404_MiddlewareStillAddsHeader` | Header added even on 404 responses |

---

## Exercises

1. Add a `CorrelationIdMiddleware` that reads `X-Correlation-Id` from the request (or generates a new `Guid` if absent) and echoes it back in the response headers.
2. Change `ResponseHeaderMiddleware` to add a `Cache-Control: no-store` header and verify with a test.
3. Add a middleware that short-circuits the pipeline for requests to `/health` and returns `200 OK` directly — bypassing the router and all downstream middleware.
4. Register `RequestLoggingMiddleware` **before** `ResponseHeaderMiddleware` and observe how the log output changes (the status code logged will still be correct because both happen after `next()`).


> **Branch:** `lesson/05-linq/c-advanced`
> **Prerequisites:** Lesson 05-B (IEnumerable vs IQueryable, GroupBy, Join, SelectMany, let)

---

## What you will learn

| Topic | C# | Java parallel |
|---|---|---|
| Custom LINQ extensions | `this IEnumerable<T>` extension methods | static utility methods (no dot-notation) |
| `Aggregate` | general-purpose fold / reduce | `stream().reduce(identity, accumulator)` |
| `Zip` | pair two sequences by index | `IntStream.range + get(i)` |
| `Chunk` | split into fixed-size pages | Guava `Lists.partition` |
| `AsParallel` (PLINQ) | CPU-bound parallelism over thread pool | `stream().parallel()` |
| Expression trees | `Expression<Func<T,bool>>` — build predicates at runtime | Reflection + `Predicate<T>` |
| `IAsyncEnumerable<T>` | async streaming with `await foreach` | Project Reactor `Flux<T>` |

---

## 1. Custom LINQ Extension Methods

Extending `IEnumerable<T>` with a static class makes reusable pipeline steps feel native:

```csharp
public static class ProductExtensions
{
    public static IEnumerable<Product> InStock(
        this IEnumerable<Product> source, int minStock = 1)
        => source.Where(p => p.Stock >= minStock);

    public static IEnumerable<Product> PriceAbove(
        this IEnumerable<Product> source, decimal min)
        => source.Where(p => p.Price >= min);
}

// Usage — reads like built-in LINQ
var result = products.InStock().PriceAbove(50m).MostExpensive(3).ToList();
```

**Java parallel:** static helper methods work but break the fluent chain:
`ProductUtils.mostExpensive(ProductUtils.priceAbove(products, 50), 3)`

---

## 2. Aggregate — General-Purpose Fold

`Aggregate` is the universal accumulator operator (like `reduce` in functional programming):

```csharp
// Sum all inventory values
decimal total = products.Aggregate(0m, (acc, p) => acc + p.Price * p.Stock);

// Build a comma-separated string
string catalogue = products
    .OrderBy(p => p.Name)
    .Aggregate(string.Empty, (acc, p) => acc.Length == 0 ? p.Name : acc + ", " + p.Name);
```

For common aggregates (`Sum`, `Average`, `Max`, `Min`, `Count`) prefer the specialised
operators — they are more readable and EF Core can translate them to SQL.

**Java:** `stream().reduce(BigDecimal.ZERO, (acc, p) -> acc.add(p.getPrice()), BigDecimal::add)`

---

## 3. Zip — Pair Two Sequences by Index

```csharp
var sorted = products.OrderByDescending(p => p.Price);
var ranks  = Enumerable.Range(1, products.Count);

var ranked = sorted
    .Zip(ranks, (p, rank) => new RankedProduct(rank, p.Name, p.Price))
    .ToList();
// ? [ { Rank=1, Name="Laptop Pro", Price=1299 }, … ]
```

`Zip` stops at the shorter sequence. Three-sequence overloads exist:
`a.Zip(b, c)` returns value tuples `(a[i], b[i], c[i])`.

**Java:** `IntStream.range(0, Math.min(a.size(), b.size())).mapToObj(i -> new Pair(a.get(i), b.get(i)))`

---

## 4. Chunk — Split into Fixed-Size Pages

Introduced in .NET 6:

```csharp
// Splits 10 products into pages of 3: [[p1,p2,p3],[p4,p5,p6],[p7,p8,p9],[p10]]
Product[][] pages = products.Chunk(3).ToArray();
```

`Chunk` is ideal for batch-processing large sequences without loading everything at once.

**Java:** `Guava: Lists.partition(list, 3)` or a custom `IntStream` splitter.

---

## 5. AsParallel — PLINQ Basics

```csharp
var expensive = products
    .AsParallel()                            // distribute work across ThreadPool
    .Where(p => p.Price > minPrice)          // runs on multiple threads
    .OrderBy(p => p.Name)                    // re-serialise before output
    .ToList();
```

Guidelines:
- Use for **CPU-bound** work on large collections (> ~1 000 items as a rough threshold).
- For **I/O-bound** work, use `async/await` — PLINQ blocks threads.
- Results are non-deterministic unless you add `AsOrdered()` or a final `OrderBy`.

**Java:** `stream().parallel()` — same concept and same caveats.

---

## 6. Expression Trees — Intro

An `Expression<Func<T, bool>>` stores a LINQ query as a **data structure** (AST) rather
than a compiled delegate. EF Core reads this tree to generate SQL.

```csharp
// Build: p => p.Price < maxPrice
var param    = Expression.Parameter(typeof(Product), "p");
var property = Expression.Property(param, nameof(Product.Price));
var constant = Expression.Constant(maxPrice, typeof(decimal));
var body     = Expression.LessThan(property, constant);
var lambda   = Expression.Lambda<Func<Product, bool>>(body, param);

// Compile and use as a normal delegate
var predicate = lambda.Compile();
var result = products.Where(predicate).ToList();
```

This pattern powers dynamic query builders, AutoMapper projections, and EF Core itself.

**Java parallel:** no direct equivalent; closest is reflection-based predicate construction.

---

## 7. IAsyncEnumerable\<T\> — Async Streaming

`IAsyncEnumerable<T>` lets you produce and consume items **one at a time** asynchronously,
without buffering the entire result:

```csharp
// Producer — async iterator method
public async IAsyncEnumerable<Product> StreamProductsAsync(decimal maxPrice,
    [EnumeratorCancellation] CancellationToken ct = default)
{
    foreach (var p in products.Where(p => p.Price <= maxPrice))
    {
        await Task.Delay(0, ct); // simulate async source (DB cursor, HTTP stream, …)
        yield return p;
    }
}

// Consumer
await foreach (var p in service.StreamProductsAsync(100m))
    Console.WriteLine(p.Name);
```

Use cases: database cursors with EF Core (`IAsyncEnumerable<T>` from `ToAsyncEnumerable`),
file streaming, server-sent events, gRPC streaming.

**Java parallel:** Project Reactor `Flux<T>` or Java 9 `Flow.Publisher<T>`.

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/linq/advanced/top-in-stock?minPrice=50&topN=3` | Custom extensions chained |
| `GET` | `/linq/advanced/inventory-value` | `Aggregate` — sum of Price × Stock |
| `GET` | `/linq/advanced/catalogue` | `Aggregate` — comma-separated names |
| `GET` | `/linq/advanced/ranked` | `Zip` — products with price rank |
| `GET` | `/linq/advanced/chunks?pageSize=3` | `Chunk` — pages of products |
| `GET` | `/linq/advanced/parallel?minPrice=50` | `AsParallel` filter |
| `GET` | `/linq/advanced/expression-tree?maxPrice=100` | Runtime-built predicate |
| `GET` | `/linq/advanced/stream?maxPrice=100` | `IAsyncEnumerable<T>` streaming |

---

## Project Structure (new / changed files)

```
Lesson/
  Extensions/
    ProductExtensions.cs       NEW  InStock, Cheapest, MostExpensive, PriceAbove
  Models/
    RankedProduct.cs           NEW  result record for Zip demo
  Services/
    LinqAdvancedService.cs     NEW  Aggregate, Zip, Chunk, PLINQ, ExprTree, IAsyncEnumerable
  Controllers/
    LinqAdvancedController.cs  NEW  /linq/advanced/* endpoints
  Program.cs                        + LinqAdvancedService registered as singleton
Lesson.Tests/
  LinqAdvancedTests.cs         NEW  13 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~LinqAdvancedTests"
# 13 tests — all pass
```

| Test | What it verifies |
|---|---|
| `GetTopInStock_ReturnsAtMostTopN` | Custom extension `MostExpensive` honours `topN` |
| `GetTopInStock_AllProductsExceedMinPrice` | Custom extension `PriceAbove` filters correctly |
| `GetInventoryValue_MatchesManualCalculation` | `Aggregate` sum equals manual calculation |
| `GetCatalogue_ContainsAllProductNames` | `Aggregate` string fold contains every name |
| `GetRanked_CountMatchesProductCount` | `Zip` produces one entry per product |
| `GetRanked_Rank1HasHighestPrice` | Rank 1 corresponds to the most expensive product |
| `GetChunks_PageSize3_ProducesCorrectNumberOfChunks` | `Chunk` creates ceiling(10/3) = 4 pages |
| `GetChunks_TotalItemsEqualsProductCount` | All items preserved across chunks |
| `GetParallel_SameIdsAsSequentialFilter` | `AsParallel` returns same set as sequential |
| `GetByExpressionTree_AllProductsBelowMaxPrice` | Expression-tree predicate filters correctly |
| `GetByExpressionTree_SameResultAsDirectFilter` | Tree result matches hard-coded LINQ filter |
| `StreamProducts_AllNamesAreBelowMaxPrice` | `IAsyncEnumerable` respects max-price filter |
| `StreamProducts_CountMatchesExpected` | Streamed item count matches direct count |

---

## Exercises

1. Add a `SumBy<T>` generic extension method on `IEnumerable<T>` that takes a `Func<T, decimal>` selector — a miniature reimplementation of `Sum`.
2. Use `Aggregate` with a seed of `new Dictionary<string, decimal>()` to build a category ? total-price map in a single pass.
3. Add `AsOrdered()` to the PLINQ pipeline and verify the test still passes — then remove it and observe whether order is preserved across runs.
4. Modify `StreamProductsAsync` to introduce a real `await Task.Delay(1)` and test the endpoint with a short cancellation token to observe `OperationCanceledException` propagation.
5. Build a more complex expression tree: `p => p.Price < maxPrice && p.Category == category` using `Expression.AndAlso`.


> **Branch:** `lesson/05-linq/b-intermediate`
> **Prerequisites:** Lesson 05-A (Where, Select, OrderBy, FirstOrDefault, deferred execution)

---

## What you will learn

| Topic | C# LINQ | Java parallel |
|---|---|---|
| `IEnumerable<T>` vs `IQueryable<T>` | in-memory iteration vs lazy expression tree | `List` stream vs `JpaSpecificationExecutor` |
| `GroupBy` | aggregate by key | `Collectors.groupingBy` |
| `Join` | inner equi-join | `stream().flatMap` + Map lookup |
| `SelectMany` | flatten nested sequences | `stream().flatMap` |
| `let` (query syntax) | introduce an intermediate value | multi-step `.map()` chain |
| Anonymous types | `new { p.Name, p.Price }` | no direct equivalent (use records/tuples) |
| Pipeline chaining | compose operators before materialising | chained stream intermediate ops |

---

## 1. IEnumerable\<T\> vs IQueryable\<T\>

`IEnumerable<T>` iterates in memory; `IQueryable<T>` builds an expression tree that a
provider (EF Core, LINQ to SQL, …) translates to SQL before touching the database.

```csharp
// IEnumerable path — ALL rows loaded first, then filtered in C#
IEnumerable<Product> all = Products.ToList();           // materialise
var result = all.Where(p => p.Category == cat).ToList();

// IQueryable-equivalent — filter is composed before materialisation
IEnumerable<Product> lazy = Products;                   // no iteration yet
lazy = lazy.Where(p => p.Category == cat);              // deferred
var result = lazy.ToList();                             // single pass, only matching elements
```

With a real `DbSet<T>` the lazy path generates `WHERE Category = @cat` in SQL;
the in-memory path loads every row and discards non-matching ones in C#.

**Rule:** keep filters on `IQueryable` until you need the data.

---

## 2. GroupBy

```csharp
var stats = products
    .GroupBy(p => p.Category)
    .Select(g => new CategorySummary(
        g.Key,
        g.Count(),
        g.Sum(p => p.Price),
        g.Average(p => (double)p.Price)))
    .OrderBy(s => s.Category)
    .ToList();
```

**Java:** `stream().collect(Collectors.groupingBy(Product::getCategory, Collectors.counting()))`

---

## 3. Join — Equi-Join Two Sequences

```csharp
var lines = orders.Join(
    products,
    o => o.ProductId,           // outer key
    p => p.Id,                  // inner key
    (o, p) => new OrderLine(
        o.Id, o.CustomerId, p.Name, p.Category,
        p.Price, o.Quantity, p.Price * o.Quantity))
    .ToList();
```

**Java:** `orders.stream().flatMap(o -> products.stream().filter(p -> p.getId() == o.getProductId()).map(p -> new OrderLine(...)))`
(prefer a `Map<Id, Product>` lookup for O(1) performance)

---

## 4. SelectMany — Flattening

```csharp
// Each group contributes multiple strings; SelectMany flattens them into one sequence
var labels = products
    .GroupBy(p => p.Category)
    .OrderBy(g => g.Key)
    .SelectMany(g => g.Select(p => $"[{g.Key}] {p.Name}"))
    .ToList();
```

**Java:** `categories.stream().flatMap(g -> g.getProducts().stream().map(p -> "[" + g.getKey() + "] " + p.getName()))`

---

## 5. let Clause (Query Syntax)

`let` introduces a named intermediate value inside a query-syntax expression,
avoiding recomputing the same expression in `where` and `select`:

```csharp
var discounted =
    (from p in products
     let d = p.Price * (1 - discountRate)   // compute once
     where d < maxDiscountedPrice            // reuse in filter
     orderby d
     select new DiscountedProduct(p.Name, p.Price, d))  // reuse in projection
    .ToList();
```

**Java:** no `let` keyword; use a `.map()` step that projects to a temporary holder record.

---

## 6. Anonymous Types

```csharp
var projection = products.Select(p => new { p.Name, p.Price }).ToList();
// Type is compiler-generated; only usable within the same method.
```

For cross-method use, prefer named records or tuples.
In EF Core queries, anonymous types in `Select` translate to a `SELECT Name, Price` SQL projection.

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/linq/filter-in-memory?category=` | IEnumerable path (materialise then filter) |
| `GET` | `/linq/filter-lazy?category=` | IQueryable-equivalent (filter then materialise) |
| `GET` | `/linq/categories/summary` | `GroupBy` ? category stats |
| `GET` | `/linq/orders/lines` | `Join` products and orders |
| `GET` | `/linq/products/labels` | `SelectMany` flattened labels |
| `GET` | `/linq/products/discounted?discountRate=0.10&maxDiscountedPrice=100` | `let` clause demo |
| `GET` | `/linq/orders/top?topN=3` | Chained pipeline — top N by line total |

---

## Project Structure (new / changed files)

```
Lesson/
  Models/
    Order.cs                 NEW  seed order records for Join demo
    LinqIntermediateDtos.cs  NEW  CategorySummary, OrderLine, DiscountedProduct
  Services/
    LinqIntermediateService.cs  NEW  GroupBy, Join, SelectMany, let, chaining
  Controllers/
    LinqIntermediateController.cs  NEW  /linq/filter-*, /linq/categories/summary,
                                        /linq/orders/lines, /linq/products/labels,
                                        /linq/products/discounted, /linq/orders/top
  Program.cs                      + LinqIntermediateService registered as singleton
Lesson.Tests/
  LinqIntermediateTests.cs  NEW  13 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~LinqIntermediateTests"
# 13 tests — all pass
```

| Test | What it verifies |
|---|---|
| `FilterInMemory_SameResultAsFilterLazy_ForSameCategory` | Both paths return identical product IDs |
| `FilterLazy_ReturnsOnlyMatchingCategory` | Lazy filter returns only the requested category |
| `GetCategorySummaries_ReturnsAllCategories` | GroupBy covers every distinct category |
| `GetCategorySummaries_CountsAreCorrect` | Per-category counts match seed data |
| `GetCategorySummaries_TotalValueMatchesSeedData` | Sum of all totals equals sum of all prices |
| `GetOrderLines_CountMatchesSeedOrders` | Join produces one row per order |
| `GetOrderLines_LineTotalIsUnitPriceTimesQuantity` | LineTotal = UnitPrice × Quantity |
| `GetProductLabels_CountMatchesProductCount` | SelectMany flattens to exactly 10 labels |
| `GetProductLabels_EachLabelContainsCategoryAndName` | Format `[Category] Name` respected |
| `GetDiscounted_AllDiscountedPricesAreBelowMaxPrice` | let filter applied correctly |
| `GetDiscounted_DiscountedPriceEqualsOriginalTimesRate` | Computed discount is accurate |
| `GetTopOrders_Top2_ReturnsExactly2` | Take(2) respected |
| `GetTopOrders_AreOrderedByLineTotalDescending` | Chaining keeps correct sort order |

---

## Exercises

1. Add `GET /linq/categories/summary?minCount=2` — filter `CategorySummary` rows where `Count >= minCount` using a chained `.Where()` after `GroupBy`.
2. Add `GET /linq/orders/by-customer/{customerId}` — use `Join` + `Where` to return only order lines for a specific customer.
3. Rewrite `GetOrderLines` using query syntax with an explicit `join … in … on … equals …` clause and compare readability.
4. Replace `SelectMany` in `GetAllProductLabels` with a nested `foreach` loop and verify the output is identical — then appreciate the brevity of `SelectMany`.


> **Branch:** `lesson/05-linq/a-basic`
> **Prerequisites:** Lesson 04-C (Raw SQL, Compiled Queries, Split Queries)

---

## What you will learn

| Topic | C# LINQ | Java parallel |
|---|---|---|
| Method syntax | `.Where().Select().ToList()` | `stream().filter().map().collect()` |
| Query syntax | `from p in … where … select p` | no direct equivalent (method chains only) |
| `Where` | filter elements | `stream().filter(…)` |
| `Select` | project / transform elements | `stream().map(…)` |
| `OrderBy` / `OrderByDescending` | sort; `ThenBy` for secondary key | `stream().sorted(Comparator…)` |
| `FirstOrDefault` | first match or `null` — never throws | `stream().findFirst().orElse(null)` |
| `ToList` | terminal — materialises the pipeline | `stream().collect(toList())` |
| Deferred execution | pipeline is lazy — work happens at the terminal operator | Java Streams are also lazy |

---

## 1. Method Syntax vs Query Syntax

LINQ provides two syntaxes that compile to identical IL.

```csharp
// Method syntax
var result = products
    .Where(p => p.Category == "Electronics")
    .OrderBy(p => p.Price)
    .Select(p => p.Name)
    .ToList();

// Query syntax (SQL-like)
var result = (from p in products
              where p.Category == "Electronics"
              orderby p.Price
              select p.Name).ToList();
```

Choose whichever reads more clearly for the task. Method syntax is more common for simple
pipelines; query syntax shines when using `let`, `join`, or `group … by`.

**Java parallel:** Java Streams only have method chains — there is no query-syntax equivalent.

---

## 2. Where — Filtering

```csharp
var electronics = products.Where(p => p.Category == "Electronics").ToList();
```

`Where` accepts a predicate and returns every element for which it is `true`.
The predicate is not evaluated until a terminal operator materialises the pipeline.

**Java:** `stream().filter(p -> p.getCategory().equals("Electronics")).collect(toList())`

---

## 3. Select — Projection

```csharp
// Project to a value tuple
var nameAndPrice = products.Select(p => (p.Name, p.Price)).ToList();
```

Only the data you need is materialised; when used with `IQueryable` (EF Core) only those
columns are included in the SQL `SELECT`.

**Java:** `stream().map(p -> new NamePrice(p.getName(), p.getPrice())).collect(toList())`

---

## 4. OrderBy / OrderByDescending

```csharp
var sorted = products
    .OrderByDescending(p => p.Price)  // primary sort key
    .ThenBy(p => p.Name)              // secondary sort key (stable)
    .ToList();
```

`ThenBy` / `ThenByDescending` add secondary keys.
Do **not** chain multiple `OrderBy` calls — each resets the sort order.

**Java:** `stream().sorted(Comparator.comparing(Product::getPrice).reversed().thenComparing(Product::getName))`

---

## 5. FirstOrDefault — Safe Single-Element Lookup

```csharp
Product? found = products.FirstOrDefault(p => p.Id == id);
// Returns null if no match — never throws.
```

| Method | No match | Multiple matches |
|---|---|---|
| `FirstOrDefault` | `null` / `default` | returns first |
| `First` | throws `InvalidOperationException` | returns first |
| `SingleOrDefault` | `null` / `default` | throws |
| `Single` | throws | throws |

**Java:** `stream().filter(p -> p.getId() == id).findFirst().orElse(null)`

---

## 6. Deferred Execution

Building a LINQ pipeline does **not** iterate the source — that work is deferred until a
terminal operator is called.

```csharp
// Steps 1-3: build the pipeline (no iteration yet)
IEnumerable<string> query = products
    .Where(p => p.Price <= 50)   // deferred
    .OrderBy(p => p.Price)       // deferred
    .Select(p => p.Name);        // deferred

// Step 4: terminal operator — iterates ONCE and produces List<string>
List<string> result = query.ToList();
```

Consequence: modifying the source between building and materialising the query is reflected
in the result. Materialise early with `ToList()` / `ToArray()` when you want a snapshot.

**Java parallel:** Java Streams are also lazy. Unlike C# queries, a Java Stream **cannot be reused**
after a terminal operation has been called.

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/linq/products` | All products; optional `?category=` filter |
| `GET` | `/linq/products/query-syntax?category=` | Same filter via query syntax |
| `GET` | `/linq/products/name-price` | `Select` projection — name + price only |
| `GET` | `/linq/products/by-price-desc` | `OrderByDescending` + `ThenBy` |
| `GET` | `/linq/products/{id}` | `FirstOrDefault` — 404 if not found |
| `GET` | `/linq/products/affordable?maxPrice=50` | Deferred pipeline materialised at `ToList` |

---

## Project Structure (new / changed files)

```
Lesson/
  Models/
    Product.cs               NEW  simple in-memory record (no EF / database)
  Services/
    LinqService.cs           NEW  static seed data + LINQ demo methods
  Controllers/
    LinqDtos.cs              NEW  ProductResponse, NamePriceDto
    LinqController.cs        NEW  /linq/* endpoints
  Program.cs                      + LinqService registered as singleton
Lesson.Tests/
  LinqBasicTests.cs          NEW  10 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~LinqBasicTests"
# 10 tests — all pass
```

| Test | What it verifies |
|---|---|
| `FilterByCategory_Electronics_ReturnsOnlyElectronics` | `Where` filters correctly |
| `FilterByCategory_UnknownCategory_ReturnsEmpty` | `Where` with no match returns empty |
| `FilterByCategory_NoCategory_ReturnsAll` | No filter returns all 10 products |
| `QuerySyntax_SameResultAsMethodSyntax` | Both syntaxes produce identical results |
| `GetNameAndPrice_ReturnsOnlyNameAndPrice` | `Select` projection returns correct shape |
| `GetByPriceDescending_FirstItemIsHighestPrice` | First item has the maximum price |
| `GetByPriceDescending_IsSorted` | Entire list is in descending price order |
| `FindById_ExistingId_ReturnsProduct` | `FirstOrDefault` finds the right product |
| `FindById_MissingId_ReturnsNotFound` | `FirstOrDefault` returns `null` ? 404 |
| `GetAffordableNames_MaxPrice50_OnlyCheapProducts` | Full deferred pipeline matches expected names |

---

## Exercises

1. Add `GET /linq/products/top/{n}` that uses `Take(n)` to return the `n` most expensive products.
2. Replace the `ToList()` terminal in `GetAffordableProductNames` with `ToArray()` and observe that the tests still pass.
3. Add a `Skip` + `Take` overload to `/linq/products?page=1&pageSize=3` to practice manual pagination over an in-memory collection.
4. Try calling `.Where(…)` twice on the same pipeline and verify that both predicates are applied (they are ANDed together in the iteration).

| Compiled query | `EF.CompileAsyncQuery(…)` | `@NamedQuery` / `@NamedNativeQuery` |
| Split query | `.Include(…).AsSplitQuery()` | `@EntityGraph` with `SUBSELECT` fetch |
| Cartesian explosion | single JOIN ? N×M rows | N+1 / cartesian product in JPA `JOIN FETCH` |

---

## 1. FromSqlRaw — Hand-written Parameterised SQL

`FromSqlRaw` lets you write arbitrary SQL while still getting tracked entities back.
EF Core can compose additional LINQ operators (`Where`, `OrderBy`, `Include`, …) on top.

```csharp
var accounts = await db.BankAccounts
    .FromSqlRaw(
        "SELECT * FROM BankAccounts WHERE Balance > {0} AND IsDeleted = 0",
        minBalance)
    .OrderBy(a => a.AccountNumber)
    .ToListAsync();
```

> ?? **SQL injection:** always use `{0}` placeholders (or `SqlParameter` objects).
> Never interpolate user input directly into the string.
> `FromSqlInterpolated` is an alternative that accepts a C# interpolated string safely.

**Java parallel:** `@Query(value = "SELECT * FROM bank_accounts WHERE balance > :min", nativeQuery = true)`

---

## 2. Stored Procedure Calls

On **SQL Server**:

```csharp
var accounts = await db.BankAccounts
    .FromSqlRaw("EXEC sp_GetAccountByNumber {0}", accountNumber)
    .ToListAsync();
```

On **PostgreSQL**:

```csharp
var accounts = await db.BankAccounts
    .FromSqlRaw("SELECT * FROM sp_get_account_by_number({0})", accountNumber)
    .ToListAsync();
```

**SQLite** has no stored-procedure engine, so this lesson uses an equivalent parameterised
`SELECT` and documents the real SP syntax above.

**Java parallel:**
```java
@Procedure("sp_GetAccountByNumber")
BankAccount getByNumber(String accountNumber);
// or:
entityManager.createNativeQuery("CALL sp_GetAccountByNumber(?)", BankAccount.class)
             .setParameter(1, accountNumber)
             .getSingleResult();
```

---

## 3. Compiled Queries

Every time you call a LINQ query EF Core translates the expression tree to SQL.
`EF.CompileAsyncQuery` does that translation **once at startup** and caches the result,
eliminating the per-call overhead on hot paths (thousands of calls per second).

```csharp
// Declared as a static field — compiled once per AppDomain.
private static readonly Func<BankingDbContext, string, IAsyncEnumerable<BankAccount>>
    _getByNumber = EF.CompileAsyncQuery(
        (BankingDbContext ctx, string number) =>
            ctx.BankAccounts.Where(a => a.AccountNumber == number));

// Usage — no translation overhead on subsequent calls.
await foreach (var account in _getByNumber(db, accountNumber))
    return account;
```

**Java parallel:** Hibernate `@NamedQuery` / `@NamedNativeQuery` — compiled during
`SessionFactory` bootstrap and reused for every execution.

---

## 4. Split Queries — Preventing Cartesian Explosion

When you `Include` a collection navigation on multiple parent rows, EF Core's default
single-JOIN strategy produces a **Cartesian product**:

```
2 accounts × 5 transactions = 10 result rows transferred
(even though only 7 logical rows exist)
```

With large collections (100 parents × 1 000 children) this multiplies to **100 000 rows**
over the wire for what is logically 1 100 rows of data.

`AsSplitQuery()` fires two separate SELECTs and stitches the results in memory:

```csharp
var accounts = await db.BankAccounts
    .Include(a => a.Transactions)
    .AsSplitQuery()          // ? two queries instead of one JOIN
    .OrderBy(a => a.AccountNumber)
    .ToListAsync();
// SQL 1: SELECT * FROM BankAccounts
// SQL 2: SELECT * FROM Transactions WHERE BankAccountId IN (1, 2, …)
```

**Trade-off:** two round-trips instead of one; results may be slightly inconsistent if
another transaction modifies data between the two SELECTs.  Choose split queries when
collection sizes make the Cartesian product impractical.

**Java parallel:** `@EntityGraph` with `@EntityGraph.EntityGraphType.FETCH` and
`fetchType = SUBSELECT` on the collection mapping.

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/accounts/raw?minBalance=0` | `FromSqlRaw` parameterised query |
| `GET` | `/accounts/by-number-sp/{number}` | Stored-procedure simulation |
| `GET` | `/accounts/by-number-compiled/{number}` | Compiled query lookup |
| `GET` | `/accounts/with-transactions` | `AsSplitQuery` — accounts + transactions |

---

## Project Structure (new / changed files)

```
Lesson/
  Repositories/
    IAccountRepository.cs     + GetByRawSqlAsync, GetByNumberStoredProcAsync,
                                GetByNumberCompiledAsync, GetWithTransactionsSplitAsync
    AccountRepository.cs      implements the above; compiled query as static field
  Controllers/
    AccountDtos.cs            + TransactionSummaryDto
    AccountsController.cs     + /raw, /by-number-sp, /by-number-compiled,
                                /with-transactions endpoints
Lesson.Tests/
  AccountsControllerRawSqlTests.cs  NEW  10 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~AccountsControllerRawSqlTests"
# 10 tests — all pass
```

| Test | What it verifies |
|---|---|
| `GetByRawSql_BelowAllBalances_ReturnsAllAccounts` | `FromSqlRaw` with threshold 0 returns all seeded accounts |
| `GetByRawSql_AboveAllBalances_ReturnsEmpty` | impossibly high threshold ? empty list |
| `GetByRawSql_OnlyReturnsAccountsAboveThreshold` | all returned accounts exceed the threshold |
| `GetByNumberStoredProc_ExistingAccount_ReturnsAccount` | SP simulation returns the right account |
| `GetByNumberStoredProc_UnknownAccount_ReturnsNotFound` | 404 for unknown account number |
| `GetByNumberCompiled_ExistingAccount_ReturnsAccount` | compiled query finds seeded account |
| `GetByNumberCompiled_UnknownAccount_ReturnsNotFound` | 404 for unknown account number |
| `GetByNumberCompiled_CalledTwice_BothReturnSameResult` | compiled query is idempotent |
| `GetWithTransactions_ReturnsAccountsWithTransactions` | split query returns accounts |
| `GetWithTransactions_SeededTransactions_ArePresent` | transactions are present in the split-query result |

---

## Exercises

1. Replace `FromSqlRaw` in `GetByRawSqlAsync` with `FromSqlInterpolated` and observe how EF Core automatically prevents SQL injection.
2. Add a `ExecuteSqlRaw` call to bulk-deactivate all accounts of a given type:
   `UPDATE BankAccounts SET IsActive = 0 WHERE AccountType = {0}`.
3. Benchmark (using BenchmarkDotNet) the compiled query vs. a non-compiled equivalent to
   measure the translation overhead on a tight loop.
4. Enable `QuerySplittingBehavior.SplitQuery` globally in `DbContextOptions` and verify that all `Include` calls now use split queries by default, then override one back to `AsSingleQuery()`.


> **Branch:** `lesson/04-ef-advanced/b-intermediate`
> **Prerequisites:** Lesson 04-A (Navigation properties, Include / ThenInclude)

---

## What you will learn

| Topic | C# / EF Core | Java / Spring Boot parallel |
|---|---|---|
| Pagination | `Skip` / `Take` | `PageRequest.of(page, size)` ? `Page<T>` |
| Projection | `.Select(a => new Dto(...))` | DTO projection with `@Query` + constructor expression |
| GroupBy aggregate | `.GroupBy(...).Select(g => ...)` | `@Query("SELECT ... GROUP BY ...")` |
| Existential check | `AnyAsync(predicate)` | `repository.existsBy...()` |
| Universal check | `AllAsync(predicate)` | custom `@Query` with NOT EXISTS |
| Scalar count | `CountAsync(predicate)` | `repository.countBy...()` |
| IQueryable vs IEnumerable | query tree vs in-memory iteration | `JpaSpecificationExecutor` vs stream |

---

## 1. IQueryable vs IEnumerable

**Key concept:** `IQueryable<T>` is an unevaluated expression tree — EF Core composes SQL
from it and executes when you call a terminal operator (`ToListAsync`, `FirstOrDefaultAsync`, …).
`IEnumerable<T>` is in-memory — all rows are loaded before filtering/projecting.

```csharp
// IQueryable — SQL WHERE is added before the query is sent
IQueryable<BankAccount> query = db.BankAccounts;
query = query.Where(a => a.AccountType == "Savings"); // no DB round-trip yet
var list = await query.ToListAsync();                  // ONE SQL query with WHERE

// IEnumerable — loads ALL rows, then filters in C#
IEnumerable<BankAccount> all = await db.BankAccounts.ToListAsync();
var savings = all.Where(a => a.AccountType == "Savings"); // in-memory!
```

**Rule:** keep your queries as `IQueryable` until you need the data.

**Java parallel:** `JpaSpecificationExecutor<T>` builds a query spec lazily; calling
`findAll(spec)` executes it. Collecting to a `List<>` and then streaming is the in-memory equivalent.

---

## 2. Pagination with Skip / Take

```csharp
var items = await db.BankAccounts
    .OrderBy(a => a.AccountNumber)      // ORDER BY is required before OFFSET
    .Skip((page - 1) * pageSize)        // SQL: OFFSET
    .Take(pageSize)                     // SQL: LIMIT / FETCH NEXT
    .ToListAsync();
```

Combined with a total count this gives a `PagedResult<T>`:

```csharp
int total = await baseQuery.CountAsync();
// ... Skip / Take ...
return new PagedResult<T>(items, total, page, pageSize);
```

**Java parallel:** `repository.findAll(PageRequest.of(page - 1, size))` returns a `Page<T>`
with `.getContent()`, `.getTotalElements()`, and `.getTotalPages()`.

---

## 3. Projection with Select

Only fetch the columns you actually need:

```csharp
var dtos = await db.BankAccounts
    .Select(a => new AccountSummaryDto(
        a.Id, a.AccountNumber, a.OwnerName, a.AccountType, a.Balance, a.IsActive))
    .ToListAsync();
// SQL: SELECT Id, AccountNumber, OwnerName, AccountType, Balance, IsActive FROM BankAccounts
```

Audit / concurrency columns (`RowVersion`, `CreatedAt`, `UpdatedAt`) are never touched.

**Java parallel:** constructor expression in JPQL:
`SELECT new com.example.dto.AccountSummaryDto(a.id, a.accountNumber, ...) FROM BankAccount a`

---

## 4. GroupBy Aggregate

```csharp
// Works fully in SQL on SQL Server / PostgreSQL
var stats = await db.BankAccounts
    .GroupBy(a => a.AccountType)
    .Select(g => new AccountTypeStatDto(
        g.Key, g.Count(), g.Sum(a => a.Balance), g.Average(a => (double)a.Balance)))
    .ToListAsync();
```

> **SQLite caveat:** SQLite's `decimal` support for aggregates is limited.
> The implementation fetches `(AccountType, Balance)` columns via `IQueryable` projection,
> then completes the grouping in C# memory — illustrating the intentional IQueryable ? IEnumerable handoff:
>
> ```csharp
> var rows = await db.BankAccounts
>     .Select(a => new { a.AccountType, a.Balance })
>     .ToListAsync();                                   // IQueryable terminates here
>
> return rows
>     .GroupBy(a => a.AccountType)                     // IEnumerable GroupBy in C#
>     .Select(g => new AccountTypeStatDto(...))
>     .ToList();
> ```

**Java parallel:** `@Query("SELECT a.accountType, COUNT(a), SUM(a.balance), AVG(a.balance) FROM BankAccount a GROUP BY a.accountType")`

---

## 5. Any / All / Count

```csharp
// Any — SQL: SELECT CASE WHEN EXISTS (...) THEN 1 ELSE 0 END
bool hasHighBalance = await db.BankAccounts.AnyAsync(a => a.Balance > threshold);

// All — SQL: SELECT CASE WHEN NOT EXISTS (... WHERE NOT condition) THEN 1 ELSE 0 END
bool allPositive = await db.BankAccounts.AllAsync(a => a.Balance > 0);

// Count — SQL: SELECT COUNT(*) FROM BankAccounts WHERE IsActive = 1
int active = await db.BankAccounts.CountAsync(a => a.IsActive);
```

None of these load entity rows — they return a single scalar from the database.

**Java parallel:**
- `AnyAsync` ? `repository.existsByBalanceGreaterThan(threshold)`
- `AllAsync` ? custom `@Query` with `NOT EXISTS`
- `CountAsync` ? `repository.countByIsActive(true)`

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/accounts/summary?page=1&pageSize=10` | Paginated + projected DTO list |
| `GET` | `/accounts/stats` | GroupBy AccountType with COUNT / SUM / AVG |
| `GET` | `/accounts/any-high-balance?threshold=10000` | AnyAsync — returns `true`/`false` |
| `GET` | `/accounts/all-positive` | AllAsync — returns `true`/`false` |
| `GET` | `/accounts/count?type=Savings` | CountAsync with optional type filter |

---

## Project Structure (new / changed files)

```
Lesson/
  Entities/
    Transaction.cs               NEW  linked to BankAccount (one-to-many)
    BankAccount.cs                    + Transactions navigation collection
  Repositories/
    IAccountRepository.cs             + GetPagedSummariesAsync, GetStatsByTypeAsync,
                                        AnyWithBalanceAboveAsync, AllPositiveBalanceAsync,
                                        CountActiveAsync
    AccountRepository.cs              implements the above; shows IQueryable/IEnumerable split
  Controllers/
    AccountDtos.cs                    + AccountSummaryDto, AccountTypeStatDto, PagedResult<T>
    AccountsController.cs             + /summary, /stats, /any-high-balance,
                                        /all-positive, /count endpoints
  Data/
    BankingDbContext.cs               + DbSet<Transaction>, HasMany/WithOne config,
                                        seed transactions, query filter on Transaction
    Migrations/
      AddTransactions            NEW  Transactions table + seed rows
Lesson.Tests/
  AccountsControllerAdvancedTests.cs  NEW  11 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~AccountsControllerAdvancedTests"
# 11 tests — all pass
```

| Test | What it verifies |
|---|---|
| `GetSummary_Page1_ReturnsPagedResult` | Pagination works; page/count fields populated |
| `GetSummary_PageSize1_ReturnsSingleItem` | `Take(1)` returns exactly one item |
| `GetSummary_ProjectionDto_DoesNotExposeAuditFields` | Projection omits audit columns |
| `GetSummary_InvalidPage_ReturnsBadRequest` | `page=0` ? 400 |
| `GetStats_ReturnsGroupedByAccountType` | GroupBy returns non-empty stat rows |
| `GetStats_SeededData_ContainsCheckingAndSavings` | Both seeded types appear |
| `AnyHighBalance_BelowSeedValues_ReturnsTrue` | AnyAsync ? true when threshold is below seed balances |
| `AnyHighBalance_AboveSeedValues_ReturnsFalse` | AnyAsync ? false for impossibly high threshold |
| `AllPositive_SeededAccounts_ReturnsTrue` | AllAsync ? true; all seeded accounts have positive balance |
| `CountActive_NoFilter_ReturnsAllActiveAccounts` | CountAsync ? > 0 |
| `CountActive_FilteredByType_SumsToTotal` | Savings + Checking counts equal total |

---

## Exercises

1. Add a `GET /accounts/summary` sort parameter (`sortBy=balance&desc=true`) using conditional `OrderBy` on `IQueryable`.
2. Add `GET /accounts/stats/transactions` — GroupBy `AccountType` with `SUM` of transaction amounts (requires a join to `Transactions`).
3. Implement server-side GroupBy for a provider that supports it (e.g., SQL Server) and compare the generated SQL to the SQLite fallback.
4. Add a `MinBalance` / `MaxBalance` filter to `/accounts/summary` and observe how adding `.Where()` clauses to an `IQueryable` before `Skip`/`Take` pushes the filter into the SQL `WHERE` clause.

| Eager loading | `.Include(c => c.Accounts)` | `@EntityGraph` / `JOIN FETCH` |
| Chained loading | `.ThenInclude(a => a.Address)` | nested `JOIN FETCH` in JPQL |
| Filtered Include | `.Include(c => c.Accounts.Where(a => a.IsActive))` | `@Query` with WHERE on the join |
| FK assignment | set `account.CustomerId` — EF updates the row | set the `@ManyToOne` field + persist |
| Lazy loading | disabled by default — must call `Include` explicitly | `FetchType.LAZY` (default in JPA) |

---

## 1. Navigation Properties

A navigation property is a C# reference or collection on an entity class that EF Core uses
to represent a relationship in the object model.

EF Core maps this to a foreign key column (`CustomerId`) in `BankAccounts`.

```csharp
// Customer -- the "one" side
public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public ICollection<BankAccount> Accounts { get; set; } = [];
}

// BankAccount -- the "many" side
public class BankAccount
{
    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }
}
```

Configure in `OnModelCreating`:

```csharp
modelBuilder.Entity<Customer>()
    .HasMany(c => c.Accounts)
    .WithOne(a => a.Customer)
    .HasForeignKey(a => a.CustomerId)
    .IsRequired(false)
    .OnDelete(DeleteBehavior.SetNull);
```

**Java parallel:**
```java
@OneToMany(mappedBy = "customer", cascade = CascadeType.ALL)
private List<BankAccount> accounts = new ArrayList<>();

@ManyToOne @JoinColumn(name = "customer_id")
private Customer customer;
```

---

## 2. Eager Loading with Include

By default, EF Core does **not** lazy-load navigation properties. You must call `.Include()` explicitly.

```csharp
var customer = await db.Customers
    .Include(c => c.Accounts)
    .FirstOrDefaultAsync(c => c.Id == id);
```

**Java parallel:** `@EntityGraph(attributePaths = "accounts")` or `JOIN FETCH` in JPQL.

---

## 3. ThenInclude — Loading Nested Navigation

```csharp
var customer = await db.Customers
    .Include(c => c.Accounts)
        .ThenInclude(a => a.Address)
    .FirstOrDefaultAsync(c => c.Id == id);
```

**Java parallel:** nested `JOIN FETCH` in JPQL.

---

## 4. Filtered Include

```csharp
var customer = await db.Customers
    .Include(c => c.Accounts.Where(a => a.IsActive))
    .FirstOrDefaultAsync(c => c.Id == id);
```

The WHERE predicate is pushed into the SQL JOIN. The global `IsDeleted` query filter is also
applied automatically to included collections.

---

## 5. FK Assignment (linking records)

```csharp
account.CustomerId = customerId;
await uow.CommitAsync();
// SQL: UPDATE BankAccounts SET CustomerId = @id WHERE Id = @accountId
```

Setting the FK scalar property is enough — EF Core keeps navigation in sync within the same DbContext scope.

---

## Endpoints

| Method | Route | Notes |
|--------|-------|-------|
| `GET` | `/customers` | All customers (no accounts loaded) |
| `GET` | `/customers/{id}/accounts` | Customer + all accounts (Include + ThenInclude) |
| `GET` | `/customers/{id}/accounts/active` | Customer + active accounts (filtered Include) |
| `POST` | `/customers` | Create customer; 201 / 409 on duplicate email |
| `POST` | `/customers/{cid}/accounts/{aid}` | Assign existing account to customer (FK update) |

---

## Project Structure (new / changed files)

```
Lesson/
  Entities/
    Customer.cs                  NEW  "one" side of Customer -> BankAccounts
    BankAccount.cs                    + CustomerId (FK) + Customer? (navigation)
  Repositories/
    ICustomerRepository.cs       NEW  interface with Include-based query methods
    CustomerRepository.cs        NEW  demonstrates Include, ThenInclude, filtered Include
  Controllers/
    CustomerDtos.cs              NEW  CreateCustomerRequest, CustomerResponse
    CustomersController.cs       NEW  CRUD + Include/filtered-Include endpoints
  Data/
    BankingDbContext.cs               + DbSet<Customer>, HasMany/WithOne config, updated seed
    Migrations/
      AddCustomerNavigation      NEW  adds Customers table + CustomerId FK column
Lesson.Tests/
  CustomersControllerTests.cs   NEW  7 integration tests
```

---

## Tests

```bash
dotnet test --filter "FullyQualifiedName~CustomersControllerTests"
# 7 tests — all pass
```

| Test | What it verifies |
|---|---|
| `GetAll_ReturnsSeededCustomers` | Seeded customers are returned |
| `Create_ValidRequest_ReturnsCreated` | 201 + id populated |
| `Create_DuplicateEmail_ReturnsConflict` | 409 on duplicate email |
| `GetWithAccounts_SeededCustomer_ReturnsAccounts` | Include fires — Accounts is not empty |
| `GetWithAccounts_MissingCustomer_ReturnsNotFound` | 404 for unknown customer |
| `GetWithActiveAccounts_ReturnsOnlyActiveAccounts` | Filtered Include — inactive account excluded |
| `AssignAccount_LinksAccountToCustomer` | FK assignment — account appears in customer list |

---

## Exercises

1. Add `GET /customers/{id}/accounts/savings` using a filtered include for `AccountType == "Savings"`.
2. Add `GET /customers?includeAccounts=true` — conditionally apply Include only when requested.
3. Explore `AsSplitQuery()`: replace the default single-JOIN strategy with two separate queries and compare SQL logs.
4. Add a `Transaction` entity linked to `BankAccount` and practice `ThenInclude` two levels deep:
   `Customer -> Accounts -> Transactions`.
