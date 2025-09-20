using Microsoft.AspNetCore.Http.Features;
using Microsoft.OpenApi.Models;
using MongoDB.Driver;
using StackExchange.Redis;
using VideoNest.Filters;
using VideoNest.Hubs;
using VideoNest.Repositories;
using VideoNest.Services; 
using Prometheus;
using Serilog;
using Microsoft.Extensions.Caching.Distributed;

namespace VideoNest {
    /// <summary>
    /// Program principal da VideoNest API.
    /// Configura DI, middleware, e integrações: MongoDB, Redis, RabbitMQ, SignalR, Prometheus.
    /// </summary>
    /// <remarks>
    /// Stack básico (.NET 8, Controllers, Docker-ready).
    /// Upload configuration (100MB limit).
    /// MongoDB NoSQL + Redis Cache.
    /// SignalR real-time + Serilog logs.
    /// Prometheus metrics + Swagger docs.
    /// DLQ RabbitMQ, Clean Code DI patterns.
    /// </remarks>
    public class Program {
        /// <summary>
        /// Entry point da aplicação (.NET 8 Minimal API).
        /// Configura Serilog, DI container, middleware pipeline.
        /// </summary>
        /// <param name="args">Argumentos de linha de comando.</param>
        public static void Main(string[] args) {
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
                    .Build())
                .CreateLogger();

            var builder = WebApplication.CreateBuilder(args);

            builder.Host.UseSerilog();

            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c => {
                c.SwaggerDoc("v1", new OpenApiInfo {
                    Title = "VideoNest API",
                    Version = "v1",
                    Description = "API para upload e processamento de vídeos com detecção de QR Codes\n\n**Hackathon FIAP 7NETT**\n- RF1: Upload .mp4/.avi via API\n- RF2: Fila assíncrona RabbitMQ\n- RF6: Status processamento\n- RF7: Resultados QR Codes + timestamps\n\n**Bônus:** SignalR, MongoDB, Redis, Prometheus",
                    Contact = new OpenApiContact {
                        Name = "VideoNest Team",
                        Email = "team@videonest.com"
                    }
                });
            });

            builder.Services.AddSignalR();

            builder.Services.AddSingleton<IMongoClient>(sp =>
                new MongoClient(
                    builder.Configuration.GetConnectionString("MongoDB") ??
                    builder.Configuration["MongoDB:ConnectionString"] ??
                    "mongodb://admin:admin@localhost:27017"));

            builder.Services.AddSingleton<IMongoDatabase>(sp =>
                sp.GetRequiredService<IMongoClient>().GetDatabase(
                    builder.Configuration["MongoDB:DatabaseName"] ?? "Hackathon_FIAP"));

            builder.Services.AddStackExchangeRedisCache(options => {
                options.Configuration = builder.Configuration["Redis:ConnectionString"] ?? "redis:6379";
                options.InstanceName = "VideoNest:";  // Prefixo para evitar colisão de chaves
            });

            builder.Services.AddScoped<IVideoRepository, VideoRepository>();
            builder.Services.AddScoped<IVideoService, VideoService>();

            builder.Services.AddScoped<IRabbitMQPublisher, RabbitMQPublisher>();

            builder.WebHost.ConfigureKestrel(options => {
                options.Limits.MaxRequestBodySize = 100_000_000;  // 100MB para vídeos
                options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);  // Uploads longos
            });

            builder.Services.Configure<FormOptions>(options => {
                options.MultipartBodyLengthLimit = 100_000_000;  // 100MB multipart/form-data
                options.ValueLengthLimit = int.MaxValue;
                options.MultipartHeadersLengthLimit = int.MaxValue;
            });

            var app = builder.Build();

            app.MapHub<VideoHub>("/videoHub");

            if (app.Environment.IsDevelopment() ||
                app.Environment.EnvironmentName == "Demo") {

                app.UseSwagger();
                app.UseSwaggerUI(c => {
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "VideoNest API v1");
                    c.RoutePrefix = string.Empty; // Swagger na raiz (/)

                    // ✅ CORREÇÃO: Chamar métodos, não atribuir propriedades
                    c.DisplayRequestDuration();  // Método, não propriedade
                    c.EnableDeepLinking();       // Método, não propriedade
                });
            }

            // ✅ Error Handling: SEMPRE em Production
            if (!app.Environment.IsDevelopment()) {
                app.UseExceptionHandler("/Error");
            } else {
                // Opcional: Developer exception page em Development
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();
            app.UseAuthorization();

            app.UseMetricServer();        
            app.UseHttpMetrics();     

            app.MapControllers();

            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("🚀 VideoNest API v1.0 iniciada em {Environment} - {Timestamp}",
                app.Environment.EnvironmentName, DateTime.UtcNow);

            app.Run();
        }
    }
}