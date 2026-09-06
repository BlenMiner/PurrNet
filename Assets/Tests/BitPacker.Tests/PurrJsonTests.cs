using System.Collections.Generic;
using NUnit.Framework;
using PurrNet.Utils;

public class PurrJsonTests
{
    [Test]
    public void StringMapRoundTripsWithEscapes()
    {
        var map = new Dictionary<string, string>
        {
            ["projectId"] = "e76999dc45cbc633960721501de237fd",
            ["quote\"key"] = "line1\nline2 \\ tab\t  ünïcode",
            ["empty"] = ""
        };

        string json = PurrJson.WriteStringMap(map);
        var read = new Dictionary<string, string>();
        PurrJson.ReadStringMap(json, read);

        Assert.That(read, Is.EquivalentTo(map));
        Assert.That(json, Does.StartWith("{\n  \"projectId\": \"e76999dc45cbc633960721501de237fd\""));
    }

    [Test]
    public void StringMapReaderAcceptsForeignFormattingAndSkipsNulls()
    {
        var read = new Dictionary<string, string>();
        PurrJson.ReadStringMap("{ \"a\" : \"1\",\r\n\t\"b\":null, \"c\": 42 , \"d\":true}", read);

        Assert.That(read, Is.EquivalentTo(new Dictionary<string, string> { ["a"] = "1", ["c"] = "42", ["d"] = "true" }));
        Assert.That(PurrJson.WriteStringMap(new Dictionary<string, string>()), Is.EqualTo("{}"));

        read.Clear();
        PurrJson.ReadStringMap("", read);
        PurrJson.ReadStringMap("not json", read);
        Assert.That(read, Is.Empty);
    }

    [Test]
    public void SerializeWritesTelemetryShapedPayloads()
    {
        var payload = new Dictionary<string, object>
        {
            ["installation_id"] = "abc",
            ["metadata"] = new Dictionary<string, object>
            {
                ["player_count"] = 100,
                ["ratio"] = 0.5,
                ["transport"] = "UDP\"Transport",
                ["flag"] = true,
                ["nothing"] = null,
                ["tags"] = new List<object> { "a", 1 }
            }
        };

        Assert.That(PurrJson.Serialize(payload), Is.EqualTo(
            "{\"installation_id\":\"abc\",\"metadata\":{\"player_count\":100,\"ratio\":0.5,\"transport\":\"UDP\\\"Transport\",\"flag\":true,\"nothing\":null,\"tags\":[\"a\",1]}}"));
    }
}
