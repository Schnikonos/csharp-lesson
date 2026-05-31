namespace Lesson.Templating;

/// <summary>
/// Abstraction over a template engine — analogous to Thymeleaf's TemplateEngine
/// or Jinja2's Environment.get_template().render().
/// </summary>
public interface ITemplateEngine
{
    /// <summary>Render a named template file with the supplied model.</summary>
    Task<string> RenderAsync(string templateName, object model, CancellationToken ct = default);

    /// <summary>Render an inline template string with the supplied model.</summary>
    Task<string> RenderStringAsync(string templateSource, object model, CancellationToken ct = default);
}
