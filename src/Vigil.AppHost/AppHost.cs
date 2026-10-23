var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("vigil-postgres-data");
var db = postgres.AddDatabase("vigil");

// No first-party Aspire Neo4j integration exists, so it runs as a plain container.
// The password is generated once and persisted to user-secrets; Neo4j requires at least 8 characters.
var neo4jPassword = builder.AddParameter(
    "neo4j-password",
    new GenerateParameterDefault { MinLength = 16, Special = false },
    secret: true,
    persist: true);

var neo4j = builder.AddContainer("neo4j", "neo4j", "5-community")
    // Fixed host ports so Neo4j Browser (http://localhost:7474) finds bolt on its default 7687.
    .WithHttpEndpoint(port: 7474, targetPort: 7474, name: "http")
    .WithEndpoint(port: 7687, targetPort: 7687, name: "bolt", scheme: "tcp")
    .WithEnvironment(ctx => ctx.EnvironmentVariables["NEO4J_AUTH"] = ReferenceExpression.Create($"neo4j/{neo4jPassword}"))
    .WithVolume("vigil-neo4j-data", "/data")
    .WithHttpHealthCheck("/", endpointName: "http");

var bolt = neo4j.GetEndpoint("bolt");

var api = builder.AddProject<Projects.Vigil_Api>("api")
    .WithReference(db)
    .WaitFor(db)
    // The endpoint's scheme is tcp, so the driver's bolt:// address is built from host and port.
    .WithEnvironment("Neo4j__Uri", ReferenceExpression.Create($"bolt://{bolt.Property(EndpointProperty.HostAndPort)}"))
    .WithEnvironment("Neo4j__Password", neo4jPassword)
    .WaitFor(neo4j);

// Explicit start: the simulator replays scripted scenarios once, so it runs when you click Start in the dashboard.
builder.AddProject<Projects.Vigil_Simulator>("simulator")
    .WithReference(api)
    .WaitFor(api)
    .WithExplicitStart();

builder.Build().Run();
