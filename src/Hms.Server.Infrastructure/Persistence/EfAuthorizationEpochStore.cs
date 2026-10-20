using Hms.Server.Application.Realtime;
using Hms.Server.Domain.Devices;
using System.Data;
using System.Data.Common;
using System.Globalization;
using Microsoft.EntityFrameworkCore;

namespace Hms.Server.Infrastructure.Persistence;

public sealed class EfAuthorizationEpochStore(ApplicationDbContext db) : IAuthorizationEpochStore
{
    public long Current(DeviceId deviceId) =>
        db.DeviceAuthorizationEpochs.AsNoTracking()
            .Where(x => x.DeviceId == deviceId.Value)
            .Select(x => (long?)x.Epoch)
            .SingleOrDefault() ?? 0;

    public long Advance(DeviceId deviceId)
    {
        if (IsPostgres)
        {
            return AdvancePostgres(deviceId);
        }

        var record = db.DeviceAuthorizationEpochs.Find(deviceId.Value);
        if (record is null)
        {
            record = new DeviceAuthorizationEpochRecord { DeviceId = deviceId.Value, Epoch = 1 };
            db.DeviceAuthorizationEpochs.Add(record);
        }
        else
        {
            record.Epoch = checked(record.Epoch + 1);
        }

        db.SaveChanges();
        return record.Epoch;
    }

    public void AdvanceMany(IReadOnlyCollection<DeviceId> deviceIds)
    {
        // Distinct: one upsert statement must not touch the same row twice.
        var ids = deviceIds.Select(x => x.Value).Distinct().ToArray();
        if (ids.Length == 0)
            return;

        if (IsPostgres)
        {
            AdvanceManyPostgres(ids);
            return;
        }

        var records = db.DeviceAuthorizationEpochs
            .Where(x => ids.Contains(x.DeviceId))
            .ToDictionary(x => x.DeviceId);
        foreach (var id in ids)
        {
            if (records.TryGetValue(id, out var record))
                record.Epoch = checked(record.Epoch + 1);
            else
                db.DeviceAuthorizationEpochs.Add(new DeviceAuthorizationEpochRecord { DeviceId = id, Epoch = 1 });
        }

        db.SaveChanges();
    }

    private bool IsPostgres => string.Equals(
        db.Database.ProviderName,
        "Npgsql.EntityFrameworkCore.PostgreSQL",
        StringComparison.Ordinal);

    private long AdvancePostgres(DeviceId deviceId) => WithCommand(command =>
    {
        // A single PostgreSQL upsert makes epoch advancement atomic across
        // API replicas; the tracked Find/SaveChanges path is retained for the
        // in-memory development provider used by tests.
        command.CommandText = """
            INSERT INTO "device_authorization_epochs" ("DeviceId", "Epoch")
            VALUES (@device_id, 1)
            ON CONFLICT ("DeviceId") DO UPDATE
            SET "Epoch" = "device_authorization_epochs"."Epoch" + 1
            RETURNING "Epoch";
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "device_id";
        parameter.DbType = DbType.Guid;
        parameter.Value = deviceId.Value;
        command.Parameters.Add(parameter);

        var result = command.ExecuteScalar();
        if (result is null || result is DBNull)
        {
            throw new InvalidOperationException("AUTHORIZATION_EPOCH_ADVANCE_FAILED");
        }

        return Convert.ToInt64(result, CultureInfo.InvariantCulture);
    });

    // All devices of one role change in one statement (uuid[] parameter). Rows are
    // taken in id order, so two concurrent role updates lock shared rows in the same
    // order and cannot deadlock.
    private void AdvanceManyPostgres(Guid[] ids) => WithCommand(command =>
    {
        command.CommandText = """
            INSERT INTO "device_authorization_epochs" ("DeviceId", "Epoch")
            SELECT device_id, 1 FROM unnest(@device_ids) AS device_id ORDER BY device_id
            ON CONFLICT ("DeviceId") DO UPDATE
            SET "Epoch" = "device_authorization_epochs"."Epoch" + 1;
            """;
        var parameter = command.CreateParameter();
        parameter.ParameterName = "device_ids";
        parameter.Value = ids;
        command.Parameters.Add(parameter);
        return command.ExecuteNonQuery();
    });

    private T WithCommand<T>(Func<DbCommand, T> run)
    {
        var connection = db.Database.GetDbConnection();
        var shouldClose = connection.State == ConnectionState.Closed;
        if (shouldClose)
        {
            connection.Open();
        }

        try
        {
            using var command = connection.CreateCommand();
            return run(command);
        }
        finally
        {
            if (shouldClose)
            {
                connection.Close();
            }
        }
    }
}
