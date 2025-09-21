using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace VideoNest.Models;

/// <summary>
/// Contador MongoDB para geração de IDs sequenciais atômicos
/// Implementa Counter Pattern para evitar race conditions em uploads concorrentes
/// Coleção: "counters", documento: { Id: "video_counter", Sequence: 0 }
/// </summary>
public class VideoCounter {
    /// <summary>
    /// Nome único do contador (ex: "video_counter")
    /// </summary>
    [BsonId]
    [BsonElement("Id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Valor sequencial atual (incrementado atomicamente)
    /// </summary>
    [BsonElement("Sequence")]
    public int Sequence { get; set; } = 0;
}