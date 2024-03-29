﻿using System.Net.Http;
using Football.Bot;
using Football.Bot.Commands;
using Football.Bot.Commands.Core;
using Football.Bot.Extensions;
using Football.Bot.Models;
using Football.Bot.Services;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.Azure.KeyVault;
using Microsoft.Azure.Services.AppAuthentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureKeyVault;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Telegram.Bot;

[assembly: FunctionsStartup(typeof(Startup))]

namespace Football.Bot;

public class Startup : FunctionsStartup
{
    public override void Configure(IFunctionsHostBuilder builder)
    {
        var configuration = BuildConfiguration(builder.GetContext().ApplicationRootPath);
        builder.Services.AddSingleton(configuration);

        builder.Services.AddHttpClient();
        builder.Services.AddTransient<SchedulerProvider>();
        builder.Services.AddSingleton<CosmosDbClient>();
        builder.Services.AddTransient<CosmosClient>();

        builder.Services.AddTransient<CommandHandler>();
        builder.Services.AddTransient<ICommand, NextMatchCommand>();
        builder.Services.AddTransient<ICommand, StartCommand>();
        builder.Services.AddTransient<ICommand, HelpCommand>();
        builder.Services.AddTransient<ICommand, UnhandledCommand>();

        builder.Services.AddSingleton(_ =>
        {
            var cosmos = configuration.GetSection("Cosmos").Get<CosmosConfiguration>();

            var client = new CosmosClient(accountEndpoint: cosmos.Endpoint, authKeyOrResourceToken: cosmos.Token);
            // var properties = new ContainerProperties(id: cosmos.Container, partitionKeyPath: "/categoryId");

            var response = client.GetDatabase(cosmos.Database);
            var container = response.GetContainer(cosmos.Container);

            return container;
        });

        builder.Services.AddTransient(serviceProvider =>
        {
            var token = serviceProvider.GetRequiredService<TelegramConfiguration>().Token;
            var httpClient = serviceProvider.GetRequiredService<HttpClient>();

            var telegramClient = new TelegramBotClient(token, httpClient);

            return telegramClient;
        });


        builder.Services.AddConfigurationModel<TelegramConfiguration>(configuration, "Telegram");
        builder.Services.AddConfigurationModel<WarmupConfiguration>(configuration, "Warmup");
    }

    private static IConfiguration BuildConfiguration(string applicationRootPath)
    {
        var envConfiguration = GetEnvConfigurationRoot();

        var builder = new ConfigurationBuilder()
            .SetBasePath(applicationRootPath)
            .AddJsonFile("local.settings.json", optional: true, reloadOnChange: true)
            .AddJsonFile("settings.json", optional: true, reloadOnChange: true)
            .AddEnvironmentVariables()
            .AddUserSecrets<Startup>();


        var keyVaultEndpoint = envConfiguration.GetValue<string>("KeyVaultEndpoint");
        if (keyVaultEndpoint != null)
        {
            var keyVaultClient = GetKeyVaultClient();

            builder = builder
                .AddAzureKeyVault(keyVaultEndpoint, keyVaultClient, new DefaultKeyVaultSecretManager());
        }

        return builder.Build();
    }

    private static IConfigurationRoot GetEnvConfigurationRoot() => new ConfigurationBuilder()
        .AddEnvironmentVariables()
        .Build();

    private static KeyVaultClient GetKeyVaultClient()
    {
        var azureServiceTokenProvider = new AzureServiceTokenProvider();

        // Create a new Key Vault client with Managed Identity authentication
        var callback = new KeyVaultClient.AuthenticationCallback(azureServiceTokenProvider.KeyVaultTokenCallback);
        var keyVaultClient = new KeyVaultClient(callback);
        return keyVaultClient;
    }
}
