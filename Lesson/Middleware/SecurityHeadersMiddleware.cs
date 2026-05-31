namespace Lesson.Middleware;

// =============================================================================
// LESSON 26-C: SecurityHeadersMiddleware
//
// Adds browser-security response headers to every response.
// These headers are a first-line defence against common web attacks.
//
// Java parallel:
//   Spring Security http.headers().*  — configured in SecurityFilterChain
//   e.g. http.headers()
//          .contentSecurityPolicy(...)
//          .frameOptions(...)
//          .xssProtection(...)
// =============================================================================
public class SecurityHeadersMiddleware : IMiddleware
{
    public Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var headers = context.Response.Headers;

        // Content-Security-Policy — controls which sources browsers may load.
        // "default-src 'self'" allows only same-origin resources.
        // Java: http.headers().contentSecurityPolicy("default-src 'self'")
        headers["Content-Security-Policy"] =
            "default-src 'self'; style-src 'self'; script-src 'self'; img-src 'self' data:";

        // X-Frame-Options — prevents clickjacking by disallowing iframe embedding.
        // Java: http.headers().frameOptions().deny()
        headers["X-Frame-Options"] = "DENY";

        // X-Content-Type-Options — prevents MIME-sniffing attacks.
        // Java: http.headers().contentTypeOptions()  (enabled by default in Spring Security)
        headers["X-Content-Type-Options"] = "nosniff";

        // Referrer-Policy — controls how much referrer info is sent with requests.
        // Java: http.headers().referrerPolicy(ReferrerPolicyHeaderWriter.ReferrerPolicy.STRICT_ORIGIN_WHEN_CROSS_ORIGIN)
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

        return next(context);
    }
}
