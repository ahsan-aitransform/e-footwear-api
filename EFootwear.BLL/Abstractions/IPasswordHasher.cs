namespace EFootwear.BLL.Abstractions;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string encodedHash);

    /// <summary>Burns the same CPU as a real check, so unknown users cannot be told apart by timing.</summary>
    void BurnTime(string password);
}
