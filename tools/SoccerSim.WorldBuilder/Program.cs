using System.Text.Json.Serialization;
using Microsoft.Extensions.FileProviders;
using SoccerSim.Core.Persistence;
using SoccerSim.Infrastructure.Sqlite;
using SoccerSim.WorldBuilder;
using SoccerSim.WorldBuilder.Api;

// The tool is a web app with one operational command in front of it. `import` is a command and
// not a migration on purpose: loading a world document is something a user does deliberately, to
// a chosen file, against a database that already has a schema (ROADMAP.md Sprint 2).
//
// Only the tool's own verbs are intercepted. Anything else is host configuration (--urls,
// --environment, and the arguments a test host passes when it starts the app in-process), so it
// falls through rather than being mistaken for a mistyped command.
if (WorldBuilderCommands.IsCommand(args))
    return await WorldBuilderCommands.RunAsync(args);

// The content root follows the binary, not the shell's working directory, so the tool serves its
// own wwwroot wherever it is launched from. The database path stays relative to the working
// directory, which is the one the user chose.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

string databasePath = builder.Configuration["World:DatabasePath"] ?? "world.db";
builder.Services.AddSingleton(SqliteConnectionFactory.ForFile(databasePath));

// One connection, one transaction scope per request.
builder.Services.AddScoped<IWorldUnitOfWork>(services =>
    new SqliteWorldUnitOfWork(services.GetRequiredService<SqliteConnectionFactory>().Open()));

builder.Services.ConfigureHttpJsonOptions(options =>
    // Enums travel as their names, never as integers: the JSON is the same closed vocabulary the
    // schema and the source document use, and it stays readable when a field is added.
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

WebApplication app = builder.Build();

// The schema is the app's responsibility; the data is not. Migrating on startup means a fresh
// checkout serves an empty world that explains how to import one, instead of a 500.
new MigrationRunner(app.Services.GetRequiredService<SqliteConnectionFactory>()).Migrate();

// Serve the UI from the wwwroot beside the binary, named explicitly rather than inferred from
// the content root: the same rule then holds when the tool runs from a different working
// directory and when it is hosted inside a test.
var webRoot = new PhysicalFileProvider(Path.Combine(AppContext.BaseDirectory, "wwwroot"));
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = webRoot });
app.UseStaticFiles(new StaticFileOptions { FileProvider = webRoot });
app.MapWorldApi();
app.MapEditApi();
app.MapGenerationApi();

app.Run();
return 0;

// Exposed for WebApplicationFactory-based integration tests in SoccerSim.WorldBuilder.Tests.
public partial class Program;
