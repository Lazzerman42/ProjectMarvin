using Marvin.Common;

namespace ProjectMarvinAPI.Tests;

/// <summary>Parsing of the payloads simple devices send (<see cref="LogEntry.Load"/>).</summary>
public class LogEntryTests
{
  [Fact]
  public void Plain_text_becomes_the_message()
  {
    var entry = LogEntry.Load("Just text");

    Assert.Equal("Just text", entry.Message);
    Assert.Null(entry.Sender);
    Assert.Equal("Info", entry.LogType);
    Assert.Null(entry.LogDate);
  }

  [Fact]
  public void Json_is_parsed_case_insensitively()
  {
    var entry = LogEntry.Load("""{"sender":"S","LOGTYPE":"Warning","message":"M","logDate":"2024-01-02T03:04:05"}""");

    Assert.Equal("S", entry.Sender);
    Assert.Equal("Warning", entry.LogType);
    Assert.Equal("M", entry.Message);
    Assert.Equal(new DateTime(2024, 1, 2, 3, 4, 5), entry.LogDate);
  }

  [Fact]
  public void Message_and_sender_are_url_decoded_as_latin1()
  {
    var entry = LogEntry.Load("""{"Sender":"K%F6k","Message":"Hej+d%E5"}""");

    Assert.Equal("Kök", entry.Sender);
    Assert.Equal("Hej då", entry.Message);
  }

  [Fact]
  public void Plain_text_is_not_url_decoded()
  {
    // Only the fields of a JSON payload are decoded; the GET endpoint decodes the URL itself
    Assert.Equal("a+b%20c", LogEntry.Load("a+b%20c").Message);
  }

  [Fact]
  public void Json_without_message_gives_an_empty_message()
  {
    Assert.Equal("", LogEntry.Load("""{"Sender":"S"}""").Message);
  }

  [Theory]
  [InlineData("null")]
  [InlineData("{broken")]
  [InlineData("[1,2,3]")]
  [InlineData("42")]
  public void Input_that_is_not_a_json_object_becomes_the_message(string input)
  {
    Assert.Equal(input, LogEntry.Load(input).Message);
  }

  [Fact]
  public void Empty_input_gives_an_empty_message()
  {
    Assert.Equal("", LogEntry.Load("").Message);
  }

  [Fact]
  public void LogDateSE_formats_the_date()
  {
    var entry = new LogEntry("x") { LogDate = new DateTime(2024, 12, 24, 15, 0, 1) };

    Assert.Equal("2024-12-24 15:00:01", entry.LogDateSE);
    Assert.Equal("", new LogEntry().LogDateSE);
  }
}
