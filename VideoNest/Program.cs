using Microsoft.AspNetCore.Http.Features;
using Microsoft.OpenApi.Models;
using MongoDB.Driver;
using VideoNest.Filters;
using VideoNest.Hubs;
using VideoNest.Repositories;
using VideoNest.Services;
using Prometheus;
using Serilog;

namespace VideoNest
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
                    .Build())
                .CreateLogger();

            try
            {
                var builder = WebApplication.CreateBuilder(args);
                builder.Host.UseSerilog();

                builder.Services.AddControllers();
                builder.Services.AddEndpointsApiExplorer();

                builder.Services.AddSwaggerGen(c =>
                {
                    c.SwaggerDoc("v1", new OpenApiInfo
                    {
                        Title = "VideoNest API",
                        Version = "v1",
                        Description = "API para upload e processamento de vídeos com detecção de QR Codes",
                        Contact = new OpenApiContact
                        {
                            Name = "VideoNest Team",
                            Email = "team@videonest.com"
                        }
                    });
                });

                builder.Services.AddSignalR();

                builder.Services.AddSingleton<IMongoClient>(_ =>
                    new MongoClient(
                        builder.Configuration.GetConnectionString("MongoDB")
                        ?? builder.Configuration["MongoDB:ConnectionString"]
                        ?? "mongodb://admin:admin@mongodb_hackathon:27017"));

                builder.Services.AddSingleton<IMongoDatabase>(sp =>
                    sp.GetRequiredService<IMongoClient>().GetDatabase(
                        builder.Configuration["MongoDB:DatabaseName"] ?? "Hackathon_FIAP"));

                builder.Services.AddStackExchangeRedisCache(options =>
                {
                    options.Configuration = builder.Configuration["Redis:ConnectionString"] ?? "redis_hackathon:6379";
                    options.InstanceName = "VideoNest:";
                });

                builder.Services.AddScoped<IVideoRepository, VideoRepository>();
                builder.Services.AddScoped<IVideoService, VideoService>();
                builder.Services.AddSingleton<IRabbitMQPublisher, RabbitMQPublisher>();

                builder.WebHost.ConfigureKestrel(options =>
                {
                    options.Limits.MaxRequestBodySize = 100_000_000;
                    options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
                });

                builder.Services.Configure<FormOptions>(options =>
                {
                    options.MultipartBodyLengthLimit = 100_000_000;
                    options.ValueLengthLimit = int.MaxValue;
                    options.MultipartHeadersLengthLimit = int.MaxValue;
                });

                var app = builder.Build();

                if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "Demo")
                {
                    app.UseSwagger();
                    app.UseSwaggerUI(c =>
                    {
                        c.SwaggerEndpoint("/swagger/v1/swagger.json", "VideoNest API v1");
                        c.RoutePrefix = "swagger";
                    });
                }

                if (app.Environment.IsDevelopment())
                {
                    app.UseDeveloperExceptionPage();
                }
                else
                {
                    app.UseExceptionHandler("/Error");
                }

                app.UseStaticFiles();
                app.UseRouting();
                app.UseAuthorization();

                app.UseMetricServer();
                app.UseHttpMetrics();

                app.MapGet("/", () => Results.Redirect("/swagger/index.html"));

                app.MapHub<VideoHub>("/videoHub");
                app.MapControllers();

                var publisher = app.Services.GetRequiredService<IRabbitMQPublisher>();
                var logger = app.Services.GetRequiredService<ILogger<Program>>();

                var rabbitMQRdy = false;
                const int maxRetries = 5;
                const int retryDelaySeconds = 5;

                for (var attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        logger.LogInformation("Tentativa {Attempt}/{MaxRetries} - Inicializando RabbitMQ...", attempt, maxRetries);

                        publisher.DeclareInfrastructureAsync()
                            .GetAwaiter()
                            .GetResult();

                        rabbitMQRdy = true;

                        logger.LogInformation("Infraestrutura RabbitMQ inicializada pelo VideoNest");
                        break;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(
                            ex,
                            "Tentativa {Attempt}/{MaxRetries} falhou ao inicializar RabbitMQ.",
                            attempt,
                            maxRetries);

                        if (attempt < maxRetries)
                        {
                            Task.Delay(TimeSpan.FromSeconds(retryDelaySeconds))
                                .GetAwaiter()
                                .GetResult();
                        }
                    }
                }

                if (!rabbitMQRdy)
                {
                    logger.LogWarning("VideoNest rodando SEM RabbitMQ. Uploads podem não ser processados.");
                }

                logger.LogInformation(
                    "VideoNest API iniciada em {Environment} - {Timestamp} - RabbitMQ: {RabbitMQStatus}",
                    app.Environment.EnvironmentName,
                    DateTime.UtcNow,
                    rabbitMQRdy ? "OK" : "FAILED");

                app.Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "A aplicação falhou ao iniciar.");
                throw;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}