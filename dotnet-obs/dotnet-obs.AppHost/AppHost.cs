var builder = DistributedApplication.CreateBuilder(args);

var cache = builder.AddRedis("cache");

// Send OTLP telemetry to the local Grafana observability stack (observability/docker-compose.yml)
// rather than the Aspire dashboard. Start the stack first: cd observability && docker compose up -d
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"] ?? "http://localhost:4317";

var server = builder.AddProject<Projects.dotnet_obs_Server>("server")
    .WithReference(cache)
    .WaitFor(cache)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", otlpEndpoint)
    .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "grpc");

var webfrontend = builder.AddViteApp("webfrontend", "../frontend")
    .WithReference(server)
    .WaitFor(server);

server.PublishWithContainerFiles(webfrontend, "wwwroot");

builder.Build().Run();
