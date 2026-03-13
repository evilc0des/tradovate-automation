using System;
using System.IO;
using NinjaTraderTradovateBridge;

namespace NinjaTraderBridge.UnitTests;

public sealed class DedupStoreTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"dedup_{Guid.NewGuid():N}");
    private string TempPath(string name) => Path.Combine(_tempDir, name);

    public DedupStoreTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void New_signal_is_not_duplicate()
    {
        var store = new DedupStore(TempPath("ids.txt"), Helpers.NullLogger, new PersistenceHealthMonitor());
        Assert.False(store.IsDuplicate("abc-123"));
    }

    [Fact]
    public void Marked_signal_is_duplicate()
    {
        var store = new DedupStore(TempPath("ids.txt"), Helpers.NullLogger, new PersistenceHealthMonitor());
        store.MarkProcessed("abc-123");
        Assert.True(store.IsDuplicate("abc-123"));
    }

    [Fact]
    public void Different_signals_are_not_duplicates()
    {
        var store = new DedupStore(TempPath("ids.txt"), Helpers.NullLogger, new PersistenceHealthMonitor());
        store.MarkProcessed("aaa");
        Assert.False(store.IsDuplicate("bbb"));
    }

    [Fact]
    public void Signal_id_lookup_is_case_sensitive()
    {
        var store = new DedupStore(TempPath("ids.txt"), Helpers.NullLogger, new PersistenceHealthMonitor());
        store.MarkProcessed("SIG-001");
        Assert.False(store.IsDuplicate("sig-001"));
        Assert.True(store.IsDuplicate("SIG-001"));
    }

    [Fact]
    public void Ids_survive_process_restart()
    {
        var path = TempPath("ids_persist.txt");
        var health = new PersistenceHealthMonitor();

        var store1 = new DedupStore(path, Helpers.NullLogger, health);
        store1.MarkProcessed("persisted-id");

        // Simulate restart: new instance over same path.
        var store2 = new DedupStore(path, Helpers.NullLogger, new PersistenceHealthMonitor());
        Assert.True(store2.IsDuplicate("persisted-id"));
    }

    [Fact]
    public void Multiple_ids_persisted_and_restored()
    {
        var path = TempPath("multi.txt");

        var store1 = new DedupStore(path, Helpers.NullLogger, new PersistenceHealthMonitor());
        store1.MarkProcessed("id-1");
        store1.MarkProcessed("id-2");
        store1.MarkProcessed("id-3");

        var store2 = new DedupStore(path, Helpers.NullLogger, new PersistenceHealthMonitor());
        Assert.True(store2.IsDuplicate("id-1"));
        Assert.True(store2.IsDuplicate("id-2"));
        Assert.True(store2.IsDuplicate("id-3"));
        Assert.False(store2.IsDuplicate("id-4"));
    }

    [Fact]
    public void Corrupted_store_file_reports_critical_health()
    {
        var path = TempPath("locked.txt");
        File.WriteAllText(path, "some-signal-id\n");

        // Hold an exclusive lock so DedupStore.Load() cannot open the file,
        // forcing an IOException which should be reported as a critical issue.
        using var lockStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        var health = new PersistenceHealthMonitor();
        var _ = new DedupStore(path, Helpers.NullLogger, health);
        Assert.True(health.HasCriticalIssues);
    }
}
