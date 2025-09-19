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
using VideoNest.Service;

namespace VideoNest {
    public class Program {
        public static void Main(string[] args) {
            // Configuração Serilog (BONUS)
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
                    .Build())
                .CreateLogger();

            var builder = WebApplication.CreateBuilder(args);

            // Usar Serilog
            builder.Host.UseSerilog();

            // Controllers e Swagger (BONUS)
            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen(c => {
                c.SwaggerDoc("v1", new OpenApiInfo {
                    Title = "VideoNest API",
                    Version = "v1",
                    Description = "API para upload e processamento de vídeos com detecção de QR Codes"
                });
                c.OperationFilter<FileUploadOperationFilter>();
            });

            // SignalR (FASE 04)
            builder.Services.AddSignalR();

            // MongoDB (FASE 03)
            builder.Services.AddSingleton<IMongoClient>(sp =>
                new MongoClient(builder.Configuration.GetConnectionString("MongoDB") ??
                    builder.Configuration["MongoDB:ConnectionString"]));
            builder.Services.AddSingleton(sp =>
                sp.GetRequiredService<IMongoClient>().GetDatabase(
                    builder.Configuration["MongoDB:DatabaseName"] ?? "Hackathon_FIAP"));

            // Redis Cache (FASE 03)
            builder.Services.AddStackExchangeRedisCache(options => {
                options.Configuration = builder.Configuration["Redis:ConnectionString"] ?? "redis:6379";
                options.InstanceName = "VideoNest:";
            });

            // Services
            builder.Services.AddScoped<IVideoRepository, VideoRepository>();
            builder.Services.AddScoped<IVideoService, VideoService>();
            builder.Services.AddSingleton<IRabbitMQPublisher, RabbitMQPublisher>();

            // Configurações de arquivo (FASE 02)
            builder.WebHost.ConfigureKestrel(options => {
                options.Limits.MaxRequestBodySize = 100_000_000;  // 100MB
            });

            builder.Services.Configure<FormOptions>(options => {
                options.MultipartBodyLengthLimit = 100_000_000; // 100MB
            });

            var app = builder.Build();

            // SignalR Hub (FASE 04)
            app.MapHub<VideoHub>("/videoHub");

            // Swagger em desenvolvimento (BONUS)
            if (app.Environment.IsDevelopment()) {
                app.UseSwagger();
                app.UseSwaggerUI(c => {
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "VideoNest API v1");
                    c.RoutePrefix = string.Empty; // Swagger na raiz
                });
            }

            app.UseHttpsRedirection();
            app.UseAuthorization();

            // Metrics Prometheus (BONUS)
            app.UseMetricServer();
            app.UseHttpMetrics();

            app.MapControllers();

            // Log de inicialização
            var logger = app.Services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("VideoNest API iniciada em {Environment}", app.Environment.EnvironmentName);

            app.Run();
        }
    }
}