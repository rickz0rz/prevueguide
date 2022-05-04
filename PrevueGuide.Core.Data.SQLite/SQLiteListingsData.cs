using System.Data.SQLite;
using PrevueGuide.Core.Model;

namespace PrevueGuide.Core.Data.SQLite;

public class SQLiteListingsData : IListingsData
{
    private const string ChannelLineupTableName = "ChannelLineUp";
    private const string ChannelLineupIdColumnName = "ID";
    private const string ChannelLineupCallSignColumnName = "CallSign";
    private const string ChannelLineupChannelNumberColumnName = "ChannelNumber";
    private const string ChannelListingsTableName = "ChannelListings";
    private const string ChannelListingsChannelIdColumnName = "ChannelID";
    private const string ChannelListingsTitleColumnName = "Title";
    private const string ChannelListingsDescriptionColumnName = "Description";
    private const string ChannelListingsStartTimeColumnName = "StartTime";
    private const string ChannelListingsEndTimeColumnName = "EndTime";

    private readonly SQLiteConnection _sqLiteConnection;

    private bool VerifyTableExists(string tableName)
    {
        using var command = _sqLiteConnection.CreateCommand();
        command.CommandText = $"SELECT name FROM sqlite_master WHERE type='table' AND name='{tableName}'";
        using var dataReader = command.ExecuteReader();
        return dataReader.HasRows;
    }

    private void GenerateChannelLineUpTable()
    {
        using var command = _sqLiteConnection.CreateCommand();
        command.CommandText = $"CREATE TABLE {ChannelLineupTableName} " +
                              $"({ChannelLineupIdColumnName} Text, " +
                              $"{ChannelLineupCallSignColumnName} TEXT, " +
                              $"{ChannelLineupChannelNumberColumnName} TEXT, " +
                              $"PRIMARY KEY ({ChannelLineupIdColumnName}))";
        _ = command.ExecuteNonQuery();
    }
    
    private void GenerateChannelListingsTable()
    {
        using var command = _sqLiteConnection.CreateCommand();
        command.CommandText = $"CREATE TABLE {ChannelListingsTableName} " +
                              $"({ChannelListingsChannelIdColumnName} REFERENCES {ChannelLineupTableName}({ChannelLineupIdColumnName}), " +
                              $"{ChannelListingsTitleColumnName} TEXT, " +
                              $"{ChannelListingsDescriptionColumnName} TEXT, " +
                              $"{ChannelListingsStartTimeColumnName} TEXT, " +
                              $"{ChannelListingsEndTimeColumnName} TEXT)";
        _ = command.ExecuteNonQuery();
    }
    
    public SQLiteListingsData(string filename)
    {
        // Make sure our tables exist.
        _sqLiteConnection = new SQLiteConnection($"Data Source={filename};Version=3;New=True;Compress=True;");
        _sqLiteConnection.Open();

        // Validate and generate (if missing) tables
        if (!VerifyTableExists(ChannelLineupTableName))
        {
            Console.WriteLine($"Warning: Table \"{ChannelLineupTableName}\" doesn't exist, creating.");
            GenerateChannelLineUpTable();
        }
        
        if (!VerifyTableExists(ChannelListingsTableName))
        {
            Console.WriteLine($"Warning: Table \"{ChannelListingsTableName}\" doesn't exist, creating.");
            GenerateChannelListingsTable();
        }
        
        // Generate indexes if missing?
    }

    public void Dispose()
    {
        _sqLiteConnection.Dispose();
    }

