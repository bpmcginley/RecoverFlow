namespace RecoverFlow.Application.Common;

public interface ITokenEncryptor
{
    string Encrypt(string plaintext);
    string Decrypt(string ciphertext);
}
