using Backend.Hardware.Gnss;
using Backend.Hardware.Imu;
using Backend.Hardware.Camera;
using Backend.Hardware.Bluetooth;
using Backend.Hardware.LoRa;
using Backend.Hubs;
using Backend.Storage;
using Backend.GnssSystem;
using Backend.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure host options to not stop on background service exceptions
builder.Services.Configure<HostOptions>(hostOptions =>
{
    hostOptions.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.Ignore;
});

// Configure Serilog
var logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "logs", "gnss-system-.log");
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "{Timestamp:HH:mm:ss.fff}: {Level:u3}: {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(logPath,
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        shared: true,
        flushToDiskInterval: TimeSpan.FromSeconds(10),
        fileSizeLimitBytes: 104_857_600, // 100 MB per file
        rollOnFileSizeLimit: true,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

builder.Host.UseSerilog();

// Keep the inline formatter as fallback for any remaining console logging
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

// Add Camera services
builder.Services.AddSingleton<CameraInitializer>();
builder.Services.AddHostedService<CameraService>();

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

SystemConfiguration.CorrectionsMode operatingMode;

// Check if running interactively (has console input available)
bool isInteractive = Environment.UserInteractive && !Console.IsInputRedirected;

char? userInput = null;
Task<char>? inputTask = null;

if (isInteractive)
{
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

    inputTask = Task.Run(() =>
    {
        var keyInfo = Console.ReadKey(true); // true = don't display the key
        return keyInfo.KeyChar;
    });
}

if (isInteractive && inputTask != null)
{
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
}
else
{
    // Not running interactively (service mode) - use existing configuration or default
    operatingMode = existingRtkMode ?? SystemConfiguration.CorrectionsMode.Disabled;
    Console.WriteLine($"Running in service mode. Using {operatingMode} mode from configuration.");
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
var cameraInitializer = app.Services.GetRequiredService<CameraInitializer>();

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

logger.LogInformation("Initializing Camera hardware...");
try 
{
    // Run camera initialization in background to prevent blocking startup
    _ = Task.Run(async () =>
    {
        try
        {
            var cameraInitialized = await cameraInitializer.InitializeAsync();
            if (!cameraInitialized)
            {
                logger.LogWarning("Camera initialization failed - camera service will run in disconnected mode");
            }
            else
            {
                logger.LogInformation("Camera initialization completed successfully");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception during Camera initialization - camera service will run in disconnected mode");
        }
    });
    
    logger.LogInformation("Camera initialization started in background");
}
catch (Exception ex)
{
    logger.LogError(ex, "Exception starting Camera initialization");
}

// Configure the HTTP request pipeline.
// Removed Swagger for cleaner console output

// Use CORS (before other middleware)
app.UseCors("AllowFrontend");

// Configure custom static file path
var frontendPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "frontend");

// Ensure frontend directory exists
if (!Directory.Exists(frontendPath))
{
    Directory.CreateDirectory(frontendPath);
    logger.LogInformation("Created frontend directory at: {FrontendPath}", frontendPath);
}

// Enable default files from custom path
app.UseDefaultFiles(new DefaultFilesOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(frontendPath)
});

// Serve static files from custom frontend directory
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(frontendPath)
});

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

// Fallback for SPA routing - serve index.html for any unmatched routes
app.MapFallbackToFile("index.html");

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