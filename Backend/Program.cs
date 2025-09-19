using Backend.Hardware.Gnss;
using Backend.Hardware.Imu;
using Backend.Hardware.Bluetooth;
using Backend.Hardware.LoRa;
using Backend.Hubs;
using Backend.Storage;
using Backend.GnssSystem;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

var builder = WebApplication.CreateBuilder(args);




builder.Logging.ClearProviders();
builder.Logging.AddConsole(options =>
{
    options.FormatterName = "inline";
});
builder.Services.AddSingleton<ConsoleFormatter, InlineFormatter>();



// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add SignalR
builder.Services.AddSignalR();

// Add background services
builder.Services.AddHostedService<SystemMonitoringService>();

// Add IMU services
builder.Services.AddSingleton<ImuInitializer>();
builder.Services.AddHostedService<ImuService>();

// Add GNSS services
builder.Services.AddSingleton<GnssInitializer>();
builder.Services.AddHostedService<GnssService>();

// Add Bluetooth services
builder.Services.AddHostedService<BluetoothStreamingService>();

// Add LoRa services
builder.Services.AddSingleton<LoRaService>();
builder.Services.AddHostedService<LoRaService>(provider => provider.GetRequiredService<LoRaService>());

// Add file logging status service
builder.Services.AddHostedService<FileLoggingStatusService>();

// Add CORS for frontend
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Initialize hardware
var logger = app.Services.GetRequiredService<ILogger<Program>>();
var imuInitializer = app.Services.GetRequiredService<ImuInitializer>();
var gnssInitializer = app.Services.GetRequiredService<GnssInitializer>();

logger.LogInformation("Initializing IM19 IMU hardware...");
var imuInitialized = await imuInitializer.InitializeAsync();
if (!imuInitialized)
{
    logger.LogWarning("IM19 IMU initialization failed - continuing without IMU");
}

logger.LogInformation("Initializing GNSS hardware...");
var gnssInitialized = await gnssInitializer.InitializeAsync();
if (!gnssInitialized)
{
    logger.LogWarning("GNSS initialization failed - continuing without GNSS");
}

// Configure the HTTP request pipeline.
// Removed Swagger for cleaner console output

// Use CORS (before other middleware)
app.UseCors("AllowFrontend");

// Disable HTTPS redirection for development
// app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast")
.WithOpenApi();

// Map SignalR hub
app.MapHub<DataHub>("/datahub");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

// Local record/class *inside Program.cs*:
sealed class InlineFormatter : ConsoleFormatter
{
    public InlineFormatter() : base("inline") { }

    public override void Write<TState>(
        in LogEntry<TState> logEntry,
        IExternalScopeProvider? scopeProvider,
        TextWriter textWriter)
    {
        var msg = logEntry.Formatter?.Invoke(logEntry.State, logEntry.Exception);
        if (string.IsNullOrEmpty(msg)) return;

        var level = logEntry.LogLevel switch
        {
            LogLevel.Trace => "trace",
            LogLevel.Debug => "dbug",
            LogLevel.Information => "info",
            LogLevel.Warning => "warn",
            LogLevel.Error => "fail",
            LogLevel.Critical => "crit",
            _ => "info"
        };

        textWriter.Write(DateTime.Now.ToString("HH:mm:ss.fff"));
        textWriter.Write(": ");
        textWriter.Write(level);
        textWriter.Write(": ");
        textWriter.WriteLine(msg);
    }
}