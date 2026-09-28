namespace EProcure.Web.Services;

/// <summary>
/// Absolute links for emails. Uses "App:PublicBaseUrl" when configured (production: the real address, so a link
/// can never point at a host name an attacker put in the request); otherwise the current request's own address
/// (fine on a developer's laptop). Without either (background work, tests) the link stays relative.
/// </summary>
public interface ILinkBuilder
{
    string Absolute(string path);
}

public class LinkBuilder : ILinkBuilder
{
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _http;

    public LinkBuilder(IConfiguration configuration, IHttpContextAccessor http)
    {
        _configuration = configuration;
        _http = http;
    }

    public string Absolute(string path)
    {
        var configured = _configuration["App:PublicBaseUrl"];
        if (!string.IsNullOrWhiteSpace(configured)) return configured.TrimEnd('/') + path;

        var request = _http.HttpContext?.Request;
        return request is null ? path : $"{request.Scheme}://{request.Host}{request.PathBase}{path}";
    }
}
