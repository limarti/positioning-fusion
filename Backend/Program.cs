using Backend.Hubs;
using Backend.Hardware.Imu;
using Backend.Hardware.Gnss;
using Backend.System;
using Backend.Storage;

var builder = WebApplication.CreateBuilder(args);

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

// Add logging services
builder.Services.AddSingleton<DataFileWriter>(provider =>
    new DataFileWriter("imu.txt", provider.GetRequiredService<ILogger<DataFileWriter>>()));
builder.Services.AddHostedService<DataFileWriter>(provider =>
    provider.GetRequiredService<DataFileWriter>());

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
