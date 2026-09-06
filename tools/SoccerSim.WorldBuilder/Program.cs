using SoccerSim.WorldBuilder;

// The tool is a web app with one operational command in front of it. `import` is a command and
// not a migration on purpose: loading a world document is something a user does deliberately, to
// a chosen file, against a database that already has a schema (ROADMAP.md Sprint 2).
if (args.Length > 0)
    return await WorldBuilderCommands.RunAsync(args);

var builder = WebApplication.CreateBuilder(args);

WebApplication app = builder.Build();

// Sprint 3 adds the real /api/world surface (GET clubs, characters, geo, competitions) that
// serves the club-page UI; Sprint 4 adds the PATCH endpoints for edits.
app.MapGet("/health", () => Results.Ok(new { status = "ok", tool = "SoccerSim.WorldBuilder" }));

app.Run();
return 0;

// Exposed for WebApplicationFactory-based integration tests in SoccerSim.WorldBuilder.Tests.
public partial class Program;
