// C:/Estudos/Hackaton_FIAP/VideoNest/VideoNest/Program.cs

using VideoNest.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http.Features;
using VideoNest.Repositories;
using VideoNest.Services;
using Microsoft.OpenApi.Models;
using VideoNest.Filters;
using VideoNest.Service;
using MongoDB.Driver;

namespace VideoNest {
    public class Program {
        public static void Main(string[] args) {
            var builder = WebApplication.CreateBuilder(args);

            // Adicionar serviços ao contêiner
            builder.Services.AddControllers();
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // Configurar o DbContext para PostgreSQL
            builder.Services.AddDbContext<VideoDbContext>(options =>
                options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

            // Adicionado: MongoDB client singleton
            builder.Services.AddSingleton<IMongoClient>(sp =>
                new MongoClient(builder.Configuration["MongoDB:ConnectionString"]));
            builder.Services.AddSingleton(sp =>
                sp.GetRequiredService<IMongoClient>().GetDatabase(builder.Configuration["MongoDB:DatabaseName"]));

            // Registrar o repositório
            builder.Services.AddScoped<IVideoRepository, VideoRepository>();
            builder.Services.AddScoped<VideoService>();
            builder.Services.AddSingleton<RabbitMQPublisher>();

            // Configurar limite do Kestrel
            builder.WebHost.ConfigureKestrel(options => {
                options.Limits.MaxRequestBodySize = 100_000_000; // 100 MB
            });

            // Configurar limite do multipart/form-data
            builder.Services.Configure<FormOptions>(options => {
                options.MultipartBodyLengthLimit = 100_000_000; // 100 MB
            });

            builder.Services.AddSwaggerGen(c => {
                c.SwaggerDoc("v1", new OpenApiInfo { Title = "VideoNest API", Version = "v1" });

                // Adicionar suporte para multipart/form-data
                c.OperationFilter<FileUploadOperationFilter>();
            });

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment()) {
                app.UseSwagger();
                app.UseSwaggerUI();
            }

            app.UseHttpsRedirection();
            app.UseAuthorization();
            app.MapControllers();
            app.Run();
        }
    }
}