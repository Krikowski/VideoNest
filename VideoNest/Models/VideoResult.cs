using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;

namespace VideoNest.Models;

/// <summary>
/// Resultado do processamento de vídeo no MongoDB (compatível com ScanForge)
/// Implementa RF5-7: armazenamento de QRs, status e timestamps
/// </summary>
[BsonIgnoreExtraElements] // Correção: Ignora campos extras adicionados pelo ScanForge
public class VideoResult {
    /// <summary>
    /// ID único do vídeo (chave primária, gerado por Counter Pattern)
    /// </summary>
    [BsonId]
    [BsonRepresentation(BsonType.Int32)]
    public int VideoId { get; set; }

    /// <summary>
    /// Título do vídeo (obrigatório para RF1)
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// Descrição opcional
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Caminho do arquivo de vídeo (salvo em /app/uploads)
    /// </summary>
    public string? FilePath { get; set; }

    /// <summary>
    /// Status do processamento (RF6)
    /// </summary>
    public string Status { get; set; } = "Na Fila";

    /// <summary>
    /// Data de criação (UTC)
    /// </summary>
    public DateTime? CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Duração do vídeo em segundos (calculada pelo ScanForge)
    /// </summary>
    public int Duration { get; set; }

    /// <summary>
    /// Mensagem de erro (se aplicável)
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Lista de QR Codes detectados (RF5, RF7)
    /// </summary>
    public List<QRCodeResult> QRCodes { get; set; } = new List<QRCodeResult>();

    /// <summary>
    /// Última atualização do registro (compatibilidade com ScanForge)
    /// Correção: Propriedade adicionada para evitar erro de deserialização
    /// </summary>
    [BsonElement("LastUpdated")]
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Validação de entidade (usada em SaveVideoAsync)
    /// Verifica campos obrigatórios para RF1-2
    /// </summary>
    /// <param name="validationError">Mensagem de erro se inválido</param>
    /// <returns>True se válido, false se inválido</returns>
    public bool IsValid(out string validationError) {
        if (VideoId <= 0) {
            validationError = "VideoId deve ser maior que zero";
            return false;
        }

        if (string.IsNullOrWhiteSpace(Title)) {
            validationError = "Título é obrigatório";
            return false;
        }

        if (string.IsNullOrWhiteSpace(FilePath)) {
            validationError = "Caminho do arquivo é obrigatório";
            return false;
        }

        if (!VideoNest.Constants.VideoConstants.ValidStatuses.Contains(Status)) {
            validationError = $"Status inválido: {Status}. Status válidos: [{string.Join(", ", VideoNest.Constants.VideoConstants.ValidStatuses)}]";
            return false;
        }

        validationError = string.Empty;
        return true;
    }
}

/// <summary>
/// Resultado individual de QR Code (RF5, RF7)
/// </summary>
public class QRCodeResult {
    /// <summary>
    /// Conteúdo do QR Code (texto decodificado)
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// Timestamp em segundos (quando aparece no vídeo)
    /// </summary>
    public int Timestamp { get; set; }

    /// <summary>
    /// Validação de QR Code (usada em AddQRCodesAsync)
    /// </summary>
    /// <returns>True se válido (Content não nulo e Timestamp >= 0)</returns>
    public bool IsValid() {
        return !string.IsNullOrWhiteSpace(Content) && Timestamp >= 0;
    }
}