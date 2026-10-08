using PrintTool.Host.Alerting;
using Xunit;

namespace PrintTool.Host.Tests.Alerting;

public class AlertingOptionsTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "PrintToolTests_alerting_" + Guid.NewGuid());

    private string ConfigPath => Path.Combine(_tempDir, "alerting.json");

    public AlertingOptionsTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void LoadOrCreate_FileMissing_CreatesFileWithWeekdayBusinessHoursAndWeekendClosed()
    {
        AlertingOptions options = AlertingOptions.LoadOrCreate(ConfigPath);

        Assert.True(File.Exists(ConfigPath));
        Assert.Equal(5, options.CheckIntervalMinutes);
        Assert.False(options.Smtp.IsConfigured);
        Assert.NotNull(options.BusinessHours[DayOfWeek.Monday]);
        Assert.NotNull(options.BusinessHours[DayOfWeek.Friday]);
        Assert.Null(options.BusinessHours[DayOfWeek.Saturday]);
        Assert.Null(options.BusinessHours[DayOfWeek.Sunday]);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsSmtpAndBusinessHours()
    {
        var original = new AlertingOptions
        {
            CheckIntervalMinutes = 10,
            Smtp = new SmtpSettings
            {
                SenderEmail = "alertas@gmail.com",
                SenderAppPassword = "senha-de-app",
                RecipientEmail = "admin@empresa.com",
            },
        };
        original.Save(ConfigPath);

        AlertingOptions loaded = AlertingOptions.LoadOrCreate(ConfigPath);

        Assert.Equal(10, loaded.CheckIntervalMinutes);
        Assert.True(loaded.Smtp.IsConfigured);
        Assert.Equal("admin@empresa.com", loaded.Smtp.RecipientEmail);
    }

    [Theory]
    [InlineData("2026-10-12", "10:00", true)]  // segunda, dentro do horário
    [InlineData("2026-10-12", "07:59", false)] // segunda, antes de abrir
    [InlineData("2026-10-12", "18:00", false)] // segunda, no instante de fechar (exclusivo)
    [InlineData("2026-10-12", "17:59", true)]  // segunda, um minuto antes de fechar
    [InlineData("2026-10-17", "10:00", false)] // sábado, dia fechado
    [InlineData("2026-10-18", "10:00", false)] // domingo, dia fechado
    public void IsWithinBusinessHours_DefaultSchedule_MatchesExpectation(string date, string time, bool expected)
    {
        var options = new AlertingOptions();
        var now = new DateTimeOffset(DateOnly.Parse(date).ToDateTime(TimeOnly.Parse(time)), TimeSpan.Zero);

        Assert.Equal(expected, options.IsWithinBusinessHours(now));
    }
}
