using Microsoft.AspNetCore.Http;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Collections.Generic;
using System.Linq;

namespace VideoNest.Filters {
    /// <summary>
    /// Filtro do Swagger para documentar endpoints de upload de arquivo
    /// </summary>
    public class FileUploadOperationFilter : IOperationFilter {
        public void Apply(Microsoft.OpenApi.Models.OpenApiOperation operation,
                         Swashbuckle.AspNetCore.SwaggerGen.OperationFilterContext context) {
            // Detecta se é endpoint de upload (tem IFormFile)
            var isFileUpload = context.MethodInfo.GetParameters()
                .Any(p => p.ParameterType == typeof(IFormFile));

            // Se não tem arquivo, não faz nada
            if (!isFileUpload)
                return;

            // Configura o RequestBody como multipart/form-data
            operation.RequestBody = new OpenApiRequestBody {
                Content = new Dictionary<string, OpenApiMediaType> {
                    ["multipart/form-data"] = new OpenApiMediaType {
                        Schema = new OpenApiSchema {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema> {
                                // Campo de arquivo obrigatório
                                ["file"] = new OpenApiSchema {
                                    Type = "string",
                                    Format = "binary"
                                },
                                // Campos de texto opcionais
                                ["Title"] = new OpenApiSchema {
                                    Type = "string"
                                },
                                ["Description"] = new OpenApiSchema {
                                    Type = "string"
                                }
                            },
                            Required = new HashSet<string> { "file" }
                        }
                    }
                }
            };
        }
    }
}