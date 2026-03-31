using qubic_doge_stats.Components;
using qubic_doge_stats.Endpoints;
using qubic_doge_stats.Infrastructure;
using qubic_doge_stats.Services;
using qubic_doge_stats.Workers;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddMudServices();

// LiteDB
builder.Services.AddSingleton<LiteDbContext>();

// DogeStats HTTP client
builder.Services.AddHttpClient<DogeStatsClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["DogeStats:ApiUrl"] ?? "https://doge-stats.qubic.org/dispatcher.json");
    client.Timeout = TimeSpan.FromSeconds(30);
});

// QubicRpc HTTP client
builder.Services.AddHttpClient<QubicRpcClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["QubicRpc:BaseUrl"] ?? "https://rpc.qubic.org/");
    client.Timeout = TimeSpan.FromSeconds(10);
});

builder.Services.AddHostedService<DogeStatsPollingWorker>();

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
});

builder.Services.AddResponseCompression(options => options.EnableForHttps = true);

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseResponseCompression();
}

app.UseStaticFiles();
app.UseAntiforgery();
app.UseCors();

app.MapApiEndpoints();
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(qubic_doge_stats.Client._Imports).Assembly);

app.Run();
