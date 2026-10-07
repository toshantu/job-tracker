using System.Text.Json;
using JobTracker.Api.Ai;
using JobTracker.Api.Dtos;
using Xunit;

namespace JobTracker.Api.Tests;

public class ModelOutputParserTests
{
    [Fact]
    public void Parses_valid_output()
    {
        const string json = """{"keywords":[{"keyword":"C#","importance":"required","inCv":true},{"keyword":"GraphQL","importance":"preferred","inCv":false}]}""";

        Assert.True(ModelOutputParser.TryParse(json, out var keywords));

        Assert.Equal(2, keywords.Count);
        Assert.Equal(new KeywordResult("C#", KeywordImportance.Required, true), keywords[0]);
        Assert.Equal(new KeywordResult("GraphQL", KeywordImportance.Preferred, false), keywords[1]);
    }

    [Fact]
    public void Ignores_unknown_fields()
    {
        const string json = """{"matchScore":99,"keywords":[{"keyword":"Docker","importance":"required","inCv":true,"note":"ignore me","score":5}],"extra":{"a":1}}""";

        Assert.True(ModelOutputParser.TryParse(json, out var keywords));

        Assert.Equal(new KeywordResult("Docker", KeywordImportance.Required, true), Assert.Single(keywords));
    }

    [Fact]
    public void Removes_duplicate_keywords_ignoring_case()
    {
        const string json = """{"keywords":[{"keyword":"PostgreSQL","importance":"required","inCv":true},{"keyword":"postgresql","importance":"preferred","inCv":false},{"keyword":" POSTGRESQL ","importance":"required","inCv":false}]}""";

        Assert.True(ModelOutputParser.TryParse(json, out var keywords));

        Assert.Equal(new KeywordResult("PostgreSQL", KeywordImportance.Required, true), Assert.Single(keywords));
    }

    [Fact]
    public void Drops_items_with_invalid_fields()
    {
        var json = JsonSerializer.Serialize(new
        {
            keywords = new object[]
            {
                new { keyword = "A", importance = "must", inCv = true },
                new { keyword = "B", importance = "required" },
                new { keyword = "C", importance = "required", inCv = "yes" },
                new { keyword = "   ", importance = "required", inCv = true },
                new { keyword = new string('x', 61), importance = "required", inCv = true },
                new { keyword = 42, importance = "required", inCv = true },
                new { keyword = "Valid", importance = "Required", inCv = false }
            }
        });

        Assert.True(ModelOutputParser.TryParse(json, out var keywords));

        Assert.Equal(new KeywordResult("Valid", KeywordImportance.Required, false), Assert.Single(keywords));
    }

    [Fact]
    public void Cleans_whitespace_and_control_characters()
    {
        const string json = """{"keywords":[{"keyword":"  Entity \n  Framework\tCore\u0000 ","importance":"required","inCv":true},{"keyword":"Ja\u200Bva","importance":"preferred","inCv":false}]}""";

        Assert.True(ModelOutputParser.TryParse(json, out var keywords));

        Assert.Equal(2, keywords.Count);
        Assert.Equal("Entity Framework Core", keywords[0].Keyword);
        Assert.Equal("Java", keywords[1].Keyword);
    }

    [Fact]
    public void Stops_at_the_keyword_cap()
    {
        var items = Enumerable.Range(1, 40)
            .Select(i => new { keyword = $"Keyword {i}", importance = "required", inCv = i % 2 == 0 })
            .ToArray();
        var json = JsonSerializer.Serialize(new { keywords = items });

        Assert.True(ModelOutputParser.TryParse(json, out var keywords));

        Assert.Equal(KeywordGapPrompt.MaxKeywords, keywords.Count);
        Assert.Equal("Keyword 1", keywords[0].Keyword);
    }

    [Fact]
    public void Accepts_an_empty_list()
    {
        Assert.True(ModelOutputParser.TryParse("""{"keywords":[]}""", out var keywords));

        Assert.Empty(keywords);
    }

    [Theory]
    [InlineData("")]
    [InlineData("this is not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"keywords\":\"x\"}")]
    public void Rejects_unusable_json(string json)
    {
        Assert.False(ModelOutputParser.TryParse(json, out var keywords));

        Assert.Empty(keywords);
    }
}
