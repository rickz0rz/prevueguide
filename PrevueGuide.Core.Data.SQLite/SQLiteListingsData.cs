using System.Data.SQLite;
using PrevueGuide.Core.Model;

namespace PrevueGuide.Core.Data.SQLite;

public class SQLiteListingsData : IListingsData
{
    // TODO: Store ratings, closed captioning, stereo...
    private const string ChannelLineupTableName = "ChannelLineUp";
    private const string ChannelLineupIdColumnName = "ID";
    private const string ChannelLineupCallSignColumnName = "CallSign";
    private const string ChannelLineupChannelNumberColumnName = "ChannelNumber";
    private const string ChannelListingsTableName = "ChannelListings";
    private const string ChannelListingsChannelIdColumnName = "ChannelID";
    private const string ChannelListingsTitleColumnName = "Title";
    private const string ChannelListingsBlockColumnName = "Block";
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
                              $"{ChannelListingsBlockColumnName} INTEGER, " +
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
            $"INSERT OR IGNORE INTO {ChannelLineupTableName} ({ChannelLineupIdColumnName}, {ChannelLineupCallSignColumnName}, {ChannelLineupChannelNumberColumnName})" +
            $" VALUES (@{ChannelLineupIdColumnName}, @{ChannelLineupCallSignColumnName}, @{ChannelLineupChannelNumberColumnName})";
        command.Parameters.AddWithValue($"@{ChannelLineupIdColumnName}", id);
        command.Parameters.AddWithValue($"@{ChannelLineupCallSignColumnName}", callSign);
        command.Parameters.AddWithValue($"@{ChannelLineupChannelNumberColumnName}", channelNumber);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IEnumerable<LineUpEntry>> GetChannelLineup()
    {
        await using var command = _sqLiteConnection.CreateCommand();
        command.CommandText = $"SELECT * FROM {ChannelLineupTableName}";
        await using var reader = command.ExecuteReader();

        var results = new List<LineUpEntry>();

        var idColumnIndex = reader.GetOrdinal(ChannelLineupIdColumnName);
        var callSignColumnIndex = reader.GetOrdinal(ChannelLineupCallSignColumnName);
        var channelNumberColumnIndex = reader.GetOrdinal(ChannelLineupChannelNumberColumnName);

        while (await reader.ReadAsync())
        {
            results.Add(
                new LineUpEntry
                {
                    Id = reader.GetString(idColumnIndex),
                    ChannelNumber = reader.GetString(channelNumberColumnIndex),
                    CallSign = reader.GetString(callSignColumnIndex)
                });
        }

        return results;
    }

    public async Task AddChannelListing(List<(string channelId, string title,
        string description, DateTime startTime, DateTime endTime)> listings)
    {
        await using var command = _sqLiteConnection.CreateCommand();
        var valuesList = new List<string>();

        for (var i = 0; i < listings.Count; i++)
        {
            valuesList.Add($"(@{ChannelListingsChannelIdColumnName}{i}, @{ChannelListingsTitleColumnName}{i}," +
                           $"@{ChannelListingsBlockColumnName}{i}, @{ChannelListingsDescriptionColumnName}{i}, " +
                           $"@{ChannelListingsStartTimeColumnName}{i}, @{ChannelListingsEndTimeColumnName}{i})");

            var listing = listings.ElementAt(i);

            var block = Utilities.Time.CalculateBlockNumber(listing.startTime);

            command.Parameters.AddWithValue($"@{ChannelListingsChannelIdColumnName}{i}", listing.channelId);
            command.Parameters.AddWithValue($"@{ChannelListingsTitleColumnName}{i}", listing.title);
            command.Parameters.AddWithValue($"@{ChannelListingsBlockColumnName}{i}", block);
            command.Parameters.AddWithValue($"@{ChannelListingsDescriptionColumnName}{i}", listing.description);
            command.Parameters.AddWithValue($"@{ChannelListingsStartTimeColumnName}{i}", listing.startTime.ToString("o"));
            command.Parameters.AddWithValue($"@{ChannelListingsEndTimeColumnName}{i}", listing.endTime.ToString("o"));
        }

        command.CommandText =
            $"INSERT OR IGNORE INTO {ChannelListingsTableName} ({ChannelListingsChannelIdColumnName}, {ChannelListingsTitleColumnName}, " +
            $"{ChannelListingsBlockColumnName}, " +
            $"{ChannelListingsDescriptionColumnName}, {ChannelListingsStartTimeColumnName}, {ChannelListingsEndTimeColumnName}) " +
            $"VALUES {string.Join(",", valuesList)}";

        await command.ExecuteNonQueryAsync();
    }

    public async Task<IEnumerable<Listing>> GetChannelListings(DateTime startTime, DateTime endTime)
    {
        await using var command = _sqLiteConnection.CreateCommand();

        var utcStartTime = startTime.ToUniversalTime().ToString("O");
        var utcEndTime = endTime.ToUniversalTime().ToString("O");

        command.CommandText = "SELECT * FROM ChannelListings WHERE " +
                              // Start time is in the past, end time is in the block
                              $"({ChannelListingsStartTimeColumnName} < '{utcStartTime}' AND " +
                              $"{ChannelListingsEndTimeColumnName} > '{utcStartTime}' AND " +
                              $"{ChannelListingsEndTimeColumnName} <= '{utcEndTime}') " +
                              "OR " +
                              // Start time is within the block, end time is within the block
                              $"({ChannelListingsStartTimeColumnName} >= '{utcStartTime}' AND " +
                              $"{ChannelListingsStartTimeColumnName} < '{utcEndTime}' AND " +
                              $"{ChannelListingsEndTimeColumnName} > '{utcStartTime}' AND " +
                              $"{ChannelListingsEndTimeColumnName} <= '{utcEndTime}') " +
                              "OR " +
                              // Start time is within the block, end time is after the block
                              $"({ChannelListingsStartTimeColumnName} >= '{utcStartTime}' AND " +
                              $"{ChannelListingsStartTimeColumnName} < '{utcEndTime}' AND " +
                              $"{ChannelListingsEndTimeColumnName} > '{utcEndTime}') " +
                              "OR " +
                              // start time is in the past, end time is after end time
                              $"({ChannelListingsStartTimeColumnName} < '{utcStartTime}' AND " +
                              $"{ChannelListingsEndTimeColumnName} > '{utcEndTime}')";

        await using var reader = command.ExecuteReader();

        var results = new List<Listing>();

        var channelIdColumnIndex = reader.GetOrdinal(ChannelListingsChannelIdColumnName);
        var titleColumnIndex = reader.GetOrdinal(ChannelListingsTitleColumnName);
        var blockColumnIndex = reader.GetOrdinal(ChannelListingsBlockColumnName);
        var descriptionColumnIndex = reader.GetOrdinal(ChannelListingsDescriptionColumnName);
        var startTimeColumnIndex = reader.GetOrdinal(ChannelListingsStartTimeColumnName);
        var endTimeColumnIndex = reader.GetOrdinal(ChannelListingsEndTimeColumnName);

        while (await reader.ReadAsync())
        {
            results.Add(new Listing
            {
                ChannelId = reader.GetString(channelIdColumnIndex),
                Title = reader.GetString(titleColumnIndex),
                Block = reader.GetInt32(blockColumnIndex),
                Description = reader.GetString(descriptionColumnIndex),
                StartTime = DateTime.Parse(reader.GetString(startTimeColumnIndex)),
                EndTime = DateTime.Parse(reader.GetString(endTimeColumnIndex))
            });
        }

        return results;
    }
}
