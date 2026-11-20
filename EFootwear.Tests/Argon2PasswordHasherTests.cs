using EFootwear.DAL.Security;

namespace EFootwear.Tests;

public class Argon2PasswordHasherTests
{
    private readonly Argon2PasswordHasher _hasher = new();

    [Fact]
    public void Correct_password_verifies()
    {
        var hash = _hasher.Hash("Test@12345");
        Assert.True(_hasher.Verify("Test@12345", hash));
    }

    [Fact]
    public void Wrong_password_is_rejected()
    {
        var hash = _hasher.Hash("Test@12345");
        Assert.False(_hasher.Verify("test@12345", hash));
    }

    [Fact]
    public void Same_password_gives_different_hashes()
    {
        Assert.NotEqual(_hasher.Hash("same"), _hasher.Hash("same"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain-text-password")]
    [InlineData("$argon2id$v=19$m=0,t=0,p=0$AA==$AA==")]
    [InlineData("$argon2id$v=19$m=19456,t=2,p=1$not-base64$also-not")]
    public void Malformed_stored_hash_never_verifies(string stored)
    {
        Assert.False(_hasher.Verify("anything", stored));
    }

    [Fact]
    public void Hash_does_not_contain_the_password()
    {
        Assert.DoesNotContain("Test@12345", _hasher.Hash("Test@12345"));
    }
}
