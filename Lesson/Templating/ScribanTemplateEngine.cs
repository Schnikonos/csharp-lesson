using Scriban;
using Scriban.Runtime;
using System.Text.RegularExpressions;

namespace Lesson.Templating;

/// <summary>
/// Scriban-backed template engine.
/// Scriban uses {{ variable }} syntax — the .NET equivalent of Jinja2 / Thymeleaf.
///
/// Java parallel:
///   new ClassLoaderTemplateResolver() + templateEngine.process(template, ctx)
///   → ScribanTemplateEngine.RenderAsync(name, model)
/// </summary>
public class ScribanTemplateEngine : ITemplateEngine
{
    private readonly string _templateRoot;

    // Converts PascalCase / camelCase → snake_case so Scriban templates can use
    // {{ transaction_id }} just like Jinja2 / Thymeleaf variable names.
    private static readonly Regex PascalToSnake =
        new(@"(?<!^)([A-Z])", RegexOptions.Compiled);

    private static string ToSnakeCase(string name) =>
        PascalToSnake.Replace(name, "_$1").ToLowerInvariant();

    public ScribanTemplateEngine(string templateRoot)
    {
        _templateRoot = templateRoot;
    }

    public async Task<string> RenderAsync(string templateName, object model, CancellationToken ct = default)
    {
        var path = Path.Combine(_templateRoot, templateName);
        var source = await File.ReadAllTextAsync(path, ct);
        return await RenderStringAsync(source, model, ct);
    }

    public Task<string> RenderStringAsync(string templateSource, object model, CancellationToken ct = default)
    {
        var template = Template.Parse(templateSource);
        if (template.HasErrors)
            throw new InvalidOperationException(
                $"Template parse errors: {string.Join("; ", template.Messages)}");

        var scriptObject = new ScriptObject();
        scriptObject.Import(model, renamer: member => ToSnakeCase(member.Name));

        var context = new TemplateContext();
        context.PushGlobal(scriptObject);

        var result = template.Render(context);
        return Task.FromResult(result);
    }
}
