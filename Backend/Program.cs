using Backend.Hardware.Gnss;
using Backend.Hardware.Imu;
using Backend.Hardware.Bluetooth;
using Backend.Hardware.LoRa;
using Backend.Hubs;
using Backend.Storage;
using Backend.GnssSystem;
using Backend.Configuration;
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

// Load existing operating mode
var existingRtkMode = SystemConfiguration.LoadRtkMode();

// Prompt user for operating mode
Console.WriteLine("Select operating mode:");
Console.WriteLine("(B) Base Station Mode");
Console.WriteLine("(R) Rover Mode");
Console.WriteLine("(D) Disabled");
if (existingRtkMode.HasValue)
{
    Console.WriteLine($"Current mode: {existingRtkMode.Value}");
}
Console.Write("Press B, R, or D: ");

char? userInput = null;
var inputTask = Task.Run(() =>
{
    var keyInfo = Console.ReadKey(true); // true = don't display the key
    return keyInfo.KeyChar;
});

for (int i = 10; i > 0; i--)
{
    if (inputTask.IsCompleted)
    {
        userInput = inputTask.Result;
        Console.WriteLine(userInput); // Display the pressed key
        break;
    }
    
    Console.Write($"\rPress B, R, or D - timeout in {i} seconds: ");
    await Task.Delay(1000);
}

SystemConfiguration.CorrectionsMode operatingMode;
if (!inputTask.IsCompleted)
{
    // Timeout occurred - use existing mode if available, otherwise default to Disabled
    operatingMode = existingRtkMode ?? SystemConfiguration.CorrectionsMode.Disabled;
    Console.WriteLine($"\rTimeout reached. Using {operatingMode} mode.              ");
}
else
{
    operatingMode = userInput?.ToString().ToUpper() switch
    {
        "B" => SystemConfiguration.CorrectionsMode.Send,
        "R" => SystemConfiguration.CorrectionsMode.Receive,
        "D" => SystemConfiguration.CorrectionsMode.Disabled,
        _ => existingRtkMode ?? SystemConfiguration.CorrectionsMode.Disabled
    };
}

// Save the selected operating mode
SystemConfiguration.SaveOperatingMode(operatingMode);

// Update the corrections operation
SystemConfiguration.CorrectionsOperation = operatingMode;

Console.WriteLine($"Operating mode selected: {operatingMode}");
Console.WriteLine();

// Initialize hardware
var logger = app.Services.GetRequiredService<ILogger<Program>>();
var imuInitializer = app.Services.GetRequiredService<ImuInitializer>();
var gnssInitializer = app.Services.GetRequiredService<GnssInitializer>();

logger.LogInformation("Initializing IM19 IMU hardware...");
try 
{
    var imuInitialized = await imuInitializer.InitializeAsync();
    if (!imuInitialized)
    {
        logger.LogWarning("IM19 IMU initialization failed - continuing without IMU");
    }
    else
    {
        logger.LogInformation("IM19 IMU initialization completed successfully");
    }
}
catch (Exception ex)
{
    logger.LogError(ex, "Exception during IM19 IMU initialization");
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