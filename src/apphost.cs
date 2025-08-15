#pragma warning disable

#:sdk Microsoft.NET.Sdk
#:sdk Aspire.AppHost.Sdk@9.4.0

#region imports
#:package Aspire.Hosting.AppHost@9.4.0
#:package Aspire.Hosting.NodeJS@9.4.0
#:package Aspire.Hosting.Python@9.4.0
#:package Aspire.Hosting.Azure.AppContainers@9.4.0
#:package Aspire.Hosting.Azure.AIFoundry@9.4.0-preview.1.25378.8
#:package Aspire.Hosting.Azure.Storage@9.4.0
#:package Aspire.Hosting.Docker@9.4.0-preview.1.25378.8
#:package CommunityToolkit.Aspire.Hosting.NodeJS.Extensions@9.7.0
#:package CommunityToolkit.Aspire.Hosting.Python.Extensions@9.7.0
#:property PublishAot=false

using System.Reflection.Metadata;
using Microsoft.Extensions.Hosting;

#endregion

#region config
Environment.SetEnvironmentVariable("ASPIRE_ALLOW_UNSECURED_TRANSPORT", "true");
Environment.SetEnvironmentVariable("ASPNETCORE_URLS", "http://localhost:5003");
Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
Environment.SetEnvironmentVariable("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL", "http://localhost:4317");
Environment.SetEnvironmentVariable("ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL", "http://localhost:5001");

Environment.SetEnvironmentVariable("Logging__LogLevel__Default", "Information");
Environment.SetEnvironmentVariable("Logging__LogLevel__Aspire.Hosting.Dcp", "Warning");
Environment.SetEnvironmentVariable("Logging__LogLevel__Microsoft.AspNetCore", "Warning");
#endregion

var builder = DistributedApplication.CreateBuilder(args);
var cae = builder.AddAzureContainerAppEnvironment("cae");
var dockerenv = builder.AddDockerComposeEnvironment("docker");
var modelName = "gpt-4.1";

// Configure Azure Services
var azureStorage = builder.AddAzureStorage("storage");

// Run as openai
var ai = builder.AddAzureAIFoundry("ai");

var embedding = ai.AddDeployment(
    name: "text-embedding",
    modelName: "text-embedding-3-small",
    modelVersion: "1",
    format: "OpenAI")
     .WithProperties(d =>
        {
            d.SkuCapacity = 20;
        });

var gpt = ai.AddDeployment(
    name: "gpt",
    modelName: modelName,
    modelVersion: "2025-04-14",
    format: "OpenAI")
     .WithProperties(d =>
        {
            d.SkuName = "GlobalStandard";
            d.SkuCapacity = 50;
        });

if (builder.Environment.IsDevelopment())
{
    azureStorage.RunAsEmulator(c =>
    {
        c.WithDataBindMount();
        c.WithLifetime(ContainerLifetime.Persistent);
    });
}

// Configure projects
var mcpServer =
    builder.AddProject("mcpserver", "./AccedeSimple.MCPServer/AccedeSimple.MCPServer.csproj")
        .WithReference(gpt)
        .WaitFor(ai);


var pythonApp =
    builder.AddUvApp("localguide", "./localguide", "main.py")
        .WithHttpEndpoint(env: "PORT")
        .WithEnvironment("AZURE_OPENAI_ENDPOINT", ai.Resource.AIFoundryApiEndpoint)
        .WithEnvironment("MODEL_NAME", modelName)
        .WithOtlpExporter()
        .WaitFor(ai)
        .PublishAsDockerFile();

var backend =
    builder
        .AddProject("backend", "./AccedeSimple.Service/AccedeSimple.Service.csproj")
        .WithReference(gpt)
        .WithReference(embedding)
        .WithReference(mcpServer)
        .WithReference(pythonApp)
        .WithReference(azureStorage.AddBlobs("uploads"))
        .WithEnvironment("MODEL_NAME", modelName)
        .WaitFor(ai);

builder.AddNpmApp("webui", "./webui")
    .WithNpmPackageInstallation()
    .WithHttpEndpoint(env: "PORT")
    .WithEnvironment("BACKEND_URL", backend.GetEndpoint("http"))
    .WithExternalHttpEndpoints()
    .WithOtlpExporter()
    .WaitFor(backend)
    .PublishAsDockerFile();

builder.Build().Run();
#pragma warning restore