    public async Task AddChannelToLineup(string id, string channelNumber, string callSign)
    {
        // Speed this up by inserting multiple rows?
        await using var command = _sqLiteConnection.CreateCommand();
        command.CommandText =
            $"INSERT INTO {ChannelLineupTableName} ({ChannelLineupIdColumnName}, {ChannelLineupCallSignColumnName}, {ChannelLineupChannelNumberColumnName})" +
            $" VALUES (@{ChannelLineupIdColumnName}, @{ChannelLineupCallSignColumnName}, @{ChannelLineupChannelNumberColumnName})";
        command.Parameters.AddWithValue($"@{ChannelLineupIdColumnName}", id);
        command.Parameters.AddWithValue($"@{ChannelLineupCallSignColumnName}", callSign);
        command.Parameters.AddWithValue($"@{ChannelLineupChannelNumberColumnName}", channelNumber);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IEnumerable<(string id, string channelNumber, string callSign)>> GetChannelLineup()
    {
        await using var command = _sqLiteConnection.CreateCommand();
        command.CommandText = $"SELECT * FROM {ChannelLineupTableName}";
        await using var reader = command.ExecuteReader();

        var results = new List<(string id, string channelNumber, string callSign)>();

        var idColumnIndex = reader.GetOrdinal(ChannelLineupIdColumnName);
        var callSignColumnIndex = reader.GetOrdinal(ChannelLineupCallSignColumnName);
        var channelNumberColumnIndex = reader.GetOrdinal(ChannelLineupChannelNumberColumnName);
        
        while (await reader.ReadAsync())
        {
            results.Add(
                (reader.GetString(idColumnIndex),
                reader.GetString(channelNumberColumnIndex),
                reader.GetString(callSignColumnIndex)));
        }

        return results;
    }

    public async Task AddChannelListing(string channelId, string title, string description,
        DateTime startTime, DateTime endTime)
    {
        // Speed this up by inserting multiple rows?
        await using var command = _sqLiteConnection.CreateCommand();
        command.CommandText =
            $"INSERT INTO {ChannelListingsTableName} ({ChannelListingsChannelIdColumnName}, {ChannelListingsTitleColumnName}," +
            $" {ChannelListingsDescriptionColumnName}, {ChannelListingsStartTimeColumnName}, {ChannelListingsEndTimeColumnName}) " +
            $"VALUES (@{ChannelListingsChannelIdColumnName}, @{ChannelListingsTitleColumnName}, @Description, @StartTime, @EndTime)";
        command.Parameters.AddWithValue($"@{ChannelListingsChannelIdColumnName}", channelId);
        command.Parameters.AddWithValue($"@{ChannelListingsTitleColumnName}", title);
        command.Parameters.AddWithValue($"@{ChannelListingsDescriptionColumnName}", description);
        command.Parameters.AddWithValue($"@{ChannelListingsStartTimeColumnName}", startTime.ToString("o"));
        command.Parameters.AddWithValue($"@{ChannelListingsEndTimeColumnName}", endTime.ToString("o"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IEnumerable<Listing>> GetChannelListings(DateTime startTime, DateTime endTime)
    {
        await using var command = _sqLiteConnection.CreateCommand();
        command.CommandText = $"SELECT * FROM {ChannelListingsTableName} " +
                              $"WHERE {ChannelListingsStartTimeColumnName} >= '{startTime.ToUniversalTime():O}' AND " +
                              $"{ChannelListingsEndTimeColumnName} <= '{endTime.ToUniversalTime():O}'";
        await using var reader = command.ExecuteReader();

        var results = new List<Listing>();

        var channelIdColumnIndex = reader.GetOrdinal(ChannelListingsChannelIdColumnName);
        var titleColumnIndex = reader.GetOrdinal(ChannelListingsTitleColumnName);
        var descriptionColumnIndex = reader.GetOrdinal(ChannelListingsDescriptionColumnName);
        var startTimeColumnIndex = reader.GetOrdinal(ChannelListingsStartTimeColumnName);
        var endTimeColumnIndex = reader.GetOrdinal(ChannelListingsEndTimeColumnName);

        while (await reader.ReadAsync())
        {
            results.Add(new Listing
            {
                ChannelId = reader.GetString(channelIdColumnIndex),
                Title = reader.GetString(titleColumnIndex),
                Description = reader.GetString(descriptionColumnIndex),
                StartTime = reader.GetString(startTimeColumnIndex),
                EndTime = reader.GetString(endTimeColumnIndex)
            });
        }

        return results;
    }
}
