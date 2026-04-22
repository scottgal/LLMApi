using mostlylucid.mockllmapi.Packs;

namespace LLMApi.Tests;

public class PackTests
{
    [Fact]
    public void HoldeckPack_HasExpectedProperties()
    {
        var pack = new HoldeckPack
        {
            Id = "wordpress-rest",
            Name = "WordPress REST API",
            PromptPersonality = "You are a WordPress REST API.",
        };

        Assert.Equal("wordpress-rest", pack.Id);
        Assert.Equal("WordPress REST API", pack.Name);
        Assert.Equal("You are a WordPress REST API.", pack.PromptPersonality);
        Assert.Empty(pack.ApiSurface);
        Assert.Empty(pack.ResponseShapes);
        Assert.Empty(pack.JourneyPatterns);
    }
}
