#pragma warning disable
using System.Reflection.Metadata;
using Microsoft.Extensions.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Define parameters for Azure OpenAI
var azureOpenAIResource = builder.AddParameterFromConfiguration("AzureOpenAIResourceName", "AzureOpenAI:ResourceName");
var azureOpenAIResourceGroup = builder.AddParameterFromConfiguration("AzureOpenAIResourceGroup", "AzureOpenAI:ResourceGroup");
var azureOpenAIEndpoint = builder.AddParameterFromConfiguration("AzureOpenAIEndpoint", "AzureOpenAI:Endpoint");

// Get Azure sub config
var azureSubscriptionId = builder.AddParameterFromConfiguration("AzureSubscriptionId", "Azure:SubscriptionId", secret: true);
var azureResourceGroup = builder.AddParameterFromConfiguration("AzureResourceGroup", "Azure:ResourceGroup");
var azureLocation = builder.AddParameterFromConfiguration("AzureLocation", "Azure:Location");

var modelName = "gpt-4.1";


// Configure Azure Services
var azureStorage = builder.AddAzureStorage("storage");
var openai =
        builder.AddAzureOpenAI("openai")
//        .AsExisting(azureOpenAIResource, azureOpenAIResourceGroup)
        ;

var embedding = openai.AddDeployment(
    name: "text-embedding",
    modelName: "text-embedding-3-small",
    modelVersion: "1")
     .WithProperties(d =>
                {
                    d.SkuCapacity = 20;
                });

var gpt = openai.AddDeployment(
    name: "gpt",
    modelName: modelName,
    modelVersion: "2025-04-14")
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
        .WaitFor(openai);


var pythonApp =
    builder.AddUvApp("localguide", "../localguide", "main.py")
        .WithHttpEndpoint(env: "PORT", port: 8000, isProxied: false)
        .WithEnvironment("AZURE_OPENAI_ENDPOINT", azureOpenAIEndpoint)
        .WithEnvironment("MODEL_NAME", modelName)
        .WithOtlpExporter()
        .WaitFor(openai);

var azureAIFoundryProject = builder.AddParameterFromConfiguration("AzureAIFoundryProject", "AzureAIFoundry:Project");

var backend =
    builder
        .AddProject<Projects.AccedeSimple_Service>("backend")
        .WithReference(gpt)
        .WithReference(embedding)
        .WithReference(mcpServer)
        .WithReference(pythonApp)
        .WithReference(azureStorage.AddBlobs("uploads"))
        .WithEnvironment("MODEL_NAME", modelName)
        .WithEnvironment("AZURE_SUBSCRIPTION_ID", azureSubscriptionId)
        .WithEnvironment("AZURE_RESOURCE_GROUP", azureOpenAIResourceGroup)
        .WithEnvironment("AZURE_AI_FOUNDRY_PROJECT", azureAIFoundryProject)
        .WaitFor(openai);

builder.AddNpmApp("webui", "../webui")
    .WithNpmPackageInstallation()
    .WithHttpEndpoint(env: "PORT", port: 35_369, isProxied: false)
    .WithEnvironment("BACKEND_URL", backend.GetEndpoint("http"))
    .WithExternalHttpEndpoints()
    .WithOtlpExporter()
    .WaitFor(backend)
    .PublishAsDockerFile();

builder.Build().Run();
#pragma warning restore