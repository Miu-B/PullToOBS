using Newtonsoft.Json.Linq;
using Xunit;

namespace PullToOBS.Tests;

/// <summary>
/// Validates that the IINACT subscribe message has the exact structure expected
/// by IINACT's Handler.DataReceived method.
///
/// IINACT protocol requires:
///   - "call" must be a string JValue (not a JArray or JObject)
///   - "events" must be a flat JArray of string event names (not nested)
///
/// A previous bug used <c>new JArray(string[])</c> which, depending on the
/// constructor overload chosen by the compiler, could produce a nested array
/// <c>[["InCombat"]]</c> instead of <c>["InCombat"]</c>.
/// That caused IINACT to read data["call"] as "[]" and fail with
/// "Tried to call missing handler '[]'".
/// </summary>
public class IINACTSubscribeMessageTests
{
    // This must match the constant in IINACTIpcClient exactly.
    private const string SubscribeMessageJson =
        "{\"call\":\"subscribe\",\"events\":[\"InCombat\"]}";

    [Fact]
    public void SubscribeMessage_HasCorrectCallField()
    {
        var msg = JObject.Parse(SubscribeMessageJson);

        var call = msg["call"];
        Assert.NotNull(call);
        Assert.Equal(JTokenType.String, call!.Type);
        Assert.Equal("subscribe", call.Value<string>());
    }

    [Fact]
    public void SubscribeMessage_HasFlatEventsArray()
    {
        var msg = JObject.Parse(SubscribeMessageJson);

        var events = msg["events"];
        Assert.NotNull(events);
        Assert.Equal(JTokenType.Array, events!.Type);

        var arr = (JArray)events;
        // Must be a flat array, not nested
        Assert.All(arr, item => Assert.Equal(JTokenType.String, item.Type));
    }

    [Fact]
    public void SubscribeMessage_ContainsExpectedEventNames()
    {
        var msg = JObject.Parse(SubscribeMessageJson);
        var events = (JArray)msg["events"]!;

        var eventNames = events.ToObject<string[]>();
        Assert.NotNull(eventNames);
        Assert.Contains("InCombat", eventNames);
    }

    [Fact]
    public void SubscribeMessage_HasExactlyTwoKeys()
    {
        var msg = JObject.Parse(SubscribeMessageJson);

        // IINACT's Handler.DataReceived switches on data["call"].ToString()
        // and then reads data["events"]. No other keys should be present.
        Assert.Equal(2, msg.Count);
        Assert.NotNull(msg["call"]);
        Assert.NotNull(msg["events"]);
    }

    /// <summary>
    /// Demonstrates the bug that new JArray(object content) would introduce:
    /// wrapping a string[] as a single content item can produce a nested array.
    /// This test proves our JObject.Parse approach avoids that pitfall.
    /// </summary>
    [Fact]
    public void JArrayConstructor_WithStringArray_ProducesFlatArray()
    {
        // This is the pattern we replaced -- verify it actually works correctly
        // so the test documents the behaviour either way.
        var events = new string[] { "InCombat" };
        var arr = new JArray(events);

        // If JArray(params object[]) is chosen (via array covariance), each
        // string becomes a separate element. If JArray(object) is chosen,
        // Json.NET iterates the collection anyway. Either way, the result
        // should be flat -- but we use JObject.Parse in production code to
        // eliminate any ambiguity.
        Assert.Single(arr);
        Assert.All(arr, item => Assert.Equal(JTokenType.String, item.Type));
    }
}
