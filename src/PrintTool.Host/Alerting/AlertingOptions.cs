using System.Text.Json;

namespace PrintTool.Host.Alerting;

/// <summary>
/// Credenciais SMTP usadas para enviar os alertas de conectividade. O Gmail funciona bem como
/// relay (smtp.gmail.com:587) desde que <see cref="SenderAppPassword"/> seja uma "Senha de app"
/// gerada na conta Google — a senha normal da conta não funciona para SMTP.
/// </summary>
public sealed record SmtpSettings
{
    public string Host { get; init; } = "smtp.gmail.com";
    public int Port { get; init; } = 587;
    public string SenderEmail { get; init; } = string.Empty;
    public string SenderAppPassword { get; init; } = string.Empty;
    public string RecipientEmail { get; init; } = string.Empty;

    /// <summary>Verdadeiro quando os três campos necessários para mandar e-mail foram preenchidos.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(SenderEmail) &&
        !string.IsNullOrWhiteSpace(SenderAppPassword) &&
        !string.IsNullOrWhiteSpace(RecipientEmail);
}

/// <summary>Janela de horário em que o Host está "em funcionamento" num dia da semana.</summary>
public sealed record BusinessHoursWindow(TimeOnly Start, TimeOnly End)
{
    public bool Contains(TimeOnly time) => time >= Start && time < End;
}

/// <summary>
/// Configuração do monitoramento de conectividade: para onde mandar alerta (<see cref="Smtp"/>),
/// de quanto em quanto tempo checar (<see cref="CheckIntervalMinutes"/>), e em quais horários a
/// checagem deve valer (<see cref="BusinessHours"/>) — fora desses horários uma máquina desligada
/// é esperado, não um problema, então o monitoramento simplesmente pausa.
/// </summary>
public sealed class AlertingOptions
{
    public SmtpSettings Smtp { get; init; } = new();

    public int CheckIntervalMinutes { get; init; } = 5;

    /// <summary>
    /// Um item por dia da semana. <c>null</c> significa "fechado o dia inteiro" (não monitora
    /// nesse dia) — o padrão já vem assim para sábado e domingo.
    /// </summary>
    public Dictionary<DayOfWeek, BusinessHoursWindow?> BusinessHours { get; init; } = DefaultBusinessHours();

    public static AlertingOptions LoadOrCreate(string path)
    {
        if (!File.Exists(path))
        {
            var created = new AlertingOptions();
            created.Save(path);
            return created;
        }

        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AlertingOptions>(json) ?? new AlertingOptions();
    }

    public void Save(string path)
    {
        string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Verdadeiro se <paramref name="now"/> cai dentro do horário comercial configurado para o
    /// dia da semana correspondente. Recebe o instante como parâmetro (em vez de ler o relógio
    /// do sistema internamente) para ficar testável com horários fixos.
    /// </summary>
    public bool IsWithinBusinessHours(DateTimeOffset now)
    {
        if (!BusinessHours.TryGetValue(now.DayOfWeek, out BusinessHoursWindow? window) || window is null)
        {
            return false;
        }

        return window.Contains(TimeOnly.FromTimeSpan(now.TimeOfDay));
    }

    private static Dictionary<DayOfWeek, BusinessHoursWindow?> DefaultBusinessHours()
    {
        var weekday = new BusinessHoursWindow(new TimeOnly(8, 0), new TimeOnly(18, 0));
        return new Dictionary<DayOfWeek, BusinessHoursWindow?>
        {
            [DayOfWeek.Monday] = weekday,
            [DayOfWeek.Tuesday] = weekday,
            [DayOfWeek.Wednesday] = weekday,
            [DayOfWeek.Thursday] = weekday,
            [DayOfWeek.Friday] = weekday,
            [DayOfWeek.Saturday] = null,
            [DayOfWeek.Sunday] = null,
        };
    }
}
