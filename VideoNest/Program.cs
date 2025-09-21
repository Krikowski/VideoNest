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
using System.Threading.Tasks;

namespace VideoNest {
    public class Program {
        public static void Main(string[] args) {
            // Configuração Serilog
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
                    .Build())
                .CreateLogger();

            var builder = WebApplication.CreateBuilder(args);
            builder.Host.UseSerilog();

            // Controllers e Swagger
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

            // SignalR
            builder.Services.AddSignalR();

            // MongoDB
            builder.Services.AddSingleton<IMongoClient>(sp =>
                new MongoClient(
                    builder.Configuration.GetConnectionString("MongoDB") ??
                    builder.Configuration["MongoDB:ConnectionString"] ??
                    "mongodb://admin:admin@mongodb_hackathon:27017"));

            builder.Services.AddSingleton<IMongoDatabase>(sp =>
                sp.GetRequiredService<IMongoClient>().GetDatabase(
                    builder.Configuration["MongoDB:DatabaseName"] ?? "Hackathon_FIAP"));

            // Redis
            builder.Services.AddStackExchangeRedisCache(options => {
                options.Configuration = builder.Configuration["Redis:ConnectionString"] ?? "redis_hackathon:6379";
                options.InstanceName = "VideoNest:";
            });

            // Repositórios e Serviços
            builder.Services.AddScoped<IVideoRepository, VideoRepository>();
            builder.Services.AddScoped<IVideoService, VideoService>();

            // ✅ REGISTRO CORRIGIDO: Interface atualizada com métodos corretos
            builder.Services.AddSingleton<IRabbitMQPublisher, RabbitMQPublisher>();

            // Kestrel (100MB para vídeos)
            builder.WebHost.ConfigureKestrel(options => {
                options.Limits.MaxRequestBodySize = 100_000_000;
                options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(2);
            });

            builder.Services.Configure<FormOptions>(options => {
                options.MultipartBodyLengthLimit = 100_000_000;
                options.ValueLengthLimit = int.MaxValue;
                options.MultipartHeadersLengthLimit = int.MaxValue;
            });

            var app = builder.Build();

            // Mapear SignalR Hub
            app.MapHub<VideoHub>("/videoHub");

            // Swagger (Development/Demo)
            if (app.Environment.IsDevelopment() || app.Environment.EnvironmentName == "Demo") {
                app.UseSwagger();
                app.UseSwaggerUI(c => {
                    c.SwaggerEndpoint("/swagger/v1/swagger.json", "VideoNest API v1");
                    c.RoutePrefix = string.Empty;
                });
            }

            // Error handling
            if (!app.Environment.IsDevelopment()) {
                app.UseExceptionHandler("/Error");
            } else {
                app.UseDeveloperExceptionPage();
            }

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();
            app.UseAuthorization();

            // Prometheus
            app.UseMetricServer();
            app.UseHttpMetrics();

            app.MapControllers();

            // ✅ INICIALIZAÇÃO CORRIGIDA: Interface agora tem DeclareInfrastructureAsync
            var publisher = app.Services.GetRequiredService<IRabbitMQPublisher>();
            var logger = app.Services.GetRequiredService<ILogger<Program>>();

            // Tenta inicializar RabbitMQ com retry
            bool rabbitMQRdy = false;
            int maxRetries = 5;
            int retryDelay = 5; // segundos

            for (int attempt = 1; attempt <= maxRetries; attempt++) {
                try {
                    logger.LogInformation("🔄 Tentativa {Attempt}/{MaxRetries} - Inicializando RabbitMQ...", attempt, maxRetries);
                    publisher.DeclareInfrastructureAsync().GetAwaiter().GetResult();
                    rabbitMQRdy = true;
                    logger.LogInformation("✅ Infraestrutura RabbitMQ inicializada pelo VideoNest");
                    break;
                } catch (Exception ex) {
                    logger.LogWarning(ex, "⚠️ Tentativa {Attempt}/{MaxRetries} falhou - Aguardando {Delay}s...", attempt, maxRetries, retryDelay);
                    if (attempt < maxRetries) {
                        Task.Delay(retryDelay * 1000).GetAwaiter().GetResult();
                    } else {
                        logger.LogWarning(ex, "⚠️ RabbitMQ falhou após {MaxRetries} tentativas - continuando sem infraestrutura...", maxRetries);
                    }
                }
            }

            if (!rabbitMQRdy) {
                logger.LogWarning("⚠️ VideoNest rodando SEM RabbitMQ - uploads não serão processados!");
            }

            var startupLogger = app.Services.GetRequiredService<ILogger<Program>>();
            startupLogger.LogInformation("🚀 VideoNest API v1.0 iniciada em {Environment} - {Timestamp} (RabbitMQ: {RabbitMQRdy})",
                app.Environment.EnvironmentName, DateTime.UtcNow, rabbitMQRdy ? "OK" : "FAILED");

            app.Run();
        }
    }
}