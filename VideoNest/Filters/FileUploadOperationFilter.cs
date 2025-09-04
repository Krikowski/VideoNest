using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Linq;

namespace VideoNest.Filters {
    public class FileUploadOperationFilter : IOperationFilter {
        public void Apply(OpenApiOperation operation, OperationFilterContext context) {
            var isFileUpload = context.MethodInfo.GetParameters()
                .Any(p => p.ParameterType == typeof(IFormFile));

            if (isFileUpload) {
                operation.RequestBody = new OpenApiRequestBody {
                    Content = new Dictionary<string, OpenApiMediaType> {
                        ["multipart/form-data"] = new OpenApiMediaType {
                            Schema = new OpenApiSchema {
                                Type = "object",
                                Properties = new Dictionary<string, OpenApiSchema> {
                                    ["file"] = new OpenApiSchema {
                                        Type = "string",
                                        Format = "binary"
                                    },
                                    ["Title"] = new OpenApiSchema { Type = "string" },
                                    ["Description"] = new OpenApiSchema { Type = "string" },
                                    ["Duration"] = new OpenApiSchema { Type = "integer", Format = "int32" },
                                    ["CustomFileName"] = new OpenApiSchema { Type = "string", Nullable = true }
                                },
                                Required = new HashSet<string> { "file", "Title", "Description", "Duration" }
                            }
                        }
                    }
                };
            }
        }
    }
}