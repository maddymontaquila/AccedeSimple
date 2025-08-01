#pragma warning disable
using System.Reflection.Metadata;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);
// var cae = builder.AddAzureContainerAppEnvironment("cae");
var modelName = "gpt-4.1";

// Configure Azure Services
var azureStorage = builder.AddAzureStorage("storage");
var blobs = azureStorage.AddBlobs("uploads");

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
    builder.AddProject<Projects.AccedeSimple_MCPServer>("mcpserver")
        .WithReference(gpt)
        .WaitFor(ai);


var pythonApp =
    builder.AddUvApp("localguide", "../localguide", "main.py")
        .WithHttpEndpoint(env: "PORT")
        .WithEnvironment("AZURE_OPENAI_ENDPOINT", ai.Resource.AIFoundryApiEndpoint)
        .WithEnvironment("MODEL_NAME", modelName)
        .WithOtlpExporter()
        .PublishAsDockerFile()
        .WaitFor(ai);

var backend =
    builder
        .AddProject<Projects.AccedeSimple_Service>("backend")
        .WithReference(gpt)
        .WithReference(embedding)
        .WithReference(mcpServer)
        .WithReference(pythonApp)
        .WithReference(blobs)
        .WithEnvironment("MODEL_NAME", modelName)
        .WaitFor(ai);

builder.AddNpmApp("webui", "../webui")
    .WithNpmPackageInstallation()
    .WithHttpEndpoint(env: "PORT")
    .WithEnvironment("BACKEND_URL", backend.GetEndpoint("http"))
    .WithExternalHttpEndpoints()
    .WithOtlpExporter()
    .WaitFor(backend)
    .PublishAsDockerFile();

builder.Build().Run();
#pragma warning restore