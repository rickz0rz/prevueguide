using System;
using FluentAssertions;
using Xunit;

namespace Guide.Core.Tests;

public class TimeTests
{
    [Theory]
    [InlineData("2022-01-01T01:00:00.0000000Z", "2022-01-01T01:00:00.0000000Z")]
    [InlineData("2022-01-01T01:00:00.0000001Z", "2022-01-01T01:00:00.0000000Z")]
    [InlineData("2022-01-01T01:29:59.9999999Z", "2022-01-01T01:00:00.0000000Z")]
    [InlineData("2022-01-01T01:30:00.0000000Z", "2022-01-01T01:30:00.0000000Z")]
    [InlineData("2022-01-01T01:30:00.0000001Z", "2022-01-01T01:30:00.0000000Z")]
    public void TestClamping(string inputDateTimeString, string expectedDateTimeString)
    {
        var inputDateTime = DateTime.Parse(inputDateTimeString);
        var expectedDateTime = DateTime.Parse(expectedDateTimeString);

        var clamped = Utilities.Time.ClampToPreviousHalfHour(inputDateTime);
        clamped.Should().Be(expectedDateTime);
    }

    [Theory]
    [InlineData("2022-01-01T00:00:00.0000000Z", 911664)]
    [InlineData("2022-01-01T00:14:59.9999999Z", 911664)]
    [InlineData("2022-01-01T00:15:00.0000000Z", 911665)]
    [InlineData("2022-01-01T00:30:00.0000000Z", 911665)]
    [InlineData("2022-01-01T00:45:00.0000000Z", 911666)]
    [InlineData("2022-01-01T01:00:00.0000000Z", 911666)]
    [InlineData("2022-01-01T23:44:59.9999999Z", 911711)]
    [InlineData("2022-01-01T23:45:00.0000000Z", 911712)]
    [InlineData("2022-01-01T23:59:59.9999999Z", 911712)]
    [InlineData("2022-01-02T00:00:00.0000000Z", 911712)]
    public void TestBlockNumberCalculation(string input, int expectedBlockNumber)
    {
        var inputDateTime = DateTime.Parse(input).ToUniversalTime();
        var calculatedBlockNumber = Utilities.Time.CalculateBlockNumber(inputDateTime);
        calculatedBlockNumber.Should().Be(expectedBlockNumber);
    }

}
