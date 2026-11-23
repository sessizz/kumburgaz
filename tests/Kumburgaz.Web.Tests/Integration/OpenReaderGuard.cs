using System.Collections.Concurrent;
using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Kumburgaz.Web.Tests.Integration;

/// <summary>
/// SQLite, okuyucu açıkken aynı bağlantıda ikinci komuta izin verir; Npgsql (canlı) vermez
/// ("A command is already in progress"). Bu kesici aynı kısıtı testlerde uygular: açık okuyuculu bağlantıda
/// başlayan komut hata alır. Uygulamanın yuttuğu hatalar da kaçmasın diye ihlaller ayrıca saklanır.
/// </summary>
public sealed class OpenReaderGuard : DbCommandInterceptor
{
    private readonly ConditionalWeakTable<DbConnection, ConcurrentQueue<DbDataReader>> _readers = [];
    private readonly ConcurrentQueue<string> _violations = new();

    public IReadOnlyCollection<string> Violations => _violations;

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        => Check(command, result);

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        => new(Check(command, result));

    public override InterceptionResult<object> ScalarExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result)
        => Check(command, result);

    public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<object> result, CancellationToken cancellationToken = default)
        => new(Check(command, result));

    public override InterceptionResult<int> NonQueryExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result)
        => Check(command, result);

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        => new(Check(command, result));

    public override DbDataReader ReaderExecuted(DbCommand command, CommandExecutedEventData eventData, DbDataReader result)
        => Track(command, result);

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, DbDataReader result, CancellationToken cancellationToken = default)
        => new(Track(command, result));

    private DbDataReader Track(DbCommand command, DbDataReader reader)
    {
        _readers.GetValue(command.Connection!, static _ => new ConcurrentQueue<DbDataReader>()).Enqueue(reader);
        return reader;
    }

    private T Check<T>(DbCommand command, T result)
    {
        if (!_readers.TryGetValue(command.Connection!, out var readers))
        {
            return result;
        }

        // Bağlantıyı aynı anda tek bağlam kullanır; kapanmış okuyucular buradan düşer.
        while (readers.TryPeek(out var reader))
        {
            if (!reader.IsClosed)
            {
                _violations.Enqueue(command.CommandText);
                throw new InvalidOperationException($"A command is already in progress (açık okuyucu): {command.CommandText}");
            }

            readers.TryDequeue(out _);
        }

        return result;
    }
}
