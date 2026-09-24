namespace Resilience.Shared;

/// <summary>Examples register several named HttpClients with different resilience setups; this picks one by name.</summary>
public sealed class FlakyApiClientFactory(IHttpClientFactory factory)
{
    public FlakyApiClient Create(string clientName) => new(factory.CreateClient(clientName));
}
