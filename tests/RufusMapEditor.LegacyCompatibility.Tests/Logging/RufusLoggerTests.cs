using RufusMapEditor.LegacyCompatibility.Logging;

namespace RufusMapEditor.LegacyCompatibility.Tests.Logging;

public sealed class RufusLoggerTests
{
    [Fact]
    public void Logs_INFO_OK_WARN_ERROR_DEBUG_levels()
    {
        var log = new RufusLogger(maxEntries: 100);
        log.Debug("d");
        log.Info("i");
        log.Ok("o");
        log.Warn("w");
        log.Error("e");

        var snap = log.Snapshot();
        Assert.Equal(5, snap.Count);
        Assert.Equal(RufusLogLevel.Debug, snap[0].Level);
        Assert.Equal(RufusLogLevel.Info, snap[1].Level);
        Assert.Equal(RufusLogLevel.Ok, snap[2].Level);
        Assert.Equal(RufusLogLevel.Warn, snap[3].Level);
        Assert.Equal(RufusLogLevel.Error, snap[4].Level);
        Assert.Equal("DEBUG", snap[0].LevelLabel);
        Assert.Equal("OK", snap[2].LevelLabel);
    }

    [Fact]
    public void Entries_are_in_chronological_order()
    {
        var log = new RufusLogger();
        log.Info("first");
        Thread.Sleep(5);
        log.Info("second");
        Thread.Sleep(5);
        log.Ok("third");

        var snap = log.Snapshot();
        Assert.True(snap[0].Timestamp <= snap[1].Timestamp);
        Assert.True(snap[1].Timestamp <= snap[2].Timestamp);
        Assert.Equal("first", snap[0].Message);
        Assert.Equal("third", snap[2].Message);
    }

    [Fact]
    public void Max_entries_trims_oldest()
    {
        var log = new RufusLogger(maxEntries: 3);
        log.Info("a");
        log.Info("b");
        log.Info("c");
        log.Info("d");
        log.Info("e");

        var snap = log.Snapshot();
        Assert.Equal(3, snap.Count);
        Assert.Equal("c", snap[0].Message);
        Assert.Equal("d", snap[1].Message);
        Assert.Equal("e", snap[2].Message);
    }

    [Fact]
    public void Thread_safety_basic_parallel_writes()
    {
        var log = new RufusLogger(maxEntries: 10_000);
        const int perThread = 200;
        var threads = Enumerable.Range(0, 8)
            .Select(i => new Thread(() =>
            {
                for (var n = 0; n < perThread; n++)
                    log.Info($"t{i}-{n}");
            }))
            .ToArray();

        foreach (var t in threads) t.Start();
        foreach (var t in threads) t.Join();

        Assert.Equal(8 * perThread, log.Count);
        Assert.Equal(8 * perThread, log.Snapshot().Count);
    }

    [Theory]
    [InlineData("password=secret123", "***")]
    [InlineData("Password: hunter2", "***")]
    [InlineData("Pwd=abc;Server=x", "***")]
    [InlineData("SFTP password=xyz host=1.2.3.4", "***")]
    [InlineData("PasswordProtectedBase64=AAAA", "***")]
    public void Sanitizer_redacts_secrets(string input, string mustContain)
    {
        var sanitized = LogMessageSanitizer.Sanitize(input);
        Assert.Contains(mustContain, sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("secret123", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hunter2", sanitized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("xyz", sanitized, StringComparison.Ordinal);
        Assert.DoesNotContain("AAAA", sanitized, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitizer_allows_host_port_user_paths()
    {
        var msg = "SFTP conectando a 169.58.162.70:22 como ubuntu";
        var sanitized = LogMessageSanitizer.Sanitize(msg);
        Assert.Equal(msg, sanitized);
        Assert.Contains("169.58.162.70", sanitized);
        Assert.Contains("ubuntu", sanitized);
    }

    [Fact]
    public void Logger_applies_sanitization_on_write()
    {
        var log = new RufusLogger();
        log.Info("Conectando password=SuperSecret Host=db.local");
        var entry = log.Snapshot()[0];
        Assert.DoesNotContain("SuperSecret", entry.Message);
        Assert.Contains("***", entry.Message);
        Assert.Contains("Host=db.local", entry.Message);
    }

    [Fact]
    public void ExportText_format_is_utf8_friendly_brackets()
    {
        var log = new RufusLogger();
        log.Info("Mapa 30057 cargado");
        log.Ok("Conexión BD correcta");

        var text = log.ExportText();
        var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
        Assert.Equal(2, lines.Length);
        Assert.Matches(@"^\[\d{2}:\d{2}:\d{2}\] \[INFO\] Mapa 30057 cargado$", lines[0]);
        Assert.Matches(@"^\[\d{2}:\d{2}:\d{2}\] \[OK\] Conexión BD correcta$", lines[1]);
    }

    [Fact]
    public void FormatDisplay_matches_console_layout()
    {
        var entry = new RufusLogEntry(
            new DateTimeOffset(2026, 8, 23, 20, 55, 2, TimeSpan.FromHours(2)),
            RufusLogLevel.Info,
            "Mapa 30057 cargado");
        Assert.Equal("20:55:02  INFO   Mapa 30057 cargado", entry.FormatDisplay());
    }

    [Fact]
    public void EntryAdded_event_fires()
    {
        var log = new RufusLogger();
        RufusLogEntry? got = null;
        log.EntryAdded += (_, e) => got = e;
        log.Warn("attention");
        Assert.NotNull(got);
        Assert.Equal(RufusLogLevel.Warn, got!.Level);
        Assert.Equal("attention", got.Message);
    }

    [Fact]
    public void Clear_empties_and_raises_event()
    {
        var log = new RufusLogger();
        log.Info("x");
        var cleared = false;
        log.Cleared += (_, _) => cleared = true;
        log.Clear();
        Assert.Equal(0, log.Count);
        Assert.True(cleared);
    }
}
