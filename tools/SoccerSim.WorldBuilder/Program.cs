var builder = WebApplication.CreateBuilder(args);

WebApplication app = builder.Build();

// Sprint 0 scope: prove the host, SoccerSim.Core and SoccerSim.Infrastructure.Sqlite compile
// and wire together end to end. Sprint 3 adds the real /api/world surface (GET clubs,
// characters, geo, competitions) that serves the club-page UI; Sprint 4 adds the PATCH
// endpoints for edits. Nothing here is meant to survive Sprint 3 unchanged.
app.MapGet("/health", () => Results.Ok(new { status = "ok", tool = "SoccerSim.WorldBuilder" }));

app.Run();

// Exposed for WebApplicationFactory-based integration tests in SoccerSim.WorldBuilder.Tests.
public partial class Program;
