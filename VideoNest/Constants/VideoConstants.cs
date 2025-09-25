using System.Collections.Generic;

namespace VideoNest.Constants;

/// <summary>
/// Constantes de validação para o domínio de vídeos
/// Garante consistência nos status e configurações do sistema
/// </summary>
public static class VideoConstants {
    /// <summary>
    /// Status válidos para o processamento de vídeos (RF6)
    /// Usado para validação em UpdateStatusAsync
    /// </summary>
    public static readonly IReadOnlyList<string> ValidStatuses = new List<string>
    {
        "Na Fila",
        "Processando",
        "Concluído",
        "Erro"
    };

    /// <summary>
    /// Extensões de arquivo permitidas para upload (RF1)
    /// </summary>
    public static readonly IReadOnlyList<string> AllowedExtensions = new List<string>
    {
        ".mp4",
        ".avi",
        ".mov",
        ".mkv"
    };

    /// <summary>
    /// Tamanho máximo de arquivo em bytes (100MB)
    /// </summary>
    public const long MaxFileSizeBytes = 104_857_600;

    /// <summary>
    /// Nome da coleção de contadores no MongoDB
    /// </summary>
    public const string CountersCollection = "counters";

}