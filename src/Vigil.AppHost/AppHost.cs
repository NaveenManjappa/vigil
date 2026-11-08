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

// The endpoint's scheme is tcp, so the driver's bolt:// address is built from host and port.
var boltUri = ReferenceExpression.Create($"bolt://{bolt.Property(EndpointProperty.HostAndPort)}");

// Generated so the AppHost starts without prompting while Llm:Provider is Fake; to use Azure OpenAI, replace the
// persisted value with the real key (user-secrets `Parameters:azure-openai-key`), so it never lands in source control.
var azureOpenAiKey = builder.AddParameter(
    "azure-openai-key",
    new GenerateParameterDefault { MinLength = 32, Special = false },
    secret: true,
    persist: true);

builder.AddProject<Projects.Vigil_Api>("api")
    .WithReference(db)
    .WaitFor(db)
    .WithEnvironment("Neo4j__Uri", boltUri)
    .WithEnvironment("Neo4j__Password", neo4jPassword)
    .WaitFor(neo4j)
    .WithEnvironment("Llm__ApiKey", azureOpenAiKey)
    // Opens Scalar from the dashboard's link, where the walkthrough happens.
    .WithUrlForEndpoint("http", url => url.Url = "/scalar");

builder.Build().Run();
