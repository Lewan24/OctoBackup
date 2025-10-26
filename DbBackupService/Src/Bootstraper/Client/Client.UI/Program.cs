using Blazored.LocalStorage;
using Client.UI.Data.HttpHandlers;
using Client.UI.Data.Interfaces;
using Client.UI.Data.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddScoped<HttpTokenAuthHeaderHandler>();
builder.Services
    .AddHttpClient("token", client => { client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress); })
    .AddHttpMessageHandler<HttpTokenAuthHeaderHandler>();

builder.Services.AddMudServices();

builder.Services.AddBlazoredLocalStorage();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();

builder.Services.AddOptions();
builder.Services.AddAuthorizationCore();

builder.Services.AddSingleton<IAuthHttpClientService, AuthHttpClientHttpClientService>();
builder.Services.AddScoped<AuthStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(s => s.GetRequiredService<AuthStateProvider>());

builder.Services.AddHttpClient("", client => { client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress); });

builder.Services.AddScoped<TokenHttpClientService>();
builder.Services.AddScoped<AdministrationHttpService>();
builder.Services.AddScoped<BackupsHttpClientService>();
builder.Services.AddScoped<ProfileService>();

builder.Logging.AddFilter((category, level)
    => level >= LogLevel.Warning ||
       category?.Contains("System.Net.Http.HttpClient") != true);

await builder.Build().RunAsync();