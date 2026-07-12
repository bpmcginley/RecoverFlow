using Microsoft.AspNetCore.DataProtection;
using RecoverFlow.Api.Auth;

namespace RecoverFlow.Tests.Integration;

public class MagicLinkTokenServiceTests
{
    private static MagicLinkTokenService NewService() =>
        new(new EphemeralDataProtectionProvider());

    [Fact]
    public void Mint_then_validate_round_trips_the_merchant_id()
    {
        var svc = NewService();
        var id = Guid.NewGuid();

        Assert.Equal(id, svc.Validate(svc.Mint(id)));
    }

    [Fact]
    public void Validate_rejects_a_tampered_token()
    {
        var svc = NewService();
        var token = svc.Mint(Guid.NewGuid());

        Assert.Null(svc.Validate(token + "x"));
    }

    [Fact]
    public void Validate_rejects_garbage()
    {
        Assert.Null(NewService().Validate("not-a-real-token"));
    }

    [Fact]
    public void Tokens_are_not_valid_under_a_different_key()
    {
        // A token minted by one provider must not validate under another — proves the
        // token is genuinely signed, not just base64.
        var token = NewService().Mint(Guid.NewGuid());

        Assert.Null(NewService().Validate(token));
    }
}
