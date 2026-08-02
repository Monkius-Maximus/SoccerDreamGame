using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using SoccerSim.Content;
using SoccerSim.Content.Csv;
using SoccerSim.Content.Model;
using SoccerSim.Content.Serialization;
using SoccerSim.Content.Validation;
using SoccerSim.ContentStudio;

const string DefaultContentDir = "content/dev";
const string DefaultOutputDb = "build/content/content.db";
const string ListenUrl = "http://127.0.0.1:5099";

string contentDir = ArgValue(args, "--content") ?? DefaultContentDir;

var builder = WebApplication.CreateBuilder(args);

// Loopback only. This tool has no authentication because it is a single-user desktop tool;
// binding it to anything routable would expose unauthenticated write access to the content.
builder.WebHost.UseUrls(ListenUrl);
builder.Services.AddSingleton(new ContentWorkspace(contentDir));

WebApplication app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

// One place that turns a content failure into a 4xx with a readable body, so every endpoint
// below can just do the work and let bad input throw.
app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (ContentValidationException ex)
    {
        await WriteProblem(context, HttpStatusCode.UnprocessableEntity, ex.Message);
    }
    catch (ContentFormatException ex)
    {
        await WriteProblem(context, HttpStatusCode.BadRequest, ex.Message);
    }
    catch (KeyNotFoundException ex)
    {
        await WriteProblem(context, HttpStatusCode.NotFound, ex.Message);
    }
});

app.MapGet("/api/categories", (ContentWorkspace workspace) =>
{
    IReadOnlyDictionary<string, int> counts = workspace.Counts();
    var issuesByCategory = workspace.Validate().Issues
        .GroupBy(i => i.Category, StringComparer.Ordinal)
        .ToDictionary(g => g.Key, g => g.Count(i => i.Severity == IssueSeverity.Error), StringComparer.Ordinal);

    return Results.Json(ContentCategory.Editable.Select(category => new
    {
        name = category,
        count = counts.GetValueOrDefault(category),
        errors = issuesByCategory.GetValueOrDefault(category),
    }));
});

app.MapGet("/api/{category}", (string category, string? q, ContentWorkspace workspace) =>
{
    IReadOnlyList<IContentEntity> entities = workspace.List(category);
    if (!string.IsNullOrWhiteSpace(q))
    {
        // Substring match over the serialized row: crude, but it means one search box finds a
        // club by name, by key, or by the league it belongs to without a query language.
        entities = entities
            .Where(e => SerializeEntity(e).Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    return Results.Text(SerializeEntities(entities), "application/json");
});

app.MapGet("/api/{category}/export.csv", (string category, ContentWorkspace workspace) =>
{
    string csv = ContentCsv.Write(workspace.List(category), ContentJson.Options);
    return Results.Text(csv, "text/csv");
});

app.MapPost("/api/{category}", async (string category, HttpRequest request, ContentWorkspace workspace) =>
{
    JsonElement payload = await ReadJson(request);
    IContentEntity saved = workspace.Upsert(category, payload);
    return Results.Text(SerializeEntity(saved), "application/json");
});

app.MapPut("/api/{category}/{key}", async (
    string category, string key, HttpRequest request, ContentWorkspace workspace) =>
{
    JsonElement payload = await ReadJson(request);
    if (!payload.TryGetProperty("key", out JsonElement keyElement)
        || !string.Equals(keyElement.GetString(), key, StringComparison.Ordinal))
    {
        // Renaming via PUT would silently create a second entity and orphan every reference to
        // the old key. Refuse, and let the UI do a delete-then-create if a rename is intended.
        return Results.Json(
            new { error = $"Body key '{keyElement.GetString()}' does not match the URL key '{key}'. "
                          + "To rename an entity, delete it and create it under the new key." },
            statusCode: (int)HttpStatusCode.Conflict);
    }

    IContentEntity saved = workspace.Upsert(category, payload);
    return Results.Text(SerializeEntity(saved), "application/json");
});

app.MapDelete("/api/{category}/{key}", (string category, string key, ContentWorkspace workspace) =>
    workspace.Delete(category, key)
        ? Results.Ok(new { deleted = key })
        : Results.NotFound(new { error = $"No '{key}' in {category}." }));

app.MapPost("/api/{category}/import", async (
    string category, bool? dryRun, HttpRequest request, ContentWorkspace workspace) =>
{
    string text = await new StreamReader(request.Body).ReadToEndAsync();
    CsvParseResult parsed = ContentCsv.Parse(text);

    if (parsed.Errors.Count > 0)
    {
        // Nothing is written when any row is malformed: a half-applied paste is far worse to
        // recover from than a rejected one.
        return Results.Json(
            new { imported = 0, errors = parsed.Errors },
            statusCode: (int)HttpStatusCode.UnprocessableEntity);
    }

    var elements = parsed.Rows.Select(row => JsonSerializer.Deserialize<JsonElement>(row.ToJsonString())).ToList();
    if (dryRun == true)
        return Results.Json(new { imported = 0, wouldImport = elements.Count, errors = Array.Empty<CsvRowError>() });

    int imported = workspace.UpsertMany(category, elements);
    return Results.Json(new { imported, errors = Array.Empty<CsvRowError>() });
});

app.MapGet("/api/refs", (ContentWorkspace workspace) => Results.Json(workspace.References()));

app.MapGet("/api/validate", (ContentWorkspace workspace) =>
{
    ContentValidationResult result = workspace.Validate();
    return Results.Json(new
    {
        valid = result.IsValid,
        issues = result.Issues.Select(i => new
        {
            severity = i.Severity.ToString(),
            code = i.Code,
            category = i.Category,
            entityKey = i.EntityKey,
            field = i.Field,
            message = i.Message,
        }),
    });
});

app.MapGet("/api/manifest", (ContentWorkspace workspace) => Results.Json(new
{
    directory = workspace.ContentDirectory,
    manifest = workspace.Bundle.Manifest,
    counts = workspace.Counts(),
}));

app.MapPost("/api/build", (string? output, ContentWorkspace workspace) =>
{
    string path = workspace.Build(output ?? DefaultOutputDb);
    return Results.Json(new { built = path, hash = workspace.Bundle.Manifest.ContentHash });
});

Console.WriteLine($"Content Studio — editing {Path.GetFullPath(contentDir)}");
Console.WriteLine($"Open {ListenUrl}");
app.Run();

// System.Text.Json serializes by the DECLARED type, so handing it an IContentEntity emits only
// Id and Key and silently drops every real field. Widening to object makes it use the runtime
// type instead — the grid depends on this.
static string SerializeEntity(IContentEntity entity) =>
    JsonSerializer.Serialize<object>(entity, ContentJson.Options);

static string SerializeEntities(IEnumerable<IContentEntity> entities) =>
    JsonSerializer.Serialize<object>(entities.Cast<object>().ToArray(), ContentJson.Options);

static string? ArgValue(string[] args, string name)
{
    int index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static async Task<JsonElement> ReadJson(HttpRequest request)
{
    try
    {
        return await request.ReadFromJsonAsync<JsonElement>();
    }
    catch (JsonException ex)
    {
        throw new ContentFormatException($"Request body is not valid JSON: {ex.Message}", ex);
    }
}

static async Task WriteProblem(HttpContext context, HttpStatusCode status, string message)
{
    context.Response.StatusCode = (int)status;
    context.Response.ContentType = "application/json";
    await context.Response.WriteAsJsonAsync(new { error = message });
}
