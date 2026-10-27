namespace HwMon.Core.Sensors;

/// <summary>
/// Fonte de sensores. Cada provedor é independente e pode falhar sem derrubar os demais.
/// </summary>
public interface ISensorProvider : IDisposable
{
    /// <summary>Identificador estável (usado em CSV e alertas).</summary>
    string Id { get; }

    string DisplayName { get; }

    /// <summary>Indica se o provedor conseguiu inicializar.</summary>
    bool IsAvailable { get; }

    /// <summary>Motivo da indisponibilidade ou aviso operacional.</summary>
    string? StatusMessage { get; }

    /// <summary>Prepara o provedor (abrir dispositivos, carregar bibliotecas).</summary>
    void Initialize();

    /// <summary>Lê todos os sensores. Pode retornar lista vazia.</summary>
    IReadOnlyList<SensorReading> Read();
}
